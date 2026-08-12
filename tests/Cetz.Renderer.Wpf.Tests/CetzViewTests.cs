using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Media;
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
    public void ViewMeasuresAllPagesWithZoomAndSpacingAndDisposesCleanly()
    {
        RunSta(() =>
        {
            var document = fixture.Renderer.RenderProject(
                CetzDemoCatalog.Get("serial-protocol").CreateProject(),
                new CetzDocumentRenderOptions { Ppi = 48 });
            Assert.True(document.Pages.Count > 1);

            var view = new CetzView
            {
                Document = document,
                Zoom = 1.25,
                PageSpacing = 17
            };
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var expectedWidth = document.Pages.Max(page => page.Width) * view.Zoom;
            var expectedHeight = document.Pages.Sum(page => page.Height) * view.Zoom
                + (document.Pages.Count - 1) * view.PageSpacing;
            Assert.InRange(view.DesiredSize.Width, expectedWidth - 0.5, expectedWidth + 0.5);
            Assert.InRange(view.DesiredSize.Height, expectedHeight - 0.5, expectedHeight + 0.5);

            view.Zoom = double.NaN;
            view.PageSpacing = -10;
            Assert.Equal(1, view.Zoom);
            Assert.Equal(0, view.PageSpacing);

            view.Dispose();
            Assert.Null(view.Document);
            view.Dispose();
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
