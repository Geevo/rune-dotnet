// examples/hello from Rune, line for line in C#.
using System;
using Rune;
using static Rune.Ui;

if (args is ["--selftest-unwind"])
{
    Console.WriteLine(App.SelfTestUnwind() ? "unwind ok" : "unwind FAILED");
    return;
}

App.Run(new AppSpec<State>("Hello", (320f, 200f), () => new State(), View)
{
    Scenes = [("on", s => s.On = true)],
});

static El View(State s, Cx _)
{
    var t = Tok.Theme(true);
    var label = Text(s.On ? "Lights on" : "Lights off").Font(TokType.Title).Color(t.Text);
    var knob = El().Key("knob").Size(18f, 18f).Radius(9f).Fill(t.Knob)
        .Absolute(s.On ? 18f : 2f, 2f);
    var toggle = El().Key("switch").Role("switch").Label("Lights").Checked(s.On).Focusable(true)
        .Size(38f, 22f).Radius(11f)
        .Fill(s.On ? Tok.Accent(0, true) : t.ControlOff)
        .Motion(TokMotion.Snappy)
        .OnPress<State>(s => s.On = !s.On)
        .Child(knob);
    return Col().WFull().HFull().Center().Gap(16f).Fill(t.Bg).Child(label).Child(toggle);
}

sealed class State
{
    public bool On;
}
