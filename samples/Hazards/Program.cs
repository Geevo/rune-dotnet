// What misuse of value-looking handles does. HAZ picks the case; each one is a C# habit that the
// Rust type system rules out. Run: HAZ=name out/hazards/hazards --snap x.png
using System;
using Rune;
using static Rune.Ui;

var mode = Environment.GetEnvironmentVariable("HAZ") ?? "";

try
{
    Run();
}
catch (Exception e) when (mode == "throw-caught")
{
    Console.Error.WriteLine($"caught {e.GetType().Name}: {e.Message}");
}

void Run() => App.Run(new AppSpec<State>("Hazards", (300f, 200f), () => new State(), (state, context) => View(state, context, mode))
{
    OnKey = (state, keyEvent, tx) =>
    {
        state.Held = keyEvent.Key;
        state.HeldTx = tx;
        state.Frames++;
        return true;
    },
    Poll = (state, time, tx) => mode == "handles",
});

static El View(State state, Cx context, string mode)
{
    var root = Col().Key("root").WFull().HFull().Padding(10f).OnPress((State state) => state.Frames++);
    root = root.Child(Text($"frame {state.Frames}").Key("count"));
    switch (mode)
    {
        case "el-twice":
            var element = Text("twice");
            root = root.Child(element).Child(element);
            break;
        case "el-static":
            // A cached element is gone after its view. Its old id is the next view's third slot.
            if (state.Frames == 0)
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
            if (state.Frames > 0)
            {
                Console.Error.WriteLine($"held key: {state.Held.Which}");
            }
            break;
        case "handles":
            for (int i = 0; i < 500; i++)
            {
                int step = i;
                root = root.Child(El().Key($"h{i}").OnPress((State state) => state.Frames += step));
            }
            if (state.Frames % 200 == 0)
            {
                Console.Error.WriteLine($"heap {GC.GetTotalMemory(true) / 1024} KiB at frame {state.Frames}");
            }
            state.Frames++;
            break;
        case "transition-reuse":
            var transition = Transition.Scale(0.9f);
            root = root.Child(El().Key("a").Transition(transition)).Child(El().Key("b").Transition(transition));
            break;
        case "span-reuse":
            Span[] spans = [Span.New(0, 2).Bold()];
            root = root.Child(Rich("ab", spans)).Child(Rich("cd", spans));
            break;
        case "builder-reuse":
        {
            var kit = Kit.Neutral(true);
            var button = Button.New("x");
            root = root.Child(button.Build(kit)).Child(button.Build(kit));
            kit.Free();
            break;
        }
        case "builder-old":
        {
            var kit = Kit.Neutral(true);
            var original = Button.New("x");
            var withPress = original.OnPress((State state) => state.Frames++);
            root = root.Child(original.Build(kit));
            kit.Free();
            break;
        }
        case "free-twice":
            var path = Path.New();
            path.Free();
            path.Free();
            break;
        case "free-borrowed":
        {
            var kit = Kit.Neutral(true);
            kit.Ty.Free();
            break;
        }
        case "set-borrowed":
        {
            var darkKit = Kit.Neutral(true);
            var lightKit = Kit.Neutral(false);
            darkKit.Elevation = lightKit.Elevation;
            root = root.Child(Button.New("x").Build(darkKit));
            darkKit.Free();
            lightKit.Free();
            break;
        }
        case "default-struct":
            root = root.Child(default(El));
            break;
        case "default-object":
            var none = default(Path);
            root = root.Child(Canvas(Shape.New().FillPath(none, Color.White)));
            break;
        case "throw":
        case "throw-caught":
            throw new InvalidOperationException("from the view");
        case "panic-recovered":
            // The panic unwinds Rust back to this call, so the view goes on without it.
            var once = Text("once");
            root = root.Child(once);
            try
            {
                Col().Child(once);
            }
            catch (RunePanicException e)
            {
                Console.Error.WriteLine($"recovered: {e.Message}");
            }
            var freed = Path.New();
            freed.Free();
            try
            {
                freed.Free();
            }
            catch (RunePanicException e)
            {
                Console.Error.WriteLine($"recovered: {e.Message}");
            }
            Path.New().Free();
            break;
        case "off-thread":
            El made = default;
            var thread = new System.Threading.Thread(() => made = Text("other thread"));
            thread.Start();
            thread.Join();
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
