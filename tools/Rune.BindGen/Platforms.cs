using System.Text;
using System.Text.RegularExpressions;

namespace Rune.BindGen;

/// <summary>Joins the Linux and macOS bindings. Windows matches Linux. The macOS output is the base, with what
/// Linux lacks gated to macOS, and what only Linux has (dmabuf import) goes in a second C# file and at the end
/// of the Rust one, gated to Linux. Counters differ between the two runs, so this compares exported symbols
/// rather than lines.</summary>
static partial class Platforms
{
    static string RustGate(string os) => $"#[cfg(target_os = \"{os}\")] ";
    static string CsGate(string os) => $"[global::System.Runtime.Versioning.SupportedOSPlatform(\"{os}\")] ";

    [GeneratedRegex(@"\bfn (rune_gen_\w+)\(")] private static partial Regex RustSymbol();
    [GeneratedRegex(@"\bN\.(rune_gen_\w+)\(")] private static partial Regex CsCall();

    /// <summary>The symbols one platform has and the other lacks.</summary>
    public static (string[] MacOnly, string[] LinuxOnly) Only(string linuxRust, string macRust)
    {
        var linux = Symbols(linuxRust);
        var mac = Symbols(macRust);
        return ([.. mac.Except(linux).Order(StringComparer.Ordinal)], [.. linux.Except(mac).Order(StringComparer.Ordinal)]);
    }

    static HashSet<string> Symbols(string rust) => [.. RustSymbol().Matches(rust).Select(m => m.Groups[1].Value)];

    public static string GateRust(string rust, IReadOnlySet<string> only, string os) => string.Join('\n', rust.Split('\n').Select(line =>
        RustSymbol().Match(line) is { Success: true } m && only.Contains(m.Groups[1].Value) ? RustGate(os) + line : line));

    /// <summary>The exports only the Linux build has, to follow the macOS ones.</summary>
    public static string LinuxRust(string linuxRust, IReadOnlySet<string> linuxOnly) =>
        "\n" + string.Join('\n', linuxRust.Split('\n').Where(line => RustSymbol().Match(line) is { Success: true } m && linuxOnly.Contains(m.Groups[1].Value)).Select(line => RustGate("linux") + line));

    [GeneratedRegex(@"\brune_gen_\w+")] private static partial Regex AnySymbol();

    /// <summary>What only the Linux build has, as partial types beside the macOS file's: whole types Linux alone
    /// declares, and the members, constants and imports of shared ones that call a Linux-only symbol.</summary>
    public static string LinuxCSharp(string linuxCs, string macCs, IReadOnlySet<string> linuxOnly)
    {
        var lines = linuxCs.Split('\n');
        var known = macCs.Split('\n').ToHashSet();
        var sb = new StringBuilder();
        int start = Array.FindIndex(lines, l => l.StartsWith("namespace "));
        for (int i = 0; i <= start; i++)
        {
            sb.Append(lines[i]).Append('\n');
        }
        for (int i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line[0] is ' ' or '{' or '}')
            {
                continue;
            }
            if (i + 1 < lines.Length && lines[i + 1] == "{")
            {
                int end = i + 2;
                while (lines[end] != "}")
                {
                    end++;
                }
                if (line.Contains("class Callbacks"))
                {
                    var ids = linuxOnly.Select(x => Regex.Match(x, @"^rune_gen_cb_(\d+)$")).Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet();
                    var regs = lines[(i + 2)..end].Where(l => ids.Any(id => l.Contains($"N.rune_gen_cb_{id}(")));
                    var tramps = lines[(i + 2)..end].Where(l => ids.Any(id => l.Contains($" Cb{id}(")));
                    if (ids.Count > 0)
                    {
                        sb.Append("\ninternal static unsafe partial class Callbacks\n{\n    static partial void RegisterLinux()\n    {\n").AppendJoin('\n', regs).Append("\n    }\n\n").AppendJoin('\n', tramps).Append("\n}\n");
                    }
                    i = end;
                    continue;
                }
                var whole = !known.Contains(line);
                var body = lines[(i + 2)..end].Where(l => whole || AnySymbol().Matches(l).Any(m => linuxOnly.Contains(m.Value.TrimEnd('_')))).ToList();
                if (body.Count > 0)
                {
                    sb.Append('\n').Append(line).Append("\n{\n").AppendJoin('\n', body).Append("\n}\n");
                }
                i = end;
            }
            else if (!known.Contains(line))
            {
                sb.Append(line).Append('\n');
            }
        }
        return GateCSharp(sb.ToString(0, sb.Length - 1), linuxOnly, "linux");
    }

    /// <summary>Marks each member calling a symbol of one platform, and each type with such a `Free`, for the platform analyzer.</summary>
    public static string GateCSharp(string cs, IReadOnlySet<string> macOnly, string os)
    {
        var lines = cs.Split('\n');
        int type = -1;
        var gated = new HashSet<int>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("public "))
            {
                type = i;
            }
            if (!lines[i].StartsWith("    public "))
            {
                continue;
            }
            var calls = CsCall().Matches(lines[i]).Select(m => m.Groups[1].Value).Where(macOnly.Contains).ToList();
            if (calls.Count == 0)
            {
                continue;
            }
            gated.Add(i);
            if (calls.Any(c => c.EndsWith("_free")))
            {
                gated.Add(type);
            }
        }
        foreach (var i in gated)
        {
            var indent = lines[i].Length - lines[i].TrimStart().Length;
            lines[i] = lines[i][..indent] + CsGate(os) + lines[i][indent..];
        }
        return string.Join('\n', lines);
    }
}
