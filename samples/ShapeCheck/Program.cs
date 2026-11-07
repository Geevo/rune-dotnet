// Checks the shapes that cross as bytes, in a real view: each line must print `ok`.
// `--borrowed-free` must abort: objects read from a field are borrowed.
using System;
using System.Linq;
using Rune;
using static Rune.Ui;

if (args is ["--borrowed-free"])
{
    var menuEntry = MenuEntry.Item(RuneKitMenuItem.New("x"));
    menuEntry.AsItem()!.Value.Free();
    menuEntry.Free();
    Console.WriteLine("ok still alive: borrow was freed");
    return;
}

App.Run(new AppSpec<State>("ShapeCheck", (360f, 260f), () => new State(), View));

static void Check(string name, bool ok) => Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}");

static El View(State state, Cx context)
{
    var kit = Kit.Neutral(true);
    var theme = Tok.Theme(dark: true);
    if (state.Done)
    {
        return Col();
    }
    state.Done = true;

    Check("enum method", Edge.Leading.Opposite() == Edge.Trailing && SortDir.Ascending.Flip() == SortDir.Descending);
    Check("tuple of strings const", Tok.AccentNames.Item1.Length > 0);
    Check("duration const", Recorder.MeterInterval > TimeSpan.Zero);
    Console.WriteLine($"   last input {Ui.LastInput()?.ToString() ?? "none"}");

    var options = FileDialogOptions.CreateDefault();
    Check("Option<String> getter none", options.Directory is null);
    options.Directory = "/tmp/é";
    Check("Option<PathBuf> round trip", options.Directory == "/tmp/é");
    options = options.Filter("Images", ["png", "jpg"]).Filter("Text", ["txt"]);
    var filters = options.Filters;
    Check("Vec<(String, Vec<String>)> getter", filters.Length == 2 && filters[0].Item1 == "Images" && filters[0].Item2.SequenceEqual(["png", "jpg"]) && filters[1].Item2[0] == "txt");
    options.Filters = [("All", ["*"])];
    Check("Vec<(String, Vec<String>)> setter", options.Filters.Single().Item1 == "All");
    options.Free();

    var highlight = TextHighlight.New((2, 9), theme.Bg);
    Check("Range round trip", highlight.Range() == (2, 9) && highlight.WithRange((1, 4)).Range() == (1, 4));
    highlight.Free();

    try
    {
        Theme.FromJson("not json", true);
        Check("Result Err throws", false);
    }
    catch (RuneException ex)
    {
        Check("Result Err throws: " + ex.Message, ex.Message.Length > 0);
    }

    var live = LiveText.New("before");
    Check("Arc<str> return", live.Get() == "before");
    live.Set("after é");
    Check("Arc<str> after set", live.Get() == "after é");
    live.Free();

    var named = Family.Named("Fira");
    Check("variant &'static str", named.AsNamed() == "Fira" && Family.Mono().AsNamed() is null);
    named.Free();

    var entry = MenuEntry.Item(RuneKitMenuItem.New("Open"));
    Check("Box variant, borrowed read", entry.AsItem() is not null);
    entry.Free();

    var highlighter = Highlighter.New((language, code) => [((0, 1), Paint.Color(theme.Text)), ((2, (nuint)code.Length), Paint.Color(theme.Bg))]);
    var spans = highlighter.Highlight(null, "abcd");
    Console.WriteLine("   spans " + string.Join(" ", spans.Select(span => span.Item1)));
    Check("closure Option<&str> arg, Vec<tuple> return, through Rust", spans.Length == 2 && spans[0].Item1 == (0, 1) && spans[1].Item1 == (2, 4) && spans[1].Item2.AsColor() is not null);
    highlighter.Free();

    var layer = VecEl.Create();
    var trigger = Tooltip.New("tip").Trigger(Text("hover")).Text(kit, "tip text").Open(true).Build(kit, layer);
    Check("&mut Vec<El> layer filled", layer.Count > 0);
    var overlays = layer.Take();
    Check("layer emptied", layer.Count == 0 && overlays.Length > 0);
    layer.Free();

    var body = Col().WFull().HFull().Center().Gap(12f).Fill(theme.Bg).Child(trigger)
        .Child(Ui.TextInput(state.Edit, TokType.Body).OnEdit<State>(state => state.Edit).Key("input"));
    foreach (var overlay in overlays)
    {
        body = body.Child(overlay);
    }
    kit.Free();
    Console.WriteLine("ok view built");
    return body;
}

sealed class State
{
    public bool Done { get; set; }
    // A handle struct, so it stays a field.
    public TextEdit Edit = TextEdit.New("typed");
}
