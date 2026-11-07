// What both tools check before they start: rune/ is at the commit RUNE_COMMIT pins, and every
// package carries the same version.
using System.Diagnostics;
using System.Text.RegularExpressions;

static partial class Pins
{
    /// <summary>Null when everything agrees, otherwise what to fix.</summary>
    public static string? Problem()
    {
        var pinned = File.ReadAllText("RUNE_COMMIT").Trim();
        var head = Git("-C", "rune", "rev-parse", "HEAD");
        if (head is null)
        {
            return "rune/ is missing or not a git clone: clone https://github.com/anomalyco/rune there";
        }
        if (head != pinned)
        {
            return $"rune/ is at {head[..12]}, but RUNE_COMMIT pins {pinned[..12]}: run `git -C rune checkout {pinned}`, or update RUNE_COMMIT";
        }

        var versions = new Dictionary<string, string?>
        {
            ["src/Rune/Rune.csproj"] = Match(CsprojVersion(), "src/Rune/Rune.csproj"),
            ["native/rune-capi/Cargo.toml"] = Match(CargoVersion(), "native/rune-capi/Cargo.toml"),
            ["native/rune-ext/Cargo.toml"] = Match(CargoVersion(), "native/rune-ext/Cargo.toml"),
        };
        if (versions.Values.Distinct().Count() != 1)
        {
            return "versions differ: " + string.Join(", ", versions.Select(v => $"{v.Key} has {v.Value ?? "none"}"));
        }
        return null;
    }

    static string? Match(Regex regex, string path) => regex.Match(File.ReadAllText(path)) is { Success: true } m ? m.Groups[1].Value : null;

    static string? Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using var git = Process.Start(start)!;
            var output = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
            return git.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [GeneratedRegex(@"<Version>([^<]+)</Version>")]
    private static partial Regex CsprojVersion();

    [GeneratedRegex(@"(?m)^version\s*=\s*""([^""]+)""")]
    private static partial Regex CargoVersion();
}
