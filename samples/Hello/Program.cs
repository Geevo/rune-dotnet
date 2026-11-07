// examples/hello from Rune, in C#.
using System;
using Rune;
using static Rune.Ui;

if (args is ["--selftest-unwind"])
{
    Console.WriteLine(App.SelfTestUnwind() ? "unwind ok" : "unwind FAILED");
    return;
}

App.Run(new AppSpec<LightState>("Hello", (320f, 200f), () => new LightState(), View)
{
    Scenes = [("on", state => state.On = true)],
});

static El View(LightState state, Cx context)
{
    var theme = Tok.Theme(dark: true);

    var knob = El().Key("knob")
        .Size(18f, 18f).Radius(9f)
        .Fill(theme.Knob)
        .Absolute(state.On ? 18f : 2f, 2f);

    var toggle = El().Key("switch")
        .Role("switch").Label("Lights").Checked(state.On).Focusable(true)
        .Size(38f, 22f).Radius(11f)
        .Fill(state.On ? Tok.Accent(0, dark: true) : theme.ControlOff)
        .Motion(TokMotion.Snappy)
        .OnPress<LightState>(state => state.On = !state.On)
        .Child(knob);

    var label = Text(state.On ? "Lights on" : "Lights off")
        .Font(TokType.Title)
        .Color(theme.Text);

    return Col().WFull().HFull().Center().Gap(16f)
        .Fill(theme.Bg)
        .Child(label)
        .Child(toggle);
}

sealed class LightState
{
    public bool On { get; set; }
}
