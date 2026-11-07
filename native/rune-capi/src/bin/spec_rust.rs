//! samples/SpecCheck in plain Rust, the reference its output is compared with.
use rune::tok::{self, type_};
use rune::{AppSpec, Cx, El, Key, KeyEvent, Tx, Waker, col, text};

struct State {
    woken: bool,
    tenths: i32,
    keys: i32,
    last: String,
}

fn init() -> State {
    State { woken: false, tenths: 0, keys: 0, last: "none".into() }
}

fn setup(s: &mut State, waker: Waker) {
    let other = waker.clone();
    std::thread::spawn(move || other()).join().unwrap();
    waker();
    s.woken = true;
}

fn poll(s: &mut State, seconds: f64, _: &mut Tx) -> bool {
    let tenths = (seconds * 10.0) as i32;
    let changed = tenths != s.tenths;
    s.tenths = tenths;
    changed
}

fn on_key(s: &mut State, ev: &KeyEvent, tx: &mut Tx) -> bool {
    let mut name = match &ev.key {
        Key::Text(t) => format!("text:{t}"),
        Key::Command(c) => format!("cmd:{c}"),
        Key::Function(n) => format!("fn:{n}"),
        other => format!("{other:?}"),
    };
    if ev.mods.shift { name += " +shift"; }
    if ev.is_command('k') { name += " is-k"; }
    tx.set_title(&name);
    s.last = name;
    s.keys += 1;
    true
}

fn view(s: &State, _: &mut Cx<State>) -> El {
    let t = tok::theme(true);
    col().w_full().h_full().center().gap(8.0).fill(t.bg)
        .child(text(if s.woken { "woken" } else { "asleep" }).key("woken").font(type_::BODY).color(t.text))
        .child(text(format!("tenths {}", s.tenths)).key("tenths").font(type_::BODY).color(t.text))
        .child(text(format!("{} keys, last {}", s.keys, s.last)).key("keys").font(type_::BODY).color(t.text))
}

fn main() {
    rune::run(AppSpec::<State> {
        min_size: Some((300.0, 200.0)),
        fonts: &[include_bytes!("../../../../rune/crates/rune/assets/IBMPlexMono-Regular.ttf")],
        families: ("IBM Plex Mono", "IBM Plex Mono"),
        setup: Some(setup),
        poll: Some(poll),
        on_key: Some(on_key),
        ..AppSpec::new("SpecCheck", (360.0, 220.0), init, view)
    });
}
