// C# port of rune/examples/settings; native/rune-capi/src/twins/settings_rust.rs is the original.
using System;
using System.Globalization;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<Settings>("Settings", (620f, 520f), () => new Settings(), View)
{
    Scenes =
    [
        ("default", _ => { }),
        ("light", settings => settings.Appearance = Appearance.Light),
        ("dark", settings => settings.Appearance = Appearance.Dark),
        ("reduced-motion", settings => settings.ReduceMotion = true),
        ("toggles-off", settings =>
        {
            settings.LaunchAtLogin = false;
            settings.MenuBar = false;
            settings.Sounds = false;
        }),
        ("accent-pink", settings => settings.Accent = 2),
        ("slow", settings => settings.Speed = 0f),
    ],
});

static float SpeedOf(float value) => MathF.Pow(2f, value * 2f - 1f);
static string Speed(float value) => SpeedOf(value).ToString("F2", CultureInfo.InvariantCulture) + "×";

static El View(Settings settings, Cx context)
{
    bool dark = settings.Appearance switch
    {
        Appearance.Auto => context.SystemDark(),
        Appearance.Light => false,
        _ => true,
    };
    context.ReducedMotion(settings.ReduceMotion);
    context.TimeScale(SpeedOf(settings.Speed));
    var paint = new Paint(Tok.Theme(dark), Tok.Accent((nuint)settings.Accent, dark), 1f / context.Env().Scale);
    var theme = paint.Theme;

    return Col().Key("window").Role("window").Fill(theme.Bg)
        .Child(Row().Key("titlebar").H(40f).WFull().Center()
            .Child(Text("Settings").Key("title").Font(TokType.Title).Color(theme.Text)))
        .Child(Col().Key("content").Grow(1f).Px(TokSpace.S6).Pt(TokSpace.S1).Pb(TokSpace.S6).Gap(TokSpace.S6)
            .ClipFade(0f, TokSpace.S1, 0f, TokSpace.S6)
            .Child(Section(paint, "appearance", "Appearance",
                SettingRow(paint, "theme", "Theme", null, Segmented(paint, settings.Appearance)),
                SettingRow(paint, "accent", "Accent colour", null, Swatches(paint, settings.Accent, dark))))
            .Child(Section(paint, "general", "General",
                ToggleRow(paint, "login", "Launch at login", settings.LaunchAtLogin, settings => settings.LaunchAtLogin = !settings.LaunchAtLogin),
                ToggleRow(paint, "menubar", "Show in menu bar", settings.MenuBar, settings => settings.MenuBar = !settings.MenuBar),
                ToggleRow(paint, "sounds", "Play sounds", settings.Sounds, settings => settings.Sounds = !settings.Sounds)))
            .Child(Section(paint, "motion", "Motion",
                SettingRow(paint, "speed", "Animation speed", null, SpeedControl(paint, settings.Speed)),
                ToggleRow(paint, "reduce", "Reduce motion", settings.ReduceMotion, settings => settings.ReduceMotion = !settings.ReduceMotion))));
}

static El Section(Paint paint, string key, string title, params El[] rows)
{
    var card = Col().Key("card").Padding(TokSpace.S1).Fill(paint.Theme.Card).Radius(TokRadius.Lg)
        .Border(paint.Hairline, paint.Theme.CardBorder).Shadow(1f, 2f, paint.Theme.Shadow).Shadow(8f, 24f, paint.Theme.ShadowSoft);
    for (int i = 0; i < rows.Length; i++)
    {
        card = card.Child(rows[i]);
        if (i + 1 < rows.Length)
        {
            card = card.Child(El().Key($"sep{i}").H(paint.Hairline).Ml(TokSpace.S3).Mr(TokSpace.S3).Fill(paint.Theme.Separator).A11yHidden());
        }
    }
    return Col().Key(key).Gap(TokSpace.S2)
        .Child(Text(title).Key("header").Font(TokType.Section).Color(paint.Theme.TextSecondary).Ml(TokSpace.S3))
        .Child(card);
}

static El SettingRow(Paint paint, string key, string label, Action<Settings>? onPress, El control)
{
    var row = Row().Key(key).H(44f).Px(TokSpace.S3).CenterY().JustifyBetween().Radius(TokRadius.Md).Label(label)
        .Child(Text(label).Key("label").Font(TokType.Body).Color(paint.Theme.Text).A11yHidden())
        .Child(control);
    if (onPress != null)
    {
        row = row.Hover(paint.Theme.Hover).Press(paint.Theme.Press).Focusable(true).OnPress(onPress);
    }
    return row;
}

static El ToggleRow(Paint paint, string key, string label, bool on, Action<Settings> flip) =>
    SettingRow(paint, key, label, flip, Toggle(paint, on).A11yHidden()).Role("switch").Checked(on);

static El Toggle(Paint paint, bool on)
{
    const float width = 38f, height = 22f, knob = 18f;
    const float inset = (height - knob) / 2f;
    return El().Key("switch").Role("switch").Size(width, height).Radius(height / 2f)
        .Fill(on ? paint.Accent : paint.Theme.ControlOff).Motion(TokMotion.Snappy)
        .Child(El().Key("knob").Size(knob, knob).Absolute(on ? width - knob - inset : inset, inset).Radius(knob / 2f)
            .Fill(paint.Theme.Knob).Shadow(1f, 3f, paint.Theme.KnobShadow).Shadow(0f, 0.5f, paint.Theme.Edge));
}

static El Segmented(Paint paint, Appearance current)
{
    const float width = 205f, height = 28f, pad = 2f;
    const float segment = (width - 2f * pad) / 3f;
    (Appearance Value, string Name)[] options = [(Appearance.Auto, "Auto"), (Appearance.Light, "Light"), (Appearance.Dark, "Dark")];
    int selectedIndex = Math.Max(Array.FindIndex(options, option => option.Value == current), 0);
    var segments = Row().Key("segments");
    for (int i = 0; i < options.Length; i++)
    {
        var (appearance, name) = options[i];
        bool selected = i == selectedIndex;
        segments = segments.Child(
            El().Key(name).Role("segment").Label(name).Checked(selected).Focusable(true)
                .Size(segment, height - 2f * pad).Center().Radius(TokRadius.Sm)
                .Hover(selected ? Color.Transparent : paint.Theme.Hover)
                .OnPressTx((Settings settings, Tx tx) =>
                {
                    settings.Appearance = appearance;
                    tx.ColorMotion(TokMotion.Calm);
                })
                .Child(Text(name).Key("label").Font(TokType.Label)
                    .Color(selected ? paint.Theme.Text : paint.Theme.TextSecondary).A11yHidden()));
    }
    return El().Key("segmented").Role("segmented").Size(width, height).Padding(pad).Radius(TokRadius.Md)
        .Fill(paint.Theme.ControlTrack).Motion(TokMotion.Snappy)
        .Child(El().Key("indicator").Size(segment, height - 2f * pad).Absolute(pad + selectedIndex * segment, pad)
            .Radius(TokRadius.Sm).Fill(paint.Theme.Indicator).Shadow(1f, 3f, paint.Theme.Shadow).Shadow(0f, 0.5f, paint.Theme.Edge)
            .Lead(TokMotion.Lead, TokMotion.Default).A11yHidden())
        .Child(segments);
}

static El Swatches(Paint paint, int current, bool dark)
{
    const float diameter = 18f, gap = 12f, ringWidth = 2f;
    const float ring = diameter + 2f * (ringWidth + 2f);
    // tok::ACCENT_NAMES is unbound.
    string[] names = ["blue", "purple", "pink", "orange", "green", "graphite"];
    var row = Row().Key("swatches").Role("radiogroup").Label("Accent colour").Gap(gap).Motion(TokMotion.Snappy);
    row = row.Child(El().Key("ring").Size(ring, ring)
        .Absolute(current * (diameter + gap) - (ring - diameter) / 2f, -(ring - diameter) / 2f)
        .Radius(ring / 2f).Border(ringWidth, paint.Accent)
        .Lead(Spring.New(0.22f, 0f), Spring.New(0.28f, 0f)).A11yHidden());
    for (int i = 0; i < 6; i++)
    {
        int accent = i;
        row = row.Child(El().Key(names[i]).Role("radio").Label(names[i]).Checked(i == current).Focusable(true)
            .Size(diameter, diameter).Radius(diameter / 2f).Fill(Tok.Accent((nuint)i, dark))
            .Border(paint.Hairline, Color.Black.Alpha(dark ? 0f : 0.08f))
            .OnPress((Settings settings) => settings.Accent = accent));
    }
    return row;
}

static El SpeedControl(Paint paint, float value)
{
    const float width = 180f, height = 22f, knob = 18f, bar = 4f;
    const float travel = width - knob;
    static float ToValue(float x) => Math.Clamp((x - knob / 2f) / travel, 0f, 1f);
    return Row().Key("speed-control").Gap(TokSpace.S3).CenterY()
        .Child(El().Key("slider").Role("slider").Label("Animation speed")
            .RangeValue(value, 0.0, 1.0).RangeStep(0.05).A11yValue(Speed(value))
            .OnStep((Settings settings, int steps, Tx _) => settings.Speed = Math.Clamp(settings.Speed + steps * 0.05f, 0f, 1f))
            .Size(width, height).Motion(TokMotion.Snappy)
            .OnDrag((Settings settings, Drag drag, Tx tx) =>
            {
                switch (drag.Phase)
                {
                    case Phase.Down:
                        drag.Data = settings.Speed;
                        float knobCenter = knob / 2f + value * travel;
                        if (MathF.Abs(drag.X - knobCenter) > knob / 2f + 2f)
                        {
                            settings.Speed = ToValue(drag.X);
                        }
                        break;
                    case Phase.Move:
                        float from = knob / 2f + drag.Data * travel;
                        float grab = MathF.Abs(drag.StartX - from) <= knob / 2f + 2f ? drag.StartX - from : 0f;
                        settings.Speed = ToValue(drag.X - grab);
                        tx.Instant();
                        break;
                    case Phase.Up:
                        if (drag.Cancelled())
                        {
                            settings.Speed = drag.Data;
                        }
                        break;
                }
            })
            .Child(El().Key("track").Size(width, bar).Absolute(0f, (height - bar) / 2f).Radius(bar / 2f)
                .Fill(paint.Theme.ControlOff).A11yHidden())
            .Child(El().Key("fill").Size(knob / 2f + value * travel, bar).Absolute(0f, (height - bar) / 2f).Radius(bar / 2f)
                .Fill(paint.Accent).A11yHidden())
            .Child(El().Key("knob").Size(knob, knob).Absolute(value * travel, (height - knob) / 2f).Radius(knob / 2f)
                .Fill(paint.Theme.Knob).Shadow(1f, 3f, paint.Theme.KnobShadow).Shadow(0f, 0.5f, paint.Theme.Edge).A11yHidden()))
        .Child(Text(Speed(value)).Key("value").W(40f).Font(TokType.MonoCaption)
            .Color(paint.Theme.TextSecondary).Align(Align.End).A11yHidden());
}

enum Appearance { Auto, Light, Dark }

readonly record struct Paint(TokTheme Theme, Color Accent, float Hairline);

sealed class Settings
{
    public Appearance Appearance { get; set; } = Appearance.Auto;
    public int Accent { get; set; }
    public bool LaunchAtLogin { get; set; } = true;
    public bool MenuBar { get; set; } = true;
    public bool Sounds { get; set; }
    public float Speed { get; set; } = 0.5f;
    public bool ReduceMotion { get; set; }
}
