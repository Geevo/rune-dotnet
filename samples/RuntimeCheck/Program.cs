// Drives a headless Runtime with synthetic clicks, so every plain `fn` pointer Rune stores (the view,
// the tree accessors and label, the split view accessor) runs. The output must match rt_rust byte
// for byte. With --pool it also fills the pool, which Rust has no limit on.
using System;
using System.Collections.Generic;
using Rune;
using static Rune.Ui;

static string Bits(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8");

static void Dump(Runtime runtime, State state, string label)
{
    Console.WriteLine($"{label} clicks={state.Clicks} width={Bits(state.Pane.Width())} log=[{string.Join(",", state.Log)}]");
    var button = runtime.Find("button");
    Console.WriteLine($"  button {(button is var (x, y) ? Bits(x) + " " + Bits(y) : "none")} src {state.Files.IsExpanded("src")} main {state.Files.IsSelected("main")}".ToLowerInvariant());
}

var state = new State();
state.Files.Insert(null, "src", "src");
state.Files.Insert("src", "main", "main.rs");
state.Files.Insert("src", "lib", "lib.rs");
state.Files.Insert(null, "docs", "docs");

var runtime = Runtime.New(state, View, (640f, 400f), Env.New(true, 2f));
runtime.Frame(0.016);
Dump(runtime, state, "start");

for (int round = 0; round < 3; round++)
{
    for (float y = 10f; y < 400f; y += 20f)
    {
        for (float x = 10f; x < 640f; x += 40f)
        {
            runtime.PointerMove(x, y);
            runtime.PointerDown(x, y);
            runtime.PointerUp(x, y);
            runtime.Frame(0.01);
            runtime.PointerDown(x, y);
            runtime.PointerUp(x, y);
            runtime.Frame(0.5);
        }
    }
    Dump(runtime, state, $"round {round}");
}
// Dragging the divider writes the width through the split view accessor.
for (float x = 200f; x < 240f; x += 2f)
{
    runtime.PointerMove(x, 200f);
    runtime.PointerDown(x, 200f);
    runtime.Frame(0.016);
    runtime.PointerMove(x + 60f, 200f);
    runtime.Frame(0.016);
    runtime.PointerUp(x + 60f, 200f);
    runtime.Frame(0.5);
}
Dump(runtime, state, "dragged");
runtime.Update(static (State state, Tx _) => state.Clicks += 100);
runtime.Frame(0.016);
Dump(runtime, state, "updated");

// A second runtime on a different view takes a second slot in the same pool.
var otherState = new State();
var otherRuntime = Runtime.New(otherState, Bare, (200f, 100f), Env.New(false, 1f));
otherRuntime.Frame(0.016);
Dump(otherRuntime, otherState, "bare");
runtime.Frame(0.016);
Dump(runtime, state, "first again");

if (args is not ["--pool"])
{
    return;
}

int taken = 0;
try
{
    for (; taken < 1000; taken++)
    {
        int index = taken;
        Runtime.New(new State(), (State state, Cx context) => index < 0 ? Bare(state, context) : El(), (1f, 1f), Env.New(true, 1f));
    }
    Console.Error.WriteLine("no limit reached");
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"pool full after {taken}: {ex.Message}");
}

static El Bare(State state, Cx context) =>
    Col().WFull().HFull().Child(El().Key("only").Size(20f, 20f).OnPress((State state) => state.Clicks++));

static El View(State state, Cx context)
{
    var kit = Kit.Neutral(true);
    var tree = Tree.New(static (State state) => state.Files, static (State state) => state.Files, static item => (string)item)
        .Name("Files")
        .Row(static (item, row) => TreeItem.New((string)item).Badge("x"))
        .OnSelect(static (State state, string id, Tx _) => state.Log.Add("select:" + id))
        .OnOpen(static (State state, string id, Tx _) => state.Log.Add("open:" + id))
        .Build(state, kit);
    var main = Col().Key("main").WFull().HFull().Padding(16f)
        .Child(Text($"Clicks {state.Clicks}").Font(TokType.Body))
        .Child(El().Key("button").Size(80f, 32f).Fill(Tok.Accent(0, dark: true)).OnPress((State state) => state.Clicks++));
    return SplitView.New(main).Sidebar(state.Pane, static (State state) => state.Pane, tree).Build(kit);
}

sealed class State
{
    public int Clicks { get; set; }
    // Handle structs, so they stay fields.
    public TreeModel Files = TreeModel.New();
    public PaneState Pane = PaneState.New(220f);
    public List<string> Log { get; } = [];
}
