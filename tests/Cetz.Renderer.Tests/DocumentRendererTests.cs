using Cetz.Renderer.Core;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class DocumentRendererTests
{
    [Fact]
    public void CoreRendererReturnsDisplayReadyPagesWithDipDimensions()
    {
        using var renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            NativeLibraryPath = NativePath(),
            BaseDirectory = Environment.CurrentDirectory,
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });

        var document = renderer.RenderSource(
            "#set page(width: 96pt, height: 48pt, margin: 0pt)\nHello",
            options: new CetzDocumentRenderOptions { Ppi = 144 });

        var page = Assert.Single(document.Pages);
        Assert.Equal(192, page.PixelWidth);
        Assert.Equal(96, page.PixelHeight);
        Assert.Equal(128, page.Width, precision: 2);
        Assert.Equal(64, page.Height, precision: 2);
        Assert.Equal(page.Stride * page.PixelHeight, page.Pixels.Length);
    }

    private static string NativePath()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return OperatingSystem.IsWindows()
            ? Path.Combine(root, "artifacts", "native", "win-x64", "cetz_dotnet_native.dll")
            : Path.Combine(root, "artifacts", "native", "linux-x64", "libcetz_dotnet_native.so");
    }
}
