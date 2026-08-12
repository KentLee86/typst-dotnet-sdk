use std::borrow::Cow;
use std::path::PathBuf;

use typst::diag::{FileError, FileResult};
use typst::foundations::Bytes;
use typst::syntax::{FileId, Source};
use typst_as_lib::file_resolver::FileResolver;

pub struct CacheOnlyPackageResolver {
    root: PathBuf,
}

impl CacheOnlyPackageResolver {
    pub fn new() -> Self {
        let root = dirs::cache_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("typst/packages");
        Self { root }
    }

    fn read(&self, id: FileId) -> FileResult<Vec<u8>> {
        let package = id.package().ok_or_else(|| not_found(id))?;
        let root = self
            .root
            .join(package.namespace.as_str())
            .join(package.name.as_str())
            .join(package.version.to_string());
        let path = id.vpath().resolve(&root).ok_or_else(|| not_found(id))?;
        std::fs::read(&path).map_err(|error| FileError::from_io(error, &path))
    }
}

impl FileResolver for CacheOnlyPackageResolver {
    fn resolve_binary(&self, id: FileId) -> FileResult<Cow<'_, Bytes>> {
        Ok(Cow::Owned(Bytes::new(self.read(id)?)))
    }

    fn resolve_source(&self, id: FileId) -> FileResult<Cow<'_, Source>> {
        let bytes = self.read(id)?;
        let text = String::from_utf8(bytes).map_err(|_| FileError::InvalidUtf8)?;
        Ok(Cow::Owned(Source::new(
            id,
            text.trim_start_matches('\u{feff}').to_owned(),
        )))
    }
}

fn not_found(id: FileId) -> FileError {
    FileError::NotFound(id.vpath().as_rootless_path().to_path_buf())
}
