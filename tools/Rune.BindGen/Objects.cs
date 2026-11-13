using System.Text.Json;

namespace Rune.BindGen;

/// <summary>What an owned object gets beyond its methods: Free, fields, Default and variants.</summary>
sealed partial class Generator
{
    void EmitObject(TypeInfo t)
    {
        if (t.Short == "Cx")
        {
            return; // only ever borrowed, and has private fields
        }
        var free = $"rune_gen_{t.Sym}_free";
        _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {free}(h: *mut c_void) {{ drop(unown::<{t.RustType}>(h)); }}");
        _native.AppendLine($"    [DllImport(Lib)] public static extern void {free}(nint h);");
        AddMember(t.CsName, "Free", n => $"    /// <summary>Drops an object nothing consumed.</summary>\n    public void {n}() => N.{free}(O);");

        var inner = t.Item.GetProperty("inner");
        if (inner.TryGetProperty("struct", out var st))
        {
            EmitFields(t, st);
            if (HasDefault(t, st))
            {
                var sym = $"rune_gen_{t.Sym}_default";
                _rs.AppendLine($"#[unsafe(no_mangle)] pub extern \"C\" fn {sym}() -> *mut c_void {{ own(<{t.RustType} as Default>::default()) }}");
                _native.AppendLine($"    [DllImport(Lib)] public static extern nint {sym}();");
                AddMember(t.CsName, "CreateDefault", n => $"    public static {t.Cs} {n}() => new(N.{sym}());");
            }
        }
        else
        {
            EmitVariants(t, inner.GetProperty("enum"));
            EmitInspection(t, inner.GetProperty("enum"));
        }
    }

    static bool HasDefault(TypeInfo t, JsonElement data)
    {
        foreach (var id in data.GetProperty("impls").EnumerateArray())
        {
            if (t.Doc.Item(id.GetInt32()) is not { } item)
            {
                continue;
            }
            var impl = item.GetProperty("inner").GetProperty("impl");
            var tr = impl.GetProperty("trait");
            if (tr.ValueKind == JsonValueKind.Object && tr.GetProperty("path").GetString()!.EndsWith("Default"))
            {
                // Host implements no traits and ManagedFn only Clone and PartialEq, so
                // `impl<T: Default> Default for Tracked<T>` holds for neither.
                var has = t.RustType.Contains("ManagedFn") ? ManagedTraits : t.RustType.Contains("Host") ? AutoTraits : null;
                return has is null || !BoundsTypeParam(impl.GetProperty("generics"), has);
            }
        }
        return false;
    }

    static readonly HashSet<string> AutoTraits = ["Send", "Sync", "Sized", "Unpin"];
    static readonly HashSet<string> ManagedTraits = ["Send", "Sync", "Sized", "Unpin", "Clone", "PartialEq"];

    /// <summary>Whether an impl asks a type parameter for a trait outside <paramref name="has"/>.</summary>
    static bool BoundsTypeParam(JsonElement generics, HashSet<string> has)
    {
        bool Real(JsonElement bounds) => bounds.EnumerateArray().Any(b =>
            b.TryGetProperty("trait_bound", out var tb)
            && !has.Contains(tb.GetProperty("trait").GetProperty("path").GetString()!.Split("::")[^1]));
        foreach (var p in generics.GetProperty("params").EnumerateArray())
        {
            if (p.GetProperty("kind").TryGetProperty("type", out var ty) && Real(ty.GetProperty("bounds")))
            {
                return true;
            }
        }
        foreach (var w in generics.GetProperty("where_predicates").EnumerateArray())
        {
            if (w.TryGetProperty("bound_predicate", out var bp) && Real(bp.GetProperty("bounds")))
            {
                return true;
            }
        }
        return false;
    }

    void EmitFields(TypeInfo t, JsonElement st)
    {
        if (st.GetProperty("kind").ValueKind != JsonValueKind.Object || !st.GetProperty("kind").TryGetProperty("plain", out var plain))
        {
            return;
        }
        foreach (var f in plain.GetProperty("fields").EnumerateArray())
        {
            if (t.Doc.Item(f.GetInt32()) is not { } item)
            {
                continue;
            }
            var name = item.GetProperty("name").GetString()!;
            var api = $"{t.Short}.{name}";
            try
            {
                var ty = Map(t.Doc, item.GetProperty("inner").GetProperty("struct_field"), new Ctx(null, t));
                var field = $"(*(h as *mut {t.RustType})).{RawIdent(name)}";
                var get = $"rune_gen_{t.Sym}_field_get_{Names.Ident(name)}";
                var set = $"rune_gen_{t.Sym}_field_set_{Names.Ident(name)}";

                if (ty is HostVal)
                {
                    _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {get}(h: *mut c_void) -> usize {{ {field}.state }}");
                    _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {set}(h: *mut c_void, v: usize) {{ {field} = Host {{ state: v }}; }}");
                    _native.AppendLine($"    [DllImport(Lib)] public static extern nint {get}(nint h);");
                    _native.AppendLine($"    [DllImport(Lib)] public static extern void {set}(nint h, nint v);");
                    AddMember(t.CsName, "Get" + Names.Pascal(name), n => $"    public S {n}<S>() where S : class => (S)App.StateOf(N.{get}(P));");
                    AddMember(t.CsName, "Set" + Names.Pascal(name), n => $"    public void {n}<S>(S value) where S : class => N.{set}(P, Interop.Keep(value));");
                    _bound++;
                    continue;
                }

                string? getter = null;
                if (ty is Prim or Value)
                {
                    var (rt, ct, conv, wrap) = Scalar(ty);
                    _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {get}(h: *mut c_void) -> {rt} {{ {conv(field)} }}");
                    _native.AppendLine($"    [DllImport(Lib)] public static extern {ct} {get}(nint h);");
                    getter = $"get => {wrap($"N.{get}(P)")};";
                }
                else if (ty is Str { Mode: StrMode.Owned or StrMode.Borrowed or StrMode.Static })
                {
                    _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {get}(h: *mut c_void) -> Utf8 {{ ret_str(&{field}) }}");
                    _native.AppendLine($"    [DllImport(Lib)] public static extern Utf8Ref {get}(nint h);");
                    getter = $"get => Interop.FromUtf8(N.{get}(P));";
                }
                else if (ty is Obj fo)
                {
                    _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {get}(h: *mut c_void) -> *mut c_void {{ &mut {field} as *mut _ as *mut c_void }}");
                    _native.AppendLine($"    [DllImport(Lib)] public static extern nint {get}(nint h);");
                    getter = $"get => new {fo.CsType}(N.{get}(P), _s == 0 ? Interop.Lent : _s);";
                }

                else if (ty is Handle or Ref)
                {
                    var csGet = WireGetter(t, ty, field, get) ?? throw new Skip("field shape");
                    AddMember(t.CsName, Names.Member(name, t.CsName), n => $"    public {Wire.Data(ty)} {n} => {csGet};");
                    _bound++;
                    continue;
                }
                else if (ty is not Closure && WireGetter(t, ty, field, get) is { } csGet)
                {
                    if (ty is Seq)
                    {
                        AddMember(t.CsName, "Get" + Names.Pascal(name), n => $"    public {Wire.Data(ty)} {n}() => {csGet};");
                    }
                    else
                    {
                        getter = $"get => {csGet};";
                    }
                    if (ty is Seq { Mode: SeqMode.Slice })
                    {
                        _bound++;
                        continue;
                    }
                }

                var ffi = FfiParams(ty, "v").ToList();
                if (ty is Seq { Mode: SeqMode.Slice } or Closure or Ref or Handle or FnPtr)
                {
                    throw new Skip("field shape");
                }
                _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {set}(h: *mut c_void, {string.Join(", ", ffi.Select(x => $"{x.Name}: {x.Rust}"))}) {{ {(ty is Wire ? WireLet("v") : "")}{field} = {RustArg(ty, "v")}; }}");
                _native.AppendLine($"    [DllImport(Lib)] public static extern void {set}(nint h, {string.Join(", ", ffi.Select(x => $"{x.Cs} {x.Name}"))});");
                var pre = new System.Text.StringBuilder();
                var fixes = new List<string>();
                var args = new List<string> { "P" };
                CsArg(ty, "value", "value", pre, fixes, args);
                var setter = $"{pre}{string.Concat(fixes.Select(x => x + " "))}{{ N.{set}({string.Join(", ", args)}); }}";

                var csType = ty.CsType;
                if (ty is Seq)
                {
                    AddMember(t.CsName, "Set" + Names.Pascal(name), n => $"    public void {n}({csType} value) {{ {setter} }}");
                }
                else
                {
                    AddMember(t.CsName, Names.Member(name, t.CsName), n => $"    public {csType} {n} {{ {getter ?? ""} set {{ {setter} }} }}");
                }
                _bound++;
            }
            catch (Skip e)
            {
                _skipped.Add((api, e.Message));
            }
        }
    }

    void EmitVariants(TypeInfo t, JsonElement en)
    {
        foreach (var v in en.GetProperty("variants").EnumerateArray())
        {
            if (t.Doc.Item(v.GetInt32()) is not { } item)
            {
                continue;
            }
            var name = item.GetProperty("name").GetString()!;
            var api = $"{t.Short}::{name}";
            try
            {
                var kind = item.GetProperty("inner").GetProperty("variant").GetProperty("kind");
                var fields = new List<(string Name, JsonElement Ty)>();
                bool named = false;
                if (kind.ValueKind == JsonValueKind.Object)
                {
                    named = kind.TryGetProperty("struct", out var sv);
                    var ids = named ? sv.GetProperty("fields") : kind.GetProperty("tuple");
                    foreach (var id in ids.EnumerateArray())
                    {
                        if (id.ValueKind != JsonValueKind.Number)
                        {
                            throw new Skip("private variant field");
                        }
                        var fi = t.Doc.Item(id.GetInt32())!.Value;
                        fields.Add((fi.GetProperty("name").GetString()!, fi.GetProperty("inner").GetProperty("struct_field")));
                    }
                }
                var ps = fields.Select((f, i) => new Param(named ? f.Name : $"item{i}", Map(t.Doc, f.Ty, new Ctx(null, t)))).ToList();
                if (ps.Any(p => p.Ty is Seq { Mode: SeqMode.Slice } or Handle))
                {
                    throw new Skip("variant field shape");
                }
                var ffi = ps.SelectMany(p => FfiParams(p.Ty, p.P)).ToList();
                var values = ps.Select(p => RustArg(p.Ty, p.P)).ToList();
                var ctor = kind.ValueKind != JsonValueKind.Object ? $"{t.RustExprPath}::{name}"
                    : named ? $"{t.RustExprPath}::{name} {{ {string.Join(", ", ps.Select((p, i) => $"{RawIdent(fields[i].Name)}: {values[i]}"))} }}"
                    : $"{t.RustExprPath}::{name}({string.Join(", ", values)})";
                var lets = string.Concat(ps.Where(p => p.Ty is Closure).Select(p => $"let {p.P}_h = ManagedFn::new({p.P}); ")) + string.Concat(ps.Where(p => p.Ty is Wire).Select(p => WireLet(p.P)));
                var sym = $"rune_gen_{t.Sym}_new_{Names.Ident(name)}";
                _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {sym}({string.Join(", ", ffi.Select(x => $"{x.Name}: {x.Rust}"))}) -> *mut c_void {{ {lets}own({ctor}) }}");
                _native.AppendLine($"    [DllImport(Lib)] public static extern nint {sym}({string.Join(", ", ffi.Select(x => $"{x.Cs} {x.Name}"))});");
                var pre = new System.Text.StringBuilder();
                var fixes = new List<string>();
                var args = new List<string>();
                foreach (var p in ps)
                {
                    CsArg(p.Ty, p.Cs, p.Cs, pre, fixes, args);
                }
                var parms = string.Join(", ", ps.Select(p => $"{p.Ty.CsType} {p.Cs}"));
                var generic = ps.Any(p => p.Ty is HostVal or Closure { UsesState: true } or FnPtr { C.UsesState: true }) ? "<S>" : "";
                var where = generic != "" ? " where S : class" : "";
                AddMember(t.CsName, Names.Member(name, t.CsName), n => $"    public static {t.Cs} {n}{generic}({parms}){where} {{ {pre}{string.Concat(fixes.Select(x => x + " "))}{{ return new(N.{sym}({string.Join(", ", args)})); }} }}");
                _bound++;
            }
            catch (Skip e)
            {
                _skipped.Add((api, e.Message));
            }
        }
    }

    /// <summary>Lets C# tell which variant a data enum holds and read its fields. Objects read this way are borrowed.</summary>
    void EmitInspection(TypeInfo t, JsonElement en)
    {
        var arms = new List<string>();
        var cases = new List<string>();
        foreach (var v in en.GetProperty("variants").EnumerateArray())
        {
            if (t.Doc.Item(v.GetInt32()) is not { } item)
            {
                continue;
            }
            var name = item.GetProperty("name").GetString()!;
            var kind = item.GetProperty("inner").GetProperty("variant").GetProperty("kind");
            bool hasFields = kind.ValueKind == JsonValueKind.Object;
            bool named = hasFields && kind.TryGetProperty("struct", out _);
            var pattern = !hasFields ? "" : named ? " { .. }" : "(..)";
            arms.Add($"{t.RustPath}::{name}{pattern} => {cases.Count}");
            cases.Add(name);
            if (!hasFields)
            {
                continue;
            }

            var ids = (named ? kind.GetProperty("struct").GetProperty("fields") : kind.GetProperty("tuple")).EnumerateArray().ToList();
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i].ValueKind != JsonValueKind.Number)
                {
                    continue;
                }
                var field = t.Doc.Item(ids[i].GetInt32())!.Value;
                var fieldName = field.GetProperty("name").GetString()!;
                try
                {
                    EmitVariantField(t, name, fieldName, field.GetProperty("inner").GetProperty("struct_field"), named, i, ids.Count);
                    _bound++;
                }
                catch (Skip e)
                {
                    _skipped.Add(($"{t.Short}::{name}.{fieldName} (read)", e.Message));
                }
            }
        }

        var which = $"rune_gen_{t.Sym}_which";
        _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {which}(h: *mut c_void) -> i32 {{ #[allow(unreachable_patterns)] match &*(h as *const {t.RustType}) {{ {string.Join(", ", arms)}, _ => -1 }} }}");
        _native.AppendLine($"    [DllImport(Lib)] public static extern int {which}(nint h);");
        string caseName = "";
        AddMember(t.CsName, "Case", n =>
        {
            caseName = n;
            return $"    public enum {n} {{ {string.Join(", ", cases)} }}";
        });
        AddMember(t.CsName, "Which", n => $"    public {caseName} {n} => ({caseName})N.{which}(P);");
    }

    void EmitVariantField(TypeInfo t, string variant, string field, JsonElement type, bool named, int index, int count)
    {
        var ty = Map(t.Doc, type, new Ctx(null, t));
        var suffix = count == 1 ? "" : "_" + Names.Ident(field);
        var sym = $"rune_gen_{t.Sym}_as_{Names.Ident(variant)}{suffix}";
        var pattern = named
            ? $"{t.RustPath}::{variant} {{ {RawIdent(field)}: v, .. }}"
            : $"{t.RustPath}::{variant}({string.Concat(Enumerable.Repeat("_, ", index))}v, ..)";

        string rustType, csType, write, read, csResult;
        switch (ty)
        {
            case Str s when s.Mode != StrMode.Static:
                (rustType, csType, write, read, csResult) = ("Utf8", "Utf8Ref", $"ret_str({StrView(s, "(*v)")})", "Interop.FromUtf8(o)", "string");
                break;
            case Obj o:
                (rustType, csType, write, read, csResult) = ("*mut c_void", "nint", $"v as *const _ as *mut c_void", $"new {o.CsType}(o, _s)", o.CsType);
                break;
            case Prim or Value { T.Kind: Kind.Enum or Kind.Record } or Managed:
                var (rt, ct, conv, wrap) = Scalar(ty);
                (rustType, csType, write, read, csResult) = (rt, ct, conv("(*v)"), wrap("o"), ty.CsType);
                break;
            default:
                if (ty is Closure or FnPtr)
                {
                    throw new Skip("variant field shape");
                }
                (rustType, csType, write, read, csResult) = ("Utf8", "Utf8Ref", $"{{ {WireBody(ty, "v", true)} }}", CsRead(ty, "o", "_s"), Wire.Data(ty));
                break;
        }
        _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {sym}(h: *mut c_void, o: *mut {rustType}) -> bool {{ if let {pattern} = &*(h as *const {t.RustType}) {{ *o = {write}; true }} else {{ false }} }}");
        _native.AppendLine($"    [DllImport(Lib)] public static extern bool {sym}(nint h, {csType}* o);");
        var member = "As" + Names.Pascal(variant) + (count == 1 ? "" : Names.Pascal(field));
        AddMember(t.CsName, member, n => $"    public {csResult.TrimEnd('?')}? {n}() {{ {csType} o; return N.{sym}(P, &o) ? {read} : null; }}");
    }
}
