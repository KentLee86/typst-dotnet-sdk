using Typst.Renderer;

var options = new TypstRendererOptions();
if (args.Length > 0) options.NativeLibraryPath = args[0];
using var renderer = new TypstRenderer(options);
var result = renderer.RenderSource("""
    #import "@preview/cetz:0.5.2": canvas, draw
    #set page(width: 160pt, height: 100pt, margin: 5pt)
    #canvas({ draw.circle((1, 1), radius: 0.8, fill: rgb("4f8cff")) })
    """, settings: new TypstRenderSettings { Formats = [TypstOutputFormat.Svg, TypstOutputFormat.Png, TypstOutputFormat.Pdf] });
var paths = result.WriteToDirectory(Path.Combine(Environment.CurrentDirectory, "rendered"));
foreach (var path in paths) Console.WriteLine(path);
