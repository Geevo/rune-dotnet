using System.Text;
using System.Text.Json;

namespace Rune.BindGen;

/// <summary>Crosses the boundary as bytes (see native/rune-capi/src/wire.rs): anything with no flat C form.</summary>
sealed record Wire(Ty Inner) : Ty
{
    public override string CsType => Data(Inner);

    /// <summary>The C# type of a value held in a composite, where spans can't go.</summary>
    public static string Data(Ty t) => t switch
    {
        Wire w => Data(w.Inner),
        Ref r => Data(r.Inner),
        Seq s => Data(s.Item) + "[]",
        Flat f => "(" + string.Join(", ", f.Items.Select(Data)) + ")",
        Opt o when Data(o.Inner).EndsWith('?') => throw new Skip("Option of Option"),
        Opt o => Data(o.Inner) + "?",
        Adapt a => a.Cs,
        Res r => r.Ok is null ? "void" : Data(r.Ok),
        _ => t.CsType,
    };
}

/// <summary>A std type carried as a simpler one. `From` takes the value and whether it is a reference,
/// and gives the converted value and whether that is one.</summary>
sealed record Adapt(Ty Repr, string Rust, string Cs, Func<string, string> To, Func<string, bool, (string, bool)> From, Func<string, string> CsTo, Func<string, string> CsFrom) : Ty
{
    public override string CsType => Cs;
}

/// <summary>A returned `Result`: `Err` throws RuneException in C#.</summary>
sealed record Res(Ty? Ok) : Ty
{
    public override string CsType => Ok is null ? "void" : Wire.Data(Ok);
}

sealed partial class Generator
{
    // Object types with no rustdoc item: `Arc<T>` and `Rc<T>` of an object, and `&mut Vec<T>` out-lists.
    sealed record Synth(TypeInfo T, TypeInfo? Inner, Ty? Item) { public External? Ext { get; init; } }
    readonly Dictionary<string, Synth> _synth = [];
    int _tmp;

    /// <summary>What a function's return needs that the flat return paths don't do.</summary>
    static Ty Wired(Ty t) => t is Seq or Handle or Str { Mode: StrMode.Static } or Ref { Inner: not (Prim or Value) } ? new Wire(t) : t;

    static IEnumerable<JsonElement> TypeArgs(JsonElement rp)
    {
        if (rp.GetProperty("args").ValueKind != JsonValueKind.Object || !rp.GetProperty("args").TryGetProperty("angle_bracketed", out var ab))
        {
            yield break;
        }
        foreach (var a in ab.GetProperty("args").EnumerateArray())
        {
            if (a.TryGetProperty("type", out var t))
            {
                yield return t;
            }
        }
    }

    static bool IsStr(JsonElement t) => t.TryGetProperty("primitive", out var p) && p.GetString() == "str";

    Ty FromSeqLike(Ty item, string rust, string to, string? owned = null) => new Wire(new Adapt(new Seq(item, SeqMode.Vec), rust, Wire.Data(item) + "[]",
        x => to.Replace("$", x), (v, r) => (owned is null ? $"{(r ? "**" : "*")}{v}".Wrap(".to_vec()") : owned.Replace("$", v), false), c => c, c => c));

    Ty FromStrLike(string rust, string to) => new Wire(new Adapt(new Str(StrMode.Owned), rust, "string",
        x => to.Replace("$", x), (v, r) => ($"{v}.to_string()", false), c => c, c => c));

    /// <summary>The std types the signature mapper doesn't know by itself.</summary>
    Ty? ShapePath(Doc doc, JsonElement rp, string name, Ctx ctx, bool inClosure)
    {
        var args = TypeArgs(rp).ToList();
        switch (name)
        {
            case "Range" when args.Count == 1:
                var at = Map(doc, args[0], ctx, inClosure);
                if (at is not (Prim or Value))
                {
                    throw new Skip("type Range");
                }
                var flat = new Flat([at, at], false);
                return new Wire(new Adapt(flat, $"std::ops::Range<{RustT(at)}>", Wire.Data(flat),
                    x => $"{{ let (s_, e_) = {x}; s_..e_ }}", (v, _) => ($"({v}.start, {v}.end)", false), c => c, c => c));
            case "Duration":
                return new Wire(new Adapt(new Prim("f64", "double"), "std::time::Duration", "global::System.TimeSpan",
                    x => $"wire_dur({x})", (v, _) => ($"{v}.as_secs_f64()", false), c => $"{c}.TotalSeconds", c => $"global::System.TimeSpan.FromSeconds({c})"));
            case "Instant":
                return new Wire(new Adapt(new Prim("f64", "double"), "std::time::Instant", "global::System.TimeSpan",
                    x => $"wire_instant({x})", (v, r) => ($"wire_secs({(r ? "" : "&")}{v})", false), c => $"{c}.TotalSeconds", c => $"global::System.TimeSpan.FromSeconds({c})"));
            case "Ordering":
                return new Wire(new Adapt(new Prim("i32", "int"), "std::cmp::Ordering", "int",
                    x => $"{x}.cmp(&0)", (v, r) => ($"({(r ? "*" : "")}{v} as i32)", false), c => c, c => c));
            case "Arc" or "Rc" when args.Count == 1:
                var ptr = name == "Arc" ? "std::sync::Arc" : "std::rc::Rc";
                if (args[0].TryGetProperty("slice", out var sl))
                {
                    var it = Map(doc, sl, ctx, inClosure);
                    return FromSeqLike(it, $"{ptr}<[{RustT(it)}]>", "($).into()");
                }
                if (IsStr(args[0]))
                {
                    return FromStrLike($"{ptr}<str>", "($).into()");
                }
                var inner = Map(doc, args[0], ctx, inClosure);
                if (inner is Obj o)
                {
                    return new Obj(SharedType(doc, name, ptr, o.T));
                }
                if (inner is not (Prim or Value or Str or Wire or Seq or Flat or Opt))
                {
                    throw new Skip($"type {name}");
                }
                return new Wire(new Adapt(inner, $"{ptr}<{RustT(inner)}>", Wire.Data(inner),
                    x => $"{ptr}::new({x})", (v, r) => ($"&{(r ? "**" : "*")}{v}", true), c => c, c => c));
            case "Zeroizing":
                return FromStrLike("zeroize::Zeroizing<String>", "($).into()");
            case "Cow" when args.Count == 1:
                if (IsStr(args[0]))
                {
                    return FromStrLike("std::borrow::Cow<'_, str>", "($).into()");
                }
                if (args[0].TryGetProperty("slice", out var cs))
                {
                    var it = Map(doc, cs, ctx, inClosure);
                    return FromSeqLike(it, $"std::borrow::Cow<'_, [{RustT(it)}]>", "($).into()");
                }
                throw new Skip("type Cow");
            case "Box" when args.Count == 1:
                if (IsStr(args[0]))
                {
                    return FromStrLike("Box<str>", "($).into_boxed_str()");
                }
                if (args[0].TryGetProperty("slice", out var bs))
                {
                    var it = Map(doc, bs, ctx, inClosure);
                    return FromSeqLike(it, $"Box<[{RustT(it)}]>", "($).into_boxed_slice()");
                }
                var boxed = Map(doc, args[0], ctx, inClosure);
                if (boxed is Closure)
                {
                    throw new Skip("type Box");
                }
                return new Wire(new Adapt(boxed, $"Box<{RustT(boxed)}>", Wire.Data(boxed),
                    x => $"Box::new({x})", (v, r) => r ? ($"&**{v}", true) : ($"*{v}", false), c => c, c => c));
            case "Result":
                if (inClosure || args.Count == 0)
                {
                    throw new Skip("type Result");
                }
                var unit = args[0].TryGetProperty("tuple", out var tu) && tu.GetArrayLength() == 0;
                return new Wire(new Res(unit ? null : Map(doc, args[0], ctx, inClosure)));
        }
        if (Resolve(doc, rp.GetProperty("id").GetInt32()) is var (adoc, item) && item.GetProperty("inner").TryGetProperty("type_alias", out var alias)
            && alias.GetProperty("generics").GetProperty("params").GetArrayLength() == 0)
        {
            return Map(adoc, alias.GetProperty("type"), ctx, inClosure);
        }
        return null;
    }

    /// <summary>`&Vec<T>`, `&Option<T>` and the like, and `&mut Vec<T>` out-lists.</summary>
    Ty? ShapeRef(Doc doc, JsonElement target, JsonElement rp, string name, bool mutable, Ctx ctx, bool inClosure)
    {
        if (mutable)
        {
            if (name != "Vec")
            {
                return null;
            }
            return new Handle(VecType(doc, Map(doc, FirstArg(rp), ctx, inClosure)), true);
        }
        var inner = Map(doc, target, ctx, inClosure);
        return inner is Obj o ? new Handle(o.T, false) : new Wire(new Ref(inner));
    }

    Ty LongArray(Ty item, int len)
    {
        var rust = $"[{RustT(item)}; {len}]";
        return new Wire(new Adapt(new Seq(item, SeqMode.Vec), rust, Wire.Data(item) + "[]",
            x => $"<{rust}>::try_from({x}).unwrap_or_else(|_| panic!(\"wrong array length\"))", (v, _) => ($"{v}.to_vec()", false), c => c, c => c));
    }

    TypeInfo SharedType(Doc doc, string name, string ptr, TypeInfo inner)
    {
        var path = $"{ptr}<{inner.RustType}>";
        if (_synth.TryGetValue(path, out var s))
        {
            return s.T;
        }
        var t = new TypeInfo(path, doc, default, path) { CsName = name + inner.CsName, Kind = Kind.Handle };
        _synth[path] = new(t, inner, null);
        return t;
    }

    TypeInfo VecType(Doc doc, Ty item)
    {
        var label = item switch { Element => "El", Value v => v.T.CsName, Obj o => o.T.CsName, _ => throw new Skip("type Vec") };
        var path = $"Vec<{RustT(item)}>";
        if (_synth.TryGetValue(path, out var s))
        {
            return s.T;
        }
        var t = new TypeInfo(path, doc, default, path) { CsName = "Vec" + label, Kind = Kind.Handle };
        _synth[path] = new(t, null, item);
        return t;
    }

    void EmitSynth()
    {
        foreach (var (_, s) in _synth.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var t = s.T;
            var free = $"rune_gen_{t.Sym}_free";
            _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {free}(h: *mut c_void) {{ drop(unown::<{t.RustPath}>(h)); }}");
            _native.AppendLine($"    [DllImport(Lib)] public static extern void {free}(nint h);");
            AddMember(t.CsName, "Free", n => $"    /// <summary>Drops an object nothing consumed.</summary>\n    public void {n}() => N.{free}(O);");
            if (s.Ext is { } ext)
            {
                EmitExternal(t, ext);
                continue;
            }
            if (s.Inner is { } inner)
            {
                string get = $"rune_gen_{t.Sym}_get", make = $"rune_gen_{t.Sym}_new", clone = $"rune_gen_{t.Sym}_clone";
                var ptr = t.RustPath[..t.RustPath.IndexOf('<')];
                _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {get}(h: *mut c_void) -> *mut c_void {{ ptr_of(&**(h as *const {t.RustPath})) }}");
                _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {make}(h: *mut c_void) -> *mut c_void {{ own({ptr}::new(unown::<{inner.RustType}>(h))) }}");
                _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {clone}(h: *mut c_void) -> *mut c_void {{ own((*(h as *const {t.RustPath})).clone()) }}");
                foreach (var f in new[] { get, make, clone })
                {
                    _native.AppendLine($"    [DllImport(Lib)] public static extern nint {f}(nint h);");
                }
                AddMember(t.CsName, "Value", n => $"    /// <summary>The shared object, borrowed: valid while this handle is, and never to be freed or consumed.</summary>\n    public {inner.Cs} {n} => new(N.{get}(P), _s == 0 ? Interop.Lent : _s);");
                AddMember(t.CsName, "From", n => $"    public static {t.Cs} {n}({inner.Cs} value) => new(N.{make}(value.O));");
                AddMember(t.CsName, "Clone", n => $"    /// <summary>Another owner of the same object.</summary>\n    public {t.Cs} {n}() => new(N.{clone}(P));");
            }
            else
            {
                string make = $"rune_gen_{t.Sym}_new", count = $"rune_gen_{t.Sym}_count", take = $"rune_gen_{t.Sym}_take";
                var seq = new Seq(s.Item!, SeqMode.Vec);
                _rs.AppendLine($"#[unsafe(no_mangle)] pub extern \"C\" fn {make}() -> *mut c_void {{ own(<{t.RustPath}>::new()) }}");
                _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {count}(h: *mut c_void) -> usize {{ (*(h as *const {t.RustPath})).len() }}");
                _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {take}(h: *mut c_void) -> Utf8 {{ let v = std::mem::take(&mut *(h as *mut {t.RustPath})); {WireBody(seq, "v", false)} }}");
                _native.AppendLine($"    [DllImport(Lib)] public static extern nint {make}();");
                _native.AppendLine($"    [DllImport(Lib)] public static extern nuint {count}(nint h);");
                _native.AppendLine($"    [DllImport(Lib)] public static extern Utf8Ref {take}(nint h);");
                AddMember(t.CsName, "Create", n => $"    public static {t.Cs} {n}() => new(N.{make}());");
                AddMember(t.CsName, "Count", n => $"    public int {n} => (int)N.{count}(P);");
                AddMember(t.CsName, "Take", n => $"    /// <summary>Empties the list and hands over what it held.</summary>\n    public {Wire.Data(seq)} {n}() => {CsRead(seq, $"N.{take}(P)")};");
            }
        }
    }

    void WriteSynth(StringBuilder cs, HashSet<string> written)
    {
        foreach (var (_, s) in _synth.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var n = s.T.CsName;
            written.Add(n);
            cs.AppendLine(HandleHead(n));
            cs.AppendJoin('\n', _members.GetValueOrDefault(n) ?? []).AppendLine("\n}");
        }
    }

    // Rust

    /// <summary>The type as Rust source, for closure parameters.</summary>
    string RustT(Ty t) => t switch
    {
        Prim p => p.Rust,
        Str { Mode: StrMode.Owned } => "String",
        Str { Mode: StrMode.Borrowed } => "&str",
        Str { Mode: StrMode.Static } => "&'static str",
        Str { Mode: StrMode.PathBuf } => "std::path::PathBuf",
        Str { Mode: StrMode.PathRef } => "&std::path::Path",
        Value v => v.T.RustType,
        Obj o => o.T.RustType,
        Element => "El",
        Handle h => $"&{(h.Mut ? "mut " : "")}{h.T.RustType}",
        Ref r => "&" + RustT(r.Inner),
        Wire w => RustT(w.Inner),
        Adapt a => a.Rust,
        Opt o => $"Option<{RustT(o.Inner)}>",
        Seq { Mode: SeqMode.Vec } s => $"Vec<{RustT(s.Item)}>",
        Seq { Mode: SeqMode.Slice } s => $"&[{RustT(s.Item)}]",
        Seq s => $"&'static [{RustT(s.Item)}]",
        Flat { Array: true } f => $"[{RustT(f.Items[0])}; {f.Items.Count}]",
        Flat f => "(" + string.Concat(f.Items.Select(i => RustT(i) + ", ")) + ")",
        _ => throw new Skip("composite shape"),
    };

    static bool Borrows(Ty t) => t switch
    {
        Ref or Str { Mode: StrMode.Borrowed or StrMode.PathRef } or Seq { Mode: SeqMode.Slice } => true,
        Seq s => Borrows(s.Item),
        Wire w => Borrows(w.Inner),
        Opt o => Borrows(o.Inner),
        Flat f => f.Items.Any(Borrows),
        Adapt a => Borrows(a.Repr),
        _ => false,
    };

    string WireBody(Ty t, string v, bool byRef) => $"let mut w = WireOut::new(); {Enc(t, v, byRef)} w.ret()";

    /// <summary>Statements writing `v` to `w`. With `byRef`, `v` is a reference: objects read that way are borrowed.</summary>
    string Enc(Ty t, string v, bool byRef)
    {
        var a = $"e{_tmp++}";
        var val = byRef ? v : $"&{v}";
        switch (t)
        {
            case Wire x:
                return Enc(x.Inner, v, byRef);
            case Ref r:
                return Enc(r.Inner, byRef ? $"(*{v})" : v, true);
            case Prim:
                return $"w.raw({val});";
            case Str s:
                return s.Mode is StrMode.PathBuf or StrMode.PathRef ? $"w.str(&{v}.to_string_lossy());" : $"w.str({val});";
            case Value { T.Kind: Kind.Enum } e:
                return $"w.raw(&from_{e.T.Sym}({val}));";
            case Value { T.Kind: Kind.Record } r:
                return $"w.raw(&R_{r.T.Sym}::from({val}));";
            case Value i:
                return $"w.raw(&intern_{i.T.Sym}(({(byRef ? "*" : "")}{v}).clone()));";
            case Element:
                return byRef ? throw new Skip("borrowed element") : $"w.raw(&put({v}));";
            case Obj o:
                return byRef ? $"w.raw(&ptr_of({v}));" : $"w.raw(&own({v}));";
            case Handle:
                return $"w.raw(&ptr_of(&{(byRef ? "**" : "*")}{v}));";
            case Seq { Item: Prim } s when byRef || s.Mode != SeqMode.Vec:
                return $"w.slice(&{v}[..]);";
            case Seq { Mode: SeqMode.Vec, Item: Prim }:
                return $"{{ let {a}: Vec<_> = ({v}).into_iter().collect(); w.slice(&{a}[..]); }}";
            case Seq s when byRef || s.Mode != SeqMode.Vec:
                return $"w.len({v}.len()); for {a} in {v}.iter() {{ {Enc(s.Item, a, true)} }}";
            case Seq s:
                return $"{{ let {a}: Vec<_> = ({v}).into_iter().collect(); w.len({a}.len()); for {a}x in {a} {{ {Enc(s.Item, a + "x", false)} }} }}";
            case Flat f:
                var names = f.Items.Select((_, i) => $"{a}_{i}").ToList();
                var pattern = f.Array ? $"[{string.Join(", ", names)}]" : $"({string.Concat(names.Select(n => n + ", "))})";
                return $"{{ let {pattern} = {v}; {string.Join(" ", f.Items.Select((x, i) => Enc(x, names[i], byRef)))} }}";
            case Opt o:
                return $"match {v} {{ Some({a}) => {{ w.tag(true); {Enc(o.Inner, a, byRef)} }} None => w.tag(false) }}";
            case Adapt d:
                var (from, isRef) = d.From(v, byRef);
                return $"{{ let {a} = {from}; {Enc(d.Repr, a, isRef)} }}";
            case Res { Ok: null }:
                return $"match {v} {{ Ok(_) => w.tag(true), Err({a}) => {{ w.tag(false); w.str(&ErrText(&{a}).text()); }} }}";
            case Res r:
                return $"match {v} {{ Ok({a}) => {{ w.tag(true); {Enc(r.Ok, a, byRef)} }} Err({a}) => {{ w.tag(false); w.str(&ErrText(&{a}).text()); }} }}";
            default:
                throw new Skip("composite shape");
        }
    }

    static string WireLet(string n) => $"let {n}_r = WireIn::new({n}_p, {n}_l); ";

    /// <summary>An expression reading a value from the reader `r`.</summary>
    string Dec(Ty t, string r) => t switch
    {
        Wire w => Dec(w.Inner, r),
        Ref x => $"{{ let t_ = {Dec(x.Inner, r)}; {r}.hold(t_) }}",
        Prim p => $"{r}.raw::<{p.Rust}>()",
        Str { Mode: StrMode.Owned } => $"{r}.str().to_owned()",
        Str { Mode: StrMode.Borrowed } => $"{r}.str()",
        Str { Mode: StrMode.Static } => $"intern({r}.str())",
        Str { Mode: StrMode.PathBuf } => $"std::path::PathBuf::from({r}.str())",
        Str { Mode: StrMode.PathRef } => $"std::path::Path::new({r}.str())",
        Value { T.Kind: Kind.Enum } e => $"to_{e.T.Sym}({r}.raw::<i32>())",
        Value { T.Kind: Kind.Record } v => $"{v.T.RustPath}::from({r}.raw::<R_{v.T.Sym}>())",
        Value v => $"(*({r}.raw::<*const c_void>() as *const {v.T.RustType})).clone()",
        Element => $"*take({r}.raw::<u32>())",
        Obj o => $"unown::<{o.T.RustType}>({r}.raw::<*mut c_void>())",
        Handle { Mut: true } h => $"&mut *({r}.raw::<*mut c_void>() as *mut {h.T.RustType})",
        Handle h => $"&*({r}.raw::<*mut c_void>() as *const {h.T.RustType})",
        Seq { Mode: SeqMode.Vec } s => DecList(s.Item, r),
        Seq { Mode: SeqMode.Slice } s => $"{{ let t_ = {DecList(s.Item, r)}; {r}.hold(t_).as_slice() }}",
        Seq s => $"&*Box::leak({DecList(s.Item, r)}.into_boxed_slice())",
        Flat { Array: true } f => "[" + string.Join(", ", f.Items.Select(i => Dec(i, r))) + "]",
        Flat f => "(" + string.Concat(f.Items.Select(i => Dec(i, r) + ", ")) + ")",
        Opt o => $"if {r}.tag() {{ Some({Dec(o.Inner, r)}) }} else {{ None }}",
        Adapt a => a.To(Dec(a.Repr, r)),
        _ => throw new Skip("composite shape"),
    };

    string DecList(Ty item, string r) => item is Prim p ? $"{r}.slice::<{p.Rust}>()"
        : $"{{ let n_ = {r}.len(); (0..n_).map(|_| {Dec(item, r)}).collect::<Vec<_>>() }}";

    // C#

    /// <summary>Statements writing `v` to the writer `w`.</summary>
    string CsEnc(Ty t, string v, string w)
    {
        var a = $"e{_tmp++}";
        switch (t)
        {
            case Wire x:
                return CsEnc(x.Inner, v, w);
            case Ref r:
                return CsEnc(r.Inner, v, w);
            case Prim p:
                return $"{w}.Raw<{p.Cs}>({v}); ";
            case Str:
                return $"{w}.Str({v}); ";
            case Value { T.Kind: Kind.Interned }:
                return $"{w}.Raw<nint>({v}.P); ";
            case Value x:
                return $"{w}.Raw<{x.CsType}>({v}); ";
            case Element:
                return $"{w}.Raw<uint>({v}.Id); ";
            case Obj:
                return $"{w}.Raw<nint>({v}.O); ";
            case Handle:
                return $"{w}.Raw<nint>({v}.P); ";
            case Seq { Item: Prim p }:
                return $"{w}.Prims<{p.Cs}>({v}); ";
            case Seq s:
                return $"{w}.Len({v}.Length); foreach (var {a} in {v}) {{ {CsEnc(s.Item, a, w)}}} ";
            case Flat f:
                return string.Concat(f.Items.Select((x, i) => CsEnc(x, $"{v}.Item{i + 1}", w)));
            case Opt o:
                return $"if ({v} is {{ }} {a}) {{ {w}.Tag(true); {CsEnc(o.Inner, a, w)}}} else {w}.Tag(false); ";
            case Adapt d:
                return CsEnc(d.Repr, d.CsTo(v), w);
            default:
                throw new Skip("composite shape");
        }
    }

    /// <summary>An expression reading a value from the reader `q`.</summary>
    string CsDec(Ty t, string q)
    {
        var inner = $"q{_tmp++}";
        return t switch
        {
            Wire w => CsDec(w.Inner, q),
            Ref r => CsDec(r.Inner, q),
            Prim p => $"{q}.Raw<{p.Cs}>()",
            Str => $"{q}.Str()",
            Value { T.Kind: Kind.Interned } v => $"new {v.CsType}({q}.Raw<nint>())",
            Value v => $"{q}.Raw<{v.CsType}>()",
            Element => $"new global::Rune.El({q}.Raw<uint>())",
            Handle => $"new {t.CsType}({q}.Raw<nint>(), {q}.Scope)",
            Obj => $"new {t.CsType}({q}.Raw<nint>())",
            Seq { Item: Prim p } => $"{q}.Prims<{p.Cs}>()",
            Seq s => $"{q}.List<{Wire.Data(s.Item)}>(static {inner} => {CsDec(s.Item, inner)})",
            Flat f => "(" + string.Join(", ", f.Items.Select(i => CsDec(i, q))) + ")",
            Opt o => $"({q}.Tag() ? {CsDec(o.Inner, q)} : ({Wire.Data(o)})null)",
            Adapt a => a.CsFrom(CsDec(a.Repr, q)),
            Res { Ok: { } ok } => $"{q}.Checked<{Wire.Data(ok)}>(static {inner} => {CsDec(ok, inner)})",
            _ => throw new Skip("composite shape"),
        };
    }

    /// <summary>A whole value out of `src`, an expression giving a Utf8Ref (or `ptr, len`).</summary>
    /// <summary>A handle's opening. `P` checks the handle on every use: null is a default struct, and
    /// a scope that has ended is a borrow held past the callback that received it.</summary>
    static string HandleHead(string n) =>
        $"public readonly unsafe partial struct {n}\n{{\n    readonly nint _p;\n    readonly uint _s;\n    internal nint P => Interop.Check(_p, _s);\n    internal nint O => Interop.Take(_p, _s);\n    internal {n}(nint p, uint scope = 0) {{ _p = p; _s = scope; }}";

    static bool HoldsHandle(Ty t) => t switch
    {
        Handle => true,
        Wire w => HoldsHandle(w.Inner),
        Ref r => HoldsHandle(r.Inner),
        Opt o => HoldsHandle(o.Inner),
        Seq s => HoldsHandle(s.Item),
        Flat f => f.Items.Any(HoldsHandle),
        Adapt a => HoldsHandle(a.Repr),
        Res { Ok: { } ok } => HoldsHandle(ok),
        _ => false,
    };

    /// <summary>Reads a wire value. Handles in it are borrows, which last as long as `scope`.</summary>
    string CsRead(Ty t, string src, string? scope = null)
    {
        if (t is Wire { Inner: var i })
        {
            t = i;
        }
        if (t is Res { Ok: null })
        {
            return $"WireReader.Check({src})";
        }
        var q = $"q{_tmp++}";
        var tail = scope is not null && HoldsHandle(t) ? $", {scope}" : "";
        return $"WireReader.Read<{Wire.Data(t)}>({src}, static {q} => {CsDec(t, q)}{tail})";
    }

    void CsWire(Wire x, string n, string value, StringBuilder pre, List<string> fixes, List<string> args)
    {
        var tmp = n.TrimStart('@') + "__" + args.Count;
        pre.Append($"var {tmp}w = new WireWriter(); {CsEnc(x.Inner, value, tmp + "w")}byte[] {tmp}b = {tmp}w.ToArray(); ");
        fixes.Add($"fixed (byte* {tmp}p = {tmp}b)");
        args.Add($"{tmp}p");
        args.Add($"(nuint){tmp}b.Length");
    }

    void EmitWireConst(Ty ty, string rustExpr, string cls, string name)
    {
        var sym = "rune_gen_const_" + Names.Ident(rustExpr);
        var body = WireBody(ty, "v", false);
        var read = CsRead(ty, $"N.{sym}()");
        _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {sym}() -> Utf8 {{ let v = {rustExpr}; {body} }}");
        _native.AppendLine($"    [DllImport(Lib)] public static extern Utf8Ref {sym}();");
        AddMember(cls, Names.Member(name, cls), n => $"    public static {Wire.Data(ty)} {n} => {read};");
        _bound++;
    }

    // Fields

    /// <summary>A getter for a field with no flat form, or null when it can't be read.</summary>
    string? WireGetter(TypeInfo t, Ty ty, string field, string get)
    {
        try
        {
            var body = WireBody(ty, "f", true);
            var read = CsRead(ty, $"N.{get}(P)", "_s");
            _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {get}(h: *mut c_void) -> Utf8 {{ let f = &{field}; {body} }}");
            _native.AppendLine($"    [DllImport(Lib)] public static extern Utf8Ref {get}(nint h);");
            return read;
        }
        catch (Skip)
        {
            return null;
        }
    }
}

static class ShapeText
{
    public static string Wrap(this string s, string suffix) => $"({s}){suffix}";
}
