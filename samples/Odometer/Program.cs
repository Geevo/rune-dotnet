// C# port of rune/examples/odometer; native/rune-capi/src/twins/odometer_rust.rs is the original.
using System.Globalization;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Odometer", (420f, 360f), () => new State(), View));

static string TokensShort(ulong n) =>
    n >= 1_000 ? (n / 1e3).ToString("F1", CultureInfo.InvariantCulture) + "K" : n.ToString(CultureInfo.InvariantCulture);

static El Header(Kit k, State s, string name, bool trailingCost, bool leads)
{
    var usageSpring = Spring.New(0.3f, 0f);
    var font = TextStyle.New(12f, 440f).Lh(16f).Features("'tnum' 1");
    El Label(string key, string t, double v, bool trailing)
    {
        var l = RollingText.New(key, t).Value(v).Font(font).Color(k.C.TextWeak).Spring_(usageSpring);
        return (trailing ? l.Trailing() : l).Build(k);
    }
    var usage = Row().Key($"{name}-usage").Gap(6f).CenterY().Motion(usageSpring);
    if (leads)
    {
        usage = usage.WidthLeads();
    }
    usage = usage
        .Child(El().Size(14f, 14f).Radius(7f).Border(2f, k.C.BorderBase))
        .Child(Label($"{name}-ctx", TokensShort(s.Tokens), s.Tokens, true))
        .Child(Label($"{name}-cost", $"${s.Cents / 100}.{s.Cents % 100:00}", s.Cents, trailingCost));
    return Row().Key(name).W(300f).H(32f).Px(10f).Gap(8f).CenterY().Fill(k.C.BgDeep)
        .Child(Row().Key($"{name}-crumbs").CenterY().Shrink()
            .Child(Text("Refactor the session list so pinned rows keep their place").Font(TextStyle.New(13f, 530f)).Truncate()))
        .Child(Spacer())
        .Child(usage);
}

static El View(State s, Cx _)
{
    var k = Kit.Neutral(true);
    var big = TextStyle.New(28f, 600f).Mono();
    var count = RollingText.New("count", s.N.ToString(CultureInfo.InvariantCulture)).Value(s.N).Font(big).Build(k);
    var time = RollingText.New("secs", $"{s.N}s").Value(s.N).Font(k.Ty.Mono).Trailing().Build(k);
    El Btn(string label, string key, System.Action<State> f) => Button.New(label).OnPress(f).Build(k).Key(key);
    var root = Col().WFull().HFull().Padding(24f).Gap(16f).Fill(k.C.BgBase)
        .Child(Row().Gap(8f)
            .Child(Btn("Up", "up", s => s.N += 1))
            .Child(Btn("Down", "down", s => s.N -= 1))
            .Child(Btn("Step", "step", s =>
            {
                s.Cents += 7;
                s.Tokens += 1_400;
            }))
            .Child(Btn("Big step", "big", s =>
            {
                s.Cents += 996;
                s.Tokens += 98_000;
            })))
        .Child(count)
        .Child(Row().W(160f).JustifyEnd().Motion(RollingText.Spring).Child(time))
        .Child(Header(k, s, "head", true, true))
        .Child(Header(k, s, "head-lead", false, true))
        .Child(Header(k, s, "head-plain", true, false));
    k.Free();
    return root;
}

sealed class State
{
    public long N = 9, Cents = 42;
    public ulong Tokens = 12_345;
}
