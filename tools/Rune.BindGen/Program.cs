// Generates the Rust C ABI and the C# bindings for Rune from rustdoc JSON.
// usage: dotnet run -c Release --project tools/Rune.BindGen
//        Rune.BindGen <out.rs> <out.cs> <report.md> <rune.json> [more crates...]   to reuse existing JSON
using System.Diagnostics;
using Rune.BindGen;

Environment.CurrentDirectory = RepositoryRoot();
if (args.Length is > 0 and < 4)
{
    Console.Error.WriteLine("usage: Rune.BindGen [<out.rs> <out.cs> <report.md> <rune.json> [more.json...]]");
    return 2;
}
if (args.Length > 0)
{
    var one = Generate(args[3..]);
    Write(args[0], args[1], args[2], one.Rust, one.CSharp, one.Report);
    Console.WriteLine(one.Summary);
    return Unbound(one);
}

// rustdoc drops other platforms' `cfg` items, so fixed targets keep the output the same on every host.
var linux = Generate(Rustdoc("x86_64-unknown-linux-gnu"));
var mac = Generate(Rustdoc("aarch64-apple-darwin"));
var macOnly = Platforms.MacOnly(linux.Rust.ReplaceLineEndings("\n"), mac.Rust.ReplaceLineEndings("\n"));
var gated = macOnly.ToHashSet();
var report = mac.Report.ReplaceLineEndings("\n") + $"\n## macOS only\n\n{macOnly.Length} exported functions, gated in Rust and marked `[SupportedOSPlatform(\"macos\")]` in C#:\n\n"
    + string.Concat(macOnly.Select(s => $"- `{s}`\n"));
Write("native/rune-capi/src/generated.rs", "src/Rune/Generated.cs", "native/rune-capi/COVERAGE.md",
    Platforms.GateRust(mac.Rust.ReplaceLineEndings("\n"), gated), Platforms.GateCSharp(mac.CSharp.ReplaceLineEndings("\n"), gated), report);
Console.WriteLine($"{mac.Summary}, {macOnly.Length} symbols macOS only");
return Math.Max(Unbound(linux), Unbound(mac));

// Output is still written, so the report shows the same list. Anything here needs a mapping or an adapter.
static int Unbound(Generator gen)
{
    foreach (var api in gen.Skipped)
    {
        Console.Error.WriteLine($"not bound: {api}");
    }
    return gen.Skipped.Any() ? 1 : 0;
}

static Generator Generate(IEnumerable<string> json)
{
    var gen = new Generator(json.Select(Doc.Load).ToList());
    gen.Run();
    return gen;
}

static void Write(string rustPath, string csPath, string reportPath, string rust, string cs, string report)
{
    File.WriteAllText(rustPath, rust.ReplaceLineEndings("\n"));
    File.WriteAllText(csPath, cs.ReplaceLineEndings("\n"));
    File.WriteAllText(reportPath, report.ReplaceLineEndings("\n"));
}

static string[] Rustdoc(string target)
{
    var doc = $"target/doc-json/{target}/doc";
    Cargo("rune", "target/doc-json", target, "-p", "rune", "-p", "rune-kit", "-p", "rune-motion");
    Cargo("native/rune-ext", "target/doc-json-ext", target, "-p", "rune-ext");
    File.Copy($"target/doc-json-ext/{target}/doc/rune_ext.json", $"{doc}/rune_ext.json", overwrite: true);
    return [$"{doc}/rune.json", $"{doc}/rune_motion.json", $"{doc}/rune_kit.json", $"{doc}/rune_ext.json"];
}

static string RepositoryRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "tools", "Rune.BindGen", "external-enums.txt")))
        {
            return dir.FullName;
        }
    }
    throw new InvalidOperationException("run this from inside the repository");
}

static void Cargo(string crateDir, string targetDir, string target, params string[] packages)
{
    var start = new ProcessStartInfo("cargo") { WorkingDirectory = crateDir };
    foreach (var arg in new[] { "doc" }.Concat(packages).Concat(["--no-deps", "--lib", "--quiet", "--target", target]))
    {
        start.ArgumentList.Add(arg);
    }
    start.Environment["CARGO_TARGET_DIR"] = Path.GetFullPath(targetDir);
    start.Environment["RUSTC_BOOTSTRAP"] = "1";
    start.Environment["CARGO_FEATURE_PURE"] = "1";   // blake3 would otherwise want a C compiler for the target
    start.Environment["RUSTDOCFLAGS"] = "-Z unstable-options --output-format json";
    using var cargo = Process.Start(start)!;
    cargo.WaitForExit();
    if (cargo.ExitCode != 0)
    {
        throw new InvalidOperationException($"cargo doc for {target} failed in {crateDir}; is that target installed for its toolchain?");
    }
}
