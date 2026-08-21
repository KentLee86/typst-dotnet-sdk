//! PoC benchmark: is precompiling static demo documents to SVG at build time and
//! rasterizing that vector at runtime cheaper than re-running Typst on every zoom step?
//!
//! Phases, per document:
//!   A  current runtime path      -- Typst compile + RGBA export over the whole
//!                                   144/192/288/384/576/768 PPI quality ladder
//!   B  proposed build-time step  -- Typst compile + SVG export, one file per page
//!   C  proposed runtime path     -- usvg parse (cacheable) + resvg raster at 1x/2x/4x/8x
//!   D  "instant bitmap" baseline -- memcpy a 144 PPI page + bilinear upscale to target
//!
//! Nothing here ships. resvg/usvg/tiny-skia stay in this crate so the cdylib is untouched.

mod embedded;
mod imageops;

use std::fs;
use std::io::Write as _;
use std::path::{Path, PathBuf};
use std::time::Instant;

use anyhow::{Context, Result, bail};
use flate2::Compression;
use flate2::write::GzEncoder;
use imageops::{bilinear_resample_premultiplied, compare, flatten_premultiplied, write_png};
use typst::foundations::Dict;
use typst_dotnet_native::engine::{
    EngineConfig, OutputFormat, PackageResolution, Renderer, millis,
};

/// One warm-up iteration reported as `cold`, then six timed repeats.
const ITERATIONS: usize = 7;
/// The full automatic-quality ladder, copied from `AutomaticSteps` in
/// `src/Typst.Renderer.Core/TypstRasterQualityPolicy.cs`. 384 and 768 are what the
/// samples request at 4x and 8x zoom, so phase C must be compared against those rows.
const TIMED_PPI: [f32; 6] = [144.0, 192.0, 288.0, 384.0, 576.0, 768.0];
/// Per-iteration *relative* PPI offset for the jittered phase-A pass: iteration `i` asks
/// for `ppi * (1 + i * PPI_JITTER)`. Small enough that every page keeps the same pixel
/// dimensions (`typst_render` rounds, and the largest shift here is ~0.05 px; checked at
/// runtime), large enough to land on a different `f32` than the previous iteration and so
/// miss typst-render's comemo glyph and texture caches, which are keyed on the raster
/// scale. An *absolute* offset does not work: at 768 PPI one f32 ulp is 6.1e-5, so a
/// 1e-5 offset rounds straight back onto the previous key.
const PPI_JITTER: f32 = 1.0e-6;
/// Zoom factors relative to the 96 dpi CSS page size, with their equivalent Typst PPI.
const ZOOM_STEPS: [(f32, f32); 4] = [(1.0, 96.0), (2.0, 192.0), (4.0, 384.0), (8.0, 768.0)];
/// The pre-rendered page the "instant bitmap" baseline starts from.
const BASELINE_PPI: f32 = 144.0;
/// Full-page PNGs are only emitted at these zoom factors; higher factors go to crops.
const FULL_PAGE_PNG_ZOOMS: [f32; 2] = [1.0, 2.0];
const CROP_SIZE: u32 = 400;

struct CropSpec {
    name: &'static str,
    /// Normalized top-left corner of the crop inside the page.
    x: f32,
    y: f32,
}

struct DocSpec {
    key: &'static str,
    directory: &'static str,
    main: &'static str,
    text_files: &'static [&'static str],
    binary_files: &'static [&'static str],
    /// Zero-based page used for every quality comparison and PNG artifact.
    quality_page: usize,
    crops: &'static [CropSpec],
}

const DOCS: &[DocSpec] = &[
    DocSpec {
        key: "editor-export",
        directory: "editor-export",
        main: "editor-export.typ",
        text_files: &[],
        binary_files: &[],
        quality_page: 0,
        // The blue/red 1.5pt-2.5pt CeTZ connector strokes meeting the circle.
        crops: &[CropSpec {
            name: "cetz-thin-lines",
            x: 0.49,
            y: 0.17,
        }],
    },
    DocSpec {
        key: "serial-protocol",
        directory: "serial-protocol",
        main: "serial-protocol.typ",
        text_files: &["protocol-components.typ"],
        binary_files: &[],
        quality_page: 0,
        crops: &[
            // 11pt justified Korean body copy.
            CropSpec {
                name: "korean-text",
                x: 0.11,
                y: 0.30,
            },
            // Hairline rules of the field table.
            CropSpec {
                name: "thin-lines",
                x: 0.10,
                y: 0.74,
            },
        ],
    },
    DocSpec {
        key: "korean-business-forms",
        directory: "korean-business-forms",
        main: "korean-business-forms.typ",
        text_files: &[],
        binary_files: &["company-logo-v2-imagegen.svg", "company-seal-imagegen.svg"],
        quality_page: 0,
        crops: &[
            // 7.4pt Korean item-row text.
            CropSpec {
                name: "korean-text",
                x: 0.138,
                y: 0.381,
            },
            // company-seal-imagegen.svg, embedded as a data URI by the SVG export.
            CropSpec {
                name: "embedded-svg-seal",
                x: 0.673,
                y: 0.168,
            },
            // company-logo-v2-imagegen.svg, same path.
            CropSpec {
                name: "embedded-svg-logo",
                x: 0.055,
                y: 0.030,
            },
        ],
    },
];

fn main() -> Result<()> {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    if arguments.first().map(String::as_str) == Some("probe") {
        return probe_svg_files(&arguments[1..]);
    }
    if arguments.first().map(String::as_str) == Some("imgprobe") {
        return probe_image_mechanism(&arguments[1..]);
    }
    if arguments.first().map(String::as_str) == Some("emb") {
        return embedded::run(&arguments[1..]);
    }
    let root = repo_root()?;
    let artifacts = root.join("artifacts").join("poc");
    fs::create_dir_all(&artifacts)?;

    println!("# poc-vector-bench");
    println!("iterations per measurement: {ITERATIONS} (first reported as cold)");
    println!("artifacts: {}", artifacts.display());

    let font_directory = root
        .join("native")
        .join("typst-dotnet-native")
        .join("assets")
        .join("fonts");
    let fontdb_started = Instant::now();
    let mut svg_options = usvg::Options::default();
    svg_options.fontdb_mut().load_fonts_dir(&font_directory);
    let fontdb_face_count = svg_options.fontdb.len();
    println!(
        "\nusvg fontdb: loaded {fontdb_face_count} face(s) from {} in {:.2} ms",
        font_directory.display(),
        millis(fontdb_started.elapsed())
    );

    let mut failures = Vec::new();
    for spec in DOCS {
        println!("\n\n================ {} ================", spec.key);
        match run_document(spec, &root, &artifacts, &svg_options) {
            Ok(()) => {}
            Err(error) => {
                println!("!! FAILED: {error:#}");
                failures.push((spec.key, format!("{error:#}")));
            }
        }
    }

    if failures.is_empty() {
        println!("\n\nall documents completed");
    } else {
        println!("\n\n{} document(s) failed:", failures.len());
        for (key, error) in &failures {
            println!("  {key}: {error}");
        }
    }
    Ok(())
}

/// `poc-vector-bench probe <scale> <file.svg>...` -- attribute raster cost by
/// rendering hand-edited variants of an exported page at a fixed zoom factor.
fn probe_svg_files(arguments: &[String]) -> Result<()> {
    let (scale, paths) = match arguments.split_first() {
        Some((scale, rest)) if !rest.is_empty() => (scale.parse::<f32>()?, rest),
        _ => bail!("usage: poc-vector-bench probe <scale> <file.svg>..."),
    };
    let options = usvg::Options::default();
    println!("file | parse med ms | raster med ms | px | Mpx/s");
    for path in paths {
        let bytes = fs::read(path).with_context(|| format!("failed to read {path}"))?;
        let tree = usvg::Tree::from_data(&bytes, &options)?;
        let width = (tree.size().width() * scale).round() as u32;
        let height = (tree.size().height() * scale).round() as u32;
        let mut parse_samples = Vec::new();
        let mut raster_samples = Vec::new();
        for _ in 0..ITERATIONS {
            let started = Instant::now();
            let parsed = usvg::Tree::from_data(&bytes, &options)?;
            parse_samples.push(millis(started.elapsed()));
            std::hint::black_box(parsed.size().width());
            let started = Instant::now();
            let mut pixmap =
                tiny_skia::Pixmap::new(width, height).context("pixmap alloc failed")?;
            resvg::render(
                &tree,
                tiny_skia::Transform::from_scale(scale, scale),
                &mut pixmap.as_mut(),
            );
            raster_samples.push(millis(started.elapsed()));
            std::hint::black_box(pixmap.data()[0]);
        }
        let raster_median = stats(&raster_samples).2;
        let megapixels = (width as f64) * (height as f64) / 1.0e6;
        println!(
            "{path} | {:.2} | {raster_median:.2} | {width}x{height} | {:.1}",
            stats(&parse_samples).2,
            megapixels / (raster_median / 1000.0)
        );
    }
    Ok(())
}

/// `poc-vector-bench imgprobe <scale> <page.svg>...` -- separate the cost of an embedded
/// SVG's *artwork* from the cost of resvg's nested-SVG code path.
///
/// resvg 0.45.1 `src/image.rs::render_vector` allocates a pixmap the size of the **whole
/// destination canvas** for every `ImageKind::SVG`, renders into it and composites it back.
/// Rewriting the same artwork as an inline nested `<svg>` element keeps the drawing
/// identical but routes it through `render_group`, which allocates only a bbox-sized layer
/// (`src/render.rs:103`). The difference between those two variants is the penalty; the
/// difference between the inlined and stripped variants is the artwork itself.
fn probe_image_mechanism(arguments: &[String]) -> Result<()> {
    let (scale, paths) = match arguments.split_first() {
        Some((scale, rest)) if !rest.is_empty() => (scale.parse::<f32>()?, rest),
        _ => bail!("usage: poc-vector-bench imgprobe <scale> <page.svg>..."),
    };
    let options = usvg::Options::default();
    println!(
        "variant | images | raster med ms | px | Mpx/s | psnr dB vs as-exported | % px delta>8 vs as-exported"
    );
    for path in paths {
        let source = fs::read_to_string(path).with_context(|| format!("failed to read {path}"))?;
        let (inlined, stripped, image_count) = rewrite_images(&source)?;
        let stem = Path::new(path).with_extension("");
        let stem = stem.to_string_lossy().into_owned();
        fs::write(format!("{stem}-images-inlined.svg"), &inlined)?;
        fs::write(format!("{stem}-images-stripped.svg"), &stripped)?;
        let mut reference = None;
        for (label, svg) in [
            ("as-exported", source.as_str()),
            ("inlined <svg>", inlined.as_str()),
            ("stripped", stripped.as_str()),
        ] {
            let tree = usvg::Tree::from_data(svg.as_bytes(), &options)?;
            let width = (tree.size().width() * scale).round() as u32;
            let height = (tree.size().height() * scale).round() as u32;
            let mut samples = Vec::new();
            let mut rendered = None;
            for _ in 0..ITERATIONS {
                let started = Instant::now();
                let mut pixmap =
                    tiny_skia::Pixmap::new(width, height).context("pixmap alloc failed")?;
                resvg::render(
                    &tree,
                    tiny_skia::Transform::from_scale(scale, scale),
                    &mut pixmap.as_mut(),
                );
                samples.push(millis(started.elapsed()));
                std::hint::black_box(pixmap.data()[0]);
                rendered = Some(pixmap);
            }
            let image = flatten_premultiplied(rendered.unwrap().data(), width, height);
            let against = match &reference {
                None => {
                    reference = Some(image);
                    "-- (reference)".to_owned()
                }
                Some(reference) => {
                    let metrics = compare(reference, &image);
                    format!("{:.2} | {:.3}%", metrics.psnr_db, metrics.percent_over_8)
                }
            };
            let median = stats(&samples).2;
            let megapixels = (width as f64) * (height as f64) / 1.0e6;
            println!(
                "{path} {label} | {} | {median:.2} | {width}x{height} | {:.1} | {against}",
                if label == "stripped" { 0 } else { image_count },
                megapixels / (median / 1000.0)
            );
        }
    }
    Ok(())
}

/// `(images inlined as nested <svg>, images removed, count)`.
fn rewrite_images(svg: &str) -> Result<(String, String, usize)> {
    let mut inlined = String::with_capacity(svg.len());
    let mut stripped = String::with_capacity(svg.len());
    let mut rest = svg;
    let mut count = 0usize;
    while let Some(start) = rest.find("<image") {
        let end = rest[start..]
            .find('>')
            .map(|offset| start + offset + 1)
            .context("unterminated <image> element")?;
        let tag = &rest[start..end];
        if !tag.ends_with("/>") {
            bail!("only self-closing <image .../> elements are handled, found: {tag}");
        }
        inlined.push_str(&rest[..start]);
        stripped.push_str(&rest[..start]);

        let href = attribute(tag, "xlink:href").context("<image> without xlink:href")?;
        let payload = href
            .strip_prefix("data:image/svg+xml;base64,")
            .context("<image> href is not a base64 SVG data URI")?;
        let inner = String::from_utf8(base64_decode(payload)?)?;
        let width = attribute(tag, "width").context("<image> without width")?;
        let height = attribute(tag, "height").context("<image> without height")?;
        let nested = nest_svg(&inner, width, height)?;
        match attribute(tag, "transform") {
            Some(transform) => {
                inlined.push_str(&format!("<g transform=\"{transform}\">{nested}</g>"));
            }
            None => inlined.push_str(&nested),
        }

        rest = &rest[end..];
        count += 1;
    }
    inlined.push_str(rest);
    stripped.push_str(rest);
    Ok((inlined, stripped, count))
}

/// Re-root a standalone SVG document as a nested `<svg>` element of the given size,
/// keeping its own `viewBox` so the artwork is scaled exactly as the `<image>` scaled it.
fn nest_svg(document: &str, width: &str, height: &str) -> Result<String> {
    let start = document
        .find("<svg")
        .context("embedded asset has no <svg> root")?;
    let end = document[start..]
        .find('>')
        .map(|offset| start + offset)
        .context("unterminated <svg> root")?;
    let mut tag = document[start..end].to_owned();
    for name in ["width", "height", "preserveAspectRatio"] {
        while let Some(range) = attribute_range(&tag, name) {
            tag.replace_range(range, "");
        }
    }
    let tag = tag.trim_end().to_owned();
    Ok(format!(
        "{tag} width=\"{width}\" height=\"{height}\" preserveAspectRatio=\"none\"{}",
        &document[end..]
    ))
}

fn attribute<'a>(tag: &'a str, name: &str) -> Option<&'a str> {
    let range = attribute_range(tag, name)?;
    let value = &tag[range];
    let start = value.find('"')? + 1;
    let end = value.rfind('"')?;
    Some(&value[start..end])
}

/// Byte range of ` name="value"` inside `tag`, leading space included.
fn attribute_range(tag: &str, name: &str) -> Option<std::ops::Range<usize>> {
    let needle = format!(" {name}=\"");
    let start = tag.find(&needle)?;
    let end = tag[start + needle.len()..].find('"')? + start + needle.len() + 1;
    Some(start..end)
}

fn base64_decode(input: &str) -> Result<Vec<u8>> {
    let mut out = Vec::with_capacity(input.len() / 4 * 3);
    let mut accumulator = 0u32;
    let mut bits = 0u32;
    for byte in input.bytes() {
        let value = match byte {
            b'A'..=b'Z' => byte - b'A',
            b'a'..=b'z' => byte - b'a' + 26,
            b'0'..=b'9' => byte - b'0' + 52,
            b'+' => 62,
            b'/' => 63,
            b'=' | b'\n' | b'\r' | b' ' | b'\t' => continue,
            _ => bail!("invalid base64 byte {byte:#04x}"),
        };
        accumulator = (accumulator << 6) | u32::from(value);
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push((accumulator >> bits) as u8);
        }
    }
    Ok(out)
}

fn repo_root() -> Result<PathBuf> {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .canonicalize()
        .context("failed to resolve repository root")
}

/// `(relative path, bytes, is_text)`, the shape `Renderer::render_project` expects.
type ProjectFile = (String, Vec<u8>, bool);

fn load_project(spec: &DocSpec, root: &Path) -> Result<(PathBuf, Vec<ProjectFile>)> {
    let directory = root
        .join("samples")
        .join("Typst.Renderer.Demo.Shared")
        .join("Resources")
        .join(spec.directory);
    let mut files = Vec::new();
    for (name, is_text) in std::iter::once((spec.main, true))
        .chain(spec.text_files.iter().map(|name| (*name, true)))
        .chain(spec.binary_files.iter().map(|name| (*name, false)))
    {
        let path = directory.join(name);
        let bytes =
            fs::read(&path).with_context(|| format!("failed to read {}", path.display()))?;
        files.push((name.to_owned(), bytes, is_text));
    }
    Ok((directory, files))
}

fn make_renderer(directory: &Path) -> Result<Renderer> {
    Renderer::new(EngineConfig {
        root: directory.to_path_buf(),
        system_fonts: false,
        font_paths: Vec::new(),
        memory_fonts: Vec::new(),
        cache_generations: 10,
        // Strictest offline mode: only the embedded CeTZ assets are reachable.
        package_resolution: PackageResolution::EmbeddedOnly,
        restrict_to_directory: None,
    })
}

/// `(cold, min of the rest, median of the rest)` in milliseconds.
fn stats(values: &[f64]) -> (f64, f64, f64) {
    let cold = values[0];
    let mut rest = values[1..].to_vec();
    rest.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let min = rest.first().copied().unwrap_or(cold);
    let median = if rest.is_empty() {
        cold
    } else if rest.len() % 2 == 1 {
        rest[rest.len() / 2]
    } else {
        (rest[rest.len() / 2 - 1] + rest[rest.len() / 2]) / 2.0
    };
    (cold, min, median)
}

fn gzip_len(bytes: &[u8]) -> Result<usize> {
    let mut encoder = GzEncoder::new(Vec::new(), Compression::default());
    encoder.write_all(bytes)?;
    Ok(encoder.finish()?.len())
}

/// One phase-A sweep of the whole ladder, `ITERATIONS` times.
struct PhaseA {
    compile: Vec<Vec<f64>>,
    export: Vec<Vec<f64>>,
    wall: Vec<Vec<f64>>,
    megapixels: Vec<f64>,
    /// Whether the jitter left every page's pixel dimensions unchanged across iterations.
    dimensions_stable: bool,
}

impl PhaseA {
    fn empty() -> Self {
        PhaseA {
            compile: vec![Vec::new(); TIMED_PPI.len()],
            export: vec![Vec::new(); TIMED_PPI.len()],
            wall: vec![Vec::new(); TIMED_PPI.len()],
            megapixels: vec![0f64; TIMED_PPI.len()],
            dimensions_stable: true,
        }
    }
}

/// Measure both cache conditions, `(warm, jittered)`.
///
/// The two are interleaved rung by rung rather than run as two separate ladder sweeps,
/// because on this box the run-to-run drift over a whole sweep is the same order as the
/// effect being measured. Within one rung the warm samples all request the identical PPI
/// -- so from the second iteration on, typst-render's comemo glyph and texture caches hit
/// -- while the jittered samples move the PPI by `PPI_JITTER` per iteration and miss.
fn run_phase_a(
    renderer: &Renderer,
    spec: &DocSpec,
    files: &[ProjectFile],
) -> Result<(PhaseA, PhaseA)> {
    let mut warm = PhaseA::empty();
    let mut jittered = PhaseA::empty();
    let mut stable = true;
    for (index, ppi) in TIMED_PPI.iter().enumerate() {
        let mut expected: Option<Vec<(u32, u32)>> = None;
        for iteration in 0..ITERATIONS {
            for (phase, requested) in [
                (&mut warm, *ppi),
                (&mut jittered, ppi * (1.0 + PPI_JITTER * iteration as f32)),
            ] {
                let started = Instant::now();
                let outcome = renderer.render_project(
                    spec.main,
                    files,
                    &[OutputFormat::Rgba],
                    requested,
                    Dict::new(),
                )?;
                phase.wall[index].push(millis(started.elapsed()));
                phase.compile[index].push(millis(outcome.compile_duration));
                phase.export[index].push(millis(outcome.export_duration));
                let dimensions: Vec<(u32, u32)> = outcome
                    .artifacts
                    .iter()
                    .map(|artifact| (artifact.width.unwrap(), artifact.height.unwrap()))
                    .collect();
                phase.megapixels[index] = dimensions
                    .iter()
                    .map(|(width, height)| (*width as f64) * (*height as f64) / 1.0e6)
                    .sum();
                match &expected {
                    None => expected = Some(dimensions),
                    Some(first) if *first != dimensions => stable = false,
                    Some(_) => {}
                }
            }
        }
    }
    warm.dimensions_stable = stable;
    jittered.dimensions_stable = stable;
    Ok((warm, jittered))
}

fn print_phase_a(phase: &PhaseA) {
    println!(
        "ppi | compile cold/min/med ms | export cold/min/med ms | wall cold/min/med ms | doc Mpx | export Mpx/s | wall Mpx/s"
    );
    for (index, ppi) in TIMED_PPI.iter().enumerate() {
        let (cc, cm, cd) = stats(&phase.compile[index]);
        let (ec, em, ed) = stats(&phase.export[index]);
        let (wc, wm, wd) = stats(&phase.wall[index]);
        println!(
            "{ppi:>4} | {cc:9.2} {cm:9.2} {cd:9.2} | {ec:9.2} {em:9.2} {ed:9.2} | {wc:9.2} {wm:9.2} {wd:9.2} | {:7.2} | {:12.1} | {:10.1}",
            phase.megapixels[index],
            phase.megapixels[index] / (ed / 1000.0),
            phase.megapixels[index] / (wd / 1000.0)
        );
    }
    println!(
        "pixel dimensions identical across all iterations: {}",
        if phase.dimensions_stable { "yes" } else { "NO" }
    );
}

fn run_document(
    spec: &DocSpec,
    root: &Path,
    artifacts: &Path,
    svg_options: &usvg::Options,
) -> Result<()> {
    let (directory, files) = load_project(spec, root)?;
    let source_bytes: usize = files.iter().map(|(_, bytes, _)| bytes.len()).sum();
    let typ_bytes: usize = files
        .iter()
        .filter(|(_, _, is_text)| *is_text)
        .map(|(_, bytes, _)| bytes.len())
        .sum();

    // ---- phase 0: genuinely first touch of this document in this process -------------
    let engine_started = Instant::now();
    let cold_renderer = make_renderer(&directory)?;
    let engine_init = millis(engine_started.elapsed());
    let cold_started = Instant::now();
    let cold = cold_renderer
        .render_project(
            spec.main,
            &files,
            &[OutputFormat::Rgba],
            BASELINE_PPI,
            Dict::new(),
        )
        .with_context(|| format!("offline compile of {} failed", spec.main))?;
    let cold_wall = millis(cold_started.elapsed());
    let page_count = cold.artifacts.len();
    println!("pages: {page_count}");
    println!("source bytes: {source_bytes} total, {typ_bytes} in .typ files");
    println!(
        "phase 0 (fresh Renderer, first render @ {BASELINE_PPI} PPI): engine init {engine_init:.1} ms, \
compile {:.1} ms, export {:.1} ms, wall {cold_wall:.1} ms",
        millis(cold.compile_duration),
        millis(cold.export_duration)
    );
    for diagnostic in &cold.diagnostics {
        println!(
            "  diagnostic[{}]: {}",
            diagnostic.severity, diagnostic.message
        );
    }
    drop(cold);

    // ---- phase A: current runtime path, one renderer, zoom stepping -------------------
    // Run twice. The first pass revisits each ladder rung ITERATIONS times at exactly the
    // same PPI, so typst-render's comemo glyph/texture caches are warm from iteration 2 on
    // -- that is what the first version of this bench reported. The second pass jitters the
    // PPI so every iteration misses those caches, which is what a user stepping through the
    // ladder once actually pays.
    let renderer = make_renderer(&directory)?;
    let (warm, cold_cache) = run_phase_a(&renderer, spec, &files)?;
    println!(
        "\n## A(warm). current runtime path, same PPI every iteration (comemo raster caches warm)"
    );
    print_phase_a(&warm);
    println!(
        "\n## A. current runtime path, PPI jittered by {PPI_JITTER:e} relative per iteration (fresh raster cache keys)"
    );
    print_phase_a(&cold_cache);
    println!("\nwarm-cache overstatement of the current path (median wall ms)");
    println!("ppi | A(warm) | A(jittered) | delta | ratio");
    for (index, ppi) in TIMED_PPI.iter().enumerate() {
        let warm_median = stats(&warm.wall[index]).2;
        let jittered_median = stats(&cold_cache.wall[index]).2;
        println!(
            "{ppi:>4} | {warm_median:8.2} | {jittered_median:11.2} | {:8.2} | {:5.2}x",
            jittered_median - warm_median,
            jittered_median / warm_median
        );
    }
    let a_wall = &cold_cache.wall;
    let a_megapixels = &cold_cache.megapixels;

    // ---- phase B: build-time SVG export ----------------------------------------------
    let mut b_compile = Vec::new();
    let mut b_export = Vec::new();
    let mut b_wall = Vec::new();
    let mut page_svgs: Vec<Vec<u8>> = Vec::new();
    for iteration in 0..ITERATIONS {
        let started = Instant::now();
        let outcome =
            renderer.render_project(spec.main, &files, &[OutputFormat::Svg], 96.0, Dict::new())?;
        b_wall.push(millis(started.elapsed()));
        b_compile.push(millis(outcome.compile_duration));
        b_export.push(millis(outcome.export_duration));
        if iteration == 0 {
            page_svgs = outcome
                .artifacts
                .iter()
                .map(|artifact| artifact.bytes.clone())
                .collect();
        }
    }
    let (bcc, bcm, bcd) = stats(&b_compile);
    let (bec, bem, bed) = stats(&b_export);
    let (bwc, bwm, bwd) = stats(&b_wall);
    println!("\n## B. build-time step (Typst compile + SVG export, whole document)");
    println!("compile cold/min/med ms: {bcc:.2} {bcm:.2} {bcd:.2}");
    println!("export  cold/min/med ms: {bec:.2} {bem:.2} {bed:.2}");
    println!("wall    cold/min/med ms: {bwc:.2} {bwm:.2} {bwd:.2}");
    let mut total_svg = 0usize;
    let mut total_gzip = 0usize;
    println!("page | svg bytes | gzip bytes");
    for (index, svg) in page_svgs.iter().enumerate() {
        let gzip = gzip_len(svg)?;
        total_svg += svg.len();
        total_gzip += gzip;
        println!("{:>4} | {:>9} | {:>10}", index + 1, svg.len(), gzip);
    }
    println!(
        "total svg {total_svg} bytes, gzip {total_gzip} bytes; .typ sources {typ_bytes} bytes, \
all sources {source_bytes} bytes (svg/typ = {:.2}x, gzip/typ = {:.2}x)",
        total_svg as f64 / typ_bytes as f64,
        total_gzip as f64 / typ_bytes as f64
    );

    // ---- SVG self-containment check ---------------------------------------------------
    let probe = String::from_utf8_lossy(&page_svgs[spec.quality_page]).into_owned();
    println!("\n## SVG self-containment (page {})", spec.quality_page + 1);
    for needle in [
        "@font-face",
        "font-family",
        "<text",
        "<image",
        "<use",
        "<path",
        "data:image/png",
        "data:image/jpeg",
        "data:image/svg+xml",
    ] {
        println!(
            "  {needle:<20} occurrences: {}",
            probe.matches(needle).count()
        );
    }
    fs::write(
        artifacts.join(format!("{}-page{}.svg", spec.key, spec.quality_page + 1)),
        &page_svgs[spec.quality_page],
    )?;

    // Standalone cost of each embedded SVG asset, to attribute page raster time.
    if !spec.binary_files.is_empty() {
        println!("\n## embedded SVG assets rendered standalone at 8x natural size");
        println!("asset | parse med ms | raster med ms | px | Mpx/s");
        for (name, bytes, _) in files.iter().filter(|(name, _, _)| name.ends_with(".svg")) {
            let tree = usvg::Tree::from_data(bytes, svg_options)?;
            let width = (tree.size().width() * 8.0).round() as u32;
            let height = (tree.size().height() * 8.0).round() as u32;
            let mut parse_samples = Vec::new();
            let mut raster_samples = Vec::new();
            for _ in 0..ITERATIONS {
                let started = Instant::now();
                let parsed = usvg::Tree::from_data(bytes, svg_options)?;
                parse_samples.push(millis(started.elapsed()));
                std::hint::black_box(parsed.size().width());
                let started = Instant::now();
                let mut pixmap =
                    tiny_skia::Pixmap::new(width, height).context("pixmap alloc failed")?;
                resvg::render(
                    &tree,
                    tiny_skia::Transform::from_scale(8.0, 8.0),
                    &mut pixmap.as_mut(),
                );
                raster_samples.push(millis(started.elapsed()));
                std::hint::black_box(pixmap.data()[0]);
            }
            let raster_median = stats(&raster_samples).2;
            let megapixels = (width as f64) * (height as f64) / 1.0e6;
            println!(
                "{name} | {:.2} | {raster_median:.2} | {width}x{height} | {:.1}",
                stats(&parse_samples).2,
                megapixels / (raster_median / 1000.0)
            );
        }
    }

    // ---- phase C timing: parse + raster, every page, every zoom step -------------------
    println!("\n## C. precompiled-vector runtime path (usvg parse + resvg raster)");
    let mut parse_stats = Vec::new();
    for svg in &page_svgs {
        let mut samples = Vec::new();
        for _ in 0..ITERATIONS {
            let started = Instant::now();
            let tree = usvg::Tree::from_data(svg, svg_options)?;
            samples.push(millis(started.elapsed()));
            std::hint::black_box(tree.size().width());
        }
        parse_stats.push(stats(&samples));
    }
    println!("page | parse cold/min/med ms");
    for (index, (cold, min, median)) in parse_stats.iter().enumerate() {
        println!("{:>4} | {cold:9.3} {min:9.3} {median:9.3}", index + 1);
    }

    let trees: Vec<usvg::Tree> = page_svgs
        .iter()
        .map(|svg| usvg::Tree::from_data(svg, svg_options))
        .collect::<Result<_, _>>()?;

    // Ground-truth dimensions per page and zoom factor come from phase A's exporter.
    println!("\nraster (alloc + resvg::render), ms");
    println!("page | zoom |     px w x h | cold      min      med |    Mpx/s");
    let mut raster_stats: Vec<Vec<(f64, f64, f64)>> = vec![Vec::new(); trees.len()];
    let mut raster_megapixels = vec![0f64; ZOOM_STEPS.len()];
    for (zoom_index, (zoom, ppi)) in ZOOM_STEPS.into_iter().enumerate() {
        let ground_truth =
            renderer.render_project(spec.main, &files, &[OutputFormat::Rgba], ppi, Dict::new())?;
        for (index, tree) in trees.iter().enumerate() {
            let artifact = &ground_truth.artifacts[index];
            let (width, height) = (artifact.width.unwrap(), artifact.height.unwrap());
            let mut samples = Vec::new();
            for _ in 0..ITERATIONS {
                let started = Instant::now();
                let mut pixmap = tiny_skia::Pixmap::new(width, height)
                    .context("failed to allocate resvg pixmap")?;
                resvg::render(
                    tree,
                    tiny_skia::Transform::from_scale(zoom, zoom),
                    &mut pixmap.as_mut(),
                );
                samples.push(millis(started.elapsed()));
                std::hint::black_box(pixmap.data()[0]);
            }
            let summary = stats(&samples);
            raster_stats[index].push(summary);
            let megapixels = (width as f64) * (height as f64) / 1.0e6;
            raster_megapixels[zoom_index] += megapixels;
            println!(
                "{:>4} | {zoom:>3}x | {width:>6} x {height:<6} | {:8.2} {:8.2} {:8.2} | {:8.1}",
                index + 1,
                summary.0,
                summary.1,
                summary.2,
                megapixels / (summary.2 / 1000.0)
            );
        }
    }

    // ---- pixel-geometry check ----------------------------------------------------------
    println!("\n## pixel geometry (pt @72dpi -> px @96dpi -> PPI)");
    let tree = &trees[spec.quality_page];
    println!(
        "page {} usvg tree size: {:.3} x {:.3} px (== pt * 96/72)",
        spec.quality_page + 1,
        tree.size().width(),
        tree.size().height()
    );
    println!("zoom |  ppi | typst px (ground truth) | resvg nominal px | match");
    for (zoom, ppi) in ZOOM_STEPS {
        let ground_truth =
            renderer.render_project(spec.main, &files, &[OutputFormat::Rgba], ppi, Dict::new())?;
        let artifact = &ground_truth.artifacts[spec.quality_page];
        let (gw, gh) = (artifact.width.unwrap(), artifact.height.unwrap());
        let nw = (tree.size().width() * zoom).round() as u32;
        let nh = (tree.size().height() * zoom).round() as u32;
        println!(
            "{zoom:>3}x | {ppi:>4} | {gw:>9} x {gh:<9} | {nw:>7} x {nh:<7} | {}",
            if gw == nw && gh == nh {
                "exact"
            } else {
                "DIFFERS"
            }
        );
    }

    // ---- phase D + quality ---------------------------------------------------------------
    let baseline = renderer.render_project(
        spec.main,
        &files,
        &[OutputFormat::Rgba],
        BASELINE_PPI,
        Dict::new(),
    )?;
    let baseline_artifact = &baseline.artifacts[spec.quality_page];
    let baseline_rgba = baseline_artifact.bytes.clone();
    let baseline_width = baseline_artifact.width.unwrap();
    let baseline_height = baseline_artifact.height.unwrap();
    drop(baseline);

    let mut quality_rows = Vec::new();
    let mut baseline_rows = Vec::new();
    for (zoom, ppi) in ZOOM_STEPS {
        let ground_truth =
            renderer.render_project(spec.main, &files, &[OutputFormat::Rgba], ppi, Dict::new())?;
        let artifact = &ground_truth.artifacts[spec.quality_page];
        let (gw, gh) = (artifact.width.unwrap(), artifact.height.unwrap());
        let reference = flatten_premultiplied(&artifact.bytes, gw, gh);
        drop(ground_truth);

        // C: resvg at the nominal vector size.
        let nw = (tree.size().width() * zoom).round() as u32;
        let nh = (tree.size().height() * zoom).round() as u32;
        let mut pixmap =
            tiny_skia::Pixmap::new(nw, nh).context("failed to allocate resvg pixmap")?;
        resvg::render(
            tree,
            tiny_skia::Transform::from_scale(zoom, zoom),
            &mut pixmap.as_mut(),
        );
        let vector = flatten_premultiplied(pixmap.data(), nw, nh);
        drop(pixmap);

        // D: memcpy + bilinear upscale of the 144 PPI page to the same target size.
        let mut memcpy_samples = Vec::new();
        let mut resample_samples = Vec::new();
        let mut upscaled = Vec::new();
        for _ in 0..ITERATIONS {
            let started = Instant::now();
            let copy = baseline_rgba.clone();
            memcpy_samples.push(millis(started.elapsed()));
            let started = Instant::now();
            upscaled =
                bilinear_resample_premultiplied(&copy, baseline_width, baseline_height, gw, gh);
            resample_samples.push(millis(started.elapsed()));
        }
        let bitmap = flatten_premultiplied(&upscaled, gw, gh);
        drop(upscaled);
        let (mc, mm, md) = stats(&memcpy_samples);
        let (rc, rm, rd) = stats(&resample_samples);
        baseline_rows.push((zoom, mc, mm, md, rc, rm, rd));

        let vector_metrics = compare(&reference, &vector);
        let bitmap_metrics = compare(&reference, &bitmap);
        quality_rows.push((zoom, ppi, "resvg (C)", vector_metrics));
        quality_rows.push((zoom, ppi, "bilinear (D)", bitmap_metrics));

        if FULL_PAGE_PNG_ZOOMS.contains(&zoom) {
            write_png(
                &artifacts.join(format!("{}-{zoom}x-a-groundtruth.png", spec.key)),
                &reference,
            )?;
            write_png(
                &artifacts.join(format!("{}-{zoom}x-c-resvg.png", spec.key)),
                &vector,
            )?;
            write_png(
                &artifacts.join(format!("{}-{zoom}x-d-bilinear.png", spec.key)),
                &bitmap,
            )?;
        }
        if zoom == 4.0 || zoom == 8.0 {
            for crop in spec.crops {
                let x = (crop.x * gw as f32) as u32;
                let y = (crop.y * gh as f32) as u32;
                for (label, image) in [
                    ("a-groundtruth", &reference),
                    ("c-resvg", &vector),
                    ("d-bilinear", &bitmap),
                ] {
                    write_png(
                        &artifacts.join(format!(
                            "{}-crop-{}-{zoom}x-{label}.png",
                            spec.key, crop.name
                        )),
                        &image.crop(x, y, CROP_SIZE, CROP_SIZE),
                    )?;
                }
            }
        }
    }

    println!(
        "\n## D. instant-bitmap baseline (page {}, from a {BASELINE_PPI} PPI RGBA page of \
{baseline_width}x{baseline_height})",
        spec.quality_page + 1
    );
    println!("zoom | memcpy cold/min/med ms | bilinear cold/min/med ms | total med ms");
    let mut d_totals = Vec::new();
    for (zoom, mc, mm, md, rc, rm, rd) in &baseline_rows {
        d_totals.push(md + rd);
        println!(
            "{zoom:>3}x | {mc:7.3} {mm:7.3} {md:7.3} | {rc:8.2} {rm:8.2} {rd:8.2} | {:.2}",
            md + rd
        );
    }

    println!(
        "\n## quality (page {}, both sides flattened over opaque white)",
        spec.quality_page + 1
    );
    println!("zoom |  ppi | source | psnr dB | max delta | % px delta>8 | compared px");
    for (zoom, ppi, source, metrics) in quality_rows {
        let psnr = if metrics.psnr_db.is_finite() {
            format!("{:.2}", metrics.psnr_db)
        } else {
            "inf".to_owned()
        };
        println!(
            "{zoom:>3}x | {ppi:>4} | {source:<12} | {psnr:>7} | {:>9} | {:>11.3}% | {}x{}",
            metrics.max_delta,
            metrics.percent_over_8,
            metrics.compared_width,
            metrics.compared_height
        );
    }

    // ---- verdict inputs ------------------------------------------------------------------
    // Whole-document totals per zoom step: what one zoom action costs end to end.
    // A is the current path (recompile at the nearest ladder PPI), C is parse+raster
    // of every page, D is the page-1 instant-bitmap cost scaled to the page count.
    // A and C are only comparable at a PPI both actually render. Only the ladder rungs
    // that equal a resvg zoom factor qualify; 1x (96 PPI) has no rung, because the policy
    // clamps a 1x view up to 144 PPI.
    println!("\n## per-zoom-step summary (median, whole document unless noted)");
    println!(
        "zoom |  ppi | matched A rung | A wall ms | A Mpx/s | C raster ms | C Mpx/s | net (C-A) ms | C parse ms (once) | D page-1 ms"
    );
    let parse_total: f64 = parse_stats.iter().map(|page| page.2).sum::<f64>();
    for (zoom_index, (zoom, ppi)) in ZOOM_STEPS.iter().enumerate() {
        let raster_total: f64 = raster_stats
            .iter()
            .map(|page| page[zoom_index].2)
            .sum::<f64>();
        let matched = TIMED_PPI.iter().position(|rung| (rung - ppi).abs() < 0.01);
        let (rung, a_median, a_throughput, net) = match matched {
            Some(index) => {
                let a_median = stats(&a_wall[index]).2;
                (
                    format!("{:.0}", TIMED_PPI[index]),
                    format!("{a_median:.2}"),
                    format!("{:.1}", a_megapixels[index] / (a_median / 1000.0)),
                    format!("{:+.2}", raster_total - a_median),
                )
            }
            None => (
                "none".to_owned(),
                "-".to_owned(),
                "-".to_owned(),
                "-".to_owned(),
            ),
        };
        let c_megapixels = raster_megapixels[zoom_index];
        println!(
            "{zoom:>3}x | {ppi:>4} | {rung:>14} | {a_median:>9} | {a_throughput:>7} | {raster_total:11.2} | {:7.1} | {net:>12} | {parse_total:17.2} | {:11.2}",
            c_megapixels / (raster_total / 1000.0),
            d_totals[zoom_index]
        );
    }

    if page_count == 0 {
        bail!("document produced no pages");
    }
    Ok(())
}
