// C# port of rune/examples/todo; native/rune-capi/src/twins/todo_rust.rs is the original.
using System.Collections.Generic;
using System.Linq;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Todo", (360f, 240f), () => new State(), View));

static El View(State s, Cx _)
{
    var t = Tok.Theme(true);
    var list = Col().Key("list").Gap(4f);
    foreach (var todo in s.Todos)
    {
        uint id = todo.Id;
        var check = El().Key("check").Size(16f, 16f).Radius(8f)
            .Fill(todo.Done ? Tok.Accent(0, true) : t.ControlOff)
            .OnPress((State s) =>
            {
                var hit = s.Todos.Find(t => t.Id == id);
                if (hit != null)
                {
                    hit.Done = !hit.Done;
                }
            });
        var title = Text(todo.Title).Font(TokType.Body).Grow(1f)
            .Color(todo.Done ? t.TextSecondary : t.Text);
        var remove = Text("Remove").Font(TokType.Label).Color(t.TextSecondary)
            .OnPress((State s) => s.Todos.RemoveAll(t => t.Id == id));
        list = list.Child(
            Row().Key($"todo-{id}").H(36f).Px(12f).Gap(12f).CenterY()
                .Radius(TokRadius.Sm).Fill(t.Card)
                .Child(check).Child(title).Child(remove));
    }
    var add = Text("Add a todo").Key("add").Font(TokType.Label).Color(Tok.Accent(0, true))
        .OnPress((State s) =>
        {
            s.Todos.Add(new Todo(s.Next, $"Todo {s.Next + 1}", false));
            s.Next++;
        });
    int done = s.Todos.Count(t => t.Done);
    return Col().WFull().HFull().Padding(24f).Gap(12f).Fill(t.Bg)
        .Child(Progress(done, s.Todos.Count, t))
        .Child(list.Scroll(new ScrollSpec(true)).Shrink().MinH(0f))
        .Child(add);
}

static El Progress(int done, int total, TokTheme t)
{
    float width = 312f * done / System.Math.Max(total, 1);
    return El().Key("track").WFull().H(4f).Radius(2f).Fill(t.ControlOff).Child(
        El().Key("bar").W(width).H(4f).Radius(2f).Fill(Tok.Accent(0, true))
            .Motion(Spring.New(0.4f, 0.2f)));
}

sealed class Todo(uint id, string title, bool done)
{
    public uint Id = id;
    public string Title = title;
    public bool Done = done;
}

sealed class State
{
    public List<Todo> Todos = [new(0, "Write the view", true), new(1, "Run it headless", false), new(2, "Ship it", false)];
    public uint Next = 3;
}
