// C# port of native/rune-capi/src/twins/texts_rust.rs, the launcher example's search panel with
// rune-kit's text widgets. Every edit and key lands in an on-screen log.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<State>("Texts", (460f, 640f), () => new State(), View) { OnKey = OnKey });

static List<int> Matches(string query)
{
    var needle = query.ToLowerInvariant();
    return Enumerable.Range(0, State.Notes.Length).Where(i => State.Notes[i].ToLowerInvariant().Contains(needle)).ToList();
}

static string KeyName(Key key) => key.Which switch
{
    Key.Case.Text => $"text:{key.AsText()}",
    Key.Case.Command => $"cmd:{key.AsCommand()}",
    Key.Case.Function => $"fn:{key.AsFunction()}",
    Key.Case.Enter or Key.Case.Backspace or Key.Case.Left or Key.Case.Right or Key.Case.Up or Key.Case.Down
        or Key.Case.Escape or Key.Case.Tab => key.Which.ToString(),
    _ => "other",
};

static string Lower(bool value) => value ? "true" : "false";

static void OpenPicked(State state, Tx tx)
{
    var found = Matches(state.Query.Text);
    if (state.Picked >= found.Count)
    {
        return;
    }
    state.Say($"open {State.Notes[found[state.Picked]]}");
    state.Query.Clear();
}

static El View(State state, Cx context)
{
    var theme = Tok.Theme(dark: true);
    var accent = Tok.Accent(0, dark: true);
    var kit = Kit.Neutral(true);
    var found = Matches(state.Query.Text);
    var field = TextInput(state.Query, TokType.Body.Lh(26f)).Key("query").SingleLine().Grow(1f)
        .Color(theme.Text).Placeholder("Search notes", theme.TextSecondary)
        .OnEdit((State state) => state.Query)
        .OnInputKey((State state, KeyEvent keyEvent, Tx tx) =>
        {
            int count = Matches(state.Query.Text).Count;
            state.Say($"input {KeyName(keyEvent.Key)} shift={Lower(keyEvent.Mods.Shift)} in {tx.Focused() ?? ""}");
            switch (keyEvent.Key.Which)
            {
                case Key.Case.Down when count > 0:
                    state.Picked = (state.Picked + 1) % count;
                    break;
                case Key.Case.Up when count > 0:
                    state.Picked = (state.Picked + count - 1) % count;
                    break;
                case Key.Case.Escape:
                    state.Query.Clear();
                    break;
                default:
                    return false;
            }
            return true;
        })
        .OnInputChange((State state, Tx _) =>
        {
            state.Picked = 0;
            state.Say($"changed \"{state.Query.Text}\" {state.Query.Anchor}..{state.Query.Focus}");
        })
        .OnSubmit<State>(OpenPicked);
    var panel = Col().Key("panel").WFull().Radius(14f).ClipRounded(14f)
        .Fill(theme.Card).Border(1f, Color.White.Alpha(0.1f))
        .Child(Row().H(44f).Px(18f).Gap(12f).CenterY()
            .Child(Text("⌕").Font(TokType.Title.Lh(26f)).Color(theme.TextSecondary))
            .Child(field));
    if (found.Count > 0)
    {
        var results = Col().Key("results").Padding(6f).Gap(2f);
        for (int row = 0; row < found.Count; row++)
        {
            int note = found[row];
            bool picked = row == state.Picked;
            results = results.Child(Row().Key($"result-{note}").H(26f).Px(12f).Radius(8f).CenterY()
                .Fill(picked ? accent.Alpha(0.85f) : Color.Transparent)
                .Child(Text(State.Notes[note]).Font(TokType.Body).Color(picked ? Color.White : theme.Text)));
        }
        panel = panel.Child(El().H(1f).WFull().Fill(theme.Separator)).Child(results);
    }
    var form = Col().Key("form").Gap(8f)
        .Child(Rune.TextInput.New(state.Name).Placeholder("Name").ClearButton()
            .OnEdit((State state) => state.Name)
            .OnChange((State state, Tx _) => state.Say($"name \"{state.Name.Text}\""))
            .OnSubmit((State state, Tx _) => state.Say("name submit"))
            .Build(kit).Key("name"))
        .Child(TextField.New("Email", state.Email).Placeholder("you@example.com")
            .OnEdit((State state) => state.Email).Build(kit).Key("email"))
        .Child(TextArea.New(state.NotesEdit).Rows(3).OnEdit((State state) => state.NotesEdit)
            .OnChange((State state, Tx _) => state.Say($"notes {Encoding.UTF8.GetByteCount(state.NotesEdit.Text)} bytes"))
            .Build(kit).Key("notes"));
    var actions = Row().Key("actions").Gap(8f)
        .Child(Button.New("Undo").OnPress((State state) => state.NotesEdit.Undo()).Build(kit).Key("undo"))
        .Child(Button.New("Set").OnPress((State state) => state.Name.Text = "Ünï 🌍").Build(kit).Key("set"))
        .Child(Button.New("All").OnPressTx((State _, Tx tx) => tx.SelectAllText("name")).Build(kit).Key("all"))
        .Child(Button.New("Blur").OnPressTx((State _, Tx tx) => tx.Blur()).Build(kit).Key("blur"));
    var line = Rich("héllo wörld, plain", [Span.New(0, 6).Bold(), Span.New(7, 13).Italic_()])
        .Key("rich").Font(TokType.Body).Color(theme.Text);
    var log = Col().Key("log").Gap(2f);
    for (int i = 0; i < state.Log.Count; i++)
    {
        log = log.Child(Text(state.Log[i]).Key($"log-{i}").Font(TokType.Label).Color(theme.TextSecondary));
    }
    kit.Free();
    return Col().WFull().HFull().Padding(16f).Gap(10f).Fill(theme.Bg)
        .Child(panel).Child(form).Child(actions).Child(line).Child(log);
}

static bool OnKey(State state, KeyEvent keyEvent, Tx tx)
{
    state.Say($"app {KeyName(keyEvent.Key)} cmd={Lower(keyEvent.Mods.Cmd)} rep={Lower(tx.KeyRepeat())}");
    return false;
}

sealed class State
{
    public static readonly string[] Notes = ["Groceries", "Trip to Lisbon", "Reading list", "Ideas"];
    // TextEdit is a handle struct, so these stay fields: setting a member through a property's copy doesn't compile.
    public TextEdit Query = NewQuery(), Name = TextEdit.New("Zoë"), Email = TextEdit.New(""), NotesEdit = TextEdit.New("first\nsecond");
    public int Picked { get; set; }
    public List<string> Log { get; } = [];

    static TextEdit NewQuery()
    {
        var query = TextEdit.New("");
        query.Focused = true;
        return query;
    }

    public void Say(string line)
    {
        Log.Add(line);
        if (Log.Count > 7)
        {
            Log.RemoveAt(0);
        }
    }
}
