#import "@preview/cetz:0.5.2"

#set page(width: auto, height: auto, margin: 12pt, fill: rgb("#e7ebef"))
#set text(font: "Noto Sans KR")

#cetz.canvas(length: 1cm, padding: 0.35, {
  import cetz.draw: *

  let panel = rgb("#f4f5f6")
  let panel-edge = rgb("#aeb5bc")
  let bezel = rgb("#20252b")
  let screen = rgb("#151a22")
  let glass = rgb("#252c36")
  let cyan = rgb("#71d7ff")
  let blue = rgb("#2d75ed")
  let green = rgb("#35a95f")
  let purple = rgb("#8057e8")

  let key(x, y, color) = {
    rect(
      (x - 0.91, y - 0.96),
      (x + 0.91, y + 0.90),
      radius: 6pt,
      fill: rgb("#9aa2aa"),
    )
    rect(
      (x - 0.88, y - 0.90),
      (x + 0.88, y + 0.90),
      radius: 6pt,
      fill: bezel,
      stroke: (paint: rgb("#11151a"), thickness: 1.2pt),
    )
    rect(
      (x - 0.70, y - 0.70),
      (x + 0.70, y + 0.70),
      radius: 3pt,
      fill: color,
      stroke: (paint: glass, thickness: 1pt),
    )
    line(
      (x - 0.62, y + 0.57),
      (x + 0.48, y + 0.57),
      stroke: (paint: rgb("#3b4551"), thickness: 0.7pt),
    )
  }

  // Soft shadow and the white, slightly beveled desktop enclosure.
  rect((-7.25, -4.60), (7.35, 4.42), radius: 17pt, fill: rgb("#a8afb6"))
  rect(
    (-7.35, -4.40),
    (7.35, 4.60),
    radius: 17pt,
    fill: panel,
    stroke: (paint: panel-edge, thickness: 1.4pt),
  )
  line((-6.75, -3.92), (6.75, -3.92), stroke: (paint: rgb("#d6dade"), thickness: 1pt))

  // Top mark reconstructed as a simple front-view logo.
  circle((-1.42, 3.88), radius: 0.17, fill: rgb("#a8afb5"))
  content((0.15, 3.88), text(size: 9pt, weight: 700, fill: rgb("#a8afb5"))[MONSTAR DECK])

  // 3 x 5 LCD key grid.
  key(-4.85, 2.20, screen)
  key(-2.80, 2.20, rgb("#1c3150"))
  key(-0.75, 2.20, screen)
  key(1.30, 2.20, rgb("#d99b25"))
  key(3.35, 2.20, rgb("#182334"))

  key(-4.85, 0.05, screen)
  key(-2.80, 0.05, screen)
  key(-0.75, 0.05, screen)
  key(1.30, 0.05, rgb("#2865d7"))
  key(3.35, 0.05, screen)

  key(-4.85, -2.10, rgb("#173755"))
  key(-2.80, -2.10, rgb("#30205f"))
  key(-0.75, -2.10, rgb("#17314b"))
  key(1.30, -2.10, green)
  key(3.35, -2.10, rgb("#246d49"))

  // First row screen contents.
  circle((-4.98, 2.34), radius: 0.18, fill: rgb("#ffb33b"))
  circle((-5.06, 2.13), radius: 0.22, fill: rgb("#f2f5fa"))
  circle((-4.77, 2.13), radius: 0.27, fill: rgb("#f2f5fa"))
  rect((-5.22, 1.96), (-4.54, 2.13), fill: rgb("#f2f5fa"))
  content((-4.85, 1.72), text(size: 6pt, fill: rgb("#dff5ff"))[Timeout])

  content((-2.80, 2.42), text(size: 8pt, weight: 700, fill: rgb("#f4f8ff"))[Tues.])
  rect((-3.30, 1.72), (-2.30, 1.98), radius: 2pt, fill: blue)
  content((-2.80, 1.85), text(size: 6pt, weight: 700, fill: white)[6:3])

  circle((1.30, 2.20), radius: 0.40, fill: rgb("#e8edf3"))
  content((1.30, 2.20), text(size: 12pt, weight: 700, fill: rgb("#6f9ee8"))[›])

  content((3.35, 2.50), text(size: 6pt, weight: 700, fill: rgb("#8bea8a"))[72%])
  line((2.92, 2.27), (3.72, 2.27), stroke: (paint: green, thickness: 2pt))
  content((3.35, 1.89), text(size: 5.5pt, fill: cyan)[23.6°C])

  // Second row active screens.
  circle((1.30, 0.05), radius: 0.43, fill: rgb("#79a7ff"))
  content((1.30, 0.06), text(size: 10pt, weight: 700, fill: rgb("#edf4ff"))[×])
  rect((3.02, -0.27), (3.68, 0.22), fill: rgb("#d4cb43"))
  rect((3.02, 0.22), (3.68, 0.40), fill: rgb("#9b9638"))

  // Bottom row timers, sensor values, and navigation controls.
  content((-4.85, -1.98), text(size: 10pt, weight: 700, fill: rgb("#e7fbff"))[00:05])
  line((-5.40, -2.39), (-4.30, -2.39), stroke: (paint: green, thickness: 2pt))
  content((-4.85, -2.54), text(size: 7pt, fill: rgb("#8ff0a8"))[⏻])

  content((-2.80, -1.98), text(size: 10pt, weight: 700, fill: rgb("#eef4ff"))[00:59])
  line((-3.34, -2.39), (-2.26, -2.39), stroke: (paint: purple, thickness: 2pt))
  content((-2.80, -2.55), text(size: 7pt, fill: rgb("#c6b1ff"))[TIMER])

  content((-0.75, -1.82), text(size: 6pt, fill: cyan)[C.W])
  content((-0.75, -2.12), text(size: 6pt, fill: rgb("#eef8ff"))[235 lux])
  content((-0.75, -2.45), text(size: 7pt, weight: 700, fill: white)[550])

  circle((1.30, -2.10), radius: 0.43, fill: rgb("#dce9e1"))
  content((1.30, -2.10), text(size: 12pt, weight: 700, fill: green)[›])

  line((2.92, -2.45), (3.34, -1.82), stroke: (paint: rgb("#7ad6ec"), thickness: 4pt))
  line((3.34, -1.82), (3.78, -2.45), stroke: (paint: purple, thickness: 4pt))

  // Narrow system status display at the right of the key matrix.
  rect((4.65, -3.08), (6.05, 3.08), radius: 5pt, fill: bezel, stroke: (paint: rgb("#11151a"), thickness: 1.2pt))
  rect((4.88, -2.83), (5.82, 2.83), radius: 2pt, fill: rgb("#152033"))
  content((5.35, 2.40), text(size: 6pt, weight: 700, fill: cyan)[SYSTEM])
  content((5.35, 1.76), text(size: 6pt, fill: rgb("#8bea8a"))[CPU 72%])
  line((5.03, 1.42), (5.67, 1.42), stroke: (paint: green, thickness: 2pt))
  content((5.35, 0.78), text(size: 6pt, fill: rgb("#d5e8ff"))[23.6°C])
  content((5.35, -0.10), text(size: 6pt, fill: cyan)[MEM 55%])
  line((5.03, -0.44), (5.55, -0.44), stroke: (paint: blue, thickness: 2pt))
  content((5.35, -1.34), text(size: 6pt, fill: rgb("#d5e8ff"))[NET OK])
  content((5.35, -2.30), text(size: 6pt, fill: rgb("#8bea8a"))[ONLINE])
})
