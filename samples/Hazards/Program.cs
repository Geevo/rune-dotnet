// What misuse of value-looking handles does. HAZ picks the case; each one is a C# habit that the
// Rust type system rules out. Run: HAZ=name out/hazards/hazards --snap x.png
using System;
using Rune;
using static Rune.Ui;

var mode = Environment.GetEnvironmentVariable("HAZ") ?? "";

App.Run(new AppSpec<State>("Hazards", (300f, 200f), () => new State(), (s, cx) => View(s, cx, mode))
{
    OnKey = (s, e, tx) =>
    {
        s.Held = e.Key;
        s.HeldTx = tx;
        s.Frames++;
        return true;
    },
    Poll = (s, t, tx) => mode == "handles",
});

static El View(State s, Cx cx, string mode)
{
    var root = Col().Key("root").WFull().HFull().Padding(10f).OnPress((State s) => s.Frames++);
    root = root.Child(Text($"frame {s.Frames}").Key("count"));
    switch (mode)
    {
        case "el-twice":
            var e = Text("twice");
            root = root.Child(e).Child(e);
            break;
        case "el-static":
            // A cached element is gone after its view. Its old id is the next view's third slot.
            if (s.Frames == 0)
            {
                State.Cached = Text("cached");
                root = root.Child(State.Cached.Value);
            }
            else
            {
                _ = Text("never added to the tree");
                root = root.Child(State.Cached!.Value);
            }
            break;
        case "held-key":
            if (s.Frames > 0)
            {
                Console.Error.WriteLine($"held key: {s.Held.Which}");
            }
            break;
        case "handles":
            for (int i = 0; i < 500; i++)
            {
                int n = i;
                root = root.Child(El().Key($"h{i}").OnPress((State s) => s.Frames += n));
            }
            if (s.Frames % 200 == 0)
            {
                Console.Error.WriteLine($"heap {GC.GetTotalMemory(true) / 1024} KiB at frame {s.Frames}");
            }
            s.Frames++;
            break;
        case "transition-reuse":
            var tr = Transition.Scale(0.9f);
            root = root.Child(El().Key("a").Transition(tr)).Child(El().Key("b").Transition(tr));
            break;
        case "span-reuse":
            Span[] spans = [Span.New(0, 2).Bold()];
            root = root.Child(Rich("ab", spans)).Child(Rich("cd", spans));
            break;
        case "builder-reuse":
            var k = Kit.Neutral(true);
            var b = Button.New("x");
            root = root.Child(b.Build(k)).Child(b.Build(k));
            k.Free();
            break;
        case "builder-old":
            var k2 = Kit.Neutral(true);
            var b2 = Button.New("x");
            var b3 = b2.OnPress((State s) => s.Frames++);
            root = root.Child(b2.Build(k2));
            k2.Free();
            break;
        case "free-twice":
            var p = Path.New();
            p.Free();
            p.Free();
            break;
        case "free-borrowed":
            var k3 = Kit.Neutral(true);
            k3.Ty.Free();
            break;
        case "set-borrowed":
            var k4 = Kit.Neutral(true);
            var k5 = Kit.Neutral(false);
            k4.Elevation = k5.Elevation;
            root = root.Child(Button.New("x").Build(k4));
            k4.Free();
            k5.Free();
            break;
        case "default-struct":
            root = root.Child(default(El));
            break;
        case "default-object":
            var none = default(Path);
            root = root.Child(Canvas(Shape.New().FillPath(none, Color.White)));
            break;
        case "throw":
            throw new InvalidOperationException("from the view");
        case "off-thread":
            El made = default;
            var t = new System.Threading.Thread(() => made = Text("other thread"));
            t.Start();
            t.Join();
            root = root.Child(made);
            break;
        case "text-nul":
            root = root.Child(Text("nul\0inside")).Child(Text("lone\ud83dsurrogate"));
            break;
    }
    return root;
}

sealed class State
{
    public static El? Cached;
    public Key Held;
    public Tx HeldTx;
    public int Frames;
}
