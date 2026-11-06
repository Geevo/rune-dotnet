# rune-dotnet

Proof of concept: [Rune](https://github.com/anomalyco/rune) from C#, statically linked into a NativeAOT executable.

- `rune/`: upstream clone, unmodified and not tracked here. Clone it at `4ca683561a100e16b1ce32f3576f4ade61bd2924`, the commit CI pins.
- `native/rune-capi/`: Rust `staticlib` exposing a C ABI, plus `hello_rust`, the baseline.
- `src/Rune/`: C# wrapper. `Rune.targets` does the static link.
- `tools/Rune.BindGen/`: C# generator that writes both sides of the bindings from Rune's rustdoc JSON.
- `samples/Hello/`: Rune's `examples/hello` in C#.
- `samples/KitCheck/`: exercises rune-kit, owned objects and closures; `kit_rust` is the same app in Rust.
- `samples/SpecCheck/`: exercises `AppSpec`'s optional fields; `spec_rust` is the same app in Rust. Run it from the repository root.
- `samples/GenCheck/`: exercises generic types at their instantiations; `gen_rust` is the same program in Rust, and their output must match byte for byte.
- `samples/RuntimeCheck/`: drives a headless `Runtime` with synthetic clicks, so stored `fn` pointers run; `rt_rust` is the same program in Rust.
- `samples/ShapeCheck/`: exercises the composite shapes in `Wire`.
- `samples/Todo`, `Settings`, `Shapes`, `Odometer`, `Matched`, `Texts`: ports of Rune examples; the originals are in `native/rune-capi/src/twins/`.
- `samples/Hazards/`: misuse cases and what each one does.
- `samples/ExtCheck/`: runs the `rune-ext` adapters and asserts their results. `--gpu` submits work to a real device, `--compositor` renders a frame through a C# compositor and every render hook, and `--window` opens a real window.
- `tools/Rune.Verify`: runs every Rust twin against its C# port and compares output byte for byte.

## Bindings

`dotnet run -c Release --project tools/Rune.BindGen` regenerates `native/rune-capi/src/generated.rs`, `src/Rune/Generated.cs` and `native/rune-capi/COVERAGE.md`, which lists what was skipped and why. Output is reproducible. To skip an item, add it to `tools/Rune.BindGen/deny.txt`.

How Rust types appear in C#:

- Plain structs become record structs, and plain enums become enums.
- Other non-generic types become owned objects: a method taking `self` consumes the object, and `Free()` drops one nothing consumed. Using a consumed object aborts the process.
- Data enums have `Which`, which returns a nested `Case` enum, and `AsVariant()` readers that return null for other variants.
- Objects read from a field or variant are borrowed: they last as long as their owner. Calling `Free()` on one, or passing it where Rune takes ownership, throws `InvalidOperationException`. A `motion_gate::Run` gives up its recording through `Ext.GateRecording`.
- Objects a callback receives by reference, and anything read from them, last until the callback returns. Using one later throws `InvalidOperationException`, as does using a `default` object. These checks need a wider handle than Rust sees, so a handle is 16 bytes in C#; never reinterpret one as a pointer.
- `impl Fn` becomes a delegate, `Vec`, slices and iterators become `ReadOnlySpan<T>`, and `char` becomes `System.Text.Rune`.
- Generic types have no C form, so each is bound at concrete instantiations. State parameters collapse to one `Host` type (`Runtime<S>` is `Runtime`, and methods that touch the state are generic in C#). Data containers hold a managed object (`TreeModel<T>` takes and returns `object`). Animatable and live types get one struct per value type (`PropF32`, `AnimatedColor`, `LiveF32x2`), and generic methods get one overload per type (`AnimateF32`, `AnimateColor`).
- Options, tuples, sequences and other composites cross the boundary as a byte buffer (`Wire`): `native/rune-capi/src/wire.rs` writes it and `src/Rune/Wire.cs` reads it. `Range` becomes `(lo, hi)`, `Duration` becomes `TimeSpan`, and `Err` becomes `RuneException`.
- Plain `fn` pointers become delegates, but Rust can only call a fixed set of them. Each signature has 64 slots, filled once per distinct delegate and never released. Pass a method group or a `static` lambda: a capturing lambda takes a new slot each time, and past 64 the call throws `InvalidOperationException`.

### Adapters and external types

`native/rune-ext` holds a Rust function for each API a C ABI can't express as written. The generator reads it like any other crate and exposes it on the `Ext` class. A doc line `covers: Type::method` ties a function to the Rune API it stands in for, and COVERAGE.md lists these pairs. Most are small changes of shape:

- A trait becomes closures: `Ext.AppCompositor` takes `features`, `render`, `animating` and `summary`, and `Ext.MotionTraceNew` takes a delegate that receives bytes.
- A generic value becomes a C# object. Where Rust needs `PartialEq`, it calls the object's `Equals`.
- `impl Hash` parameters take a `HashKey`, which converts implicitly from a string, an integer or a `byte[]` and hashes as the Rust value does. `impl Display` parameters take any object, formatted with the invariant culture.
- Borrowed results are copied or visited: `Ext.TreeRow` returns a `TreeRowInfo`, and `Ext.TreeSubtree` calls a delegate per item.
- `FileDialogs.OpenAsync`, `SaveAsync` and `OpenFolderAsync` return a `Task<string?>`: null is a cancel, and a failure faults the task. The dialog waits on its own thread, so they are safe to call from the UI thread.
- `AppSpec.SetScenes` takes every scene at once and leaks one copy of the list, because `AppSpec.scenes` is `'static`.
- `ImageAtlas` is in a private Rune module, so C# reaches it only as `Ext.RendererUploadImages(..., runtime)`.

Types from Rune's dependencies (wgpu, parley, accesskit, `serde_json`, `image`) cross as opaque objects: Rune's API can take and return them, but their own APIs are not bound. `rune-ext` adds only what's needed to make or read them, such as `Ext.DeviceCreateEncoder`, `Ext.QueueSubmit` and `Ext.JsonParse`. To draw with wgpu from C#, add the wgpu calls you need to `rune-ext`. Enum values for these types come from `tools/Rune.BindGen/external-enums.txt`. A variant with data is listed as `Name=factory`, and generation fails if `rune-ext` has no such function: `WgpuTextureFormat.Astc(block, channel)` is the only one. The generated Rust matches each list exhaustively, so a variant added upstream stops the build, except for `PhysicalKeyCode`, which is `#[non_exhaustive]`.

## Platforms

| | Linux x64 | Windows x64 | macOS arm64 |
|---|---|---|---|
| Shim compiles to a static library | Yes | Yes, cross-compiled | Yes, cross-compiled |
| Linked and run | Yes | Not yet | Not yet |
| Platform-only API | None | None | 15 embedding APIs: `LayerHost`, `PixelBuffer`, `EmbedFrame`, `NativeSurface.Push`, `Embed.Prewarm` |

The tools are plain .NET, so generating and verifying need no shell. Generation always reads the Linux and macOS APIs, so its output is the same on every host. macOS is the base, because its API contains the others, and what Linux lacks is gated with `#[cfg(target_os = "macos")]` in Rust and `[SupportedOSPlatform("macos")]` in C#. COVERAGE.md lists those symbols, and generation fails if Linux gains anything macOS doesn't have.

Call macOS-only APIs behind `OperatingSystem.IsMacOS()`. Without that check you get a CA1416 warning and a link error on other platforms, not a runtime failure.

Generation needs both targets installed, for Rune's pinned toolchain and for your default one. Add them with:

```bash
rustup target add x86_64-unknown-linux-gnu aarch64-apple-darwin --toolchain 1.99.1
```

```bash
rustup target add x86_64-unknown-linux-gnu aarch64-apple-darwin
```

The Windows and macOS link settings in `src/Rune/Rune.targets` come from rustc's list of native libraries. `.github/workflows/ci.yml` links and runs everything on all three, but it hasn't run yet, so those settings are still untested. `tools/Rune.Verify` runs both sides on the same machine, so it needs no reference output.

## Build

Linux is shown; on Windows the library is `rune_capi.lib`. Samples build for the machine they're published on.

```bash
cd native/rune-capi
cargo build --release --lib
cd ../../samples/Hello
dotnet publish -c Release -o ../../out/net10
```

`native/linkshim/libxkbcommon.so` is a symlink to the system `libxkbcommon.so.0`, standing in for `libxkbcommon-devel`.

Fat LTO peaks at about 2.5 GB per binary, and `cargo build --release` links all the twins at once, which can exhaust memory. Build them one at a time with `cargo build --release --bin NAME -j2`.

Smaller build without accessibility, D-Bus services, SVG or Ogg audio:

```bash
(cd native/rune-capi && cargo build --release --lib --no-default-features --target-dir ../../target/slim)
rm -rf samples/Hello/obj/Release/net10.0/linux-x64/native
(cd samples/Hello && dotnet publish -c Release -o ../../out/slim -p:RuneLib=$PWD/../../target/slim/release/librune_capi.a)
```

The `rm` matters: publish does not relink when only `RuneLib` changes, so it would quietly reuse the previous binary.

## Check

Rune's headless tooling works unchanged, so output can be compared with the Rust build:

```bash
out/net10/hello --snap on.png --scene on
out/net10/hello --selftest-unwind
```

To build everything one link at a time, compare every sample with its Rust twin and run the adapters:

```bash
dotnet run -c Release --project tools/Rune.Verify -- --build
```

Leave out `--build` to reuse what's built. Add `--window` to open a real window too, which needs a display.

Generation exits non-zero if any public API goes unbound, listing each one. It still writes its output, so the coverage report shows the same list.
