using System.Text.Json;

namespace Rune.BindGen;

/// <summary>One rustdoc JSON file.</summary>
sealed record Doc(string Crate, JsonElement Index, JsonElement Paths, int Root)
{
    public static Doc Load(string path)
    {
        var root = JsonDocument.Parse(File.ReadAllBytes(path)).RootElement;
        return new(Path.GetFileNameWithoutExtension(path), root.GetProperty("index"), root.GetProperty("paths"), root.GetProperty("root").GetInt32());
    }

    public JsonElement? Item(int id) => Index.TryGetProperty(id.ToString(), out var item) ? item : null;

    public string? Canon(int id) =>
        Paths.TryGetProperty(id.ToString(), out var p) ? string.Join("::", p.GetProperty("path").EnumerateArray().Select(s => s.GetString())) : null;
}

/// <summary>Thrown while mapping a signature; the message is the reason in the report.</summary>
sealed class Skip(string reason) : Exception(reason);

enum Kind { Unclassified, InProgress, Unsupported, Record, Enum, Handle, Interned, Element }

sealed class TypeInfo(string canon, Doc doc, JsonElement item, string rustPath)
{
    public string Canon = canon;
    public Doc Doc = doc;
    public JsonElement Item = item;
    public string RustPath = rustPath;
    public string CsName = "";
    public Kind Kind;
    public List<(string Rust, string Cs, Ty Ty)> Fields = [];
    public List<(string Rust, string Cs)> Variants = [];

    /// <summary>The concrete arguments of a generic type: Rune's generics have no C form, so each instantiation is its own type.</summary>
    public List<(string Param, string Key, string Rust)> Args = [];
    public int Lifetimes;
    public bool Instantiated;
    /// <summary>An opaque `Copy` type: passing one by value leaves the object usable.</summary>
    public bool CopyObject;
    public string Base = canon;
    public string Short => RustPath.Split("::")[^1];
    public string Sym => CsName.ToLowerInvariant();
    public string RustType => !Instantiated ? RustPath : $"{RustPath}<{RustArgs}>";
    /// <summary>The type in expression position, its arguments spelled out (`ToastEvent::<ManagedFn>`),
    /// so a variant built from it never leaves them to inference.</summary>
    public string RustExprPath => !Instantiated ? RustPath : $"{RustPath}::<{RustArgs}>";
    string RustArgs => string.Join(", ", Enumerable.Repeat("'_", Lifetimes).Concat(Args.Select(a => a.Rust)));
    public string Cs => "global::Rune." + CsName;
}

/// <summary>One FFI-level value: its Rust type and its C# type.</summary>
readonly record struct Leaf(string Rust, string Cs);

/// <summary>How a Rust type crosses the boundary.</summary>
abstract record Ty
{
    public abstract string CsType { get; }
}

sealed record Prim(string Rust, string Cs) : Ty { public override string CsType => Cs; }
enum StrMode { Owned, Borrowed, Static, PathBuf, PathRef }
sealed record Str(StrMode Mode, bool Shown = false) : Ty { public override string CsType => Shown ? "object" : "string"; }   // Shown: `impl Display`
sealed record Hashed : Ty { public override string CsType => "global::Rune.HashKey"; }                                         // `impl Hash`
sealed record Value(TypeInfo T) : Ty { public override string CsType => T.Cs; }       // record, enum or interned
sealed record Ref(Ty Inner) : Ty { public override string CsType => Inner.CsType; }   // `&T` of a value
sealed record Element : Ty { public override string CsType => "global::Rune.El"; }
sealed record Obj(TypeInfo T) : Ty { public override string CsType => T.Cs; }             // owned by value
enum SeqMode { Vec, Slice, StaticSlice }
sealed record Seq(Ty Item, SeqMode Mode) : Ty
{
    public override string CsType => $"global::System.ReadOnlySpan<{Item.CsType}>";
}
sealed record Handle(TypeInfo T, bool Mut) : Ty { public override string CsType => T.Cs; }
sealed record Flat(List<Ty> Items, bool Array) : Ty
{
    public override string CsType => "(" + string.Join(", ", Items.Select(i => i.CsType)) + ")";
}
sealed record Opt(Ty Inner) : Ty
{
    public override string CsType => Inner.CsType + "?";
}
sealed record State(bool Mut) : Ty { public override string CsType => "S"; }
/// <summary>The app state outside a closure, by value or by reference.</summary>
sealed record HostVal(bool Ref) : Ty { public override string CsType => "S"; }
/// <summary>Opaque data a generic container holds for C#.</summary>
sealed record Managed(bool Ref) : Ty { public override string CsType => "object"; }
/// <summary>A bare `fn`, which can't capture: C# delegates bind to a fixed pool of Rust functions.</summary>
sealed record FnPtr(Closure C) : Ty { public override string CsType => C.CsType; }
sealed record Closure(List<Ty> Args, Ty? Ret) : Ty
{
    public bool UsesState => Ret is HostVal || Args.Any(a => a is State);
    public override string CsType => Delegate("S");
    public string Delegate(string state)
    {
        var args = Args.Select(a => a is State ? state : a.CsType).ToList();
        if (Ret is null)
        {
            return args.Count == 0 ? "global::System.Action" : $"global::System.Action<{string.Join(", ", args)}>";
        }
        args.Add(Ret is HostVal ? state : Ret.CsType);
        return $"global::System.Func<{string.Join(", ", args)}>";
    }
}
