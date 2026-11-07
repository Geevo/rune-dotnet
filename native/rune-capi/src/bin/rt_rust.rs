//! The Rust twin of samples/RuntimeCheck.
use rune::tok::{self, type_};
use rune::{Cx, El, Env, Runtime, col, el, text};
use rune_kit::{Kit, PaneState, SplitView, Tree, TreeItem, TreeModel};

fn b(v: f32) -> String { format!("{:08x}", v.to_bits()) }

struct State { clicks: i32, files: TreeModel<String>, pane: PaneState, log: Vec<String> }

impl State {
    fn new() -> State {
        State { clicks: 0, files: TreeModel::new(), pane: PaneState::new(220.0), log: Vec::new() }
    }
}

fn dump(rt: &Runtime<State>, label: &str) {
    let s = &rt.state;
    println!("{label} clicks={} width={} log=[{}]", s.clicks, b(s.pane.width()), s.log.join(","));
    let at = rt.find("button").map_or("none".to_owned(), |(x, y)| format!("{} {}", b(x), b(y)));
    println!("  button {at} src {} main {}", s.files.is_expanded("src"), s.files.is_selected("main"));
}

fn bare(_: &State, _: &mut Cx<State>) -> El {
    col().w_full().h_full().child(el().key("only").size(20.0, 20.0).on_press(|s: &mut State| s.clicks += 1))
}

fn view(s: &State, _: &mut Cx<State>) -> El {
    let k = Kit::neutral(true);
    let tree = Tree::new(|s: &State| &s.files, |s: &mut State| &mut s.files, |o: &String| o.as_str())
        .name("Files")
        .row(|o, _| TreeItem::new(o.clone()).badge("x"))
        .on_select(|s: &mut State, id, _| s.log.push(format!("select:{id}")))
        .on_open(|s: &mut State, id, _| s.log.push(format!("open:{id}")))
        .build(s, &k);
    let main = col().key("main").w_full().h_full().padding(16.0)
        .child(text(format!("Clicks {}", s.clicks)).font(type_::BODY))
        .child(el().key("button").size(80.0, 32.0).fill(tok::accent(0, true)).on_press(|s: &mut State| s.clicks += 1));
    SplitView::new(main).sidebar(&s.pane, |s: &mut State| &mut s.pane, tree).build(&k)
}

fn main() {
    let mut s = State::new();
    s.files.insert(None, "src", "src".to_owned());
    s.files.insert(Some("src"), "main", "main.rs".to_owned());
    s.files.insert(Some("src"), "lib", "lib.rs".to_owned());
    s.files.insert(None, "docs", "docs".to_owned());

    let mut rt = Runtime::new(s, view, (640.0, 400.0), Env::new(true, 2.0));
    rt.frame(0.016);
    dump(&rt, "start");

    for round in 0..3 {
        for iy in 0..20 {
            for ix in 0..16 {
                let (x, y) = (10.0 + 40.0 * ix as f32, 10.0 + 20.0 * iy as f32);
                rt.pointer_move(x, y);
                rt.pointer_down(x, y);
                rt.pointer_up(x, y);
                rt.frame(0.01);
                rt.pointer_down(x, y);
                rt.pointer_up(x, y);
                rt.frame(0.5);
            }
        }
        dump(&rt, &format!("round {round}"));
    }
    for i in 0..20 {
        let x = 200.0 + 2.0 * i as f32;
        rt.pointer_move(x, 200.0);
        rt.pointer_down(x, 200.0);
        rt.frame(0.016);
        rt.pointer_move(x + 60.0, 200.0);
        rt.frame(0.016);
        rt.pointer_up(x + 60.0, 200.0);
        rt.frame(0.5);
    }
    dump(&rt, "dragged");
    rt.update(|s, _| s.clicks += 100);
    rt.frame(0.016);
    dump(&rt, "updated");

    let mut rt2 = Runtime::new(State::new(), bare, (200.0, 100.0), Env::new(false, 1.0));
    rt2.frame(0.016);
    dump(&rt2, "bare");
    rt.frame(0.016);
    dump(&rt, "first again");
}
