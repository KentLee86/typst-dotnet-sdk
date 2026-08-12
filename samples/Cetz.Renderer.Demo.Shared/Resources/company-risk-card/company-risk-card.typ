#import "@preview/cetz:0.5.2"

#set page(width: auto, height: auto, margin: 0pt, fill: rgb("#edf6ff"))
#set text(font: "Noto Sans KR", fill: rgb("#111827"))

#cetz.canvas(length: 10mm, padding: 0, {
  import cetz.draw: *

  let navy = rgb("#111b31")
  let muted = rgb("#667085")
  let icon = rgb("#42526b")
  let border = rgb("#d8e0ea")
  let orange = rgb("#ffb511")
  let red = rgb("#d52f34")
  let blue = rgb("#3275ff")

  let label(pos, body, size: 9pt, fill: navy, weight: 400, anchor: "west") = {
    content(pos, text(size: size, fill: fill, weight: weight, body), anchor: anchor)
  }

  // The bundled Korean face is Regular only. A tiny second impression gives
  // important headings the visual weight of the reference without another font.
  let strong-label(pos, body, size: 9pt, fill: navy, anchor: "west") = {
    label(pos, body, size: size, fill: fill, weight: 700, anchor: anchor)
    label((pos.first() + 0.012, pos.at(1)), body, size: size, fill: fill, weight: 700, anchor: anchor)
  }

  let building(pos) = scope({
    translate(pos)
    rect((-0.12, -0.14), (0.12, 0.14), radius: 1.2pt, stroke: (paint: icon, thickness: 0.9pt))
    for x in (-0.055, 0.055) {
      for y in (-0.065, 0.035, 0.095) {
        circle((x, y), radius: 0.012, fill: icon)
      }
    }
    line((0, -0.14), (0, -0.08), stroke: (paint: icon, thickness: 0.8pt))
  })

  let person(pos) = scope({
    translate(pos)
    circle((0, 0.075), radius: 0.065, stroke: (paint: icon, thickness: 0.9pt))
    arc((0, -0.13), anchor: "origin", start: 20deg, delta: 140deg, radius: 0.13,
      stroke: (paint: icon, thickness: 0.9pt))
  })

  let calendar(pos) = scope({
    translate(pos)
    rect((-0.13, -0.12), (0.13, 0.12), radius: 1.2pt, stroke: (paint: icon, thickness: 0.9pt))
    line((-0.13, 0.045), (0.13, 0.045), stroke: (paint: icon, thickness: 0.8pt))
    line((-0.065, 0.16), (-0.065, 0.08), stroke: (paint: icon, thickness: 1.2pt))
    line((0.065, 0.16), (0.065, 0.08), stroke: (paint: icon, thickness: 1.2pt))
    circle((-0.055, -0.035), radius: 0.012, fill: icon)
    circle((0.055, -0.035), radius: 0.012, fill: icon)
  })

  let briefcase(pos) = scope({
    translate(pos)
    rect((-0.13, -0.10), (0.13, 0.10), radius: 1.2pt, stroke: (paint: icon, thickness: 0.9pt))
    arc((-0.055, 0.10), anchor: "arc-start", start: 180deg, delta: -180deg,
      radius: (0.055, 0.055), stroke: (paint: icon, thickness: 0.9pt))
    line((-0.13, 0.015), (0.13, 0.015), stroke: (paint: icon, thickness: 0.75pt))
  })

  let shield(pos) = scope({
    translate(pos)
    line((-0.14, 0.11), (0, 0.18), (0.14, 0.11), (0.12, -0.08), (0, -0.18),
      (-0.12, -0.08), close: true, fill: rgb("#5f7fac"), stroke: none)
    line((0, 0.10), (0, -0.09), stroke: (paint: white, thickness: 0.9pt))
    circle((0, -0.125), radius: 0.012, fill: white)
  })

  // This function preserves the original card geometry. Only its data changes.
  let company-card(
    offset-y,
    name,
    english,
    owner,
    number,
    founded,
    score,
    grade,
    badge,
    risk-title,
    risk-detail,
    risk-count,
    accent,
    marker,
  ) = scope({
    translate((0, offset-y))

    rect((0, 0), (17.42, 9.83), fill: rgb("#edf6ff"), stroke: none)
    rect((0.47, 0.20), (16.99, 9.48), radius: 8pt, fill: rgb("#dce8f4"), stroke: none)
    rect((0.44, 0.27), (16.98, 9.58), radius: 8pt, fill: rgb("#e5eef7"), stroke: none)
    rect((0.45, 0.35), (16.97, 9.60), radius: 8pt, fill: white,
      stroke: (paint: rgb("#eef2f6"), thickness: 0.6pt))

    circle((3.25, 6.50), radius: 2.12, stroke: (paint: rgb("#e5e7eb"), thickness: 15pt))
    arc((3.25, 6.50), anchor: "origin", start: 90deg, delta: -score * 3.6deg, radius: 2.12,
      stroke: (paint: accent, thickness: 15pt, cap: "butt"))
    label((3.25, 6.63), [#score], size: 39pt, fill: accent, weight: 700, anchor: "center")
    label((3.25, 5.78), [/100], size: 15pt, fill: rgb("#7c8495"), anchor: "center")
    strong-label((3.25, 3.70), [신뢰도 점수], size: 13pt, anchor: "center")

    strong-label((6.35, 8.56), name, size: 23pt, fill: navy)
    label((6.37, 7.82), english, size: 11pt, fill: rgb("#747b8b"))

    rect((6.35, 6.77), (7.92, 7.25), radius: 3pt, fill: accent, stroke: none)
    label((7.135, 7.01), badge, size: 8.5pt, fill: white, weight: 700, anchor: "center")

    if marker == "check" {
      circle((6.79, 6.08), radius: 0.37, fill: accent, stroke: none)
      label((6.79, 6.08), [✓], size: 13pt, fill: white, weight: 700, anchor: "center")
    } else {
      line((6.42, 5.74), (6.79, 6.48), (7.16, 5.74), close: true, fill: accent, stroke: none)
      label((6.79, 5.96), [!], size: 16pt, fill: white, weight: 700, anchor: "center")
    }
    strong-label((7.55, 6.08), risk-title, size: 21pt, fill: accent)
    label((6.35, 5.28), risk-detail, size: 9.6pt, fill: rgb("#273142"))

    rect((6.35, 2.72), (16.40, 4.80), radius: 5pt, fill: white,
      stroke: (paint: border, thickness: 0.8pt))
    line((12.73, 2.72), (12.73, 4.80), stroke: (paint: border, thickness: 0.7pt))

    building((6.84, 4.26))
    label((7.18, 4.28), [사업자등록번호], size: 7.9pt, fill: muted, weight: 700)
    label((7.18, 3.78), number, size: 8.2pt, fill: icon)

    person((10.30, 4.25))
    label((10.65, 4.28), [대표자], size: 7.9pt, fill: muted, weight: 700)
    label((10.65, 3.78), owner, size: 8.2pt, fill: icon)

    briefcase((13.20, 4.25))
    label((13.55, 4.28), [설립일], size: 7.9pt, fill: muted, weight: 700)
    label((13.55, 3.78), founded, size: 8.2pt, fill: icon)

    calendar((6.84, 3.16))
    label((7.18, 3.18), [업종], size: 7.9pt, fill: muted, weight: 700)
    label((8.55, 3.18), [소프트웨어 개발 및 공급업], size: 8.2pt, fill: icon)

    line((1.02, 2.28), (16.40, 2.28), stroke: (paint: rgb("#dfe5ed"), thickness: 0.8pt))
    shield((1.28, 1.48))
    label((1.65, 1.48), [신뢰도 등급], size: 8.7pt, fill: rgb("#60708c"), weight: 700)
    line((4.57, 1.12), (4.57, 1.84), stroke: (paint: rgb("#dfe5ed"), thickness: 0.8pt))
    circle((3.78, 1.48), radius: 0.35, fill: accent, stroke: none)
    label((3.78, 1.48), grade, size: 14pt, fill: white, weight: 700, anchor: "center")

    label((5.12, 1.48), [위험 요인], size: 8.7pt, fill: rgb("#60708c"), weight: 700)
    label((6.52, 1.48), risk-count, size: 9.4pt, fill: accent, weight: 700)

    rect((13.42, 1.05), (16.40, 1.91), radius: 5pt, fill: white,
      stroke: (paint: rgb("#6693ff"), thickness: 1pt))
    label((14.88, 1.48), [상세 정보 보기], size: 8.6pt, fill: blue, weight: 700, anchor: "center")
    line((15.88, 1.61), (16.02, 1.48), (15.88, 1.35),
      stroke: (paint: blue, thickness: 1.1pt))
  })

  company-card(
    19.66,
    [아크포인트랩], [ARKPOINT LAB Co., Ltd.], [윤서진], [310-82-10427], [2020.06.18],
    82, [B], [상태 좋음], [특이사항 없음], [최근 3년 이내 주요 계약 위험 이력이 없습니다.], [0건],
    rgb("#1c9363"), "check",
  )
  company-card(
    9.83,
    [루멘브릿지], [LUMEN BRIDGE Co., Ltd.], [한도윤], [417-31-58206], [2018.11.02],
    65, [C], [상태 보통], [추가 검토 권장], [최근 3년 이내 정산 지연 이력 1건이 확인되었습니다.], [1건],
    orange, "warning",
  )
  company-card(
    0,
    [노바플로우], [NOVA FLOW Co., Ltd.], [오하린], [624-17-93051], [2022.03.25],
    48, [D], [상태 경고], [분쟁이력 있음], [최근 3년 이내 체불·납품 분쟁 이력이 확인되었습니다.], [3건],
    red, "warning",
  )
})
