# Vector precompile benchmark (PoC)

> **Dead-end branch — reference only, not intended for `main`.**
>
> Three reviews landed after this document was written. A correction pass was started
> and interrupted before it wrote anything, so **the body below is the uncorrected
> original** plus an appended `Embedded targets` section. Read it with the following
> corrections in hand.
>
> 1. **The `Verdict` table below records the wrong sign for `editor-export`.** Phase A
>    was measured at 144/288/576 PPI, but the real ladder in
>    `src/Typst.Renderer.Core/TypstRasterQualityPolicy.cs` is
>    `[144, 192, 288, 384, 576, 768]`. C was measured at 384/768 and compared against A
>    at 288/576, so C drew 1.78x more pixels. At matched PPI (re-measured in review):
>
>    | Doc | zoom / PPI | A wall | C raster | net |
>    | --- | --- | --- | --- | --- |
>    | editor-export | 4x / 384 | 1.19 | 1.08 | **C faster by 0.11** |
>    | editor-export | 8x / 768 | 3.86 | 3.24 | **C faster by 0.62** |
>    | serial-protocol | 4x / 384 | 210.78 | 280.01 | +69.2 |
>    | serial-protocol | 8x / 768 | 654.36 | 734.89 | +80.5 |
>    | korean-business-forms | 4x / 384 | 37.63 | 419.51 | +381.9 |
>    | korean-business-forms | 8x / 768 | 131.56 | 1716.93 | +1585.4 |
>
> 2. **Phase A was measured with typst-render's comemo caches warm.** Glyph rasters and
>    image textures are memoized on keys that include PPI; the loop revisits the same
>    PPI seven times, so the median is over pre-warmed iterations while resvg redoes all
>    glyph work every iteration. Jittering PPI per iteration costs A +34–64 % on
>    `serial-protocol` and +17–33 % on `korean-business-forms`.
>
> 3. **"The saving is negative in every case" is retracted.** Negative for text-heavy
>    multi-page documents (~1.2–1.6x per pixel), neutral-to-positive for CeTZ line art.
>
> 4. **The `korean-business-forms` collapse is a removable export artifact, not a
>    property of the vector path.** `resvg-0.45.1/src/image.rs::render_vector` allocates
>    a full-canvas pixmap per nested-SVG `<image>`. Inlining those two images as nested
>    `<svg>` groups takes page 1 at 8x from 638.30 ms to 78.07 ms. Raster images take a
>    different path and pay no penalty, so "any Typst document with an image hits this"
>    is false.
>
> 5. **Cold-start accounting omits engine initialization** (16.4 / 12.7 / 15.0 ms).
>    Cold time-to-first-paint at 144 PPI is 41.8 / 67.4 / 36.9 ms, none of which the
>    vector path pays. Precompiling only the first visible page saves 21–41 ms for
>    4 KB / 82 KB / 118 KB gzip — not the 3.28 MB whole-corpus figure.
>
> 6. **The binary-size section answers the wrong question.** "+11 KB" is the cost of
>    adding resvg to the current dll. A view-only consumer needs resvg-only vs the
>    current 51,230,208 B cdylib, and needs no font data at all. Unmeasured, ~40+ MB.
>
> 7. **The premise names the wrong layer.** `TypstRasterQualityPolicy` is a pure
>    function and `TypstRenderController` renders only when asked; nothing in the SDK
>    re-renders on zoom. The ladder-driven re-render lives in the sample apps
>    (`ScheduleAutomaticQualityRefresh` + timer).
>
> 8. **The end-to-end zoom step was never measured.** This bench drives
>    `engine::Renderer::render_project` directly. The real path adds a JSON FFI request,
>    a copy of every page's RGBA into managed arrays, and platform bitmap creation
>    (~288 MB per zoom step at 576 PPI on `serial-protocol`). Phase A is a lower bound
>    on user-visible latency. Page-range rendering and progressive display are
>    recommended here but never measured, and a build-time raster ladder and GPU vector
>    rasterization were never tested — the verdict is scoped to CPU resvg.
>
> **Largest measured win, unrelated to this proposal:** a page-range argument on the
> native render call — 127 ms → 14 ms per step and 562 MiB → ~115 MiB peak on the
> embedded profile, with no new assets.

Question: static demo documents are recompiled by Typst on every zoom step
(`TypstRasterQualityPolicy` walks a 144→768 PPI ladder, and each PPI change re-runs
`render_project`). Would precompiling them to SVG at build time and rasterizing that
vector with resvg at runtime be faster, and stay crisp at any zoom?

**Verdict: disproved as a performance win. The premise is wrong — Typst does not
recompile on a zoom step, it re-rasterizes, and resvg re-rasterizes the same pixels
more slowly.** Quality is a genuine win over upscaling an existing bitmap, but it is
not a win over the current path, which is already pixel-exact by definition.

## Method

`native/poc-vector-bench` (workspace member, bin, never shipped). It links
`typst-dotnet-native` by path and drives `engine::Renderer::render_project` directly.
The only change to the shipping crate is `mod engine` → `pub mod engine` in
`native/typst-dotnet-native/src/lib.rs`. No ABI, behavior, or .NET change.

Four phases per document:

| Phase | What it measures |
| --- | --- |
| A | Current runtime path: `render_project` → `Rgba` at 144 / 288 / 576 PPI, compile and export split |
| B | Proposed build-time step: `render_project` → `Svg`, one file per page, plus raw and gzip size |
| C | Proposed runtime path: `usvg::Tree::from_data` (measured separately, cacheable) + `resvg::render` at 1x/2x/4x/8x |
| D | "Instant bitmap" baseline: memcpy a pre-rendered 144 PPI RGBA page + bilinear upscale to the target size |

- Release build only (`cargo build --release`, `lto = "thin"`, `codegen-units = 1`).
- `std::time::Instant`, 7 iterations per measurement; the first is reported as `cold`,
  min and median are taken over the remaining 6.
- `PackageResolution::EmbeddedOnly` — the strictest offline mode. All three documents
  compiled offline; none needed the network or a package cache.
- Phase 0 runs once per document on a fresh `Renderer` before anything else touches
  that document in the process, so its compile time is the only genuinely cold one.

Pixel mapping: Typst page size is pt (72 dpi); `typst_svg` writes `width="…pt"`, and
usvg converts to px at its default 96 dpi, so `tree.size() == page_pt * 96/72`. The
resvg scale for a target PPI is therefore `ppi / 96`, i.e. exactly the 1x/2x/4x/8x
zoom factor. **All 12 document×zoom combinations produced pixel dimensions exactly
equal to phase A's ground truth**, so no overlapping-region fallback was needed.

Quality comparison: `typst-render` and `resvg` both return premultiplied RGBA8. Both
sides are flattened to straight RGB over opaque white (`out = premul + (255 - a)`)
before comparison, which also neutralizes any page-fill difference (`serial-protocol`
uses `fill: rgb("#f8fafc")`). PSNR is over RGB; "% px delta>8" counts pixels where any
channel differs by more than 8/255.

### Machine and toolchain

- AMD Ryzen 9 7950X3D, 16C/32T, 128 GB RAM, Windows 11 Pro 10.0.26200.
- rustc 1.92.0 (ded5c06cf 2025-12-08), cargo 1.92.0, MSVC target.
- typst 0.14.2, typst-render/typst-svg 0.14.2, resvg/usvg 0.45.1, tiny-skia 0.11.4.
- resvg 0.45.1 and usvg 0.45.1 were **already in `Cargo.lock` before this PoC**
  (`typst-render → resvg`, `typst-library → usvg`), so the pinned versions share the
  single existing tiny-skia 0.11.4 — no duplicate.
- Raw run log: `artifacts/poc/bench-run.txt`.

## A. Current runtime path — the premise does not hold

`compile` is memoized by comemo. The FFI layer keeps one `Renderer` per handle
(`RendererHandle { renderer: Mutex<Renderer> }`), so consecutive zoom steps hit the
same warm cache. A zoom step pays rasterization, not compilation.

Whole document, milliseconds, cold / min / median:

| Doc | PPI | compile | export | wall | doc Mpx | export Mpx/s |
| --- | --- | --- | --- | --- | --- | --- |
| editor-export (1p) | 144 | 3.82 / 0.27 / 0.28 | 0.24 / 0.14 / 0.15 | 4.11 / 0.45 / 0.47 | 0.13 | 901 |
| | 288 | 0.29 / 0.25 / 0.26 | 0.62 / 0.51 / 0.51 | 0.95 / 0.80 / 0.81 | 0.53 | 1043 |
| | 576 | 0.29 / 0.25 / 0.26 | 1.78 / 1.61 / 1.68 | 2.12 / 1.92 / 1.97 | 2.13 | 1263 |
| serial-protocol (9p) | 144 | 1.77 / 0.76 / 1.02 | 24.88 / 22.19 / 23.36 | 27.76 / 24.76 / 25.96 | 18.05 | 773 |
| | 288 | 0.92 / 0.85 / 1.01 | 93.50 / 75.58 / 78.48 | 95.59 / 77.74 / 80.75 | 72.17 | 920 |
| | 576 | 2.06 / 0.74 / 0.82 | 308.03 / 281.85 / 287.64 | 312.99 / 284.03 / 291.82 | 288.65 | 1004 |
| korean-business-forms (2p) | 144 | 1.49 / 0.54 / 0.77 | 6.60 / 4.68 / 5.39 | 8.68 / 6.07 / 6.98 | 4.01 | 745 |
| | 288 | 1.27 / 0.51 / 0.55 | 21.96 / 14.75 / 16.38 | 23.90 / 15.90 / 17.99 | 16.04 | 979 |
| | 576 | 0.67 / 0.53 / 0.57 | 66.28 / 52.54 / 56.27 | 67.65 / 55.77 / 59.56 | 64.14 | 1140 |

Truly cold compile, once per process on a fresh `Renderer` (phase 0):

| Doc | engine init | cold compile | cold export @144 | wall |
| --- | --- | --- | --- | --- |
| editor-export | 16.4 | 25.0 | 0.4 | 25.5 |
| serial-protocol | 12.7 | 22.1 | 32.6 | 55.9 |
| korean-business-forms | 15.0 | 10.8 | 11.1 | 22.5 |

So compile costs 10–25 ms **once**, then 0.26–1.0 ms on every later zoom step —
0.3 % to 4 % of the step. Precompiling to SVG can only ever remove that 0.26–1.0 ms.

## B. Build-time SVG export

Whole document, milliseconds, cold / min / median:

| Doc | compile | export | wall |
| --- | --- | --- | --- |
| editor-export | 0.28 / 0.21 / 0.23 | 0.28 / 0.03 / 0.03 | 0.60 / 0.28 / 0.29 |
| serial-protocol | 0.84 / 0.63 / 0.70 | 13.96 / 9.68 / 9.95 | 16.06 / 11.22 / 11.53 |
| korean-business-forms | 0.64 / 0.51 / 0.55 | 6.73 / 3.50 / 3.76 | 8.27 / 4.44 / 4.78 |

The build-time step is cheap. Adding the one-off cold compile, a full precompile is
~26 ms, ~34 ms and ~15 ms respectively.

### Asset size — this is the real cost

| Doc | pages | .typ source | all sources | SVG raw | SVG gzip | SVG/typ | gzip/typ |
| --- | --- | --- | --- | --- | --- | --- | --- |
| editor-export | 1 | 1,023 B | 1,023 B | 15,401 B | 4,361 B | 15.1x | 4.3x |
| serial-protocol | 9 | 29,355 B | 29,355 B | 2,173,515 B | 403,513 B | 74.0x | 13.8x |
| korean-business-forms | 2 | 11,000 B | 105,819 B | 1,090,634 B | 280,292 B | 99.2x | 25.5x |

Per-page SVG for `serial-protocol` ranges 199 KB – 406 KB raw (35 KB – 82 KB gzip).
Shipping all three as embedded resources costs **3.28 MB raw / 0.67 MB gzip**, versus
136 KB of source today. Text is stored as outlined `<path>` geometry in `<defs>`, so
the SVG carries a copy of every glyph shape used.

## SVG self-containment

Occurrence counts in the exported page 1:

| Doc | `@font-face` | `font-family` | `<text` | `<image` | `<use` | `<path` | `data:image/svg+xml` |
| --- | --- | --- | --- | --- | --- | --- | --- |
| editor-export | 0 | 0 | 0 | 0 | 10 | 16 | 0 |
| serial-protocol | 0 | 0 | 0 | 0 | 864 | 487 | 0 |
| korean-business-forms | 0 | 0 | 0 | 2 | 773 | 405 | 2 |

**resvg does not need a fontdb.** `typst_svg` outlines every glyph to a `<path>` in
`<defs>` and instantiates it with `<use>`; there is no `<text>`, no `font-family`, and
no `@font-face` anywhere. The bench still loads
`native/typst-dotnet-native/assets/fonts` into `usvg::Options` (1 face, 0.18 ms once)
purely to prove it changes nothing. Not a productization risk.

## C. Precompiled-vector runtime path

`usvg::Tree::from_data`, median ms per page (cacheable, paid once per page):

| Doc | per-page parse (median) | document total |
| --- | --- | --- |
| editor-export | 0.124 | 0.12 |
| serial-protocol | 2.63 – 5.95 | 28.07 |
| korean-business-forms | 6.41, 7.38 | 13.78 |

`resvg::render` (pixmap alloc + render), median ms, whole document:

| Doc | 1x (96) | 2x (192) | 4x (384) | 8x (768) |
| --- | --- | --- | --- | --- |
| editor-export | 0.14 | 0.30 | 1.15 | 2.96 |
| serial-protocol | 35.24 | 80.52 | 222.41 | 684.04 |
| korean-business-forms | 32.50 | 106.51 | 399.13 | 1499.19 |

Per-page throughput at 8x: `editor-export` 1276 Mpx/s, `serial-protocol` 565–1037
Mpx/s, `korean-business-forms` 67–89 Mpx/s. `typst-render` sustains 745–1264 Mpx/s on
the same content.

### Why korean-business-forms collapses

Removing the two `<image>` elements from the page-1 SVG and re-rendering at 8x
(`poc-vector-bench probe 8 …`, log in `artifacts/poc/probe-attribution.txt`):

| Variant | raster median ms | Mpx/s |
| --- | --- | --- |
| korean-business-forms page 1, as exported | 589.38 | 96.8 |
| same, `<image>` elements stripped | **62.00** | 919.7 |
| same, `<use>` (all glyphs) stripped | 576.00 | 99.0 |
| serial-protocol page 1, as exported | 81.32 | 701.2 |
| same, `<use>` stripped | 55.48 | 1027.9 |

Two `<image>` elements account for **527 ms of 589 ms**. They are tiny —
29.4 pt × 31.2 pt and 22.1 pt × 22.7 pt — and rendering both source SVGs standalone at
8x of their natural size costs only 17.0 ms and 39.8 ms. So the cost is not the artwork.

The `<image>` surcharge scales with *page* area, not image area: it is ~580 ms at 8x
and ~140 ms at 4x (page render 642.05 vs 155.49 ms, minus the image-free cost at each
scale), a 4.1x ratio matching the 4x canvas-area ratio. That is consistent with resvg
isolating each nested-SVG `<image>` into a canvas-sized intermediate layer
(6350 × 8980 px ≈ 228 MB at 8x) and compositing it back — the exact mechanism was not
confirmed against resvg's source, but the scaling law is measured. Either way, any
Typst document containing an image pays it.

## D. Instant-bitmap baseline

From the pre-rendered 144 PPI page of the quality page, median ms:

| Doc | zoom | memcpy | bilinear | total |
| --- | --- | --- | --- | --- |
| editor-export (488×272) | 1x | 0.009 | 0.82 | 0.83 |
| | 2x | 0.011 | 3.27 | 3.29 |
| | 4x | 0.025 | 13.39 | 13.41 |
| | 8x | 0.058 | 53.61 | 53.67 |
| serial-protocol (1191×1684) | 1x | 0.976 | 14.24 | 15.22 |
| | 2x | 1.053 | 54.96 | 56.01 |
| | 4x | 0.957 | 221.95 | 222.91 |
| | 8x | 0.943 | 802.22 | 803.16 |
| korean-business-forms (1191×1684) | 1x | 0.888 | 12.44 | 13.33 |
| | 2x | 0.917 | 50.11 | 51.02 |
| | 4x | 0.950 | 202.75 | 203.70 |
| | 8x | 1.054 | 816.17 | 817.22 |

The memcpy is sub-millisecond throughout. The bilinear pass dominates, and a real GUI
would not do it on the CPU at all — it would hand the 144 PPI bitmap to the compositor
and let the GPU scale it, at effectively the memcpy cost. Treat the memcpy column as
the true floor for "display something immediately" and the total as a conservative
CPU-only ceiling.

## Quality

Page 1 of each document, C and D against A at the equivalent PPI, both flattened over
opaque white. Compared region equals the full page in every row (dimensions matched
exactly).

### editor-export (CeTZ line art)

| zoom | PPI | source | PSNR dB | max delta | % px delta>8 |
| --- | --- | --- | --- | --- | --- |
| 1x | 96 | resvg (C) | 44.73 | 51 | 0.661 % |
| 1x | 96 | bilinear (D) | 28.06 | 163 | 3.435 % |
| 2x | 192 | resvg (C) | 45.92 | 80 | 0.355 % |
| 2x | 192 | bilinear (D) | 31.16 | 126 | 3.439 % |
| 4x | 384 | resvg (C) | 49.22 | 77 | 0.179 % |
| 4x | 384 | bilinear (D) | 29.42 | 169 | 3.527 % |
| 8x | 768 | resvg (C) | **inf (bit-exact)** | 0 | 0.000 % |
| 8x | 768 | bilinear (D) | 27.73 | 204 | 3.596 % |

### serial-protocol (9 pages, Korean text)

| zoom | PPI | source | PSNR dB | max delta | % px delta>8 |
| --- | --- | --- | --- | --- | --- |
| 1x | 96 | resvg (C) | 38.81 | 55 | 3.381 % |
| 1x | 96 | bilinear (D) | 32.39 | 99 | 6.559 % |
| 2x | 192 | resvg (C) | 40.97 | 86 | 1.890 % |
| 2x | 192 | bilinear (D) | 26.97 | 201 | 7.974 % |
| 4x | 384 | resvg (C) | 43.41 | 101 | 0.964 % |
| 4x | 384 | bilinear (D) | 26.46 | 185 | 8.296 % |
| 8x | 768 | resvg (C) | 62.06 | 60 | 0.010 % |
| 8x | 768 | bilinear (D) | 25.39 | 216 | 8.615 % |

### korean-business-forms (embedded SVG images)

| zoom | PPI | source | PSNR dB | max delta | % px delta>8 |
| --- | --- | --- | --- | --- | --- |
| 1x | 96 | resvg (C) | 38.12 | 190 | 1.609 % |
| 1x | 96 | bilinear (D) | 30.90 | 209 | 5.475 % |
| 2x | 192 | resvg (C) | 40.07 | 201 | 0.970 % |
| 2x | 192 | bilinear (D) | 27.36 | 219 | 6.460 % |
| 4x | 384 | resvg (C) | 41.72 | 175 | 0.503 % |
| 4x | 384 | bilinear (D) | 26.97 | 219 | 6.853 % |
| 8x | 768 | resvg (C) | 44.22 | 187 | 0.263 % |
| 8x | 768 | bilinear (D) | 26.11 | 233 | 7.009 % |

resvg converges on Typst's own raster as resolution rises: differences are confined to
antialias edges, and their share falls monotonically (3.38 % → 0.010 % for
`serial-protocol`). `editor-export` is bit-identical at 8x. Max per-channel delta stays
high in `korean-business-forms` (175–201) even at 8x because the seal artwork has
thousands of near-vertical hairline edges where a sub-pixel coverage difference flips a
saturated red pixel; the affected fraction is 0.263 %.

The bilinear baseline never improves — its error share *grows* with zoom (6.6 % → 8.6 %)
because it is resampling a fixed 144 PPI source.

### Embedded images stay vector

`korean-business-forms` embeds two SVG files. `typst_svg` writes them back out as
`<image … xlink:href="data:image/svg+xml;base64,…">` — vector preserved, not
rasterized. The 8x seal crop from resvg is indistinguishable from ground truth; the
bilinear crop is visibly soft. **There is no raster image in any of the three documents**,
so "do embedded raster images blur at 8x" could not be answered from this corpus. A raster
`<image>` would necessarily blur, since Typst embeds the original pixels either way.

## PNG artifacts

All in `artifacts/poc/` (gitignored). `<doc>` ∈ {`editor-export`, `serial-protocol`,
`korean-business-forms`}; page 1 of each.

Full pages at 1x and 2x:

- `<doc>-1x-a-groundtruth.png`, `<doc>-1x-c-resvg.png`, `<doc>-1x-d-bilinear.png`
- `<doc>-2x-a-groundtruth.png`, `<doc>-2x-c-resvg.png`, `<doc>-2x-d-bilinear.png`

400×400 crops at 4x and 8x, each in `-a-groundtruth` / `-c-resvg` / `-d-bilinear`:

- `editor-export-crop-cetz-thin-lines-{4,8}x-*.png` — 1.5–2.5 pt CeTZ strokes
- `serial-protocol-crop-korean-text-{4,8}x-*.png` — 11 pt justified Korean body copy
- `serial-protocol-crop-thin-lines-{4,8}x-*.png` — hairline table rules
- `korean-business-forms-crop-korean-text-{4,8}x-*.png` — 7.4 pt Korean item rows
- `korean-business-forms-crop-embedded-svg-seal-{4,8}x-*.png` — embedded seal image
- `korean-business-forms-crop-embedded-svg-logo-{4,8}x-*.png` — embedded logo image

Also: `<doc>-page1.svg` (the exported page), `bench-run.txt` (full log),
`probe-attribution.txt` (image-cost attribution).

## Binary size if resvg were linked into the shipping cdylib

Measured, not estimated. Release `typst_dotnet_native.dll`, `strip = true`:

| Build | bytes |
| --- | --- |
| Current | 51,230,208 |
| Temporarily + `resvg`/`usvg`/`tiny-skia` with an exported function that calls `usvg::Tree::from_data` and `resvg::render` | 51,241,472 |
| Delta | **+11,264 B (+11 KB, +0.02 %)** |

Near-zero, because `typst-render` already depends on resvg and `typst-library` on usvg
— the code is already linked. The temporary change was reverted; the dll rebuilt to
exactly 51,230,208 bytes. **Binary size is not an argument against this proposal.**

## Verdict

**(a) ms saved per zoom step vs compile-every-time.** Between 0.26 ms and 1.0 ms
(median warm compile), because comemo already memoizes the compile across zoom steps
on a live `Renderer` handle. Precompiling removes that and adds a slower rasterizer:

| Doc | zoom | A wall (nearest ladder PPI) | C raster | net |
| --- | --- | --- | --- | --- |
| editor-export | 4x | 0.81 (@288) | 1.15 | **+0.34 ms slower** |
| | 8x | 1.97 (@576) | 2.96 | **+0.99 ms slower** |
| serial-protocol | 4x | 80.75 (@288) | 222.41 | **+141.7 ms slower** |
| | 8x | 291.82 (@576) | 684.04 | **+392.2 ms slower** |
| korean-business-forms | 4x | 17.99 (@288) | 399.13 | **+381.1 ms slower** |
| | 8x | 59.56 (@576) | 1499.19 | **+1439.6 ms slower** |

C is compared at a *higher* PPI than A in every row (384 vs 288, 768 vs 576), which
flatters A. Normalizing to Mpx/s removes that: `typst-render` runs 745–1264 Mpx/s
across all three documents; resvg runs 565–1276 Mpx/s on image-free pages and 67–89
Mpx/s on `korean-business-forms`. **The saving is negative in every case.** The only
real saving is the one-off cold compile (10–25 ms), paid once per process per document,
which a first-render warm-up would also remove.

**(b) How much slower than showing an already-rasterized bitmap.** At the memcpy floor
(what a GPU compositor costs), vector raster is 0.14–2.96 ms slower for
`editor-export`, 34–683 ms slower for `serial-protocol`, and 32–1498 ms slower for
`korean-business-forms`. Against the CPU-bilinear ceiling, resvg is *faster* than
bilinear at 8x for `serial-protocol` (684 vs 803 ms) but far slower for
`korean-business-forms` (1499 vs 817 ms). Either way the numbers are hundreds of
milliseconds at 4x and above, so **progressive display is required** — show the
existing bitmap scaled immediately, then swap in the sharp raster. That is true of the
current path too: A itself costs 292 ms at 576 PPI for `serial-protocol`.

**(c) Does zoom quality match ground truth at 1x/2x/4x/8x.** Yes, and it improves with
zoom. Pixel dimensions match exactly at all 12 combinations. PSNR rises from ~38 dB at
1x to 44–62 dB at 8x, and `editor-export` is bit-exact at 8x. Where it does not match:
antialias edges only (0.010 %–0.263 % of pixels at 8x), plus a high max-channel delta on
the `korean-business-forms` seal artwork. But this is quality parity with a path that is
already exact — there is no quality *gain* over the status quo, only over the D baseline.

**Recommendation: do not productize.** Precompiling to SVG buys a sub-millisecond
compile saving, costs 3.28 MB of embedded resources, and makes every zoom step slower —
catastrophically so for documents containing images. If zoom latency is the actual
problem, the leverage is in (i) rendering only visible pages instead of the whole
document (A renders all 9 `serial-protocol` pages on every step —
`TypstVisiblePageSelector` already exists on the .NET side but the native call does
not take a page range), and (ii) progressive display from the existing bitmap.

## Surprises

- comemo makes warm compile 0.26–1.0 ms. The "recompiled on every zoom step" premise is
  a misreading: the compile is cached, the rasterization is not.
- resvg was already a transitive dependency of the shipping cdylib. The binary-size
  concern that motivated keeping it out of `typst-dotnet-native` does not apply.
- Two 30 pt embedded images inflate an 8x page render from 62 ms to 589 ms in resvg.
- `typst_svg` outlines all glyphs, so no font setup is needed — but that is also why the
  SVG is 74–99x the source size.
- `render_project` rasterizes every page at the requested PPI, so an 8x view of
  `serial-protocol` materializes 9 × 228 MB of RGBA regardless of what is on screen.

---

# Embedded targets

The verdict above answers "which path is faster at an 8x zoom on a 32-thread desktop".
An embedded panel asks a different question: what fits in flash, what fits in 512 MB,
and what one page costs on one slow core at the panel's *one* fixed scale. This section
re-runs the comparison under those constraints.

**The desktop verdict flips.** Not on rasterizer speed — that survives every embedded
test unchanged — but on **flash and peak RAM**, at which point the winner is not the
vector path either. It is a third option the desktop bench never considered: precompile
to raster at the panel's exact scale and ship no renderer at all.

## Method (embedded)

`poc-vector-bench emb <mode>`, a second entry point in the same non-shipping crate. No
further change to `typst-dotnet-native`. Each mode is a **separate process** so peak RSS
is attributable to exactly one approach.

| Mode | What it measures |
| --- | --- |
| `prepare` | Build-time step: exports per-page SVG, and per-page PNG / gray PNG / mono1 / RGB565 at each panel scale. Prints flash cost. |
| `typst <doc> <panel> <n>` | Current path: `Renderer::new` + `render_project` → RGBA at the panel PPI |
| `resvg <doc> <panel> <n>` | Precompiled vector: `usvg` parse + `resvg::render`, **no Typst engine in the process** |
| `raster <doc> <panel> <n>` | Precompiled raster: PNG decode, and raw RGB565 read, **no renderer in the process** |
| `page <doc> <panel> <n>` | Same, first page only — a panel shows one page |
| `ladder <doc> <panel>` | RSS across a 1x/2x/4x/1x zoom ladder in one process, Typst path |

- Peak/current RSS and page-fault count come from `K32GetProcessMemoryInfo` on the
  process itself; core count from `GetProcessAffinityMask`, printed on every line so a
  pinned run cannot be mistaken for a free one.
- Single-core runs use `start /affinity 1`; every such line reports `cores available 1`.
- 7 iterations, first reported as `cold`, min/median over the remaining 6 — same as above.
- Raw logs: `artifacts/poc/emb-prepare.txt`, `emb-run.txt`, `emb-raster.txt`,
  `emb-faults.txt`. Driver: `artifacts/poc/emb-run.sh`.

**Panel scale.** A doc reader fits the page to the panel width, so
`ppi = 96 * panel_width / css_page_width`. Measured: the two A4 documents land at
**96.7 PPI on an 800x480 panel and 232.1 PPI on 1920x1080**. `editor-export` has a
smaller page (244 pt) so it lands at 236.3 / 567.1 PPI. **The desktop bench's 576 and
768 PPI rows describe no panel that exists** — on a fixed-resolution panel the
interesting scale is ~1x, and that is where these numbers are taken.

### What is measured here vs extrapolated

Measured on the same 7950X3D/Windows box as the rest of this document. **No ARM
hardware was available**, so every absolute on-device number below is an extrapolation
with the factor stated inline. File reads in the `raster` mode hit the OS page cache;
flash I/O is accounted separately from size and bandwidth, never measured.

## 1. Footprint

### Correction to the premise

The 4.64 MB `NotoSansKR-Regular.otf` and the 774 KB of CeTZ/oxifmt assets (incl. the
343 KB `cetz_core.wasm`) are **already inside** the 51,230,208 B cdylib — `engine.rs`
uses `include_bytes!` for the font and `build.rs` generates `include_bytes!` for every
package file. They are not additive. The device pays 51,230,208 B, not 56.5 MB.

### A runtime that can only rasterize precompiled SVG

Measured, not estimated. A throwaway `poc-resvg-lib` cdylib exporting one
`usvg::Tree::from_data` + `resvg::render` function, same release profile
(`lto = "thin"`, `codegen-units = 1`, `strip = true`):

| cdylib | bytes | vs shipping |
| --- | --- | --- |
| `typst_dotnet_native` (shipping) | 51,230,208 | — |
| resvg + usvg + tiny-skia, default features | 2,747,904 | **-48.48 MB (-94.6 %)** |
| same, `default-features = false` (no text, no font db, no raster codecs) | **1,229,824** | **-50.00 MB (-97.6 %)** |

The feature-less build is legitimate for this corpus and was verified, not assumed:
rendering all three page-1 SVGs through both builds produced **byte-identical output**
(equal ink-pixel count and equal pixmap checksum). That is the same fact the
"SVG self-containment" section above established — `typst_svg` outlines every glyph, so
no font machinery is reachable. The crate was deleted afterwards and the shipping dll
rebuilt to exactly 51,230,208 bytes.

### Flash cost per approach

Measured asset sizes, all three documents, PNG at `Compression::Best`:

| Assets | 800x480 panel | 1920x1080 panel |
| --- | --- | --- |
| `.typ` + `.svg` sources (today) | 136,197 | 136,197 |
| SVG raw (scale-free) | 3,279,550 | 3,279,550 |
| SVG gzip (scale-free) | 688,166 | 688,166 |
| PNG, RGB | 1,236,662 | 3,407,731 |
| PNG, grayscale | 547,309 | 1,560,790 |
| mono1 raw (1 bpp) | 1,289,247 | 7,423,553 |
| mono1 gzip | **67,215** | 248,791 |
| RGB565 raw | 20,621,694 | 118,704,264 |
| RGB565 gzip | 711,981 | 2,091,467 |
| RGBA8 raw | 41,243,388 | 237,408,528 |

Totals on flash, code **plus** assets:

| Approach | Code | Assets (800 panel) | Total | vs today |
| --- | --- | --- | --- | --- |
| Runtime Typst (today) | 51,230,208 | 136,197 | **51,366,405** | 1.0x |
| Precompiled vector | 1,229,824 | 688,166 (gzip SVG) | **1,917,990** | **26.8x smaller** |
| Precompiled raster, RGB PNG | ~0 † | 1,236,662 | **~1,236,662** | **41.5x smaller** |
| Precompiled raster, gray PNG | ~0 † | 547,309 | **~547,309** | 94x smaller |
| Precompiled raster, mono1 gzip | ~50 KB (inflate) | 67,215 | **~117,000** | **439x smaller** |
| Precompiled raster, RGB565 gzip | ~50 KB (inflate) | 711,981 | ~762,000 | 67x smaller |

† assumes the UI stack already carries a PNG decoder. Under Avalonia it does
(SkiaSharp); on a bare framebuffer budget ~100–300 KB for libpng+zlib, or ship RGB565
and carry no decoder at all.

At 1920x1080 the ordering shifts: PNG grows to 3,407,731 B and the scale-free vector
bundle (1,917,990 B) becomes the smaller of the two. **Flash alone does not decide
between vector and raster; it only decides, decisively, against shipping Typst.**

## 2. Peak RAM

`render_project` materializes every page as RGBA at the requested PPI and accumulates
all of them in `Vec<Artifact>` before returning. Measured peak RSS, whole process:

| Path | serial-protocol @ 800 panel (96.7 PPI, 9p, 8.14 Mpx) | @ 1920 panel (232.1 PPI, 46.87 Mpx) |
| --- | --- | --- |
| Runtime Typst | **90.5 MB** | **240.5 MB** |
| Precompiled vector (resvg) | 23.4 MB | 39.7 MB |
| Precompiled raster (PNG) | 10.6 MB | 22.9 MB |

The peak follows a linear law, fitted to the three measured points above and the ladder
run: **peak ≈ 4 bytes x doc Mpx + 60 MiB**, where the offset is the 30 MiB engine
baseline plus the transient second copy of the page being handed out. It reproduces all
three measurements to within 1 %. All figures below are MiB/GiB.

| Requested PPI (serial-protocol, 9 pages) | doc Mpx | peak | 512 MB device | 1 GB device |
| --- | --- | --- | --- | --- |
| 96.7 (800 panel, 1x) | 8.14 | 90.5 MiB (measured) | fits | fits |
| 232.1 (1920 panel, 1x) | 46.87 | 240.5 MiB (measured) | tight | fits |
| 386.9 (4x of 800 panel) | 130.25 | 562.5 MiB (measured) | **no** | tight |
| 576 (desktop ladder step) | 288.65 | **~1.13 GiB** (predicted) | **no** | **no** |
| 768 (desktop ladder top) | 513.2 | ~1.97 GiB (predicted) | **no** | **no** |
| 928.4 (4x of 1920 panel) | 749.9 | ~2.85 GiB (predicted) | **no** | **no** |

**The current path cannot render `serial-protocol` above ~2x of a 1080p panel scale on
a 512 MB device, and cannot reach the existing 576/768 PPI ladder rungs on a 1 GB
device.** That is before .NET: a headless .NET 8 process adds roughly 30–60 MB and an
Avalonia/Skia one 80–150 MB (extrapolated from published figures, not measured here).

Two things make this worse than it looks and one makes it better:

- The FFI hands out a raw pointer into the Rust `Vec` (`ffi.rs:605`) and the managed
  side copies it with `Marshal.Copy` into a `byte[]` (`TypstRenderer.cs:159`), so the
  page is **transiently resident twice**, plus a per-framework conversion buffer.
- **comemo retains memory across zoom steps and never gives it back.** Measured on the
  1x→2x→4x→1x ladder (`cache_generations = 10`), resident set *after* each step:
  `serial-protocol` 55.2 → 57.7 → 65.6 → 65.6 MB; `korean-business-forms` 43.4 → 44.3 →
  46.2 → 46.3 MB; `editor-export` flat at 39.7 MB. So ~10 MB of permanent growth on the
  9-page document, on top of a 30.0 MB engine baseline.
- The vector path's RAM advantage is **not** a property of resvg. It comes from
  streaming one page pixmap at a time and dropping it, which the Typst path could do
  too if the native call took a page range. Attribute it to the loop, not the library.

## 3. CPU — is typst-render's win just rayon?

**No.** `typst-render` does not depend on rayon (`Cargo.lock:2797`); rayon reaches the
build only through `typst-library`/`typst-utils` (layout) and `krilla` (PDF). The
export loop in `engine.rs:242` is a plain serial `for` over pages. Confirmed by
measurement — median ms, 32 threads vs pinned to one core:

| Measurement | 32 threads | 1 core | ratio |
| --- | --- | --- | --- |
| serial-protocol **cold compile** | 20.98 | 44.33 | **2.11x** |
| serial-protocol typst export @96.7 | 13.09 | 12.69 | 0.97x |
| serial-protocol resvg raster @96.7 | 39.12 | 39.02 | 1.00x |
| serial-protocol typst export @232.1 | 55.99 | 52.03 | 0.93x |
| serial-protocol resvg raster @232.1 | 107.54 | 107.53 | 1.00x |
| korean-business-forms typst export @232.1 | 10.84 | 9.83 | 0.91x |
| korean-business-forms resvg raster @232.1 | 148.26 | 153.38 | 1.03x |

Only Typst's **compile** is multithreaded, and it is the one thing comemo already
memoizes away. Rasterization is single-threaded on both sides; pinned runs land within
noise of free ones (occasionally faster, from less scheduler migration). **The
typst-render : resvg ratio is unchanged on one core, so every desktop conclusion about
rasterizer speed transfers to a 1-core SBC intact.**

Whole-document medians, **pinned to one core**, at the panel's own scale:

| Doc | Panel | Mpx | typst export | resvg raster | resvg parse | PNG decode | RGB565 read |
| --- | --- | --- | --- | --- | --- | --- | --- |
| editor-export (1p) | 800 | 0.36 | 0.40 | 0.60 | 0.17 | 0.66 | 0.17 |
| serial-protocol (9p) | 800 | 8.14 | **12.69** | 39.02 | 35.13 | 15.61 | 3.30 |
| korean-business-forms (2p) | 800 | 1.81 | **3.09** | 35.11 | 16.21 | 3.34 | 0.70 |
| editor-export (1p) | 1920 | 2.06 | 1.71 | 2.03 | 0.16 | 2.58 | 0.79 |
| serial-protocol (9p) | 1920 | 46.87 | **52.03** | 107.53 | 34.83 | 64.04 | 20.00 |
| korean-business-forms (2p) | 1920 | 10.42 | **9.83** | 153.38 | 15.84 | 13.94 | 3.65 |

`typst-render` is fastest of the three in every row at 1920 and every row but the
trivial one at 800. Two findings worth pulling out:

- **The `<image>` penalty is not a high-zoom artifact.** `korean-business-forms` at 1x
  on an 800-px panel already runs resvg at 51.5 Mpx/s against typst-render's 586 Mpx/s
  — **11.4x slower** — and 15.6x slower at 1920. The desktop bench found this at 8x;
  it is just as bad at the scale a panel actually uses.
- **PNG decode is not free.** At 1920 it costs 64.04 ms for `serial-protocol` against
  typst-render's 52.03 ms. Only the uncompressed RGB565 form (20.00 ms, and that is
  mostly memcpy) is genuinely near-zero.

### Extrapolation to an ARM Cortex-A core

Both rasterizers are tiny-skia and both are SIMD-vectorized (SSE2 on x86-64, NEON on
aarch64), so the gap is IPC x clock x memory bandwidth, not vector width. **Assumed**
single-core slowdown vs one 7950X3D core: **6x** for Cortex-A76 @ 2.4 GHz (RPi 5,
i.MX95), **10x** for Cortex-A72 @ 1.5 GHz (RPi 4, i.MX8M), **15x** for Cortex-A53 @
1.2 GHz (i.MX8M Mini, AM62x). These factors are estimates, not measurements.

One page of `serial-protocol` on an 800x480 panel (0.90 Mpx), single core, ms:

| Step | desktop 1-core | A76 (x6) | A72 (x10) | A53 (x15) |
| --- | --- | --- | --- | --- |
| typst-render, 1 page | 1.41 | 8 | 14 | 21 |
| typst-render, all 9 pages (**what the API does today**) | 12.69 | 76 | 127 | 190 |
| resvg raster, page 1 | 6.00 | 36 | 60 | 90 |
| usvg parse, page 1 (one-off, then held in RAM) | 6.26 | 38 | 63 | 94 |
| PNG decode, page 1 | 1.97 | 12 | 20 | 30 |
| RGB565 read + blit, page 1 | 0.30 | 2 | 3 | 5 |

**What a zoom step actually costs on an A72-class core**, one visible page:

| Doc / panel | current path (whole doc) | current path (1 page, if the API took a range) | precompiled vector | precompiled raster |
| --- | --- | --- | --- | --- |
| serial-protocol @ 800 | ~127 ms | ~14 ms | ~60 ms | ~20 ms (PNG) / ~3 ms (565) |
| serial-protocol @ 1920 | ~520 ms | ~58 ms | ~120 ms | ~71 ms / ~22 ms |
| korean-business-forms @ 1920 | ~98 ms | ~49 ms | **~612 ms** | ~70 ms / ~18 ms |

## 4. The zoom assumption itself

On a fixed panel with a known, small set of zoom levels, the whole vector-vs-compile
debate is moot: **precompute rasters at build time for exactly those scales and render
nothing at runtime.** Evaluated against the other two, for the realistic case of a
1920x1080 panel with three zoom stops (fit-width, 2x, 4x):

| | Flash | Peak RAM | Latency / step (1x, 1 page, A72) | First paint |
| --- | --- | --- | --- | --- |
| Runtime Typst | 51.37 MB | 240 MiB @1x, **~2.85 GiB @4x** | 58 ms (1 page) / 520 ms (whole doc) | ~500 ms–1.5 s |
| Precompiled vector | **1.92 MB** (scale-free) | 40 MiB | 120 ms/page, **612 ms with images** | ~40 ms |
| Precompiled raster, RGB PNG, 3 scales | ~31 MB (extrapolated) | 23 MiB | **71 ms/page** (22 ms at 565) | **~15–30 ms** |
| Precompiled raster, gray PNG, 3 scales | ~14 MB (extrapolated) | 23 MiB | ~71 ms/page | ~15–30 ms |
| Precompiled raster, mono1 gzip, 3 scales | **~2.3 MB** (extrapolated) | 12 MiB | ~5 ms/page | ~10 ms |

**The 3-scale flash figures are extrapolated and this is where raster's flash advantage
runs out.** PNG grew 2.76x for the measured 2.4x linear scale-up (800 → 1920), so each
further doubling costs ~2.4x: 3.41 → ~8.2 → ~19.6 MB, total ~31 MB. At three full-colour
stops on a 1080p panel, **precompiled raster stops being the small option and the
scale-free vector bundle (1.92 MB) wins on flash** — while still losing on latency and
RAM. Only the grayscale and mono1 forms keep raster small across many scales.

So the flash crossover is concrete: **raster wins outright up to about two colour scales
at 1080p; past that, either drop to grayscale/mono1 raster or accept vector.** Note that
one scale is the normal case — a fixed panel with fit-width and nothing else.

**Vector is genuinely required only when the (document, scale) set is not finite and
known at build time:**

- continuous or unbounded zoom (a pinch gesture, not a stepped ladder);
- unknown DPI — one firmware image across several panel resolutions, where you cannot
  enumerate the scales;
- user-supplied or field-updated documents — but note that this case usually needs the
  *compiler*, not just the rasterizer, and so falls back to runtime Typst.

Even then, this corpus argues against SVG specifically: any document containing an
`<image>` costs resvg 11–16x typst-render at panel scale.

## 5. Startup

Measured on the desktop:

| Step | ms | Notes |
| --- | --- | --- |
| `LoadLibraryW` of the 51,230,208 B dll (warm) | 6.70 | working-set delta **1.06 MB** — the dll is mapped, not read |
| `GetProcAddress` | 1.00 | |
| `Renderer::new` | 14.4–17.2 | RSS 7.3 → 30.0 MB, ~7,800 page faults ≈ 30 MB first-touched |
| cold compile, 1 core | 18.5 / 23.4 / 44.3 | kbf / editor-export / serial-protocol |
| cold export @ panel scale, 1 core | 8.1 / 0.6 / 20.9 | same order |

First paint of `serial-protocol` at 800x480, one core, desktop: **≈ 89 ms**
(6.7 + 1.0 + 15.8 + 44.3 + 20.9).

**The 51 MB is demand-paged, so the startup cost is not 51 MB of flash reads — it is
the ~30 MB the engine actually touches.** That is the number that matters, and it is
measured two ways that agree: RSS after `Renderer::new` is 30.0 MB, and the page-fault
count is ~7,800 x 4 KB ≈ 31 MB. Most of it is unavoidable: the 4.64 MB OTF is parsed,
`EmbeddedPackageResolver::new()` copies all 774 KB of package bytes and builds a
`Source` syntax tree for every one of the ~90 `.typ` files.

On-device estimate (**extrapolated**): CPU terms x 6–15, plus ~30 MB of first-touch
paging from flash. At 50–150 MB/s effective for scattered 4 KB reads on eMMC/UBIFS that
is **200–600 ms of pure I/O**, dominating everything else. Total first paint
**~0.5–1.5 s**.

The precompiled-raster path has no dll to page in, no font to parse and no compile: it
reads one 184,416 B PNG (page 1 of `serial-protocol` at the 800 panel scale) and decodes
it in 1.97 ms. On-device **~15–30 ms**. **Removing the renderer changes first paint by
roughly 20–50x, and the mechanism is paging, not arithmetic.**

## Per-tier recommendation

### Tier 1 — MCU, no .NET runtime (STM32/ESP32 class)

**This SDK is not applicable, and not by a small margin.** It needs a .NET 8 CoreCLR, a
51 MB shared library and a 90 MB peak working set for a 9-page A4 document at 1x. An
STM32H7 has 1–2 MB of SRAM; an ESP32-S3 has 512 KB plus up to 8 MB of PSRAM. Even the
stripped 1.23 MB resvg-only library is out of reach: the resvg process floor measured
8.2 MB before any page is rendered, and an 800x480 RGBA scratch buffer is another
1.5 MB. usvg and resvg both require `std` and a multi-MB heap.

**Correct architecture: precompute everything on the host; the device does not
rasterize.**

| Ship | When | Measured flash (3 docs, 800x480) | Renders it on-device |
| --- | --- | --- | --- |
| **mono1 (1 bpp), RLE/gzip** | default for text/form documents | **67,215 B** | display driver / LVGL `lv_img`, or DMA straight to the panel |
| **RGB565, gzip** | color needed | 711,981 B | same; decompress per page into a line buffer |
| **Custom binary display list** | line art only, and only if it must scale on-device | not measured | MCU 2D engine (STM32 Chrom-ART / NeoChrom) or a small scanline rasterizer |
| SVG | **never** | 3,279,550 B raw | would need an XML parser plus a heap-hungry vector rasterizer |

On the display list: extract the CeTZ path geometry at build time into a compact op
list (moveto/lineto/cubic/stroke/fill, fixed-point coordinates). **This is viable for
line art only.** `typst_svg` outlines every glyph to a `<path>` — page 1 of
`serial-protocol` carries 864 `<use>` and 487 `<path>` elements — so a text page becomes
thousands of filled paths and a display list buys nothing. For text, ship a font plus a
positioned glyph list (glyph id + x/y) and let the MCU font renderer draw it; that is
LVGL's native model. Do not send Typst, SVG, or a rasterizer to an MCU.

### Tier 2 — ARM SBC / industrial panel, .NET 8 on Linux

**Blocker first, independent of performance:** the repo ships only
`Typst.Renderer.Native.win-x64` and `Typst.Renderer.Native.linux-x64`. There is no
`linux-arm64` runtime package and no aarch64 target in the build scripts, so **the SDK
cannot be deployed to an ARM panel today** without adding that target.

| Approach | Flash (1 scale / 3 scales) | Peak RAM (native) | Latency / zoom step (A72, 1 page) | First paint | Choose when |
| --- | --- | --- | --- | --- | --- |
| **Precompiled raster @ panel scales** | **0.55–3.41 MB** / ~14–31 MB | **11–23 MiB** | **20–71 ms** (PNG), 3–22 ms (RGB565) | **~15–30 ms** | **Default.** Panel resolution fixed, zoom stops enumerable at build time, documents shipped with firmware. |
| Precompiled vector (SVG + stripped resvg) | 1.92 MB (scale-free) | 24–40 MiB | 60–120 ms, **612 ms if the page has images** | ~40 ms | Three or more colour zoom stops at 1080p, unknown panel DPI, or continuous zoom. Image-free documents only. |
| Runtime Typst (today) | 51.37 MB | 90 MiB @1x, **1.1–2.9 GiB at the ladder top** | 58 ms/page, **520 ms whole doc** | 0.5–1.5 s | Document content is not known at build time — field-updated or user-authored `.typ`. |

**Recommendation for the stated target (on-panel help documents): precompile to raster
at the panel's fixed scales and do not ship the renderer.** Help documents are static
and their scale set is known at build time, which is exactly the condition under which
the runtime path pays 15–41x the flash, 4–8x the RAM and 20–50x the first paint for
nothing. At one scale — the normal case for a fixed panel — raster costs 3.41 MB against
51.37 MB. If three colour stops are genuinely required at 1080p, switch to grayscale or
mono1 rather than to vector; vector only wins the flash column there, and loses the
other two.

**Where the desktop verdict holds, and where it flips**

- **Holds:** resvg is not faster than typst-render, and the ratio is identical on one
  core (measured, §3). "Precompiled SVG + resvg beats runtime Typst on latency" is
  wrong on a panel too, and the `<image>` penalty is 11–16x at panel scale, not just at 8x.
- **Flips:** "do not productize precompilation" was correct for the desktop app and is
  wrong for a panel. **It flips at the flash and peak-RAM constraints, not at latency**
  — specifically at 51,230,208 B of binary for static content, and at
  `peak ≈ 4 x doc Mpx + 60 MiB`, which puts the existing 576/768 PPI ladder rungs beyond
  a 1 GB device. The correct precompiled form is **raster, not vector**; the desktop
  bench's rejection of vector was right for the wrong reason on this target.

## Follow-ups this exposes

1. **A page-range argument on the native call is worth more than either proposal.** It
   turns 127 ms into 14 ms per step on an A72-class core, and 562 MiB into ~115 MiB of
   peak RSS, for the current path, with no new assets. `TypstVisiblePageSelector`
   already exists on the .NET side. This is the single highest-leverage change for
   embedded.
2. `cache_generations = 10` retains ~10 MB indefinitely on a 9-page document. It should
   be tunable per deployment, and lower on a panel.
3. `EmbeddedPackageResolver::new()` copies 774 KB and parses ~90 `.typ` files on every
   `Renderer::new`. `Bytes::from_static` and lazy `Source` construction would cut
   measurable startup RSS and time.
4. No `linux-arm64` native package exists. Nothing embedded ships without it.
