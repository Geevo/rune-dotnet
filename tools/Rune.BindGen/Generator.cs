using System.Text;
using System.Text.Json;

namespace Rune.BindGen;

/// <summary>Walks the public API, classifies types, and emits both sides of each binding.</summary>
sealed partial class Generator(List<Doc> docs)
{
    // Copy values too rich for a C struct (they hold enums with data); Rust keeps one leaked copy
    // of each distinct value and C# holds a pointer.
    static readonly HashSet<string> InternedTypes = ["TextStyle"];
    // Written by hand in the shim or wrapper.
    static readonly HashSet<string> Denied = [.. File.Exists(DenyFile) ? File.ReadAllLines(DenyFile).Select(l => l.Split('#')[0].Trim()).Where(l => l != "") : []];
    const string DenyFile = "tools/Rune.BindGen/deny.txt";

    readonly Dictionary<string, TypeInfo> _types = [];
    readonly Dictionary<string, (Doc Doc, JsonElement Item, string RustPath, string Module)> _functions = [];
    readonly Dictionary<string, (Doc Doc, JsonElement Item, string RustPath, string Module)> _constants = [];
    readonly HashSet<string> _visited = [];

    public string Rust = "", CSharp = "", Report = "", Summary = "";
    public IEnumerable<string> Skipped => _skipped.Select(s => $"{s.Api}: {s.Reason}");

    public void Run()
    {
        var main = docs[0];
        Walk(main, main.Item(main.Root)!.Value, main.Crate, "");
        foreach (var other in docs.Skip(1))
        {
            Walk(other, other.Item(other.Root)!.Value, other.Crate, other.Crate.Replace("rune_", ""));
        }
        Instantiate();
        NameTypes();

        Emit();
    }

    Doc? DocOf(string crate) => docs.FirstOrDefault(d => d.Crate == crate);

    (Doc, JsonElement)? Resolve(Doc doc, int id)
    {
        if (doc.Item(id) is { } local)
        {
            return (doc, local);
        }
        var canon = doc.Canon(id);
        if (canon is null)
        {
            return null;
        }
        var other = DocOf(canon.Split("::")[0]);
        if (other is null)
        {
            return null;
        }
        foreach (var p in other.Paths.EnumerateObject())
        {
            if (other.Canon(int.Parse(p.Name)) == canon && other.Item(int.Parse(p.Name)) is { } found)
            {
                return (other, found);
            }
        }
        return null;
    }

    static bool Hidden(JsonElement item) => item.GetProperty("attrs").GetRawText().Contains("doc(hidden)");

    void Walk(Doc doc, JsonElement module, string path, string csModule)
    {
        foreach (var idEl in module.GetProperty("inner").GetProperty("module").GetProperty("items").EnumerateArray())
        {
            if (doc.Item(idEl.GetInt32()) is not { } item || Hidden(item))
            {
                continue;
            }
            Visit(doc, item, item.GetProperty("name").GetString(), path, csModule);
        }
    }

    void Visit(Doc doc, JsonElement item, string? name, string path, string csModule)
    {
        var inner = item.GetProperty("inner");
        if (inner.TryGetProperty("use", out var use))
        {
            if (use.GetProperty("id").ValueKind != JsonValueKind.Number)
            {
                return;
            }
            if (Resolve(doc, use.GetProperty("id").GetInt32()) is not var (tdoc, target))
            {
                return;
            }
            if (use.GetProperty("is_glob").GetBoolean())
            {
                if (target.GetProperty("inner").TryGetProperty("module", out _))
                {
                    Walk(tdoc, target, path, csModule);
                }
                return;
            }
            Visit(tdoc, target, use.GetProperty("name").GetString(), path, csModule);
        }
        else if (inner.TryGetProperty("module", out _))
        {
            var key = $"{path}::{name}";
            if (_visited.Add(key))
            {
                Walk(doc, item, key, csModule == "" ? name! : $"{csModule}_{name}");
            }
        }
        else if (inner.TryGetProperty("struct", out _) || inner.TryGetProperty("enum", out _))
        {
            var id = item.GetProperty("id").GetInt32();
            var canon = doc.Canon(id);
            if (canon is null)
            {
                return;
            }
            var rust = $"{path}::{name}";
            if (!_types.TryGetValue(canon, out var t))
            {
                _types[canon] = new TypeInfo(canon, doc, item, rust);
            }
            else if (rust.Length < t.RustPath.Length)
            {
                t.RustPath = rust;
            }
        }
        else if (inner.TryGetProperty("function", out _))
        {
            Keep(_functions, doc, item, $"{path}::{name}", csModule);
        }
        else if (inner.TryGetProperty("constant", out _))
        {
            Keep(_constants, doc, item, $"{path}::{name}", csModule);
        }
    }

    static void Keep(Dictionary<string, (Doc, JsonElement, string, string)> items, Doc doc, JsonElement item, string path, string module)
    {
        var key = $"{doc.Crate}:{item.GetProperty("id").GetInt32()}";
        if (!items.TryGetValue(key, out var old) || path.Length < old.Item3.Length)
        {
            items[key] = (doc, item, path, module);
        }
    }

    void NameTypes()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in _types.Values.Where(t => t.Args.All(a => a.Key is HostKey or ManagedKey)).OrderBy(t => t.RustPath.Length).ThenBy(t => t.RustPath, StringComparer.Ordinal))
        {
            var parts = t.RustPath.Split("::");
            var name = parts[^1];
            for (int i = parts.Length - 2; !used.Add(name) && i >= 0; i--)
            {
                name = Names.Pascal(parts[i]) + name;
            }
            t.CsName = name;
        }
        foreach (var t in _types.Values.Where(t => t.CsName == "").OrderBy(t => t.Canon, StringComparer.Ordinal))
        {
            var name = t.Short + string.Concat(t.Args.Select(a => _types.TryGetValue(a.Key, out var arg) ? arg.CsName : KeyName(a.Key)));
            while (!used.Add(name))
            {
                name += "_";
            }
            t.CsName = name;
        }
    }

    Kind Classify(TypeInfo t)
    {
        if (t.Kind is Kind.InProgress)
        {
            return Kind.Unsupported;
        }
        if (t.Kind is not Kind.Unclassified)
        {
            return t.Kind;
        }
        t.Kind = Kind.InProgress;
        var kind = ClassifyNow(t);
        if (kind == Kind.Unsupported && (t.Instantiated || !IsGeneric(t)))
        {
            kind = Kind.Handle;
        }
        return t.Kind = kind;
    }

    static bool IsGeneric(TypeInfo t)
    {
        var inner = t.Item.GetProperty("inner");
        var data = inner.TryGetProperty("struct", out var s) ? s : inner.GetProperty("enum");
        return data.GetProperty("generics").GetProperty("params").GetArrayLength() != 0;
    }

    Kind ClassifyNow(TypeInfo t)
    {
        if (t.Canon == "rune::el::El")
        {
            return Kind.Element;
        }
        if (InternedTypes.Contains(t.Short))
        {
            return Kind.Interned;
        }
        var inner = t.Item.GetProperty("inner");
        var nonExhaustive = t.Item.GetProperty("attrs").GetRawText().Contains("non_exhaustive");
        if (inner.TryGetProperty("enum", out var en))
        {
            if (en.GetProperty("generics").GetProperty("params").GetArrayLength() != 0)
            {
                return Kind.Unsupported;
            }
            foreach (var v in en.GetProperty("variants").EnumerateArray())
            {
                var item = t.Doc.Item(v.GetInt32())!.Value;
                var kind = item.GetProperty("inner").GetProperty("variant").GetProperty("kind");
                if (kind.ValueKind != JsonValueKind.String || kind.GetString() != "plain" || item.GetProperty("attrs").GetRawText().Contains("non_exhaustive"))
                {
                    return Kind.Unsupported;
                }
                var name = item.GetProperty("name").GetString()!;
                t.Variants.Add((name, name));
            }
            return t.Variants.Count > 0 ? Kind.Enum : Kind.Unsupported;
        }
        var st = inner.GetProperty("struct");
        if (nonExhaustive || st.GetProperty("generics").GetProperty("params").GetArrayLength() != 0)
        {
            return Kind.Unsupported;
        }
        if (st.GetProperty("kind").ValueKind != JsonValueKind.Object || !st.GetProperty("kind").TryGetProperty("plain", out var plain))
        {
            return Kind.Unsupported;
        }
        if (plain.GetProperty("has_stripped_fields").GetBoolean() || plain.GetProperty("fields").GetArrayLength() == 0)
        {
            return Kind.Unsupported;
        }
        var fields = new List<(string, string, Ty)>();
        try
        {
            foreach (var f in plain.GetProperty("fields").EnumerateArray())
            {
                var item = t.Doc.Item(f.GetInt32())!.Value;
                var ty = Map(t.Doc, item.GetProperty("inner").GetProperty("struct_field"), new Ctx(null, null));
                if (ty is not (Prim or Value { T.Kind: Kind.Record or Kind.Enum }))
                {
                    return Kind.Unsupported;
                }
                var name = item.GetProperty("name").GetString()!;
                fields.Add((name, Names.Member(name, t.Short), ty));
            }
        }
        catch (Skip)
        {
            return Kind.Unsupported;
        }
        t.Fields = fields;
        return Kind.Record;
    }

    /// <summary>The generics in scope for one signature, and the type `Self` means.</summary>
    sealed record Ctx(JsonElement? Generics, TypeInfo? Self, Dictionary<string, string>? Subst = null);

    Ty Map(Doc doc, JsonElement t, Ctx ctx, bool inClosure = false)
    {
        if (t.TryGetProperty("primitive", out var p))
        {
            return Primitive(p.GetString()!);
        }
        if (t.TryGetProperty("borrowed_ref", out var r))
        {
            return MapRef(doc, r, ctx, inClosure);
        }
        if (t.TryGetProperty("resolved_path", out var rp))
        {
            return MapPath(doc, rp, ctx, inClosure);
        }
        if (t.TryGetProperty("tuple", out var tu))
        {
            var items = tu.EnumerateArray().Select(i => Map(doc, i, ctx, inClosure)).ToList();
            if (items.Count == 0)
            {
                throw new Skip("unit value");
            }
            return Flatten(items, false);
        }
        if (t.TryGetProperty("array", out var ar))
        {
            if (!int.TryParse(ar.GetProperty("len").GetString(), out var len) || len < 2)
            {
                throw new Skip("array length");
            }
            var item = Map(doc, ar.GetProperty("type"), ctx, inClosure);
            return len > 7 ? LongArray(item, len) : Flatten(Enumerable.Repeat(item, len).ToList(), true);
        }
        if (t.TryGetProperty("impl_trait", out var bounds))
        {
            return MapBounds(doc, bounds, ctx, inClosure);
        }
        if (t.TryGetProperty("generic", out var g))
        {
            var name = g.GetString()!;
            if (name == "Self" && ctx.Self is { } self)
            {
                return FromType(self, inClosure);
            }
            if (KeyOf(ctx, name) is { } key)
            {
                return TyOf(key, false, false, inClosure);
            }
            if (ctx.Generics is { } gens)
            {
                foreach (var param in gens.GetProperty("params").EnumerateArray())
                {
                    if (param.GetProperty("name").GetString() != name || !param.GetProperty("kind").TryGetProperty("type", out var kind))
                    {
                        continue;
                    }
                    return MapBounds(doc, kind.GetProperty("bounds"), ctx, inClosure);
                }
            }
            throw new Skip("bare generic parameter");
        }
        if (t.TryGetProperty("slice", out _))
        {
            throw new Skip("unsized slice");
        }
        if (t.TryGetProperty("dyn_trait", out _))
        {
            throw new Skip("dyn trait");
        }
        if (t.TryGetProperty("function_pointer", out var fp))
        {
            var sig = fp.GetProperty("sig");
            var inputs = sig.GetProperty("inputs").EnumerateArray().Select(a => a[1]).ToList();
            return new FnPtr(ClosureOf(doc, inputs, sig.GetProperty("output"), ctx));
        }
        throw new Skip("unusual type");
    }

    static Ty Flatten(List<Ty> items, bool array)
    {
        if (items.Any(i => i is FnPtr))
        {
            throw new Skip("tuple of fn pointers");
        }
        if (items.Any(i => i is not (Prim or Value)))
        {
            return new Wire(new Flat(items, array));
        }
        return new Flat(items, array);
    }

    static Ty Primitive(string name) => name switch
    {
        "bool" => new Prim("bool", "bool"),
        "f32" => new Prim("f32", "float"),
        "f64" => new Prim("f64", "double"),
        "u8" => new Prim("u8", "byte"),
        "i8" => new Prim("i8", "sbyte"),
        "u16" => new Prim("u16", "ushort"),
        "i16" => new Prim("i16", "short"),
        "u32" => new Prim("u32", "uint"),
        "i32" => new Prim("i32", "int"),
        "u64" => new Prim("u64", "ulong"),
        "i64" => new Prim("i64", "long"),
        "char" => new Prim("char", "global::System.Text.Rune"),
        "usize" => new Prim("usize", "nuint"),
        "isize" => new Prim("isize", "nint"),
        _ => throw new Skip($"primitive {name}"),
    };

    Ty MapRef(Doc doc, JsonElement r, Ctx ctx, bool inClosure)
    {
        var target = r.GetProperty("type");
        var mutable = r.GetProperty("is_mutable").GetBoolean();
        if (target.TryGetProperty("primitive", out var p) && p.GetString() == "str")
        {
            return new Str(r.GetProperty("lifetime").ValueKind == JsonValueKind.String && r.GetProperty("lifetime").GetString() == "'static" ? StrMode.Static : StrMode.Borrowed);
        }
        if (target.TryGetProperty("generic", out var bound) && KeyOf(ctx, bound.GetString()!) is { } refKey)
        {
            return TyOf(refKey, true, mutable, inClosure);
        }
        if (target.TryGetProperty("generic", out var g) && g.GetString() == "S")
        {
            return inClosure ? new State(mutable) : throw new Skip("bare generic parameter");
        }
        if (target.TryGetProperty("generic", out var self) && self.GetString() == "Self" && ctx.Self is { } st)
        {
            return RefOf(st, mutable, inClosure);
        }
        if (target.TryGetProperty("resolved_path", out var rp))
        {
            var name = rp.GetProperty("path").GetString()!.Split("::")[^1];
            if (name == "ManagedFn")
            {
                return new Managed(true);
            }
            if (Lookup(doc, rp, ctx) is { } t)
            {
                return RefOf(t, mutable, inClosure);
            }
            if (mutable && name is "Path" or "String")
            {
                throw new Skip("mutable string");
            }
            if (ExternalType(doc, name) is { } external)
            {
                return new Handle(external, mutable);
            }
            if (name is not ("Path" or "String") && ShapeRef(doc, target, rp, name, mutable, ctx, inClosure) is { } shaped)
            {
                return shaped;
            }
            if (name == "Path")
            {
                return new Str(StrMode.PathRef);
            }
            if (name == "String")
            {
                return new Str(StrMode.Borrowed);
            }
            throw new Skip($"type {name}");
        }
        if (target.TryGetProperty("slice", out var slice))
        {
            if (mutable)
            {
                throw new Skip("mutable slice");
            }
            var isStatic = r.GetProperty("lifetime").ValueKind == JsonValueKind.String && r.GetProperty("lifetime").GetString() == "'static";
            return MakeSeq(Map(doc, slice, ctx, inClosure), isStatic ? SeqMode.StaticSlice : SeqMode.Slice);
        }
        var inner = Map(doc, target, ctx, inClosure);
        if (mutable)
        {
            throw new Skip("reference to composite");
        }
        return inner is Prim or Value ? new Ref(inner) : new Wire(new Ref(inner));
    }

    Ty RefOf(TypeInfo t, bool mutable, bool inClosure)
    {
        var kind = Classify(t);
        if (kind == Kind.Handle)
        {
            return new Handle(t, mutable);
        }
        if (kind is Kind.Record or Kind.Enum or Kind.Interned)
        {
            return mutable ? throw new Skip("&mut value") : new Ref(new Value(t));
        }
        throw new Skip($"generic type {t.Short}");
    }

    Ty FromType(TypeInfo t, bool inClosure) => Classify(t) switch
    {
        Kind.Element => new Element(),
        Kind.Record or Kind.Enum or Kind.Interned => new Value(t),
        Kind.Handle => new Obj(t),
        _ => throw new Skip($"generic type {t.Short}"),
    };

    Ty MapPath(Doc doc, JsonElement rp, Ctx ctx, bool inClosure)
    {
        var name = rp.GetProperty("path").GetString()!.Split("::")[^1];
        switch (name)
        {
            case "String":
                return new Str(StrMode.Owned);
            case "ManagedFn":
                return new Managed(false);
            case "PathBuf":
                return new Str(StrMode.PathBuf);
            case "Waker":
                return new Obj(new TypeInfo("Waker", doc, default, "rune::Waker") { CsName = "Waker", Kind = Kind.Handle });
            case "Option":
                var inner = Map(doc, FirstArg(rp), ctx, inClosure);
                if (inner is FnPtr)
                {
                    throw new Skip("optional fn pointer");
                }
                if (inner is not (Prim or Value or Str or Flat or Obj or Managed))
                {
                    return new Wire(new Opt(inner));
                }
                return new Opt(inner);
            case "Vec":
                return MakeSeq(Map(doc, FirstArg(rp), ctx, inClosure), SeqMode.Vec);
            // A state machine's effects (`ComboboxState::step`) reach C# as an array of them. `Effects<ManagedFn>`
            // is the type itself, for C# machines.
            case "Effects" when Map(doc, FirstArg(rp), ctx, inClosure) is not Managed and var effect:
                var effects = $"rune::machine::Effects<{RustT(effect)}>";
                return new Wire(new Adapt(new Seq(effect, SeqMode.Vec), effects, Wire.Data(effect) + "[]",
                    x => $"{{ let mut e_ = {effects}::none(); e_.extend({x}); e_ }}",
                    (v, r) => ($"{(r ? $"{v}.clone()" : v)}.into_iter().collect::<Vec<_>>()", false), c => c, c => c));
            // What's left of a list being iterated (`EffectsIter::Many`).
            case "IntoIter" when Map(doc, FirstArg(rp), ctx, inClosure) is var left:
                return new Wire(new Adapt(new Seq(left, SeqMode.Vec), $"std::vec::IntoIter<{RustT(left)}>", Wire.Data(left) + "[]",
                    x => $"{x}.into_iter()", (v, r) => r ? ($"{v}.as_slice()", true) : ($"{v}.collect::<Vec<_>>()", false), c => c, c => c));
        }
        if (Lookup(doc, rp, ctx) is { } t)
        {
            return FromType(t, inClosure);
        }
        if (ShapePath(doc, rp, name, ctx, inClosure) is { } shaped)
        {
            return shaped;
        }
        if (ExternalType(doc, name) is { } external)
        {
            return new Obj(external);
        }
        throw new Skip($"type {name}");
    }

    static Ty MakeSeq(Ty item, SeqMode mode) =>
        item is Flat { Items: var fi } && fi.Any(i => i is FnPtr) ? throw new Skip("sequence of fn pointers") : item is Prim or Value or Element or Obj or FnPtr ? new Seq(item, mode) : new Wire(new Seq(item, mode));

    static JsonElement FirstArg(JsonElement rp) =>
        TypeArgs(rp).First();

    Closure ClosureOf(Doc doc, List<JsonElement> inputs, JsonElement output, Ctx ctx)
    {
        var args = inputs.Select(a => Map(doc, a, ctx, true)).ToList();
        if (args.Any(a => a is Closure or FnPtr))
        {
            throw new Skip("closure argument shape");
        }
        args = args.Select(a => a is Flat or Opt or Ref or Seq ? new Wire(a) : a).ToList();
        Ty? ret = output.ValueKind == JsonValueKind.Null ? null : Map(doc, output, ctx, true);
        if (ret is Wire { Inner: Handle wired })
        {
            ret = wired;
        }
        // `Fn(&mut S) -> &mut T` and `Fn(&S) -> &T`: the borrow points into state the managed side already holds.
        bool projection = ret is Handle hr && args is [State st] && hr.Mut == st.Mut;
        // `fn(&T) -> &str`: Rust can't borrow from a managed string, so each distinct string is interned.
        if (ret is Str { Mode: StrMode.Borrowed })
        {
            ret = new Wire(new Str(StrMode.Static));
        }
        else if (ret is not (null or Prim or Value or Element or Obj or Managed or HostVal) && !projection)
        {
            ret = !Borrows(ret) && ret is not (Closure or FnPtr) ? ret as Wire ?? new Wire(ret) : throw new Skip("closure return shape");
        }
        return new Closure(args, ret);
    }

    Ty MapBounds(Doc doc, JsonElement bounds, Ctx ctx, bool inClosure)
    {
        foreach (var b in bounds.EnumerateArray())
        {
            if (!b.TryGetProperty("trait_bound", out var tb))
            {
                continue;
            }
            var tr = tb.GetProperty("trait");
            var name = tr.GetProperty("path").GetString()!.Split("::")[^1];
            switch (name)
            {
                case "Fn" or "FnMut" or "FnOnce":
                    var par = tr.GetProperty("args").GetProperty("parenthesized");
                    return ClosureOf(doc, [.. par.GetProperty("inputs").EnumerateArray()], par.GetProperty("output"), ctx);
                case "Display":
                    return new Str(StrMode.Owned, Shown: true);
                case "Hash":
                    return new Hashed();
                case "Into":
                    var into = Map(doc, tr.GetProperty("args").GetProperty("angle_bracketed").GetProperty("args")[0].GetProperty("type"), ctx, inClosure);
                    return into is Str ? new Str(StrMode.Owned) : into is Wire { Inner: Adapt { Repr: Seq or Str } shim } ? shim.Repr : into;
                case "AsRef":
                    var asRef = tr.GetProperty("args").GetProperty("angle_bracketed").GetProperty("args")[0].GetProperty("type");
                    if (asRef.TryGetProperty("primitive", out var s) && s.GetString() == "str")
                    {
                        return new Str(StrMode.Borrowed);
                    }
                    if (asRef.TryGetProperty("resolved_path", out var ap) && ap.GetProperty("path").GetString()!.EndsWith("Path"))
                    {
                        return new Str(StrMode.PathRef);
                    }
                    if (asRef.TryGetProperty("slice", out var refSlice))
                    {
                        return MakeSeq(Map(doc, refSlice, ctx, inClosure), SeqMode.Vec);
                    }
                    throw new Skip("AsRef target");
                case "IntoIterator" or "Iterator":
                    foreach (var c in tr.GetProperty("args").GetProperty("angle_bracketed").GetProperty("constraints").EnumerateArray())
                    {
                        if (c.GetProperty("name").GetString() != "Item")
                        {
                            continue;
                        }
                        var binding = c.GetProperty("binding");
                        return MakeSeq(binding.TryGetProperty("equality", out var eq) ? Map(doc, eq.GetProperty("type"), ctx, inClosure) : MapBounds(doc, binding.GetProperty("constraint"), ctx, inClosure), SeqMode.Vec);
                    }
                    throw new Skip("iterator without Item");
                default:
                    throw new Skip($"trait bound {name}");
            }
        }
        throw new Skip("no usable bound");
    }
}
