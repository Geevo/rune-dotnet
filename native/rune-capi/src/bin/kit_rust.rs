//! samples/KitCheck in plain Rust, the reference its output is compared with.
use rune::tok::{self, type_};
use rune::{AppSpec, Cx, El, LineCap, Shape, Span, canvas, col, rich, row, text};
use rune_kit::{Button, ButtonVariant, Kit};

#[derive(Default)]
struct State {
    count: i32,
}

fn view(s: &State, cx: &mut Cx<State>) -> El {
    let k = Kit::neutral(true);
    let t = tok::theme(true);

    let bold = Span { start: 6, end: 10, weight: Some(700.0), color: Some(tok::accent(0, true)), ..Span::default() };
    let rich = rich(format!("count {:04} done", s.count), vec![bold]).font(type_::BODY).color(t.text);

    let line = canvas(Shape::new().line([0.0, 0.0], [60.0, 24.0], 2.0, LineCap::Round, t.text)).key("line").size(60.0, 24.0);

    let inc = Button::new("Add").variant(ButtonVariant::Contrast).on_press(|s: &mut State| s.count += 1).build(&k).key("inc");
    let ten = Button::new("Add ten").on_press_tx(|s: &mut State, tx| {
        s.count += 10;
        tx.set_title("Ten more");
    }).build(&k).key("ten");
    let custom = Button::new("ignored").label_with(|style, color| text("Custom").font(style).color(color)).build(&k).key("custom");

    col().w_full().h_full().center().gap(12.0).fill(t.bg)
        .child(rich).child(line).child(row().gap(8.0).child(inc).child(ten).child(custom))
        .child(text(if cx.system_dark() { "dark" } else { "light" }).font(type_::LABEL).color(t.text_secondary))
}

fn main() {
    rune::run(AppSpec::<State> {
        scenes: &[("bumped", |s| s.count = 41)],
        ..AppSpec::new("KitCheck", (360.0, 260.0), State::default, view)
    });
}
