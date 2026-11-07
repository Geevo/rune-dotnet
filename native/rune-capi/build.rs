// Linking needs `libxkbcommon.so`, which comes with the development package (`libxkbcommon-dev`,
// `libxkbcommon-devel`). Without it, a link to the runtime library in `target/linkshim` stands in.
// `src/Rune/Rune.targets` searches the same directory, so apps link the same way.
#[cfg(unix)]
use std::path::{Path, PathBuf};

#[cfg(unix)]
const LIB_DIRS: &[&str] = &["/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib", "/lib64", "/lib"];

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    #[cfg(unix)]
    shim();
}

#[cfg(unix)]
fn shim() {
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("linux") || std::env::var("HOST") != std::env::var("TARGET") {
        return;
    }
    if LIB_DIRS.iter().any(|dir| Path::new(dir).join("libxkbcommon.so").exists()) {
        return;
    }
    let Some(runtime) = LIB_DIRS.iter().map(|dir| Path::new(dir).join("libxkbcommon.so.0")).find(|path| path.exists()) else {
        return;
    };
    let shim = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap()).join("../../target/linkshim");
    std::fs::create_dir_all(&shim).unwrap();
    let link = shim.join("libxkbcommon.so");
    let _ = std::fs::remove_file(&link);
    std::os::unix::fs::symlink(&runtime, &link).unwrap();
    println!("cargo:rustc-link-search=native={}", shim.canonicalize().unwrap().display());
}
