using System.Text.RegularExpressions;

namespace Rune.BindGen;

/// <summary>Joins the Linux and macOS bindings. Rune's macOS API is a superset, and Windows matches Linux, so
/// the macOS output is the base and whatever Linux lacks is gated. Counters differ between the two runs,
/// so this compares exported symbols rather than lines.</summary>
static partial class Platforms
{
    const string RustGate = "#[cfg(target_os = \"macos\")] ";
    const string CsGate = "[global::System.Runtime.Versioning.SupportedOSPlatform(\"macos\")] ";

    [GeneratedRegex(@"\bfn (rune_gen_\w+)\(")] private static partial Regex RustSymbol();
    [GeneratedRegex(@"\bN\.(rune_gen_\w+)\(")] private static partial Regex CsCall();

    public static string[] MacOnly(string linuxRust, string macRust)
    {
        var linux = Symbols(linuxRust);
        var mac = Symbols(macRust);
        var missing = linux.Except(mac).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"macOS is no longer a superset of Linux: {string.Join(", ", missing)}");
        }
        return [.. mac.Except(linux).Order(StringComparer.Ordinal)];
    }

    static HashSet<string> Symbols(string rust) => [.. RustSymbol().Matches(rust).Select(m => m.Groups[1].Value)];

    public static string GateRust(string rust, IReadOnlySet<string> macOnly) => string.Join('\n', rust.Split('\n').Select(line =>
        RustSymbol().Match(line) is { Success: true } m && macOnly.Contains(m.Groups[1].Value) ? RustGate + line : line));

    /// <summary>Marks each member calling a macOS symbol, and each type with such a `Free`, for the platform analyzer.</summary>
    public static string GateCSharp(string cs, IReadOnlySet<string> macOnly)
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
            lines[i] = lines[i][..indent] + CsGate + lines[i][indent..];
        }
        return string.Join('\n', lines);
    }
}
