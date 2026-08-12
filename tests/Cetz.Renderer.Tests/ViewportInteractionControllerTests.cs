using Cetz.Renderer.Core;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class ViewportInteractionControllerTests
{
    [Fact]
    public void DragPanUsesTheCapturedPointerAndOffsetOrigins()
    {
        var interaction = new CetzViewportInteractionController(new TestView());
        interaction.BeginPan(100, 100, 50, 70);

        Assert.True(interaction.TryPanTo(80, 60, out var offset));
        Assert.Equal(new CetzViewportOffset(70, 110), offset);

        interaction.EndPan();
        Assert.False(interaction.TryPanTo(0, 0, out _));
    }

    [Fact]
    public async Task WheelZoomSwitchesToCustomAndKeepsPointerAnchor()
    {
        var view = new TestView();
        using var renderer = new CetzDocumentRenderer(RendererOptions());
        view.SetDocument(await renderer.RenderSourceAsync(
            "#set page(width: 96pt, height: 48pt, margin: 0pt)\nHello",
            options: new CetzDocumentRenderOptions { Ppi = 96 },
            cancellationToken: TestContext.Current.CancellationToken));
        var interaction = new CetzViewportInteractionController(view);

        var offset = interaction.ZoomByWheel(120, 32, 16, 16, 8);

        Assert.Equal(1.25, view.Zoom);
        Assert.Equal(CetzZoomMode.Custom, view.ZoomMode);
        Assert.Equal(28, offset.X, precision: 2);
        Assert.Equal(14, offset.Y, precision: 2);
    }

    [Fact]
    public void InvalidWheelStepIsRejected()
    {
        var interaction = new CetzViewportInteractionController(new TestView());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            interaction.ZoomByWheel(1, 0, 0, 0, 0, double.NaN));
    }

    [Fact]
    public void NativeExtentAnchorResolvesAfterThePlatformLayoutPass()
    {
        var anchor = CetzViewportAnchor.Capture(100, 50, 200, 150, 600, 400);

        var offset = anchor.Resolve(900, 800);

        Assert.Equal(new CetzViewportOffset(350, 350), offset);
    }

    [Fact]
    public void ContentAnchorIgnoresTheMovableWorkspaceGutter()
    {
        var anchor = CetzViewportAnchor.Capture(100, 50, 500, 350, 600, 400, 400, 300);

        var offset = anchor.Resolve(900, 800, 400, 300);

        Assert.Equal(new CetzViewportOffset(600, 450), offset);
    }

    [Fact]
    public async Task DocumentAnchorKeepsTheExactPagePointAcrossFixedSpacing()
    {
        var view = new TestView();
        using var renderer = new CetzDocumentRenderer(RendererOptions());
        view.SetDocument(await renderer.RenderSourceAsync(
            "#set page(width: 96pt, height: 48pt, margin: 0pt)\nFirst\n#pagebreak()\nSecond",
            options: new CetzDocumentRenderOptions { Ppi = 96 },
            cancellationToken: TestContext.Current.CancellationToken));
        var second = view.Layout.Pages[1];
        var anchor = CetzDocumentAnchor.Capture(
            view.Layout, 20, 20, 400 + second.X, 300 + second.Y, 400, 300);

        view.SetZoom(2);
        var offset = anchor.Resolve(view.Layout, 400, 300);
        var newSecond = view.Layout.Pages[1];

        Assert.Equal(420 + newSecond.X, offset.X, precision: 5);
        Assert.Equal(320 + newSecond.Y, offset.Y, precision: 5);
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

    private sealed class TestView : ICetzDocumentView
    {
        private readonly CetzDocumentViewController _controller = new();
        public CetzRenderedDocument? Document => _controller.Document;
        public double Zoom => _controller.Zoom;
        public CetzZoomMode ZoomMode => _controller.ZoomMode;
        public CetzPageViewMode ViewMode => _controller.ViewMode;
        public double PageSpacing => _controller.PageSpacing;
        public int CurrentPageIndex => _controller.CurrentPageIndex;
        public int PageCount => _controller.PageCount;
        public CetzDocumentViewLayout Layout => _controller.Layout;
        public void SetDocument(CetzRenderedDocument document) => _controller.SetDocument(document);
        public void SetZoom(double zoom) => _controller.SetZoom(zoom);
        public void SetZoomMode(CetzZoomMode mode) => _controller.SetZoomMode(mode);
        public void SetViewMode(CetzPageViewMode mode) => _controller.SetViewMode(mode);
        public void SetViewport(double width, double height) => _controller.SetViewport(width, height);
        public void SetPageSpacing(double pageSpacing) => _controller.SetPageSpacing(pageSpacing);
        public void GoToPage(int pageIndex) => _controller.GoToPage(pageIndex);
        public bool MoveNext() => _controller.MoveNext();
        public bool MovePrevious() => _controller.MovePrevious();
        public void ReleaseDocument() => _controller.ReleaseDocument();
    }
}
