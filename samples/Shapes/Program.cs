// C# port of rune/examples/shapes, without Shape.fill and Shape.stroke (unbound: a sequence of
// [f32; 2]). native/rune-capi/src/twins/shapes_rust.rs is the original with the same cut.
using System;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Shapes", (820f, 360f), () => new State(), View)
{
    Poll = (s, t, _) =>
    {
        s.T = (float)t;
        return true;
    },
});

static float Level(float t, int i)
{
    float x = i * 0.09f + t * 2f;
    return MathF.Min(MathF.Abs(MathF.Sin(x) * 0.6f + MathF.Sin(x * 2.7f) * 0.3f + MathF.Cos(x * 7.1f) * 0.1f), 1f);
}

static Shape Waveform(float w, float h)
{
    float mid = h / 2f;
    return Shape.New().Line((0f, mid), (w, mid), 0.5f, LineCap.Butt, K.Ink).ViewBox(w, h);
}

static Shape Meters(float t)
{
    var s = Shape.New();
    for (int i = 0; i < 24; i++)
    {
        float x = 4f + i * 10f;
        float v = Level(t, i * 7) * 56f + 4f;
        s = s.Line((x, 64f), (x, 64f - v), 6f, LineCap.Round, v > 48f ? K.Warm : K.Accent);
    }
    return s;
}

static Shape Curves()
{
    var wave = Path.New()
        .MoveTo((4f, 50f))
        .CubicTo((40f, -20f), (80f, 120f), (116f, 50f))
        .QuadTo((150f, 0f), (186f, 50f));
    var blob = Path.New()
        .MoveTo((210f, 30f))
        .CubicTo((230f, 0f), (270f, 0f), (280f, 30f))
        .LineTo((260f, 80f))
        .LineTo((220f, 80f))
        .Close();
    var shape = Shape.New()
        .StrokePath(wave, 3f, LineCap.Round, K.Warm)
        .FillPath(blob, K.Accent)
        .StrokePath(blob, 1f, LineCap.Round, K.Ink)
        .Line((400f, 10f), (460f, 86f), 0.5f, LineCap.Butt, K.Ink)
        .Line((410f, 10f), (470f, 86f), 1f, LineCap.Butt, K.Ink)
        .Line((420f, 10f), (480f, 86f), 2f, LineCap.Butt, K.Ink);
    wave.Free();
    blob.Free();
    return shape;
}

static El Label(string s) => Text(s).Color(K.Ink);

static El View(State s, Cx _) =>
    Col().Key("root").Padding(24f).Gap(16f).Fill(new Color(0.06f, 0.065f, 0.08f, 1f))
        .Child(Label("Waveform: filled envelope, polyline, hairline"))
        .Child(Canvas(Waveform(260f, 96f)).Key("wave").Size(520f, 96f)
            .Fill(new Color(0.1f, 0.11f, 0.13f, 1f)).Radius(8f))
        .Child(Row().Gap(24f)
            .Child(Col().Gap(8f).Child(Label("Meters"))
                .Child(Canvas(Meters(s.T)).Key("meters").Size(244f, 68f)))
            .Child(Col().Gap(8f).Child(Label("Paths, a concave fill, line widths"))
                .Child(Canvas(Curves()).Key("curves").Size(490f, 96f))));

static class K
{
    public static readonly Color Ink = new(0.92f, 0.93f, 0.95f, 1f);
    public static readonly Color Accent = new(0.35f, 0.55f, 1f, 1f);
    public static readonly Color Warm = new(1f, 0.55f, 0.3f, 1f);
}

sealed class State { public float T; }
