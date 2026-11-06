//! The launcher example's search panel as one window, with rune-kit's text widgets and a rich
//! line. samples/Texts is the same app in C#. Every edit and key lands in an on-screen log.

use rune::tok::{self, type_};
use rune::{AppSpec, Color, Cx, El, Key, KeyEvent, Span, TextEdit, Tx, col, el, rich, row, text, text_input};
use rune_kit::{Button, Kit, TextArea, TextField, TextInput};

const NOTES: [&str; 4] = ["Groceries", "Trip to Lisbon", "Reading list", "Ideas"];

struct State {
    query: TextEdit,
    name: TextEdit,
    email: TextEdit,
    notes: TextEdit,
    picked: usize,
    log: Vec<String>,
}

fn init() -> State {
    let mut query = TextEdit::new("");
    query.focused = true;
    State { query, name: TextEdit::new("Zoë"), email: TextEdit::new(""), notes: TextEdit::new("first\nsecond"), picked: 0, log: vec![] }
}

fn matches(query: &str) -> Vec<usize> {
    let q = query.to_lowercase();
    (0..NOTES.len()).filter(|&i| NOTES[i].to_lowercase().contains(&q)).collect()
}

fn key_name(k: &Key) -> String {
    match k {
        Key::Text(t) => format!("text:{t}"),
        Key::Command(c) => format!("cmd:{c}"),
        Key::Function(n) => format!("fn:{n}"),
        Key::Enter => "Enter".into(),
        Key::Backspace => "Backspace".into(),
        Key::Left => "Left".into(),
        Key::Right => "Right".into(),
        Key::Up => "Up".into(),
        Key::Down => "Down".into(),
        Key::Escape => "Escape".into(),
        Key::Tab => "Tab".into(),
        _ => "other".into(),
    }
}

fn say(s: &mut State, line: String) {
    s.log.push(line);
    if s.log.len() > 7 {
        s.log.remove(0);
    }
}

fn open_picked(s: &mut State, _: &mut Tx) {
    let Some(&i) = matches(&s.query.text).get(s.picked) else { return };
    say(s, format!("open {}", NOTES[i]));
    s.query.clear();
}

fn view(s: &State, _: &mut Cx<State>) -> El {
    let t = tok::theme(true);
    let accent = tok::accent(0, true);
    let k = Kit::neutral(true);
    let found = matches(&s.query.text);
    let field = text_input(&s.query, type_::BODY.lh(26.0)).key("query").single_line().grow(1.0)
        .color(t.text).placeholder("Search notes", t.text_secondary)
        .on_edit(|s: &mut State| &mut s.query)
        .on_input_key(|s: &mut State, ev: &KeyEvent, tx: &mut Tx| {
            let n = matches(&s.query.text).len();
            let focused = tx.focused().map(str::to_owned).unwrap_or_default();
            say(s, format!("input {} shift={} in {focused}", key_name(&ev.key), ev.mods.shift));
            match ev.key {
                Key::Down if n > 0 => s.picked = (s.picked + 1) % n,
                Key::Up if n > 0 => s.picked = (s.picked + n - 1) % n,
                Key::Escape => s.query.clear(),
                _ => return false,
            }
            true
        })
        .on_input_change(|s: &mut State, _| {
            s.picked = 0;
            let line = format!("changed {:?} {}..{}", s.query.text, s.query.anchor, s.query.focus);
            say(s, line);
        })
        .on_submit(open_picked);
    let mut panel = col().key("panel").w_full().radius(14.0).clip_rounded(14.0)
        .fill(t.card).border(1.0, Color::WHITE.alpha(0.1))
        .child(row().h(44.0).px(18.0).gap(12.0).center_y()
            .child(text("⌕").font(type_::TITLE.lh(26.0)).color(t.text_secondary))
            .child(field));
    if !found.is_empty() {
        let mut results = col().key("results").padding(6.0).gap(2.0);
        for (row_i, &i) in found.iter().enumerate() {
            let on = row_i == s.picked;
            results = results.child(row().key(format!("result-{i}")).h(26.0).px(12.0).radius(8.0).center_y()
                .fill(if on { accent.alpha(0.85) } else { Color::TRANSPARENT })
                .child(text(NOTES[i]).font(type_::BODY).color(if on { Color::WHITE } else { t.text })));
        }
        panel = panel.child(el().h(1.0).w_full().fill(t.separator)).child(results);
    }
    let form = col().key("form").gap(8.0)
        .child(TextInput::new(&s.name).placeholder("Name").clear_button()
            .on_edit(|s: &mut State| &mut s.name)
            .on_change(|s: &mut State, _: &mut Tx| {
                let line = format!("name {:?}", s.name.text);
                say(s, line);
            })
            .on_submit(|s: &mut State, _: &mut Tx| say(s, "name submit".into()))
            .build(&k).key("name"))
        .child(TextField::new("Email", &s.email).placeholder("you@example.com")
            .on_edit(|s: &mut State| &mut s.email).build(&k).key("email"))
        .child(TextArea::new(&s.notes).rows(3).on_edit(|s: &mut State| &mut s.notes)
            .on_change(|s: &mut State, _: &mut Tx| {
                let line = format!("notes {} bytes", s.notes.text.len());
                say(s, line);
            })
            .build(&k).key("notes"));
    let actions = row().key("actions").gap(8.0)
        .child(Button::new("Undo").on_press(|s: &mut State| s.notes.undo()).build(&k).key("undo"))
        .child(Button::new("Set").on_press(|s: &mut State| s.name.text = "Ünï 🌍".into()).build(&k).key("set"))
        .child(Button::new("All").on_press_tx(|_: &mut State, tx: &mut Tx| tx.select_all_text("name")).build(&k).key("all"))
        .child(Button::new("Blur").on_press_tx(|_: &mut State, tx: &mut Tx| tx.blur()).build(&k).key("blur"));
    let line = rich("héllo wörld, plain", vec![Span::new(0, 6).bold(), Span::new(7, 13).italic()])
        .key("rich").font(type_::BODY).color(t.text);
    let mut log = col().key("log").gap(2.0);
    for (i, l) in s.log.iter().enumerate() {
        log = log.child(text(l.clone()).key(format!("log-{i}")).font(type_::LABEL).color(t.text_secondary));
    }
    col().w_full().h_full().padding(16.0).gap(10.0).fill(t.bg)
        .child(panel).child(form).child(actions).child(line).child(log)
}

fn on_key(s: &mut State, e: &KeyEvent, tx: &mut Tx) -> bool {
    let line = format!("app {} cmd={} rep={}", key_name(&e.key), e.mods.cmd, tx.key_repeat());
    say(s, line);
    false
}

fn main() {
    rune::run(AppSpec::<State> { on_key: Some(on_key), ..AppSpec::new("Texts", (460.0, 640.0), init, view) });
}
