use std::borrow::Cow;
use std::collections::HashMap;
use std::str::FromStr;

use typst::diag::{FileError, FileResult};
use typst::foundations::Bytes;
use typst::syntax::package::PackageSpec;
use typst::syntax::{FileId, Source, VirtualPath};
use typst_as_lib::file_resolver::FileResolver;

include!(concat!(env!("OUT_DIR"), "/embedded_cetz_files.rs"));

pub struct EmbeddedPackageResolver {
    binaries: HashMap<FileId, Bytes>,
    sources: HashMap<FileId, Source>,
}

impl EmbeddedPackageResolver {
    pub fn new() -> Self {
        let mut binaries = HashMap::with_capacity(EMBEDDED_PACKAGE_FILES.len());
        let mut sources = HashMap::with_capacity(EMBEDDED_PACKAGE_FILES.len());

        for &(spec, path, contents) in EMBEDDED_PACKAGE_FILES {
            let package = PackageSpec::from_str(spec)
                .expect("every embedded Typst package spec must be valid");
            let id = FileId::new(Some(package), VirtualPath::new(path));
            binaries.insert(id, Bytes::new(contents.to_vec()));
            if let Ok(text) = std::str::from_utf8(contents) {
                sources.insert(
                    id,
                    Source::new(id, text.trim_start_matches('\u{feff}').to_owned()),
                );
            }
        }

        Self { binaries, sources }
    }
}

impl FileResolver for EmbeddedPackageResolver {
    fn resolve_binary(&self, id: FileId) -> FileResult<Cow<'_, Bytes>> {
        self.binaries
            .get(&id)
            .map(Cow::Borrowed)
            .ok_or_else(|| not_found(id))
    }

    fn resolve_source(&self, id: FileId) -> FileResult<Cow<'_, Source>> {
        self.sources
            .get(&id)
            .map(Cow::Borrowed)
            .ok_or_else(|| not_found(id))
    }
}

fn not_found(id: FileId) -> FileError {
    FileError::NotFound(id.vpath().as_rootless_path().to_path_buf())
}

#[cfg(test)]
mod tests {
    use super::*;

    const CETZ_PACKAGE_SPEC: &str = "@preview/cetz:0.5.2";
    const OXIFMT_PACKAGE_SPEC: &str = "@preview/oxifmt:1.0.0";

    fn cetz_file(path: &str) -> FileId {
        let package = PackageSpec::from_str(CETZ_PACKAGE_SPEC).unwrap();
        FileId::new(Some(package), VirtualPath::new(path))
    }

    #[test]
    fn embeds_manifest_sources_and_wasm() {
        let resolver = EmbeddedPackageResolver::new();
        assert!(resolver.resolve_source(cetz_file("typst.toml")).is_ok());
        assert!(resolver.resolve_source(cetz_file("src/lib.typ")).is_ok());

        let wasm = resolver
            .resolve_binary(cetz_file("cetz-core/cetz_core.wasm"))
            .expect("embedded WASM should resolve");
        assert!(wasm.starts_with(b"\0asm"));
        assert_eq!(wasm.len(), 343_529);

        let oxifmt = PackageSpec::from_str(OXIFMT_PACKAGE_SPEC).unwrap();
        let oxifmt_id = FileId::new(Some(oxifmt), VirtualPath::new("oxifmt.typ"));
        assert!(resolver.resolve_source(oxifmt_id).is_ok());
    }
}
