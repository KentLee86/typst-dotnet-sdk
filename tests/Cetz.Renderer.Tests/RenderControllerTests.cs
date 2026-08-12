using Cetz.Renderer.Core;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class RenderControllerTests
{
    [Fact]
    public async Task SuccessfulRenderIsAppliedThroughCommonViewContract()
    {
        var view = new TestView();
        using var controller = new CetzRenderController(view, RendererOptions());

        var rendered = await controller.RenderSourceAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(rendered, view.Document);
        Assert.False(controller.IsRendering);
        Assert.Null(controller.LastError);
    }

    [Fact]
    public async Task FailedRenderKeepsPreviousSuccessfulDocument()
    {
        var view = new TestView();
        using var controller = new CetzRenderController(view, RendererOptions());
        var previous = await controller.RenderSourceAsync("Good", cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => controller.RenderSourceAsync(
            "#this-function-does-not-exist()", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(previous, view.Document);
        Assert.NotNull(controller.LastError);
    }

    [Fact]
    public async Task StateObserverFailureDoesNotLeaveControllerBusy()
    {
        var view = new TestView();
        using var controller = new CetzRenderController(view, RendererOptions());
        controller.StateChanged += (_, _) => throw new InvalidOperationException("observer");

        var document = await controller.RenderSourceAsync(
            "Still renders", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        Assert.False(controller.IsRendering);
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
        public bool TrackCurrentPage(int pageIndex) => _controller.TrackCurrentPage(pageIndex);
        public bool MoveNext() => _controller.MoveNext();
        public bool MovePrevious() => _controller.MovePrevious();
        public void ReleaseDocument() => _controller.ReleaseDocument();
    }
}
