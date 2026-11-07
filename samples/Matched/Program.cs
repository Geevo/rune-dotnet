// C# port of rune/examples/matched; native/rune-capi/src/twins/matched_rust.rs is the original.
using System;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Matched", (720f, 480f), () => new State(), View)
{
    Scenes = [("grid", _ => { }), ("one", state => state.Zoomed = true)],
    OnKey = OnKey,
});

static El Tile(State state, int index)
{
    string id = $"tile-{index + 1}";
    var tile = El().Key(id).Matched(id).Grow(1f).Radius(20f).ClipRounded(20f).Fill(Color.Black)
        .OnPress((State state) =>
        {
            if (state.Zoomed)
            {
                state.Zoomed = false;
            }
            else
            {
                state.Picked = index;
                state.Zoomed = true;
            }
        })
        .Child(El().WFull().HFull().PixelsFit(state.Pictures[index], SurfaceFit.Fill))
        .Child(Text($"{index + 1}  {State.Names[index]}").Font(TextStyle.New(22f, 600f)).Color(Color.White).Absolute(18f, 14f));
    if (state.Picked == index)
    {
        tile = tile.Child(El().Absolute(0f, 0f).WFull().HFull().Radius(20f).Border(3f, Color.White));
    }
    return tile;
}

static El View(State state, Cx context)
{
    var theme = Tok.Theme(dark: true);
    var hint = Text("1–4 pick    ↩ zoom    esc back").Font(TokType.Label).Color(theme.TextSecondary);
    var stage = Col().Key("stage").WFull().Grow(1f);
    if (state.Zoomed)
    {
        stage = stage.Child(Tile(state, state.Picked));
    }
    else
    {
        El Pair(int first) => Row().Key($"row-{first}").Grow(1f).Gap(12f).Child(Tile(state, first)).Child(Tile(state, first + 1));
        stage = stage.Child(Col().Key("grid").Grow(1f).Gap(12f).Transition(Transition.Scale(0.96f))
            .Child(Pair(0)).Child(Pair(2)));
    }
    return Col().WFull().HFull().Padding(24f).Gap(14f).Fill(theme.Bg).Child(hint).Child(stage);
}

static bool OnKey(State state, KeyEvent keyEvent, Tx tx)
{
    var key = keyEvent.Key;
    switch (key.Which)
    {
        case Key.Case.Text when !state.Zoomed && int.TryParse(key.AsText(), out int number) && number is >= 1 and <= 4:
            state.Picked = number - 1;
            return true;
        case Key.Case.Enter:
            state.Zoomed = true;
            return true;
        case Key.Case.Escape:
            state.Zoomed = false;
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

    public int Picked { get; set; }
    public bool Zoomed { get; set; }
    public Pixels[] Pictures { get; } = [Picture(0), Picture(1), Picture(2), Picture(3)];

    static float[] Mix(float[] from, byte[] to, float amount)
    {
        var result = new float[3];
        for (int c = 0; c < 3; c++)
        {
            result[c] = from[c] + (to[c] - from[c]) * amount;
        }
        return result;
    }

    static float[] ToFloats(byte[] rgb) => [rgb[0], rgb[1], rgb[2]];

    static Pixels Picture(int index)
    {
        const uint width = 320, height = 240;
        byte[] sky = Scenes[index][0], ground = Scenes[index][1], sun = Scenes[index][2];
        byte[] night = [10, 10, 16];
        var rgba = new byte[width * height * 4];
        int at = 0;
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                float u = x / (float)width, v = y / (float)height;
                float horizon = 0.62f + 0.04f * MathF.Sin(u * 6f + index);
                float land = Math.Clamp((v - horizon) * height + 0.5f, 0f, 1f);
                var air = Mix(ToFloats(sky), ground, MathF.Pow(MathF.Min(v / horizon, 1f), 2.2f) * 0.5f);
                float du = u - 0.3f - 0.12f * index, dv = (v - 0.42f) * 0.75f;
                float distance = MathF.Sqrt(du * du + dv * dv);
                float glow = MathF.Pow(Math.Clamp(1f - distance / 0.16f, 0f, 1f), 0.6f);
                for (int c = 0; c < 3; c++)
                {
                    air[c] += (sun[c] - air[c]) * glow;
                }
                var soil = Mix(ToFloats(ground), night, MathF.Max(v - horizon, 0f) * 1.6f);
                for (int c = 0; c < 3; c++)
                {
                    rgba[at++] = (byte)Math.Clamp(air[c] + (soil[c] - air[c]) * land, 0f, 255f);
                }
                rgba[at++] = 255;
            }
        }
        var pixels = Pixels.New();
        pixels.Set(width, height, rgba);
        return pixels;
    }
}
