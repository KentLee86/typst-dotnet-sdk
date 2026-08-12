#import "@preview/cetz:0.5.2"

#set page(width: auto, height: auto, margin: 12pt)

#cetz.canvas(length: 1cm, padding: 0.25, {
  import cetz.draw: *

  line((-1.19, 1.19), (1.19, 1.06), name: "Sample-connection", stroke: (paint: rgb("#2563eb"), thickness: 1.5pt))

  scope({
    translate((-2.69, 1.19))
    rotate(0deg)
    rect((-1.5, -0.725), (1.5, 0.725), name: "Input-card", radius: 9pt, fill: rgb("#dbeafe"), stroke: (paint: rgb("#1e3a5f"), thickness: 1.5pt))
  })

  scope({
    translate((2.13, 1.06))
    rotate(0deg)
    circle((0, 0), name: "Processor", radius: (0.94, 0.94), fill: rgb("#fef3c7"), stroke: (paint: rgb("#713f12"), thickness: 1.5pt))
  })

  scope({
    translate((-0.19, 1.13))
    rotate(0deg)
    line((-0.97, 0), (0.97, 0), name: "Flow", stroke: (paint: rgb("#dc2626"), thickness: 2.5pt))
  })

  scope({
    translate((-0.25, -1.25))
    rotate(7deg)
    content((0, 0), text(size: 18pt, weight: 700, fill: rgb("#172b3d"))[CeTZ Studio], name: "Title")
  })
})
