// Exercises what hello doesn't: rune-kit, owned objects, sequences, closures with arguments and
// results, and Tx. native/rune-capi/src/bin/kit_rust.rs is the same app in Rust.
using System;
using Rune;
using static Rune.Ui;

if (args is ["--double-consume"])
{
    var button = Button.New("x");
    button.Free();
    button.Free();
    return;
}

App.Run(new AppSpec<State>("KitCheck", (360f, 260f), () => new State(), View)
{
    Scenes = [("bumped", state => state.Count = 41)],
});

static El View(State state, Cx context)
{
    var kit = Kit.Neutral(true);
    var theme = Tok.Theme(dark: true);

    var bold = Rune.Span.CreateDefault();
    bold.Start = 6;
    bold.End = 10;
    bold.Weight = 700f;
    bold.Color = Tok.Accent(0, dark: true);
    var rich = Rich($"count {state.Count:D4} done", [bold]).Font(TokType.Body).Color(theme.Text);

    var line = Canvas(Shape.New().Line((0f, 0f), (60f, 24f), 2f, LineCap.Round, theme.Text)).Key("line").Size(60f, 24f);

    var add = Button.New("Add").Variant(ButtonVariant.Contrast).OnPress<State>(state => state.Count++).Build(kit).Key("inc");
    var addTen = Button.New("Add ten").OnPressTx<State>((state, tx) =>
    {
        state.Count += 10;
        tx.SetTitle("Ten more");
    }).Build(kit).Key("ten");
    var custom = Button.New("ignored").LabelWith((style, color) => Text("Custom").Font(style).Color(color)).Build(kit).Key("custom");

    var body = Col().WFull().HFull().Center().Gap(12f).Fill(theme.Bg)
        .Child(rich).Child(line).Child(Row().Gap(8f).Child(add).Child(addTen).Child(custom))
        .Child(Text(context.SystemDark() ? "dark" : "light").Font(TokType.Label).Color(theme.TextSecondary));
    kit.Free();
    return body;
}

sealed class State
{
    public int Count { get; set; }
}
