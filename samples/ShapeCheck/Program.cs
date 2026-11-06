// Checks the shapes that cross as bytes, in a real view: each line must print `ok`.
// `--borrowed-free` must abort: objects read from a field are borrowed.
using System;
using System.Linq;
using Rune;
using static Rune.Ui;

if (args is ["--borrowed-free"])
{
    var e = MenuEntry.Item(RuneKitMenuItem.New("x"));
    e.AsItem()!.Value.Free();
    e.Free();
    Console.WriteLine("ok still alive: borrow was freed");
    return;
}

App.Run(new AppSpec<State>("ShapeCheck", (360f, 260f), () => new State(), View));

static void Check(string name, bool ok) => Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}");

static El View(State s, Cx cx)
{
    var k = Kit.Neutral(true);
    var t = Tok.Theme(true);
    if (s.Done)
    {
        return Col();
    }
    s.Done = true;

    Check("enum method", Edge.Leading.Opposite() == Edge.Trailing && SortDir.Ascending.Flip() == SortDir.Descending);
    Check("tuple of strings const", Tok.AccentNames.Item1.Length > 0);
    Check("duration const", Recorder.MeterInterval > TimeSpan.Zero);
    Console.WriteLine($"   last input {Ui.LastInput()?.ToString() ?? "none"}");

    var o = FileDialogOptions.CreateDefault();
    Check("Option<String> getter none", o.Directory is null);
    o.Directory = "/tmp/é";
    Check("Option<PathBuf> round trip", o.Directory == "/tmp/é");
    o = o.Filter("Images", ["png", "jpg"]).Filter("Text", ["txt"]);
    var f = o.Filters;
    Check("Vec<(String, Vec<String>)> getter", f.Length == 2 && f[0].Item1 == "Images" && f[0].Item2.SequenceEqual(["png", "jpg"]) && f[1].Item2[0] == "txt");
    o.Filters = [("All", ["*"])];
    Check("Vec<(String, Vec<String>)> setter", o.Filters.Single().Item1 == "All");
    o.Free();

    var h = TextHighlight.New((2, 9), t.Bg);
    Check("Range round trip", h.Range() == (2, 9) && h.WithRange((1, 4)).Range() == (1, 4));
    h.Free();

    try
    {
        Theme.FromJson("not json", true);
        Check("Result Err throws", false);
    }
    catch (RuneException e)
    {
        Check("Result Err throws: " + e.Message, e.Message.Length > 0);
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

    var hl = Highlighter.New((lang, code) => [((0, 1), Paint.Color(t.Text)), ((2, (nuint)code.Length), Paint.Color(t.Bg))]);
    var spans = hl.Highlight(null, "abcd");
    Console.WriteLine("   spans " + string.Join(" ", spans.Select(x => x.Item1)));
    Check("closure Option<&str> arg, Vec<tuple> return, through Rust", spans.Length == 2 && spans[0].Item1 == (0, 1) && spans[1].Item1 == (2, 4) && spans[1].Item2.AsColor() is not null);
    hl.Free();

    var layer = VecEl.Create();
    var trigger = Tooltip.New("tip").Trigger(Text("hover")).Text(k, "tip text").Open(true).Build(k, layer);
    Check("&mut Vec<El> layer filled", layer.Count > 0);
    var overlays = layer.Take();
    Check("layer emptied", layer.Count == 0 && overlays.Length > 0);
    layer.Free();

    var body = Col().WFull().HFull().Center().Gap(12f).Fill(t.Bg).Child(trigger).Child(Ui.TextInput(s.Edit, TokType.Body).OnEdit<State>(st => st.Edit).Key("input"));
    foreach (var e in overlays)
    {
        body = body.Child(e);
    }
    k.Free();
    Console.WriteLine("ok view built");
    return body;
}

sealed class State
{
    public bool Done;
    public TextEdit Edit = TextEdit.New("typed");
}
