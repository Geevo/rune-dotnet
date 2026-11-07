// C# port of rune/examples/todo; native/rune-capi/src/twins/todo_rust.rs is the original.
using System.Collections.Generic;
using System.Linq;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Todo", (360f, 240f), () => new State(), View));

static El View(State state, Cx context)
{
    var theme = Tok.Theme(dark: true);
    var list = Col().Key("list").Gap(4f);
    foreach (var todo in state.Todos)
    {
        uint id = todo.Id;
        var check = El().Key("check").Size(16f, 16f).Radius(8f)
            .Fill(todo.Done ? Tok.Accent(0, dark: true) : theme.ControlOff)
            .OnPress((State state) =>
            {
                var hit = state.Todos.Find(item => item.Id == id);
                if (hit != null)
                {
                    hit.Done = !hit.Done;
                }
            });
        var title = Text(todo.Title).Font(TokType.Body).Grow(1f)
            .Color(todo.Done ? theme.TextSecondary : theme.Text);
        var remove = Text("Remove").Font(TokType.Label).Color(theme.TextSecondary)
            .OnPress((State state) => state.Todos.RemoveAll(item => item.Id == id));
        list = list.Child(
            Row().Key($"todo-{id}").H(36f).Px(12f).Gap(12f).CenterY()
                .Radius(TokRadius.Sm).Fill(theme.Card)
                .Child(check).Child(title).Child(remove));
    }
    var add = Text("Add a todo").Key("add").Font(TokType.Label).Color(Tok.Accent(0, dark: true))
        .OnPress((State state) =>
        {
            state.Todos.Add(new Todo(state.NextId, $"Todo {state.NextId + 1}", false));
            state.NextId++;
        });
    int done = state.Todos.Count(item => item.Done);
    return Col().WFull().HFull().Padding(24f).Gap(12f).Fill(theme.Bg)
        .Child(Progress(done, state.Todos.Count, theme))
        .Child(list.Scroll(new ScrollSpec(true)).Shrink().MinH(0f))
        .Child(add);
}

static El Progress(int done, int total, TokTheme theme)
{
    float width = 312f * done / System.Math.Max(total, 1);
    return El().Key("track").WFull().H(4f).Radius(2f).Fill(theme.ControlOff).Child(
        El().Key("bar").W(width).H(4f).Radius(2f).Fill(Tok.Accent(0, dark: true))
            .Motion(Spring.New(0.4f, 0.2f)));
}

sealed class Todo(uint id, string title, bool done)
{
    public uint Id { get; } = id;
    public string Title { get; } = title;
    public bool Done { get; set; } = done;
}

sealed class State
{
    public List<Todo> Todos { get; } = [new(0, "Write the view", true), new(1, "Run it headless", false), new(2, "Ship it", false)];
    public uint NextId { get; set; } = 3;
}
