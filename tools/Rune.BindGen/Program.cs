// Generates the Rust C ABI and the C# bindings for Rune from rustdoc JSON.
// usage: dotnet run -c Release --project tools/Rune.BindGen
//        Rune.BindGen <out.rs> <out.cs> <report.md> <rune.json> [more crates...]   to reuse existing JSON
using System.Diagnostics;
using Rune.BindGen;

Environment.CurrentDirectory = RepositoryRoot();
if (Pins.Problem() is { } problem)
{
    Console.Error.WriteLine(problem);
    return 1;
}
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
var mac = Generate(Rustdoc("aarch64-apple-darwin"));
var linux = Generate(Rustdoc("x86_64-unknown-linux-gnu"), mac);
var (macOnly, linuxOnly) = Platforms.Only(linux.Rust.ReplaceLineEndings("\n"), mac.Rust.ReplaceLineEndings("\n"));
var macGate = macOnly.ToHashSet();
var linuxGate = linuxOnly.ToHashSet();
static string Section(string os, string[] symbols) => $"\n## {os} only\n\n{symbols.Length} exported functions, gated in Rust and marked `[SupportedOSPlatform(\"{os.ToLowerInvariant()}\")]` in C#:\n\n"
    + string.Concat(symbols.Select(s => $"- `{s}`\n"));
var report = mac.Report.ReplaceLineEndings("\n") + Section("macOS", macOnly) + Section("Linux", linuxOnly);
Write("native/rune-capi/src/generated.rs", "src/Rune/Generated.cs", "native/rune-capi/COVERAGE.md",
    Platforms.GateRust(mac.Rust.ReplaceLineEndings("\n"), macGate, "macos") + Platforms.LinuxRust(linux.Rust.ReplaceLineEndings("\n"), linuxGate),
    Platforms.GateCSharp(mac.CSharp.ReplaceLineEndings("\n"), macGate, "macos"), report);
File.WriteAllText("src/Rune/Generated.Linux.cs", Guards.CSharp(Platforms.LinuxCSharp(linux.CSharp.ReplaceLineEndings("\n"), mac.CSharp.ReplaceLineEndings("\n"), linuxGate)));
Console.WriteLine($"{mac.Summary}, {macOnly.Length} symbols macOS only; Linux {linux.Summary}, {linuxOnly.Length} symbols Linux only; {mac.Shapes.Keys.Union(linux.Shapes.Keys).Count()} closure shapes in all");
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

static Generator Generate(IEnumerable<string> json, Generator? first = null)
{
    var gen = new Generator(json.Select(Doc.Load).ToList()) { ShapeHints = first is null ? [] : new(first.Shapes), PoolHints = first is null ? [] : new(first.PoolNumbers) };
    gen.Run();
    return gen;
}

static void Write(string rustPath, string csPath, string reportPath, string rust, string cs, string report)
{
    File.WriteAllText(rustPath, Guards.Rust(rust.ReplaceLineEndings("\n")));
    File.WriteAllText(csPath, Guards.CSharp(cs.ReplaceLineEndings("\n")));
    File.WriteAllText(reportPath, report.ReplaceLineEndings("\n"));
}

static string[] Rustdoc(string target)
{
    var doc = $"target/doc-json/{target}/doc";
    Cargo("rune", "target/doc-json", target, "-p", "rune", "-p", "rune-kit", "-p", "rune-motion", "-p", "rune-notify",
        "-p", "rune-core", "-p", "rune-edit", "-p", "rune-gpu", "-p", "rune-image", "-p", "rune-media", "-p", "rune-spell", "-p", "rune-text");
    Cargo("native/rune-ext", "target/doc-json-ext", target, "-p", "rune-ext");
    File.Copy($"target/doc-json-ext/{target}/doc/rune_ext.json", $"{doc}/rune_ext.json", overwrite: true);
    return [$"{doc}/rune.json", $"{doc}/rune_motion.json", $"{doc}/rune_kit.json", $"{doc}/rune_notify.json", $"{doc}/rune_core.json", $"{doc}/rune_edit.json", $"{doc}/rune_gpu.json", $"{doc}/rune_image.json", $"{doc}/rune_media.json", $"{doc}/rune_spell.json", $"{doc}/rune_text.json", $"{doc}/rune_ext.json"];
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
    if (target.EndsWith("apple-darwin") && !OperatingSystem.IsMacOS())
    {
        // psm assembles its stack switching for the target; the host's gcc doesn't take Apple's flags, clang does.
        start.Environment[$"CC_{target.Replace('-', '_')}"] = "clang";
    }
    start.Environment["RUSTDOCFLAGS"] = "-Z unstable-options --output-format json --document-hidden-items";
    using var cargo = Process.Start(start)!;
    cargo.WaitForExit();
    if (cargo.ExitCode != 0)
    {
        throw new InvalidOperationException($"cargo doc for {target} failed in {crateDir}; is that target installed for its toolchain?");
    }
}
