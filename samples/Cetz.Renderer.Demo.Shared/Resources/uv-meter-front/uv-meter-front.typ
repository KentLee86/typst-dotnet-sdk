#import "@preview/cetz:0.5.2"

#set page(width: auto, height: auto, margin: 12pt, fill: rgb("#e8edf2"))
#set text(font: "Noto Sans KR", fill: rgb("#172b3d"))

#cetz.canvas(length: 1cm, padding: 0.35, {
  import cetz.draw: *

  let body = rgb("#d8dee5")
  let edge = rgb("#344454")
  let lcd = rgb("#c9d7a5")
  let lcd-edge = rgb("#33443a")
  let segment-on = rgb("#16251d")
  let segment-off = rgb("#b7c596")

  let segment(a, b, on) = line(
    a,
    b,
    stroke: (
      paint: if on { segment-on } else { segment-off },
      thickness: 5pt,
    ),
  )

  let digit(offset, active) = scope({
    translate(offset)
    segment((-0.42, 0.90), (0.42, 0.90), active.contains("a"))
    segment((0.50, 0.80), (0.50, 0.10), active.contains("b"))
    segment((0.50, -0.10), (0.50, -0.80), active.contains("c"))
    segment((-0.42, -0.90), (0.42, -0.90), active.contains("d"))
    segment((-0.50, -0.10), (-0.50, -0.80), active.contains("e"))
    segment((-0.50, 0.80), (-0.50, 0.10), active.contains("f"))
    segment((-0.42, 0), (0.42, 0), active.contains("g"))
  })

  // Instrument enclosure and recessed grip details.
  rect(
    (-5.0, -5.5),
    (5.0, 5.5),
    radius: 16pt,
    fill: body,
    stroke: (paint: edge, thickness: 2pt),
  )
  line((-4.55, -4.75), (-4.55, 4.75), stroke: (paint: rgb("#b4bec8"), thickness: 1pt))
  line((4.55, -4.75), (4.55, 4.75), stroke: (paint: rgb("#b4bec8"), thickness: 1pt))

  content((0, 4.95), text(size: 15pt, weight: 700)[휴대용 UV 측정기])
  content((0, 4.48), text(size: 7pt, fill: rgb("#536474"))[UV RADIOMETER · FRONT PANEL])

  // Segmented LCD: large numeric reading plus fixed status characters.
  rect(
    (-4.05, 0.65),
    (4.05, 3.95),
    radius: 7pt,
    fill: lcd-edge,
    stroke: (paint: rgb("#202c27"), thickness: 1.5pt),
  )
  rect((-3.82, 0.88), (3.82, 3.72), radius: 4pt, fill: lcd)
  content((-3.25, 3.38), text(size: 7pt, weight: 700, fill: segment-on)[UV-A])
  content((2.95, 3.38), text(size: 7pt, fill: segment-on)[HOLD])

  digit((-2.35, 2.20), ("a", "b", "c", "d", "g"))
  digit((-1.05, 2.20), ("a", "c", "d", "e", "f", "g"))
  digit((0.25, 2.20), ("a", "c", "d", "f", "g"))
  circle((1.00, 1.32), radius: 0.09, fill: segment-on)
  digit((1.80, 2.20), ("a", "b", "c", "d", "e", "f"))
  content((3.05, 1.34), text(size: 8pt, weight: 700, fill: segment-on)[mW/cm²])

  // Five tactile front-panel buttons.
  for item in (
    (-3.20, "POWER", rgb("#ef4444")),
    (-1.60, "ZERO", rgb("#64748b")),
    (0.00, "MODE", rgb("#2563eb")),
    (1.60, "HOLD", rgb("#64748b")),
    (3.20, "LIGHT", rgb("#64748b")),
  ) {
    let (x, label, accent) = item
    circle(
      (x, -1.25),
      radius: 0.52,
      fill: rgb("#f8fafc"),
      stroke: (paint: edge, thickness: 1.5pt),
    )
    circle((x, -1.25), radius: 0.32, fill: accent)
    content((x, -2.05), text(size: 7pt, weight: 700)[#label])
  }

  rect((-3.75, -3.65), (3.75, -2.75), radius: 6pt, fill: rgb("#c7d0d9"))
  content((-2.90, -3.20), text(size: 7pt, fill: rgb("#526373"))[RANGE])
  content((-0.85, -3.20), text(size: 8pt, weight: 700)[0–20.00 mW/cm²])
  content((0, -4.55), text(size: 7pt, fill: rgb("#64748b"))[MODEL UV-365 · SENSOR INPUT])
  circle((3.55, -4.55), radius: 0.18, fill: rgb("#22c55e"), stroke: (paint: edge, thickness: 0.8pt))
})
