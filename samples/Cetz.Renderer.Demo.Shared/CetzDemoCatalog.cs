namespace Cetz.Renderer.Demo.Shared;

/// <summary>Platform-independent examples shared by every GUI demo.</summary>
public static class CetzDemoCatalog
{
    public static IReadOnlyList<CetzDemo> All { get; } =
    [
        new("editor-export", "Editor export", "Compact CeTZ editor export with connected shapes.", "editor-export.typ"),
        new("uv-meter-front", "UV meter front", "Korean UV radiometer front panel.", "uv-meter-front.typ"),
        new("monstar-deck-front", "Monstar Deck front", "Front view of a programmable LCD keypad.", "monstar-deck-front.typ"),
        new("monstar-deck-isometric", "Monstar Deck isometric", "Isometric programmable keypad rendering.", "monstar-deck-isometric.typ"),
        new("company-risk-card", "Company risk cards", "Korean company risk dashboard cards.", "company-risk-card.typ"),
        new("ddr3l-power-domain", "DDR3L power domain", "Dark electronics power-domain diagram.", "ddr3l-power-domain.typ"),
        new("serial-protocol", "Serial protocol", "Multi-page Korean serial protocol specification.", "serial-protocol.typ", "protocol-components.typ"),
        new("korean-business-forms", "Korean business forms", "Multi-page business forms with embedded vector logo and seal.",
            "korean-business-forms.typ", "company-logo-v2-imagegen.svg", "company-seal-imagegen.svg"),
        new("embedded-noto", "Embedded Korean font", "Small Korean and English embedded-font smoke example.", "embedded-noto.typ")
    ];

    public static CetzDemo Get(string id)
        => All.FirstOrDefault(demo => demo.Id == id)
            ?? throw new KeyNotFoundException($"Unknown CeTZ demo: {id}");
}
