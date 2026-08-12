#import "@preview/cetz:0.5.2"

#set page(width: auto, height: auto, margin: 0pt, fill: rgb("#030608"))
#set text(font: "Noto Sans KR", fill: rgb("#dbe4e8"))

#cetz.canvas(length: 8.8mm, padding: 0, {
  import cetz.draw: *

  let bg = rgb("#030608")
  let white = rgb("#eef3f4")
  let muted = rgb("#b5c0c7")
  let frame = rgb("#555d5b")
  let pin = rgb("#747a77")
  let blue = rgb("#45a9e8")
  let blue-line = rgb("#337fab")
  let blue-panel = rgb("#071d2c")
  let green = rgb("#83d85d")
  let green-line = rgb("#4b9f3d")
  let green-panel = rgb("#08260f")
  let yellow = rgb("#e1cb28")

  let label(pos, body, size: 8pt, fill: white, weight: 400, anchor: "center") = {
    content(pos, text(size: size, fill: fill, weight: weight, body), anchor: anchor)
  }

  let strong-label(pos, body, size: 8pt, fill: white, anchor: "center") = {
    label(pos, body, size: size, fill: fill, weight: 700, anchor: anchor)
    label((pos.first() + 0.012, pos.at(1)), body,
      size: size, fill: fill, weight: 700, anchor: anchor)
  }

  let panel-box(a, b, color, fill) = {
    rect(a, b, radius: 3.5pt, fill: fill,
      stroke: (paint: color, thickness: 0.75pt))
  }

  let arrow-right(a, b, color, thickness: 0.8pt) = {
    line(a, b, stroke: (paint: color, thickness: thickness))
    line((b.first() - 0.15, b.at(1) + 0.10), b,
      (b.first() - 0.15, b.at(1) - 0.10), close: true, fill: color, stroke: none)
  }

  let ground(pos, color) = scope({
    translate(pos)
    line((0, 0.28), (0, 0), stroke: (paint: color, thickness: 0.8pt))
    line((-0.28, 0), (0.28, 0), stroke: (paint: color, thickness: 0.8pt))
    line((-0.19, -0.09), (0.19, -0.09), stroke: (paint: color, thickness: 0.8pt))
    line((-0.09, -0.18), (0.09, -0.18), stroke: (paint: color, thickness: 0.8pt))
  })

  let shield(pos, color) = scope({
    translate(pos)
    line((-0.28, 0.22), (0, 0.34), (0.28, 0.22), (0.24, -0.14),
      (0, -0.36), (-0.24, -0.14), close: true,
      stroke: (paint: color, thickness: 0.8pt))
  })

  let cell-array(pos, color) = scope({
    translate(pos)
    for x in (-0.38, -0.12, 0.14, 0.40) {
      for y in (-0.36, -0.12, 0.12, 0.36) {
        rect((x - 0.08, y - 0.08), (x + 0.08, y + 0.08),
          stroke: (paint: color, thickness: 0.55pt))
      }
    }
  })

  let amplifier(pos, color) = scope({
    translate(pos)
    line((-0.43, -0.42), (-0.43, 0.42), (0.40, 0), close: true,
      stroke: (paint: color, thickness: 0.8pt))
    line((0.40, 0), (0.62, 0), stroke: (paint: color, thickness: 0.8pt))
  })

  let decoder(pos, color) = scope({
    translate(pos)
    rect((-0.48, -0.30), (0.48, 0.30), stroke: (paint: color, thickness: 0.65pt))
    for x in (-0.34, -0.20, 0.30) {
      line((x, -0.30), (x, 0.30), stroke: (paint: color, thickness: 0.45pt))
    }
    for y in (-0.23, 0, 0.23) {
      circle((0.65, y), radius: 0.035, stroke: (paint: color, thickness: 0.55pt))
    }
  })

  let refresh(pos, color) = scope({
    translate(pos)
    arc((0, 0), anchor: "origin", start: 22deg, delta: 230deg, radius: 0.34,
      stroke: (paint: color, thickness: 0.75pt))
    line((0.27, 0.24), (0.42, 0.27), (0.36, 0.10), close: true,
      fill: color, stroke: none)
    arc((0, 0), anchor: "origin", start: 202deg, delta: 150deg, radius: 0.34,
      stroke: (paint: color, thickness: 0.75pt))
    line((-0.27, -0.24), (-0.42, -0.27), (-0.36, -0.10), close: true,
      fill: color, stroke: none)
  })

  let logic-chip(pos, color) = scope({
    translate(pos)
    rect((-0.25, -0.25), (0.25, 0.25), stroke: (paint: color, thickness: 0.65pt))
    rect((-0.15, -0.15), (0.15, 0.15), stroke: (paint: color, thickness: 0.55pt))
    for d in (-0.18, 0, 0.18) {
      line((-0.34, d), (-0.25, d), stroke: (paint: color, thickness: 0.55pt))
      line((0.25, d), (0.34, d), stroke: (paint: color, thickness: 0.55pt))
      line((d, -0.34), (d, -0.25), stroke: (paint: color, thickness: 0.55pt))
      line((d, 0.25), (d, 0.34), stroke: (paint: color, thickness: 0.55pt))
    }
  })

  let triangle-buffer(pos, color, scale: 1.0) = scope({
    translate(pos)
    line((-0.35 * scale, -0.35 * scale), (-0.35 * scale, 0.35 * scale),
      (0.30 * scale, 0), close: true, stroke: (paint: color, thickness: 0.75pt))
    line((-0.55 * scale, 0), (-0.35 * scale, 0), stroke: (paint: color, thickness: 0.75pt))
    line((0.30 * scale, 0), (0.50 * scale, 0), stroke: (paint: color, thickness: 0.75pt))
  })

  let receiver(pos, color) = scope({
    translate(pos)
    // Two-input NAND receiver: flat input side, round output side, inversion bubble.
    line((-0.28, -0.30), (-0.28, 0.30),
      stroke: (paint: color, thickness: 0.8pt))
    line((-0.28, -0.30), (0, -0.30), stroke: (paint: color, thickness: 0.8pt))
    line((-0.28, 0.30), (0, 0.30), stroke: (paint: color, thickness: 0.8pt))
    arc((0, 0), anchor: "origin", start: -90deg, delta: 180deg, radius: 0.30,
      stroke: (paint: color, thickness: 0.8pt))
    line((-0.28, -0.14), (-0.74, -0.14), stroke: (paint: color, thickness: 0.8pt))
    line((-0.28, 0.14), (-0.74, 0.14), stroke: (paint: color, thickness: 0.8pt))
    circle((0.37, 0), radius: 0.07, fill: green-panel,
      stroke: (paint: color, thickness: 0.7pt))
    line((0.44, 0), (0.70, 0), stroke: (paint: color, thickness: 0.8pt))
  })

  // Background and header.
  // At the renderer's default 144 PPI, these bounds produce 1130 x 742 px.
  rect((0, 0), (22.653, 14.870), fill: bg, stroke: none)
  strong-label((11.3, 14.38), "DDR3L MEMORY — INTERNAL POWER DOMAIN ARCHITECTURE",
    size: 15pt)
  content((11.3, 13.77), text(size: 9.5pt, fill: muted)[
    Both Rails Typically #text(fill: yellow, weight: 700)[1.35 V] in DDR3L
  ], anchor: "center")

  // Package shadow, pins, and body.
  rect((4.16, 2.08), (18.08, 11.63), radius: 4pt, fill: rgb("#151918"), stroke: none)
  for i in range(25) {
    let x = 4.48 + i * 0.54
    rect((x, 11.62), (x + 0.17, 12.05), fill: pin, stroke: none)
    rect((x, 1.85), (x + 0.17, 2.28), fill: pin, stroke: none)
  }
  for i in (3, 5, 7, 9, 11) {
    let x = 4.48 + i * 0.54
    rect((x, 1.85), (x + 0.17, 2.28), fill: blue-line, stroke: none)
  }
  for i in (14, 16, 18, 20, 22) {
    let x = 4.48 + i * 0.54
    rect((x, 1.85), (x + 0.17, 2.28), fill: green-line, stroke: none)
  }
  for i in range(17) {
    let y = 2.48 + i * 0.54
    rect((3.86, y), (4.18, y + 0.16), fill: pin, stroke: none)
    rect((17.98, y), (18.30, y + 0.16), fill: pin, stroke: none)
  }
  rect((4.10, 2.22), (18.05, 11.68), radius: 3pt, fill: rgb("#101716"),
    stroke: (paint: frame, thickness: 2pt))
  rect((4.34, 2.48), (11.25, 11.45), fill: blue-panel, stroke: none)
  rect((11.30, 2.48), (17.81, 11.45), fill: green-panel, stroke: none)
  line((4.35, 11.44), (11.24, 11.44), stroke: (paint: rgb("#17415a"), thickness: 0.55pt))
  line((11.31, 11.44), (17.80, 11.44), stroke: (paint: rgb("#22572a"), thickness: 0.55pt))
  line((11.27, 2.48), (11.27, 11.45), stroke: (paint: rgb("#70813f"), thickness: 0.8pt))

  // Supply brackets.
  line((6.35, 12.02), (6.35, 12.60), (10.75, 12.60), (10.75, 12.02),
    stroke: (paint: blue, thickness: 0.9pt))
  rect((6.27, 11.95), (6.44, 12.12), fill: blue, stroke: none)
  rect((10.67, 11.95), (10.84, 12.12), fill: blue, stroke: none)
  strong-label((8.55, 12.95), "VDD (1.35 V)", size: 10pt, fill: blue)
  line((12.02, 12.02), (12.02, 12.60), (16.08, 12.60), (16.08, 12.02),
    stroke: (paint: green, thickness: 0.9pt))
  rect((11.94, 11.95), (12.11, 12.12), fill: green, stroke: none)
  rect((16.00, 11.95), (16.17, 12.12), fill: green, stroke: none)
  strong-label((14.05, 12.95), "VDDQ (1.35 V)", size: 10pt, fill: green)

  // Core domain.
  label((7.80, 11.03), "CORE DOMAIN (VDD)", size: 11pt, fill: blue, weight: 700)
  label((7.80, 10.56), "Powers the internal DRAM core", size: 8pt, fill: rgb("#78bce5"))
  panel-box((4.82, 7.42), (7.52, 10.06), blue-line, rgb("#092235"))
  label((6.17, 9.54), "CELL ARRAY", size: 8.5pt)
  cell-array((6.17, 8.55), rgb("#93c9e8"))
  panel-box((7.78, 7.42), (10.72, 10.06), blue-line, rgb("#092235"))
  label((9.25, 9.65), "SENSE", size: 8.4pt)
  label((9.25, 9.26), "AMPLIFIERS", size: 8.4pt)
  amplifier((9.28, 8.30), rgb("#a7cee4"))
  panel-box((4.82, 4.80), (7.52, 7.16), blue-line, rgb("#092235"))
  label((6.17, 6.66), "ROW / COLUMN", size: 8.2pt)
  label((6.17, 6.25), "DECODERS", size: 8.2pt)
  decoder((6.17, 5.48), rgb("#a7cee4"))
  panel-box((7.78, 4.80), (10.72, 7.16), blue-line, rgb("#092235"))
  label((9.25, 6.66), "REFRESH", size: 8.2pt)
  label((9.25, 6.25), "CIRCUITRY", size: 8.2pt)
  refresh((9.25, 5.48), rgb("#a7cee4"))
  panel-box((4.82, 2.85), (10.72, 4.52), blue-line, rgb("#092235"))
  logic-chip((5.55, 3.68), rgb("#a7cee4"))
  label((8.18, 3.93), "CONTROL LOGIC", size: 8.4pt)
  label((8.18, 3.46), "(STATE MACHINES, TIMING, ETC.)", size: 6.2pt, fill: muted)

  // I/O domain.
  label((14.55, 11.03), "I/O DOMAIN (VDDQ)", size: 11pt, fill: green, weight: 700)
  label((14.55, 10.56), "Powers the high-speed I/O interface", size: 8pt, fill: rgb("#a4dc83"))
  panel-box((11.88, 7.52), (17.08, 10.06), green-line, rgb("#0b3015"))
  label((14.48, 9.57), "DQ / DQS I/O BUFFERS", size: 8.6pt)
  label((14.48, 9.18), "(READ / WRITE)", size: 7pt, fill: muted)
  triangle-buffer((13.00, 8.43), rgb("#b7ddb0"), scale: 0.95)
  arrow-right((13.78, 8.43), (14.20, 8.43), green, thickness: 0.7pt)
  line((15.10, 8.43), (14.68, 8.43), stroke: (paint: green, thickness: 0.7pt))
  line((14.83, 8.53), (14.68, 8.43), (14.83, 8.33), close: true, fill: green, stroke: none)
  triangle-buffer((15.75, 8.43), rgb("#b7ddb0"), scale: 0.95)
  line((15.75, 7.91), (15.75, 8.12), stroke: (paint: green, thickness: 0.7pt))
  circle((15.75, 8.18), radius: 0.055, fill: green-panel,
    stroke: (paint: green, thickness: 0.65pt))
  panel-box((11.88, 5.02), (17.08, 7.27), green-line, rgb("#0b3015"))
  label((14.48, 6.80), "COMMAND / ADDRESS", size: 8.5pt)
  label((14.48, 6.39), "RECEIVERS", size: 8.5pt)
  receiver((14.48, 5.62), rgb("#b7ddb0"))
  panel-box((11.88, 2.85), (17.08, 4.78), green-line, rgb("#0b3015"))
  label((14.48, 4.32), "OUTPUT DRIVERS", size: 8.5pt)
  triangle-buffer((12.85, 3.62), rgb("#b7ddb0"), scale: 0.84)
  triangle-buffer((14.10, 3.62), rgb("#b7ddb0"), scale: 0.84)
  line((13.27, 3.62), (13.64, 3.62),
    stroke: (paint: rgb("#b7ddb0"), thickness: 0.75pt))
  circle((15.00, 3.62), radius: 0.040, fill: rgb("#b7ddb0"), stroke: none)
  circle((15.20, 3.62), radius: 0.040, fill: rgb("#b7ddb0"), stroke: none)
  circle((15.40, 3.62), radius: 0.040, fill: rgb("#b7ddb0"), stroke: none)
  triangle-buffer((16.20, 3.62), rgb("#b7ddb0"), scale: 0.84)

  // Right-side signal pins and corrected signal names.
  for y in (9.46, 8.82, 8.20, 7.58, 6.42, 5.75, 5.10, 4.48, 3.92) {
    line((17.08, y), (17.52, y), stroke: (paint: green, thickness: 0.7pt))
    rect((17.52, y - 0.09), (17.73, y + 0.09), fill: green, stroke: none)
    line((17.73, y), (18.18, y), stroke: (paint: green, thickness: 0.7pt))
  }
  arrow-right((18.18, 9.46), (18.43, 9.46), green)
  arrow-right((18.18, 8.82), (18.43, 8.82), green)
  arrow-right((18.18, 8.20), (18.43, 8.20), green)
  arrow-right((18.18, 7.58), (18.43, 7.58), green)
  arrow-right((18.18, 6.42), (18.43, 6.42), green)
  arrow-right((18.18, 5.75), (18.43, 5.75), green)
  arrow-right((18.18, 5.10), (18.43, 5.10), green)
  arrow-right((18.18, 4.48), (18.43, 4.48), green)
  arrow-right((18.18, 3.92), (18.43, 3.92), green)
  label((18.48, 9.46), "DQ[15:0]", size: 4pt, fill: green, anchor: "west")
  label((18.48, 8.82), "DQS/DQS#", size: 4pt, fill: green, anchor: "west")
  label((18.48, 8.20), "DM[1:0]", size: 4pt, fill: green, anchor: "west")
  label((18.48, 7.58), "DQS[1:0]", size: 4pt, fill: green, anchor: "west")
  label((18.48, 6.42), "ADDR/CTRL", size: 4pt, fill: green, anchor: "west")
  label((18.48, 5.75), "CK/CK#", size: 4pt, fill: green, anchor: "west")
  label((18.48, 5.10), "ODT", size: 4pt, fill: green, anchor: "west")
  label((18.48, 4.48), "CS#/RAS#", size: 3.7pt, fill: green, anchor: "west")
  label((18.48, 4.18), "CAS#/WE#", size: 3.7pt, fill: green, anchor: "west")
  label((18.48, 3.92), "•••", size: 5pt, fill: green, anchor: "west")

  // Left explanatory cards.
  panel-box((0.18, 6.28), (3.25, 10.94), blue-line, rgb("#04111a"))
  label((0.46, 10.55), "VDD powers the", size: 8.2pt, fill: blue, anchor: "west")
  label((0.46, 10.10), "sensitive internal", size: 8.2pt, fill: blue, anchor: "west")
  label((0.46, 9.65), "DRAM core:", size: 8.2pt, fill: blue, anchor: "west")
  for item in (("•  Cell array", 9.08), ("•  Sense amplifiers", 8.57),
    ("•  Decoders", 8.06), ("•  Refresh", 7.55), ("•  Control logic", 7.04)) {
    label((0.46, item.at(1)), item.first(), size: 7pt, fill: muted, anchor: "west")
  }
  arrow-right((3.25, 8.72), (4.34, 8.72), blue, thickness: 0.9pt)

  panel-box((0.18, 2.62), (3.25, 5.67), blue-line, rgb("#04111a"))
  shield((0.68, 4.94), blue)
  label((1.10, 5.18), "Isolated from I/O", size: 6.3pt, fill: blue, anchor: "west")
  label((1.10, 4.75), "switching noise", size: 6.3pt, fill: blue, anchor: "west")
  label((0.46, 4.32), "preserves data", size: 6.3pt, fill: blue, anchor: "west")
  label((0.46, 3.89), "integrity and", size: 6.3pt, fill: blue, anchor: "west")
  label((0.46, 3.46), "reliable operation", size: 6.3pt, fill: blue, anchor: "west")

  // Right explanatory cards.
  panel-box((19.35, 6.28), (22.42, 10.94), green-line, rgb("#061407"))
  label((19.63, 10.55), "VDDQ powers the", size: 8.2pt, fill: green, anchor: "west")
  label((19.63, 10.10), "high-speed I/O", size: 8.2pt, fill: green, anchor: "west")
  label((19.63, 9.65), "interface:", size: 8.2pt, fill: green, anchor: "west")
  label((19.63, 9.08), "•  DQ/DQS buffers", size: 7pt, fill: muted, anchor: "west")
  label((19.63, 8.57), "•  Command/address", size: 7pt, fill: muted, anchor: "west")
  label((19.95, 8.16), "receivers", size: 7pt, fill: muted, anchor: "west")
  label((19.63, 7.55), "•  Output drivers", size: 7pt, fill: muted, anchor: "west")

  panel-box((19.35, 2.62), (22.42, 5.67), green-line, rgb("#061407"))
  scope({
    translate((19.85, 4.84))
    // A clean two-pulse switching waveform with consistent high/low dwell.
    line((-0.36, -0.16), (-0.28, -0.16), (-0.28, 0.16), (-0.10, 0.16),
      (-0.10, -0.16), (0.08, -0.16), (0.08, 0.16), (0.26, 0.16),
      (0.26, -0.16), (0.36, -0.16),
      stroke: (paint: green, thickness: 0.8pt, join: "miter"))
  })
  label((20.28, 5.18), "Fast switching", size: 6.3pt, fill: green, anchor: "west")
  label((20.28, 4.75), "currents stay in", size: 6.3pt, fill: green, anchor: "west")
  label((20.28, 4.32), "VDDQ and do not", size: 6.3pt, fill: green, anchor: "west")
  label((20.28, 3.89), "inject noise into", size: 6.3pt, fill: green, anchor: "west")
  label((20.28, 3.46), "the core", size: 6.3pt, fill: green, anchor: "west")

  // Grounds and isolation callout.
  line((7.45, 2.24), (7.45, 1.58), stroke: (paint: blue, thickness: 0.8pt))
  ground((7.45, 1.30), blue)
  label((7.45, 0.72), "VSS", size: 10pt, fill: blue, weight: 700)
  line((14.66, 2.24), (14.66, 1.58), stroke: (paint: green, thickness: 0.8pt))
  ground((14.66, 1.30), green)
  label((14.66, 0.72), "VSSQ", size: 10pt, fill: green, weight: 700)
  line((11.27, 2.23), (11.27, 1.82), stroke: (paint: yellow, thickness: 0.8pt))
  line((11.17, 1.97), (11.27, 2.15), (11.37, 1.97), close: true, fill: yellow, stroke: none)
  label((11.27, 1.43), "Internal Isolation", size: 7.8pt, fill: yellow, weight: 700)
  label((11.27, 1.00), "Minimizes noise coupling", size: 6.8pt, fill: muted)
  label((11.27, 0.65), "between domains", size: 6.8pt, fill: muted)
})
