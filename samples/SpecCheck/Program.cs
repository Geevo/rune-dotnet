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

static void Setup(State state, Waker waker)
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
    state.Woken = true;
}

static bool Poll(State state, double seconds, Tx tx)
{
    int tenths = (int)(seconds * 10);
    bool changed = tenths != state.Tenths;
    state.Tenths = tenths;
    return changed;
}

static bool OnKey(State state, KeyEvent keyEvent, Tx tx)
{
    var key = keyEvent.Key;
    var name = key.Which switch
    {
        Key.Case.Text => $"text:{key.AsText()}",
        Key.Case.Command => $"cmd:{key.AsCommand()}",
        Key.Case.Function => $"fn:{key.AsFunction()}",
        var other => other.ToString(),
    };
    if (keyEvent.Mods.Shift)
    {
        name += " +shift";
    }
    if (keyEvent.IsCommand(new System.Text.Rune('k')))
    {
        name += " is-k";
    }
    state.Last = name;
    state.Keys++;
    tx.SetTitle(name);
    return true;
}

static El View(State state, Cx context)
{
    var theme = Tok.Theme(dark: true);
    return Col().WFull().HFull().Center().Gap(8f).Fill(theme.Bg)
        .Child(Text(state.Woken ? "woken" : "asleep").Key("woken").Font(TokType.Body).Color(theme.Text))
        .Child(Text($"tenths {state.Tenths}").Key("tenths").Font(TokType.Body).Color(theme.Text))
        .Child(Text($"{state.Keys} keys, last {state.Last}").Key("keys").Font(TokType.Body).Color(theme.Text));
}

sealed class State
{
    public bool Woken { get; set; }
    public int Tenths { get; set; }
    public int Keys { get; set; }
    public string Last { get; set; } = "none";
}
