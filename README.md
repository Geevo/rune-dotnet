# rune-dotnet
C# bindings for [Rune](https://github.com/anomalyco/rune), the Rust UI framework, built for **NativeAOT with static linking**. Your app ships as one self-contained native executable: no .NET runtime to install and no Rust shared library beside it.

```csharp
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<LightState>("Hello", (320f, 200f), () => new LightState(), View));

static El View(LightState state, Cx context)
{
    var theme = Tok.Theme(dark: true);

    var knob = El().Key("knob")
        .Size(18f, 18f).Radius(9f)
        .Fill(theme.Knob)
        .Absolute(state.On ? 18f : 2f, 2f);

    var toggle = El().Key("switch")
        .Role("switch").Label("Lights").Checked(state.On).Focusable(true)
        .Size(38f, 22f).Radius(11f)
        .Fill(state.On ? Tok.Accent(0, dark: true) : theme.ControlOff)
        .Motion(TokMotion.Snappy)
        .OnPress<LightState>(state => state.On = !state.On)
        .Child(knob);

    var label = Text(state.On ? "Lights on" : "Lights off")
        .Font(TokType.Title)
        .Color(theme.Text);

    return Col().WFull().HFull().Center().Gap(16f)
        .Fill(theme.Bg)
        .Child(label)
        .Child(toggle);
}

sealed class LightState
{
    public bool On { get; set; }
}
```

That's Rune's `examples/hello` in C# ([`samples/Hello`](samples/Hello/Program.cs)).

## Highlights

- **All of Rune's public API is bound.** 5,164 of 5,164 items, including rune-kit and rune-notify, across 436 object types and 141 closure shapes. [COVERAGE.md](native/rune-capi/COVERAGE.md) lists every item and how it's reached.
- **Generated, not hand-written.** [`tools/Rune.BindGen`](tools/Rune.BindGen) is a C# program that reads Rune's rustdoc JSON and writes both sides: the Rust C ABI shim and the C# API. Output is reproducible, and generation fails if anything public goes unbound, so an upstream change can't be silently missed.
- **Checked against Rust.** Every sample except the gallery has a Rust twin. [`tools/Rune.Verify`](tools/Rune.Verify) runs both through Rune's headless tooling with the same scripted input and compares output byte for byte.
- **Idiomatic C#.** Fluent builders, delegates for closures, `ReadOnlySpan<T>` for slices, `TimeSpan`, `Task`-returning file dialogs, and exceptions for Rust errors.
- **Safe by default.** Use-after-free, use-after-consume and escaping borrowed references throw or abort cleanly instead of corrupting memory. [`samples/Hazards`](samples/Hazards/Program.cs) shows each case.
- **Cross-platform tooling.** Generation and verification are plain .NET programs with no shell scripts.

## Status

Rune is in active development, and these bindings track one upstream commit, pinned in [`RUNE_COMMIT`](RUNE_COMMIT). Releases are tagged `vX.Y.Z`, and each release notes the Rune commit it binds.

| | Linux x64 | Windows x64 | macOS arm64 |
|---|---|---|---|
| Native shim builds | Yes | Yes, cross-compiled | Yes, cross-compiled |
| Linked and run | Yes, every sample verified | Not yet tested | Not yet tested |
| Platform-only API | None | None | 15 embedding APIs: `LayerHost`, `PixelBuffer`, `EmbedFrame`, `NativeSurface.Push`, `Embed.Prewarm` |

The Windows and macOS link settings in [`src/Rune/Rune.targets`](src/Rune/Rune.targets) come from rustc's list of native libraries. [CI](.github/workflows/ci.yml) links and runs everything on all three platforms. Reports and fixes from Windows and macOS users are very welcome.

## Getting started

### Install from NuGet

The [`RuneDotNet`](https://www.nuget.org/packages/RuneDotNet) package carries the native library for Linux x64, Windows x64 and macOS arm64, so there's no Rust to install. You need the [.NET 10 or 11 SDK](https://dotnet.microsoft.com/download) and the [NativeAOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites) for your platform (`clang` and `zlib1g-dev` on Linux), plus `libxkbcommon` on Linux.

```bash
dotnet new console -n MyApp
cd MyApp
dotnet add package RuneDotNet
```

Rune links into the executable at publish time, so the app must be published with NativeAOT. Add this to `MyApp.csproj`:

```xml
<PropertyGroup>
  <RuntimeIdentifier>$(NETCoreSdkPortableRuntimeIdentifier)</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

Put the example above in `Program.cs`, then:

```bash
dotnet publish -c Release -o out
out/MyApp
```

`dotnet run` won't work: a JIT build can't load a static library, and the build warns if `PublishAot` is off. The output is one executable with nothing to ship beside it.

### Build from source

#### Requirements

- [.NET 10 or 11 SDK](https://dotnet.microsoft.com/download), plus the [NativeAOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites) for your platform. On Linux these are `clang` and `zlib1g-dev`.
- [Rust](https://rustup.rs), with the toolchain Rune pins in its `rust-toolchain.toml`.
- On Linux, `libxkbcommon` (`libxkbcommon-dev` on Debian and Ubuntu, `libxkbcommon-devel` on Fedora). If only the runtime library is installed, the build links against it directly.

#### Build

```bash
git clone https://github.com/Geevo/rune-dotnet
cd rune-dotnet
git clone https://github.com/anomalyco/rune
git -C rune checkout $(cat RUNE_COMMIT)
```

The last line works in bash, zsh and PowerShell.

Build the native library, then publish a sample:

```bash
cd native/rune-capi
cargo build --release --lib
cd ../../samples/Hello
dotnet publish -c Release -o ../../out/hello
```

On Windows the library is `rune_capi.lib`. Samples build for the machine they're published on.

The samples target `net10.0`. Add `-p:RuneTfm=net11.0` to build one, and the library with it, for .NET 11.

Rune's headless tooling works unchanged from C#:

```bash
out/hello/hello --snap on.png --scene on
```

For a tour of rune-kit, publish [`samples/Gallery`](samples/Gallery/Program.cs) the same way. It has a page for each family of widgets, three themes in light and dark, and a scene for each overlay:

```bash
out/gallery/gallery --snap menu.png --scene menu --at 0.5
```

#### Use the source tree in your own app

Reference `src/Rune/Rune.csproj`, import `src/Rune/Rune.targets`, and publish with `PublishAot` and `SelfContained` set to true. [`samples/Hello/Hello.csproj`](samples/Hello/Hello.csproj) is a complete example, including size settings.

## Repository layout

| Path | What it is |
|---|---|
| `src/Rune/` | The C# library. `Generated.cs` is the generated API, `Rune.targets` does the static link. |
| `native/rune-capi/` | Rust `staticlib` that exposes Rune through a C ABI, plus the Rust twins of the samples. |
| `native/rune-ext/` | Small Rust adapters for APIs a C ABI can't express as written (see below). |
| `tools/Rune.BindGen/` | The generator. |
| `tools/Rune.Verify/` | Builds every sample and its Rust twin and compares their output. |
| `tests/PackageCheck/` | `Hello`, built from a local `RuneDotNet` package the way a user's app is. |
| `samples/` | C# ports of Rune's examples and focused checks. See below. |
| `rune/` | The upstream clone. Not tracked here and never modified. |

Samples:

- `Gallery`: as many rune-kit widgets as fit, live, across six pages and three themes. It's a showcase, so it has no Rust twin.
- `Hello`, `Todo`, `Settings`, `Shapes`, `Odometer`, `Matched`, `Texts`: ports of Rune's examples.
- `KitCheck`: rune-kit, owned objects and closures.
- `SpecCheck`: `AppSpec`'s optional fields. Run it from the repository root.
- `GenCheck`: generic types at their instantiations.
- `RuntimeCheck`: a headless `Runtime` driven with synthetic clicks, so stored `fn` pointers run.
- `ShapeCheck`: the composite shapes that cross the boundary as bytes.
- `ExtCheck`: the `rune-ext` adapters. `--gpu` submits work to a real device, `--compositor` renders a frame through a C# compositor and every render hook, and `--window` opens a real window.
- `Hazards`: misuse cases and what each one does.

## How Rust maps to C#

- Plain structs become record structs, and plain enums become enums.
- Other non-generic types become owned objects. A method taking `self` consumes the object, and `Free()` drops one that nothing consumed. Using a consumed object aborts the process.
- Data enums have `Which`, which returns a nested `Case` enum, and `AsVariant()` readers that return null for other variants.
- Objects read from a field or variant are borrowed: they last as long as their owner. Calling `Free()` on one, or passing it where Rune takes ownership, throws `InvalidOperationException`. A `motion_gate::Run` gives up its recording through `Ext.GateRecording`.
- Objects a callback receives by reference, and anything read from them, last until the callback returns. Using one later throws `InvalidOperationException`, as does using a `default` object. These checks need a wider handle than Rust sees, so a handle is 16 bytes in C#. Never reinterpret one as a pointer.
- `impl Fn` becomes a delegate, `Vec`, slices and iterators become `ReadOnlySpan<T>`, and `char` becomes `System.Text.Rune`.
- Generic types have no C form, so each is bound at concrete instantiations:
  - State parameters collapse to one `Host` type. `Runtime<S>` is `Runtime`, and methods that touch the state are generic in C#.
  - Data containers hold a managed object. `TreeModel<T>` takes and returns `object`.
  - Animatable and live types get one struct per value type, such as `PropF32`, `AnimatedColor` and `LiveF32x2`. Generic methods get one overload per type, such as `AnimateF32` and `AnimateColor`.
- Options, tuples, sequences and other composites cross the boundary as a byte buffer, `Wire`. [`wire.rs`](native/rune-capi/src/wire.rs) writes it and [`Wire.cs`](src/Rune/Wire.cs) reads it. `Range` becomes `(lo, hi)`, `Duration` becomes `TimeSpan`, and `Err` becomes `RuneException`.
- Plain `fn` pointers become delegates, but Rust can only call a fixed set of them. Each signature has 64 slots, filled once per distinct delegate and never released. Pass a method group or a `static` lambda: a capturing lambda takes a new slot each time, and past 64 the call throws `InvalidOperationException`.

### Adapters

`native/rune-ext` holds a Rust function for each API a C ABI can't express as written. The generator reads it like any other crate and exposes it on the `Ext` class. A doc line `covers: Type::method` ties a function to the Rune API it stands in for, and COVERAGE.md lists these pairs. Most are small changes of shape:

- A trait becomes closures. `Ext.AppCompositor` takes `features`, `render`, `animating` and `summary`, and `Ext.MotionTraceNew` takes a delegate that receives bytes.
- A generic value becomes a C# object. Where Rust needs `PartialEq`, it calls the object's `Equals`.
- `impl Hash` parameters take a `HashKey`, which converts implicitly from a string, an integer or a `byte[]` and hashes as the Rust value does. `impl Display` parameters take any object, formatted with the invariant culture.
- Borrowed results are copied or visited. `Ext.TreeRow` returns a `TreeRowInfo`, and `Ext.TreeSubtree` calls a delegate per item.
- `FileDialogs.OpenAsync`, `SaveAsync` and `OpenFolderAsync` return a `Task<string?>`. Null is a cancel, and a failure faults the task. The dialog waits on its own thread, so they're safe to call from the UI thread.
- `OsString` becomes `string`. `Ext.AppDirsFromEnv` and `Ext.HomeFrom` take a lookup that returns a variable's value or null.
- `AppSpec.SetScenes` takes every scene at once and leaks one copy of the list, because `AppSpec.scenes` is `'static`.
- `ImageAtlas` is in a private Rune module, so C# reaches it only as `Ext.RendererUploadImages(..., runtime)`.

### Types from Rune's dependencies

Types from wgpu, parley, accesskit, `serde_json` and `image` cross as opaque objects. Rune's API can take and return them, but their own APIs aren't bound. `rune-ext` adds only what's needed to make or read them, such as `Ext.DeviceCreateEncoder`, `Ext.QueueSubmit` and `Ext.JsonParse`. To draw with wgpu from C#, add the wgpu calls you need to `rune-ext`.

Enum values for these types come from [`external-enums.txt`](tools/Rune.BindGen/external-enums.txt). A variant with data is listed as `Name=factory`, and generation fails if `rune-ext` has no such function. `WgpuTextureFormat.Astc(block, channel)` is the only one. The generated Rust matches each list exhaustively, so a variant added upstream stops the build. The exception is `PhysicalKeyCode`, which is `#[non_exhaustive]`.

### Platform-specific API

Generation always reads both the Linux and macOS APIs, so its output is the same on every host. macOS is the base, because its API contains the others. What Linux lacks is gated with `#[cfg(target_os = "macos")]` in Rust and `[SupportedOSPlatform("macos")]` in C#. COVERAGE.md lists those symbols, and generation fails if Linux gains anything macOS doesn't have.

Call macOS-only APIs behind `OperatingSystem.IsMacOS()`. Without that check you get a CA1416 warning and a link error on other platforms, not a runtime failure.

## Development

### Regenerating the bindings

```bash
dotnet run -c Release --project tools/Rune.BindGen
```

This rewrites `native/rune-capi/src/generated.rs`, `src/Rune/Generated.cs` and `native/rune-capi/COVERAGE.md`. It exits non-zero if any public API goes unbound and lists each one, but still writes its output, so COVERAGE.md shows the same list. To skip an item on purpose, add it to [`deny.txt`](tools/Rune.BindGen/deny.txt).

Generation needs the Linux and macOS targets for both your default toolchain and the one Rune pins in `rune/rust-toolchain.toml`. Run this once in the repository root and once inside `rune/`, where rustup picks up Rune's toolchain by itself:

```bash
rustup target add x86_64-unknown-linux-gnu aarch64-apple-darwin
```

To move to a newer Rune, put the new commit in `RUNE_COMMIT`, check it out in `rune/`, regenerate, and verify. CI reads the same file. Both tools refuse to run if `rune/` isn't at the pinned commit, or if `src/Rune/Rune.csproj`, `native/rune-capi/Cargo.toml` and `native/rune-ext/Cargo.toml` disagree on the version.

### Verifying

```bash
dotnet run -c Release --project tools/Rune.Verify -- --build
```

This builds everything one link at a time, compares every sample with its Rust twin and runs the adapters. Both sides run on the same machine, so no reference output is stored. Leave out `--build` to reuse what's built. Add `--window` to also open a real window, which needs a display.

### Packaging and releasing

To try the package locally, stage the native library under `artifacts/native/<rid>/`, pack, and publish `tests/PackageCheck` from it:

```bash
mkdir -p artifacts/native/linux-x64
cp target/release/librune_capi.a artifacts/native/linux-x64/
dotnet pack src/Rune -c Release -o artifacts/packages
dotnet publish tests/PackageCheck -c Release -o out/packagecheck
```

A local pack warns about the platforms it's missing. Delete `out/packagecheck-packages` before publishing a repack of the same version, or the old one is reused.

A release bumps `<Version>` in `src/Rune/Rune.csproj` and both `Cargo.toml` files (the tools refuse to run if they differ), then pushes a `vX.Y.Z` tag. The [release workflow](.github/workflows/release.yml) builds the native library on each platform, packs all three into one package, tests it in a fresh app on each platform, then pushes it to NuGet and creates a GitHub release. It publishes with NuGet's [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so there's no API key to store: it needs a policy on nuget.org for this repository, `release.yml` and the `nuget` environment, and a `NUGET_USER` repository variable holding the nuget.org user name. Run it by hand to build and test a package without publishing.

### Build notes

- Fat LTO peaks at about 2.5 GB per binary, and a plain `cargo build --release` links every twin at once, which can exhaust memory. Build them one at a time with `cargo build --release --bin NAME -j2`.
- Without the xkbcommon development package there's no `libxkbcommon.so` to link against. Cargo then links `target/linkshim/libxkbcommon.so` to the runtime library, `libxkbcommon.so.0`, and an app's publish does the same under its `obj/` folder.
- For a smaller build without accessibility, D-Bus services, SVG or Ogg audio:

  ```bash
  (cd native/rune-capi && cargo build --release --lib --no-default-features --target-dir ../../target/slim)
  rm -rf samples/Hello/obj/Release/net10.0/linux-x64/native
  (cd samples/Hello && dotnet publish -c Release -o ../../out/slim -p:RuneLib=$PWD/../../target/slim/release/librune_capi.a)
  ```

  The `rm` matters: publish doesn't relink when only `RuneLib` changes, so it would quietly reuse the previous binary.

## License

[MIT](LICENSE). Rune itself is MIT-licensed by Anomaly.
