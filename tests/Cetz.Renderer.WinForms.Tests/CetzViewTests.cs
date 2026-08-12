using Cetz.Renderer.Core;
using Cetz.Renderer.WinForms;
using Xunit;

namespace Cetz.Renderer.WinForms.Tests;

public sealed class CetzViewTests
{
    private static readonly Lazy<CetzRenderedDocument> FourPageDocument = new(RenderFourPages);

    [Fact]
    public void ImplementsCommonDocumentViewContract()
    {
        using var view = new CetzView();
        Assert.IsAssignableFrom<ICetzDocumentView>(view);
    }

    [Fact]
    public void PublicStatePathsDelegateToCommonController()
    {
        using var view = new CetzView { ClientSize = new Size(600, 400), Padding = Padding.Empty };
        ICetzDocumentView contract = view;
        contract.SetViewport(600, 400);
        contract.SetDocument(FourPageDocument.Value);

        Assert.Same(FourPageDocument.Value, view.Document);
        Assert.Equal(4, view.PageCount);
        Assert.Equal(4, view.BitmapCount);

        view.Zoom = 100;
        Assert.Equal(CetzZoomMode.Custom, view.ZoomMode);
        Assert.Equal(CetzDocumentViewController.MaximumZoom, view.Zoom);
        contract.SetZoom(double.NaN);
        Assert.Equal(CetzDocumentViewController.DefaultZoom, view.Zoom);

        view.PageSpacing = -1;
        Assert.Equal(0, view.PageSpacing);
        contract.SetPageSpacing(double.PositiveInfinity);
        Assert.Equal(CetzDocumentViewController.DefaultPageSpacing, view.PageSpacing);

        view.ZoomMode = CetzZoomMode.FitWidth;
        Assert.Equal(CetzZoomMode.FitWidth, view.ZoomMode);
        Assert.Equal(600, view.Layout.ExtentWidth, precision: 2);

        contract.SetZoomMode(CetzZoomMode.FitPage);
        Assert.Equal(CetzZoomMode.FitPage, view.ZoomMode);
        view.ViewMode = CetzPageViewMode.FacingPages;
        Assert.Equal(2, view.Layout.Pages.Count);
        Assert.Equal([0, 1], view.Layout.Pages.Select(page => page.PageIndex));
    }

    [Fact]
    public void NavigationLayoutAndReleaseUseCommonBehaviorAndClearResources()
    {
        using var view = new CetzView { ClientSize = new Size(600, 400), Padding = Padding.Empty };
        view.SetViewport(600, 400);
        view.SetDocument(FourPageDocument.Value);

        view.SetViewMode(CetzPageViewMode.SinglePage);

        Assert.True(view.MoveNext());
        Assert.Equal(1, view.CurrentPageIndex);
        Assert.Single(view.Layout.Pages);
        view.GoToPage(3);
        Assert.Equal(3, view.CurrentPageIndex);
        Assert.Contains(3, view.RealizedPageIndices);
        Assert.False(view.MoveNext());
        Assert.True(view.MovePrevious());

        view.SetViewMode(CetzPageViewMode.ContinuousFacing);
        Assert.Equal(0, view.CurrentPageIndex % 2);
        Assert.Equal(4, view.Layout.Pages.Count);
        Assert.True(view.Layout.Pages[1].X > view.Layout.Pages[0].X);

        view.ReleaseDocument();
        Assert.Null(view.Document);
        Assert.Equal(0, view.PageCount);
        Assert.Equal(0, view.BitmapCount);
        Assert.Empty(view.Layout.Pages);

        view.Document = FourPageDocument.Value;
        view.Document = null;
        Assert.Equal(0, view.BitmapCount);
    }

    [Fact]
    public void ContinuousNavigationMovesTheWinFormsScrollHostToTheSelectedPage()
    {
        using var view = new CetzView
        {
            ClientSize = new Size(100, 40),
            Padding = Padding.Empty,
            ViewMode = CetzPageViewMode.ContinuousSingle
        };
        view.CreateControl();
        view.SetViewport(100, 40);
        view.SetDocument(FourPageDocument.Value);

        Assert.InRange(view.RealizedPageCount, 1, 3);

        view.GoToPage(3);

        Assert.Equal(3, view.CurrentPageIndex);
        Assert.True(view.Layout.Pages.Single(page => page.PageIndex == 3).Y > 0);
        Assert.True(view.AutoScrollPosition.Y < 0);
    }

    [Fact]
    public void PremultipliedRgbaConversionUsesGdiBgraChannelOrder()
    {
        byte[] rgba = [10, 20, 30, 40, 0, 0, 0, 0, 90, 80, 70, 100];
        var bgra = new byte[rgba.Length];

        CetzBitmapConverter.ConvertPremultipliedRgbaToBgra(rgba, bgra);

        Assert.Equal([30, 20, 10, 40, 0, 0, 0, 0, 70, 80, 90, 100], bgra);
    }

    [Fact]
    public void DisposedViewRejectsContractMutations()
    {
        var view = new CetzView();
        view.Dispose();
        Assert.Throws<ObjectDisposedException>(() => view.SetZoom(2));
        Assert.Throws<ObjectDisposedException>(() => view.SetDocument(FourPageDocument.Value));
    }

    private static CetzRenderedDocument RenderFourPages()
    {
        using var renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            NativeLibraryPath = NativePath(),
            BaseDirectory = Environment.CurrentDirectory,
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });
        return renderer.RenderSource(
            "#set page(width: 96pt, height: 48pt, margin: 0pt)\nOne\n#pagebreak()\nTwo\n#pagebreak()\nThree\n#pagebreak()\nFour",
            options: new CetzDocumentRenderOptions { Ppi = 96 });
    }

    private static string NativePath()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "artifacts", "native", "win-x64", "cetz_dotnet_native.dll");
    }
}
