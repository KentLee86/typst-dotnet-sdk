using Cetz.Renderer.Core;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class DocumentViewControllerTests
{
    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(-1, 0.1)]
    [InlineData(0.05, 0.1)]
    [InlineData(3, 3)]
    [InlineData(12, 8)]
    public void ZoomNormalizationIsShared(double input, double expected)
        => Assert.Equal(expected, CetzDocumentViewController.NormalizeZoom(input));

    [Theory]
    [InlineData(double.NaN, 24)]
    [InlineData(double.NegativeInfinity, 24)]
    [InlineData(-1, 0)]
    [InlineData(30, 30)]
    [InlineData(1200, 1000)]
    public void PageSpacingNormalizationIsShared(double input, double expected)
        => Assert.Equal(expected, CetzDocumentViewController.NormalizePageSpacing(input));

    [Fact]
    public void EmptyControllerHasStableDefaultsAndLayout()
    {
        var controller = new CetzDocumentViewController();

        Assert.Null(controller.Document);
        Assert.Equal(1, controller.Zoom);
        Assert.Equal(24, controller.PageSpacing);
        Assert.Empty(controller.Layout.Pages);
        Assert.Equal(0, controller.Layout.ExtentWidth);
        Assert.Equal(0, controller.Layout.ExtentHeight);
    }

    [Fact]
    public void LayoutCentersPagesAndUsesUnscaledSpacing()
    {
        using var renderer = CreateRenderer();
        var document = renderer.RenderSource(
            "#set page(width: 96pt, height: 48pt, margin: 0pt)\nFirst\n#pagebreak()\n#set page(width: 48pt, height: 24pt)\nSecond",
            options: new CetzDocumentRenderOptions { Ppi = 96 });
        var controller = new CetzDocumentViewController();

        controller.SetPageSpacing(10);
        controller.SetDocument(document);
        controller.SetZoom(2);

        Assert.Equal(2, controller.Layout.Pages.Count);
        Assert.Equal(128, controller.Layout.LogicalWidth, precision: 2);
        Assert.Equal(96, controller.Layout.LogicalHeight, precision: 2);
        Assert.Equal(256, controller.Layout.ExtentWidth, precision: 2);
        Assert.Equal(202, controller.Layout.ExtentHeight, precision: 2);
        Assert.Equal(0, controller.Layout.Pages[0].X, precision: 2);
        Assert.Equal(64, controller.Layout.Pages[1].X, precision: 2);
        Assert.Equal(138, controller.Layout.Pages[1].Y, precision: 2);
    }

    [Fact]
    public void ChangedOnlyFiresForEffectiveStateChanges()
    {
        var controller = new CetzDocumentViewController();
        var changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.SetZoom(1);
        controller.SetPageSpacing(24);
        controller.ReleaseDocument();
        controller.SetZoom(2);
        controller.SetZoom(2);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void FitModesUseViewportAndFacingSpread()
    {
        using var renderer = CreateRenderer();
        var document = RenderFourPages(renderer);
        var controller = new CetzDocumentViewController();
        controller.SetDocument(document);
        controller.SetViewMode(CetzPageViewMode.FacingPages);
        controller.SetPageSpacing(10);
        controller.SetViewport(522, 300);

        controller.SetZoomMode(CetzZoomMode.FitWidth);
        Assert.Equal(2, controller.Zoom, precision: 2);
        Assert.Equal(2, controller.Layout.Pages.Count);
        Assert.Equal(522, controller.Layout.ExtentWidth, precision: 2);

        controller.SetZoomMode(CetzZoomMode.FitPage);
        Assert.Equal(2, controller.Zoom, precision: 2);
    }

    [Fact]
    public void NavigationUsesOneOrTwoPageStepsAndPagedModesLimitLayout()
    {
        using var renderer = CreateRenderer();
        var document = RenderFourPages(renderer);
        var controller = new CetzDocumentViewController();
        controller.SetDocument(document);
        controller.SetViewMode(CetzPageViewMode.SinglePage);

        Assert.True(controller.MoveNext());
        Assert.Equal(1, controller.CurrentPageIndex);
        Assert.Single(controller.Layout.Pages);

        controller.SetViewMode(CetzPageViewMode.FacingPages);
        Assert.Equal(0, controller.CurrentPageIndex);
        Assert.True(controller.MoveNext());
        Assert.Equal(2, controller.CurrentPageIndex);
        Assert.Equal([2, 3], controller.Layout.Pages.Select(page => page.PageIndex));
        Assert.False(controller.MoveNext());
        Assert.True(controller.MovePrevious());
        Assert.Equal(0, controller.CurrentPageIndex);
    }

    [Fact]
    public void ContinuousFacingLaysOutTwoPagesPerRowWithoutTrailingSpacing()
    {
        using var renderer = CreateRenderer();
        var controller = new CetzDocumentViewController();
        controller.SetDocument(RenderFourPages(renderer));
        controller.SetViewMode(CetzPageViewMode.ContinuousFacing);
        controller.SetZoom(1);
        controller.SetPageSpacing(10);

        Assert.Equal(4, controller.Layout.Pages.Count);
        Assert.Equal(266, controller.Layout.ExtentWidth, precision: 2);
        Assert.Equal(138, controller.Layout.ExtentHeight, precision: 2);
        Assert.Equal(74, controller.Layout.Pages[2].Y, precision: 2);
    }

    [Fact]
    public void ReplacingWithShorterDocumentKeepsFacingPageAtSpreadStart()
    {
        using var renderer = CreateRenderer();
        var controller = new CetzDocumentViewController();
        controller.SetDocument(RenderFourPages(renderer));
        controller.SetViewMode(CetzPageViewMode.FacingPages);
        controller.GoToPage(2);
        var twoPages = renderer.RenderSource(
            "One\n#pagebreak()\nTwo",
            options: new CetzDocumentRenderOptions { Ppi = 96 });

        controller.SetDocument(twoPages);

        Assert.Equal(0, controller.CurrentPageIndex);
        Assert.Equal([0, 1], controller.Layout.Pages.Select(page => page.PageIndex));
    }

    [Fact]
    public void TrackingAContinuousPageUpdatesStateWithoutChangingLayoutOrScrolling()
    {
        using var renderer = CreateRenderer();
        var controller = new CetzDocumentViewController();
        controller.SetDocument(RenderFourPages(renderer));
        var layout = controller.Layout;

        Assert.True(controller.TrackCurrentPage(2));

        Assert.Equal(2, controller.CurrentPageIndex);
        Assert.Same(layout, controller.Layout);
        Assert.False(controller.TrackCurrentPage(2));
        controller.SetViewMode(CetzPageViewMode.SinglePage);
        Assert.False(controller.TrackCurrentPage(3));
    }

    private static CetzDocumentRenderer CreateRenderer()
        => new(new CetzRendererOptions
        {
            NativeLibraryPath = NativePath(),
            BaseDirectory = Environment.CurrentDirectory,
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });

    private static CetzRenderedDocument RenderFourPages(CetzDocumentRenderer renderer)
        => renderer.RenderSource(
            "#set page(width: 96pt, height: 48pt, margin: 0pt)\nOne\n#pagebreak()\nTwo\n#pagebreak()\nThree\n#pagebreak()\nFour",
            options: new CetzDocumentRenderOptions { Ppi = 96 });

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
