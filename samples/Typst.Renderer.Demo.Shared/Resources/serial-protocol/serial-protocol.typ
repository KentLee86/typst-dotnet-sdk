#import "protocol-components.typ": *

#set page(
  paper: "a4",
  margin: (x: 18mm, top: 15mm, bottom: 14mm),
  fill: rgb("#f8fafc"),
  header: context {
    if counter(page).get().first() > 1 {
      align(right, text(size: 10pt, fill: muted)[LUMALINK UART PROTOCOL · DEMO SPECIFICATION])
    }
  },
  footer: context {
    grid(
      columns: (1fr, auto),
      text(size: 10pt, fill: muted)[CeTZ Renderer · Serial protocol document demo],
      text(size: 10pt, fill: muted)[#counter(page).display("1 / 1", both: true)],
    )
  },
)
#set text(font: "Noto Sans KR", size: 11pt, fill: ink, lang: "ko")
#set par(justify: true, leading: 0.65em)

#let communication-fields = (
  sof: (
    name: "SOF", long-name: "Start of Frame", value: "AA", label: "SOF · 1 B",
    role: "sof", size: "1 B", range: "0xAA", target: <field-sof>,
    description: [프레임 시작을 알리는 고정 바이트],
  ),
  length: (
    name: "LEN", long-name: "Length", value: "LEN", label: "길이 · 1 B",
    role: "length", size: "1 B", range: "0x02–0xFF", target: <field-len>,
    description: [CMD + SEQ + PAYLOAD의 바이트 수],
  ),
  command: (
    name: "CMD", long-name: "Command", value: "CMD", label: "명령 · 1 B",
    role: "command", size: "1 B", range: "0x00–0x7F", target: <field-cmd>,
    description: [요청 명령 코드이며 응답은 CMD | 0x80],
  ),
  sequence: (
    name: "SEQ", long-name: "Sequence", value: "SEQ", label: "순번 · 1 B",
    role: "sequence", size: "1 B", range: "0x00–0xFF", target: <field-seq>,
    description: [요청과 응답을 연결하는 순환 번호],
  ),
  payload: (
    name: "PAYLOAD", long-name: "Payload", value: "DATA…", label: "페이로드 · N B",
    role: "payload", size: "N B", range: "가변", target: <field-payload>,
    description: [명령별 파라미터 또는 응답 데이터],
  ),
  checksum: (
    name: "CHK", long-name: "Checksum", value: "CHK", label: "XOR · 1 B",
    role: "checksum", size: "1 B", range: "XOR", target: <field-chk>,
    description: [LEN부터 PAYLOAD 마지막 바이트까지 XOR],
  ),
)

#let command-fields = (
  brightness: (
    name: "BRIGHTNESS", table-name: "brightness", long-name: "Brightness", role: "payload", size: "1 B",
    range: "0–100", target: <field-brightness>,
    description: [LED 밝기 값. 0은 소등, 100은 최대 밝기],
  ),
  status: (
    name: "STATUS", table-name: "status", long-name: "Result Status", role: "success", size: "1 B",
    range: "0x00–0x02", target: <field-status>,
    description: [명령 처리 결과. 0x00: OK, 0x01: BAD_CMD, 0x02: BAD_VALUE],
  ),
  power: (
    name: "POWER", table-name: "power", long-name: "Power State", role: "success", size: "1 B",
    range: "0 / 1", target: <field-power>,
    description: [LED 컨트롤러 전원 상태. 0: OFF, 1: ON],
  ),
  temperature: (
    name: "TEMPERATURE", table-name: "temperature", long-name: "Temperature", role: "payload", size: "1 B",
    range: "0–125 °C", target: <field-temperature>,
    description: [컨트롤러 내부 온도. 부호 없는 섭씨 정수 값],
  ),
  channel: (
    name: "CHANNEL", table-name: "channel", long-name: "LED Channel", role: "payload", size: "1 B",
    range: "0–7", target: <field-channel>,
    description: [제어하거나 조회할 LED 출력 채널 번호],
  ),
  fade-time: (
    name: "FADE_TIME_MS", table-name: "fade_time_ms", long-name: "Fade Time", role: "payload", size: "2 B",
    range: "0–5000 ms", target: <field-fade-time>,
    description: [밝기 전환 시간. Little Endian 16비트 값],
  ),
  operating-mode: (
    name: "OPERATING_MODE", table-name: "operating_mode", long-name: "Operating Mode", role: "payload", size: "1 B",
    range: "0x00–0x02", target: <field-operating-mode>,
    description: [0: MANUAL, 1: AUTO, 2: TEST],
  ),
)

#block(
  width: 100%,
  inset: 15pt,
  radius: 8pt,
  fill: rgb("#102b4e"),
)[
  #grid(
    columns: (1fr, auto),
    align: (left + horizon, right + horizon),
    [
      #text(size: 11pt, weight: "bold", fill: rgb("#72c2f1"))[DEMO SPECIFICATION · REV 1.0]
      #v(7pt)
      #text(size: 28pt, weight: "bold", fill: white)[LumaLink 시리얼 통신]
      #linebreak()
      #text(size: 18pt, fill: rgb("#d8ebf8"))[LED 컨트롤러 패킷 설명서]
      #v(9pt)
      #text(size: 10pt, fill: rgb("#afc9dc"))[간결한 바이너리 프레임 · 요청/응답 방식 · XOR 무결성 검사]
    ],
    box(
      width: 52pt,
      height: 52pt,
      radius: 10pt,
      fill: rgb("#1769aa"),
      align(center + horizon)[
        #text(size: 14pt, weight: "bold", fill: white)[UART]
        #linebreak()
        #text(size: 10pt, fill: rgb("#cde8fa"))[BINARY]
      ],
    ),
  )
]

#v(10pt)
#section-title("01", "프로토콜 소개", subtitle: "긴 설명과 명령 식별자는 사용 가능한 폭에 맞춰 자동으로 개행됩니다.")

#block(
  width: 100%,
  inset: 10pt,
  radius: 5pt,
  fill: white,
  stroke: 0.7pt + line,
)[
  LumaLink는 호스트가 LED 컨트롤러의 밝기와 상태를 안정적으로 제어하기 위한 요청·응답형 UART 프로토콜입니다. 각 요청은 고유한 SEQ를 포함하며 장치는 동일한 SEQ와 응답 CMD를 반환하므로 여러 명령이 연속으로 전송되어도 요청과 결과를 정확하게 연결할 수 있습니다. 설명이 길어지면 이 문단처럼 카드 폭에 맞춰 다음 줄로 자동 배치됩니다.

  #v(5pt)
  긴 식별자 예시: #text(weight: "bold", fill: blue, breakable-identifier("SET_BRIGHTNESS_WITH_VALIDATION_AND_STATUS_REPORT"))
]

#v(10pt)
#section-title("02", "통신 사양", subtitle: "호스트가 명령을 전송하고 장치가 동일 SEQ로 응답합니다.")

#grid(
  columns: (1fr, 1fr, 1fr, 1fr),
  gutter: 6pt,
  ..(
    ("115200", "Baud rate"),
    ("8N1", "Data · Parity · Stop"),
    ("LSB first", "Bit order"),
    ("50 ms", "Response timeout"),
  ).map(item => block(
    width: 100%,
    height: 52pt,
    inset: 8pt,
    radius: 5pt,
    fill: white,
    stroke: 0.7pt + line,
  )[
    #align(left + horizon)[
      #text(size: 16pt, weight: "bold", fill: blue, item.first())
      #linebreak()
      #text(size: 10pt, fill: muted, item.last())
    ]
  ]),
)

#v(10pt)
#section-title("03", "공통 프레임", subtitle: "모든 숫자는 16진수 1 byte이며 다중 바이트 값은 Little Endian입니다.")

#packet-frame(
  "기본 패킷 레이아웃",
  (
    communication-fields.sof,
    communication-fields.length,
    communication-fields.command,
    communication-fields.sequence,
    communication-fields.payload,
    communication-fields.checksum,
  ),
) <common-frame>

#parameter-table((
  (name: "SOF", size: "1 B", range: "0xAA", description: "프레임 시작을 알리는 고정 바이트", target: communication-fields.sof.target),
  (name: "LEN", size: "1 B", range: "0x02–0xFF", description: "CMD + SEQ + PAYLOAD의 바이트 수", target: communication-fields.length.target),
  (name: "CMD", size: "1 B", range: "0x00–0x7F", description: "요청 명령 코드, 응답은 CMD | 0x80", target: communication-fields.command.target),
  (name: "SEQ", size: "1 B", range: "0x00–0xFF", description: "요청과 응답을 연결하는 순환 번호", target: communication-fields.sequence.target),
  (name: "PAYLOAD", size: "N B", range: "가변", description: "명령별 파라미터 또는 응답 데이터", target: communication-fields.payload.target),
  (name: "CHK", size: "1 B", range: "XOR", description: "LEN부터 PAYLOAD 마지막 바이트까지 XOR", target: communication-fields.checksum.target),
))

#v(8pt)
#grid(
  columns: (1fr, 1fr),
  gutter: 7pt,
  note-box("LEN 계산", [SOF, LEN, CHK는 길이에 포함하지 않습니다. 최소값은 CMD와 SEQ만 포함한 `0x02`입니다.]),
  note-box("CHK 계산", [`CHK = LEN ⊕ CMD ⊕ SEQ ⊕ PAYLOAD[0..N]`이며 수신기는 계산 결과가 다르면 패킷을 폐기합니다.], tone: "warning"),
)

#v(10pt)
#pagebreak()
#section-title("04", "응답과 오류", subtitle: "응답 CMD는 요청 CMD의 최상위 비트를 1로 설정합니다.")

#grid(
  columns: (1fr, 1fr, 1fr),
  gutter: 6pt,
  ..(
    ("00", "OK", "명령 정상 처리", green),
    ("01", "BAD_CMD", "지원하지 않는 명령", amber),
    ("02", "BAD_VALUE", "파라미터 범위 오류", red),
  ).map(item => block(width: 100%, height: 52pt, inset: 7pt, radius: 4pt, fill: white, stroke: 0.7pt + line)[
    #align(left + horizon)[
      #text(size: 14pt, weight: "bold", fill: item.at(3), item.at(0))
      #h(5pt)
      #text(size: 11pt, weight: "bold", fill: ink, item.at(1))
      #linebreak()
      #text(size: 10pt, fill: muted, item.at(2))
    ]
  ]),
)

#section-title("05", "통신 필드 선언", subtitle: "공통 프레임의 필드를 누르면 이 선언으로 이동합니다.")

#grid(
  columns: (1fr, 1fr),
  gutter: 7pt,
  row-gutter: 7pt,
  [#field-declaration(communication-fields.sof) <field-sof>],
  [#field-declaration(communication-fields.length) <field-len>],
  [#field-declaration(communication-fields.command) <field-cmd>],
  [#field-declaration(communication-fields.sequence) <field-seq>],
  [#field-declaration(communication-fields.payload) <field-payload>],
  [#field-declaration(communication-fields.checksum) <field-chk>],
)

#pagebreak()

#section-title("06", "명령 목록", subtitle: "지원하는 전체 프로토콜을 한눈에 확인하고 명령명을 눌러 상세 페이지로 이동합니다.")

#table(
  columns: (8%, 32%, 18%, 20%, 22%),
  inset: (x: 6pt, y: 6pt),
  stroke: 0.6pt + line,
  fill: (x, y) => if y == 0 { rgb("#e8f1fa") } else if calc.rem(y, 2) == 0 { rgb("#f8fafc") } else { white },
  align: (center + horizon, left + horizon, left + horizon, left + horizon, left + horizon),
  table.header(
    text(size: 10pt, weight: "bold", fill: ink)[CMD],
    text(size: 10pt, weight: "bold", fill: ink)[명령],
    text(size: 10pt, weight: "bold", fill: ink)[요청 DATA],
    text(size: 10pt, weight: "bold", fill: ink)[응답 DATA],
    text(size: 10pt, weight: "bold", fill: ink)[설명],
  ),
  text(size: 10pt, weight: "bold", fill: blue)[0x10],
  link(<cmd-set-brightness>, underline(text(size: 10pt, weight: "bold", fill: ink, breakable-identifier("SET_BRIGHTNESS", limit: 18)))),
  text(size: 10pt, fill: muted)[brightness],
  text(size: 10pt, fill: muted)[status],
  text(size: 10pt, fill: ink)[LED 전체 밝기 설정],
  text(size: 10pt, weight: "bold", fill: blue)[0x11],
  link(<cmd-set-brightness-validated>, underline(text(size: 10pt, weight: "bold", fill: ink, breakable-identifier("SET_BRIGHTNESS_WITH_VALIDATION_AND_STATUS_REPORT", limit: 18)))),
  text(size: 10pt, fill: muted)[brightness],
  text(size: 10pt, fill: muted)[status],
  text(size: 10pt, fill: ink)[범위 검증 후 밝기 적용],
  text(size: 10pt, weight: "bold", fill: blue)[0x12],
  link(<cmd-set-channel-fade>, underline(text(size: 10pt, weight: "bold", fill: ink, breakable-identifier("SET_CHANNEL_BRIGHTNESS_WITH_FADE", limit: 18)))),
  text(size: 10pt, fill: muted)[channel,#linebreak()brightness,#linebreak()fade_time_ms],
  text(size: 10pt, fill: muted)[status],
  text(size: 10pt, fill: ink)[채널별 페이드 밝기 설정],
  text(size: 10pt, weight: "bold", fill: blue)[0x20],
  link(<cmd-get-status>, underline(text(size: 10pt, weight: "bold", fill: ink, breakable-identifier("GET_STATUS", limit: 18)))),
  text(size: 10pt, fill: muted)[없음],
  text(size: 10pt, fill: muted)[power,#linebreak()brightness,#linebreak()temperature],
  text(size: 10pt, fill: ink)[전체 장치 상태 조회],
  text(size: 10pt, weight: "bold", fill: blue)[0x21],
  link(<cmd-get-channel-mode>, underline(text(size: 10pt, weight: "bold", fill: ink, breakable-identifier("GET_CHANNEL_STATUS_WITH_OPERATING_MODE", limit: 18)))),
  text(size: 10pt, fill: muted)[channel],
  text(size: 10pt, fill: muted)[power,#linebreak()brightness,#linebreak()temperature,#linebreak()operating_mode],
  text(size: 10pt, fill: ink)[채널별 상태와 운전 모드 조회],
)

#v(8pt)
#note-box(
  "빠른 이동",
  [밑줄 표시된 명령명을 누르면 해당 요청·응답 패킷과 필드 설명이 있는 상세 페이지로 바로 이동합니다.],
  tone: "success",
)

#pagebreak()

#section-title("07", "명령별 PAYLOAD 필드", subtitle: "필드의 크기·범위·사용 명령과 의미를 한 표에서 비교합니다.")

#table(
  columns: (18%, 10%, 16%, 24%, 32%),
  inset: (x: 6pt, y: 5pt),
  stroke: 0.6pt + line,
  fill: (x, y) => if y == 0 { rgb("#e8f1fa") } else if calc.rem(y, 2) == 0 { rgb("#f8fafc") } else { white },
  align: (left + horizon, center + horizon, center + horizon, left + horizon, left + horizon),
  table.header(
    text(size: 10pt, weight: "bold", fill: ink)[필드],
    text(size: 10pt, weight: "bold", fill: ink)[크기],
    text(size: 10pt, weight: "bold", fill: ink)[범위],
    text(size: 10pt, weight: "bold", fill: ink)[사용 명령],
    text(size: 10pt, weight: "bold", fill: ink)[설명],
  ),
  [#text(size: 10pt, weight: "bold", fill: teal)[brightness] <field-brightness>],
  text(size: 10pt, fill: muted)[1 B],
  text(size: 10pt, fill: blue)[0–100],
  text(size: 10pt, fill: muted)[SET 계열 · GET_STATUS],
  text(size: 10pt, fill: ink)[LED 밝기. 0은 소등, 100은 최대],
  [#text(size: 10pt, weight: "bold", fill: green)[status] <field-status>],
  text(size: 10pt, fill: muted)[1 B],
  text(size: 10pt, fill: blue)[0x00–0x02],
  text(size: 10pt, fill: muted)[SET 계열 응답],
  text(size: 10pt, fill: ink)[명령 처리 결과 코드],
  [#text(size: 10pt, weight: "bold", fill: green)[power] <field-power>],
  text(size: 10pt, fill: muted)[1 B],
  text(size: 10pt, fill: blue)[0 / 1],
  text(size: 10pt, fill: muted)[GET_STATUS 응답],
  text(size: 10pt, fill: ink)[LED 컨트롤러 전원 상태],
  [#text(size: 10pt, weight: "bold", fill: teal)[temperature] <field-temperature>],
  text(size: 10pt, fill: muted)[1 B],
  text(size: 10pt, fill: blue)[0–125 °C],
  text(size: 10pt, fill: muted)[GET_STATUS 응답],
  text(size: 10pt, fill: ink)[컨트롤러 내부 온도],
  [#text(size: 10pt, weight: "bold", fill: teal)[channel] <field-channel>],
  text(size: 10pt, fill: muted)[1 B],
  text(size: 10pt, fill: blue)[0–7],
  text(size: 10pt, fill: muted)[채널 명령 요청],
  text(size: 10pt, fill: ink)[LED 출력 채널 번호],
  [#text(size: 10pt, weight: "bold", fill: teal)[fade_time_ms] <field-fade-time>],
  text(size: 10pt, fill: muted)[2 B],
  text(size: 10pt, fill: blue)[0–5000 ms],
  text(size: 10pt, fill: muted)[SET_CHANNEL 계열 요청],
  text(size: 10pt, fill: ink)[Little Endian 밝기 전환 시간],
  [#text(size: 10pt, weight: "bold", fill: teal)[operating_mode] <field-operating-mode>],
  text(size: 10pt, fill: muted)[1 B],
  text(size: 10pt, fill: blue)[0x00–0x02],
  text(size: 10pt, fill: muted)[GET_CHANNEL 계열 응답],
  text(size: 10pt, fill: ink)[MANUAL / AUTO / TEST 모드],
)

#v(5pt)
#link(<common-frame>)[#text(size: 10pt, fill: blue)[← 공통 프레임으로 돌아가기]]

#v(9pt)
#note-box(
  "링크 사용",
  [PDF에서 공통 프레임 또는 명령 상세의 필드 칸을 누르면 해당 선언으로 이동합니다. 각 선언 아래의 돌아가기 링크로 공통 프레임을 다시 열 수 있습니다.],
  tone: "success",
)

#pagebreak()

#section-title("08", "명령 상세", subtitle: "긴 명령명은 자동 개행되며 밑줄 표시된 모든 필드 칸은 해당 선언으로 연결됩니다.")

#command-card(
  "SET_BRIGHTNESS",
  0x10,
  [LED 전체 밝기 설정],
  (sequence: 0x01, data: (
    (value: 100, label: "brightness · 100", target: command-fields.brightness.target),
  )),
  (data: (
    (value: 0x00, label: "status · OK", role: "success", target: command-fields.status.target),
  )),
  (
    parameter-row(command-fields.brightness),
    parameter-row(command-fields.status),
  ),
  note: [설정값은 즉시 적용됩니다. 범위를 벗어나면 `BAD_VALUE(0x02)`를 반환하고 이전 밝기를 유지합니다.],
) <cmd-set-brightness>

#pagebreak()

#command-card(
  "SET_BRIGHTNESS_WITH_VALIDATION_AND_STATUS_REPORT",
  0x11,
  [밝기 범위를 검증한 뒤 적용 결과를 상태 코드로 반환],
  (sequence: 0x03, data: (
    (value: 75, label: "brightness · 75", target: command-fields.brightness.target),
  )),
  (data: (
    (value: 0x00, label: "status · OK", role: "success", target: command-fields.status.target),
  )),
  (
    parameter-row(command-fields.brightness),
    parameter-row(command-fields.status),
  ),
  note: [긴 명령 식별자는 카드 너비를 넘으면 밑줄 뒤에서 개행됩니다. 밝기 범위 검증에 실패하면 `BAD_VALUE(0x02)`를 반환합니다.],
) <cmd-set-brightness-validated>

#pagebreak()

#command-card(
  "SET_CHANNEL_BRIGHTNESS_WITH_FADE",
  0x12,
  [채널별 밝기를 지정된 시간 동안 부드럽게 전환],
  (sequence: 0x04, data: (
    (value: 0x02, label: "channel · 2", target: command-fields.channel.target),
    (value: 80, label: "brightness · 80", target: command-fields.brightness.target),
    (value: 0xf4, label: "fade LSB · F4", target: command-fields.fade-time.target),
    (value: 0x01, label: "fade MSB · 01", target: command-fields.fade-time.target),
  )),
  (data: (
    (value: 0x00, label: "status · OK", role: "success", target: command-fields.status.target),
  )),
  (
    parameter-row(command-fields.channel),
    parameter-row(command-fields.brightness),
    parameter-row(command-fields.fade-time),
    parameter-row(command-fields.status),
  ),
  note: [`fade_time_ms = 0x01F4`는 Little Endian 바이트 `F4 01`로 전송되며 500 ms 전환을 의미합니다.],
) <cmd-set-channel-fade>

#pagebreak()

#command-card(
  "GET_STATUS",
  0x20,
  [전원·밝기·온도 상태 조회],
  (sequence: 0x02, data: ()),
  (data: (
    (value: 0x01, label: "power · ON", role: "success", target: command-fields.power.target),
    (value: 100, label: "brightness · 100", target: command-fields.brightness.target),
    (value: 42, label: "temp · 42°C", target: command-fields.temperature.target),
  )),
  (
    parameter-row(command-fields.power),
    parameter-row(command-fields.brightness),
    parameter-row(command-fields.temperature),
  ),
  note: [조회 명령에는 요청 PAYLOAD가 없습니다. 예제 응답은 전원 ON, 밝기 100%, 내부 온도 42°C를 의미합니다. 수신기는 SOF 탐색 → LEN만큼 본문 수신 → CHK 확인 → CMD 처리 순서로 파싱하며, 타임아웃 또는 CHK 불일치 프레임에는 응답하지 않습니다.],
) <cmd-get-status>

#pagebreak()

#command-card(
  "GET_CHANNEL_STATUS_WITH_OPERATING_MODE",
  0x21,
  [지정 채널의 전원·밝기·온도·운전 모드 조회],
  (sequence: 0x05, data: (
    (value: 0x02, label: "channel · 2", target: command-fields.channel.target),
  )),
  (data: (
    (value: 0x01, label: "power · ON", role: "success", target: command-fields.power.target),
    (value: 80, label: "brightness · 80", target: command-fields.brightness.target),
    (value: 41, label: "temp · 41°C", target: command-fields.temperature.target),
    (value: 0x01, label: "mode · AUTO", target: command-fields.operating-mode.target),
  )),
  (
    parameter-row(command-fields.channel),
    parameter-row(command-fields.power),
    parameter-row(command-fields.brightness),
    parameter-row(command-fields.temperature),
    parameter-row(command-fields.operating-mode),
  ),
  note: [응답은 채널 2가 전원 ON, 밝기 80%, 내부 온도 41°C, AUTO 모드임을 의미합니다.],
) <cmd-get-channel-mode>
