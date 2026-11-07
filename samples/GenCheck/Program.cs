// Exercises generic Rune types at their concrete instantiations. Prints exact float bits, so
// the output must match gen_rust byte for byte.
using System;
using Rune;
using static Rune.Ui;

static string Bits(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8");
static string Bits64(double value) => BitConverter.DoubleToUInt64Bits(value).ToString("x16");
static string ColorBits(Color color) => $"{Bits(color.R)} {Bits(color.G)} {Bits(color.B)} {Bits(color.A)}";

var red = new Color(1f, 0f, 0f, 1f);
var blue = new Color(0f, 0f, 1f, 0.5f);

var animated = AnimatedF32.New(0f);
animated.AnimateTo(10f, Anim.New(Curve.Spring(Spring.New(0.4f, 0.1f))), 0.0);
foreach (var time in new[] { 0.0, 0.1, 0.25, 1.0 })
{
    Console.WriteLine($"f32 {Bits(animated.Sample(time))} {Bits(animated.Velocity(time))} {animated.IsSettled(time)}".ToLowerInvariant());
}
Console.WriteLine($"target {Bits(animated.Target())} settle {Bits64(animated.SettleTime())}");
animated.Free();

var animatedColor = AnimatedColor.New(red);
animatedColor.AnimateTo(blue, Anim.New(Curve.EaseInOut(0.5f)), 0.0);
foreach (var time in new[] { 0.0, 0.2, 0.5 })
{
    Console.WriteLine("color " + ColorBits(animatedColor.Sample(time)));
}
animatedColor.Free();

var keyframes = KeyframesF32.New().Linear(1f, 0.5f).Delay(0.25f).Cubic(2f, 0.5f).Repeat(2);
Console.WriteLine("keyframes " + Bits(keyframes.Duration()));
keyframes.Free();

var prop = PropColor.Custom(70, PropClass.Scale);
Console.WriteLine($"prop {prop.Class}".ToLowerInvariant());
prop.Free();

var liveFloat = LiveF32.New(1.5f);
Console.WriteLine($"live {liveFloat.Set(1.5f)} {liveFloat.Set(2.5f)} {Bits(liveFloat.Get())}".ToLowerInvariant());
liveFloat.Free();
var liveColor = LiveColor.New(red);
liveColor.Snap(blue);
Console.WriteLine("livecolor " + ColorBits(liveColor.Get()));
liveColor.Free();
var livePair = LiveF32x2.New((1f, 2f));
livePair.Set((3f, 4f));
Console.WriteLine($"live2 {Bits(livePair.Get().Item1)} {Bits(livePair.Get().Item2)}");
livePair.Free();
var liveQuad = LiveF32x4.New((1f, 2f, 3f, 4f));
Console.WriteLine($"live4 {Bits(liveQuad.Get().Item4)}");
liveQuad.Free();

var tree = TreeModel.New();
var root = "root data";
Console.WriteLine($"insert {tree.Insert(null, "a", root)} {tree.Insert("a", "b", "child data")} {tree.Insert("missing", "c", "x")}".ToLowerInvariant());
Console.WriteLine($"tree {tree.Len()} {tree.Depth("b")} {tree.Parent("b")} {tree.Get("b")} {tree.Get("zzz") ?? "none"}");
Console.WriteLine($"same {ReferenceEquals(tree.Get("a"), root)}".ToLowerInvariant());
tree.Toggle("a");
Console.WriteLine($"open {tree.IsExpanded("a")}".ToLowerInvariant());
tree.Clear();
Console.WriteLine($"cleared {tree.Len()}");
tree.Free();

var latest = Latest.CreateDefault();
Console.WriteLine($"latest {latest.Take() ?? "none"} {latest.IsPending()}".ToLowerInvariant());
latest.Free();

if (args.Length > 0)
{
    var spec = new AppSpec<object>("GenCheck", (200f, 120f), () => new object(), (state, context) => Text("via App"));
    App.New(App.Build(spec)).Run();
}
