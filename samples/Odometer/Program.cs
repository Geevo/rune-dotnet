// C# port of rune/examples/odometer; native/rune-capi/src/twins/odometer_rust.rs is the original.
using System.Globalization;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Odometer", (420f, 360f), () => new State(), View));

static string TokensShort(ulong count) =>
    count >= 1_000 ? (count / 1e3).ToString("F1", CultureInfo.InvariantCulture) + "K" : count.ToString(CultureInfo.InvariantCulture);

static El Header(Kit kit, State state, string name, bool trailingCost, bool leads)
{
    var usageSpring = Spring.New(0.3f, 0f);
    var font = TextStyle.New(12f, 440f).Lh(16f).Features("'tnum' 1");
    El Label(string key, string text, double value, bool trailing)
    {
        var rolling = RollingText.New(key, text).Value(value).Font(font).Color(kit.C.TextWeak).Spring_(usageSpring);
        return (trailing ? rolling.Trailing() : rolling).Build(kit);
    }
    var usage = Row().Key($"{name}-usage").Gap(6f).CenterY().Motion(usageSpring);
    if (leads)
    {
        usage = usage.WidthLeads();
    }
    usage = usage
        .Child(El().Size(14f, 14f).Radius(7f).Border(2f, kit.C.BorderBase))
        .Child(Label($"{name}-ctx", TokensShort(state.Tokens), state.Tokens, true))
        .Child(Label($"{name}-cost", $"${state.Cents / 100}.{state.Cents % 100:00}", state.Cents, trailingCost));
    return Row().Key(name).W(300f).H(32f).Px(10f).Gap(8f).CenterY().Fill(kit.C.BgDeep)
        .Child(Row().Key($"{name}-crumbs").CenterY().Shrink()
            .Child(Text("Refactor the session list so pinned rows keep their place").Font(TextStyle.New(13f, 530f)).Truncate()))
        .Child(Spacer())
        .Child(usage);
}

static El View(State state, Cx context)
{
    var kit = Kit.Neutral(true);
    var big = TextStyle.New(28f, 600f).Mono();
    var count = RollingText.New("count", state.Count.ToString(CultureInfo.InvariantCulture)).Value(state.Count).Font(big).Build(kit);
    var time = RollingText.New("secs", $"{state.Count}s").Value(state.Count).Font(kit.Ty.Mono).Trailing().Build(kit);
    El StepButton(string label, string key, System.Action<State> onPress) => Button.New(label).OnPress(onPress).Build(kit).Key(key);
    var root = Col().WFull().HFull().Padding(24f).Gap(16f).Fill(kit.C.BgBase)
        .Child(Row().Gap(8f)
            .Child(StepButton("Up", "up", state => state.Count += 1))
            .Child(StepButton("Down", "down", state => state.Count -= 1))
            .Child(StepButton("Step", "step", state =>
            {
                state.Cents += 7;
                state.Tokens += 1_400;
            }))
            .Child(StepButton("Big step", "big", state =>
            {
                state.Cents += 996;
                state.Tokens += 98_000;
            })))
        .Child(count)
        .Child(Row().W(160f).JustifyEnd().Motion(RollingText.Spring).Child(time))
        .Child(Header(kit, state, "head", true, true))
        .Child(Header(kit, state, "head-lead", false, true))
        .Child(Header(kit, state, "head-plain", true, false));
    kit.Free();
    return root;
}

sealed class State
{
    public long Count { get; set; } = 9;
    public long Cents { get; set; } = 42;
    public ulong Tokens { get; set; } = 12_345;
}
