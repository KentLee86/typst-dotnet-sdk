using Cetz.Renderer;

var options = new CetzRendererOptions();
if (args.Length > 0) options.NativeLibraryPath = args[0];
using var renderer = new CetzRenderer(options);
var result = renderer.RenderSource("""
    #import "@preview/cetz:0.5.2": canvas, draw
    #set page(width: 160pt, height: 100pt, margin: 5pt)
    #canvas({ draw.circle((1, 1), radius: 0.8, fill: rgb("4f8cff")) })
    """, settings: new CetzRenderSettings { Formats = [CetzOutputFormat.Svg, CetzOutputFormat.Png, CetzOutputFormat.Pdf] });
var paths = result.WriteToDirectory(Path.Combine(Environment.CurrentDirectory, "rendered"));
foreach (var path in paths) Console.WriteLine(path);
