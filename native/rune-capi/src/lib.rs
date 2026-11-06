//! C ABI over Rune for the .NET NativeAOT wrapper.
//!
//! Rune's generics collapse here: every `S` is instantiated at `Host`, a handle to the managed
//! state, and `impl Fn` closures become `ManagedFn`, which frees its GC handle when Rune drops it.
//! `El` is a by-value builder, so managed code holds slab ids. The slab is cleared after every
//! view call, so an element the view never attached can't leak.
#![allow(clippy::missing_safety_doc)]

use std::cell::{Cell, RefCell};
use std::collections::{HashMap, HashSet};
use std::ffi::c_void;
use std::sync::OnceLock;

use rune::{AppSpec, Cx, El, KeyEvent, Tx, Waker};

/// How many distinct functions each bare `fn` signature can bind.
const FN_SLOTS: usize = 64;

macro_rules! fn_slots {
    ($i:expr, $at:ident) => { fn_slots!(@ $i, $at, 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 53 54 55 56 57 58 59 60 61 62 63) };
    (@ $i:expr, $at:ident, $($n:literal)*) => { match $i { $($n => $at::<$n>(),)* _ => panic!("fn slot out of range") } };
}

#[allow(unused, unsafe_op_in_unsafe_fn, non_camel_case_types, non_snake_case, clippy::all)]
use rune_ext::ManagedFn;

mod generated;
mod wire;
use wire::*;

pub struct Host {
    state: usize,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Callbacks {
    init: extern "C" fn() -> usize,
    view: extern "C" fn(usize, *mut c_void) -> u32,
    scene: extern "C" fn(u32, usize),
    // Null when the app doesn't use them.
    setup: Option<extern "C" fn(usize, *mut c_void)>,
    poll: Option<extern "C" fn(usize, f64, *mut c_void) -> bool>,
    on_key: Option<extern "C" fn(usize, *const c_void, *mut c_void) -> bool>,
}

static CALLBACKS: OnceLock<Callbacks> = OnceLock::new();

/// Objects can outlive `rune_run` or exist without it, so this registers first.
#[unsafe(no_mangle)]
pub extern "C" fn rune_set_release(release: extern "C" fn(usize)) {
    rune_ext::set_release(release);
}

/// Compares two managed objects, for Rune APIs that need `PartialEq`.
#[unsafe(no_mangle)]
pub extern "C" fn rune_set_equals(equals: extern "C" fn(usize, usize) -> bool) {
    rune_ext::set_equals(equals);
}

fn host() -> &'static Callbacks {
    CALLBACKS.get().expect("rune_run sets the callbacks first")
}

/// A closure passed through here gets the `for<'a> Fn(&'a mut A) -> &'a mut R` signature a `let` would lose.
fn tie_mut<A, R, F: for<'a> Fn(&'a mut A) -> &'a mut R>(f: F) -> F {
    f
}

fn tie_ref<A, R, F: for<'a> Fn(&'a A) -> &'a R>(f: F) -> F {
    f
}

fn tie_str<A, F: for<'a> Fn(&'a A) -> &'a str>(f: F) -> F {
    f
}

/// An element id is a slab slot and, above it, the era it was built in. Each view starts a new era,
/// so an id kept past its view fails instead of naming whatever sits in that slot now.
const SLOT_BITS: u32 = 20;
const SLOT_MASK: u32 = (1 << SLOT_BITS) - 1;
const LAST_ERA: u32 = u32::MAX >> SLOT_BITS;

thread_local! {
    static SLAB: RefCell<Vec<Option<Box<El>>>> = const { RefCell::new(Vec::new()) };
    // Never 0, so `default(El)` is never valid.
    static ERA: Cell<u32> = const { Cell::new(1) };
    static DEPTH: Cell<u32> = const { Cell::new(0) };
}

/// Runs a view: its elements live until the outermost view returns, as nested views share the slab.
fn view_scope(view: impl FnOnce() -> u32) -> El {
    DEPTH.set(DEPTH.get() + 1);
    let root = view();
    let el = *take(root);
    DEPTH.set(DEPTH.get() - 1);
    if DEPTH.get() == 0 {
        SLAB.with_borrow_mut(Vec::clear);
        ERA.set(ERA.get() % LAST_ERA + 1);
    }
    el
}

fn slot(id: u32) -> usize {
    assert!(id >> SLOT_BITS == ERA.get(), "element used outside the view that built it");
    (id & SLOT_MASK) as usize
}

fn put(el: El) -> u32 {
    SLAB.with_borrow_mut(|slab| {
        let slot = slab.len() as u32;
        assert!(slot <= SLOT_MASK, "too many elements in one view");
        slab.push(Some(Box::new(el)));
        ERA.get() << SLOT_BITS | slot
    })
}

fn take(id: u32) -> Box<El> {
    let slot = slot(id);
    SLAB.with_borrow_mut(|slab| slab.get_mut(slot).and_then(Option::take)).expect("element already consumed")
}

/// Applies a by-value builder method to the element in slot `id`.
fn edit(id: u32, f: impl FnOnce(El) -> El) -> u32 {
    let el = f(*take(id));
    let slot = slot(id);
    SLAB.with_borrow_mut(|slab| slab[slot] = Some(Box::new(el)));
    id
}

/// A `HashKey` from C#. Each case hashes exactly as its Rust value does.
enum HashKey<'a> {
    Text(&'a str),
    Signed(i64),
    Unsigned(u64),
    Bytes(&'a [u8]),
}

impl std::hash::Hash for HashKey<'_> {
    fn hash<H: std::hash::Hasher>(&self, state: &mut H) {
        match self {
            Self::Text(v) => v.hash(state),
            Self::Signed(v) => v.hash(state),
            Self::Unsigned(v) => v.hash(state),
            Self::Bytes(v) => v.hash(state),
        }
    }
}

/// The cases are numbered as `HashKey` in Rune.cs numbers them.
unsafe fn hash_key<'a>(kind: u32, number: u64, ptr: *const u8, len: usize) -> HashKey<'a> {
    match kind {
        0 => HashKey::Text(unsafe { utf8(ptr, len) }),
        1 => HashKey::Signed(number as i64),
        2 => HashKey::Unsigned(number),
        // SAFETY: the caller passes a live buffer of `len` bytes.
        _ if len == 0 => HashKey::Bytes(&[]),
        _ => HashKey::Bytes(unsafe { std::slice::from_raw_parts(ptr, len) }),
    }
}

unsafe fn utf8<'a>(ptr: *const u8, len: usize) -> &'a str {
    if len == 0 {
        return "";
    }
    // SAFETY: the caller passes a live UTF-8 buffer of `len` bytes.
    unsafe { std::str::from_utf8_unchecked(std::slice::from_raw_parts(ptr, len)) }
}

/// Rune keeps roles as `&'static str`; each distinct string is leaked once.
fn intern(s: &str) -> &'static str {
    static NAMES: OnceLock<std::sync::Mutex<HashSet<&'static str>>> = OnceLock::new();
    let mut names = NAMES.get_or_init(Default::default).lock().unwrap();
    if let Some(found) = names.get(s) {
        return found;
    }
    let leaked: &'static str = Box::leak(s.to_owned().into_boxed_str());
    names.insert(leaked);
    leaked
}

fn view(s: &Host, cx: &mut Cx<Host>) -> El {
    view_scope(|| (host().view)(s.state, cx as *mut Cx<Host> as *mut c_void))
}

fn init() -> Host {
    Host { state: (host().init)() }
}

fn setup(s: &mut Host, waker: Waker) {
    (host().setup.expect("only set when present"))(s.state, own(waker));
}

fn poll(s: &mut Host, seconds: f64, tx: &mut Tx) -> bool {
    (host().poll.expect("only set when present"))(s.state, seconds, tx as *mut Tx as *mut c_void)
}

fn on_key(s: &mut Host, event: &KeyEvent, tx: &mut Tx) -> bool {
    (host().on_key.expect("only set when present"))(s.state, event as *const KeyEvent as *const c_void, tx as *mut Tx as *mut c_void)
}

/// `AppSpec::scenes` takes plain fn pointers, so each slot is its own monomorphised trampoline.
fn scene<const I: u32>(s: &mut Host) {
    (host().scene)(I, s.state);
}

const SCENES: [fn(&mut Host); 16] = [
    scene::<0>, scene::<1>, scene::<2>, scene::<3>, scene::<4>, scene::<5>, scene::<6>, scene::<7>,
    scene::<8>, scene::<9>, scene::<10>, scene::<11>, scene::<12>, scene::<13>, scene::<14>, scene::<15>,
];

#[repr(C)]
pub struct Utf8 {
    ptr: *const u8,
    len: usize,
}

#[repr(C)]
pub struct Spec {
    title: Utf8,
    width: f32,
    height: f32,
    scenes: *const Utf8,
    scene_count: usize,
    has_min_size: bool,
    min_width: f32,
    min_height: f32,
    fonts: *const Utf8,
    font_count: usize,
    sans: Utf8,
    mono: Utf8,
}

/// The element in slot `id`. Slots are boxed, so the reference survives slab growth; it's only
/// used for the length of one generated call.
unsafe fn el(id: u32) -> &'static mut El {
    let slot = slot(id);
    let ptr: *mut El = SLAB.with_borrow_mut(|slab| &mut **slab[slot].as_mut().expect("element already consumed") as *mut El);
    // SAFETY: the box stays put, and the UI thread is the only one touching the slab.
    unsafe { &mut *ptr }
}

// Objects C# owns, with their types. This catches double consumption, which would otherwise be a
// double free, and a borrowed field handle standing at its owner's address.
static LIVE: std::sync::Mutex<Option<HashMap<usize, &'static str>>> = std::sync::Mutex::new(None);

fn own<T>(value: T) -> *mut c_void {
    // Every box of a zero-sized type shares one address, which LIVE could not tell apart, so those
    // get a word of their own. `Tray` is zero-sized in a build without D-Bus.
    const { assert!(align_of::<T>() <= align_of::<u64>()) };
    let ptr = if size_of::<T>() == 0 {
        std::mem::forget(value);
        Box::into_raw(Box::new(0u64)) as *mut c_void
    } else {
        Box::into_raw(Box::new(value)) as *mut c_void
    };
    LIVE.lock().unwrap().get_or_insert_default().insert(ptr as usize, std::any::type_name::<T>());
    ptr
}

unsafe fn unown<T>(ptr: *mut c_void) -> T {
    let mut live = LIVE.lock().unwrap();
    let live = live.get_or_insert_default();
    match live.get(&(ptr as usize)) {
        Some(&ty) if ty == std::any::type_name::<T>() => live.remove(&(ptr as usize)),
        Some(ty) => panic!("a borrowed {} is not an owned {ty}", std::any::type_name::<T>()),
        None => panic!("object already consumed"),
    };
    // SAFETY: `own` made this box, and the set says it hasn't been taken back. A zero-sized value
    // has no bytes, so reading one from the word's aligned address is sound.
    unsafe {
        if size_of::<T>() == 0 {
            let value = std::ptr::read(ptr as *const T);
            drop(Box::from_raw(ptr as *mut u64));
            value
        } else {
            *Box::from_raw(ptr as *mut T)
        }
    }
}

unsafe fn slice_of<'a, T>(ptr: *const T, len: usize) -> &'a [T] {
    if len == 0 { return &[]; }
    // SAFETY: the caller passes a live buffer of `len` items.
    unsafe { std::slice::from_raw_parts(ptr, len) }
}

thread_local! {
    static RETURNED: RefCell<String> = const { RefCell::new(String::new()) };
}

thread_local! {
    static RETURNED_OBJECT: RefCell<Option<ManagedFn>> = const { RefCell::new(None) };
}

/// Keeps a returned object alive until the next one on this thread; C# reads it at once.
fn ret_managed(value: ManagedFn) -> usize {
    let id = value.id();
    RETURNED_OBJECT.with_borrow_mut(|r| *r = Some(value));
    id
}

/// Keeps a returned string alive until the next one on this thread; C# copies it at once.
fn ret_str(s: &str) -> Utf8 {
    RETURNED.with_borrow_mut(|r| {
        r.clear();
        r.push_str(s);
        Utf8 { ptr: r.as_ptr(), len: r.len() }
    })
}

/// Builds an owned `AppSpec`, copying everything it needs. Null on too many scenes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn rune_build_spec(callbacks: *const Callbacks, spec: *const Spec) -> *mut c_void {
    // SAFETY: both pointers are live for this call.
    let (callbacks, spec) = unsafe { (*callbacks, &*spec) };
    if spec.scene_count > SCENES.len() {
        return std::ptr::null_mut();
    }
    let _ = CALLBACKS.set(callbacks);
    let title: &'static str = Box::leak(unsafe { utf8(spec.title.ptr, spec.title.len) }.to_owned().into_boxed_str());
    let scenes: Vec<(&'static str, fn(&mut Host))> = (0..spec.scene_count)
        .map(|i| {
            // SAFETY: `scenes` holds `scene_count` entries.
            let name = unsafe { let n = &*spec.scenes.add(i); utf8(n.ptr, n.len) };
            (intern(name), SCENES[i])
        })
        .collect();
    let fonts: Vec<&'static [u8]> = (0..spec.font_count)
        .map(|i| {
            // SAFETY: `fonts` holds `font_count` entries of live bytes, which Rune keeps forever.
            let font = unsafe { let f = &*spec.fonts.add(i); slice_of(f.ptr, f.len) };
            &*Box::leak(font.to_vec().into_boxed_slice())
        })
        .collect();
    // SAFETY: both names are live for this call.
    let families = unsafe { (intern(utf8(spec.sans.ptr, spec.sans.len)), intern(utf8(spec.mono.ptr, spec.mono.len))) };
    own(AppSpec::<Host> {
        scenes: Box::leak(scenes.into_boxed_slice()),
        min_size: spec.has_min_size.then_some((spec.min_width, spec.min_height)),
        fonts: Box::leak(fonts.into_boxed_slice()),
        families,
        setup: callbacks.setup.map(|_| setup as fn(&mut Host, Waker)),
        poll: callbacks.poll.map(|_| poll as fn(&mut Host, f64, &mut Tx) -> bool),
        on_key: callbacks.on_key.map(|_| on_key as fn(&mut Host, &KeyEvent, &mut Tx) -> bool),
        ..AppSpec::new(title, (spec.width, spec.height), init, view)
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn rune_scene_limit() -> u32 {
    SCENES.len() as u32
}

/// Runs the app on the calling thread until its window closes, or renders headless for Rune's
/// tooling arguments. Consumes `spec`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn rune_run(spec: *mut c_void) {
    // SAFETY: `spec` came from `rune_build_spec`.
    rune::run(unsafe { unown::<AppSpec<Host>>(spec) });
}

/// Panics and catches inside Rust, as Rune's image decoder and present worker do. Returns 1 when
/// unwinding works in the final link.
#[unsafe(no_mangle)]
pub extern "C" fn rune_selftest_unwind() -> u32 {
    let hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(|_| {}));
    let caught = std::panic::catch_unwind(|| panic!("selftest")).is_err();
    std::panic::set_hook(hook);
    caught as u32
}

/// Wakes the app. Safe from any thread.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn rune_waker_wake(waker: *const c_void) {
    // SAFETY: `waker` came from `own::<Waker>` and hasn't been consumed.
    (unsafe { &*(waker as *const Waker) })();
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn rune_waker_clone(waker: *const c_void) -> *mut c_void {
    // SAFETY: as in `rune_waker_wake`.
    own(unsafe { &*(waker as *const Waker) }.clone())
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn rune_waker_free(waker: *mut c_void) {
    // SAFETY: as in `rune_waker_wake`.
    drop(unsafe { unown::<Waker>(waker) });
}
