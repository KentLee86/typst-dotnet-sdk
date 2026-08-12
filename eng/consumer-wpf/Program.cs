using Cetz.Renderer;
using Cetz.Renderer.Core;
using Cetz.Renderer.Wpf;
using System.IO;

namespace CleanWpfConsumer;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        using var renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });
        var document = renderer.RenderSource(
            "#set page(width: 80pt, height: 50pt, margin: 2pt)\nHello WPF",
            options: new CetzDocumentRenderOptions { Ppi = 96 });
        var bitmap = CetzBitmapSource.Create(document.Pages.Single());
        if (bitmap.Format != System.Windows.Media.PixelFormats.Pbgra32 || !bitmap.IsFrozen)
            throw new InvalidDataException("Invalid WPF bitmap.");
        Console.WriteLine($"Typst {document.TypstVersion}: {bitmap.PixelWidth}x{bitmap.PixelHeight} WPF bitmap");
    }
}
