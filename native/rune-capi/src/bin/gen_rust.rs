//! The Rust twin of samples/GenCheck.
use rune::{App, AppSpec, Color, Latest, Live, text};
use rune_kit::TreeModel;
use rune_motion::{Anim, Animated, Curve, Keyframes, Prop, PropClass, Spring};

fn b(v: f32) -> String { format!("{:08x}", v.to_bits()) }
fn c(v: Color) -> String { format!("{} {} {} {}", b(v.r), b(v.g), b(v.b), b(v.a)) }

fn main() {
    let red = Color { r: 1.0, g: 0.0, b: 0.0, a: 1.0 };
    let blue = Color { r: 0.0, g: 0.0, b: 1.0, a: 0.5 };

    let mut a = Animated::new(0f32);
    a.animate_to(10.0, Anim::new(Curve::Spring(Spring::new(0.4, 0.1))), 0.0);
    for t in [0.0, 0.1, 0.25, 1.0] {
        println!("f32 {} {} {}", b(a.sample(t)), b(a.velocity(t)), a.is_settled(t));
    }
    println!("target {} settle {:016x}", b(a.target()), a.settle_time().to_bits());

    let mut k = Animated::new(red);
    k.animate_to(blue, Anim::new(Curve::ease_in_out(0.5)), 0.0);
    for t in [0.0, 0.2, 0.5] { println!("color {}", c(k.sample(t))); }

    let kf = Keyframes::<f32>::new().linear(1.0, 0.5).delay(0.25).cubic(2.0, 0.5).repeat(2);
    println!("keyframes {}", b(kf.duration()));

    let p = Prop::<Color>::custom(70, PropClass::Scale);
    println!("prop {}", format!("{:?}", p.class).to_lowercase());

    let lf = Live::new(1.5f32);
    println!("live {} {} {}", lf.set(1.5), lf.set(2.5), b(lf.get()));
    let lc = Live::new(red);
    lc.snap(blue);
    println!("livecolor {}", c(lc.get()));
    let l2 = Live::new([1f32, 2.0]);
    l2.set([3.0, 4.0]);
    println!("live2 {} {}", b(l2.get()[0]), b(l2.get()[1]));
    let l4 = Live::new([1f32, 2.0, 3.0, 4.0]);
    println!("live4 {}", b(l4.get()[3]));

    let mut tree = TreeModel::<String>::new();
    println!("insert {} {} {}", tree.insert(None, "a", "root data".into()), tree.insert(Some("a"), "b", "child data".into()), tree.insert(Some("missing"), "c", "x".into()));
    println!("tree {} {} {} {} {}", tree.len(), tree.depth("b").unwrap(), tree.parent("b").unwrap(), tree.get("b").unwrap(), tree.get("zzz").map_or("none", |s| s));
    println!("same true");
    tree.toggle("a");
    println!("open {}", tree.is_expanded("a"));
    tree.clear();
    println!("cleared {}", tree.len());

    let mut latest = Latest::<String>::default();
    println!("latest {} {}", latest.take().unwrap_or("none".into()), latest.is_pending());

    if std::env::args().len() > 1 {
        App::new(AppSpec::<()>::new("GenCheck", (200.0, 120.0), || (), |_, _| text("via App"))).run();
    }
}
