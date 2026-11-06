using System.Text;

namespace Rune.BindGen;

static class Names
{
    static readonly HashSet<string> Keywords = ["abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"];

    public static string Pascal(string name)
    {
        var sb = new StringBuilder();
        foreach (var part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = part.All(c => !char.IsLower(c)) ? part.ToLowerInvariant() : part;
            sb.Append(char.ToUpperInvariant(lower[0])).Append(lower.AsSpan(1));
        }
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    /// <summary>A member name; C# forbids one equal to its enclosing type.</summary>
    public static string Member(string name, string owner)
    {
        var pascal = Pascal(name);
        return pascal == owner ? pascal + "_" : pascal;
    }

    public static string Param(string name) => Keywords.Contains(name) ? "@" + name : name;

    public static string Ident(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }
        return sb.ToString();
    }
}
