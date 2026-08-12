use std::borrow::Cow;
use std::collections::HashMap;
use std::path::{Component, Path};
use std::sync::{Arc, RwLock};

use anyhow::{Result, bail};
use typst::diag::{FileError, FileResult};
use typst::foundations::Bytes;
use typst::syntax::{FileId, Source, VirtualPath};
use typst_as_lib::file_resolver::FileResolver;

#[derive(Clone, Default)]
pub struct MemoryProjectResolver {
    files: Arc<RwLock<HashMap<FileId, MemoryFile>>>,
}

#[derive(Clone)]
enum MemoryFile {
    Source(Source),
    Binary(Bytes),
}

pub struct ProjectGuard {
    resolver: MemoryProjectResolver,
    ids: Vec<FileId>,
}

impl MemoryProjectResolver {
    pub fn source(&self, id: FileId) -> Option<Source> {
        match self
            .files
            .read()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .get(&id)
        {
            Some(MemoryFile::Source(source)) => Some(source.clone()),
            _ => None,
        }
    }

    pub fn register_project(&self, files: &[(String, Vec<u8>, bool)]) -> Result<ProjectGuard> {
        let mut seen = std::collections::HashSet::with_capacity(files.len());
        for (path, bytes, is_text) in files {
            validate_project_path(path)?;
            if !seen.insert(path.as_str()) {
                bail!("duplicate project path: {path}");
            }
            if *is_text && std::str::from_utf8(bytes).is_err() {
                bail!("text file is not valid UTF-8: {path}");
            }
        }
        let mut map = self
            .files
            .write()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let mut ids = Vec::with_capacity(files.len());
        for (path, bytes, is_text) in files {
            let id = FileId::new(None, VirtualPath::new(path));
            let file = if *is_text {
                let text = std::str::from_utf8(bytes).expect("validated above");
                MemoryFile::Source(Source::new(
                    id,
                    text.trim_start_matches('\u{feff}').to_owned(),
                ))
            } else {
                MemoryFile::Binary(Bytes::new(bytes.clone()))
            };
            map.insert(id, file);
            ids.push(id);
        }
        Ok(ProjectGuard {
            resolver: self.clone(),
            ids,
        })
    }
}

impl FileResolver for MemoryProjectResolver {
    fn resolve_binary(&self, id: FileId) -> FileResult<Cow<'_, Bytes>> {
        match self
            .files
            .read()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .get(&id)
            .cloned()
        {
            Some(MemoryFile::Binary(bytes)) => Ok(Cow::Owned(bytes)),
            Some(MemoryFile::Source(source)) => {
                Ok(Cow::Owned(Bytes::new(source.text().as_bytes().to_vec())))
            }
            None => Err(not_found(id)),
        }
    }

    fn resolve_source(&self, id: FileId) -> FileResult<Cow<'_, Source>> {
        match self
            .files
            .read()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .get(&id)
            .cloned()
        {
            Some(MemoryFile::Source(source)) => Ok(Cow::Owned(source)),
            _ => Err(not_found(id)),
        }
    }
}

impl Drop for ProjectGuard {
    fn drop(&mut self) {
        let mut map = self
            .resolver
            .files
            .write()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        for id in &self.ids {
            map.remove(id);
        }
    }
}

pub fn validate_project_path(path: &str) -> Result<()> {
    let path = Path::new(path);
    if path.as_os_str().is_empty() || path.is_absolute() {
        bail!("project path must be a non-empty relative path");
    }
    if path.components().any(|component| {
        matches!(
            component,
            Component::ParentDir | Component::RootDir | Component::Prefix(_)
        )
    }) {
        bail!("project path must not contain '..' or a root prefix");
    }
    Ok(())
}

fn not_found(id: FileId) -> FileError {
    FileError::NotFound(id.vpath().as_rootless_path().to_path_buf())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_parent_paths_and_duplicates() {
        assert!(validate_project_path("../secret.typ").is_err());
        let resolver = MemoryProjectResolver::default();
        let duplicate = vec![
            ("main.typ".to_owned(), b"one".to_vec(), true),
            ("main.typ".to_owned(), b"two".to_vec(), true),
        ];
        assert!(resolver.register_project(&duplicate).is_err());
    }
}
