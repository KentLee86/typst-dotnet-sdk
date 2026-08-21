//! Pixel helpers shared by the benchmark: premultiplied-alpha flattening,
//! bilinear resampling, PSNR-style comparison, and PNG output.

use std::fs::File;
use std::io::BufWriter;
use std::path::Path;

use anyhow::{Context, Result};

/// Straight (non-premultiplied) 8-bit RGB, already composited over opaque white.
/// Both `typst-render` and `resvg` hand back premultiplied RGBA8, so flattening
/// to this shape makes the two directly comparable.
pub struct Rgb8 {
    pub width: u32,
    pub height: u32,
    pub data: Vec<u8>,
}

impl Rgb8 {
    pub fn crop(&self, x: u32, y: u32, width: u32, height: u32) -> Rgb8 {
        let x = x.min(self.width.saturating_sub(1));
        let y = y.min(self.height.saturating_sub(1));
        let width = width.min(self.width - x);
        let height = height.min(self.height - y);
        let mut data = Vec::with_capacity((width * height * 3) as usize);
        for row in 0..height {
            let start = (((y + row) * self.width + x) * 3) as usize;
            data.extend_from_slice(&self.data[start..start + (width * 3) as usize]);
        }
        Rgb8 {
            width,
            height,
            data,
        }
    }
}

/// Composite premultiplied RGBA8 over opaque white: `out = premul + (255 - a)`.
pub fn flatten_premultiplied(rgba: &[u8], width: u32, height: u32) -> Rgb8 {
    let mut data = Vec::with_capacity((width * height * 3) as usize);
    for pixel in rgba.chunks_exact(4) {
        let inverse = 255 - pixel[3];
        data.push(pixel[0].saturating_add(inverse));
        data.push(pixel[1].saturating_add(inverse));
        data.push(pixel[2].saturating_add(inverse));
    }
    Rgb8 {
        width,
        height,
        data,
    }
}

/// Bilinear resample of premultiplied RGBA8. Interpolating premultiplied values
/// is linear-correct, so no un-premultiply round trip is needed.
pub fn bilinear_resample_premultiplied(
    source: &[u8],
    source_width: u32,
    source_height: u32,
    target_width: u32,
    target_height: u32,
) -> Vec<u8> {
    let mut out = vec![0u8; (target_width as usize) * (target_height as usize) * 4];
    let x_ratio = source_width as f32 / target_width as f32;
    let y_ratio = source_height as f32 / target_height as f32;
    let max_x = source_width.saturating_sub(1);
    let max_y = source_height.saturating_sub(1);
    for y in 0..target_height {
        let source_y = ((y as f32 + 0.5) * y_ratio - 0.5).max(0.0);
        let y0 = (source_y as u32).min(max_y);
        let y1 = (y0 + 1).min(max_y);
        let wy = source_y - y0 as f32;
        for x in 0..target_width {
            let source_x = ((x as f32 + 0.5) * x_ratio - 0.5).max(0.0);
            let x0 = (source_x as u32).min(max_x);
            let x1 = (x0 + 1).min(max_x);
            let wx = source_x - x0 as f32;
            let i00 = ((y0 * source_width + x0) * 4) as usize;
            let i01 = ((y0 * source_width + x1) * 4) as usize;
            let i10 = ((y1 * source_width + x0) * 4) as usize;
            let i11 = ((y1 * source_width + x1) * 4) as usize;
            let target = ((y * target_width + x) * 4) as usize;
            for channel in 0..4 {
                let top =
                    source[i00 + channel] as f32 * (1.0 - wx) + source[i01 + channel] as f32 * wx;
                let bottom =
                    source[i10 + channel] as f32 * (1.0 - wx) + source[i11 + channel] as f32 * wx;
                out[target + channel] =
                    (top * (1.0 - wy) + bottom * wy).round().clamp(0.0, 255.0) as u8;
            }
        }
    }
    out
}

pub struct Metrics {
    pub psnr_db: f64,
    pub max_delta: u8,
    pub percent_over_8: f64,
    pub compared_width: u32,
    pub compared_height: u32,
}

/// Compare on the overlapping top-left region so a one-pixel rounding
/// difference does not silently shift every row.
pub fn compare(reference: &Rgb8, candidate: &Rgb8) -> Metrics {
    let width = reference.width.min(candidate.width);
    let height = reference.height.min(candidate.height);
    let mut squared_error = 0f64;
    let mut max_delta = 0u8;
    let mut over_threshold = 0u64;
    for y in 0..height {
        let reference_row = ((y * reference.width) * 3) as usize;
        let candidate_row = ((y * candidate.width) * 3) as usize;
        for x in 0..width {
            let a = reference_row + (x * 3) as usize;
            let b = candidate_row + (x * 3) as usize;
            let mut pixel_over = false;
            for channel in 0..3 {
                let delta = reference.data[a + channel].abs_diff(candidate.data[b + channel]);
                squared_error += (delta as f64) * (delta as f64);
                max_delta = max_delta.max(delta);
                pixel_over |= delta > 8;
            }
            over_threshold += u64::from(pixel_over);
        }
    }
    let samples = (width as f64) * (height as f64) * 3.0;
    let mean_squared_error = squared_error / samples;
    let psnr_db = if mean_squared_error == 0.0 {
        f64::INFINITY
    } else {
        10.0 * (255.0f64 * 255.0 / mean_squared_error).log10()
    };
    Metrics {
        psnr_db,
        max_delta,
        percent_over_8: over_threshold as f64 * 100.0 / (width as f64 * height as f64),
        compared_width: width,
        compared_height: height,
    }
}

pub fn write_png(path: &Path, image: &Rgb8) -> Result<()> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let file =
        File::create(path).with_context(|| format!("failed to create {}", path.display()))?;
    let mut encoder = png::Encoder::new(BufWriter::new(file), image.width, image.height);
    encoder.set_color(png::ColorType::Rgb);
    encoder.set_depth(png::BitDepth::Eight);
    encoder
        .write_header()?
        .write_image_data(&image.data)
        .with_context(|| format!("failed to write {}", path.display()))?;
    Ok(())
}
