#import "@preview/cetz:0.5.2"

#set page(width: auto, height: auto, margin: 12pt, fill: rgb("#cbd3da"))
#set text(font: "Noto Sans KR")

#cetz.canvas(length: 1cm, padding: 0.55, {
  import cetz.draw: *

  let edge = rgb("#8f979f")
  let bezel = rgb("#20252b")
  let dark = rgb("#151b23")
  let blue = rgb("#2f75e8")
  let green = rgb("#32a961")
  let purple = rgb("#7350d8")
  let cyan = rgb("#69d2fa")

  // Map the device plane into the reference photo's asymmetric trapezoid.
  // The right side is farther from the camera and therefore shorter.
  let project(point) = {
    let (x, y, z) = point
    let u = (x + 6.70) / 13.40
    let v = (y + 3.70) / 7.40
    let left-x = -5.90 + (-6.50 + 5.90) * v
    let left-y = -4.10 + (3.40 + 4.10) * v
    let right-x = 6.80 + (6.25 - 6.80) * v
    let right-y = -3.55 + (3.00 + 3.55) * v
    (
      left-x + (right-x - left-x) * u + 0.04 * z,
      left-y + (right-y - left-y) * u + 0.36 * z,
    )
  }

  let face(points, color, stroke-color: edge) = line(
    ..points.map(project),
    close: true,
    fill: color,
    stroke: if stroke-color == none { none } else { (paint: stroke-color, thickness: 1pt) },
  )

  let key(x, y, color) = {
    // Raised key front and right walls.
    face(
      ((x - 0.72, y - 0.72, 0.50), (x + 0.72, y - 0.72, 0.50),
       (x + 0.72, y - 0.72, 0.20), (x - 0.72, y - 0.72, 0.20)),
      rgb("#11161c"),
      stroke-color: rgb("#0a0d10"),
    )
    face(
      ((x + 0.72, y - 0.72, 0.50), (x + 0.72, y + 0.72, 0.50),
       (x + 0.72, y + 0.72, 0.20), (x + 0.72, y - 0.72, 0.20)),
      rgb("#303842"),
      stroke-color: rgb("#0a0d10"),
    )
    face(
      ((x - 0.72, y - 0.72, 0.51), (x + 0.72, y - 0.72, 0.51),
       (x + 0.72, y + 0.72, 0.51), (x - 0.72, y + 0.72, 0.51)),
      bezel,
      stroke-color: rgb("#0a0d10"),
    )
    face(
      ((x - 0.57, y - 0.57, 0.53), (x + 0.57, y - 0.57, 0.53),
       (x + 0.57, y + 0.57, 0.53), (x - 0.57, y + 0.57, 0.53)),
      color,
      stroke-color: rgb("#323c48"),
    )
  }

  scope({
    // Chassis thickness: front, right, and top surfaces.
    face(
      ((-6.70, -3.70, 0.42), (6.70, -3.70, 0.42),
       (6.70, -3.70, -0.62), (-6.70, -3.70, -0.62)),
      rgb("#939da6"),
    )
    face(
      ((6.70, -3.70, 0.42), (6.70, 3.70, 0.42),
       (6.70, 3.70, -0.62), (6.70, -3.70, -0.62)),
      rgb("#7f8a94"),
    )
    face(
      ((-6.70, -3.70, 0.43), (6.70, -3.70, 0.43),
       (6.70, 3.70, 0.43), (-6.70, 3.70, 0.43)),
      rgb("#f7f8f9"),
      stroke-color: rgb("#7f8992"),
    )

    // 3 x 5 raised LCD keys.
    key(-4.45, 1.55, dark)
    key(-2.65, 1.55, rgb("#1c3150"))
    key(-0.85, 1.55, dark)
    key(0.95, 1.55, rgb("#dda124"))
    key(2.75, 1.55, rgb("#17283b"))

    key(-4.45, -0.15, dark)
    key(-2.65, -0.15, dark)
    key(-0.85, -0.15, dark)
    key(0.95, -0.15, blue)
    key(2.75, -0.15, dark)

    key(-4.45, -1.85, rgb("#173755"))
    key(-2.65, -1.85, rgb("#30205f"))
    key(-0.85, -1.85, rgb("#17314b"))
    key(0.95, -1.85, green)
    key(2.75, -1.85, rgb("#246d49"))

    // Key highlights and recognizable display marks.
    circle(project((-4.45, 1.55, 0.56)), radius: 0.24, fill: rgb("#edf4fb"), stroke: none)
    circle(project((0.95, 1.55, 0.56)), radius: 0.28, fill: rgb("#edf4fb"), stroke: none)
    circle(project((0.95, -0.15, 0.56)), radius: 0.29, fill: rgb("#9ebeff"), stroke: none)
    face(
      ((2.48, -0.40, 0.56), (3.02, -0.40, 0.56),
       (3.02, 0.12, 0.56), (2.48, 0.12, 0.56)),
      rgb("#d7ce43"),
      stroke-color: none,
    )
    line(project((-4.88, -2.14, 0.56)), project((-4.02, -2.14, 0.56)), stroke: (paint: green, thickness: 2pt))
    line(project((-3.08, -2.14, 0.56)), project((-2.22, -2.14, 0.56)), stroke: (paint: purple, thickness: 2pt))
    circle(project((0.95, -1.85, 0.56)), radius: 0.28, fill: rgb("#e2eee6"), stroke: none)
    line(project((2.38, -2.20, 0.56)), project((2.72, -1.60, 0.56)), stroke: (paint: cyan, thickness: 4pt))
    line(project((2.72, -1.60, 0.56)), project((3.12, -2.20, 0.56)), stroke: (paint: purple, thickness: 4pt))

    // Raised narrow system monitor on the right.
    face(
      ((4.16, -2.38, 0.50), (5.28, -2.38, 0.50),
       (5.28, -2.38, 0.20), (4.16, -2.38, 0.20)),
      rgb("#11161c"),
      stroke-color: rgb("#0a0d10"),
    )
    face(
      ((4.16, -2.38, 0.52), (5.28, -2.38, 0.52),
       (5.28, 2.38, 0.52), (4.16, 2.38, 0.52)),
      bezel,
      stroke-color: rgb("#0a0d10"),
    )
    face(
      ((4.34, -2.20, 0.54), (5.10, -2.20, 0.54),
       (5.10, 2.20, 0.54), (4.34, 2.20, 0.54)),
      rgb("#152033"),
      stroke-color: rgb("#0e1420"),
    )
    line(project((4.46, 1.25, 0.56)), project((4.96, 1.25, 0.56)), stroke: (paint: green, thickness: 2pt))
    line(project((4.46, 0.00, 0.56)), project((4.88, 0.00, 0.56)), stroke: (paint: blue, thickness: 2pt))
    line(project((4.46, -1.25, 0.56)), project((5.00, -1.25, 0.56)), stroke: (paint: cyan, thickness: 2pt))

    // Upright micro-labels retain legibility while their positions follow the projection.
    content(project((0, 3.02, 0.48)), text(size: 8pt, weight: 700, fill: rgb("#9ea6ae"))[MONSTAR DECK])
    content(project((-4.45, -1.82, 0.58)), text(size: 7pt, weight: 700, fill: white)[00:05])
    content(project((-2.65, -1.82, 0.58)), text(size: 7pt, weight: 700, fill: white)[00:59])
    content(project((-0.85, -1.82, 0.58)), text(size: 6pt, fill: white)[550])
    content(project((4.72, 1.72, 0.58)), text(size: 5pt, fill: cyan)[CPU])
    content(project((4.72, 0.48, 0.58)), text(size: 5pt, fill: rgb("#88e78f"))[72%])
    content(project((4.72, -0.75, 0.58)), text(size: 5pt, fill: cyan)[23°C])
  })
})
