// Drives a headless Runtime with synthetic clicks, so every plain `fn` pointer Rune stores (the view,
// the tree accessors and label, the split view accessor) runs. The output must match rt_rust byte
// for byte. With --pool it also fills the pool, which Rust has no limit on.
using System;
using System.Collections.Generic;
using Rune;
using static Rune.Ui;

static string B(float v) => BitConverter.SingleToUInt32Bits(v).ToString("x8");

static void Dump(Runtime rt, State s, string label)
{
    Console.WriteLine($"{label} clicks={s.Clicks} width={B(s.Pane.Width())} log=[{string.Join(",", s.Log)}]");
    var at = rt.Find("button");
    Console.WriteLine($"  button {(at is var (x, y) ? B(x) + " " + B(y) : "none")} src {s.Files.IsExpanded("src")} main {s.Files.IsSelected("main")}".ToLowerInvariant());
}

var s = new State();
s.Files.Insert(null, "src", "src");
s.Files.Insert("src", "main", "main.rs");
s.Files.Insert("src", "lib", "lib.rs");
s.Files.Insert(null, "docs", "docs");

var rt = Runtime.New(s, View, (640f, 400f), Env.New(true, 2f));
rt.Frame(0.016);
Dump(rt, s, "start");

for (int round = 0; round < 3; round++)
{
    for (float y = 10f; y < 400f; y += 20f)
    {
        for (float x = 10f; x < 640f; x += 40f)
        {
            rt.PointerMove(x, y);
            rt.PointerDown(x, y);
            rt.PointerUp(x, y);
            rt.Frame(0.01);
            rt.PointerDown(x, y);
            rt.PointerUp(x, y);
            rt.Frame(0.5);
        }
    }
    Dump(rt, s, $"round {round}");
}
// Dragging the divider writes the width through the split view accessor.
for (float x = 200f; x < 240f; x += 2f)
{
    rt.PointerMove(x, 200f);
    rt.PointerDown(x, 200f);
    rt.Frame(0.016);
    rt.PointerMove(x + 60f, 200f);
    rt.Frame(0.016);
    rt.PointerUp(x + 60f, 200f);
    rt.Frame(0.5);
}
Dump(rt, s, "dragged");
rt.Update(static (State s, Tx _) => s.Clicks += 100);
rt.Frame(0.016);
Dump(rt, s, "updated");

// A second runtime on a different view takes a second slot in the same pool.
var other = new State();
var rt2 = Runtime.New(other, Bare, (200f, 100f), Env.New(false, 1f));
rt2.Frame(0.016);
Dump(rt2, other, "bare");
rt.Frame(0.016);
Dump(rt, s, "first again");

if (args is not ["--pool"])
{
    return;
}

int taken = 0;
try
{
    for (; taken < 1000; taken++)
    {
        int n = taken;
        Runtime.New(new State(), (State s, Cx cx) => n < 0 ? Bare(s, cx) : El(), (1f, 1f), Env.New(true, 1f));
    }
    Console.Error.WriteLine("no limit reached");
}
catch (InvalidOperationException e)
{
    Console.Error.WriteLine($"pool full after {taken}: {e.Message}");
}

static El Bare(State s, Cx cx) => Col().WFull().HFull().Child(El().Key("only").Size(20f, 20f).OnPress((State s) => s.Clicks++));

static El View(State s, Cx cx)
{
    var k = Kit.Neutral(true);
    var tree = Tree.New(static (State s) => s.Files, static (State s) => s.Files, static o => (string)o)
        .Name("Files")
        .Row(static (o, r) => TreeItem.New((string)o).Badge("x"))
        .OnSelect(static (State s, string id, Tx _) => s.Log.Add("select:" + id))
        .OnOpen(static (State s, string id, Tx _) => s.Log.Add("open:" + id))
        .Build(s, k);
    var main = Col().Key("main").WFull().HFull().Padding(16f)
        .Child(Text($"Clicks {s.Clicks}").Font(TokType.Body))
        .Child(El().Key("button").Size(80f, 32f).Fill(Tok.Accent(0, true)).OnPress((State s) => s.Clicks++));
    return SplitView.New(main).Sidebar(s.Pane, static (State s) => s.Pane, tree).Build(k);
}

sealed class State
{
    public int Clicks;
    public TreeModel Files = TreeModel.New();
    public PaneState Pane = PaneState.New(220f);
    public List<string> Log = new();
}
