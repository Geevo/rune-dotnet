using System.Text;
using System.Text.Json;

namespace Rune.BindGen;

sealed partial class Generator
{
    enum Recv { None, ElOwned, ElRef, HandleRef, HandleMut, HandleOwned, ValueSelf }

    StringBuilder _rs = new(), _native = new(), _trampolines = new(), _register = new();
    Dictionary<string, List<string>> _members = [];
    Dictionary<string, HashSet<string>> _memberNames = [];
    Dictionary<string, int> _shapes = [];
    List<(string Api, string Reason)> _skipped = [];
    int _bound;

    void Emit()
    {
        _rs = new();
        _native = new();
        _trampolines = new();
        _register = new();
        _members = [];
        _memberNames = [];
        _shapes = [];
        _skipped = [];
        _bound = 0;
        _pools.Clear();

        foreach (var t in _types.Values.ToList())
        {
            Classify(t);
        }
        var ordered = _types.Values.OrderBy(t => t.CsName, StringComparer.Ordinal).ToList();
        foreach (var t in ordered)
        {
            EmitType(t);
        }
        foreach (var t in ordered)
        {
            EmitImpls(t);
        }
        foreach (var (doc, item, path, module) in _functions.Values.OrderBy(f => f.RustPath, StringComparer.Ordinal))
        {
            EmitFree(doc, item, path, module);
        }
        foreach (var (doc, item, path, module) in _constants.Values.OrderBy(f => f.RustPath, StringComparer.Ordinal))
        {
            EmitConst(doc, item.GetProperty("inner").GetProperty("constant").GetProperty("type"), path, path, ClassFor(module), item.GetProperty("name").GetString()!);
        }
        EmitSynth();
        Finish(ordered);
    }

    static string ClassName(string module) => module == "" ? "Ui" : Names.Pascal(module);

    string ClassFor(string module)
    {
        var name = ClassName(module);
        return _types.Values.Any(t => t.CsName == name) ? name + "Module" : name;
    }

    void AddMember(string cls, string name, Func<string, string> code)
    {
        if (!_memberNames.TryGetValue(cls, out var names))
        {
            _memberNames[cls] = names = [];
        }
        while (!names.Add(name))
        {
            name += "_";
        }
        if (!_members.TryGetValue(cls, out var list))
        {
            _members[cls] = list = [];
        }
        list.Add(code(name));
    }

    // Types

    void EmitType(TypeInfo t)
    {
        switch (t.Kind)
        {
            case Kind.Record:
                EmitRecord(t);
                break;
            case Kind.Enum:
                EmitEnum(t);
                break;
            case Kind.Handle:
                EmitObject(t);
                break;
            case Kind.Interned:
                _rs.AppendLine($$"""
                    static POOL_{{t.Sym}}: std::sync::Mutex<Vec<&'static {{t.RustPath}}>> = std::sync::Mutex::new(Vec::new());
                    fn intern_{{t.Sym}}(v: {{t.RustPath}}) -> *const c_void {
                        let mut pool = POOL_{{t.Sym}}.lock().unwrap();
                        if let Some(found) = pool.iter().find(|p| ***p == v) { return *found as *const {{t.RustPath}} as *const c_void; }
                        let leaked: &'static {{t.RustPath}} = Box::leak(Box::new(v));
                        pool.push(leaked);
                        leaked as *const {{t.RustPath}} as *const c_void
                    }
                    """);
                break;
        }
    }

    static string FieldRust(Ty ty) => ty switch
    {
        Prim p => p.Rust,
        Value { T.Kind: Kind.Enum } => "i32",
        Value v => $"R_{v.T.Sym}",
        _ => throw new InvalidOperationException(),
    };

    void EmitRecord(TypeInfo t)
    {
        var r = $"R_{t.Sym}";
        _rs.AppendLine($"#[repr(C)] #[derive(Clone, Copy)] pub struct {r} {{ {string.Join(", ", t.Fields.Select(f => $"pub {f.Rust}: {FieldRust(f.Ty)}"))} }}");
        string To(string name, Ty ty) => ty switch
        {
            Value { T.Kind: Kind.Enum } v => $"to_{v.T.Sym}(c.{name})",
            Value v => $"{v.T.RustPath}::from(c.{name})",
            _ => $"c.{name}",
        };
        string From(string name, Ty ty) => ty switch
        {
            Value { T.Kind: Kind.Enum } v => $"from_{v.T.Sym}(&c.{name})",
            Value v => $"R_{v.T.Sym}::from(&c.{name})",
            _ => $"c.{name}",
        };
        _rs.AppendLine($"impl From<{r}> for {t.RustPath} {{ fn from(c: {r}) -> Self {{ {t.RustPath} {{ {string.Join(", ", t.Fields.Select(f => $"{f.Rust}: {To(f.Rust, f.Ty)}"))} }} }} }}");
        _rs.AppendLine($"impl From<&{t.RustPath}> for {r} {{ fn from(c: &{t.RustPath}) -> Self {{ {r} {{ {string.Join(", ", t.Fields.Select(f => $"{f.Rust}: {From(f.Rust, f.Ty)}"))} }} }} }}");
    }

    void EmitEnum(TypeInfo t)
    {
        var to = string.Join(" ", t.Variants.Select((v, i) => $"{i} => {t.RustPath}::{v.Rust},"));
        var from = string.Join(" ", t.Variants.Select((v, i) => $"{t.RustPath}::{v.Rust} => {i},"));
        _rs.AppendLine($"fn to_{t.Sym}(v: i32) -> {t.RustPath} {{ match v {{ {to} _ => panic!(\"bad {t.Short} value\") }} }}");
        _rs.AppendLine($"#[allow(unreachable_patterns)] fn from_{t.Sym}(v: &{t.RustPath}) -> i32 {{ match v {{ {from} _ => -1 }} }}");
    }

    // Impl blocks

    void EmitImpls(TypeInfo t)
    {
        var inner = t.Item.GetProperty("inner");
        var data = inner.TryGetProperty("struct", out var s) ? s : inner.GetProperty("enum");
        foreach (var implId in data.GetProperty("impls").EnumerateArray())
        {
            if (t.Doc.Item(implId.GetInt32()) is not { } implItem)
            {
                continue;
            }
            var impl = implItem.GetProperty("inner").GetProperty("impl");
            if (impl.GetProperty("trait").ValueKind != JsonValueKind.Null || impl.GetProperty("is_synthetic").GetBoolean())
            {
                continue;
            }
            if (impl.GetProperty("blanket_impl").ValueKind != JsonValueKind.Null)
            {
                continue;
            }
            if (!EnterImpl(t, impl))
            {
                continue;
            }
            foreach (var m in impl.GetProperty("items").EnumerateArray())
            {
                if (t.Doc.Item(m.GetInt32()) is not { } item || item.GetProperty("visibility").GetString() != "public" || Hidden(item))
                {
                    continue;
                }
                var name = item.GetProperty("name").GetString()!;
                var mi = item.GetProperty("inner");
                if (mi.TryGetProperty("function", out var fn))
                {
                    foreach (var (subst, suffix) in MethodInstances(fn))
                    {
                        _fnSubst = subst;
                        EmitMethod(t, item, fn, name, suffix);
                    }
                    _fnSubst = null;
                }
                else if (mi.TryGetProperty("assoc_const", out var ac))
                {
                    if (t.Kind is Kind.Record or Kind.Interned or Kind.Handle or Kind.Enum)
                    {
                        EmitConst(t.Doc, ac.GetProperty("type"), $"{t.Short}::{name}", $"{t.RustPath}::{name}", t.Kind == Kind.Enum ? t.CsName + "Extensions" : t.CsName, name, new Ctx(null, t));
                    }
                }
            }
        }
    }

    Dictionary<string, string>? _fnSubst;

    bool IsOwnerBound(TypeInfo t) => t.Kind is Kind.Record or Kind.Interned or Kind.Element or Kind.Handle or Kind.Enum;

    void EmitMethod(TypeInfo t, JsonElement item, JsonElement fn, string name, string suffix)
    {
        var api = $"{t.Short}::{name}";
        try
        {
            if (!IsOwnerBound(t))
            {
                throw new Skip("method on a generic type");
            }
            var sig = fn.GetProperty("sig");
            var inputs = sig.GetProperty("inputs").EnumerateArray().ToList();
            var hasSelf = inputs.Count > 0 && inputs[0][0].GetString() == "self";
            var recv = Recv.None;
            if (hasSelf)
            {
                var self = inputs[0][1];
                var isRef = self.TryGetProperty("borrowed_ref", out var br);
                var mutable = isRef && br.GetProperty("is_mutable").GetBoolean();
                recv = (t.Kind, isRef) switch
                {
                    (Kind.Element, false) => Recv.ElOwned,
                    (Kind.Element, true) => Recv.ElRef,
                    (Kind.Record or Kind.Interned or Kind.Enum, _) when !mutable => Recv.ValueSelf,
                    (Kind.Record or Kind.Interned or Kind.Enum, _) => throw new Skip("&mut self on a value"),
                    (_, true) => mutable ? Recv.HandleMut : Recv.HandleRef,
                    _ => Recv.HandleOwned,
                };
            }
            EmitFunction(api, t, recv, t.Doc, fn, sig, inputs.Skip(hasSelf ? 1 : 0).ToList(), $"rune_gen_{t.Sym}_{Names.Ident(name)}{suffix.ToLowerInvariant()}", t.CsName, Names.Member(name, t.CsName) + suffix, t.Instantiated ? $"<{t.RustType}>::{name}" : $"{t.RustPath}::{name}", name);
        }
        catch (Skip e)
        {
            _skipped.Add((api, e.Message));
        }
    }

    void EmitFree(Doc doc, JsonElement item, string path, string module)
    {
        var fn = item.GetProperty("inner").GetProperty("function");
        var name = item.GetProperty("name").GetString()!;
        // A free function has no impl: its generics come from its own signature, never from the last impl emitted.
        _implSubst = null;
        try
        {
            var sig = fn.GetProperty("sig");
            foreach (var (subst, suffix) in MethodInstances(fn))
            {
                // Only `S` is the app's state. Another unbounded parameter (`with_blend<R>`) is the caller's choice, which rune-ext pins.
                foreach (var key in subst?.Where(p => p.Key != "S" && p.Value == HostKey).Select(p => p.Key).ToList() ?? [])
                {
                    subst!.Remove(key);
                }
                _fnSubst = subst;
                EmitFunction(path, null, Recv.None, doc, fn, sig, sig.GetProperty("inputs").EnumerateArray().ToList(), "rune_gen_" + Names.Ident(path) + suffix.ToLowerInvariant(), ClassFor(module), Names.Pascal(name) + suffix, path, name);
            }
        }
        catch (Skip e)
        {
            _skipped.Add((path, e.Message));
        }
        finally
        {
            _fnSubst = null;
        }
    }

    void EmitConst(Doc doc, JsonElement type, string api, string rustExpr, string cls, string name, Ctx? ctx = null)
    {
        try
        {
            var ty = Map(doc, type, ctx ?? new Ctx(null, null));
            if (ty is Wire or Flat or Seq or Str or Opt or Ref)
            {
                EmitWireConst(ty, rustExpr, cls, name);
                return;
            }
            if (ty is not (Prim or Value or Obj))
            {
                throw new Skip("constant of composite");
            }
            var sym = "rune_gen_const_" + Names.Ident(rustExpr);
            var (rustRet, csFfi, conv, wrap) = Scalar(ty);
            _rs.AppendLine($"#[unsafe(no_mangle)] pub extern \"C\" fn {sym}() -> {rustRet} {{ {conv(rustExpr)} }}");
            _native.AppendLine($"    [DllImport(Lib)] public static extern {csFfi} {sym}();");
            AddMember(cls, Names.Member(name, cls), n => $"    public static {ty.CsType} {n} => {wrap($"N.{sym}()")};");
            _bound++;
        }
        catch (Skip e)
        {
            _skipped.Add((api, e.Message));
        }
    }

    /// <summary>A returned scalar: Rust type, C# FFI type, Rust conversion, C# conversion.</summary>
    (string, string, Func<string, string>, Func<string, string>) Scalar(Ty ty) => ty switch
    {
        Prim p => (p.Rust, p.Cs, v => v, v => v),
        Value { T.Kind: Kind.Enum } e => ("i32", e.CsType, v => $"from_{e.T.Sym}(&{v})", v => v),
        Value { T.Kind: Kind.Record } r => ($"R_{r.T.Sym}", r.CsType, v => $"R_{r.T.Sym}::from(&{v})", v => v),
        Value { T.Kind: Kind.Interned } i => ("*const c_void", "nint", v => $"intern_{i.T.Sym}({v})", v => $"new {i.CsType}({v})"),
        Element => ("u32", "uint", v => $"put({v})", v => $"new global::Rune.El({v})"),
        Obj o => ("*mut c_void", "nint", v => $"own({v})", v => $"new {o.CsType}({v})"),
        HostVal => ("usize", "nint", v => $"{v}.state", v => $"(S)App.StateOf({v})"),
        Managed => ("usize", "nint", v => $"ret_managed({v}.clone())", v => $"Interop.Target({v})"),
        _ => throw new Skip("return shape"),
    };

    // Parameters

    sealed class Param(string name, Ty ty)
    {
        public string Name = name, Cs = Names.Param(name), P = "p_" + name;
        public Ty Ty = ty;
    }

    IEnumerable<(string Name, string Rust, string Cs)> FfiParams(Ty ty, string n)
    {
        switch (ty)
        {
            case Prim p:
                yield return (n, p.Rust, p.Cs);
                break;
            case Str or Wire:
                yield return (n + "_p", "*const u8", "byte*");
                yield return (n + "_l", "usize", "nuint");
                break;
            case Hashed:
                yield return (n + "_k", "u32", "uint");
                yield return (n + "_n", "u64", "ulong");
                yield return (n + "_p", "*const u8", "byte*");
                yield return (n + "_l", "usize", "nuint");
                break;
            case Ref r:
                foreach (var x in FfiParams(r.Inner, n))
                {
                    yield return x;
                }
                break;
            case Value { T.Kind: Kind.Enum } e:
                yield return (n, "i32", e.CsType);
                break;
            case Value { T.Kind: Kind.Record } v:
                yield return (n, $"R_{v.T.Sym}", v.CsType);
                break;
            case Value:
                yield return (n, "*const c_void", "nint");
                break;
            case Element:
                yield return (n, "u32", "uint");
                break;
            case Handle:
                yield return (n, "*mut c_void", "nint");
                break;
            case Obj:
                yield return (n, "*mut c_void", "nint");
                break;
            case HostVal or Managed:
                yield return (n, "usize", "nint");
                break;
            case Seq s:
                yield return (n + "_p", "*const " + SeqLeaf(s.Item).Rust, SeqLeaf(s.Item).Cs + "*");
                yield return (n + "_l", "usize", "nuint");
                break;
            case Closure:
                yield return (n, "usize", "nint");
                break;
            case FnPtr:
                yield return (n, "u32", "uint");
                break;
            case Flat f:
                for (int i = 0; i < f.Items.Count; i++)
                {
                    foreach (var x in FfiParams(f.Items[i], $"{n}_{i}"))
                    {
                        yield return x;
                    }
                }
                break;
            case Opt o:
                yield return (n + "_has", "bool", "bool");
                foreach (var x in FfiParams(o.Inner, n))
                {
                    yield return x;
                }
                break;
            default:
                throw new Skip("parameter shape");
        }
    }

    string RustArg(Ty ty, string n) => ty switch
    {
        Prim => n,
        Str { Mode: StrMode.Owned } => $"utf8({n}_p, {n}_l).to_owned()",
        Str { Mode: StrMode.Borrowed } => $"utf8({n}_p, {n}_l)",
        Str { Mode: StrMode.Static } => $"intern(utf8({n}_p, {n}_l))",
        Str { Mode: StrMode.PathBuf } => $"std::path::PathBuf::from(utf8({n}_p, {n}_l))",
        Str { Mode: StrMode.PathRef } => $"std::path::Path::new(utf8({n}_p, {n}_l))",
        Hashed => $"hash_key({n}_k, {n}_n, {n}_p, {n}_l)",
        Ref r => $"&({RustArg(r.Inner, n)})",
        Value { T.Kind: Kind.Enum } e => $"to_{e.T.Sym}({n})",
        Value { T.Kind: Kind.Record } v => $"{v.T.RustPath}::from({n})",
        Value v => $"*({n} as *const {v.T.RustPath})",
        Element => $"*take({n})",
        Obj { T.CopyObject: true } o => $"*({n} as *const {o.T.RustType})",
        Obj o => $"unown::<{o.T.RustType}>({n})",
        HostVal { Ref: true } => $"&Host {{ state: {n} }}",
        HostVal => $"Host {{ state: {n} }}",
        Managed { Ref: true } => $"&ManagedFn::new({n})",
        Managed => $"ManagedFn::new({n})",
        Seq s => SeqExpr(s, n),
        Handle { Mut: true } h => $"&mut *({n} as *mut {h.T.RustType})",
        Handle h => $"&*({n} as *const {h.T.RustType})",
        Flat { Array: true } f => "[" + string.Join(", ", f.Items.Select((x, i) => RustArg(x, $"{n}_{i}"))) + "]",
        Flat f => "(" + string.Join(", ", f.Items.Select((x, i) => RustArg(x, $"{n}_{i}"))) + ")",
        Opt o => $"if {n}_has {{ Some({RustArg(o.Inner, n)}) }} else {{ None }}",
        Closure c => RustClosure(c, n),
        FnPtr f => FnPool(f.C) + $"({n})",
        Wire w => Dec(w.Inner, n + "_r"),
        _ => throw new Skip("parameter shape"),
    };

    (string Rust, string Cs) SeqLeaf(Ty item) => item switch
    {
        Prim p => (p.Rust, p.Cs),
        Value { T.Kind: Kind.Enum } e => ("i32", e.CsType),
        Value { T.Kind: Kind.Record } r => ($"R_{r.T.Sym}", r.CsType),
        Element => ("u32", "global::Rune.El"),
        FnPtr => ("u32", "uint"),
        Obj or Handle => ("*mut c_void", "nint"),
        _ => ("*mut c_void", item.CsType),
    };

    string SeqExpr(Seq s, string n)
    {
        var item = s.Item switch
        {
            Value { T.Kind: Kind.Interned } v => $"*(*x as *const {v.T.RustPath})",
            _ => RustArg(s.Item, "(*x)"),
        };
        var vec = $"slice_of({n}_p, {n}_l).iter().map(|x| {item}).collect::<Vec<_>>()";
        return s.Mode == SeqMode.StaticSlice ? $"&*Box::leak({vec}.into_boxed_slice())" : vec;
    }

    // Closures

    IEnumerable<(string Rust, string Cs)> ClosureLeaves(Ty a) => a switch
    {
        State => [("usize", "nint")],
        Handle => [("*mut c_void", "nint")],
        Str or Wire => [("*const u8", "byte*"), ("usize", "nuint")],
        Prim p => [(p.Rust, p.Cs)],
        Value { T.Kind: Kind.Enum } e => [("i32", e.CsType)],
        Value { T.Kind: Kind.Record } r => [($"R_{r.T.Sym}", r.CsType)],
        Value => [("*const c_void", "nint")],
        Obj => [("*mut c_void", "nint")],
        Managed => [("usize", "nint")],
        Element => [("u32", "uint")],
        _ => throw new Skip("closure argument shape"),
    };

    int Shape(Closure c)
    {
        var leaves = new List<(string Rust, string Cs)> { ("usize", "nint") };
        foreach (var a in c.Args)
        {
            leaves.AddRange(ClosureLeaves(a));
        }
        var (rustRet, csRet) = c.Ret is null ? ("()", "void") : ClosureRet(c.Ret);
        var erased = c.Delegate("object");
        var key = $"{erased}|{string.Join(",", leaves)}|{rustRet}|{c.Ret?.GetType().Name}";
        if (_shapes.TryGetValue(key, out var id))
        {
            return id;
        }
        id = _shapes.Count;
        var ps = new List<string> { "nint h" };
        var args = new List<string>();
        var borrows = false;
        int i = 0;
        foreach (var a in c.Args)
        {
            var n = $"a{i++}";
            switch (a)
            {
                case State:
                    ps.Add($"nint {n}");
                    args.Add($"App.StateOf({n})");
                    break;
                case Handle h:
                    ps.Add($"nint {n}");
                    args.Add($"new {h.CsType}({n}, sc)");
                    borrows = true;
                    break;
                case Str:
                    ps.Add($"byte* {n}p");
                    ps.Add($"nuint {n}l");
                    args.Add($"Interop.FromUtf8({n}p, {n}l)");
                    break;
                case Wire w:
                    ps.Add($"byte* {n}p");
                    ps.Add($"nuint {n}l");
                    args.Add(CsRead(w, $"{n}p, {n}l", "sc"));
                    borrows |= HoldsHandle(w);
                    break;
                case Value { T.Kind: Kind.Interned } v:
                    ps.Add($"nint {n}");
                    args.Add($"new {v.CsType}({n})");
                    break;
                case Element:
                    ps.Add($"uint {n}");
                    args.Add($"new global::Rune.El({n})");
                    break;
                case Obj o:
                    ps.Add($"nint {n}");
                    args.Add($"new {o.CsType}({n})");
                    break;
                case Managed:
                    ps.Add($"nint {n}");
                    args.Add($"Interop.Target({n})");
                    break;
                default:
                    ps.Add($"{ClosureLeaves(a).Single().Cs} {n}");
                    args.Add(n);
                    break;
            }
        }
        var call = $"(({erased})GCHandle.FromIntPtr(h).Target!)({string.Join(", ", args)})";
        var body = c.Ret switch
        {
            null => $"{call};",
            Element => $"return {call}.Id;",
            Value { T.Kind: Kind.Interned } => $"return {call}.P;",
            Wire w => $"var rv = {call}; var rw = new WireWriter(); {CsEnc(w.Inner, "rv", "rw")}return rw.Keep();",
            Obj => $"return {call}.O;",
            Handle => $"return {call}.P;",
            Managed => $"return Interop.Keep({call});",
            HostVal => $"return Interop.Keep({call});",
            _ => $"return {call};",
        };
        // Borrowed arguments die when the callback returns.
        if (borrows)
        {
            body = $"var sc = Interop.Enter(); try {{ {body} }} finally {{ Interop.Leave(); }}";
        }
        var rustSig = $"extern \"C\" fn({string.Join(", ", leaves.Select(l => l.Rust))}) -> {rustRet}";
        _rs.AppendLine($"static CB_{id}: OnceLock<{rustSig}> = OnceLock::new();");
        _rs.AppendLine($"#[unsafe(no_mangle)] pub extern \"C\" fn rune_gen_cb_{id}(f: {rustSig}) {{ let _ = CB_{id}.set(f); }}");
        var ptr = $"delegate* unmanaged<{string.Join(", ", leaves.Select(l => l.Cs).Append(csRet))}>";
        _native.AppendLine($"    [DllImport(Lib)] public static extern void rune_gen_cb_{id}({ptr} f);");
        _register.AppendLine($"        N.rune_gen_cb_{id}(&Cb{id});");
        var fail = c.Ret is null ? "" : " return default;";
        _trampolines.AppendLine($"    [UnmanagedCallersOnly] static {csRet} Cb{id}({string.Join(", ", ps)}) {{ try {{ {body} }} catch (global::System.Exception e) {{ App.Fatal(e);{fail} }} }}");
        _shapes[key] = id;
        return id;
    }

    (string Rust, string Cs) ClosureRet(Ty t) => t switch
    {
        Element => ("u32", "uint"),
        Wire => ("Utf8", "Utf8Ref"),
        HostVal => ("usize", "nint"),
        _ => ClosureLeaves(t).Single(),
    };

    readonly Dictionary<string, string> _pools = [];

    string RustFn(Ty t) => t switch
    {
        State s => s.Mut ? "&mut Host" : "&Host",
        HostVal => "Host",
        Managed { Ref: true } => "&ManagedFn",
        Managed => "ManagedFn",
        _ => RustT(t),
    };

    /// <summary>A pool of real Rust functions of one signature, each forwarding to the delegate in its slot.</summary>
    string FnPool(Closure c)
    {
        var ret = c.Ret is null ? "" : " -> " + RustFn(c.Ret);
        var type = $"fn({string.Join(", ", c.Args.Select(RustFn))}){ret}";
        if (_pools.TryGetValue(type, out var name))
        {
            return name;
        }
        name = $"fp_{_pools.Count}";
        var made = RustClosure(c, "h", $"{name.ToUpperInvariant()}[I].load(std::sync::atomic::Ordering::Relaxed)");
        _rs.AppendLine($"static {name.ToUpperInvariant()}: [std::sync::atomic::AtomicUsize; FN_SLOTS] = [const {{ std::sync::atomic::AtomicUsize::new(0) }}; FN_SLOTS];");
        _rs.AppendLine($"#[allow(unused_unsafe)] fn {name}_at<const I: usize>() -> {type} {{ unsafe {{ {made} }} }}");
        _rs.AppendLine($"fn {name}(i: u32) -> {type} {{ fn_slots!(i, {name}_at) }}");
        _rs.AppendLine($"#[unsafe(no_mangle)] pub extern \"C\" fn rune_gen_{name}_set(i: u32, h: usize) {{ {name.ToUpperInvariant()}[i as usize].store(h, std::sync::atomic::Ordering::Relaxed); }}");
        _native.AppendLine($"    [DllImport(Lib)] public static extern void rune_gen_{name}_set(uint i, nint h);");
        return _pools[type] = name;
    }

    string RustClosure(Closure c, string n, string? handle = null)
    {
        var id = Shape(c);
        var ps = new List<string>();
        var pre = new StringBuilder();
        var args = new List<string> { handle ?? $"{n}_h.id()" };
        int i = 0;
        foreach (var a in c.Args)
        {
            var v = $"a{i++}";
            switch (a)
            {
                case State s:
                    ps.Add($"{v}: &{(s.Mut ? "mut " : "")}Host");
                    args.Add($"{v}.state");
                    break;
                case Handle { Mut: true } h:
                    ps.Add($"{v}: &mut {h.T.RustType}");
                    args.Add($"{v} as *mut {h.T.RustType} as *mut c_void");
                    break;
                case Handle h:
                    ps.Add($"{v}: &{h.T.RustType}");
                    args.Add($"{v} as *const {h.T.RustType} as *mut c_void");
                    break;
                case Wire w:
                    ps.Add($"{v}: {RustT(w)}");
                    pre.Append($"let mut w = WireOut::new(); {Enc(w.Inner, v, false)} let b_{v} = w; let (p_{v}, l_{v}) = b_{v}.bytes(); ");
                    args.Add($"p_{v}");
                    args.Add($"l_{v}");
                    break;
                case Str { Mode: StrMode.Owned }:
                    ps.Add($"{v}: String");
                    args.Add($"{v}.as_ptr()");
                    args.Add($"{v}.len()");
                    break;
                case Str { Mode: StrMode.PathRef }:
                    ps.Add($"{v}: &std::path::Path");
                    pre.Append($"let {v} = {v}.to_string_lossy(); ");
                    args.Add($"{v}.as_ptr()");
                    args.Add($"{v}.len()");
                    break;
                case Str:
                    ps.Add($"{v}: &str");
                    args.Add($"{v}.as_ptr()");
                    args.Add($"{v}.len()");
                    break;
                case Prim p:
                    ps.Add($"{v}: {p.Rust}");
                    args.Add(v);
                    break;
                case Value { T.Kind: Kind.Enum } e:
                    ps.Add($"{v}: {e.T.RustPath}");
                    args.Add($"from_{e.T.Sym}(&{v})");
                    break;
                case Value { T.Kind: Kind.Record } r:
                    ps.Add($"{v}: {r.T.RustPath}");
                    args.Add($"R_{r.T.Sym}::from(&{v})");
                    break;
                case Value x:
                    ps.Add($"{v}: {x.T.RustPath}");
                    args.Add($"intern_{x.T.Sym}({v})");
                    break;
                case Element:
                    ps.Add($"{v}: El");
                    args.Add($"put({v})");
                    break;
                case Obj o:
                    ps.Add($"{v}: {o.T.RustType}");
                    args.Add($"own({v})");
                    break;
                case Managed { Ref: true }:
                    ps.Add($"{v}: &ManagedFn");
                    args.Add($"{v}.id()");
                    break;
                case Managed:
                    ps.Add($"{v}: ManagedFn");
                    args.Add($"{v}.id()");
                    break;
                default:
                    throw new Skip("closure argument shape");
            }
        }
        var call = $"(CB_{id}.get().expect(\"callbacks registered\"))({string.Join(", ", args)})";
        if (c.Ret is Handle ret)
        {
            return ret.Mut ? $"tie_mut(move |{string.Join(", ", ps)}| {{ {pre}let r = {call}; unsafe {{ &mut *(r as *mut {ret.T.RustType}) }} }})"
                : $"tie_ref(move |{string.Join(", ", ps)}| {{ {pre}let r = {call}; unsafe {{ &*(r as *const {ret.T.RustType}) }} }})";
        }
        if (handle is not null && c.Ret is Element && c.Args.Any(a => a is Handle { T.Short: "Cx" }))
        {
            return $"move |{string.Join(", ", ps)}| {{ {pre}view_scope(|| {call}) }}";
        }
        var result = c.Ret is null ? call : c.Ret is Wire rw ? $"let r_r = WireIn::of(r); {Dec(rw.Inner, "r_r")}" : RustArg(c.Ret, "r");
        var body = c.Ret is null ? $"{call};" : $"let r = {call}; {result}";
        var made = $"move |{string.Join(", ", ps)}| {{ {pre}{body} }}";
        if (handle is null && c.Ret is Wire { Inner: Str { Mode: StrMode.Static } } && c.Args is [Managed { Ref: true } or Handle])
        {
            return $"tie_str({made})";
        }
        return c.Ret is Wire { Inner: Adapt { Rust: var ar } } && ar.StartsWith("std::borrow::Cow") && c.Args is [State, Prim] ? $"cow_key({made})" : made;
    }

    static string StrView(Str s, string v) => s.Mode is StrMode.PathRef or StrMode.PathBuf ? $"&{v}.to_string_lossy()" : $"&{v}";

    // Functions

    void EmitFunction(string api, TypeInfo? owner, Recv recv, Doc doc, JsonElement fn, JsonElement sig, List<JsonElement> inputs, string sym, string cls, string csName, string rustPath, string rustName)
    {
        if (Denied.Contains(api))
        {
            throw new Skip("denied (see deny.txt)");
        }
        var header = fn.GetProperty("header");
        if (header.GetProperty("is_unsafe").GetBoolean())
        {
            throw new Skip("unsafe fn");
        }
        if (header.GetProperty("is_async").GetBoolean())
        {
            throw new Skip("async fn");
        }

        var ctx = new Ctx(fn.GetProperty("generics"), owner, _fnSubst ?? _implSubst);
        var ps = new List<Param>();
        int index = 0;
        foreach (var input in inputs)
        {
            var name = input[0].GetString()!;
            if (name is "_" or "" || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                name = $"arg{index}";
            }
            index++;
            ps.Add(new Param(name.TrimStart('_') is { Length: > 0 } clean ? clean : name, Map(doc, input[1], ctx)));
        }

        var output = sig.GetProperty("output");
        bool fluent = false;
        Ty? ret = null;
        if (output.ValueKind != JsonValueKind.Null && !(output.TryGetProperty("tuple", out var unit) && unit.GetArrayLength() == 0))
        {
            // `-> &mut Self` hands back the receiver. An owned `Self` from `&self` is a new value (`Cx::retype`).
            if (recv is Recv.HandleMut or Recv.HandleRef && output.TryGetProperty("borrowed_ref", out _) && IsSelf(doc, output, owner!))
            {
                fluent = true;
            }
            else
            {
                ret = Wired(Map(doc, output, ctx));
            }
            if (ret is Closure or Handle or State)
            {
                throw new Skip("return shape");
            }
        }

        bool builder = recv == Recv.ElOwned && ret is Element;
        if (recv == Recv.ElOwned && !builder)
        {
            throw new Skip("consuming El getter");
        }

        // Rust
        var ffi = new List<string>();
        var lets = new StringBuilder();
        var names = new List<string>();
        var csFfi = new List<string>();
        if (recv is Recv.ElOwned or Recv.ElRef)
        {
            ffi.Add("id: u32");
            csFfi.Add("uint id");
        }
        else if (recv is Recv.HandleMut or Recv.HandleRef or Recv.HandleOwned)
        {
            ffi.Add("h: *mut c_void");
            csFfi.Add("nint h");
        }
        else if (recv == Recv.ValueSelf)
        {
            foreach (var (n, r, c) in FfiParams(new Value(owner!), "this_"))
            {
                ffi.Add($"{n}: {r}");
                csFfi.Add($"{c} {n}");
            }
        }
        foreach (var p in ps)
        {
            foreach (var (n, r, c) in FfiParams(p.Ty, p.P))
            {
                ffi.Add($"{n}: {r}");
                csFfi.Add($"{c} {n}");
            }
            if (p.Ty is Closure)
            {
                lets.Append($"let {p.P}_h = ManagedFn::new({p.P}); ");
            }
            if (p.Ty is Wire)
            {
                lets.Append(WireLet(p.P));
            }
            if (p.Ty is Seq { Mode: SeqMode.Slice })
            {
                lets.Append($"let v_{p.Name} = {RustArg(p.Ty, p.P)}; let a_{p.Name} = &v_{p.Name}[..]; ");
            }
            else
            {
                lets.Append($"let a_{p.Name} = {RustArg(p.Ty, p.P)}; ");
            }
            names.Add($"a_{p.Name}");
        }
        var args = string.Join(", ", names);
        var turbofish = Turbofish(fn, sig);
        var call = recv switch
        {
            Recv.None => $"{rustPath}{turbofish}({args})",
            Recv.ElOwned => $"e.{RawIdent(rustName)}{turbofish}({args})",
            Recv.ElRef => $"el(id).{RawIdent(rustName)}{turbofish}({args})",
            Recv.HandleMut => $"(&mut *(h as *mut {owner!.RustType})).{RawIdent(rustName)}{turbofish}({args})",
            Recv.HandleRef => $"(&*(h as *const {owner!.RustType})).{RawIdent(rustName)}{turbofish}({args})",
            Recv.HandleOwned => $"unown::<{owner!.RustType}>(h).{RawIdent(rustName)}{turbofish}({args})",
            _ => $"({RustArg(new Value(owner!), "this_")}).{RawIdent(rustName)}{turbofish}({args})",
        };

        string rustRet = "", body;
        string csFfiRet = "void";
        var outs = new List<(string Decl, string Name)>();
        Func<string, string> csResult = v => v;
        string? csPre = null;
        if (builder)
        {
            rustRet = " -> u32";
            csFfiRet = "uint";
            body = $"edit(id, |e| {call})";
            csResult = v => $"new global::Rune.El({v})";
        }
        else if (ret is null)
        {
            body = $"{call};";
        }
        else if (ret is Str rs && rs.Mode != StrMode.Static)
        {
            rustRet = " -> Utf8";
            csFfiRet = "Utf8Ref";
            body = $"let v = {call}; ret_str({StrView(rs, "v")})";
            csResult = v => $"Interop.FromUtf8({v})";
        }
        else if (ret is Ref { Inner: Prim or Value } rr)
        {
            var (rt, ct, conv, wrap) = Scalar(rr.Inner);
            rustRet = $" -> {rt}";
            csFfiRet = ct;
            csResult = wrap;
            body = $"let v = {call}; {conv("*v").Replace("&*v", "v")}";
        }
        else if (ret is Prim or Value or Element or Obj or HostVal or Managed)
        {
            var (rt, ct, conv, wrap) = Scalar(ret);
            rustRet = $" -> {rt}";
            csFfiRet = ct;
            csResult = wrap;
            body = $"let v = {call}; {conv("v")}";
        }
        else if (ret is Flat fl)
        {
            var (w, value) = Outs(fl, "v", ffi, csFfi, outs);
            body = $"let v = {call}; {w}";
            csResult = v => value;
            csPre = "call";
        }
        else if (ret is Opt { Inner: var oi })
        {
            rustRet = " -> bool";
            csFfiRet = "bool";
            var (w, value) = oi is Flat ofl ? Outs(ofl, "v", ffi, csFfi, outs) : OutOne(oi, "v", "o0", ffi, csFfi, outs);
            body = $"match {call} {{ Some(v) => {{ {w} true }} None => false }}";
            csResult = v => $"{v} ? {value} : null";
        }
        else if (ret is Wire wr)
        {
            rustRet = " -> Utf8";
            csFfiRet = "Utf8Ref";
            body = $"let v = {call}; {WireBody(wr.Inner, "v", false)}";
            var inherits = recv is Recv.HandleMut or Recv.HandleRef;
            csResult = v => CsRead(wr, v, inherits ? "_s" : null);
        }
        else
        {
            throw new Skip("return shape");
        }

        _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {sym}({string.Join(", ", ffi)}){rustRet} {{ {lets}{body} }}");
        _native.AppendLine($"    [DllImport(Lib)] public static extern {csFfiRet} {sym}({string.Join(", ", csFfi)});");

        // C#
        var sb = new StringBuilder();
        bool enumOwner = owner?.Kind == Kind.Enum;
        if (enumOwner)
        {
            cls += "Extensions";
        }
        var csArgs = new List<string>();
        if (recv is Recv.ElOwned or Recv.ElRef)
        {
            csArgs.Add("Id");
        }
        else if (recv is Recv.HandleMut or Recv.HandleRef)
        {
            csArgs.Add("P");
        }
        else if (recv == Recv.HandleOwned)
        {
            csArgs.Add("O");
        }
        else if (recv == Recv.ValueSelf)
        {
            csArgs.Add(owner!.Kind == Kind.Interned ? "P" : enumOwner ? "self" : "this");
        }
        var fixes = new List<string>();
        foreach (var p in ps)
        {
            CsArg(p.Ty, p.Cs, p.Cs, sb, fixes, csArgs);
        }
        csArgs.AddRange(outs.Select(o => "&" + o.Name));
        foreach (var o in outs)
        {
            sb.Append($"{o.Decl} {o.Name}; ");
        }
        var invoke = $"N.{sym}({string.Join(", ", csArgs)})";
        var csRet = builder ? "global::Rune.El" : fluent ? owner!.Cs : ret is null ? "void" : ret is Ref rf ? rf.Inner.CsType : ret.CsType;
        string stmt = fluent ? $"{invoke}; return this;"
            : (ret is null && !builder) || ret is Wire { Inner: Res { Ok: null } } ? ret is null ? $"{invoke};" : $"{csResult(invoke)};"
            : csPre is not null ? $"{invoke}; return {csResult("")};"
            : $"return {csResult(invoke)};";
        var fixedPrefix = string.Concat(fixes.Select(f => f + " "));
        var generic = ret is HostVal || ps.Any(p => p.Ty is HostVal or Closure { UsesState: true } or FnPtr { C.UsesState: true } or Seq { Item: FnPtr { C.UsesState: true } }) ? "<S>" : "";
        var where = generic != "" ? " where S : class" : "";
        var isStatic = recv == Recv.None || enumOwner ? "static " : "";
        var parms = string.Join(", ", ps.Select(p => $"{p.Ty.CsType} {p.Cs}"));
        if (enumOwner && recv == Recv.ValueSelf)
        {
            parms = $"this {owner!.Cs} self" + (parms == "" ? "" : ", " + parms);
        }
        AddMember(cls, csName, n => $"    public {isStatic}{csRet} {n}{generic}({parms}){where} {{ {sb}{fixedPrefix}{{ {stmt} }} }}");
        _bound++;
    }

    /// <summary>
    /// `::<Host>` for a type parameter Rust can't infer because only the output names it (`Cx::retype<T>() -> Cx<T>`).
    /// Empty when inference works, or when Rust forbids explicit arguments (an `impl Trait` parameter) or needs a const.
    /// </summary>
    string Turbofish(JsonElement fn, JsonElement sig)
    {
        var generics = fn.GetProperty("generics").GetProperty("params").EnumerateArray().ToList();
        if (generics.Any(p => p.GetProperty("kind").TryGetProperty("const", out _)))
        {
            return "";
        }
        var types = generics.Where(p => p.GetProperty("kind").TryGetProperty("type", out _)).ToList();
        if (types.Count == 0 || types.Any(p => p.GetProperty("kind").GetProperty("type").GetProperty("is_synthetic").GetBoolean()))
        {
            return "";
        }
        var inputs = sig.GetProperty("inputs").GetRawText();
        var names = types.Select(p => p.GetProperty("name").GetString()!).ToList();
        if (names.All(n => inputs.Contains($"\"generic\":\"{n}\"")))
        {
            return "";
        }
        var keys = names.Select(n => _fnSubst is { } s && s.TryGetValue(n, out var k) ? RustOfKey(k) : null).ToList();
        return keys.Any(k => k is null) ? "" : $"::<{string.Join(", ", keys)}>";
    }

    static string RawIdent(string name) => name is "type" or "match" or "move" or "loop" or "ref" or "in" or "box" or "use" or "fn" or "impl" or "trait" or "where" or "async" or "await" or "dyn" or "gen" ? "r#" + name : name;

    bool IsSelf(Doc doc, JsonElement output, TypeInfo owner)
    {
        var t = output.TryGetProperty("borrowed_ref", out var r) ? r.GetProperty("type") : output;
        if (t.TryGetProperty("generic", out var g))
        {
            return g.GetString() == "Self";
        }
        return t.TryGetProperty("resolved_path", out var rp) && doc.Canon(rp.GetProperty("id").GetInt32()) == owner.Base;
    }

    (string Write, string Value) Outs(Flat f, string v, List<string> ffi, List<string> csFfi, List<(string, string)> outs)
    {
        var w = new StringBuilder();
        var values = new List<string>();
        for (int i = 0; i < f.Items.Count; i++)
        {
            var access = f.Array ? $"{v}[{i}]" : $"{v}.{i}";
            var (write, value) = OutOne(f.Items[i], access, $"o{i}", ffi, csFfi, outs);
            w.Append(write).Append(' ');
            values.Add(value);
        }
        return (w.ToString(), "(" + string.Join(", ", values) + ")");
    }

    (string Write, string Value) OutOne(Ty ty, string v, string o, List<string> ffi, List<string> csFfi, List<(string, string)> outs)
    {
        if (ty is Str os)
        {
            ffi.Add($"{o}: *mut Utf8");
            csFfi.Add($"Utf8Ref* {o}");
            outs.Add(("Utf8Ref", o));
            return ($"*{o} = ret_str({StrView(os, v)});", $"Interop.FromUtf8({o})");
        }
        var (rt, ct, conv, wrap) = Scalar(ty);
        ffi.Add($"{o}: *mut {rt}");
        csFfi.Add($"{ct}* {o}");
        outs.Add((ct, o));
        return ($"*{o} = {conv(v)};", wrap(o));
    }

    void CsArg(Ty ty, string n, string value, StringBuilder pre, List<string> fixes, List<string> args)
    {
        var tmp = n.TrimStart('@') + "__" + args.Count;
        switch (ty)
        {
            case Hashed:
                pre.Append($"var {tmp}k = {value}; ");
                fixes.Add($"fixed (byte* {tmp}p = {tmp}k.Bytes)");
                args.Add($"{tmp}k.Kind");
                args.Add($"{tmp}k.Number");
                args.Add($"{tmp}p");
                args.Add($"(nuint)({tmp}k.Bytes?.Length ?? 0)");
                break;
            case Str { Shown: true }:
                pre.Append($"byte[] {tmp}b = Interop.Shown({value}); ");
                fixes.Add($"fixed (byte* {tmp}p = {tmp}b)");
                args.Add($"{tmp}p");
                args.Add($"(nuint){tmp}b.Length");
                break;
            case Str:
                pre.Append($"byte[] {tmp}b = Interop.Utf8({value}); ");
                fixes.Add($"fixed (byte* {tmp}p = {tmp}b)");
                args.Add($"{tmp}p");
                args.Add($"(nuint){tmp}b.Length");
                break;
            case Ref r:
                CsArg(r.Inner, n, value, pre, fixes, args);
                break;
            case Value { T.Kind: Kind.Interned }:
                args.Add($"{value}.P");
                break;
            case Element:
                args.Add($"{value}.Id");
                break;
            case Handle:
                args.Add($"{value}.P");
                break;
            case Obj { T.CopyObject: true }:
                args.Add($"{value}.P");
                break;
            case Obj:
                args.Add($"{value}.O");
                break;
            case HostVal or Managed:
                args.Add($"Interop.Keep({value})");
                break;
            case Seq { Item: Obj or Handle } seq:
                // Handles are wider than the pointers Rust reads, so a span of them can't be pinned.
                pre.Append($"var {tmp}h = new nint[{value}.Length]; for (int {tmp}i = 0; {tmp}i < {tmp}h.Length; {tmp}i++) {tmp}h[{tmp}i] = {value}[{tmp}i].{(seq.Item is Obj ? "O" : "P")}; ");
                fixes.Add($"fixed (nint* {tmp}p = {tmp}h)");
                args.Add($"{tmp}p");
                args.Add($"(nuint){value}.Length");
                break;
            case Seq { Item: FnPtr item }:
                // Each delegate takes a slot, and Rust gets the slot numbers.
                var slot = new List<string>();
                CsArg(item, n, $"{tmp}f", new StringBuilder(), fixes, slot);
                pre.Append($"var {tmp}h = new uint[{value}.Length]; for (int {tmp}i = 0; {tmp}i < {tmp}h.Length; {tmp}i++) {{ var {tmp}f = {value}[{tmp}i]; {tmp}h[{tmp}i] = {slot[0]}; }} ");
                fixes.Add($"fixed (uint* {tmp}p = {tmp}h)");
                args.Add($"{tmp}p");
                args.Add($"(nuint){value}.Length");
                break;
            case Seq s:
                fixes.Add($"fixed ({SeqLeaf(s.Item).Cs}* {tmp}p = {value})");
                args.Add($"{tmp}p");
                args.Add($"(nuint){value}.Length");
                break;
            case Wire w:
                CsWire(w, n, value, pre, fixes, args);
                break;
            case FnPtr f:
                var inner = new List<string>();
                var build = new StringBuilder();
                CsArg(f.C, n, value, build, fixes, inner);
                var pool = FnPool(f.C);
                args.Add($"Interop.Slot({pool[3..]}, {value}, () => {{ {build}return {inner[0]}; }}, static (i, h) => N.rune_gen_{pool}_set(i, h))");
                break;
            case Closure c:
                var erased = c.Delegate("object");
                string keep = value;
                if (c.UsesState)
                {
                    var ps = c.Args.Select((_, i) => $"x{i}").ToList();
                    var cast = c.Args.Select((a, i) => a is State ? $"(S)x{i}" : $"x{i}");
                    pre.Append($"{erased} {tmp}e = ({string.Join(", ", ps)}) => {value}({string.Join(", ", cast)}); ");
                    keep = $"{tmp}e";
                }
                args.Add($"Interop.Keep({keep})");
                break;
            case Flat f:
                for (int i = 0; i < f.Items.Count; i++)
                {
                    CsArg(f.Items[i], n, $"{value}.Item{i + 1}", pre, fixes, args);
                }
                break;
            case Opt { Inner: Managed }:
                args.Add($"{value} is not null");
                args.Add($"{value} is null ? 0 : Interop.Keep({value})");
                break;
            case Opt { Inner: Str } o:
                args.Add($"{value} != null");
                CsArg(o.Inner, n, $"({value} ?? \"\")", pre, fixes, args);
                break;
            case Opt o:
                args.Add($"{value}.HasValue");
                CsArg(o.Inner, n, $"{value}.GetValueOrDefault()", pre, fixes, args);
                break;
            default:
                args.Add(value);
                break;
        }
    }

    // Output

    void Finish(List<TypeInfo> types)
    {
        var rust = new StringBuilder("// Generated by tools/Rune.BindGen from rustdoc JSON. Do not edit.\nuse super::*;\nuse std::ffi::c_void;\nuse std::sync::OnceLock;\n\n");
        rust.Append(_rs);
        Rust = rust.ToString();

        var cs = new StringBuilder("// Generated by tools/Rune.BindGen from rustdoc JSON. Do not edit.\n#nullable enable\n#pragma warning disable CS0108, CS0114, CS8981\nusing System.Runtime.InteropServices;\n\nnamespace Rune;\n\n");
        var written = new HashSet<string>();
        foreach (var t in types)
        {
            var members = _members.GetValueOrDefault(t.CsName) ?? [];
            written.Add(t.CsName);
            switch (t.Kind)
            {
                case Kind.Enum:
                    cs.AppendLine($"public enum {t.CsName} {{ {string.Join(", ", t.Variants.Select(v => v.Cs))} }}");
                    break;
                case Kind.Record:
                    cs.AppendLine($"[StructLayout(LayoutKind.Sequential)] public unsafe partial record struct {t.CsName}({string.Join(", ", t.Fields.Select(f => $"{f.Ty.CsType} {f.Cs}"))})");
                    cs.AppendLine("{").AppendJoin('\n', members).AppendLine("\n}");
                    break;
                case Kind.Interned:
                    // Records hold these by value, so they must stay the size of a pointer.
                    cs.AppendLine($"public readonly unsafe partial struct {t.CsName}\n{{\n    readonly nint _p;\n    internal nint P => Interop.Check(_p, 0);\n    internal {t.CsName}(nint p) => _p = p;");
                    cs.AppendJoin('\n', members).AppendLine("\n}");
                    break;
                case Kind.Element:
                    cs.AppendLine($"public readonly unsafe partial struct {t.CsName}\n{{").AppendJoin('\n', members).AppendLine("\n}");
                    break;
                default:
                    if (t.Kind != Kind.Handle)
                    {
                        break;
                    }
                    cs.AppendLine(HandleHead(t.CsName));
                    cs.AppendJoin('\n', members).AppendLine("\n}");
                    break;
            }
        }
        WriteSynth(cs, written);
        foreach (var (cls, members) in _members.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (written.Contains(cls))
            {
                continue;
            }
            cs.AppendLine($"public static unsafe partial class {cls}\n{{").AppendJoin('\n', members).AppendLine("\n}");
        }
        cs.AppendLine("internal static unsafe class Callbacks\n{\n    public static void Register()\n    {").Append(_register).AppendLine("    }\n").Append(_trampolines).AppendLine("}");
        cs.AppendLine("internal static unsafe partial class N\n{\n    private const string Lib = \"rune_capi\";").Append(_native).AppendLine("}");
        CSharp = cs.ToString();

        var adapters = ApplyCovers();
        int total = _bound + _skipped.Count;
        var report = new StringBuilder($"# Binding coverage\n\nGenerated by tools/Rune.BindGen from the macOS API, which contains the Linux and Windows API.\n\n- Bound: {_bound}\n- Skipped: {_skipped.Count}\n- Coverage: {100.0 * _bound / total:F1}%\n\n## Skip reasons\n\n");
        foreach (var g in _skipped.GroupBy(s => s.Reason).OrderByDescending(g => g.Count()))
        {
            report.AppendLine($"- {g.Count()}: {g.Key}");
        }
        report.AppendLine("\n## Skipped APIs\n");
        foreach (var (a, r) in _skipped.OrderBy(s => s.Api, StringComparer.Ordinal))
        {
            report.AppendLine($"- `{a}`: {r}");
        }
        report.AppendLine($"\n## Bound through rune-ext\n");
        foreach (var (a, f) in adapters.OrderBy(s => s.Api, StringComparer.Ordinal))
        {
            report.AppendLine($"- `{a}`: `{f}`");
        }
        Report = report.ToString();
        Summary = $"bound {_bound} of {total} ({100.0 * _bound / total:F1}%), {_shapes.Count} closure shapes, {_types.Values.Count(t => t.Kind == Kind.Handle)} object types";
    }
}
