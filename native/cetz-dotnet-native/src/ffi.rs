#![allow(unused_unsafe)]

use std::ffi::{CStr, CString, c_char};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::PathBuf;
use std::ptr;
use std::slice;
use std::sync::Mutex;
use std::time::Instant;

use serde::{Deserialize, Serialize};
use typst::foundations::Dict;

use crate::engine::{
    Artifact, CompilationFailure, EngineConfig, EngineDiagnostic, OutputFormat, PackageResolution,
    RenderOutcome, Renderer, TYPST_VERSION, millis,
};
use crate::memory_source::validate_project_path;

const ABI_VERSION: u32 = 1;
const OK: i32 = 0;
const INVALID_ARGUMENT: i32 = 1;
const CONFIGURATION_ERROR: i32 = 2;
const COMPILATION_ERROR: i32 = 3;
const EXPORT_ERROR: i32 = 4;
const PANIC_CONTAINED: i32 = 5;

pub struct BuilderHandle {
    config: NativeConfig,
    fonts: Vec<Vec<u8>>,
}

pub struct RendererHandle {
    renderer: Mutex<Renderer>,
}

pub struct ProjectHandle {
    main_path: String,
    files: Vec<(String, Vec<u8>, bool)>,
}

pub struct ResultHandle {
    artifacts: Vec<Artifact>,
}

#[derive(Debug, Deserialize)]
struct NativeConfig {
    #[serde(default = "default_base_directory")]
    base_directory: PathBuf,
    #[serde(default)]
    system_fonts: bool,
    #[serde(default)]
    font_paths: Vec<PathBuf>,
    #[serde(default = "default_cache_generations")]
    cache_generations: usize,
    #[serde(default)]
    package_resolution: NativePackageResolution,
    #[serde(default)]
    restrict_to_directory: Option<PathBuf>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "snake_case")]
enum NativePackageResolution {
    #[default]
    CacheThenNetwork,
    CacheOnly,
    EmbeddedOnly,
}

#[derive(Debug, Deserialize)]
struct RenderRequest {
    #[serde(default = "default_formats")]
    formats: Vec<String>,
    #[serde(default = "default_ppi")]
    ppi: f32,
    #[serde(default)]
    inputs: Dict,
}

#[derive(Serialize)]
struct Response<'a> {
    typst_version: &'static str,
    input: &'a std::path::Path,
    engine_init_ms: f64,
    compile_ms: f64,
    export_ms: f64,
    total_ms: f64,
    diagnostics: Vec<EngineDiagnostic>,
    outputs: Vec<ArtifactInfo<'a>>,
}

#[derive(Serialize)]
struct ArtifactInfo<'a> {
    format: OutputFormat,
    page: Option<usize>,
    file_name: &'a str,
    bytes: usize,
    width: Option<u32>,
    height: Option<u32>,
    stride: Option<u32>,
    pixel_format: Option<&'static str>,
}

struct Failure {
    status: i32,
    kind: &'static str,
    message: String,
    diagnostics: Vec<EngineDiagnostic>,
}

impl Failure {
    fn invalid(message: impl Into<String>) -> Self {
        Self {
            status: INVALID_ARGUMENT,
            kind: "invalid_argument",
            message: message.into(),
            diagnostics: Vec::new(),
        }
    }

    fn config(message: impl Into<String>) -> Self {
        Self {
            status: CONFIGURATION_ERROR,
            kind: "configuration",
            message: message.into(),
            diagnostics: Vec::new(),
        }
    }

    fn compile(message: impl Into<String>) -> Self {
        let message = message.into();
        Self {
            status: COMPILATION_ERROR,
            kind: "compilation",
            diagnostics: vec![EngineDiagnostic {
                severity: "error",
                message: message.clone(),
                file: None,
                line: None,
                column: None,
                hints: Vec::new(),
            }],
            message,
        }
    }

    fn compilation(diagnostics: Vec<EngineDiagnostic>) -> Self {
        let message = diagnostics
            .first()
            .map(|diagnostic| diagnostic.message.clone())
            .unwrap_or_else(|| "Typst compilation failed".to_owned());
        Self {
            status: COMPILATION_ERROR,
            kind: "compilation",
            message,
            diagnostics,
        }
    }

    fn export(message: impl Into<String>) -> Self {
        Self {
            status: EXPORT_ERROR,
            kind: "export",
            message: message.into(),
            diagnostics: Vec::new(),
        }
    }
}

fn default_base_directory() -> PathBuf {
    std::env::current_dir().unwrap_or_else(|_| PathBuf::from("."))
}

fn default_cache_generations() -> usize {
    10
}
fn default_ppi() -> f32 {
    144.0
}
fn default_formats() -> Vec<String> {
    vec!["pdf".to_owned()]
}

fn package_mode(value: NativePackageResolution) -> PackageResolution {
    match value {
        NativePackageResolution::CacheThenNetwork => PackageResolution::CacheThenNetwork,
        NativePackageResolution::CacheOnly => PackageResolution::CacheOnly,
        NativePackageResolution::EmbeddedOnly => PackageResolution::EmbeddedOnly,
    }
}

fn parse_formats(values: &[String]) -> Result<Vec<OutputFormat>, Failure> {
    let mut formats = Vec::new();
    for value in values {
        let requested: &[OutputFormat] = match value.as_str() {
            "svg" => &[OutputFormat::Svg],
            "png" => &[OutputFormat::Png],
            "pdf" => &[OutputFormat::Pdf],
            "rgba" => &[OutputFormat::Rgba],
            "all" => &[OutputFormat::Svg, OutputFormat::Png, OutputFormat::Pdf],
            other => {
                return Err(Failure::invalid(format!(
                    "unsupported output format: {other}"
                )));
            }
        };
        for format in requested {
            if !formats.contains(format) {
                formats.push(*format);
            }
        }
    }
    if formats.is_empty() {
        return Err(Failure::invalid("at least one output format is required"));
    }
    Ok(formats)
}

fn json_response(
    renderer: &Renderer,
    outcome: &RenderOutcome,
    total: Instant,
) -> Result<String, Failure> {
    let outputs = outcome
        .artifacts
        .iter()
        .map(|artifact| ArtifactInfo {
            format: artifact.format,
            page: artifact.page,
            file_name: &artifact.file_name,
            bytes: artifact.bytes.len(),
            width: artifact.width,
            height: artifact.height,
            stride: artifact.stride,
            pixel_format: artifact.pixel_format,
        })
        .collect();
    serde_json::to_string(&Response {
        typst_version: TYPST_VERSION,
        input: &outcome.input,
        engine_init_ms: millis(renderer.initialization_duration()),
        compile_ms: millis(outcome.compile_duration),
        export_ms: millis(outcome.export_duration),
        total_ms: millis(total.elapsed()),
        diagnostics: outcome.diagnostics.clone(),
        outputs,
    })
    .map_err(|error| Failure::compile(format!("failed to encode result: {error}")))
}

fn owned_string(value: impl Into<String>) -> *mut c_char {
    CString::new(value.into().replace('\0', "\\0"))
        .unwrap()
        .into_raw()
}

unsafe fn read_string<'a>(value: *const c_char, name: &str) -> Result<&'a str, Failure> {
    if value.is_null() {
        return Err(Failure::invalid(format!("{name} must not be null")));
    }
    unsafe { CStr::from_ptr(value) }
        .to_str()
        .map_err(|_| Failure::invalid(format!("{name} must be valid UTF-8")))
}

unsafe fn read_bytes<'a>(data: *const u8, len: usize, name: &str) -> Result<&'a [u8], Failure> {
    if len > 0 && data.is_null() {
        return Err(Failure::invalid(format!(
            "{name} must not be null when length is non-zero"
        )));
    }
    Ok(if len == 0 {
        &[]
    } else {
        unsafe { slice::from_raw_parts(data, len) }
    })
}

unsafe fn clear<T>(output: *mut *mut T) {
    if !output.is_null() {
        unsafe { ptr::write(output, ptr::null_mut()) };
    }
}

unsafe fn boundary(
    error_out: *mut *mut c_char,
    action: impl FnOnce() -> Result<(), Failure>,
) -> i32 {
    if !error_out.is_null() {
        unsafe { ptr::write(error_out, ptr::null_mut()) };
    }
    match catch_unwind(AssertUnwindSafe(action)) {
        Ok(Ok(())) => OK,
        Ok(Err(error)) => {
            if !error_out.is_null() {
                let body = serde_json::json!({
                    "kind": error.kind,
                    "message": error.message,
                    "diagnostics": error.diagnostics
                })
                .to_string();
                unsafe { ptr::write(error_out, owned_string(body)) };
            }
            error.status
        }
        Err(_) => {
            if !error_out.is_null() {
                unsafe {
                    ptr::write(
                        error_out,
                        owned_string(r#"{"kind":"panic","message":"Rust panic was contained"}"#),
                    )
                };
            }
            PANIC_CONTAINED
        }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cetz_dotnet_abi_version() -> u32 {
    ABI_VERSION
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_builder_create(
    config_json: *const c_char,
    output: *mut *mut BuilderHandle,
    error: *mut *mut c_char,
) -> i32 {
    unsafe { clear(output) };
    unsafe {
        boundary(error, || {
            if output.is_null() {
                return Err(Failure::invalid("builder output must not be null"));
            }
            let json = unsafe { read_string(config_json, "config_json")? };
            let config = serde_json::from_str(json)
                .map_err(|e| Failure::invalid(format!("invalid configuration JSON: {e}")))?;
            unsafe {
                ptr::write(
                    output,
                    Box::into_raw(Box::new(BuilderHandle {
                        config,
                        fonts: Vec::new(),
                    })),
                )
            };
            Ok(())
        })
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_builder_add_font(
    handle: *mut BuilderHandle,
    data: *const u8,
    len: usize,
    error: *mut *mut c_char,
) -> i32 {
    unsafe {
        boundary(error, || {
            let builder = unsafe { handle.as_mut() }
                .ok_or_else(|| Failure::invalid("builder handle must not be null"))?;
            builder
                .fonts
                .push(unsafe { read_bytes(data, len, "font data")? }.to_vec());
            Ok(())
        })
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_builder_build(
    handle: *mut BuilderHandle,
    output: *mut *mut RendererHandle,
    error: *mut *mut c_char,
) -> i32 {
    unsafe { clear(output) };
    unsafe {
        boundary(error, || {
            if output.is_null() {
                return Err(Failure::invalid("renderer output must not be null"));
            }
            let builder = unsafe { handle.as_mut() }
                .ok_or_else(|| Failure::invalid("builder handle must not be null"))?;
            let config = std::mem::replace(
                &mut builder.config,
                NativeConfig {
                    base_directory: default_base_directory(),
                    system_fonts: false,
                    font_paths: Vec::new(),
                    cache_generations: 10,
                    package_resolution: NativePackageResolution::EmbeddedOnly,
                    restrict_to_directory: None,
                },
            );
            let renderer = Renderer::new(EngineConfig {
                root: config.base_directory,
                system_fonts: config.system_fonts,
                font_paths: config.font_paths,
                memory_fonts: std::mem::take(&mut builder.fonts),
                cache_generations: config.cache_generations,
                package_resolution: package_mode(config.package_resolution),
                restrict_to_directory: config.restrict_to_directory,
            })
            .map_err(|e| Failure::config(e.to_string()))?;
            unsafe {
                ptr::write(
                    output,
                    Box::into_raw(Box::new(RendererHandle {
                        renderer: Mutex::new(renderer),
                    })),
                )
            };
            Ok(())
        })
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_builder_destroy(handle: *mut BuilderHandle) {
    if !handle.is_null() {
        drop(unsafe { Box::from_raw(handle) });
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_project_create(
    main_path: *const c_char,
    output: *mut *mut ProjectHandle,
    error: *mut *mut c_char,
) -> i32 {
    unsafe { clear(output) };
    unsafe {
        boundary(error, || {
            if output.is_null() {
                return Err(Failure::invalid("project output must not be null"));
            }
            let main_path = unsafe { read_string(main_path, "main_path")? }.to_owned();
            validate_project_path(&main_path).map_err(|e| Failure::invalid(e.to_string()))?;
            unsafe {
                ptr::write(
                    output,
                    Box::into_raw(Box::new(ProjectHandle {
                        main_path,
                        files: Vec::new(),
                    })),
                )
            };
            Ok(())
        })
    }
}

unsafe fn add_project_file(
    handle: *mut ProjectHandle,
    path: *const c_char,
    data: *const u8,
    len: usize,
    is_text: bool,
    error: *mut *mut c_char,
) -> i32 {
    unsafe {
        boundary(error, || {
            let project = unsafe { handle.as_mut() }
                .ok_or_else(|| Failure::invalid("project handle must not be null"))?;
            let path = unsafe { read_string(path, "path")? }.to_owned();
            validate_project_path(&path).map_err(|e| Failure::invalid(e.to_string()))?;
            if project
                .files
                .iter()
                .any(|(existing, _, _)| existing == &path)
            {
                return Err(Failure::invalid(format!("duplicate project path: {path}")));
            }
            let bytes = unsafe { read_bytes(data, len, "file data")? }.to_vec();
            if is_text && std::str::from_utf8(&bytes).is_err() {
                return Err(Failure::invalid(format!(
                    "text file is not valid UTF-8: {path}"
                )));
            }
            project.files.push((path, bytes, is_text));
            Ok(())
        })
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_project_add_text(
    handle: *mut ProjectHandle,
    path: *const c_char,
    data: *const u8,
    len: usize,
    error: *mut *mut c_char,
) -> i32 {
    unsafe { add_project_file(handle, path, data, len, true, error) }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_project_add_binary(
    handle: *mut ProjectHandle,
    path: *const c_char,
    data: *const u8,
    len: usize,
    error: *mut *mut c_char,
) -> i32 {
    unsafe { add_project_file(handle, path, data, len, false, error) }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_project_destroy(handle: *mut ProjectHandle) {
    if !handle.is_null() {
        drop(unsafe { Box::from_raw(handle) });
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_render_project(
    renderer: *mut RendererHandle,
    project: *const ProjectHandle,
    request_json: *const c_char,
    result_out: *mut *mut ResultHandle,
    json_out: *mut *mut c_char,
    error: *mut *mut c_char,
) -> i32 {
    unsafe {
        clear(result_out);
        clear(json_out);
    }
    unsafe {
        boundary(error, || {
            if result_out.is_null() || json_out.is_null() {
                return Err(Failure::invalid("result outputs must not be null"));
            }
            let renderer = unsafe { renderer.as_ref() }
                .ok_or_else(|| Failure::invalid("renderer handle must not be null"))?;
            let project = unsafe { project.as_ref() }
                .ok_or_else(|| Failure::invalid("project handle must not be null"))?;
            let request: RenderRequest =
                serde_json::from_str(unsafe { read_string(request_json, "request_json")? })
                    .map_err(|e| Failure::invalid(format!("invalid render JSON: {e}")))?;
            let formats = parse_formats(&request.formats)?;
            let total = Instant::now();
            let renderer = renderer
                .renderer
                .lock()
                .map_err(|_| Failure::compile("renderer lock is poisoned"))?;
            let outcome = renderer
                .render_project(
                    &project.main_path,
                    &project.files,
                    &formats,
                    request.ppi,
                    request.inputs,
                )
                .map_err(|error| {
                    let message = error.to_string();
                    if let Some(compilation) = error.downcast_ref::<CompilationFailure>() {
                        Failure::compilation(compilation.diagnostics.clone())
                    } else if message.contains("export failed")
                        || message.contains("failed to encode PNG")
                        || message.contains("RGBA stride overflow")
                    {
                        Failure::export(message)
                    } else {
                        Failure::compile(message)
                    }
                })?;
            let json = json_response(&renderer, &outcome, total)?;
            unsafe {
                ptr::write(
                    result_out,
                    Box::into_raw(Box::new(ResultHandle {
                        artifacts: outcome.artifacts,
                    })),
                );
                ptr::write(json_out, owned_string(json));
            }
            Ok(())
        })
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_result_count(handle: *const ResultHandle) -> usize {
    unsafe { handle.as_ref() }.map_or(0, |result| result.artifacts.len())
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_result_get(
    handle: *const ResultHandle,
    index: usize,
    data: *mut *const u8,
    len: *mut usize,
) -> i32 {
    if handle.is_null() || data.is_null() || len.is_null() {
        return INVALID_ARGUMENT;
    }
    let Some(artifact) = unsafe { &*handle }.artifacts.get(index) else {
        return INVALID_ARGUMENT;
    };
    unsafe {
        ptr::write(data, artifact.bytes.as_ptr());
        ptr::write(len, artifact.bytes.len());
    }
    OK
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_result_destroy(handle: *mut ResultHandle) {
    if !handle.is_null() {
        drop(unsafe { Box::from_raw(handle) });
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_renderer_destroy(handle: *mut RendererHandle) {
    if !handle.is_null() {
        drop(unsafe { Box::from_raw(handle) });
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_string_free(value: *mut c_char) {
    if !value.is_null() {
        drop(unsafe { CString::from_raw(value) });
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn cetz_dotnet_test_panic(error: *mut *mut c_char) -> i32 {
    unsafe {
        boundary(error, || -> Result<(), Failure> {
            panic!("ffi test panic")
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn abi_and_panic_boundary_are_stable() {
        assert_eq!(cetz_dotnet_abi_version(), 1);
        let mut error = ptr::null_mut();
        let status = unsafe { cetz_dotnet_test_panic(&mut error) };
        assert_eq!(status, PANIC_CONTAINED);
        assert!(!error.is_null());
        unsafe { cetz_dotnet_string_free(error) };
    }

    #[test]
    fn null_builder_output_is_rejected() {
        let config = CString::new("{}").unwrap();
        let mut error = ptr::null_mut();
        let status =
            unsafe { cetz_dotnet_builder_create(config.as_ptr(), ptr::null_mut(), &mut error) };
        assert_eq!(status, INVALID_ARGUMENT);
        unsafe { cetz_dotnet_string_free(error) };
    }
}
