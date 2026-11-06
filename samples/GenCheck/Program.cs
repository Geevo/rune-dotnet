// Exercises generic Rune types at their concrete instantiations. Prints exact float bits, so
// the output must match gen_rust byte for byte.
using System;
using Rune;
using static Rune.Ui;

static string B(float v) => BitConverter.SingleToUInt32Bits(v).ToString("x8");
static string B2(double v) => BitConverter.DoubleToUInt64Bits(v).ToString("x16");
static string C(Color c) => $"{B(c.R)} {B(c.G)} {B(c.B)} {B(c.A)}";

var red = new Color(1f, 0f, 0f, 1f);
var blue = new Color(0f, 0f, 1f, 0.5f);

var a = AnimatedF32.New(0f);
a.AnimateTo(10f, Anim.New(Curve.Spring(Spring.New(0.4f, 0.1f))), 0.0);
foreach (var t in new[] { 0.0, 0.1, 0.25, 1.0 })
{
    Console.WriteLine($"f32 {B(a.Sample(t))} {B(a.Velocity(t))} {a.IsSettled(t)}".ToLowerInvariant());
}
Console.WriteLine($"target {B(a.Target())} settle {B2(a.SettleTime())}");
a.Free();

var c = AnimatedColor.New(red);
c.AnimateTo(blue, Anim.New(Curve.EaseInOut(0.5f)), 0.0);
foreach (var t in new[] { 0.0, 0.2, 0.5 })
{
    Console.WriteLine("color " + C(c.Sample(t)));
}
c.Free();

var k = KeyframesF32.New().Linear(1f, 0.5f).Delay(0.25f).Cubic(2f, 0.5f).Repeat(2);
Console.WriteLine("keyframes " + B(k.Duration()));
k.Free();

var p = PropColor.Custom(70, PropClass.Scale);
Console.WriteLine($"prop {p.Class}".ToLowerInvariant());
p.Free();

var lf = LiveF32.New(1.5f);
Console.WriteLine($"live {lf.Set(1.5f)} {lf.Set(2.5f)} {B(lf.Get())}".ToLowerInvariant());
lf.Free();
var lc = LiveColor.New(red);
lc.Snap(blue);
Console.WriteLine("livecolor " + C(lc.Get()));
lc.Free();
var l2 = LiveF32x2.New((1f, 2f));
l2.Set((3f, 4f));
Console.WriteLine($"live2 {B(l2.Get().Item1)} {B(l2.Get().Item2)}");
l2.Free();
var l4 = LiveF32x4.New((1f, 2f, 3f, 4f));
Console.WriteLine($"live4 {B(l4.Get().Item4)}");
l4.Free();

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
    var spec = new AppSpec<object>("GenCheck", (200f, 120f), () => new object(), (s, cx) => Text("via App"));
    App.New(App.Build(spec)).Run();
}
