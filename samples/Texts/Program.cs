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
    var q = query.ToLowerInvariant();
    return Enumerable.Range(0, State.Notes.Length).Where(i => State.Notes[i].ToLowerInvariant().Contains(q)).ToList();
}

static string KeyName(Key k) => k.Which switch
{
    Key.Case.Text => $"text:{k.AsText()}",
    Key.Case.Command => $"cmd:{k.AsCommand()}",
    Key.Case.Function => $"fn:{k.AsFunction()}",
    Key.Case.Enter or Key.Case.Backspace or Key.Case.Left or Key.Case.Right or Key.Case.Up or Key.Case.Down
        or Key.Case.Escape or Key.Case.Tab => k.Which.ToString(),
    _ => "other",
};

static string B(bool b) => b ? "true" : "false";

static void OpenPicked(State s, Tx _)
{
    var found = Matches(s.Query.Text);
    if (s.Picked >= found.Count)
    {
        return;
    }
    s.Say($"open {State.Notes[found[s.Picked]]}");
    s.Query.Clear();
}

static El View(State s, Cx _)
{
    var t = Tok.Theme(true);
    var accent = Tok.Accent(0, true);
    var k = Kit.Neutral(true);
    var found = Matches(s.Query.Text);
    var field = TextInput(s.Query, TokType.Body.Lh(26f)).Key("query").SingleLine().Grow(1f)
        .Color(t.Text).Placeholder("Search notes", t.TextSecondary)
        .OnEdit((State s) => s.Query)
        .OnInputKey((State s, KeyEvent ev, Tx tx) =>
        {
            int n = Matches(s.Query.Text).Count;
            s.Say($"input {KeyName(ev.Key)} shift={B(ev.Mods.Shift)} in {tx.Focused() ?? ""}");
            switch (ev.Key.Which)
            {
                case Key.Case.Down when n > 0:
                    s.Picked = (s.Picked + 1) % n;
                    break;
                case Key.Case.Up when n > 0:
                    s.Picked = (s.Picked + n - 1) % n;
                    break;
                case Key.Case.Escape:
                    s.Query.Clear();
                    break;
                default:
                    return false;
            }
            return true;
        })
        .OnInputChange((State s, Tx _) =>
        {
            s.Picked = 0;
            s.Say($"changed \"{s.Query.Text}\" {s.Query.Anchor}..{s.Query.Focus}");
        })
        .OnSubmit<State>(OpenPicked);
    var panel = Col().Key("panel").WFull().Radius(14f).ClipRounded(14f)
        .Fill(t.Card).Border(1f, Color.White.Alpha(0.1f))
        .Child(Row().H(44f).Px(18f).Gap(12f).CenterY()
            .Child(Text("⌕").Font(TokType.Title.Lh(26f)).Color(t.TextSecondary))
            .Child(field));
    if (found.Count > 0)
    {
        var results = Col().Key("results").Padding(6f).Gap(2f);
        for (int r = 0; r < found.Count; r++)
        {
            int i = found[r];
            bool on = r == s.Picked;
            results = results.Child(Row().Key($"result-{i}").H(26f).Px(12f).Radius(8f).CenterY()
                .Fill(on ? accent.Alpha(0.85f) : Color.Transparent)
                .Child(Text(State.Notes[i]).Font(TokType.Body).Color(on ? Color.White : t.Text)));
        }
        panel = panel.Child(El().H(1f).WFull().Fill(t.Separator)).Child(results);
    }
    var form = Col().Key("form").Gap(8f)
        .Child(Rune.TextInput.New(s.Name).Placeholder("Name").ClearButton()
            .OnEdit((State s) => s.Name)
            .OnChange((State s, Tx _) => s.Say($"name \"{s.Name.Text}\""))
            .OnSubmit((State s, Tx _) => s.Say("name submit"))
            .Build(k).Key("name"))
        .Child(TextField.New("Email", s.Email).Placeholder("you@example.com")
            .OnEdit((State s) => s.Email).Build(k).Key("email"))
        .Child(TextArea.New(s.NotesEdit).Rows(3).OnEdit((State s) => s.NotesEdit)
            .OnChange((State s, Tx _) => s.Say($"notes {Encoding.UTF8.GetByteCount(s.NotesEdit.Text)} bytes"))
            .Build(k).Key("notes"));
    var actions = Row().Key("actions").Gap(8f)
        .Child(Button.New("Undo").OnPress((State s) => s.NotesEdit.Undo()).Build(k).Key("undo"))
        .Child(Button.New("Set").OnPress((State s) => s.Name.Text = "Ünï 🌍").Build(k).Key("set"))
        .Child(Button.New("All").OnPressTx((State _, Tx tx) => tx.SelectAllText("name")).Build(k).Key("all"))
        .Child(Button.New("Blur").OnPressTx((State _, Tx tx) => tx.Blur()).Build(k).Key("blur"));
    var line = Rich("héllo wörld, plain", [Span.New(0, 6).Bold(), Span.New(7, 13).Italic_()])
        .Key("rich").Font(TokType.Body).Color(t.Text);
    var log = Col().Key("log").Gap(2f);
    for (int i = 0; i < s.Log.Count; i++)
    {
        log = log.Child(Text(s.Log[i]).Key($"log-{i}").Font(TokType.Label).Color(t.TextSecondary));
    }
    k.Free();
    return Col().WFull().HFull().Padding(16f).Gap(10f).Fill(t.Bg)
        .Child(panel).Child(form).Child(actions).Child(line).Child(log);
}

static bool OnKey(State s, KeyEvent e, Tx tx)
{
    s.Say($"app {KeyName(e.Key)} cmd={B(e.Mods.Cmd)} rep={B(tx.KeyRepeat())}");
    return false;
}

sealed class State
{
    public static readonly string[] Notes = ["Groceries", "Trip to Lisbon", "Reading list", "Ideas"];
    public TextEdit Query = NewQuery(), Name = TextEdit.New("Zoë"), Email = TextEdit.New(""), NotesEdit = TextEdit.New("first\nsecond");
    public int Picked;
    public List<string> Log = [];

    static TextEdit NewQuery()
    {
        var q = TextEdit.New("");
        q.Focused = true;
        return q;
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
