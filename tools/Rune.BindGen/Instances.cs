using System.Text.Json;

namespace Rune.BindGen;

/// <summary>Turns generic types into concrete ones. State parameters collapse to `Host`, data
/// containers hold a managed object, and animatable types get one instantiation per value type.</summary>
sealed partial class Generator
{
    const string HostKey = "Host", ManagedKey = "Managed", FileNodeKey = "rune_kit::tree::file_tree::FileNode";
    // Unbounded in Rust, but their methods need `Animatable`.
    static readonly HashSet<string> AnimatableContainers = ["Prop", "Keyframes", "Keyframe", "Release"];
    static readonly HashSet<(string, string)> OpaqueParams = [("TreeModel", "T"), ("FileNode", "D"), ("Latest", "T"), ("Tree", "T")];

    static readonly System.Text.RegularExpressions.Regex ContainerArg = new(@"""path"":\s*""[^""]*\b(Prop|Keyframes|Keyframe|Release)""");

    readonly Dictionary<string, List<TypeInfo>> _instances = [];
    readonly Dictionary<string, List<string>> _implementers = [];
    Dictionary<string, string>? _implSubst;

    static IEnumerable<JsonElement> TypeParams(JsonElement generics) =>
        generics.GetProperty("params").EnumerateArray().Where(p => p.GetProperty("kind").TryGetProperty("type", out _));

    static IEnumerable<string> BoundNames(JsonElement generics, JsonElement param)
    {
        var name = param.GetProperty("name").GetString();
        var bounds = param.GetProperty("kind").GetProperty("type").GetProperty("bounds").EnumerateArray().ToList();
        foreach (var w in generics.GetProperty("where_predicates").EnumerateArray())
        {
            if (w.TryGetProperty("bound_predicate", out var bp) && bp.GetProperty("type").TryGetProperty("generic", out var g) && g.GetString() == name)
            {
                bounds.AddRange(bp.GetProperty("bounds").EnumerateArray());
            }
        }
        foreach (var b in bounds)
        {
            if (b.TryGetProperty("trait_bound", out var tb))
            {
                yield return tb.GetProperty("trait").GetProperty("path").GetString()!.Split("::")[^1];
            }
        }
    }

    void FindImplementers()
    {
        _implementers["Animatable"] = ["f32"];
        _implementers["LiveValue"] = ["f32", "[f32; 2]", "[f32; 4]"];
        foreach (var t in _types.Values)
        {
            var inner = t.Item.GetProperty("inner");
            var data = inner.TryGetProperty("struct", out var s) ? s : inner.GetProperty("enum");
            if (data.GetProperty("generics").GetProperty("params").GetArrayLength() != 0)
            {
                continue;
            }
            foreach (var id in data.GetProperty("impls").EnumerateArray())
            {
                if (t.Doc.Item(id.GetInt32()) is not { } item)
                {
                    continue;
                }
                var tr = item.GetProperty("inner").GetProperty("impl").GetProperty("trait");
                if (tr.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var name = tr.GetProperty("path").GetString()!.Split("::")[^1];
                if (_implementers.TryGetValue(name, out var list))
                {
                    list.Add(t.Canon);
                }
            }
        }
    }

    List<string>? Candidates(TypeInfo t, JsonElement generics, JsonElement param)
    {
        var name = param.GetProperty("name").GetString()!;
        var bounds = BoundNames(generics, param).ToList();
        if (bounds.Contains("Animatable") || AnimatableContainers.Contains(t.Short))
        {
            return _implementers["Animatable"];
        }
        if (bounds.Contains("LiveValue"))
        {
            return _implementers["LiveValue"];
        }
        if (t.Short == "TreeModel")
        {
            return [ManagedKey, FileNodeKey];
        }
        if (OpaqueParams.Contains((t.Short, name)))
        {
            return [ManagedKey];
        }
        return bounds.All(b => b is "Send" or "Sync") ? [HostKey] : null;
    }

    string RustOfKey(string key) => key switch
    {
        HostKey => "Host",
        ManagedKey => "ManagedFn",
        FileNodeKey => "rune_kit::FileNode<ManagedFn>",
        _ => _types.TryGetValue(key, out var t) ? t.RustPath : key,
    };

    void Instantiate()
    {
        FindImplementers();
        foreach (var t in _types.Values.ToList())
        {
            var inner = t.Item.GetProperty("inner");
            var generics = (inner.TryGetProperty("struct", out var s) ? s : inner.GetProperty("enum")).GetProperty("generics");
            var all = generics.GetProperty("params").EnumerateArray().ToList();
            if (all.Count == 0)
            {
                continue;
            }
            if (all.Any(p => p.GetProperty("kind").TryGetProperty("const", out _)))
            {
                continue;
            }
            var ps = TypeParams(generics).ToList();
            var choices = ps.Select(p => Candidates(t, generics, p)).ToList();
            if (choices.Any(c => c is null))
            {
                continue;
            }

            var combos = new List<List<string>> { new() };
            foreach (var c in choices)
            {
                combos = [.. combos.SelectMany(prefix => c!.Select(k => prefix.Append(k).ToList()))];
            }
            var list = _instances[t.Canon] = [];
            _types.Remove(t.Canon);
            foreach (var combo in combos)
            {
                var canon = combo.Count == 0 ? t.Canon : $"{t.Canon}<{string.Join(",", combo)}>";
                var inst = new TypeInfo(canon, t.Doc, t.Item, t.RustPath) { Base = t.Canon, Instantiated = true, Lifetimes = all.Count - ps.Count };
                inst.Args = [.. ps.Select((p, i) => (p.GetProperty("name").GetString()!, combo[i], RustOfKey(combo[i])))];
                _types[canon] = inst;
                list.Add(inst);
            }
        }
    }

    /// <summary>The instantiation key `name` stands for here, if any.</summary>
    static string KeyName(string key) => key.StartsWith('[') ? "F32x" + key[^2] : Names.Pascal(key.Split("::")[^1]);

    string? KeyOf(Ctx ctx, string name)
    {
        if (ctx.Subst is { } subst && subst.TryGetValue(name, out var key))
        {
            return key;
        }
        if (ctx.Self is { } self)
        {
            foreach (var a in self.Args)
            {
                if (a.Param == name)
                {
                    return a.Key;
                }
            }
        }
        return null;
    }

    string? ArgKey(Doc doc, JsonElement arg, Ctx ctx)
    {
        if (arg.TryGetProperty("generic", out var g))
        {
            return KeyOf(ctx, g.GetString()!) ?? (g.GetString() == "S" ? HostKey : null);
        }
        if (arg.TryGetProperty("primitive", out var p))
        {
            return p.GetString();
        }
        if (arg.TryGetProperty("array", out var ar) && ar.GetProperty("type").TryGetProperty("primitive", out var item))
        {
            return $"[{item.GetString()}; {ar.GetProperty("len").GetString()}]";
        }
        if (arg.TryGetProperty("resolved_path", out var rp) && rp.GetProperty("path").GetString()!.EndsWith("ManagedFn"))
        {
            return ManagedKey;
        }
        if (arg.TryGetProperty("resolved_path", out rp))
        {
            return doc.Canon(rp.GetProperty("id").GetInt32());
        }
        return null;
    }

    TypeInfo? Lookup(Doc doc, JsonElement rp, Ctx ctx)
    {
        var canon = doc.Canon(rp.GetProperty("id").GetInt32());
        if (canon is null)
        {
            return null;
        }
        if (_types.TryGetValue(canon, out var t) && !_instances.ContainsKey(canon))
        {
            return t;
        }
        if (!_instances.TryGetValue(canon, out var list))
        {
            return null;
        }
        var args = TypeArgs(rp).ToList();
        if (args.Count == 0 && list.Count == 1)
        {
            return list[0];
        }
        var keys = args.Select(a => ArgKey(doc, a, ctx)).ToList();
        foreach (var inst in list)
        {
            if (inst.Args.Count == keys.Count && inst.Args.Select(a => a.Key).SequenceEqual(keys!))
            {
                return inst;
            }
        }
        throw new Skip($"generic type {list[0].Short}");
    }

    Ty TyOf(string key, bool isRef, bool mutable, bool inClosure) => key switch
    {
        HostKey => inClosure && isRef ? new State(mutable) : new HostVal(isRef),
        ManagedKey => new Managed(isRef),
        FileNodeKey => isRef ? RefOf(_instances[FileNodeKey][0], mutable, inClosure) : FromType(_instances[FileNodeKey][0], inClosure),
        _ when _types.TryGetValue(key, out var t) => isRef ? RefOf(t, mutable, inClosure) : FromType(t, inClosure),
        "[f32; 2]" => Flatten([Primitive("f32"), Primitive("f32")], true),
        "[f32; 4]" => Flatten([Primitive("f32"), Primitive("f32"), Primitive("f32"), Primitive("f32")], true),
        _ => isRef ? new Ref(Primitive(key)) : Primitive(key),
    };

    /// <summary>Sets up `impl` for this instantiation; false when it applies to other ones only.</summary>
    bool EnterImpl(TypeInfo t, JsonElement impl)
    {
        _implSubst = null;
        if (t.Args.Count == 0)
        {
            return true;
        }
        var target = impl.GetProperty("for");
        if (!target.TryGetProperty("resolved_path", out var rp))
        {
            return false;
        }
        var args = TypeArgs(rp).ToList();
        if (args.Count != t.Args.Count)
        {
            return false;
        }
        var subst = new Dictionary<string, string>();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].TryGetProperty("generic", out var g))
            {
                subst[g.GetString()!] = t.Args[i].Key;
            }
            else if (ArgKey(t.Doc, args[i], new Ctx(null, null)) != t.Args[i].Key)
            {
                return false;
            }
        }
        _implSubst = subst;
        return true;
    }

    /// <summary>Methods generic over a value type get one binding per type: `AnimateF32`, `AnimateColor`.</summary>
    IEnumerable<(Dictionary<string, string>? Subst, string Suffix)> MethodInstances(JsonElement fn)
    {
        var generics = fn.GetProperty("generics");
        var fixedArgs = new Dictionary<string, string>(_implSubst ?? []);
        (string Name, string Trait)? varying = null;
        foreach (var p in TypeParams(generics))
        {
            if (p.GetProperty("kind").GetProperty("type").GetProperty("is_synthetic").GetBoolean())
            {
                continue;
            }
            var name = p.GetProperty("name").GetString()!;
            var bounds = BoundNames(generics, p).ToList();
            var trait = bounds.FirstOrDefault(b => b is "Animatable" or "LiveValue");
            if (trait is null && ContainerArg.IsMatch(fn.GetRawText()))
            {
                trait = "Animatable";
            }
            if (trait is not null)
            {
                varying ??= (name, trait);
            }
            else if (bounds.Count == 0)
            {
                fixedArgs[name] = HostKey;
            }
        }
        if (varying is not { } v)
        {
            yield return (fixedArgs, "");
            yield break;
        }
        foreach (var key in _implementers[v.Trait])
        {
            var subst = new Dictionary<string, string>(fixedArgs) { [v.Name] = key };
            yield return (subst, _types.TryGetValue(key, out var t) ? t.CsName : KeyName(key));
        }
    }
}
