#let ink = rgb("#14213d")
#let muted = rgb("#5f6f89")
#let line = rgb("#d8e2ef")
#let paper-blue = rgb("#f4f8fc")
#let blue = rgb("#1769aa")
#let cyan = rgb("#0787a5")
#let teal = rgb("#0d8a75")
#let amber = rgb("#b66a08")
#let green = rgb("#237a57")
#let red = rgb("#ba3b46")

#let role-color(role) = {
  if role == "sof" { ink }
  else if role == "length" { rgb("#54657e") }
  else if role == "command" { blue }
  else if role == "sequence" { cyan }
  else if role == "payload" { teal }
  else if role == "checksum" { amber }
  else if role == "success" { green }
  else if role == "error" { red }
  else { muted }
}

#let breakable-identifier(value, limit: 32) = {
  let lines = ()
  let current = ""
  for part in value.split("_") {
    let candidate = if current == "" { part } else { current + "_" + part }
    if current != "" and candidate.len() > limit {
      lines = lines + (current,)
      current = part
    } else {
      current = candidate
    }
  }
  (lines + (current,)).join("\n")
}

#let section-title(number, title, subtitle: none) = block(
  width: 100%,
  below: 7pt,
)[
  #grid(
    columns: (24pt, 1fr),
    column-gutter: 8pt,
    align: (center + horizon, left + horizon),
    box(
      width: 24pt,
      height: 24pt,
      radius: 5pt,
      fill: blue,
      align(center + horizon, text(size: 12pt, weight: "bold", fill: white, number)),
    ),
    [
      #text(size: 18pt, weight: "bold", fill: ink, title)
      #if subtitle != none {
        linebreak()
        text(size: 11pt, fill: muted, subtitle)
      }
    ],
  )
]

#let hex-byte(value) = {
  assert(value >= 0 and value <= 0xff, message: "packet byte must be in 0x00..0xFF")
  let digits = str(value, base: 16)
    .replace("a", "A")
    .replace("b", "B")
    .replace("c", "C")
    .replace("d", "D")
    .replace("e", "E")
    .replace("f", "F")
  if digits.len() == 1 { "0" + digits } else { digits }
}

#let xor-checksum(bytes) = bytes.fold(0, (checksum, byte) => checksum.bit-xor(byte))

#let build-packet(command, sequence, data, response: false) = {
  assert(command >= 0 and command <= 0x7f, message: "command must be in 0x00..0x7F")
  assert(sequence >= 0 and sequence <= 0xff, message: "sequence must be in 0x00..0xFF")
  assert(data.len() <= 0xfd, message: "packet data is too long")
  let _ = data.map(item => assert(
    item.value >= 0 and item.value <= 0xff,
    message: "packet data byte must be in 0x00..0xFF",
  ))

  let wire-command = if response { command.bit-or(0x80) } else { command }
  let length = 2 + data.len()
  let checksum = xor-checksum((length, wire-command, sequence) + data.map(item => item.value))
  let command-label = if response { "RSP CMD" } else { "CMD" }

  (
    (value: hex-byte(0xaa), label: "SOF", role: "sof", target: <field-sof>),
    (value: hex-byte(length), label: "LEN", role: "length", target: <field-len>),
    (value: hex-byte(wire-command), label: command-label, role: "command", target: <field-cmd>),
    (value: hex-byte(sequence), label: "SEQ", role: "sequence", target: <field-seq>),
  ) + data.map(item => (
    value: hex-byte(item.value),
    label: item.label,
    role: item.at("role", default: "payload"),
    target: item.at("target", default: none),
  )) + (
    (value: hex-byte(checksum), label: "CHK", role: "checksum", target: <field-chk>),
  )
}

#let packet-frame(title, fields, direction: none, show-bytes: false) = block(
  width: 100%,
  inset: 8pt,
  radius: 6pt,
  fill: white,
  stroke: 0.8pt + line,
  above: 4pt,
  below: 6pt,
)[
  #grid(
    columns: (1fr, auto),
    align: (left + horizon, right + horizon),
    text(size: 11pt, weight: "bold", fill: ink, title),
    if direction == none { [] } else {
      box(
        inset: (x: 6pt, y: 2pt),
        radius: 10pt,
        fill: rgb("#e7f1fb"),
        text(size: 10pt, weight: "bold", fill: blue, direction),
      )
    },
  )
  #v(5pt)
  #let column-count = calc.min(6, fields.len())
  #for start in range(0, fields.len(), step: 6) {
    if start > 0 { v(3pt) }
    let row = fields.slice(start, calc.min(start + 6, fields.len()))
    grid(
      columns: (1fr,) * column-count,
      gutter: 2pt,
      ..row.map(field => {
        let color = role-color(field.role)
        let target = field.at("target", default: none)
        let display-label = field.label.replace(" · ", "\n")
        let cell = box(
          width: 100%,
          height: 52pt,
          inset: (x: 2pt, y: 5pt),
          radius: 3pt,
          fill: color.lighten(82%),
          stroke: 0.7pt + color.lighten(25%),
          align(center + horizon)[
            #text(size: 14pt, weight: "bold", fill: color, field.value)
            #linebreak()
            #if target == none {
              text(size: 10pt, fill: muted, display-label)
            } else {
              underline(text(size: 10pt, fill: muted, display-label))
            }
          ],
        )
        if target == none { cell } else { link(target, cell) }
      }),
    )
  }
  #if show-bytes {
    v(5pt)
    block(
      width: 100%,
      inset: (x: 7pt, y: 4pt),
      radius: 3pt,
      fill: paper-blue,
      text(
        size: 10pt,
        weight: "bold",
        fill: ink,
        fields.map(field => field.value).join(" "),
      ),
    )
  }
]

#let field-declaration(field) = block(
  width: 100%,
  inset: 9pt,
  radius: 5pt,
  fill: white,
  stroke: 0.7pt + line,
  breakable: false,
)[
  #grid(
    columns: (1fr, auto),
    align: (left + horizon, right + horizon),
    [
      #text(size: 14pt, weight: "bold", fill: role-color(field.role), field.name)
      #h(5pt)
      #text(size: 10pt, fill: muted, field.long-name)
    ],
    box(
      inset: (x: 6pt, y: 2pt),
      radius: 3pt,
      fill: role-color(field.role).lighten(86%),
      text(size: 10pt, weight: "bold", fill: role-color(field.role), field.size),
    ),
  )
  #v(4pt)
  #grid(
    columns: (42pt, 1fr),
    row-gutter: 2pt,
    text(size: 10pt, weight: "bold", fill: muted)[범위],
    text(size: 10pt, fill: blue, field.range),
    text(size: 10pt, weight: "bold", fill: muted)[설명],
    text(size: 10pt, fill: ink, field.description),
  )
  #v(4pt)
  #link(<common-frame>)[#text(size: 10pt, fill: blue)[← 공통 프레임으로 돌아가기]]
]

#let parameter-row(field) = (
  name: field.at("table-name", default: field.name),
  size: field.size,
  range: field.range,
  description: field.description,
  target: field.target,
)

#let parameter-table(rows) = table(
  columns: (22%, 14%, 20%, 44%),
  inset: (x: 6pt, y: 4pt),
  stroke: 0.6pt + line,
  fill: (x, y) => if y == 0 { rgb("#e8f1fa") } else if calc.rem(y, 2) == 0 { rgb("#f8fafc") } else { white },
  align: (left + horizon, center + horizon, center + horizon, left + horizon),
  table.header(
    text(size: 10pt, weight: "bold", fill: ink)[필드],
    text(size: 10pt, weight: "bold", fill: ink)[크기],
    text(size: 10pt, weight: "bold", fill: ink)[범위],
    text(size: 10pt, weight: "bold", fill: ink)[설명],
  ),
  ..rows.map(row => {
    let target = row.at("target", default: none)
    let name = text(size: 10pt, weight: "bold", fill: ink, row.name)
    let linked-name = if target == none { name } else { link(target, underline(name)) }
    (
      linked-name,
      text(size: 10pt, fill: muted, row.size),
      text(size: 10pt, fill: blue, row.range),
      text(size: 10pt, fill: muted, row.description),
    )
  }).flatten(),
)

#let note-box(title, body, tone: "info") = {
  let color = if tone == "warning" { amber } else if tone == "error" { red } else if tone == "success" { green } else { blue }
  block(
    width: 100%,
    inset: (x: 8pt, y: 6pt),
    radius: 4pt,
    fill: color.lighten(90%),
    stroke: (left: 2.2pt + color),
  )[
    #text(size: 10.5pt, weight: "bold", fill: color, title)
    #h(5pt)
    #text(size: 10pt, fill: ink, body)
  ]
}

#let command-card(
  name,
  code,
  summary,
  request,
  response,
  parameters,
  note: none,
) = block(
  width: 100%,
  inset: 10pt,
  radius: 7pt,
  fill: paper-blue,
  stroke: 0.8pt + line,
  breakable: false,
)[
  #grid(
    columns: (1fr, auto),
    align: (left + horizon, right + horizon),
    [
      #text(size: 16pt, weight: "bold", fill: ink, breakable-identifier(name))
      #linebreak()
      #text(size: 10.5pt, fill: muted, summary)
    ],
    box(
      inset: (x: 7pt, y: 3pt),
      radius: 3pt,
      fill: blue,
      text(size: 11pt, weight: "bold", fill: white, "CMD 0x" + hex-byte(code)),
    ),
  )
  #v(4pt)
  #packet-frame(
    "요청 패킷",
    build-packet(code, request.sequence, request.data),
    direction: "HOST → DEVICE",
    show-bytes: true,
  )
  #packet-frame(
    "정상 응답",
    build-packet(code, request.sequence, response.data, response: true),
    direction: "DEVICE → HOST",
    show-bytes: true,
  )
  #parameter-table(parameters)
  #if note != none {
    v(5pt)
    note-box("동작", note)
  }
]
