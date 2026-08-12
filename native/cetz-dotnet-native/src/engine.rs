use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use anyhow::{Context, Result, bail};
use serde::Serialize;
use typst::diag::{Severity, SourceDiagnostic};
use typst::foundations::Dict;
use typst::layout::PagedDocument;
use typst::syntax::{FileId, VirtualPath};
use typst_as_lib::file_resolver::FileSystemResolver;
use typst_as_lib::typst_kit_options::TypstKitFontOptions;
use typst_as_lib::{TypstAsLibError, TypstEngine, TypstTemplateCollection};

use crate::cache_packages::CacheOnlyPackageResolver;
use crate::embedded_cetz::EmbeddedPackageResolver;
use crate::memory_source::{MemoryProjectResolver, validate_project_path};

pub const TYPST_VERSION: &str = "0.14.2";
const NOTO_SANS_KR_REGULAR: &[u8] = include_bytes!("../assets/fonts/NotoSansKR-Regular.otf");

#[derive(Debug, Clone)]
pub struct EngineConfig {
    pub root: PathBuf,
    pub system_fonts: bool,
    pub font_paths: Vec<PathBuf>,
    pub memory_fonts: Vec<Vec<u8>>,
    pub cache_generations: usize,
    pub package_resolution: PackageResolution,
    pub restrict_to_directory: Option<PathBuf>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PackageResolution {
    CacheThenNetwork,
    CacheOnly,
    EmbeddedOnly,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum OutputFormat {
    Svg,
    Png,
    Pdf,
    Rgba,
}

#[derive(Debug)]
pub struct Artifact {
    pub format: OutputFormat,
    pub page: Option<usize>,
    pub file_name: String,
    pub bytes: Vec<u8>,
    pub width: Option<u32>,
    pub height: Option<u32>,
    pub stride: Option<u32>,
    pub pixel_format: Option<&'static str>,
}

#[derive(Debug)]
pub struct RenderOutcome {
    pub input: PathBuf,
    pub compile_duration: Duration,
    pub export_duration: Duration,
    pub diagnostics: Vec<EngineDiagnostic>,
    pub artifacts: Vec<Artifact>,
}

#[derive(Debug, Clone, Serialize)]
pub struct EngineDiagnostic {
    pub severity: &'static str,
    pub message: String,
    pub file: Option<String>,
    pub line: Option<u32>,
    pub column: Option<u32>,
    pub hints: Vec<String>,
}

#[derive(Debug)]
pub struct CompilationFailure {
    pub diagnostics: Vec<EngineDiagnostic>,
}

impl std::fmt::Display for CompilationFailure {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let message = self
            .diagnostics
            .first()
            .map(|diagnostic| diagnostic.message.as_str())
            .unwrap_or("Typst compilation failed");
        write!(formatter, "{message}")
    }
}

impl std::error::Error for CompilationFailure {}

pub struct Renderer {
    engine: TypstEngine<TypstTemplateCollection>,
    memory_projects: MemoryProjectResolver,
    initialization_duration: Duration,
}

impl Renderer {
    pub fn new(config: EngineConfig) -> Result<Self> {
        let initialization_started = Instant::now();
        let configured_root = config
            .restrict_to_directory
            .as_ref()
            .unwrap_or(&config.root);
        let root = configured_root
            .canonicalize()
            .with_context(|| format!("failed to resolve root {}", configured_root.display()))?;

        for path in &config.font_paths {
            if !path.exists() {
                bail!("font path does not exist: {}", path.display());
            }
        }
        for (index, bytes) in config.memory_fonts.iter().enumerate() {
            if typst::text::Font::iter(typst::foundations::Bytes::new(bytes.clone()))
                .next()
                .is_none()
            {
                bail!("memory font {index} is not a supported font");
            }
        }
        let font_options = TypstKitFontOptions::new()
            .include_system_fonts(config.system_fonts)
            .include_embedded_fonts(true)
            .include_dirs(config.font_paths);

        // Do not wrap the project resolver in typst-as-lib's source cache: daemon
        // jobs must observe file changes. Typst/comemo still caches unchanged work.
        let memory_projects = MemoryProjectResolver::default();
        let mut fonts: Vec<&[u8]> = vec![NOTO_SANS_KR_REGULAR];
        fonts.extend(config.memory_fonts.iter().map(Vec::as_slice));
        let mut builder = TypstEngine::builder()
            .fonts(fonts)
            .search_fonts_with(font_options)
            .add_file_resolver(memory_projects.clone())
            .add_file_resolver(EmbeddedPackageResolver::new())
            .add_file_resolver(FileSystemResolver::new(root.clone()));
        match config.package_resolution {
            PackageResolution::CacheThenNetwork => {
                builder = builder.with_package_file_resolver();
            }
            PackageResolution::CacheOnly => {
                builder = builder.add_file_resolver(CacheOnlyPackageResolver::new());
            }
            PackageResolution::EmbeddedOnly => {}
        }
        builder.comemo_evict_max_age(Some(config.cache_generations));

        let engine = builder.build();
        Ok(Self {
            engine,
            memory_projects,
            initialization_duration: initialization_started.elapsed(),
        })
    }

    pub fn initialization_duration(&self) -> Duration {
        self.initialization_duration
    }

    pub fn render_project(
        &self,
        main_path: &str,
        files: &[(String, Vec<u8>, bool)],
        formats: &[OutputFormat],
        ppi: f32,
        inputs: Dict,
    ) -> Result<RenderOutcome> {
        validate_project_path(main_path)?;
        if !files
            .iter()
            .any(|(path, _, is_text)| path == main_path && *is_text)
        {
            bail!("main Typst file is missing from the project: {main_path}");
        }
        let guard = self.memory_projects.register_project(files)?;
        let input = PathBuf::from(main_path);
        let stem = file_stem(&input)?;
        let source_id = FileId::new(None, VirtualPath::new(main_path));
        let result = self.compile_and_export(source_id, input, stem, formats, ppi, Some(inputs));
        drop(guard);
        result
    }

    fn compile_and_export(
        &self,
        source_id: FileId,
        input: PathBuf,
        stem: String,
        formats: &[OutputFormat],
        ppi: f32,
        inputs: Option<Dict>,
    ) -> Result<RenderOutcome> {
        if !ppi.is_finite() || ppi <= 0.0 {
            bail!("ppi must be a positive number");
        }

        let compile_started = Instant::now();
        let warned = match inputs {
            Some(inputs) => self
                .engine
                .compile_with_input::<_, _, PagedDocument>(source_id, inputs),
            None => self.engine.compile::<_, PagedDocument>(source_id),
        };
        let compile_duration = compile_started.elapsed();
        let diagnostics = warned
            .warnings
            .iter()
            .map(|diagnostic| self.map_diagnostic(diagnostic))
            .collect();
        let document = match warned.output {
            Ok(document) => document,
            Err(TypstAsLibError::TypstSource(diagnostics)) => {
                return Err(anyhow::Error::new(CompilationFailure {
                    diagnostics: diagnostics
                        .iter()
                        .map(|diagnostic| self.map_diagnostic(diagnostic))
                        .collect(),
                }));
            }
            Err(error) => return Err(anyhow::anyhow!(error)),
        };
        if document.pages.is_empty() {
            bail!("Typst produced no pages");
        }

        let export_started = Instant::now();
        let many_pages = document.pages.len() > 1;
        let page_format_count = formats
            .iter()
            .filter(|format| **format != OutputFormat::Pdf)
            .count();
        let mut artifacts = Vec::with_capacity(
            document.pages.len() * page_format_count
                + usize::from(formats.contains(&OutputFormat::Pdf)),
        );
        for (index, page) in document.pages.iter().enumerate() {
            let page_number = index + 1;
            for format in formats {
                let extension = match format {
                    OutputFormat::Svg => "svg",
                    OutputFormat::Png => "png",
                    OutputFormat::Rgba => "rgba",
                    OutputFormat::Pdf => continue,
                };
                let file_name = if many_pages {
                    format!("{stem}-{page_number:03}.{extension}")
                } else {
                    format!("{stem}.{extension}")
                };
                let (bytes, width, height, stride, pixel_format) = match format {
                    OutputFormat::Svg => {
                        (typst_svg::svg(page).into_bytes(), None, None, None, None)
                    }
                    OutputFormat::Png => (
                        typst_render::render(page, ppi / 72.0)
                            .encode_png()
                            .context("failed to encode PNG")?,
                        None,
                        None,
                        None,
                        None,
                    ),
                    OutputFormat::Rgba => {
                        let pixmap = typst_render::render(page, ppi / 72.0);
                        let width = pixmap.width();
                        let height = pixmap.height();
                        let stride = width.checked_mul(4).context("RGBA stride overflow")?;
                        (
                            pixmap.take(),
                            Some(width),
                            Some(height),
                            Some(stride),
                            Some("rgba8_premultiplied"),
                        )
                    }
                    OutputFormat::Pdf => unreachable!("PDF is exported as one document"),
                };
                artifacts.push(Artifact {
                    format: *format,
                    page: Some(page_number),
                    file_name,
                    bytes,
                    width,
                    height,
                    stride,
                    pixel_format,
                });
            }
        }

        if formats.contains(&OutputFormat::Pdf) {
            let bytes = typst_pdf::pdf(&document, &typst_pdf::PdfOptions::default())
                .map_err(|error| anyhow::anyhow!("PDF export failed: {error:?}"))?;
            artifacts.push(Artifact {
                format: OutputFormat::Pdf,
                page: None,
                file_name: format!("{stem}.pdf"),
                bytes,
                width: None,
                height: None,
                stride: None,
                pixel_format: None,
            });
        }

        Ok(RenderOutcome {
            input,
            compile_duration,
            export_duration: export_started.elapsed(),
            diagnostics,
            artifacts,
        })
    }

    fn map_diagnostic(&self, diagnostic: &SourceDiagnostic) -> EngineDiagnostic {
        let id = diagnostic.span.id();
        let file = id.map(|id| id.vpath().as_rootless_path().to_string_lossy().into_owned());
        let position = id
            .and_then(|id| self.memory_projects.source(id))
            .and_then(|source| {
                source
                    .range(diagnostic.span)
                    .or_else(|| diagnostic.span.range())
                    .and_then(|range| source.lines().byte_to_line_column(range.start))
            });
        EngineDiagnostic {
            severity: match diagnostic.severity {
                Severity::Error => "error",
                Severity::Warning => "warning",
            },
            message: diagnostic.message.to_string(),
            file,
            line: position.map(|(line, _)| line.saturating_add(1) as u32),
            column: position.map(|(_, column)| column.saturating_add(1) as u32),
            hints: diagnostic.hints.iter().map(ToString::to_string).collect(),
        }
    }
}

fn file_stem(path: &Path) -> Result<String> {
    path.file_stem()
        .and_then(|value| value.to_str())
        .filter(|value| !value.is_empty())
        .map(str::to_owned)
        .context("input has no valid UTF-8 file stem")
}

pub fn millis(duration: Duration) -> f64 {
    duration.as_secs_f64() * 1000.0
}

#[cfg(test)]
mod tests {
    use super::*;

    fn renderer(mode: PackageResolution) -> Renderer {
        Renderer::new(EngineConfig {
            root: PathBuf::from(env!("CARGO_MANIFEST_DIR")),
            system_fonts: false,
            font_paths: Vec::new(),
            memory_fonts: Vec::new(),
            cache_generations: 10,
            package_resolution: mode,
            restrict_to_directory: None,
        })
        .unwrap()
    }

    #[test]
    fn project_renders_import_and_multiple_pages() {
        let files = vec![
            (
                "main.typ".to_owned(),
                b"#import \"parts.typ\": message\n#message\n#pagebreak()\nSecond".to_vec(),
                true,
            ),
            (
                "parts.typ".to_owned(),
                b"#let message = [First]".to_vec(),
                true,
            ),
        ];
        let result = renderer(PackageResolution::EmbeddedOnly)
            .render_project(
                "main.typ",
                &files,
                &[
                    OutputFormat::Svg,
                    OutputFormat::Png,
                    OutputFormat::Pdf,
                    OutputFormat::Rgba,
                ],
                96.0,
                Dict::new(),
            )
            .unwrap();
        assert_eq!(result.artifacts.len(), 7);
        assert!(
            result
                .artifacts
                .iter()
                .any(|item| item.bytes.starts_with(b"%PDF-"))
        );
        assert!(
            result
                .artifacts
                .iter()
                .any(|item| item.pixel_format.is_some())
        );
    }

    #[test]
    fn embedded_package_is_available_in_every_mode() {
        let source = br#"#import "@preview/cetz:0.5.2": canvas, draw
#set page(width: 40pt, height: 40pt, margin: 0pt)
#canvas({ draw.line((0, 0), (1, 1)) })"#;
        for mode in [
            PackageResolution::CacheThenNetwork,
            PackageResolution::CacheOnly,
            PackageResolution::EmbeddedOnly,
        ] {
            renderer(mode)
                .render_project(
                    "main.typ",
                    &[("main.typ".to_owned(), source.to_vec(), true)],
                    &[OutputFormat::Pdf],
                    96.0,
                    Dict::new(),
                )
                .unwrap();
        }
    }
}
