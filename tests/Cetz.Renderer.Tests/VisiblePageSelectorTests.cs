using Cetz.Renderer.Core;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class VisiblePageSelectorTests
{
    [Fact]
    public async Task SelectsIntersectingPagesAndSequentialOverscan()
    {
        using var renderer = new CetzDocumentRenderer(RendererOptions());
        var document = await renderer.RenderSourceAsync(
            "#set page(width: 100pt, height: 200pt, margin: 0pt)\n" +
            string.Join("\n#pagebreak()\n", Enumerable.Range(1, 6).Select(index => $"Page {index}")),
            options: new CetzDocumentRenderOptions { Ppi = 96 },
            cancellationToken: TestContext.Current.CancellationToken);
        var controller = new CetzDocumentViewController();
        controller.SetDocument(document);
        var third = controller.Layout.Pages[2];

        var selected = CetzVisiblePageSelector.Select(
            controller.Layout, third.X, third.Y + 10, third.Width, 50, overscanPages: 1);

        Assert.Equal([1, 2, 3], selected);
    }

    [Fact]
    public void EmptyOrInvalidViewportSelectsNothing()
    {
        var empty = new CetzDocumentViewController().Layout;
        Assert.Empty(CetzVisiblePageSelector.Select(empty, 0, 0, 100, 100));
        Assert.Empty(CetzVisiblePageSelector.Select(empty, 0, 0, double.NaN, 100));
    }

    [Fact]
    public async Task CurrentPageIsThePageWithTheLargestVisibleArea()
    {
        using var renderer = new CetzDocumentRenderer(RendererOptions());
        var document = await renderer.RenderSourceAsync(
            "#set page(width: 100pt, height: 200pt, margin: 0pt)\nFirst\n#pagebreak()\nSecond",
            options: new CetzDocumentRenderOptions { Ppi = 96 },
            cancellationToken: TestContext.Current.CancellationToken);
        var controller = new CetzDocumentViewController();
        controller.SetDocument(document);
        var second = controller.Layout.Pages[1];

        var selected = CetzVisiblePageSelector.SelectCurrentPage(
            controller.Layout, second.X, second.Y - 40, second.Width, 150);

        Assert.Equal(1, selected);
    }

    private static CetzRendererOptions RendererOptions() => new()
    {
        NativeLibraryPath = NativePath(),
        BaseDirectory = Environment.CurrentDirectory,
        PackageResolution = CetzPackageResolution.EmbeddedOnly
    };

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
