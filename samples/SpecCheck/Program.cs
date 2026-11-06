// Exercises AppSpec's optional fields. native/rune-capi/src/bin/spec_rust.rs is the same app in
// Rust. Run from the repository root, where the font path resolves.
using System;
using System.IO;
using System.Threading;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("SpecCheck", (360f, 220f), () => new State(), View)
{
    MinSize = (300f, 200f),
    Fonts = [File.ReadAllBytes("rune/crates/rune/assets/IBMPlexMono-Regular.ttf")],
    Families = ("IBM Plex Mono", "IBM Plex Mono"),
    Setup = Setup,
    Poll = Poll,
    OnKey = OnKey,
});

static void Setup(State s, Waker waker)
{
    var other = waker.Clone();
    var thread = new Thread(() =>
    {
        other.Wake();
        other.Free();
    });
    thread.Start();
    thread.Join();
    waker.Wake();
    waker.Free();
    s.Woken = true;
}

static bool Poll(State s, double seconds, Tx tx)
{
    int tenths = (int)(seconds * 10);
    bool changed = tenths != s.Tenths;
    s.Tenths = tenths;
    return changed;
}

static bool OnKey(State s, KeyEvent ev, Tx tx)
{
    var key = ev.Key;
    var name = key.Which switch
    {
        Key.Case.Text => $"text:{key.AsText()}",
        Key.Case.Command => $"cmd:{key.AsCommand()}",
        Key.Case.Function => $"fn:{key.AsFunction()}",
        var other => other.ToString(),
    };
    if (ev.Mods.Shift)
    {
        name += " +shift";
    }
    if (ev.IsCommand(new System.Text.Rune('k')))
    {
        name += " is-k";
    }
    s.Last = name;
    s.Keys++;
    tx.SetTitle(name);
    return true;
}

static El View(State s, Cx _)
{
    var t = Tok.Theme(true);
    return Col().WFull().HFull().Center().Gap(8f).Fill(t.Bg)
        .Child(Text(s.Woken ? "woken" : "asleep").Key("woken").Font(TokType.Body).Color(t.Text))
        .Child(Text($"tenths {s.Tenths}").Key("tenths").Font(TokType.Body).Color(t.Text))
        .Child(Text($"{s.Keys} keys, last {s.Last}").Key("keys").Font(TokType.Body).Color(t.Text));
}

sealed class State
{
    public bool Woken;
    public int Tenths;
    public int Keys;
    public string Last = "none";
}
