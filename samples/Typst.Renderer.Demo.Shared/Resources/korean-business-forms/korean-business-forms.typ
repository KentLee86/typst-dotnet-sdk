#set page(
  paper: "a4",
  margin: (x: 15mm, y: 13mm),
  fill: white,
)
#set text(font: "Noto Sans KR", size: 8pt, fill: rgb("#172033"))
#set par(leading: 0.55em)

#let navy = rgb("#16345f")
#let blue = rgb("#2368a2")
#let pale = rgb("#edf4fa")
#let rule = rgb("#9aabba")
#let muted = rgb("#65758a")

#let won(value) = value + "원"

#let cell(body, fill: white, align: left + horizon, weight: "regular", size: 7.4pt) = table.cell(
  fill: fill,
  inset: (x: 4pt, y: 4.2pt),
  align: align,
)[#text(size: size, weight: weight, body)]

#let label(body, colspan: 1, rowspan: 1) = table.cell(
  colspan: colspan,
  rowspan: rowspan,
  fill: pale,
  inset: (x: 3pt, y: 4.2pt),
  align: center + horizon,
)[#text(size: 7pt, weight: "bold", fill: navy, body)]

// Keep populated and blank item rows at exactly the same height. Ten generous
// rows use the page without making the form look like a dense accounting ledger.
#let item-cell(body, placement: left + horizon, weight: "regular") = table.cell(
  inset: (x: 4pt, y: 9pt),
  align: placement,
)[
  #box(width: 0pt)[#text(size: 7.4pt, fill: white)[Ag]]
  #text(size: 7.4pt, weight: weight, body)
]

#let document-mark(kind) = grid(
  columns: (12mm, 1fr),
  column-gutter: 3pt,
  align: (center + horizon, left + horizon),
  box(width: 11mm, height: 11mm)[
    #image("company-logo-v2-imagegen.svg", width: 100%, height: 100%, fit: "contain")
  ],
  [
    #text(size: 8pt, weight: "bold", fill: navy)[예시상사]
    #linebreak()
    #text(size: 4.6pt, fill: muted)[YESI COMPANY · #kind]
  ],
)

#let company-seal(size: 9mm) = box(
  width: size,
  height: size,
)[
  // Image-generated seal, vectorized to avoid PDF alpha-mask compatibility issues.
  #image("company-seal-imagegen.svg", width: 100%, height: 100%, fit: "contain")
]

#let company-seal-over-mark(size: 9mm) = box(
  width: size,
  height: size,
)[
  #place(center + horizon)[#text(size: 6pt, weight: "medium")[(인)]]
  #place(center + horizon)[#company-seal(size: size)]
]

#let sealed-representative(size: 8mm) = grid(
  columns: (auto, auto),
  column-gutter: 3pt,
  align: (left + horizon, center + horizon),
  [홍길동],
  company-seal-over-mark(size: size),
)

#let document-heading(title, english, number, date) = {
  grid(
    columns: (1fr, 1.4fr, 1fr),
    align: (left + horizon, center + horizon, right + horizon),
    document-mark(english),
    [
      #align(center)[
        #text(size: 20pt, weight: "bold", fill: navy, title)
        #linebreak()
        #text(size: 7pt, tracking: 0.18em, fill: muted, english)
      ]
    ],
    [
      #table(
        columns: (34%, 66%),
        stroke: 0.55pt + rule,
        label([문서번호]), cell(number, align: center),
        label([작성일자]), cell(date, align: center),
      )
    ],
  )
  v(8pt)
  line(length: 100%, stroke: 1.8pt + navy)
  v(9pt)
}

#let party-box(title, name, number, person-title, person, address, phone) = table(
  columns: (34%, 66%),
  stroke: 0.55pt + rule,
  table.cell(
    colspan: 2,
    fill: pale,
    inset: (x: 4pt, y: 4.2pt),
    align: center + horizon,
  )[#text(size: 7pt, weight: "bold", fill: navy, title)],
  label([상호]), cell(name),
  label([등록번호]), cell(number),
  label(person-title), cell(person),
  label([주소]), cell(address),
  label([연락처]), cell(phone),
)

#let party-table(customer-title: "공급받는자", customer-name: [가나다 주식회사]) = grid(
  columns: (49.5%, 49.5%),
  column-gutter: 1%,
  party-box(
    customer-title, customer-name, [xxx-xx-xxxxx], [담당자], [홍길동],
    [○○특별시 ○○구 ○○로 xx], [xx-xxxx-xxxx],
  ),
  party-box(
    [공급자], [예시상사 주식회사], [xxx-xx-xxxxx], [대표자], sealed-representative(),
    [○○도 ○○시 ○○로 xx], [xxx-xxxx-xxxx],
  ),
)

#let total-banner(prefix, amount, note) = block(
  width: 100%,
  inset: (x: 11pt, y: 8pt),
  fill: navy,
)[
  #grid(
    columns: (auto, 1fr, auto),
    column-gutter: 12pt,
    align: (left + horizon, left + horizon, right + horizon),
    text(size: 8pt, weight: "bold", fill: white, prefix),
    text(size: 13pt, weight: "bold", fill: white, amount),
    text(size: 7pt, fill: rgb("#dce8f4"), note),
  )
]

#let amount-summary(supply, tax, total) = table(
  columns: (1fr, 24%, 24%),
  stroke: 0.55pt + rule,
  table.cell(rowspan: 3, inset: 7pt, align: left + top)[
    #text(size: 7pt, weight: "bold", fill: navy)[비고]
    #linebreak()
    #text(size: 7pt, fill: muted)[납품 및 결제 조건은 하단의 안내사항을 따릅니다.]
  ],
  label([공급가액]), cell(supply, align: right),
  label([부가세]), cell(tax, align: right),
  label([합계금액]), cell(total, align: right, weight: "bold"),
)

#let footer-note(lines) = {
  v(1fr)
  block(
    width: 100%,
    inset: 8pt,
    fill: rgb("#f7f9fb"),
    stroke: 0.5pt + rgb("#d7e0e8"),
  )[
    #text(size: 7pt, weight: "bold", fill: navy)[안내사항]
    #v(3pt)
    #for item in lines {
      text(size: 6.8pt, fill: muted, [• #item])
      linebreak()
    }
  ]
}

// Page 1 — quotation
#document-heading([견 적 서], [QUOTATION], [Q-2026-0811-01], [2026. 08. 11])
#party-table(customer-title: "수신")
#v(10pt)
#total-banner([견적금액], [금 육백이십삼만칠천원정], [VAT 포함 · ₩6,237,000])
#v(8pt)

#table(
  columns: (5%, 28%, 12%, 8%, 9%, 14%, 12%, 12%),
  stroke: 0.55pt + rule,
  table.header(
    label([No.]), label([품명]), label([규격]), label([단위]),
    label([수량]), label([단가]), label([공급가액]), label([세액]),
  ),
  item-cell([1], placement: center), item-cell([CeTZ 문서 렌더러 구축]), item-cell([CLI + HTTP]), item-cell([식], placement: center), item-cell([1], placement: center), item-cell([3,800,000], placement: right), item-cell([3,800,000], placement: right), item-cell([380,000], placement: right),
  item-cell([2], placement: center), item-cell([업무 문서 템플릿 설계]), item-cell([A4 2종]), item-cell([식], placement: center), item-cell([1], placement: center), item-cell([1,200,000], placement: right), item-cell([1,200,000], placement: right), item-cell([120,000], placement: right),
  item-cell([3], placement: center), item-cell([한글 글꼴 및 출력 검증]), item-cell([SVG·PNG·PDF]), item-cell([식], placement: center), item-cell([1], placement: center), item-cell([670,000], placement: right), item-cell([670,000], placement: right), item-cell([67,000], placement: right),
  ..range(7).map(index => (
    item-cell([#eval(str(index + 4))], placement: center), item-cell([]), item-cell([]), item-cell([]), item-cell([]), item-cell([]), item-cell([]), item-cell([]),
  )).flatten(),
)
#amount-summary([5,670,000], [567,000], [6,237,000])

#v(9pt)
#table(
  columns: (14%, 36%, 14%, 36%),
  stroke: 0.55pt + rule,
  label([납기]), cell([발주일로부터 15영업일]), label([견적 유효기간]), cell([작성일로부터 30일]),
  label([결제조건]), cell([계약금 50%, 검수 후 잔금 50%]), label([납품장소]), cell([고객 지정 온라인 저장소]),
  label([입금계좌]), cell([○○은행 xxx-xxx-xxxxxx 예시상사]), label([담당자]), cell([홍길동 · xxx\@example.com]),
)
#footer-note((
  [본 견적은 요구사항 변경 시 금액과 일정이 조정될 수 있습니다.],
  [산출물의 저작권과 유지보수 범위는 별도 계약서에 따릅니다.],
))

#pagebreak()

// Page 2 — transaction statement
#document-heading([거 래 명 세 서], [TRANSACTION STATEMENT], [T-2026-0811-04], [2026. 08. 11])
#party-table()
#v(10pt)
#total-banner([합계금액], [금 이백육십사만원정], [공급가액 ₩2,400,000 · 세액 ₩240,000])
#v(8pt)

#table(
  columns: (8%, 24%, 12%, 9%, 9%, 14%, 12%, 12%),
  stroke: 0.55pt + rule,
  table.header(
    label([월일]), label([품명]), label([규격]), label([단위]),
    label([수량]), label([단가]), label([공급가액]), label([세액]),
  ),
  item-cell([08/04], placement: center), item-cell([렌더링 엔진 개발 1차]), item-cell([마일스톤 A]), item-cell([식], placement: center), item-cell([1], placement: center), item-cell([1,200,000], placement: right), item-cell([1,200,000], placement: right), item-cell([120,000], placement: right),
  item-cell([08/08], placement: center), item-cell([문서 템플릿 제작]), item-cell([견적서]), item-cell([종], placement: center), item-cell([1], placement: center), item-cell([600,000], placement: right), item-cell([600,000], placement: right), item-cell([60,000], placement: right),
  item-cell([08/11], placement: center), item-cell([문서 템플릿 제작]), item-cell([거래명세서]), item-cell([종], placement: center), item-cell([1], placement: center), item-cell([600,000], placement: right), item-cell([600,000], placement: right), item-cell([60,000], placement: right),
  ..range(7).map(index => (
    item-cell([]), item-cell([]), item-cell([]), item-cell([]), item-cell([]), item-cell([]), item-cell([]), item-cell([]),
  )).flatten(),
)
#amount-summary([2,400,000], [240,000], [2,640,000])

#v(9pt)
#table(
  columns: (14%, 36%, 14%, 36%),
  stroke: 0.55pt + rule,
  label([전 잔액]), cell([0원], align: right), label([당일 거래]), cell([2,640,000원], align: right),
  label([입금액]), cell([1,320,000원], align: right), label([미수 잔액]), cell([1,320,000원], align: right, weight: "bold"),
  label([인수자]), cell([홍길동 (서명)]), label([공급자 확인]), cell([홍길동]),
)
#v(9pt)
#grid(
  columns: (49.5%, 49.5%),
  column-gutter: 1%,
  block(
    width: 100%,
    height: 28mm,
    inset: 9pt,
    fill: rgb("#f7f9fb"),
    stroke: 0.55pt + rule,
  )[
    #text(size: 7pt, weight: "bold", fill: navy)[인수 확인]
    #v(5pt)
    #text(size: 6.8pt, fill: muted)[상기 품목과 수량을 이상 없이 인수하였습니다.]
    #v(5pt)
    #align(right)[#text(size: 7pt)[담당자: 홍길동  (서명)]]
  ],
  block(
    width: 100%,
    height: 28mm,
    inset: 9pt,
    fill: rgb("#f7f9fb"),
    stroke: 0.55pt + rule,
  )[
    #text(size: 7pt, weight: "bold", fill: navy)[공급 확인]
    #v(5pt)
    #text(size: 6.8pt, fill: muted)[기재된 거래 내역과 공급 금액을 확인합니다.]
    #v(5pt)
    #align(right)[
      #grid(
        columns: (auto, auto),
        column-gutter: 5pt,
        align: (right + horizon, center + horizon),
        text(size: 7pt)[예시상사 주식회사 · 대표 홍길동],
        company-seal-over-mark(size: 12mm),
      )
    ]
  ],
)
#footer-note((
  [위 품목과 수량을 정상적으로 공급하였음을 확인합니다.],
  [본 거래명세서는 세금계산서를 대체하는 법정 증빙이 아닙니다.],
))
