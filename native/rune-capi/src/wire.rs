//! Bytes for values too rich for a C signature: nested options, tuples with strings, lists of
//! strings. `tools/Rune.BindGen` writes the encoders on both sides; `src/Rune/Wire.cs` is the twin.

use std::cell::{Cell, RefCell};
use std::ffi::c_void;
use std::fmt::{Debug, Display};
use std::sync::OnceLock;
use std::time::{Duration, Instant};

use super::Utf8;

thread_local! {
    static RETURNED: RefCell<Vec<u8>> = const { RefCell::new(Vec::new()) };
}

pub struct WireOut(Vec<u8>);

impl WireOut {
    pub fn new() -> Self {
        Self(Vec::new())
    }

    pub fn raw<T: Copy>(&mut self, v: &T) {
        // SAFETY: `v` is `size_of::<T>()` readable bytes; only C-layout values come through here.
        self.0.extend_from_slice(unsafe { std::slice::from_raw_parts(v as *const T as *const u8, size_of::<T>()) });
    }

    pub fn len(&mut self, n: usize) {
        self.raw(&(n as u64));
    }

    pub fn tag(&mut self, on: bool) {
        self.0.push(on as u8);
    }

    pub fn str(&mut self, s: &str) {
        self.len(s.len());
        self.0.extend_from_slice(s.as_bytes());
    }

    pub fn slice<T: Copy>(&mut self, s: &[T]) {
        self.len(s.len());
        // SAFETY: the slice is `size_of_val(s)` readable bytes.
        self.0.extend_from_slice(unsafe { std::slice::from_raw_parts(s.as_ptr() as *const u8, size_of_val(s)) });
    }

    pub fn bytes(&self) -> (*const u8, usize) {
        (self.0.as_ptr(), self.0.len())
    }

    /// Valid until the next `ret` on this thread; C# decodes it before calling back in.
    pub fn ret(self) -> Utf8 {
        RETURNED.with_borrow_mut(|r| {
            *r = self.0;
            Utf8 { ptr: r.as_ptr(), len: r.len() }
        })
    }
}

/// Reads what C# wrote. Temporaries that a signature borrows live here until the call ends.
pub struct WireIn {
    at: Cell<*const u8>,
    end: *const u8,
    held: RefCell<Vec<(*mut u8, unsafe fn(*mut u8))>>,
}

impl WireIn {
    pub unsafe fn new(ptr: *const u8, len: usize) -> Self {
        Self { at: Cell::new(ptr), end: ptr.wrapping_add(len), held: RefCell::new(Vec::new()) }
    }

    pub unsafe fn of(s: Utf8) -> Self {
        unsafe { Self::new(s.ptr, s.len) }
    }

    fn advance(&self, n: usize) -> *const u8 {
        let start = self.at.get();
        assert!(self.end as usize - start as usize >= n, "wire value ended early");
        self.at.set(start.wrapping_add(n));
        start
    }

    pub fn raw<T: Copy>(&self) -> T {
        let p = self.advance(size_of::<T>());
        // SAFETY: `advance` checked the bytes are there; C# wrote a `T`.
        unsafe { (p as *const T).read_unaligned() }
    }

    pub fn len(&self) -> usize {
        self.raw::<u64>() as usize
    }

    pub fn tag(&self) -> bool {
        self.raw::<u8>() != 0
    }

    pub fn str<'a>(&self) -> &'a str {
        let n = self.len();
        let p = self.advance(n);
        // SAFETY: C# encodes valid UTF-8 and keeps the buffer pinned for the call.
        unsafe { super::utf8(p, n) }
    }

    pub fn slice<T: Copy>(&self) -> Vec<T> {
        let n = self.len();
        let p = self.advance(n * size_of::<T>());
        let mut v = Vec::<T>::with_capacity(n);
        // SAFETY: both ranges hold `n` items, and `v` has the room.
        unsafe {
            std::ptr::copy_nonoverlapping(p, v.as_mut_ptr() as *mut u8, n * size_of::<T>());
            v.set_len(n);
        }
        v
    }

    /// Keeps `v` until this reader drops; the borrow checker holds callers to that.
    pub fn hold<T>(&self, v: T) -> &T {
        unsafe fn free<T>(p: *mut u8) {
            // SAFETY: `hold` made this box.
            drop(unsafe { Box::from_raw(p as *mut T) });
        }
        let p = Box::into_raw(Box::new(v));
        self.held.borrow_mut().push((p as *mut u8, free::<T>));
        // SAFETY: the box stays put until `drop`.
        unsafe { &*p }
    }
}

impl Drop for WireIn {
    fn drop(&mut self) {
        for (p, free) in self.held.get_mut().drain(..).rev() {
            // SAFETY: `hold` paired them.
            unsafe { free(p) };
        }
    }
}

pub fn ptr_of<T>(r: &T) -> *mut c_void {
    r as *const T as *mut c_void
}

/// Display when the error has it, otherwise Debug: `ErrText(&e).text()`.
pub struct ErrText<'a, T>(pub &'a T);

impl<T: Display> ErrText<'_, T> {
    pub fn text(&self) -> String {
        self.0.to_string()
    }
}

pub trait DebugText {
    fn text(&self) -> String;
}

impl<T: Debug> DebugText for ErrText<'_, T> {
    fn text(&self) -> String {
        format!("{:?}", self.0)
    }
}

fn epoch() -> Instant {
    static EPOCH: OnceLock<Instant> = OnceLock::new();
    *EPOCH.get_or_init(Instant::now)
}

pub fn wire_dur(seconds: f64) -> Duration {
    Duration::try_from_secs_f64(seconds).unwrap_or_default()
}

/// Instants cross as seconds from one process-wide origin.
pub fn wire_secs(i: &Instant) -> f64 {
    let e = epoch();
    if *i >= e { (*i - e).as_secs_f64() } else { -(e - *i).as_secs_f64() }
}

pub fn wire_instant(seconds: f64) -> Instant {
    let e = epoch();
    if seconds >= 0.0 { e + wire_dur(seconds) } else { e - wire_dur(-seconds) }
}

/// A closure's return can only borrow from its argument if the signature says so at the closure.
pub fn cow_key<F: for<'a> Fn(&'a super::Host, usize) -> std::borrow::Cow<'a, str>>(f: F) -> F {
    f
}
