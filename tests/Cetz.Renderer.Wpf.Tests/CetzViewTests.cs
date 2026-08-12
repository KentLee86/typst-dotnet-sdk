using System.Runtime.ExceptionServices;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;
using Cetz.Renderer.Wpf;
using Xunit;

namespace Cetz.Renderer.Wpf.Tests;

public sealed class CetzViewTests(CetzRendererFixture fixture) : IClassFixture<CetzRendererFixture>
{
    [Fact]
    public void BitmapSourceSwizzlesEveryPixelAndPreservesPpi()
    {
        RunSta(() =>
        {
            var document = fixture.Renderer.RenderSource(
                "#set page(width: 12pt, height: 8pt, margin: 0pt)\n#rect(width: 100%, height: 100%, fill: rgb(200, 100, 50))",
                options: new CetzDocumentRenderOptions { Ppi = 120 });
            var page = Assert.Single(document.Pages);

            var bitmap = CetzBitmapSource.Create(page);

            Assert.Equal(PixelFormats.Pbgra32, bitmap.Format);
            Assert.Equal(page.Ppi, bitmap.DpiX, 3);
            Assert.Equal(page.Ppi, bitmap.DpiY, 3);
            Assert.True(bitmap.IsFrozen);

            var destinationStride = checked(page.PixelWidth * 4);
            var converted = new byte[checked(destinationStride * page.PixelHeight)];
            bitmap.CopyPixels(converted, destinationStride, 0);
            var source = page.Pixels.Span;
            for (var row = 0; row < page.PixelHeight; row++)
            {
                for (var column = 0; column < destinationStride; column += 4)
                {
                    var sourceOffset = row * page.Stride + column;
                    var destinationOffset = row * destinationStride + column;
                    Assert.Equal(source[sourceOffset + 2], converted[destinationOffset]);
                    Assert.Equal(source[sourceOffset + 1], converted[destinationOffset + 1]);
                    Assert.Equal(source[sourceOffset], converted[destinationOffset + 2]);
                    Assert.Equal(source[sourceOffset + 3], converted[destinationOffset + 3]);
                }
            }
        });
    }

    [Fact]
    public void ViewImplementsCommonContractAndUsesControllerLayoutNavigationAndRelease()
    {
        RunSta(() =>
        {
            var document = fixture.Renderer.RenderProject(
                CetzDemoCatalog.Get("serial-protocol").CreateProject(),
                new CetzDocumentRenderOptions { Ppi = 48 });
            Assert.True(document.Pages.Count > 1);

            using var concrete = new CetzView();
            ICetzDocumentView view = concrete;
            view.SetDocument(document);
            view.SetZoom(1.25);
            view.SetPageSpacing(17);
            view.SetViewport(900, 700);
            view.SetViewMode(CetzPageViewMode.ContinuousFacing);
            concrete.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Assert.Same(document, view.Document);
            Assert.Equal(CetzPageViewMode.ContinuousFacing, view.ViewMode);
            Assert.Equal(document.Pages.Count, view.PageCount);
            Assert.Equal(document.Pages.Count, view.Layout.Pages.Count);
            Assert.InRange(concrete.DesiredSize.Width, view.Layout.ExtentWidth - 0.5, view.Layout.ExtentWidth + 0.5);
            Assert.InRange(concrete.DesiredSize.Height, view.Layout.ExtentHeight - 0.5, view.Layout.ExtentHeight + 0.5);
            Assert.Equal(0, view.CurrentPageIndex);
            Assert.True(view.MoveNext());
            Assert.Equal(2, view.CurrentPageIndex);
            Assert.True(view.MovePrevious());
            Assert.Equal(0, view.CurrentPageIndex);

            view.SetViewMode(CetzPageViewMode.FacingPages);
            view.GoToPage(document.Pages.Count - 1);
            Assert.Equal((document.Pages.Count - 1) / 2 * 2, view.CurrentPageIndex);
            Assert.InRange(view.Layout.Pages.Count, 1, 2);

            view.SetZoomMode(CetzZoomMode.FitPage);
            Assert.Equal(CetzZoomMode.FitPage, view.ZoomMode);
            Assert.InRange(view.Zoom, CetzDocumentViewController.MinimumZoom, CetzDocumentViewController.MaximumZoom);

            view.ReleaseDocument();
            Assert.Null(view.Document);
            Assert.Equal(0, view.PageCount);
            Assert.Empty(view.Layout.Pages);

            concrete.Dispose();
            concrete.Dispose();
        });
    }

    [Fact]
    public void DependencyPropertiesRemainConvenientControllerBackedPaths()
    {
        RunSta(() =>
        {
            var document = fixture.Renderer.RenderSource("Hello");
            using var view = new CetzView
            {
                Document = document,
                Zoom = double.NaN,
                PageSpacing = -10,
                ZoomMode = CetzZoomMode.FitWidth,
                ViewMode = CetzPageViewMode.SinglePage
            };

            Assert.IsAssignableFrom<ICetzDocumentView>(view);
            Assert.Equal(CetzDocumentViewController.DefaultZoom, view.Zoom);
            Assert.Equal(0, view.PageSpacing);
            Assert.Equal(CetzZoomMode.FitWidth, view.ZoomMode);
            Assert.Equal(CetzPageViewMode.SinglePage, view.ViewMode);
            Assert.Same(document, view.Document);
        });
    }

    [Fact]
    public void LoadedViewReleasesPlatformBitmapResourcesWithDocument()
    {
        RunSta(() =>
        {
            var document = fixture.Renderer.RenderSource("Hello");
            using var view = new CetzView();
            var window = new Window
            {
                Content = view,
                Width = 2,
                Height = 2,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            try
            {
                window.Show();
                view.SetDocument(document);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

                var bitmaps = Assert.IsAssignableFrom<ICollection>(typeof(CetzView)
                    .GetField("_bitmaps", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(view));
                Assert.Equal(document.Pages.Count, bitmaps.Count);

                view.ReleaseDocument();
                Assert.Empty(bitmaps);
                Assert.Null(view.Document);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FitPageUsesHostViewportAndIgnoresTheViewsOwnExtent()
    {
        RunSta(() =>
        {
            var document = fixture.Renderer.RenderSource(
                "#set page(width: 600pt, height: 900pt, margin: 0pt)\nFit viewport");
            using var view = new CetzView();
            view.SetDocument(document);
            view.SetViewport(400, 300);
            view.SetZoomMode(CetzZoomMode.FitPage);
            var hostViewportZoom = view.Zoom;

            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            view.Arrange(new Rect(0, 0, view.DesiredSize.Width, view.DesiredSize.Height));

            Assert.Equal(hostViewportZoom, view.Zoom, 8);
            Assert.True(view.Layout.ExtentWidth <= 400.001);
            Assert.True(view.Layout.ExtentHeight <= 300.001);
        });
    }

    [Fact]
    public void ContinuousNavigationScrollsTheHostViewerToTheSelectedPage()
    {
        RunSta(() =>
        {
            var document = fixture.Renderer.RenderProject(
                CetzDemoCatalog.Get("serial-protocol").CreateProject(),
                new CetzDocumentRenderOptions { Ppi = 48 });
            using var view = new CetzView();
            var scrollViewer = new System.Windows.Controls.ScrollViewer
            {
                Content = view,
                Width = 420,
                Height = 260,
                CanContentScroll = false,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
            };
            var window = new Window
            {
                Content = scrollViewer,
                Width = 430,
                Height = 270,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            try
            {
                window.Show();
                view.SetDocument(document);
                view.SetViewMode(CetzPageViewMode.ContinuousSingle);
                view.SetViewport(scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
                window.UpdateLayout();
                Assert.Equal(0, scrollViewer.VerticalOffset);

                view.GoToPage(2);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                window.UpdateLayout();

                Assert.Equal(2, view.CurrentPageIndex);
                Assert.True(scrollViewer.VerticalOffset > 0,
                    $"Expected navigation to scroll, actual offset was {scrollViewer.VerticalOffset}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

public sealed class CetzRendererFixture : IDisposable
{
    public CetzRendererFixture()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var nativePath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(root, "artifacts", "native", "win-x64", "cetz_dotnet_native.dll");
        Renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            NativeLibraryPath = nativePath,
            BaseDirectory = Environment.CurrentDirectory,
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });
    }

    public CetzDocumentRenderer Renderer { get; }

    public void Dispose() => Renderer.Dispose();
}
