// Runs each Rust twin and its C# port with the same arguments and compares exit code, stdout, stderr,
// PNG and tree byte for byte, then checks that misuse fails cleanly and runs the adapters for real.
// --build builds both sides first. --window also opens a real window, so it needs a display.
// In arguments, @PNG and @TREE become an output path per side. Exits non-zero on any difference.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

const string V = "tools/verify";
const string D = "out/verify/run";
var exe = OperatingSystem.IsWindows() ? ".exe" : "";
int pass = 0, fail = 0;

Environment.CurrentDirectory = RepositoryRoot();
if (Pins.Problem() is { } problem)
{
    Console.Error.WriteLine(problem);
    return 1;
}
if (args.Contains("--build"))
{
    Build();
}
if (Directory.Exists(D))
{
    Directory.Delete(D, recursive: true);
}
Directory.CreateDirectory(D);

Case("hello_rust", "hello", "snap", "--snap", "@PNG");
Case("hello_rust", "hello", "scene", "--snap", "@PNG", "--scene", "on");
Case("hello_rust", "hello", "tree", "--tree", "@TREE");
Case("hello_rust", "hello", "one", "--script", $"{V}/hello-one.json", "--snap", "@PNG", "--tree", "@TREE");
Case("hello_rust", "hello", "many", "--script", $"{V}/hello-many.json", "--snap", "@PNG", "--tree", "@TREE");

Case("kit_rust", "kitcheck", "snap", "--snap", "@PNG");
Case("kit_rust", "kitcheck", "scene", "--snap", "@PNG", "--scene", "bumped");
Case("kit_rust", "kitcheck", "clicks", "--script", $"{V}/kit-clicks.json", "--snap", "@PNG", "--tree", "@TREE");

Case("spec_rust", "speccheck", "snap", "--snap", "@PNG");
Case("spec_rust", "speccheck", "keys", "--script", $"{V}/spec-keys.json", "--snap", "@PNG", "--tree", "@TREE");

Case("gen_rust", "gencheck", "stdout");
Case("gen_rust", "gencheck", "app", "--snap", "@PNG");
Case("rt_rust", "runtimecheck", "stdout");

Case("todo_rust", "todo", "snap", "--snap", "@PNG");
Case("todo_rust", "todo", "light", "--snap", "@PNG", "--theme", "light", "--scale", "1");
Case("todo_rust", "todo", "tree", "--tree", "@TREE");
Case("todo_rust", "todo", "script", "--script", $"{V}/todo.json", "--snap", "@PNG", "--tree", "@TREE");
Case("todo_rust", "todo", "mid", "--script", $"{V}/todo.json", "--snap", "@PNG", "--at", "0.82");

Case("settings_rust", "settings", "list", "--list-scenes");
var scenes = Run($"target/release/settings_rust{exe}", ["--list-scenes"], null).Out;
foreach (var scene in Encoding.UTF8.GetString(scenes).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
{
    Case("settings_rust", "settings", scene, "--scene", scene, "--snap", "@PNG", "--tree", "@TREE");
}
Case("settings_rust", "settings", "script", "--script", $"{V}/settings.json", "--snap", "@PNG", "--tree", "@TREE");
Case("settings_rust", "settings", "mid", "--script", $"{V}/settings.json", "--snap", "@PNG", "--at", "1.35");

Case("shapes_rust", "shapes", "snap", "--snap", "@PNG");
Case("shapes_rust", "shapes", "at", "--snap", "@PNG", "--at", "1.5");
Case("shapes_rust", "shapes", "tree", "--tree", "@TREE", "--at", "0.7");

Case("odometer_rust", "odometer", "snap", "--snap", "@PNG");
Case("odometer_rust", "odometer", "tree", "--tree", "@TREE");
Case("odometer_rust", "odometer", "script", "--script", $"{V}/odometer.json", "--snap", "@PNG", "--tree", "@TREE");
Case("odometer_rust", "odometer", "mid", "--script", $"{V}/odometer.json", "--snap", "@PNG", "--at", "0.95");

Case("matched_rust", "matched", "snap", "--snap", "@PNG");
Case("matched_rust", "matched", "tree", "--tree", "@TREE");
Case("matched_rust", "matched", "one", "--scene", "one", "--snap", "@PNG");
Case("matched_rust", "matched", "grid", "--scene", "grid", "--snap", "@PNG");
Case("matched_rust", "matched", "zoom", "--script", "rune/examples/matched/scripts/zoom.json", "--snap", "@PNG", "--tree", "@TREE");

Case("texts_rust", "texts", "snap", "--snap", "@PNG");
Case("texts_rust", "texts", "tree", "--tree", "@TREE");
Case("texts_rust", "texts", "script", "--script", $"{V}/texts.json", "--snap", "@PNG", "--tree", "@TREE");

// Misuse must end in a managed exception, not a crash.
Hazard("held-key", "borrowed by a callback");
Hazard("default-object", "default struct");

// The adapters, through a real frame, device and worker thread.
Adapters("adapters", "--gpu", "--compositor", $"{D}/compositor.png");
if (args.Contains("--window"))
{
    Adapters("window", "--window");
}

Console.WriteLine($"{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

void Case(string rust, string cs, string label, params string[] args)
{
    var r = $"{D}/{cs}-{label}-rust";
    var c = $"{D}/{cs}-{label}-cs";
    var rr = Run($"target/release/{rust}{exe}", args.Select(a => Expand(a, r)), null);
    var cr = Run($"out/{cs}/{cs}{exe}", args.Select(a => Expand(a, c)), null);
    var bad = new List<string>();
    var summary = $"exit={rr.Exit}";
    if (rr.Exit != cr.Exit)
    {
        bad.Add($"exit({rr.Exit}/{cr.Exit})");
    }
    if (rr.TimedOut || cr.TimedOut)
    {
        bad.Add("timeout");
    }
    var (ro, co) = (Clean(rr.Out, r), Clean(cr.Out, c));
    if (ro.SequenceEqual(co))
    {
        summary += $" out={ro.Length}";
    }
    else
    {
        bad.Add("stdout");
    }
    if (!Clean(rr.Err, r).SequenceEqual(Clean(cr.Err, c)))
    {
        bad.Add("stderr");
    }
    foreach (var e in new[] { "png", "tree" })
    {
        var (rf, cf) = ($"{r}.{e}", $"{c}.{e}");
        if (!File.Exists(rf) && !File.Exists(cf))
        {
            continue;
        }
        if (File.Exists(rf) && File.Exists(cf) && File.ReadAllBytes(rf).AsSpan().SequenceEqual(File.ReadAllBytes(cf)))
        {
            summary += $" {e}={new FileInfo(rf).Length}";
        }
        else
        {
            bad.Add(e);
        }
    }
    if (bad.Count == 0)
    {
        pass++;
        Console.WriteLine($"ok   {cs}/{label} {summary}");
    }
    else
    {
        fail++;
        Console.WriteLine($"FAIL {cs}/{label}: {string.Join(' ', bad)}");
    }
}

void Hazard(string name, string expected)
{
    var run = Run($"out/hazards/hazards{exe}", ["--script", $"{V}/hazards-keys.json", "--snap", $"{D}/hazard.png"], name, seconds: 30);
    // The exit code of an abort differs by platform.
    var text = Encoding.UTF8.GetString(run.Out) + Encoding.UTF8.GetString(run.Err);
    if (run.Exit != 0 && !run.TimedOut && text.Contains(expected))
    {
        pass++;
        Console.WriteLine($"ok   hazards/{name}");
    }
    else
    {
        fail++;
        Console.WriteLine($"FAIL hazards/{name}: exit {run.Exit}");
    }
}

void Adapters(string name, params string[] arguments)
{
    var run = Run($"out/extcheck/extcheck{exe}", arguments, null, seconds: 120);
    if (run.Exit == 0 && !run.TimedOut)
    {
        pass++;
        Console.WriteLine($"ok   extcheck/{name}");
        return;
    }
    fail++;
    Console.WriteLine($"FAIL extcheck/{name}: exit {run.Exit}");
    Console.WriteLine(Encoding.UTF8.GetString(run.Out) + Encoding.UTF8.GetString(run.Err));
}

// One link at a time: each fat-LTO link peaks at over 2 GB.
static void Build()
{
    string[] twins = ["hello", "kit", "spec", "rt", "gen", "todo", "settings", "shapes", "matched", "odometer", "texts"];
    string[] samples = ["Hello", "KitCheck", "SpecCheck", "GenCheck", "RuntimeCheck", "ShapeCheck", "Todo", "Settings", "Shapes", "Odometer", "Matched", "Texts", "Hazards", "ExtCheck"];
    Exec("native/rune-capi", "cargo", "build", "--release", "--lib", "-j2");
    foreach (var twin in twins)
    {
        Exec("native/rune-capi", "cargo", "build", "--release", "--bin", $"{twin}_rust", "-j2");
    }
    foreach (var sample in samples)
    {
        // Publish doesn't notice a changed static library, so it would keep the old link.
        var obj = $"samples/{sample}/obj";
        if (Directory.Exists(obj))
        {
            foreach (var native in Directory.GetDirectories(obj, "native", SearchOption.AllDirectories))
            {
                Directory.Delete(native, recursive: true);
            }
        }
        Exec(".", "dotnet", "publish", $"samples/{sample}/{sample}.csproj", "-c", "Release", "-o", $"out/{sample.ToLowerInvariant()}");
    }
}

static void Exec(string directory, string program, params string[] arguments)
{
    var start = new ProcessStartInfo(program) { WorkingDirectory = directory };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }
    using var process = Process.Start(start)!;
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{program} {string.Join(' ', arguments)} exited {process.ExitCode}");
    }
}

static string Expand(string arg, string side) => arg.Replace("@PNG", side + ".png").Replace("@TREE", side + ".tree");

// Latin-1 maps bytes to chars one to one, so this edits the output without reencoding it.
static byte[] Clean(byte[] output, string side)
{
    var text = Encoding.Latin1.GetString(output).Replace(side, "X");
    return Encoding.Latin1.GetBytes(Regex.Replace(text, @"^rune: .*(\r?\n|\z)", "", RegexOptions.Multiline));
}

static Result Run(string program, IEnumerable<string> args, string? hazard, int seconds = 200)
{
    var start = new ProcessStartInfo(Path.GetFullPath(program)) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in args)
    {
        start.ArgumentList.Add(arg);
    }
    if (hazard is not null)
    {
        start.Environment["HAZ"] = hazard;
    }
    using var process = Process.Start(start)!;
    using var stdout = new MemoryStream();
    using var stderr = new MemoryStream();
    var reads = Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(stdout), process.StandardError.BaseStream.CopyToAsync(stderr));
    var timedOut = !process.WaitForExit(seconds * 1000);
    if (timedOut)
    {
        process.Kill(entireProcessTree: true);
    }
    reads.Wait();
    process.WaitForExit();
    return new(process.ExitCode, timedOut, stdout.ToArray(), stderr.ToArray());
}

static string RepositoryRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "tools", "verify")))
        {
            return dir.FullName;
        }
    }
    throw new InvalidOperationException("run this from inside the repository");
}

record Result(int Exit, bool TimedOut, byte[] Out, byte[] Err);
