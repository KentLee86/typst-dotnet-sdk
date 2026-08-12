use std::env;
use std::fs;
use std::path::{Path, PathBuf};

const EMBEDDED_PACKAGES: &[(&str, &str)] = &[("cetz", "0.5.2"), ("oxifmt", "1.0.0")];

fn collect_files(directory: &Path, files: &mut Vec<PathBuf>) {
    let mut entries = fs::read_dir(directory)
        .unwrap_or_else(|error| panic!("failed to read {}: {error}", directory.display()))
        .map(|entry| entry.expect("failed to read embedded package entry").path())
        .collect::<Vec<_>>();
    entries.sort();

    for path in entries {
        if path.is_dir() {
            collect_files(&path, files);
        } else if path.is_file() {
            files.push(path);
        }
    }
}

fn main() {
    let manifest_dir =
        PathBuf::from(env::var_os("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR must be set"));
    let mut generated =
        String::from("pub static EMBEDDED_PACKAGE_FILES: &[(&str, &str, &[u8])] = &[\n");
    for &(name, version) in EMBEDDED_PACKAGES {
        let package_root = manifest_dir
            .join("assets/packages")
            .join(name)
            .join(version);
        println!("cargo:rerun-if-changed={}", package_root.display());

        let mut files = Vec::new();
        collect_files(&package_root, &mut files);
        assert!(!files.is_empty(), "embedded package {name} is empty");

        let spec = format!("@preview/{name}:{version}");
        for path in files {
            let relative = path
                .strip_prefix(&package_root)
                .expect("embedded package file must remain under its root")
                .to_string_lossy()
                .replace('\\', "/");
            let absolute = path.to_string_lossy();
            generated.push_str(&format!(
                "    ({spec:?}, {relative:?}, include_bytes!({absolute:?}) as &'static [u8]),\n"
            ));
        }
    }
    generated.push_str("];\n");

    let output = PathBuf::from(env::var_os("OUT_DIR").expect("OUT_DIR must be set"))
        .join("embedded_cetz_files.rs");
    fs::write(&output, generated)
        .unwrap_or_else(|error| panic!("failed to write {}: {error}", output.display()));
}
