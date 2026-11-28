//! Rust signatures the generator can read, one for each Rune API it can't bind as written: a trait
//! becomes closures, a generic becomes `ManagedFn` or a string, and a borrow becomes a copy or a
//! visitor. `covers:` names the APIs a function stands in for; COVERAGE.md counts them from that.

use std::future::Future;
use std::io::{self, Write};
use std::path::PathBuf;
use std::sync::{Arc, OnceLock};
use std::task::{Context, Poll, Wake, Waker as TaskWaker};

use rune::compose::{Compositor, Frame};
use rune::motion_gate::{Allow, AllowRow, Cell, Job, Recording};
use rune::prefs::{Prefs, PrefsError};
use rune::render::cpu_boot::{Scene, Target};
use rune::render::{EffectPhase, EffectSite, Renderer};
use rune::{App, AppSpec, ConfiguredOverlay, Cx, El, FileDialogError, FileDialogOptions, FileDialogs, KeyEvent, NativeSurface, OverlaySpec, Runtime, ShortcutError, Tx, Waker};
use rune_kit::shell::RowStatus;
use rune_kit::{FileNode, FileTree, Kit, Palette, ShortcutRecorder, TreeItem, TreeModel, TreeRow};

static RELEASE: OnceLock<extern "C" fn(usize)> = OnceLock::new();
static EQUALS: OnceLock<extern "C" fn(usize, usize) -> bool> = OnceLock::new();

#[doc(hidden)]
pub fn set_release(release: extern "C" fn(usize)) {
    let _ = RELEASE.set(release);
}

#[doc(hidden)]
pub fn set_equals(equals: extern "C" fn(usize, usize) -> bool) {
    let _ = EQUALS.set(equals);
}

/// A managed object Rune owns; dropping the last clone frees the GC handle. Some Rune APIs take
/// `impl Fn + Clone`, hence the `Arc`. Equality is the object's own `Equals`.
#[doc(hidden)]
#[derive(Clone)]
pub struct ManagedFn(Arc<Delegate>);

struct Delegate(usize);

impl ManagedFn {
    pub fn new(id: usize) -> Self {
        Self(Arc::new(Delegate(id)))
    }

    /// Closures capture disjoint fields, so reading `.0` in one would copy the id and drop the
    /// `ManagedFn` early. A `&self` method makes the closure own the whole value.
    pub fn id(&self) -> usize {
        self.0.0
    }
}

impl PartialEq for ManagedFn {
    fn eq(&self, other: &Self) -> bool {
        (EQUALS.get().expect("the C# module initializer registers this"))(self.id(), other.id())
    }
}

impl Drop for Delegate {
    fn drop(&mut self) {
        (RELEASE.get().expect("the C# module initializer registers this"))(self.0);
    }
}

// Function pointer fields

/// covers: AppSpec.init
pub fn spec_set_init<S: 'static>(spec: &mut AppSpec<S>, f: fn() -> S) { spec.init = f; }
/// covers: AppSpec.view
pub fn spec_set_view<S: 'static>(spec: &mut AppSpec<S>, f: fn(&S, &mut Cx<S>) -> El) { spec.view = f; }
/// Sets every scene at once. The list is `'static`, so each call leaks one copy of it. covers: AppSpec.scenes
pub fn spec_set_scenes<S: 'static>(spec: &mut AppSpec<S>, names: Vec<String>, runs: Vec<fn(&mut S)>) {
    assert_eq!(names.len(), runs.len(), "each scene needs a name and a function");
    let scenes: Vec<(&'static str, fn(&mut S))> = names.into_iter().map(|n| &*n.leak()).zip(runs).collect();
    spec.scenes = scenes.leak();
}
/// covers: AppSpec.setup
pub fn spec_set_setup<S: 'static>(spec: &mut AppSpec<S>, f: fn(&mut S, Waker)) { spec.setup = Some(f); }
pub fn spec_clear_setup<S: 'static>(spec: &mut AppSpec<S>) { spec.setup = None; }
/// covers: AppSpec.poll
pub fn spec_set_poll<S: 'static>(spec: &mut AppSpec<S>, f: fn(&mut S, f64, &mut Tx) -> bool) { spec.poll = Some(f); }
pub fn spec_clear_poll<S: 'static>(spec: &mut AppSpec<S>) { spec.poll = None; }
/// covers: AppSpec.on_key
pub fn spec_set_on_key<S: 'static>(spec: &mut AppSpec<S>, f: fn(&mut S, &KeyEvent, &mut Tx) -> bool) { spec.on_key = Some(f); }
pub fn spec_clear_on_key<S: 'static>(spec: &mut AppSpec<S>) { spec.on_key = None; }
/// covers: Runtime.poll
pub fn runtime_set_poll<S: 'static>(rt: &mut Runtime<S>, f: fn(&mut S, f64, &mut Tx) -> bool) { rt.poll = Some(f); }
pub fn runtime_clear_poll<S: 'static>(rt: &mut Runtime<S>) { rt.poll = None; }
/// covers: Runtime.on_key
pub fn runtime_set_on_key<S: 'static>(rt: &mut Runtime<S>, f: fn(&mut S, &KeyEvent, &mut Tx) -> bool) { rt.on_key = Some(f); }
pub fn runtime_clear_on_key<S: 'static>(rt: &mut Runtime<S>) { rt.on_key = None; }
/// covers: ConfiguredOverlay.on_key
pub fn overlay_set_on_key<S: 'static>(o: &mut ConfiguredOverlay<S>, f: fn(&mut S, &KeyEvent, &mut Tx) -> bool) { o.on_key = Some(f); }
pub fn overlay_clear_on_key<S: 'static>(o: &mut ConfiguredOverlay<S>) { o.on_key = None; }
/// covers: ConfiguredOverlay.on_created
pub fn overlay_set_on_created<S: 'static>(o: &mut ConfiguredOverlay<S>, f: impl FnMut(rune::OverlayCreated) + 'static) { o.on_created = Some(Box::new(f)); }
pub fn overlay_clear_on_created<S: 'static>(o: &mut ConfiguredOverlay<S>) { o.on_created = None; }
/// covers: WindowSpec.on_created
pub fn window_spec_set_on_created<S: 'static>(w: &mut rune::WindowSpec<S>, f: impl FnOnce(rune::WindowHandle) + 'static) { w.on_created = Some(Box::new(f)); }
pub fn window_spec_clear_on_created<S: 'static>(w: &mut rune::WindowSpec<S>) { w.on_created = None; }
/// covers: OverlaySpec.view
pub fn overlay_spec_set_view<S: 'static>(spec: &mut OverlaySpec<S>, f: fn(&S, &mut Cx<S>) -> El) { spec.view = f; }
/// covers: Kit.ring
pub fn kit_set_ring(kit: &mut Kit, f: fn(&Palette) -> rune::Color) { kit.ring = Some(f); }
pub fn kit_clear_ring(kit: &mut Kit) { kit.ring = None; }
/// covers: Channel.embed
pub fn channel_set_embed(c: &mut rune::motion_gate::check::paint::Channel, f: fn(&rune::motion_gate::check::paint::Paint) -> [f32; 6]) { c.embed = f; }
/// covers: Channel.weight
pub fn channel_set_weight(c: &mut rune::motion_gate::check::paint::Channel, f: fn(&rune::motion_gate::check::paint::Paint, &[f32; 4]) -> f32) { c.weight = f; }

// Traits as closures

struct Closures<S: 'static, F, R, A, M> { features: F, render: R, animating: A, summary: M, state: std::marker::PhantomData<fn(S)> }

impl<S: 'static, F, R, A, M> Compositor<S> for Closures<S, F, R, A, M>
where
    F: Fn() -> wgpu::Features + 'static,
    R: FnMut(&mut Frame<'_, S>) + 'static,
    A: Fn() -> bool + 'static,
    M: FnMut(&wgpu::Device) -> Option<String> + 'static,
{
    fn features(&self) -> wgpu::Features { (self.features)() }
    fn render(&mut self, f: &mut Frame<'_, S>) { (self.render)(f) }
    fn animating(&self) -> bool { (self.animating)() }
    fn summary(&mut self, device: &wgpu::Device) -> Option<String> { (self.summary)(device) }
}

/// covers: App::compositor
pub fn app_compositor<S: 'static>(
    app: App<S>,
    features: impl Fn() -> wgpu::Features + 'static,
    render: impl FnMut(&mut Frame<'_, S>) + 'static,
    animating: impl Fn() -> bool + 'static,
    summary: impl FnMut(&wgpu::Device) -> Option<String> + 'static,
) -> App<S> {
    app.compositor(Closures { features, render, animating, summary, state: std::marker::PhantomData })
}

/// covers: rune::env::var_os
pub fn env_var_os(name: &str) -> Option<String> { rune::env::var_os(name).map(|v| v.to_string_lossy().into_owned()) }

/// The wall clock as the time since the Unix epoch, which C# adds to `DateTime.UnixEpoch`.
/// covers: rune::time::system_now
pub fn system_now() -> std::time::Duration {
    rune::time::system_now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default()
}

/// covers: WindowSpec::compositor
pub fn window_compositor<S: 'static>(
    spec: rune::WindowSpec<S>,
    features: impl Fn() -> wgpu::Features + 'static,
    render: impl FnMut(&mut Frame<'_, S>) + 'static,
    animating: impl Fn() -> bool + 'static,
    summary: impl FnMut(&wgpu::Device) -> Option<String> + 'static,
) -> rune::WindowSpec<S> {
    spec.compositor(Closures { features, render, animating, summary, state: std::marker::PhantomData })
}

/// covers: ConfiguredOverlay::compositor
pub fn overlay_compositor<S: 'static>(
    overlay: ConfiguredOverlay<S>,
    features: impl Fn() -> wgpu::Features + 'static,
    render: impl FnMut(&mut Frame<'_, S>) + 'static,
    animating: impl Fn() -> bool + 'static,
    summary: impl FnMut(&wgpu::Device) -> Option<String> + 'static,
) -> ConfiguredOverlay<S> {
    overlay.compositor(Closures { features, render, animating, summary, state: std::marker::PhantomData })
}

/// covers: rune::run_with
pub fn run_with<S: 'static>(
    spec: AppSpec<S>,
    features: impl Fn() -> wgpu::Features + 'static,
    render: impl FnMut(&mut Frame<'_, S>) + 'static,
    animating: impl Fn() -> bool + 'static,
    summary: impl FnMut(&wgpu::Device) -> Option<String> + 'static,
) {
    rune::run_with(spec, Closures { features, render, animating, summary, state: std::marker::PhantomData });
}

/// `Write` for a delegate, which gets each buffer as it fills.
struct Sink<F>(F);

impl<F: FnMut(&[u8])> Write for Sink<F> {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> { (self.0)(buf); Ok(buf.len()) }
    fn flush(&mut self) -> io::Result<()> { Ok(()) }
}

/// covers: MotionTrace::new
pub fn motion_trace_new(out: impl FnMut(&[u8]) + Send + 'static, header: &rune_motion::TraceHeader) -> io::Result<rune_motion::MotionTrace> {
    rune_motion::MotionTrace::new(Sink(out), header)
}

/// covers: NativeSurface::set_observer
pub fn surface_set_observer(surface: &NativeSurface, observer: impl Fn(rune::native_surface::FrameTiming) + Send + Sync + 'static) {
    surface.set_observer(Arc::new(observer));
}

// Generic values as managed objects

/// covers: Cx::layout_with
pub fn cx_layout_with<S: 'static>(cx: &Cx<S>, key: &str, f: impl Fn([f32; 4]) -> ManagedFn + 'static) -> Option<ManagedFn> { cx.layout_with(key, f) }
/// covers: Cx::window_layout_with
pub fn cx_window_layout_with<S: 'static>(cx: &Cx<S>, key: &str, f: impl Fn([f32; 4]) -> ManagedFn + 'static) -> Option<ManagedFn> { cx.window_layout_with(key, f) }
/// covers: rune::memo::layout_with
pub fn memo_layout_with(key: &str, f: impl Fn([f32; 4]) -> ManagedFn + 'static) -> Option<ManagedFn> { rune::memo::layout_with(key, f) }
/// covers: rune::memo::window_layout_with
pub fn memo_window_layout_with(key: &str, f: impl Fn([f32; 4]) -> ManagedFn + 'static) -> Option<ManagedFn> { rune::memo::window_layout_with(key, f) }
/// covers: rune::memo::snapshot
pub fn memo_snapshot(key: &str, value: &ManagedFn) -> ManagedFn { (*rune::memo::snapshot(key, value)).clone() }
/// covers: rune::theme::with_blend
pub fn with_blend(srgb: bool, f: impl FnOnce() -> ManagedFn) -> ManagedFn { rune::theme::with_blend(srgb, f) }
/// covers: rune::memo::as_next_build
pub fn as_next_build(f: impl FnOnce() -> ManagedFn) -> ManagedFn { rune::memo::as_next_build(f) }
/// The value, counted as read by the region or `tracked_reads` building around the call (`Deref`).
pub fn tracked_get(t: &rune::Tracked<ManagedFn>) -> ManagedFn { (**t).clone() }
/// Replaces the value, so a region that read it builds again (`DerefMut`).
pub fn tracked_set(t: &mut rune::Tracked<ManagedFn>, value: ManagedFn) { **t = value; }
/// The Tracked values `f` read; C# keeps what `f` makes in a variable it captures. covers: rune::tracked_reads
pub fn tracked_reads(f: impl FnOnce()) -> rune::Reads { rune::tracked_reads(f).1 }
/// Each entry in turn, oldest first, to change in place. covers: ToastStack::entries_mut
pub fn toast_stack_entries_mut(stack: &mut rune_kit::ToastStack<ManagedFn>, mut each: impl FnMut(&mut rune_kit::ToastEntry<ManagedFn>)) {
    for entry in stack.entries_mut() {
        each(entry);
    }
}
/// The sending end of a channel that `serve` drains. A clone sends to the same server, which stops
/// once every sender is dropped.
#[derive(Clone)]
pub struct ServeSender(std::sync::mpsc::Sender<ManagedFn>);

impl ServeSender {
    /// Queues `message` for the server. False once the server has stopped.
    pub fn send(&self, message: ManagedFn) -> bool {
        self.0.send(message).is_ok()
    }
}

/// Serves a new channel off the UI thread: `each` takes every message in order until it returns
/// false or every sender is dropped. covers: rune::task::serve
pub fn serve(name: String, mut each: impl FnMut(ManagedFn) -> bool + Send + 'static) -> io::Result<ServeSender> {
    let (tx, rx) = std::sync::mpsc::channel();
    rune::task::serve(name, rx, move |message| if each(message) { std::ops::ControlFlow::Continue(()) } else { std::ops::ControlFlow::Break(()) })?;
    Ok(ServeSender(tx))
}

/// A serial service: `each` takes every message sent to the queue, in order, one at a time, on a task
/// worker it borrows only while messages wait, until it returns false. covers: rune::task::queue
pub fn task_queue(name: String, mut each: impl FnMut(ManagedFn) -> bool + Send + 'static) -> rune::task::Queue<ManagedFn> {
    rune::task::queue(name, move |message| if each(message) { std::ops::ControlFlow::Continue(()) } else { std::ops::ControlFlow::Break(()) })
}

/// covers: Latest::request
pub fn latest_request(latest: &rune::Latest<ManagedFn>, input: ManagedFn, work: impl FnOnce(ManagedFn) -> ManagedFn + Send + 'static) { latest.request(input, work) }
/// `keep` says whether a result counts; one it refuses is `None` to Rune. covers: Latest::request_opt
pub fn latest_request_opt(latest: &rune::Latest<ManagedFn>, input: ManagedFn, work: impl FnOnce(ManagedFn) -> ManagedFn + Send + 'static, keep: impl FnOnce(&ManagedFn) -> bool + Send + 'static) {
    latest.request_opt(input, move |k| Some(work(k)).filter(|r| keep(r)))
}
/// covers: Latest::request_if_changed
pub fn latest_request_if_changed(latest: &rune::Latest<ManagedFn>, key: &ManagedFn, work: impl FnOnce(ManagedFn) -> ManagedFn + Send + 'static) { latest.request_if_changed(key, work) }
/// covers: Runtime::playground_state
pub fn runtime_playground_state<S: 'static>(rt: &Runtime<S>) -> Option<ManagedFn> { rt.playground_state::<ManagedFn>().cloned() }

/// covers: Prefs::get
pub fn prefs_get(prefs: &Prefs, key: &str) -> Option<serde_json::Value> { prefs.get(key) }
/// covers: Prefs::set
pub fn prefs_set(prefs: &mut Prefs, key: &str, value: &serde_json::Value) -> Result<(), PrefsError> { prefs.set(key, value) }
pub fn json_parse(text: &str) -> Result<serde_json::Value, String> { serde_json::from_str(text).map_err(|e| e.to_string()) }
pub fn json_text(value: &serde_json::Value) -> String { value.to_string() }

// Sequences and tuples

/// covers: Choreography::stagger
pub fn choreography_stagger(c: &mut rune_motion::Choreography, cue: rune_motion::Cue, stagger: rune_motion::Stagger, count: usize, mut each: impl FnMut(&mut rune_motion::Ops, usize)) -> Vec<rune_motion::StepId> {
    c.stagger(cue, stagger, 0..count, |ops, i| each(ops, i))
}
/// covers: PopupMenu::picker
pub fn popup_picker(ids: Vec<String>, titles: Vec<String>, selected: &str) -> rune::PopupMenu {
    rune::PopupMenu::picker(ids.into_iter().zip(titles), selected)
}
/// covers: FileTree::build
pub fn file_tree_build(tree: FileTree, paths: Vec<String>, data: impl Fn(String) -> ManagedFn) -> TreeModel<FileNode<ManagedFn>> {
    tree.build(paths.into_iter().map(|p| { let d = data(p.clone()); (p, d) }))
}
/// covers: TreeItem::file
pub fn tree_item_file(node: &FileNode<ManagedFn>, id: &str, model: &TreeModel<FileNode<ManagedFn>>) -> Option<TreeItem> {
    let row = model.visible_row(id)?;
    model.row(row).map(|(_, state)| TreeItem::file(node, &state))
}
/// covers: TreeModel::subtree
pub fn tree_subtree(model: &TreeModel<ManagedFn>, id: &str, mut each: impl FnMut(ManagedFn)) { for d in model.subtree(id) { each(d.clone()); } }

/// A visible row of a `TreeModel`, copied out of its borrow.
pub struct TreeRowInfo {
    pub id: String,
    pub depth: u32,
    pub expanded: Option<bool>,
    pub selected: bool,
    pub cursor: bool,
    pub matched: bool,
}

impl From<TreeRow<'_>> for TreeRowInfo {
    fn from(r: TreeRow<'_>) -> Self {
        Self { id: r.id.to_owned(), depth: r.depth, expanded: r.expanded, selected: r.selected, cursor: r.cursor, matched: r.matched }
    }
}

/// covers: TreeModel::row
pub fn tree_row(model: &TreeModel<ManagedFn>, row: usize) -> Option<TreeRowInfo> { model.row(row).map(|(_, r)| r.into()) }
pub fn tree_row_data(model: &TreeModel<ManagedFn>, row: usize) -> Option<ManagedFn> { model.row(row).map(|(d, _)| d.clone()) }

/// covers: RowStatus::Other.0 (read)
pub fn row_status_take_other(status: RowStatus) -> Option<El> {
    match status { RowStatus::Other(el) => Some(el), _ => None }
}

/// covers: TraceChannels::write_json
pub fn trace_channels_json(channels: &mut rune_motion::TraceChannels, mut line: String) -> String {
    channels.write_json(&mut line);
    line
}

// Results and futures

/// Makes the one data variant of `TextureFormat`.
pub fn texture_format_astc(block: wgpu::AstcBlock, channel: wgpu::AstcChannel) -> wgpu::TextureFormat { wgpu::TextureFormat::Astc { block, channel } }

/// Waits for a future on this thread. File dialogs answer from another one.
fn block_on<F: Future>(future: F) -> F::Output {
    struct Unpark(std::thread::Thread);
    impl Wake for Unpark {
        fn wake(self: Arc<Self>) { self.0.unpark(); }
    }
    let waker = TaskWaker::from(Arc::new(Unpark(std::thread::current())));
    let mut cx = Context::from_waker(&waker);
    let mut future = std::pin::pin!(future);
    loop {
        match future.as_mut().poll(&mut cx) {
            Poll::Ready(value) => return value,
            Poll::Pending => std::thread::park(),
        }
    }
}

/// The path, or null for a cancel, then the error text.
fn outcome(result: Result<Option<PathBuf>, FileDialogError>) -> (Option<String>, Option<String>) {
    match result {
        Ok(path) => (path.map(|p| p.to_string_lossy().into_owned()), None),
        Err(e) => (None, Some(e.to_string())),
    }
}

fn outcome_many(result: Result<Option<Vec<PathBuf>>, FileDialogError>) -> (Option<Vec<String>>, Option<String>) {
    match result {
        Ok(paths) => (paths.map(|ps| ps.into_iter().map(|p| p.to_string_lossy().into_owned()).collect()), None),
        Err(e) => (None, Some(e.to_string())),
    }
}

/// A C# delegate, which any thread may call and drop.
struct Anywhere<T>(T);
// SAFETY: `T` only ever holds a `ManagedFn`, whose target is a GC handle.
unsafe impl<T> Send for Anywhere<T> {}
impl<T> Anywhere<T> {
    fn take(self) -> T { self.0 }
}

/// Waits for a dialog on its own thread, as blocking the window's thread would deadlock it.
fn in_background<F: Future<Output = Result<Option<PathBuf>, FileDialogError>>>(
    show: impl FnOnce() -> F + Send + 'static,
    done: impl FnOnce(Option<String>, Option<String>) + 'static,
) {
    let done = Anywhere(done);
    std::thread::spawn(move || {
        let (path, error) = outcome(block_on(show()));
        done.take()(path, error);
    });
}

/// The answer a scripted dialog gives; a null path is a cancel.
pub fn scripted_dialog(kind: rune::FileDialogKind, path: Option<String>) -> rune::ScriptedFileDialog {
    rune::ScriptedFileDialog { kind, path: path.map(PathBuf::from) }
}

/// Returns at once and calls `done` on another thread. covers: FileDialogs::open
pub fn dialogs_open(dialogs: &FileDialogs, options: FileDialogOptions, done: impl FnOnce(Option<String>, Option<String>) + 'static) {
    let dialogs = dialogs.clone();
    in_background(move || async move { dialogs.open(options).await }, done);
}
/// Returns at once and calls `done` on another thread. covers: FileDialogs::save
pub fn dialogs_save(dialogs: &FileDialogs, options: FileDialogOptions, done: impl FnOnce(Option<String>, Option<String>) + 'static) {
    let dialogs = dialogs.clone();
    in_background(move || async move { dialogs.save(options).await }, done);
}
/// Returns at once and calls `done` on another thread: the chosen paths, or null for a cancel, then the error text. covers: FileDialogs::open_files
pub fn dialogs_open_files(dialogs: &FileDialogs, options: FileDialogOptions, done: impl FnOnce(Option<Vec<String>>, Option<String>) + 'static) {
    let dialogs = dialogs.clone();
    let done = Anywhere(done);
    std::thread::spawn(move || {
        let (paths, error) = outcome_many(block_on(async move { dialogs.open_files(options).await }));
        done.take()(paths, error);
    });
}
/// Returns at once and calls `done` on another thread. covers: FileDialogs::open_folder
pub fn dialogs_open_folder(dialogs: &FileDialogs, options: FileDialogOptions, done: impl FnOnce(Option<String>, Option<String>) + 'static) {
    let dialogs = dialogs.clone();
    in_background(move || async move { dialogs.open_folder(options).await }, done);
}

fn answer<S>(on_answer: impl FnOnce(&mut S, Option<String>, Option<String>, &mut Tx)) -> impl FnOnce(&mut S, Result<Option<PathBuf>, FileDialogError>, &mut Tx) {
    move |s, result, tx| {
        let (path, error) = outcome(result);
        on_answer(s, path, error, tx);
    }
}

/// `on_answer` gets the path, or null for a cancel, then the error text. covers: Tx::open_file
pub fn tx_open_file<S: 'static>(tx: &mut Tx, options: FileDialogOptions, on_answer: impl FnOnce(&mut S, Option<String>, Option<String>, &mut Tx) + 'static) { tx.open_file(options, answer(on_answer)) }
/// `on_answer` gets the paths, or null for a cancel, then the error text. covers: Tx::open_files
pub fn tx_open_files<S: 'static>(tx: &mut Tx, options: FileDialogOptions, on_answer: impl FnOnce(&mut S, Option<Vec<String>>, Option<String>, &mut Tx) + 'static) {
    tx.open_files(options, move |s, result, tx| {
        let (paths, error) = outcome_many(result);
        on_answer(s, paths, error, tx);
    })
}
/// covers: Tx::save_file
pub fn tx_save_file<S: 'static>(tx: &mut Tx, options: FileDialogOptions, on_answer: impl FnOnce(&mut S, Option<String>, Option<String>, &mut Tx) + 'static) { tx.save_file(options, answer(on_answer)) }
/// covers: Tx::open_folder
pub fn tx_open_folder<S: 'static>(tx: &mut Tx, options: FileDialogOptions, on_answer: impl FnOnce(&mut S, Option<String>, Option<String>, &mut Tx) + 'static) { tx.open_folder(options, answer(on_answer)) }

/// `change` returns null to accept the chord, or the error refusing it. covers: ShortcutRecorder::on_change
pub fn shortcut_on_change<S: 'static>(recorder: ShortcutRecorder<S>, change: impl Fn(&mut S, Option<&rune::Shortcut>, &mut Tx) -> Option<ShortcutError> + 'static) -> ShortcutRecorder<S> {
    recorder.on_change(move |s, shortcut, tx| change(s, shortcut, tx).map_or(Ok(()), Err))
}

/// covers: WindowHandle::with_raw_handle
pub fn window_with_raw_handle(window: &rune::WindowHandle, f: impl for<'a> FnOnce(&rune::raw_window_handle::WindowHandle<'a>)) -> Result<(), String> {
    window.with_raw_handle(|handle| f(&handle))
}

// Rendering hooks

/// covers: Renderer::draw_with
pub fn renderer_draw_with(r: &Renderer, encoder: &mut wgpu::CommandEncoder, view: &wgpu::TextureView, clear: [f32; 4], mut hook: impl FnMut(&mut wgpu::RenderPass<'_>, &EffectSite<'_>)) {
    r.draw_with(encoder, view, clear, &mut |pass, site| hook(pass, site));
}
/// covers: Renderer::draw_range_with
pub fn renderer_draw_range_with(r: &Renderer, pass: &mut wgpu::RenderPass<'_>, format: wgpu::TextureFormat, start: u32, end: u32, mut hook: impl FnMut(&mut wgpu::RenderPass<'_>, &EffectSite<'_>)) {
    r.draw_range_with(pass, format, start..end, &mut |pass, site| hook(pass, site));
}
/// covers: Renderer::encode_layers_with
pub fn renderer_encode_layers_with(r: &Renderer, encoder: &mut wgpu::CommandEncoder, mut hook: impl FnMut(&mut wgpu::RenderPass<'_>, &EffectSite<'_>)) {
    r.encode_layers_with(encoder, &mut |pass, site| hook(pass, site));
}

macro_rules! phases {
    ($backdrop:ident, $draw:ident) => {
        &mut |phase, site| match phase {
            EffectPhase::Backdrop(encoder) => $backdrop(encoder, site),
            EffectPhase::Draw(pass) => $draw(pass, site),
        }
    };
}

/// covers: Renderer::draw_with_backdrop, EffectPhase::Backdrop, EffectPhase::Draw
pub fn renderer_draw_with_backdrop(r: &Renderer, encoder: &mut wgpu::CommandEncoder, view: &wgpu::TextureView, clear: [f32; 4], mut backdrop: impl FnMut(&mut wgpu::CommandEncoder, &EffectSite<'_>), mut draw: impl FnMut(&mut wgpu::RenderPass<'_>, &EffectSite<'_>)) {
    r.draw_with_backdrop(encoder, view, clear, phases!(backdrop, draw));
}
/// covers: Renderer::encode_layers_with_backdrop
pub fn renderer_encode_layers_with_backdrop(r: &Renderer, encoder: &mut wgpu::CommandEncoder, mut backdrop: impl FnMut(&mut wgpu::CommandEncoder, &EffectSite<'_>), mut draw: impl FnMut(&mut wgpu::RenderPass<'_>, &EffectSite<'_>)) {
    r.encode_layers_with_backdrop(encoder, phases!(backdrop, draw));
}
/// covers: Renderer::draw_target_range_with
#[allow(clippy::too_many_arguments)]
pub fn renderer_draw_target_range_with(r: &Renderer, encoder: &mut wgpu::CommandEncoder, view: &wgpu::TextureView, format: wgpu::TextureFormat, load: wgpu::LoadOp<wgpu::Color>, start: u32, end: u32, mut backdrop: impl FnMut(&mut wgpu::CommandEncoder, &EffectSite<'_>), mut draw: impl FnMut(&mut wgpu::RenderPass<'_>, &EffectSite<'_>)) {
    r.draw_target_range_with(encoder, view, format, load, start..end, phases!(backdrop, draw));
}
/// `ImageAtlas` is in a private module, so C# reaches it only through its `Runtime`.
/// covers: Renderer::upload_images, Runtime.images
pub fn renderer_upload_images<S: 'static>(r: &mut Renderer, device: &wgpu::Device, queue: &wgpu::Queue, rt: &mut Runtime<S>) {
    r.upload_images(device, queue, &mut rt.images);
}

/// A frame's CPU scene, from the `Runtime` that laid it out: `ImageAtlas` is in a private module,
/// so C# can't name the atlases `Scene::new` borrows. Paint it before the runtime changes.
/// covers: Scene::new
pub fn cpu_boot_scene<S: 'static>(rt: &Runtime<S>, size: (u32, u32), clear: [f32; 4]) -> Scene<'_> {
    Scene::new(&rt.instances, &rt.text.atlas, &rt.images, size, clear)
}

/// The bytes a CPU paint writes into, kept on the Rust side: C# has no `&mut [u8]` to lend a `Target`.
pub struct CpuPixels(Vec<u8>);

impl CpuPixels {
    /// `len` zeroed bytes.
    pub fn new(len: usize) -> Self { CpuPixels(vec![0; len]) }
    /// A copy of the bytes.
    pub fn bytes(&self) -> Vec<u8> { self.0.clone() }
    pub fn len(&self) -> usize { self.0.len() }
    pub fn is_empty(&self) -> bool { self.0.is_empty() }
}

/// A target of tightly packed rows over `pixels`. covers: Target::new
pub fn cpu_target(pixels: &mut CpuPixels, width: u32, height: u32) -> Target<'_> { Target::new(&mut pixels.0, width, height) }
/// A target over `pixels` with rows `stride` bytes apart. covers: Target::with_stride
pub fn cpu_target_with_stride(pixels: &mut CpuPixels, width: u32, height: u32, stride: usize) -> Target<'_> { Target::with_stride(&mut pixels.0, width, height, stride) }

// Motion gate

fn jobs(names: Vec<String>, job: impl Fn(String) -> Recording + Send + Sync + 'static) -> Vec<(String, Job)> {
    let job = Arc::new(job);
    names.into_iter().map(|name| {
        let job = job.clone();
        let cell = name.clone();
        (name, Box::new(move || job(cell)) as Job)
    }).collect()
}

/// `job` records the named cell, on a worker thread. covers: rune::motion_gate::run
pub fn gate_run(names: Vec<String>, job: impl Fn(String) -> Recording + Send + Sync + 'static, filter: &str) -> Vec<Cell> { rune::motion_gate::run(jobs(names, job), filter) }
/// covers: rune::motion_gate::gate
pub fn gate(names: Vec<String>, job: impl Fn(String) -> Recording + Send + Sync + 'static, allow: &[Allow]) { rune::motion_gate::gate(jobs(names, job), allow) }
/// covers: rune::motion_gate::gate_rows
pub fn gate_rows(names: Vec<String>, job: impl Fn(String) -> Recording + Send + Sync + 'static, allow: &[AllowRow]) { rune::motion_gate::gate_rows(jobs(names, job), allow) }

/// The recording a finished run made: a gate job returns it, and `Run::rec` only lends it.
pub fn gate_recording<S: 'static>(run: rune::motion_gate::Run<S>) -> Recording { run.rec }

// What C# needs to make the external types Rune's API takes

pub fn instance_new() -> wgpu::Instance { wgpu::Instance::default() }
pub fn device_create_encoder(device: &wgpu::Device, label: &str) -> wgpu::CommandEncoder {
    device.create_command_encoder(&wgpu::CommandEncoderDescriptor { label: Some(label) })
}
pub fn queue_submit(queue: &wgpu::Queue, encoder: wgpu::CommandEncoder) -> wgpu::SubmissionIndex { queue.submit([encoder.finish()]) }
pub fn features_empty() -> wgpu::Features { wgpu::Features::empty() }
pub fn load_clear(color: [f32; 4]) -> wgpu::LoadOp<wgpu::Color> {
    wgpu::LoadOp::Clear(wgpu::Color { r: color[0] as f64, g: color[1] as f64, b: color[2] as f64, a: color[3] as f64 })
}
pub fn load_keep() -> wgpu::LoadOp<wgpu::Color> { wgpu::LoadOp::Load }
pub fn action_data_value(value: String) -> rune::accesskit::ActionData { rune::accesskit::ActionData::Value(value.into()) }
pub fn action_data_number(value: f64) -> rune::accesskit::ActionData { rune::accesskit::ActionData::NumericValue(value) }
pub fn language_parse(tag: &str) -> Option<parley::fontique::Language> { parley::fontique::Language::parse(tag).ok() }
pub fn io_error(message: String) -> io::Error { io::Error::other(message) }
pub fn rgb_image(width: u32, height: u32, pixels: Vec<u8>) -> Option<image::RgbImage> { image::RgbImage::from_raw(width, height, pixels) }
pub fn rgb_image_bytes(image: &image::RgbImage) -> Vec<u8> { image.as_raw().clone() }

pub fn trace_header(launch: String, width: f32, height: f32, hz: f32, scale: f32) -> rune_motion::TraceHeader {
    rune_motion::TraceHeader { launch, width, height, hz, scale }
}
/// The data of a file; folders have none.
pub fn file_node_data(node: &FileNode<ManagedFn>) -> Option<ManagedFn> { node.data.clone() }

// macOS embedding. `layer`, `raw` and the like are native pointers, which C# holds as numbers.

/// `layer` must be a live `CAMetalLayer *` that outlives the host, and this must run on the UI thread.
/// covers: LayerHost::from_core_animation_layer
#[cfg(target_os = "macos")]
pub fn layer_host_new<S: 'static>(layer: usize, spec: &AppSpec<S>, state: S, viewport: (f32, f32), scale: f32, dark: bool, wake: Waker) -> Result<rune::embed::LayerHost<S>, String> {
    // SAFETY: the caller keeps the promise above, which is the one Rune asks for.
    unsafe { rune::embed::LayerHost::from_core_animation_layer(layer as *mut std::ffi::c_void, spec, state, viewport, scale, dark, wake) }
}

/// covers: LayerHost::with_compositor
#[cfg(target_os = "macos")]
pub fn layer_host_compositor<S: 'static>(
    host: rune::embed::LayerHost<S>,
    features: impl Fn() -> wgpu::Features + 'static,
    render: impl FnMut(&mut Frame<'_, S>) + 'static,
    animating: impl Fn() -> bool + 'static,
    summary: impl FnMut(&wgpu::Device) -> Option<String> + 'static,
) -> rune::embed::LayerHost<S> {
    host.with_compositor(Closures { features, render, animating, summary, state: std::marker::PhantomData })
}

/// `raw` must be a live `CVPixelBufferRef` whose pixels stay unchanged until every use is released.
/// covers: PixelBuffer::from_raw
#[cfg(target_os = "macos")]
pub fn pixel_buffer_from_raw(raw: usize) -> Result<rune::native_surface::macos::PixelBuffer, String> {
    // SAFETY: the caller keeps the promise above, which is the one Rune asks for.
    unsafe { rune::native_surface::macos::PixelBuffer::from_raw(raw as *mut std::ffi::c_void) }
}

/// `raw` must be a live `IOSurfaceRef` of complete pixels that stays unchanged through all GPU use.
/// covers: PixelBuffer::from_iosurface
#[cfg(target_os = "macos")]
pub fn pixel_buffer_from_iosurface(raw: usize, color: rune::native_surface::ColorInfo) -> Result<rune::native_surface::macos::PixelBuffer, String> {
    // SAFETY: the caller keeps the promise above, which is the one Rune asks for.
    unsafe { rune::native_surface::macos::PixelBuffer::from_iosurface(raw as *mut std::ffi::c_void, color) }
}

// Linux dmabufs. A file descriptor is a number to C#.

/// A plane over `fd`, which the plane owns from here: it closes the descriptor when it drops, so
/// the caller must not use or close it again. covers: DmabufPlane.fd
#[cfg(target_os = "linux")]
pub fn dmabuf_plane_from_fd(fd: i32, offset: u32, stride: u32, modifier: u64) -> rune::native_surface::linux::DmabufPlane {
    use std::os::fd::FromRawFd;
    // SAFETY: the caller hands over an open descriptor it no longer uses, which is what `OwnedFd` asks for.
    rune::native_surface::linux::DmabufPlane { fd: unsafe { std::os::fd::OwnedFd::from_raw_fd(fd) }, offset, stride, modifier }
}

/// `f` gets null once Rune's GPU has read the frame, or why it couldn't. It runs on a thread of Rune's.
/// covers: Dmabuf::on_read
#[cfg(target_os = "linux")]
pub fn dmabuf_on_read(frame: rune::native_surface::linux::Dmabuf, f: impl FnOnce(Option<String>) + Send + 'static) -> rune::native_surface::linux::Dmabuf {
    frame.on_read(move |read| f(read.err()))
}

/// The environment as strings: a variable that isn't valid UTF-8 reads as unset. covers: AppDirs::from_env
pub fn app_dirs_from_env(app_id: &str, var: impl Fn(&str) -> Option<String>) -> Option<rune::dirs::AppDirs> {
    rune::dirs::AppDirs::from_env(app_id, |name| var(name).map(Into::into))
}

/// The environment as strings, as `app_dirs_from_env`. covers: rune::dirs::home_from
pub fn home_from(var: impl Fn(&str) -> Option<String>) -> Option<PathBuf> {
    rune::dirs::home_from(|name| var(name).map(Into::into))
}

/// Rune sorts `ids` in place, which no caller sees, so they're taken by value. covers: AnimationStore::delay
pub fn animation_store_delay(store: &mut rune_motion::AnimationStore, mut ids: Vec<rune_motion::TrackId>, secs: f64) {
    store.delay(&mut ids, secs);
}

/// The kind a lexer token is coloured as, with the token by its capture name (`keyword.control`),
/// as `rune_highlights` names it. `None` for a name it doesn't know. covers: rune_kit::syntax::kind
pub fn syntax_kind(token: &str) -> Option<rune_kit::syntax::Syntax> {
    rune_highlights::Token::from_name(token).and_then(rune_kit::syntax::kind)
}

/// Rune keeps the icon a swap returns for as long as the app runs, so each one the C# swap hands
/// back is copied once into a table that lives that long, and freeing the C# object later is safe.
/// covers: rune::text::set_icon_swap
pub fn set_icon_swap(swap: fn(&rune::text::Icon) -> Option<&rune::text::Icon>) {
    *ICON_SWAP.write().unwrap_or_else(std::sync::PoisonError::into_inner) = Some(swap);
    rune::text::set_icon_swap(Some(swapped_icon));
}

static ICON_SWAP: std::sync::RwLock<Option<fn(&rune::text::Icon) -> Option<&rune::text::Icon>>> = std::sync::RwLock::new(None);

fn swapped_icon(icon: &rune::text::Icon) -> Option<&'static rune::text::Icon> {
    use rune::text::Icon;
    type Kept = std::collections::HashMap<(&'static str, u32, &'static str), &'static Icon>;
    static KEPT: std::sync::Mutex<Option<Kept>> = std::sync::Mutex::new(None);
    let swap = (*ICON_SWAP.read().unwrap_or_else(std::sync::PoisonError::into_inner))?;
    let to = swap(icon)?;
    // An icon swapped for itself stays the same icon, which is how Rune tells it wasn't swapped.
    if std::ptr::eq(to, icon) {
        return None;
    }
    let mut kept = KEPT.lock().unwrap_or_else(std::sync::PoisonError::into_inner);
    Some(*kept.get_or_insert_with(Default::default).entry((to.name, to.view.to_bits(), to.body)).or_insert_with(|| Box::leak(Box::new(Icon { name: to.name, view: to.view, body: to.body }))))
}
pub fn clear_icon_swap() {
    rune::text::set_icon_swap(None);
    *ICON_SWAP.write().unwrap_or_else(std::sync::PoisonError::into_inner) = None;
}
/// covers: rune::text::set_icon_swap_size
pub fn set_icon_swap_size(size: fn(f32) -> f32) { rune::text::set_icon_swap_size(Some(size)); }
pub fn clear_icon_swap_size() { rune::text::set_icon_swap_size(None); }

/// The arguments a relaunch keeps, with the ones that drive input or act for the person dropped.
/// covers: rune::without_relaunch_drops
pub fn without_relaunch_drops(args: Vec<String>) -> Vec<String> {
    rune::without_relaunch_drops(args.into_iter().map(std::ffi::OsString::from).collect())
        .into_iter().map(|a| a.to_string_lossy().into_owned()).collect()
}

/// Sets the inclusive minimum and maximum values. covers: Slider::range
pub fn slider_range(slider: rune_kit::Slider, min: f32, max: f32) -> rune_kit::Slider { slider.range(min..=max) }

/// The image's width, height and RGBA bytes, or null while it holds none. covers: Pixels::with_rgba
pub fn pixels_rgba(pixels: &rune::Pixels) -> Option<(u32, u32, Vec<u8>)> { pixels.with_rgba(|w, h, rgba| (w, h, rgba.to_vec())) }

/// covers: WindowHandle::with_raw_display_handle
pub fn window_with_raw_display_handle(window: &rune::WindowHandle, f: impl for<'a> FnOnce(&rune::raw_window_handle::DisplayHandle<'a>)) -> Result<(), String> {
    window.with_raw_display_handle(|handle| f(&handle))
}

/// `on_result` gets null when the link opened, or why it didn't. covers: Tx::open_url_then
pub fn tx_open_url_then<S: 'static>(tx: &mut Tx, url: &str, on_result: impl FnOnce(&mut S, Option<rune::OpenUrlError>, &mut Tx) + 'static) -> Result<(), rune::OpenUrlError> {
    tx.open_url_then(url, move |s: &mut S, result, tx| on_result(s, result.err(), tx))
}
/// `on_result` gets null when the link opened, or why it didn't. covers: Tx::open_url_same_tab_then
pub fn tx_open_url_same_tab_then<S: 'static>(tx: &mut Tx, url: &str, on_result: impl FnOnce(&mut S, Option<rune::OpenUrlError>, &mut Tx) + 'static) -> Result<(), rune::OpenUrlError> {
    tx.open_url_same_tab_then(url, move |s: &mut S, result, tx| on_result(s, result.err(), tx))
}

/// The schemes the app opens as links. covers: rune::url_schemes
pub fn url_schemes(schemes: Vec<String>) { rune::url_schemes(schemes) }

/// Sets the order the columns show in. covers: TableState::set_column_order
pub fn table_set_column_order(state: &mut rune_kit::TableState, order: Vec<String>) { state.set_column_order(order) }

/// A text tag on the element, which a pick finds again. covers: El::data
pub fn el_data(el: El, tag: String) -> El { el.data(Tag(tag)) }
/// The text tag set with `el_data`, or null. covers: Picked::data
pub fn picked_data(picked: &rune::Picked) -> Option<String> { picked.data::<Tag>().map(|t| t.0.clone()) }
struct Tag(String);

struct ItemClosures<V, K, B, P> { version: V, keys: K, build: B, priority: P, budget: Option<usize> }
impl<V: Fn() -> u64 + 'static, K: Fn() -> Vec<String> + 'static, B: Fn(&str) -> El + 'static, P: Fn(&str) -> f32 + 'static> rune::VirtualItems for ItemClosures<V, K, B, P> {
    fn version(&self) -> u64 { (self.version)() }
    fn keys(&self) -> Vec<String> { (self.keys)() }
    fn build(&self, key: &str) -> El { (self.build)(key) }
    fn mount_budget(&self) -> Option<usize> { self.budget }
    fn mount_priority(&self, key: &str) -> f32 { (self.priority)(key) }
}
/// Fills the element with keyed items the app windows. `budget` caps the items mounted per frame (0 for no cap).
/// covers: El::virtual_items
pub fn el_virtual_items(
    el: El,
    version: impl Fn() -> u64 + 'static,
    keys: impl Fn() -> Vec<String> + 'static,
    build: impl Fn(&str) -> El + 'static,
    priority: impl Fn(&str) -> f32 + 'static,
    budget: u32,
) -> El {
    el.virtual_items(std::rc::Rc::new(ItemClosures { version, keys, build, priority, budget: (budget > 0).then_some(budget as usize) }))
}

// Rune 07bf8c5d

/// Adds an overlay from any thread: `spec` and `state` run on the app's thread when it builds it.
/// covers: AppHandle::overlay
/// In C# the app's and the overlay's state are both managed objects, so one parameter stands for both.
pub fn app_handle_overlay<S: 'static>(app: &rune::AppHandle<S>, spec: impl FnOnce() -> ConfiguredOverlay<S> + 'static, state: impl FnOnce() -> S + 'static) -> rune::OverlayHandle<S> {
    let build = Anywhere((spec, state));
    app.overlay(move || {
        let (spec, state) = build.take();
        (spec(), state())
    })
}

/// The drag's other formats as MIME types and bytes, in the order they were added. covers: DragItem::custom_data
pub fn drag_item_custom_data(item: &rune::DragItem) -> Vec<(String, Vec<u8>)> {
    item.custom_data().map(|(mime, bytes)| (mime.to_owned(), bytes.to_vec())).collect()
}

fn handle_outcome<T>(result: Result<Option<T>, FileDialogError>) -> (Option<T>, Option<String>) {
    match result {
        Ok(v) => (v, None),
        Err(e) => (None, Some(e.to_string())),
    }
}

/// Returns at once and calls `done` on another thread: the file, or null for a cancel, then the error text. covers: FileDialogs::pick_file
pub fn dialogs_pick_file(dialogs: &FileDialogs, options: FileDialogOptions, done: impl FnOnce(Option<rune::FileHandle>, Option<String>) + 'static) {
    let (dialogs, done) = (dialogs.clone(), Anywhere(done));
    std::thread::spawn(move || {
        let (file, error) = handle_outcome(block_on(async move { dialogs.pick_file(options).await }));
        done.take()(file, error);
    });
}
/// Returns at once and calls `done` on another thread: the files, or null for a cancel, then the error text. covers: FileDialogs::pick_files
pub fn dialogs_pick_files(dialogs: &FileDialogs, options: FileDialogOptions, done: impl FnOnce(Option<Vec<rune::FileHandle>>, Option<String>) + 'static) {
    let (dialogs, done) = (dialogs.clone(), Anywhere(done));
    std::thread::spawn(move || {
        let (files, error) = handle_outcome(block_on(async move { dialogs.pick_files(options).await }));
        done.take()(files, error);
    });
}
/// Returns at once and calls `done` on another thread: the file, or null for a cancel, then the error text. covers: FileDialogs::pick_save_file
pub fn dialogs_pick_save_file(dialogs: &FileDialogs, options: FileDialogOptions, done: impl FnOnce(Option<rune::FileHandle>, Option<String>) + 'static) {
    let (dialogs, done) = (dialogs.clone(), Anywhere(done));
    std::thread::spawn(move || {
        let (file, error) = handle_outcome(block_on(async move { dialogs.pick_save_file(options).await }));
        done.take()(file, error);
    });
}

/// `on_answer` gets the file, or null for a cancel, then the error text. covers: Tx::pick_file
pub fn tx_pick_file<S: 'static>(tx: &mut Tx, options: FileDialogOptions, on_answer: impl FnOnce(&mut S, Option<rune::FileHandle>, Option<String>, &mut Tx) + 'static) {
    tx.pick_file(options, move |s, result, tx| {
        let (file, error) = handle_outcome(result);
        on_answer(s, file, error, tx);
    })
}
/// `on_answer` gets the files, or null for a cancel, then the error text. covers: Tx::pick_files
pub fn tx_pick_files<S: 'static>(tx: &mut Tx, options: FileDialogOptions, on_answer: impl FnOnce(&mut S, Option<Vec<rune::FileHandle>>, Option<String>, &mut Tx) + 'static) {
    tx.pick_files(options, move |s, result, tx| {
        let (files, error) = handle_outcome(result);
        on_answer(s, files, error, tx);
    })
}
/// `on_answer` gets the file, or null for a cancel, then the error text. covers: Tx::pick_save_file
pub fn tx_pick_save_file<S: 'static>(tx: &mut Tx, options: FileDialogOptions, on_answer: impl FnOnce(&mut S, Option<rune::FileHandle>, Option<String>, &mut Tx) + 'static) {
    tx.pick_save_file(options, move |s, result, tx| {
        let (file, error) = handle_outcome(result);
        on_answer(s, file, error, tx);
    })
}

/// `on_result` gets null when the image was copied, or why it wasn't. covers: Tx::copy_image_then
pub fn tx_copy_image_then<S: 'static>(tx: &mut Tx, image: &rune::Image, on_result: impl FnOnce(&mut S, Option<rune::CopyImageError>, &mut Tx) + 'static) {
    tx.copy_image_then(image, move |s: &mut S, result, tx| on_result(s, result.err(), tx))
}

/// Returns at once and calls `done` on another thread with the file's bytes, or null and the error text. covers: FileHandle::read
pub fn file_handle_read(file: &rune::FileHandle, done: impl FnOnce(Option<Vec<u8>>, Option<String>) + 'static) {
    let (file, done) = (file.clone(), Anywhere(done));
    std::thread::spawn(move || {
        let (bytes, error) = match block_on(async move { file.read().await }) {
            Ok(b) => (Some(b), None),
            Err(e) => (None, Some(e.to_string())),
        };
        done.take()(bytes, error);
    });
}
/// Returns at once and calls `done` on another thread with null once written, or the error text. covers: FileHandle::write
pub fn file_handle_write(file: &rune::FileHandle, bytes: Vec<u8>, done: impl FnOnce(Option<String>) + 'static) {
    let (file, done) = (file.clone(), Anywhere(done));
    std::thread::spawn(move || {
        let error = block_on(async move { file.write(&bytes).await }).err().map(|e| e.to_string());
        done.take()(error);
    });
}

/// A stream reading a [`rune::FileHandle`] from its start.
pub struct FileReader(Box<dyn io::Read + Send>);
impl FileReader {
    /// Up to `max` more bytes, or none at the end.
    pub fn read(&mut self, max: u32) -> io::Result<Vec<u8>> {
        let mut buf = vec![0; max as usize];
        let n = self.0.read(&mut buf)?;
        buf.truncate(n);
        Ok(buf)
    }
}
/// A stream replacing a [`rune::FileHandle`]'s contents.
pub struct FileWriter(Box<dyn Write + Send>);
impl FileWriter {
    /// Writes all of `bytes`.
    pub fn write(&mut self, bytes: &[u8]) -> io::Result<()> { self.0.write_all(bytes) }
    pub fn flush(&mut self) -> io::Result<()> { self.0.flush() }
}
/// covers: FileHandle::reader
pub fn file_handle_reader(file: &rune::FileHandle) -> io::Result<FileReader> { file.reader().map(FileReader) }
/// covers: FileHandle::writer
pub fn file_handle_writer(file: &rune::FileHandle) -> io::Result<FileWriter> { file.writer().map(FileWriter) }

/// C# delegates, which any thread may call and drop.
struct Shared<T>(T);
// SAFETY: `T` only holds generated closures over C# delegates, whose targets are GC handles.
unsafe impl<T> Send for Shared<T> {}
unsafe impl<T> Sync for Shared<T> {}

struct Provider<R, W> { read: Shared<R>, write: std::sync::Arc<Shared<W>> }
impl<R: Fn() -> Option<Vec<u8>>, W: Fn(Vec<u8>) -> Option<String> + 'static> rune::FileHandleProvider for Provider<R, W> {
    fn reader(&self) -> io::Result<Box<dyn io::Read + Send>> {
        let bytes = (self.read.0)().ok_or_else(|| io::Error::other("the file couldn't be read"))?;
        Ok(Box::new(io::Cursor::new(bytes)))
    }
    fn writer(&self) -> io::Result<Box<dyn Write + Send>> {
        Ok(Box::new(Collected { bytes: Vec::new(), dirty: false, write: std::sync::Arc::clone(&self.write) }))
    }
}

/// Bytes a provider's writer holds until it is flushed or dropped.
struct Collected<W: Fn(Vec<u8>) -> Option<String>> { bytes: Vec<u8>, dirty: bool, write: std::sync::Arc<Shared<W>> }
impl<W: Fn(Vec<u8>) -> Option<String>> Write for Collected<W> {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        self.bytes.extend_from_slice(buf);
        self.dirty = true;
        Ok(buf.len())
    }
    fn flush(&mut self) -> io::Result<()> {
        if !self.dirty {
            return Ok(());
        }
        self.dirty = false;
        match (self.write.0)(self.bytes.clone()) {
            None => Ok(()),
            Some(error) => Err(io::Error::other(error)),
        }
    }
}
impl<W: Fn(Vec<u8>) -> Option<String>> Drop for Collected<W> {
    fn drop(&mut self) { let _ = self.flush(); }
}

/// A file with no path, read and written through C#: `read` returns the whole file or null when it
/// can't, and `write` gets the whole new contents and returns null or the error text.
/// covers: FileHandle::from_provider
pub fn file_handle_from_provider(name: String, read: impl Fn() -> Option<Vec<u8>> + 'static, write: impl Fn(Vec<u8>) -> Option<String> + 'static) -> rune::FileHandle {
    rune::FileHandle::from_provider(name, Provider { read: Shared(read), write: std::sync::Arc::new(Shared(write)) })
}

/// A menu standing in for a native popup menu, resolving `request` when picked or closed.
/// covers: Menu::from_popup_menu
pub fn menu_from_popup_menu<S: 'static>(key: String, request: &rune::PopupMenuRequest, on_resolve: impl Fn(&mut S, Option<&str>, &mut Tx) + 'static) -> rune_kit::Menu {
    rune_kit::Menu::from_popup_menu(key, request, on_resolve)
}

/// Sets the inclusive minimum and maximum values. covers: NumberField::range
pub fn number_field_range(field: rune_kit::NumberField, min: f64, max: f64) -> rune_kit::NumberField { field.range(min..=max) }
