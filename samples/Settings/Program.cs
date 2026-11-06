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
        ("light", s => s.Appearance = Appearance.Light),
        ("dark", s => s.Appearance = Appearance.Dark),
        ("reduced-motion", s => s.ReduceMotion = true),
        ("toggles-off", s =>
        {
            s.LaunchAtLogin = false;
            s.MenuBar = false;
            s.Sounds = false;
        }),
        ("accent-pink", s => s.Accent = 2),
        ("slow", s => s.Speed = 0f),
    ],
});

static float SpeedOf(float v) => MathF.Pow(2f, v * 2f - 1f);
static string Speed(float v) => SpeedOf(v).ToString("F2", CultureInfo.InvariantCulture) + "×";

static El View(Settings s, Cx cx)
{
    bool dark = s.Appearance switch
    {
        Appearance.Auto => cx.SystemDark(),
        Appearance.Light => false,
        _ => true,
    };
    cx.ReducedMotion(s.ReduceMotion);
    cx.TimeScale(SpeedOf(s.Speed));
    var p = new Paint(Tok.Theme(dark), Tok.Accent((nuint)s.Accent, dark), 1f / cx.Env().Scale);
    var t = p.T;

    return Col().Key("window").Role("window").Fill(t.Bg)
        .Child(Row().Key("titlebar").H(40f).WFull().Center()
            .Child(Text("Settings").Key("title").Font(TokType.Title).Color(t.Text)))
        .Child(Col().Key("content").Grow(1f).Px(TokSpace.S6).Pt(TokSpace.S1).Pb(TokSpace.S6).Gap(TokSpace.S6)
            .ClipFade(0f, TokSpace.S1, 0f, TokSpace.S6)
            .Child(Section(p, "appearance", "Appearance",
                SettingRow(p, "theme", "Theme", null, Segmented(p, s.Appearance)),
                SettingRow(p, "accent", "Accent colour", null, Swatches(p, s.Accent, dark))))
            .Child(Section(p, "general", "General",
                ToggleRow(p, "login", "Launch at login", s.LaunchAtLogin, s => s.LaunchAtLogin = !s.LaunchAtLogin),
                ToggleRow(p, "menubar", "Show in menu bar", s.MenuBar, s => s.MenuBar = !s.MenuBar),
                ToggleRow(p, "sounds", "Play sounds", s.Sounds, s => s.Sounds = !s.Sounds)))
            .Child(Section(p, "motion", "Motion",
                SettingRow(p, "speed", "Animation speed", null, SpeedControl(p, s.Speed)),
                ToggleRow(p, "reduce", "Reduce motion", s.ReduceMotion, s => s.ReduceMotion = !s.ReduceMotion))));
}

static El Section(Paint p, string key, string title, params El[] rows)
{
    var card = Col().Key("card").Padding(TokSpace.S1).Fill(p.T.Card).Radius(TokRadius.Lg)
        .Border(p.Hair, p.T.CardBorder).Shadow(1f, 2f, p.T.Shadow).Shadow(8f, 24f, p.T.ShadowSoft);
    for (int i = 0; i < rows.Length; i++)
    {
        card = card.Child(rows[i]);
        if (i + 1 < rows.Length)
        {
            card = card.Child(El().Key($"sep{i}").H(p.Hair).Ml(TokSpace.S3).Mr(TokSpace.S3).Fill(p.T.Separator).A11yHidden());
        }
    }
    return Col().Key(key).Gap(TokSpace.S2)
        .Child(Text(title).Key("header").Font(TokType.Section).Color(p.T.TextSecondary).Ml(TokSpace.S3))
        .Child(card);
}

static El SettingRow(Paint p, string key, string label, Action<Settings>? onPress, El control)
{
    var r = Row().Key(key).H(44f).Px(TokSpace.S3).CenterY().JustifyBetween().Radius(TokRadius.Md).Label(label)
        .Child(Text(label).Key("label").Font(TokType.Body).Color(p.T.Text).A11yHidden())
        .Child(control);
    if (onPress != null)
    {
        r = r.Hover(p.T.Hover).Press(p.T.Press).Focusable(true).OnPress(onPress);
    }
    return r;
}

static El ToggleRow(Paint p, string key, string label, bool on, Action<Settings> flip) =>
    SettingRow(p, key, label, flip, Toggle(p, on).A11yHidden()).Role("switch").Checked(on);

static El Toggle(Paint p, bool on)
{
    const float w = 38f, h = 22f, k = 18f;
    const float inset = (h - k) / 2f;
    return El().Key("switch").Role("switch").Size(w, h).Radius(h / 2f)
        .Fill(on ? p.Accent : p.T.ControlOff).Motion(TokMotion.Snappy)
        .Child(El().Key("knob").Size(k, k).Absolute(on ? w - k - inset : inset, inset).Radius(k / 2f)
            .Fill(p.T.Knob).Shadow(1f, 3f, p.T.KnobShadow).Shadow(0f, 0.5f, p.T.Edge));
}

static El Segmented(Paint p, Appearance current)
{
    const float w = 205f, h = 28f, pad = 2f;
    const float seg = (w - 2f * pad) / 3f;
    (Appearance, string)[] all = [(Appearance.Auto, "Auto"), (Appearance.Light, "Light"), (Appearance.Dark, "Dark")];
    int idx = Math.Max(Array.FindIndex(all, a => a.Item1 == current), 0);
    var segs = Row().Key("segments");
    for (int i = 0; i < all.Length; i++)
    {
        var (a, name) = all[i];
        bool selected = i == idx;
        segs = segs.Child(
            El().Key(name).Role("segment").Label(name).Checked(selected).Focusable(true)
                .Size(seg, h - 2f * pad).Center().Radius(TokRadius.Sm)
                .Hover(selected ? Color.Transparent : p.T.Hover)
                .OnPressTx((Settings s, Tx tx) =>
                {
                    s.Appearance = a;
                    tx.ColorMotion(TokMotion.Calm);
                })
                .Child(Text(name).Key("label").Font(TokType.Label)
                    .Color(selected ? p.T.Text : p.T.TextSecondary).A11yHidden()));
    }
    return El().Key("segmented").Role("segmented").Size(w, h).Padding(pad).Radius(TokRadius.Md)
        .Fill(p.T.ControlTrack).Motion(TokMotion.Snappy)
        .Child(El().Key("indicator").Size(seg, h - 2f * pad).Absolute(pad + idx * seg, pad)
            .Radius(TokRadius.Sm).Fill(p.T.Indicator).Shadow(1f, 3f, p.T.Shadow).Shadow(0f, 0.5f, p.T.Edge)
            .Lead(TokMotion.Lead, TokMotion.Default).A11yHidden())
        .Child(segs);
}

static El Swatches(Paint p, int current, bool dark)
{
    const float d = 18f, gap = 12f, ringW = 2f;
    const float ring = d + 2f * (ringW + 2f);
    // tok::ACCENT_NAMES is unbound.
    string[] names = ["blue", "purple", "pink", "orange", "green", "graphite"];
    var r = Row().Key("swatches").Role("radiogroup").Label("Accent colour").Gap(gap).Motion(TokMotion.Snappy);
    r = r.Child(El().Key("ring").Size(ring, ring)
        .Absolute(current * (d + gap) - (ring - d) / 2f, -(ring - d) / 2f)
        .Radius(ring / 2f).Border(ringW, p.Accent)
        .Lead(Spring.New(0.22f, 0f), Spring.New(0.28f, 0f)).A11yHidden());
    for (int i = 0; i < 6; i++)
    {
        int at = i;
        r = r.Child(El().Key(names[i]).Role("radio").Label(names[i]).Checked(i == current).Focusable(true)
            .Size(d, d).Radius(d / 2f).Fill(Tok.Accent((nuint)i, dark))
            .Border(p.Hair, Color.Black.Alpha(dark ? 0f : 0.08f))
            .OnPress((Settings s) => s.Accent = at));
    }
    return r;
}

static El SpeedControl(Paint p, float v)
{
    const float w = 180f, h = 22f, k = 18f, bar = 4f;
    const float travel = w - k;
    static float ToValue(float x) => Math.Clamp((x - k / 2f) / travel, 0f, 1f);
    return Row().Key("speed-control").Gap(TokSpace.S3).CenterY()
        .Child(El().Key("slider").Role("slider").Label("Animation speed")
            .RangeValue(v, 0.0, 1.0).RangeStep(0.05).A11yValue(Speed(v))
            .OnStep((Settings s, int steps, Tx _) => s.Speed = Math.Clamp(s.Speed + steps * 0.05f, 0f, 1f))
            .Size(w, h).Motion(TokMotion.Snappy)
            .OnDrag((Settings s, Drag d, Tx tx) =>
            {
                switch (d.Phase)
                {
                    case Phase.Down:
                        d.Data = s.Speed;
                        float knob = k / 2f + v * travel;
                        if (MathF.Abs(d.X - knob) > k / 2f + 2f)
                        {
                            s.Speed = ToValue(d.X);
                        }
                        break;
                    case Phase.Move:
                        float from = k / 2f + d.Data * travel;
                        float grab = MathF.Abs(d.StartX - from) <= k / 2f + 2f ? d.StartX - from : 0f;
                        s.Speed = ToValue(d.X - grab);
                        tx.Instant();
                        break;
                    case Phase.Up:
                        if (d.Cancelled())
                        {
                            s.Speed = d.Data;
                        }
                        break;
                }
            })
            .Child(El().Key("track").Size(w, bar).Absolute(0f, (h - bar) / 2f).Radius(bar / 2f)
                .Fill(p.T.ControlOff).A11yHidden())
            .Child(El().Key("fill").Size(k / 2f + v * travel, bar).Absolute(0f, (h - bar) / 2f).Radius(bar / 2f)
                .Fill(p.Accent).A11yHidden())
            .Child(El().Key("knob").Size(k, k).Absolute(v * travel, (h - k) / 2f).Radius(k / 2f)
                .Fill(p.T.Knob).Shadow(1f, 3f, p.T.KnobShadow).Shadow(0f, 0.5f, p.T.Edge).A11yHidden()))
        .Child(Text(Speed(v)).Key("value").W(40f).Font(TokType.MonoCaption)
            .Color(p.T.TextSecondary).Align(Align.End).A11yHidden());
}

enum Appearance { Auto, Light, Dark }

readonly record struct Paint(TokTheme T, Color Accent, float Hair);

sealed class Settings
{
    public Appearance Appearance = Appearance.Auto;
    public int Accent;
    public bool LaunchAtLogin = true, MenuBar = true, Sounds;
    public float Speed = 0.5f;
    public bool ReduceMotion;
}
