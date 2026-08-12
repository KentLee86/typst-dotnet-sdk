using Typst.Renderer;

using var renderer = new TypstRenderer(new TypstRendererOptions
{
    PackageResolution = TypstPackageResolution.EmbeddedOnly
});
var result = renderer.RenderSource("""
    #import "@preview/cetz:0.5.2": canvas, draw
    #set page(width: 80pt, height: 50pt, margin: 2pt)
    #canvas({ draw.line((0, 0), (2, 1)) })
    """, settings: new TypstRenderSettings { Formats = [TypstOutputFormat.Pdf] });
var pdf = result.Artifacts.Single();
if (!pdf.Data.Span.StartsWith("%PDF-"u8)) throw new InvalidDataException("Invalid PDF signature.");
Console.WriteLine($"Typst {result.TypstVersion}: {pdf.Data.Length} PDF bytes");
