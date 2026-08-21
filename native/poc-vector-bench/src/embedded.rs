//! Embedded-target measurements: the desktop bench asked "which path is faster at
//! an 8x zoom on a 32-thread desktop". A panel asks a different question --
//! "what fits in flash, what fits in 512 MB, and what does one page cost on one
//! slow core at the panel's one fixed scale".
//!
//! Each mode is a separate process so peak RSS is attributable to exactly one
//! approach. `prepare` writes the build-time assets and a manifest; the timing
//! modes read the manifest and never touch a scale the panel would not use.
//!
//!   emb prepare
//!   emb typst  <doc> <panel_width> <iters>   -- runtime Typst, whole document
//!   emb resvg  <doc> <panel_width> <iters>   -- precompiled SVG + resvg, no Typst
//!   emb raster <doc> <panel_width> <iters>   -- precompiled PNG / RGB565, no renderer
//!   emb page   <doc> <panel_width> <iters>   -- resvg + raster, first page only
//!   emb ladder <doc> <panel_width>           -- RSS across a zoom ladder, Typst path

use std::fs;
use std::path::{Path, PathBuf};
use std::time::Instant;

use anyhow::{Context, Result, bail};
use typst::foundations::Dict;
use typst_dotnet_native::engine::{OutputFormat, millis};

use crate::imageops::{Rgb8, flatten_premultiplied};
use crate::{DOCS, DocSpec, gzip_len, load_project, make_renderer, repo_root, stats};

/// Fixed panel widths a doc reader would fit a page to. 800x480 is the classic
/// industrial 7" panel; 1920x1080 the 15" one.
const PANEL_WIDTHS: [u32; 2] = [800, 1920];
/// Reference PPI the SVG coordinate system is in (usvg's default dpi).
const CSS_PPI: f32 = 96.0;

pub fn run(arguments: &[String]) -> Result<()> {
    let mode = arguments.first().map(String::as_str).unwrap_or("");
    let root = repo_root()?;
    let directory = root.join("artifacts").join("poc").join("emb");
    match mode {
        "prepare" => prepare(&root, &directory),
        "typst" | "resvg" | "raster" | "page" | "ladder" => {
            let spec = doc_spec(arg(arguments, 1, "doc")?)?;
            let panel: u32 = arg(arguments, 2, "panel_width")?.parse()?;
            let iterations: usize = arguments.get(3).map_or(Ok(5), |value| value.parse())?;
            let manifest = Manifest::load(&directory, spec.key, panel)?;
            match mode {
                "typst" => timed_typst(spec, &root, &manifest, iterations),
                "resvg" => timed_resvg(spec, &directory, &manifest, iterations, false),
                "raster" => timed_raster(spec, &directory, &manifest, panel, iterations, false),
                "page" => {
                    timed_resvg(spec, &directory, &manifest, iterations, true)?;
                    timed_raster(spec, &directory, &manifest, panel, iterations, true)
                }
                _ => ladder(spec, &root, &manifest),
            }
        }
        _ => bail!(
            "usage: poc-vector-bench emb <prepare|typst|resvg|raster|page|ladder> [doc] [panel_width] [iters]"
        ),
    }
}

fn arg<'a>(arguments: &'a [String], index: usize, name: &str) -> Result<&'a str> {
    arguments
        .get(index)
        .map(String::as_str)
        .with_context(|| format!("missing argument: {name}"))
}

fn doc_spec(key: &str) -> Result<&'static DocSpec> {
    DOCS.iter()
        .find(|spec| spec.key == key)
        .with_context(|| format!("unknown document: {key}"))
}

/// What `prepare` computed for one (document, panel width) pair, so the timing
/// modes can hit the exact same scale without re-running Typst to discover it.
struct Manifest {
    ppi: f32,
    pages: Vec<(u32, u32)>,
}

impl Manifest {
    fn path(directory: &Path, key: &str, panel: u32) -> PathBuf {
        directory.join(format!("{key}-w{panel}.manifest"))
    }

    fn load(directory: &Path, key: &str, panel: u32) -> Result<Self> {
        let path = Manifest::path(directory, key, panel);
        let text = fs::read_to_string(&path)
            .with_context(|| format!("run `emb prepare` first: {} missing", path.display()))?;
        let mut lines = text.lines();
        let ppi = lines.next().context("manifest is empty")?.parse()?;
        let mut pages = Vec::new();
        for line in lines {
            let (width, height) = line.split_once('x').context("bad manifest page line")?;
            pages.push((width.parse()?, height.parse()?));
        }
        Ok(Self { ppi, pages })
    }

    fn megapixels(&self) -> f64 {
        self.pages
            .iter()
            .map(|(width, height)| (*width as f64) * (*height as f64) / 1.0e6)
            .sum()
    }
}

fn scale(manifest: &Manifest) -> f32 {
    manifest.ppi / CSS_PPI
}

// ---- build-time step ------------------------------------------------------------------

fn prepare(root: &Path, directory: &Path) -> Result<()> {
    fs::create_dir_all(directory)?;
    println!("# emb prepare");
    println!(
        "panel widths: {}; page is fit to panel width, so ppi = 96 * panel_w / css_page_w",
        PANEL_WIDTHS
            .iter()
            .map(u32::to_string)
            .collect::<Vec<_>>()
            .join(", ")
    );

    println!("\n## precompiled vector assets (scale independent)");
    println!("doc | pages | svg raw B | svg gzip B");
    let mut svg_totals = (0usize, 0usize);
    for spec in DOCS {
        let (project_dir, files) = load_project(spec, root)?;
        let renderer = make_renderer(&project_dir)?;
        let outcome = renderer.render_project(
            spec.main,
            &files,
            &[OutputFormat::Svg],
            CSS_PPI,
            Dict::new(),
        )?;
        let mut raw = 0usize;
        let mut gzip = 0usize;
        for (index, artifact) in outcome.artifacts.iter().enumerate() {
            fs::write(
                directory.join(format!("{}-p{:03}.svg", spec.key, index + 1)),
                &artifact.bytes,
            )?;
            raw += artifact.bytes.len();
            gzip += gzip_len(&artifact.bytes)?;
        }
        svg_totals.0 += raw;
        svg_totals.1 += gzip;
        println!(
            "{} | {} | {raw} | {gzip}",
            spec.key,
            outcome.artifacts.len()
        );
    }
    println!("all three docs | | {} | {}", svg_totals.0, svg_totals.1);

    println!("\n## precompiled raster assets (one panel scale)");
    println!(
        "doc | panel w | ppi | page px | pages | png B | gray png B | mono1 B | mono1 gzip B | rgb565 B | rgb565 gzip B | rgba8 B"
    );
    for spec in DOCS {
        let (project_dir, files) = load_project(spec, root)?;
        let renderer = make_renderer(&project_dir)?;
        // One reference render establishes the CSS page width, which fixes the
        // fit-to-width PPI for every panel.
        let reference = renderer.render_project(
            spec.main,
            &files,
            &[OutputFormat::Rgba],
            CSS_PPI,
            Dict::new(),
        )?;
        let css_width = reference.artifacts[0].width.context("no page width")?;
        drop(reference);

        for panel in PANEL_WIDTHS {
            let ppi = CSS_PPI * panel as f32 / css_width as f32;
            let outcome = renderer.render_project(
                spec.main,
                &files,
                &[OutputFormat::Rgba],
                ppi,
                Dict::new(),
            )?;
            let mut manifest = format!("{ppi}\n");
            let mut sizes = SizeRow::default();
            let mut dimensions = String::new();
            for (index, artifact) in outcome.artifacts.iter().enumerate() {
                let (width, height) = (artifact.width.unwrap(), artifact.height.unwrap());
                manifest.push_str(&format!("{width}x{height}\n"));
                if index == 0 {
                    dimensions = format!("{width}x{height}");
                }
                let image = flatten_premultiplied(&artifact.bytes, width, height);
                let stem = format!("{}-w{panel}-p{:03}", spec.key, index + 1);
                let png_path = directory.join(format!("{stem}.png"));
                encode_png(&png_path, &image.data, width, height, png::ColorType::Rgb)?;
                let gray = to_gray8(&image);
                let gray_path = directory.join(format!("{stem}.gray.png"));
                encode_png(&gray_path, &gray, width, height, png::ColorType::Grayscale)?;
                let mono = to_mono1(&gray, width, height);
                let rgb565 = to_rgb565(&image);
                fs::write(directory.join(format!("{stem}.565")), &rgb565)?;
                sizes.add(SizeRow {
                    png: fs::metadata(&png_path)?.len() as usize,
                    gray_png: fs::metadata(&gray_path)?.len() as usize,
                    mono1: mono.len(),
                    mono1_gzip: gzip_len(&mono)?,
                    rgb565: rgb565.len(),
                    rgb565_gzip: gzip_len(&rgb565)?,
                    rgba: artifact.bytes.len(),
                });
            }
            fs::write(Manifest::path(directory, spec.key, panel), manifest)?;
            println!(
                "{} | {panel} | {ppi:.1} | {dimensions} | {} | {} | {} | {} | {} | {} | {} | {}",
                spec.key,
                outcome.artifacts.len(),
                sizes.png,
                sizes.gray_png,
                sizes.mono1,
                sizes.mono1_gzip,
                sizes.rgb565,
                sizes.rgb565_gzip,
                sizes.rgba
            );
        }
    }
    report_rss("prepare");
    Ok(())
}

#[derive(Default)]
struct SizeRow {
    png: usize,
    gray_png: usize,
    mono1: usize,
    mono1_gzip: usize,
    rgb565: usize,
    rgb565_gzip: usize,
    rgba: usize,
}

impl SizeRow {
    fn add(&mut self, other: SizeRow) {
        self.png += other.png;
        self.gray_png += other.gray_png;
        self.mono1 += other.mono1;
        self.mono1_gzip += other.mono1_gzip;
        self.rgb565 += other.rgb565;
        self.rgb565_gzip += other.rgb565_gzip;
        self.rgba += other.rgba;
    }
}

/// `imageops::write_png` uses the crate default (fast) compression, which is fine
/// for viewing artifacts but overstates flash cost. Flash numbers use `Best`.
fn encode_png(
    path: &Path,
    data: &[u8],
    width: u32,
    height: u32,
    color: png::ColorType,
) -> Result<()> {
    let file = fs::File::create(path).with_context(|| format!("create {}", path.display()))?;
    let mut encoder = png::Encoder::new(std::io::BufWriter::new(file), width, height);
    encoder.set_color(color);
    encoder.set_depth(png::BitDepth::Eight);
    encoder.set_compression(png::Compression::Best);
    encoder.write_header()?.write_image_data(data)?;
    Ok(())
}

/// 16 bpp little-endian RGB565 -- what a framebuffer panel actually consumes,
/// and half the flash of RGBA8 with no visible loss on document content.
fn to_rgb565(image: &Rgb8) -> Vec<u8> {
    let mut out = Vec::with_capacity(image.data.len() / 3 * 2);
    for pixel in image.data.chunks_exact(3) {
        let value = ((pixel[0] as u16 & 0xF8) << 8)
            | ((pixel[1] as u16 & 0xFC) << 3)
            | (pixel[2] as u16 >> 3);
        out.extend_from_slice(&value.to_le_bytes());
    }
    out
}

/// Rec. 601 luma, the grayscale a mono industrial panel would show.
fn to_gray8(image: &Rgb8) -> Vec<u8> {
    image
        .data
        .chunks_exact(3)
        .map(|pixel| {
            ((pixel[0] as u32 * 299 + pixel[1] as u32 * 587 + pixel[2] as u32 * 114) / 1000) as u8
        })
        .collect()
}

/// 1 bpp, MSB first, row aligned to a byte -- the LVGL / e-paper representation.
/// Plain 50 % threshold: no dithering, because document content is already
/// near-bilevel and dithering would defeat the run-length compression.
fn to_mono1(gray: &[u8], width: u32, height: u32) -> Vec<u8> {
    let row_bytes = width.div_ceil(8) as usize;
    let mut out = vec![0u8; row_bytes * height as usize];
    for y in 0..height as usize {
        for x in 0..width as usize {
            if gray[y * width as usize + x] < 128 {
                out[y * row_bytes + x / 8] |= 0x80 >> (x % 8);
            }
        }
    }
    out
}

// ---- runtime paths --------------------------------------------------------------------

fn timed_typst(spec: &DocSpec, root: &Path, manifest: &Manifest, iterations: usize) -> Result<()> {
    let (project_dir, files) = load_project(spec, root)?;
    let init_started = Instant::now();
    let renderer = make_renderer(&project_dir)?;
    let init = millis(init_started.elapsed());
    let rss_after_init = current_rss();
    let faults_after_init = page_faults();

    let mut compile = Vec::new();
    let mut export = Vec::new();
    let mut wall = Vec::new();
    for _ in 0..iterations {
        let started = Instant::now();
        let outcome = renderer.render_project(
            spec.main,
            &files,
            &[OutputFormat::Rgba],
            manifest.ppi,
            Dict::new(),
        )?;
        wall.push(millis(started.elapsed()));
        compile.push(millis(outcome.compile_duration));
        export.push(millis(outcome.export_duration));
    }
    let (_, _, export_median) = stats(&export);
    println!(
        "typst | {} | ppi {:.1} | pages {} | {:.2} Mpx | engine init {init:.2} ms | rss after init {} | page faults after init {}",
        spec.key,
        manifest.ppi,
        manifest.pages.len(),
        manifest.megapixels(),
        rss_label(rss_after_init),
        faults_after_init.map_or("n/a".to_owned(), |count| count.to_string())
    );
    print_row("compile", &compile);
    print_row("export", &export);
    print_row("wall", &wall);
    println!(
        "export Mpx/s: {:.1}",
        manifest.megapixels() / (export_median / 1000.0)
    );
    report_rss("typst");
    Ok(())
}

fn timed_resvg(
    spec: &DocSpec,
    directory: &Path,
    manifest: &Manifest,
    iterations: usize,
    first_page_only: bool,
) -> Result<()> {
    let options = usvg::Options::default();
    let page_count = if first_page_only {
        1
    } else {
        manifest.pages.len()
    };
    let mut svgs = Vec::new();
    for index in 0..page_count {
        svgs.push(fs::read(directory.join(format!(
            "{}-p{:03}.svg",
            spec.key,
            index + 1
        )))?);
    }

    let mut parse = Vec::new();
    let mut raster = Vec::new();
    let zoom = scale(manifest);
    for _ in 0..iterations {
        let started = Instant::now();
        let trees: Vec<usvg::Tree> = svgs
            .iter()
            .map(|svg| usvg::Tree::from_data(svg, &options))
            .collect::<Result<_, _>>()?;
        parse.push(millis(started.elapsed()));

        let started = Instant::now();
        for (index, tree) in trees.iter().enumerate() {
            let (width, height) = manifest.pages[index];
            let mut pixmap =
                tiny_skia::Pixmap::new(width, height).context("pixmap alloc failed")?;
            resvg::render(
                tree,
                tiny_skia::Transform::from_scale(zoom, zoom),
                &mut pixmap.as_mut(),
            );
            std::hint::black_box(pixmap.data()[0]);
        }
        raster.push(millis(started.elapsed()));
    }
    let megapixels: f64 = manifest.pages[..page_count]
        .iter()
        .map(|(width, height)| (*width as f64) * (*height as f64) / 1.0e6)
        .sum();
    println!(
        "resvg | {} | ppi {:.1} | scale {zoom:.3} | pages {page_count} | {megapixels:.2} Mpx",
        spec.key, manifest.ppi
    );
    print_row("parse", &parse);
    print_row("raster", &raster);
    println!(
        "raster Mpx/s: {:.1}",
        megapixels / (stats(&raster).2 / 1000.0)
    );
    report_rss("resvg");
    Ok(())
}

fn timed_raster(
    spec: &DocSpec,
    directory: &Path,
    manifest: &Manifest,
    panel: u32,
    iterations: usize,
    first_page_only: bool,
) -> Result<()> {
    let page_count = if first_page_only {
        1
    } else {
        manifest.pages.len()
    };
    let mut png_total = Vec::new();
    let mut rgb565_total = Vec::new();
    for _ in 0..iterations {
        let started = Instant::now();
        for index in 0..page_count {
            let path = directory.join(format!("{}-w{panel}-p{:03}.png", spec.key, index + 1));
            let decoder = png::Decoder::new(std::io::BufReader::new(fs::File::open(&path)?));
            let mut reader = decoder.read_info()?;
            let mut buffer = vec![0u8; reader.output_buffer_size()];
            reader.next_frame(&mut buffer)?;
            std::hint::black_box(buffer[0]);
        }
        png_total.push(millis(started.elapsed()));

        let started = Instant::now();
        for index in 0..page_count {
            let path = directory.join(format!("{}-w{panel}-p{:03}.565", spec.key, index + 1));
            let bytes = fs::read(&path)?;
            std::hint::black_box(bytes[0]);
        }
        rgb565_total.push(millis(started.elapsed()));
    }
    let megapixels: f64 = manifest.pages[..page_count]
        .iter()
        .map(|(width, height)| (*width as f64) * (*height as f64) / 1.0e6)
        .sum();
    println!(
        "raster | {} | panel {panel} | pages {page_count} | {megapixels:.2} Mpx",
        spec.key
    );
    print_row("png decode", &png_total);
    print_row("rgb565 read", &rgb565_total);
    report_rss("raster");
    Ok(())
}

/// Does the process give memory back between zoom steps, or does comemo keep it?
fn ladder(spec: &DocSpec, root: &Path, manifest: &Manifest) -> Result<()> {
    let (project_dir, files) = load_project(spec, root)?;
    let renderer = make_renderer(&project_dir)?;
    println!(
        "ladder | {} | base ppi {:.1} | rss after engine init {}",
        spec.key,
        manifest.ppi,
        rss_label(current_rss())
    );
    println!("step ppi | doc Mpx | wall ms | rss after | peak so far");
    for multiplier in [1.0f32, 2.0, 4.0, 1.0] {
        let ppi = manifest.ppi * multiplier;
        let started = Instant::now();
        let outcome =
            renderer.render_project(spec.main, &files, &[OutputFormat::Rgba], ppi, Dict::new())?;
        let wall = millis(started.elapsed());
        let megapixels: f64 = outcome
            .artifacts
            .iter()
            .map(|artifact| {
                (artifact.width.unwrap() as f64) * (artifact.height.unwrap() as f64) / 1.0e6
            })
            .sum();
        drop(outcome);
        println!(
            "{ppi:8.1} | {megapixels:7.2} | {wall:7.1} | {} | {}",
            rss_label(current_rss()),
            rss_label(peak_rss())
        );
    }
    report_rss("ladder");
    Ok(())
}

fn print_row(label: &str, samples: &[f64]) {
    let (cold, min, median) = stats(samples);
    println!("{label:<12} cold/min/med ms: {cold:.2} {min:.2} {median:.2}");
}

// ---- resident set ---------------------------------------------------------------------

fn report_rss(label: &str) {
    println!(
        "rss[{label}] current {} peak {} | cores available {} | page faults {}",
        rss_label(current_rss()),
        rss_label(peak_rss()),
        available_cores(),
        page_faults().map_or("n/a".to_owned(), |count| count.to_string())
    );
}

/// A first touch of a page that is backed by the 51 MB shared library is a soft
/// fault here and a flash read on a device, so the count bounds cold-start I/O.
#[cfg(windows)]
fn page_faults() -> Option<u32> {
    platform::counters().map(|counters| counters.page_fault_count)
}

#[cfg(not(windows))]
fn page_faults() -> Option<u32> {
    None
}

/// Every timing run states how many cores it was actually allowed to use, so a
/// `start /affinity 1` run cannot be silently mistaken for an unpinned one.
#[cfg(windows)]
fn available_cores() -> u32 {
    platform::affinity_mask().map_or(0, u64::count_ones)
}

#[cfg(not(windows))]
fn available_cores() -> u32 {
    std::thread::available_parallelism().map_or(0, |value| value.get() as u32)
}

fn rss_label(bytes: Option<usize>) -> String {
    match bytes {
        Some(bytes) => format!("{:.1} MB ({bytes} B)", bytes as f64 / (1024.0 * 1024.0)),
        None => "n/a".to_owned(),
    }
}

#[cfg(windows)]
mod platform {
    #[repr(C)]
    #[derive(Default)]
    pub struct ProcessMemoryCounters {
        pub cb: u32,
        pub page_fault_count: u32,
        pub peak_working_set_size: usize,
        pub working_set_size: usize,
        pub quota_peak_paged_pool_usage: usize,
        pub quota_paged_pool_usage: usize,
        pub quota_peak_non_paged_pool_usage: usize,
        pub quota_non_paged_pool_usage: usize,
        pub pagefile_usage: usize,
        pub peak_pagefile_usage: usize,
    }

    unsafe extern "system" {
        fn GetCurrentProcess() -> isize;
        fn K32GetProcessMemoryInfo(
            process: isize,
            counters: *mut ProcessMemoryCounters,
            size: u32,
        ) -> i32;
        fn GetProcessAffinityMask(
            process: isize,
            process_mask: *mut usize,
            system_mask: *mut usize,
        ) -> i32;
    }

    pub fn affinity_mask() -> Option<u64> {
        let mut process_mask = 0usize;
        let mut system_mask = 0usize;
        let ok = unsafe {
            GetProcessAffinityMask(GetCurrentProcess(), &mut process_mask, &mut system_mask)
        };
        (ok != 0).then_some(process_mask as u64)
    }

    pub fn counters() -> Option<ProcessMemoryCounters> {
        let mut counters = ProcessMemoryCounters {
            cb: size_of::<ProcessMemoryCounters>() as u32,
            ..Default::default()
        };
        let ok = unsafe {
            K32GetProcessMemoryInfo(
                GetCurrentProcess(),
                &mut counters,
                size_of::<ProcessMemoryCounters>() as u32,
            )
        };
        (ok != 0).then_some(counters)
    }
}

#[cfg(windows)]
fn current_rss() -> Option<usize> {
    platform::counters().map(|counters| counters.working_set_size)
}

#[cfg(windows)]
fn peak_rss() -> Option<usize> {
    platform::counters().map(|counters| counters.peak_working_set_size)
}

#[cfg(not(windows))]
fn current_rss() -> Option<usize> {
    proc_status("VmRSS:")
}

#[cfg(not(windows))]
fn peak_rss() -> Option<usize> {
    proc_status("VmHWM:")
}

#[cfg(not(windows))]
fn proc_status(key: &str) -> Option<usize> {
    let status = fs::read_to_string("/proc/self/status").ok()?;
    let line = status.lines().find(|line| line.starts_with(key))?;
    let kilobytes: usize = line.split_whitespace().nth(1)?.parse().ok()?;
    Some(kilobytes * 1024)
}
