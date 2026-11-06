// Exercises what hello doesn't: rune-kit, owned objects, sequences, closures with arguments and
// results, and Tx. native/rune-capi/src/bin/kit_rust.rs is the same app in Rust.
using System;
using Rune;
using static Rune.Ui;

if (args is ["--double-consume"])
{
    var b = Button.New("x");
    b.Free();
    b.Free();
    return;
}

App.Run(new AppSpec<State>("KitCheck", (360f, 260f), () => new State(), View)
{
    Scenes = [("bumped", s => s.Count = 41)],
});

static El View(State s, Cx cx)
{
    var k = Kit.Neutral(true);
    var t = Tok.Theme(true);

    var bold = Rune.Span.CreateDefault();
    bold.Start = 6;
    bold.End = 10;
    bold.Weight = 700f;
    bold.Color = Tok.Accent(0, true);
    var rich = Rich($"count {s.Count:D4} done", [bold]).Font(TokType.Body).Color(t.Text);

    var line = Canvas(Shape.New().Line((0f, 0f), (60f, 24f), 2f, LineCap.Round, t.Text)).Key("line").Size(60f, 24f);

    var inc = Button.New("Add").Variant(ButtonVariant.Contrast).OnPress<State>(s => s.Count++).Build(k).Key("inc");
    var tx = Button.New("Add ten").OnPressTx<State>((s, tx) =>
    {
        s.Count += 10;
        tx.SetTitle("Ten more");
    }).Build(k).Key("ten");
    var custom = Button.New("ignored").LabelWith((style, color) => Text("Custom").Font(style).Color(color)).Build(k).Key("custom");

    var body = Col().WFull().HFull().Center().Gap(12f).Fill(t.Bg)
        .Child(rich).Child(line).Child(Row().Gap(8f).Child(inc).Child(tx).Child(custom))
        .Child(Text(cx.SystemDark() ? "dark" : "light").Font(TokType.Label).Color(t.TextSecondary));
    k.Free();
    return body;
}

sealed class State
{
    public int Count;
}
