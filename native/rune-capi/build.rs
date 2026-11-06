// Fedora ships libxkbcommon.so.0 without the -devel symlink; ../linkshim supplies `libxkbcommon.so`.
fn main() {
    let dir = std::path::Path::new(&std::env::var("CARGO_MANIFEST_DIR").unwrap()).join("../linkshim");
    println!("cargo:rustc-link-search=native={}", dir.canonicalize().unwrap().display());
}
