using System.Text;
using System.Text.RegularExpressions;

namespace Rune.BindGen;

/// <summary>
/// Wraps every export so a Rust panic, or a C# exception from a callback inside it, comes back to C#
/// as an exception instead of aborting: the Rust body runs in `guard`, and each import is called
/// through a wrapper that throws what the export recorded. Callback registration is left bare.
/// </summary>
static partial class Guards
{
    [GeneratedRegex(@"^(?<head>(?:#\[cfg\([^\]]*\)\] )?#\[unsafe\(no_mangle\)\] pub (?<unsafe>unsafe )?extern ""C"" fn (?<name>\w+)\()")]
    private static partial Regex RustExport();

    [GeneratedRegex(@"^    \[DllImport\(Lib\)\] public static extern (?<ret>.+?) (?<name>\w+)\((?<params>.*)\);$")]
    private static partial Regex Import();

    static bool Bare(string name) => name.StartsWith("rune_gen_cb_", StringComparison.Ordinal);

    public static string Rust(string rust)
    {
        var sb = new StringBuilder();
        foreach (var line in rust.Split('\n'))
        {
            var m = RustExport().Match(line);
            if (!m.Success || Bare(m.Groups["name"].Value))
            {
                sb.Append(line).Append('\n');
                continue;
            }
            // The body opens at the first brace after the parameter list, whose types can hold parentheses.
            int depth = 1, i = m.Length;
            for (; depth > 0; i++)
            {
                depth += line[i] switch { '(' => 1, ')' => -1, _ => 0 };
            }
            int open = line.IndexOf(" { ", i, StringComparison.Ordinal);
            if (open < 0 || !line.EndsWith(" }", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"export not on one line: {m.Groups["name"].Value}");
            }
            var body = line[(open + 3)..^2];
            var block = m.Groups["unsafe"].Success ? "unsafe " : "";
            sb.Append(line[..open]).Append(" { guard(|| ").Append(block).Append("{ ").Append(body).Append(" }) }\n");
        }
        return sb.ToString(0, sb.Length - 1);
    }

    public static string CSharp(string cs)
    {
        var sb = new StringBuilder();
        foreach (var line in cs.Split('\n'))
        {
            var m = Import().Match(line);
            if (!m.Success || Bare(m.Groups["name"].Value))
            {
                sb.Append(line).Append('\n');
                continue;
            }
            string ret = m.Groups["ret"].Value, name = m.Groups["name"].Value, ps = m.Groups["params"].Value;
            var args = string.Join(", ", Split(ps).Select(p => p[(p.LastIndexOf(' ') + 1)..]));
            sb.Append($"    [DllImport(Lib, EntryPoint = \"{name}\")] static extern {ret} {name}__({ps});\n");
            sb.Append(ret == "void"
                ? $"    public static void {name}({ps}) {{ {name}__({args}); Interop.Rethrow(); }}\n"
                : $"    public static {ret} {name}({ps}) {{ var r = {name}__({args}); Interop.Rethrow(); return r; }}\n");
        }
        return sb.ToString(0, sb.Length - 1);
    }

    /// <summary>Splits a parameter list at top-level commas: `delegate* unmanaged&lt;a, b&gt;` is one type.</summary>
    static IEnumerable<string> Split(string ps)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < ps.Length; i++)
        {
            depth += ps[i] switch { '<' or '(' => 1, '>' or ')' => -1, _ => 0 };
            if (ps[i] == ',' && depth == 0)
            {
                yield return ps[start..i].Trim();
                start = i + 1;
            }
        }
        if (ps.Trim().Length > 0)
        {
            yield return ps[start..].Trim();
        }
    }
}
