// C# port of rune/examples/matched; native/rune-capi/src/twins/matched_rust.rs is the original.
using System;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Matched", (720f, 480f), () => new State(), View)
{
    Scenes = [("grid", _ => { }), ("one", s => s.Zoomed = true)],
    OnKey = OnKey,
});

static El Tile(State s, int i)
{
    string id = $"tile-{i + 1}";
    var t = El().Key(id).Matched(id).Grow(1f).Radius(20f).ClipRounded(20f).Fill(Color.Black)
        .OnPress((State s) =>
        {
            if (s.Zoomed)
            {
                s.Zoomed = false;
            }
            else
            {
                s.Picked = i;
                s.Zoomed = true;
            }
        })
        .Child(El().WFull().HFull().PixelsFit(s.Pictures[i], SurfaceFit.Fill))
        .Child(Text($"{i + 1}  {State.Names[i]}").Font(TextStyle.New(22f, 600f)).Color(Color.White).Absolute(18f, 14f));
    if (s.Picked == i)
    {
        t = t.Child(El().Absolute(0f, 0f).WFull().HFull().Radius(20f).Border(3f, Color.White));
    }
    return t;
}

static El View(State s, Cx _)
{
    var theme = Tok.Theme(true);
    var hint = Text("1–4 pick    ↩ zoom    esc back").Font(TokType.Label).Color(theme.TextSecondary);
    var stage = Col().Key("stage").WFull().Grow(1f);
    if (s.Zoomed)
    {
        stage = stage.Child(Tile(s, s.Picked));
    }
    else
    {
        El Pair(int a) => Row().Key($"row-{a}").Grow(1f).Gap(12f).Child(Tile(s, a)).Child(Tile(s, a + 1));
        stage = stage.Child(Col().Key("grid").Grow(1f).Gap(12f).Transition(Transition.Scale(0.96f))
            .Child(Pair(0)).Child(Pair(2)));
    }
    return Col().WFull().HFull().Padding(24f).Gap(14f).Fill(theme.Bg).Child(hint).Child(stage);
}

static bool OnKey(State s, KeyEvent e, Tx _)
{
    var key = e.Key;
    switch (key.Which)
    {
        case Key.Case.Text when !s.Zoomed && int.TryParse(key.AsText(), out int n) && n is >= 1 and <= 4:
            s.Picked = n - 1;
            return true;
        case Key.Case.Enter:
            s.Zoomed = true;
            return true;
        case Key.Case.Escape:
            s.Zoomed = false;
            return true;
        default:
            return false;
    }
}

sealed class State
{
    public static readonly string[] Names = ["Dusk", "Lake", "Haze", "Dune"];
    static readonly byte[][][] Scenes =
    [
        [[255, 166, 120], [58, 40, 92], [255, 236, 170]],
        [[120, 200, 255], [30, 90, 70], [255, 255, 235]],
        [[196, 150, 255], [40, 30, 70], [255, 200, 240]],
        [[255, 214, 120], [120, 60, 30], [255, 250, 220]],
    ];

    public int Picked;
    public bool Zoomed;
    public Pixels[] Pictures = [Picture(0), Picture(1), Picture(2), Picture(3)];

    static float[] Mix(float[] a, byte[] b, float t)
    {
        var r = new float[3];
        for (int c = 0; c < 3; c++)
        {
            r[c] = a[c] + (b[c] - a[c]) * t;
        }
        return r;
    }

    static float[] F(byte[] a) => [a[0], a[1], a[2]];

    static Pixels Picture(int i)
    {
        const uint w = 320, h = 240;
        byte[] sky = Scenes[i][0], ground = Scenes[i][1], sun = Scenes[i][2];
        byte[] night = [10, 10, 16];
        var rgba = new byte[w * h * 4];
        int at = 0;
        for (uint y = 0; y < h; y++)
        {
            for (uint x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float horizon = 0.62f + 0.04f * MathF.Sin(u * 6f + i);
                float land = Math.Clamp((v - horizon) * h + 0.5f, 0f, 1f);
                var air = Mix(F(sky), ground, MathF.Pow(MathF.Min(v / horizon, 1f), 2.2f) * 0.5f);
                float du = u - 0.3f - 0.12f * i, dv = (v - 0.42f) * 0.75f;
                float d = MathF.Sqrt(du * du + dv * dv);
                float glow = MathF.Pow(Math.Clamp(1f - d / 0.16f, 0f, 1f), 0.6f);
                for (int c = 0; c < 3; c++)
                {
                    air[c] += (sun[c] - air[c]) * glow;
                }
                var soil = Mix(F(ground), night, MathF.Max(v - horizon, 0f) * 1.6f);
                for (int c = 0; c < 3; c++)
                {
                    rgba[at++] = (byte)Math.Clamp(air[c] + (soil[c] - air[c]) * land, 0f, 255f);
                }
                rgba[at++] = 255;
            }
        }
        var p = Pixels.New();
        p.Set(w, h, rgba);
        return p;
    }
}
