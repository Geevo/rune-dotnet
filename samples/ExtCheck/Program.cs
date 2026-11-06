// Runs the rune-ext adapters for real: each line either holds or the process exits non-zero.
// --gpu takes a device through Gpu.Headless. --compositor [png] renders a frame through a C# compositor
// and every render hook. --window opens a real window and closes it again.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Rune;
using static Rune.Ui;

int passed = 0;
void Check(string what, bool ok)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok)
    {
        Environment.Exit(1);
    }
    passed++;
}

// JSON and prefs
var prefs = Prefs.Memory();
Ext.PrefsSet(prefs, "size", Ext.JsonParse("{\"w\":640,\"tags\":[\"a\",\"b\"]}"));
Check("prefs round trip", Ext.PrefsGet(prefs, "size") is { } got && Ext.JsonText(got) == "{\"tags\":[\"a\",\"b\"],\"w\":640}");
Check("prefs missing key", Ext.PrefsGet(prefs, "nope") is null);
try
{
    Ext.JsonParse("{");
    Check("bad JSON throws", false);
}
catch (Exception)
{
    Check("bad JSON throws", true);
}

// External values
Check("key code debug", PhysicalKeyCode.KeyA.ToString() == "KeyA");
Check("texture format debug", WgpuTextureFormat.Rgba8Unorm.ToString() == "Rgba8Unorm");
Check("copy value survives a call", Shortcut.FromPhysicalKey(PhysicalKeyCode.KeyA, default).ToString() != null && PhysicalKeyCode.KeyA.ToString() == "KeyA");
Check("language parse", Ext.LanguageParse("en-US") is { } && Ext.LanguageParse("!!") is null);
Check("rgb image", Ext.RgbImage(1, 1, new byte[] { 1, 2, 3 }) is { } img && Ext.RgbImageBytes(img).SequenceEqual(new byte[] { 1, 2, 3 }));
Check("rgb image size", Ext.RgbImage(2, 2, new byte[] { 1 }) is null);
Check("io error", Ext.IoError("boom").ToString().Contains("boom"));

// Lists and tuples
var menu = Ext.PopupPicker(["a", "b"], ["Apple", "Banana"], "b");
Check("popup picker", menu.Find("b") is { } b && b.Title() == "Banana" && b.IsChecked() && menu.Find("a") is { } a && !a.IsChecked());
var order = new List<nuint>();
var steps = Ext.ChoreographyStagger(Choreography.New(), Cue.Now(), Stagger.New(0.1f), 3, (_, i) => order.Add(i));
Check("stagger", steps.Length == 3 && order.SequenceEqual(new nuint[] { 0, 1, 2 }));

// Trees
var files = Ext.FileTreeBuild(FileTree.New(), ["src/main.rs", "src/lib.rs", "README.md"], p => "data:" + p);
Check("file tree rows", files.Row(0) is { } first && first.Item1.Name == "src");
Check("file tree data", Enumerable.Range(0, 4).Select(i => files.Row((nuint)i)).Any(r => r is { } x && Ext.FileNodeData(x.Item1) is string d && d == "data:README.md"));
Check("tree item file", files.Row(0) is { } r0 && Ext.TreeItemFile(r0.Item1, r0.Item2.Id, files) is not null);
var tree = TreeModel.New();
tree.Insert(null, "root", "R");
tree.Insert("root", "kid", "K");
tree.SetExpanded("root", true);
var seen = new List<object>();
Ext.TreeSubtree(tree, "root", seen.Add);
Check("subtree", seen.SequenceEqual(new object[] { "R", "K" }));
Check("tree row", Ext.TreeRow(tree, 1) is { } info && info.Id == "kid" && info.Depth == 1 && info.Expanded is null);
Check("tree row data", (string?)Ext.TreeRowData(tree, 1) == "K");
Check("tree row past end", Ext.TreeRow(tree, 9) is null);

// Managed values through Rust generics
Check("with blend", (string)Ext.WithBlend(true, () => "inside") == "inside");
Check("snapshot outside a scope", (string)Ext.MemoSnapshot("k", "v") == "v");

// A worker thread calling back into C#
var latest = Latest.CreateDefault();
Ext.LatestRequest(latest, 20, k => (int)k + 1);
Check("latest request", Wait(latest) is 21);
int ran = 0;
Ext.LatestRequestIfChanged(latest, "same", k =>
{
    Interlocked.Increment(ref ran);
    return k;
});
Wait(latest);
Ext.LatestRequestIfChanged(latest, new string("same".AsSpan()), k =>
{
    Interlocked.Increment(ref ran);
    return k;
});
Thread.Sleep(50);
Check("equal keys use C# Equals", ran == 1);
Ext.LatestRequestOpt(latest, 1, k => k, r => false);
Thread.Sleep(50);
Check("refused result", latest.Take() is null);

static object? Wait(Latest l)
{
    for (int i = 0; i < 500; i++)
    {
        if (l.Take() is { } v)
        {
            return v;
        }
        Thread.Sleep(2);
    }
    return null;
}

// Function pointer fields, and Display and Hash taking strings
var spec = AppSpec.New<State>("ext", (320f, 200f), static () => new State(), static (s, cx) => Text("one").Key("t"));
Ext.SpecSetView<State>(spec, static (s, cx) => Text("two").Key("t").VirtualRowsVersion("v1"));
spec.SetScenes<State>(("bump", static s => s.Bumps++), ("jump", static s => s.Bumps += 10));
Check("scene ran", Tooling.InitialState<State>(spec, "jump").Bumps == 10);
Check("second scene ran", Tooling.InitialState<State>(spec, "bump").Bumps == 1);
Ext.SpecSetPoll<State>(spec, static (s, t, tx) =>
{
    s.Polls++;
    tx.TraceEvent("polled");
    return false;
});
var state = new State();
var rt = Runtime.New(state, View, (320f, 200f), Env.New(true, 2f));
Ext.RuntimeSetPoll<State>(rt, static (s, t, tx) =>
{
    s.Polls++;
    return false;
});
rt.Frame(0.016);
rt.TraceEvent("frame");
Check("poll ran", state.Polls > 0);
rt.Frame(0.016);
Check("layout read", state.Width > 0f);
Ext.RuntimeClearPoll(rt);
Check("highlighter key", Highlighter.Builtin().Key("theme").ToString() != null);
Check("number and byte keys", Highlighter.Builtin().Key(7).Key(7UL).Key(new byte[] { 1, 2 }).Key(new byte[0]).ToString() != null);
rt.TraceEvent(42);
rt.TraceEvent(1.5);

// Awaitable file dialogs, on Rune's scripted backend
var dialogs = FileDialogs.Scripted([
    Ext.ScriptedDialog(FileDialogKind.Open, "/tmp/a.txt"),
    Ext.ScriptedDialog(FileDialogKind.Save, null),
    Ext.ScriptedDialog(FileDialogKind.Folder, "/tmp"),
    Ext.ScriptedDialog(FileDialogKind.Save, "/tmp/wrong-kind"),
]);
Check("open dialog answered", dialogs.OpenAsync(FileDialogOptions.CreateDefault()).GetAwaiter().GetResult() == "/tmp/a.txt");
Check("cancelled dialog is null", dialogs.SaveAsync(FileDialogOptions.CreateDefault()).GetAwaiter().GetResult() is null);
Check("folder dialog answered", dialogs.OpenFolderAsync(FileDialogOptions.CreateDefault()).GetAwaiter().GetResult() == "/tmp");
var failed = dialogs.OpenAsync(FileDialogOptions.CreateDefault());
try
{
    failed.GetAwaiter().GetResult();
    Check("dialog failure faults the task", false);
}
catch (RuneException e)
{
    Check("dialog failure faults the task", e.Message.Length > 0);
}

// macOS-only API: this branch has to compile and link everywhere
if (OperatingSystem.IsMacOS())
{
    Embed.Prewarm();
}

// Enum data variants
var astc = WgpuTextureFormat.Astc(WgpuAstcBlock.B6x6, WgpuAstcChannel.UnormSrgb);
Check("astc format", astc.ToString().Contains("B6x6") && astc.ToString().Contains("UnormSrgb"));

// A sink for MotionTrace
var trace = new StringBuilder();
var mt = Ext.MotionTraceNew(b =>
{
    lock (trace)
    {
        trace.Append(Encoding.UTF8.GetString(b));
    }
}, Ext.TraceHeader("ext", 320f, 200f, 60f, 2f));
mt.Event("hello");
mt.Free();
Check("motion trace sink", trace.ToString().Contains("\"header\""));

if (args.Contains("--gpu"))
{
    var gpu = Gpu.Headless();
    var encoder = Ext.DeviceCreateEncoder(gpu.Device, "ext");
    Ext.QueueSubmit(gpu.Queue, encoder);
    Check("gpu submit", true);
}

// The motion gate: a real run on the virtual clock, recorded on Rune's worker thread
var cells = Ext.GateRun(["tap/wide", "tap/narrow"], name =>
{
    var run = MotionGateRun.New(Runtime.New(new State(), GateView, (320f, 200f), Env.New(true, 1f)), 120.0);
    run.Idle(0.1);
    run.Step();
    run.Act("tap", static rt => MotionGateRun.Tap(rt, "go"));
    run.Play(0.25);
    return Ext.GateRecording(run);
}, "");
Check("gate ran every cell", cells.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(["tap/narrow", "tap/wide"]));
Check("gate recorded frames", cells.All(c => !c.Skipped && c.Frames > 0));
var filtered = Ext.GateRun(["tap/wide", "tap/narrow"], static _ => throw new InvalidOperationException("a filtered cell ran"), "nothing matches this");
Check("gate filter", filtered.All(c => c.Skipped));

// A field only lends its object: giving it away would free memory its owner still holds
var lender = MotionGateRun.New(Runtime.New(new State(), GateView, (320f, 200f), Env.New(true, 1f)), 120.0);
Check("lent object reads", lender.Rec.Hz == 120.0);
try
{
    lender.Rec.Free();
    Check("lent object can't be freed", false);
}
catch (InvalidOperationException)
{
    Check("lent object can't be freed", true);
}
try
{
    Ext.GateRecording(lender).Free();
    Check("owned object frees", true);
}
catch (InvalidOperationException)
{
    Check("owned object frees", false);
}

// A frame through a C# compositor, and every render hook inside it
if (args.Contains("--compositor"))
{
    var at = Array.IndexOf(args, "--compositor") + 1;
    var png = at < args.Length ? args[at] : "compositor.png";
    var surface = NativeSurface.New();
    int submitted = 0, completed = 0;
    Ext.SurfaceSetObserver(surface, t =>
    {
        if (t.CompletedAt is null)
        {
            Interlocked.Increment(ref submitted);
        }
        else
        {
            Interlocked.Increment(ref completed);
        }
    });
    surface.PushBytes(NativeSurfaceModule.Synthetic(64, 32, 0), 1);

    var hits = new Dictionary<string, int>();
    void Hit(string what) => hits[what] = hits.GetValueOrDefault(what) + 1;
    int featureAsks = 0, renders = 0;
    (uint, uint) size = default;

    void Render(Frame f)
    {
        renders++;
        size = f.Size;
        f.DrawUi();
        var ui = f.Ui;
        var clear = (0f, 0f, 0f, 1f);
        var count = (uint)f.Rt.GetInstances().Length;
        Ext.RendererUploadImages(ui, f.Device, f.Queue, f.Rt);
        Ext.RendererDrawWith(ui, f.Encoder, f.Target, clear, (pass, site) =>
        {
            Hit($"draw {site.Mark}");
            if (site.Mark == 42)
            {
                Ext.RendererDrawRangeWith(ui, pass, f.Format, site.Index, site.Index + 1, (_, inner) => Hit($"range {inner.Mark}"));
            }
        });
        Ext.RendererDrawWithBackdrop(ui, f.Encoder, f.Target, clear, (_, site) => Hit($"backdrop {site.Mark}"), (_, site) => Hit($"backdrop draw {site.Mark}"));
        Ext.RendererEncodeLayersWith(ui, f.Encoder, (_, site) => Hit($"layer {site.Mark}"));
        Ext.RendererEncodeLayersWithBackdrop(ui, f.Encoder, (_, site) => Hit($"layer backdrop {site.Mark}"), (_, site) => Hit($"layer draw {site.Mark}"));
        Ext.RendererDrawTargetRangeWith(ui, f.Encoder, f.Target, f.Format, Ext.LoadKeep(), 0, count, (_, site) => Hit($"target backdrop {site.Mark}"), (_, site) => Hit($"target draw {site.Mark}"));
    }

    var drawn = AppSpec.New<State>("compositor", (320f, 200f), static () => new State(), (s, cx) =>
        Col().WFull().HFull().Gap(4f)
            .Child(Effect(42).Size(60f, 20f))
            .Child(Effect(43).EffectBackdrop(true).Size(60f, 20f))
            .Child(Col().Opacity(0.5f).Child(Effect(44).Size(60f, 20f)).Child(Effect(45).EffectBackdrop(true).Size(60f, 20f)))
            .Child(El().Size(64f, 32f).NativeSurface(surface, SurfaceFit.Fit)));
    var composed = Ext.AppCompositor(App.New(drawn), () =>
    {
        featureAsks++;
        return Ext.FeaturesEmpty();
    }, Render, static () => false, static _ => "extcheck");
    composed.Args(["--snap", png]).TryRun();

    foreach (var (what, n) in hits.OrderBy(h => h.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"     {what}: {n}");
    }
    Console.WriteLine($"     surface: {submitted} submitted, {completed} completed");
    Check("compositor asked for features", featureAsks > 0);
    Check("compositor rendered", renders > 0 && size.Item1 > 0);
    Check("snapshot written", new System.IO.FileInfo(png) is { Exists: true, Length: > 0 });
    foreach (var want in new[] { "draw 42", "range 42", "backdrop 43", "backdrop draw 43", "layer 44", "layer backdrop 45", "layer draw 45", "target draw 42", "target backdrop 43" })
    {
        Check($"hook: {want}", hits.GetValueOrDefault(want) > 0);
    }
    Check("surface observer", submitted > 0);
}

// An on-screen window, closed as soon as it is ready
if (args.Contains("--window"))
{
    string? raw = null;
    int ready = 0;
    var shown = App.New(AppSpec.New<State>("extcheck", (240f, 120f), static () => new State(), static (s, cx) => Text("window check")));
    var handle = shown.Handle();
    shown.Args([]).OnReady(w =>
    {
        ready++;
        Ext.WindowWithRawHandle(w, h => raw = h.ToString());
        handle.Quit();
    }).TryRun();
    Console.WriteLine($"     {raw}");
    Check("window became ready", ready > 0);
    Check("raw window handle", !string.IsNullOrEmpty(raw));
}

Console.WriteLine($"{passed} checks passed");

static El View(State s, Cx cx)
{
    if (Ext.CxLayoutWith(cx, "t", r => r.Item3) is float w)
    {
        s.Width = w;
    }
    return Text("x").Key("t");
}

static El GateView(State s, Cx cx) =>
    Col().WFull().HFull().Child(El().Key("go").Size(s.Bumps > 0 ? 80f : 40f, 20f).Motion(TokMotion.Snappy).OnPress<State>(static s => s.Bumps++));

sealed class State
{
    public int Bumps;
    public int Polls;
    public float Width;
}
