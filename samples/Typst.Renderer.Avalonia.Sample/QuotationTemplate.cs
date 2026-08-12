using System.Text;

namespace Typst.Renderer.Avalonia.Sample;

internal sealed record QuotationFields(
    string RecipientName,
    string RegistrationNumber,
    string ContactName,
    string Phone,
    string Email,
    string Address,
    string ProjectName,
    string QuoteDate);

internal static class QuotationTemplate
{
    public static QuotationFields Defaults { get; } = new(
        "가나다 주식회사",
        "123-45-67890",
        "김담당",
        "02-1234-5678",
        "contact@example.com",
        "서울특별시 중구 세종대로 110",
        "CeTZ 문서 렌더링 시스템 구축",
        DateTime.Today.ToString("yyyy. MM. dd", System.Globalization.CultureInfo.InvariantCulture));

    public static string Build(QuotationFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var values = new StringBuilder()
            .AppendLine($"#let recipient-name = {TypstString(fields.RecipientName)}")
            .AppendLine($"#let registration-number = {TypstString(fields.RegistrationNumber)}")
            .AppendLine($"#let contact-name = {TypstString(fields.ContactName)}")
            .AppendLine($"#let contact-phone = {TypstString(fields.Phone)}")
            .AppendLine($"#let contact-email = {TypstString(fields.Email)}")
            .AppendLine($"#let recipient-address = {TypstString(fields.Address)}")
            .AppendLine($"#let project-name = {TypstString(fields.ProjectName)}")
            .AppendLine($"#let quote-date = {TypstString(fields.QuoteDate)}")
            .ToString();

        return values + """

#set page(paper: "a4", margin: (x: 17mm, y: 15mm), fill: white)
#set text(font: "Noto Sans KR", size: 8.5pt, fill: rgb("#172033"))
#set par(leading: 0.62em)

#let navy = rgb("#16345f")
#let blue = rgb("#2368a2")
#let pale = rgb("#edf4fa")
#let rule = rgb("#9aabba")
#let muted = rgb("#65758a")

#let cell(body, fill: white, align: left + horizon, weight: "regular") = table.cell(
  fill: fill,
  inset: (x: 5pt, y: 5.5pt),
  align: align,
)[#text(size: 8pt, weight: weight, body)]

#let label(body) = table.cell(
  fill: pale,
  inset: (x: 4pt, y: 5.5pt),
  align: center + horizon,
)[#text(size: 7.4pt, weight: "bold", fill: navy, body)]

#grid(
  columns: (1fr, 1.4fr, 1fr),
  align: (left + horizon, center + horizon, right + horizon),
  [
    #text(size: 11pt, weight: "bold", fill: navy)[CETZ LAB]
    #linebreak()
    #text(size: 6pt, fill: muted)[DOCUMENT AUTOMATION]
  ],
  [
    #text(size: 22pt, weight: "bold", fill: navy)[견 적 서]
    #linebreak()
    #text(size: 7pt, tracking: 0.18em, fill: muted)[QUOTATION]
  ],
  [
    #table(
      columns: (38%, 62%),
      stroke: 0.55pt + rule,
      label([문서번호]), cell([Q-LIVE-001], align: center),
      label([작성일자]), cell(quote-date, align: center),
    )
  ],
)
#v(8pt)
#line(length: 100%, stroke: 1.8pt + navy)
#v(10pt)

#grid(
  columns: (49.5%, 49.5%),
  column-gutter: 1%,
  table(
    columns: (34%, 66%),
    stroke: 0.55pt + rule,
    table.cell(colspan: 2, fill: pale, inset: 5pt, align: center)[
      #text(size: 7.5pt, weight: "bold", fill: navy)[공급받는자]
    ],
    label([상호]), cell(recipient-name, weight: "bold"),
    label([등록번호]), cell(registration-number),
    label([담당자]), cell(contact-name),
    label([주소]), cell(recipient-address),
    label([연락처]), cell(contact-phone),
    label([이메일]), cell(contact-email),
  ),
  table(
    columns: (34%, 66%),
    stroke: 0.55pt + rule,
    table.cell(colspan: 2, fill: pale, inset: 5pt, align: center)[
      #text(size: 7.5pt, weight: "bold", fill: navy)[공급자]
    ],
    label([상호]), cell([예시상사 주식회사], weight: "bold"),
    label([등록번호]), cell([987-65-43210]),
    label([대표자]), cell([홍길동 (인)]),
    label([주소]), cell([경기도 성남시 분당구 판교로 123]),
    label([연락처]), cell([031-123-4567]),
    label([이메일]), cell("sales@example.com"),
  ),
)

#v(12pt)
#block(width: 100%, inset: (x: 12pt, y: 9pt), fill: navy)[
  #grid(
    columns: (auto, 1fr, auto),
    column-gutter: 14pt,
    align: (left + horizon, left + horizon, right + horizon),
    text(size: 8pt, weight: "bold", fill: white)[견적명],
    text(size: 11pt, weight: "bold", fill: white, project-name),
    text(size: 8pt, fill: rgb("#dce8f4"))[VAT 포함],
  )
]
#v(9pt)

#table(
  columns: (7%, 35%, 13%, 10%, 10%, 13%, 12%),
  stroke: 0.55pt + rule,
  table.header(
    label([No.]), label([품명]), label([규격]), label([단위]),
    label([수량]), label([단가]), label([금액]),
  ),
  cell([1], align: center), cell([CeTZ 렌더러 코어]), cell([.NET 8]), cell([식], align: center), cell([1], align: center), cell([3,800,000], align: right), cell([3,800,000], align: right),
  cell([2], align: center), cell([Avalonia 문서 뷰어]), cell([Desktop]), cell([식], align: center), cell([1], align: center), cell([1,200,000], align: right), cell([1,200,000], align: right),
  cell([3], align: center), cell([문서 템플릿 및 검증]), cell([A4]), cell([식], align: center), cell([1], align: center), cell([670,000], align: right), cell([670,000], align: right),
  cell([]), cell([]), cell([]), cell([]), cell([]), cell([]), cell([]),
  cell([]), cell([]), cell([]), cell([]), cell([]), cell([]), cell([]),
)

#v(8pt)
#align(right)[
  #table(
    columns: (32mm, 45mm),
    stroke: 0.55pt + rule,
    label([공급가액]), cell([5,670,000원], align: right),
    label([부가세]), cell([567,000원], align: right),
    label([합계금액]), cell([6,237,000원], align: right, weight: "bold"),
  )
]

#v(12pt)
#table(
  columns: (18%, 32%, 18%, 32%),
  stroke: 0.55pt + rule,
  label([납기]), cell([발주일로부터 15영업일]), label([견적 유효기간]), cell([작성일로부터 30일]),
  label([결제조건]), cell([계약금 50%, 검수 후 잔금 50%]), label([납품장소]), cell(recipient-address),
)

#v(1fr)
#block(width: 100%, inset: 9pt, fill: rgb("#f7f9fb"), stroke: 0.5pt + rgb("#d7e0e8"))[
  #text(size: 7.2pt, weight: "bold", fill: navy)[안내사항]
  #v(4pt)
  #text(size: 7pt, fill: muted)[• 본 견적은 Avalonia 입력 필드와 연동되는 실시간 렌더링 예제입니다.]
  #linebreak()
  #text(size: 7pt, fill: muted)[• 공급받는자 정보를 수정하면 Typst 원본과 미리보기가 자동으로 갱신됩니다.]
]
""";
    }

    private static string TypstString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return '"' + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + '"';
    }
}
