using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Cetz.Renderer.Core;
using Xunit;

namespace Cetz.Renderer.Avalonia.Tests;

public sealed class CetzViewportTests
{
    [AvaloniaFact]
    public async Task RepeatedCtrlWheelZoomKeepsTheSamePagePointUnderThePointer()
    {
        using var renderer = new CetzDocumentRenderer(RendererOptions());
        using var viewport = new CetzViewport();
        var window = Show(viewport);
        try
        {
            viewport.View.SetDocument(await RenderDocument(renderer));
            Flush();

            var page = viewport.View.Layout.Pages[0];
            const double relativeX = 0.35;
            const double relativeY = 0.4;
            var pointer = viewport.View.TranslatePoint(
                new Point(page.X + page.Width * relativeX, page.Y + page.Height * relativeY), window)!.Value;
            var zoomEvents = 0;
            viewport.ZoomChanged += (_, _) => zoomEvents++;
            window.MouseMove(pointer, RawInputModifiers.None);

            for (var index = 0; index < 4; index++)
            {
                window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.Control);
                Flush();
                page = viewport.View.Layout.Pages[0];
                var anchored = viewport.View.TranslatePoint(
                    new Point(page.X + page.Width * relativeX, page.Y + page.Height * relativeY), window)!.Value;
                Assert.InRange(Math.Abs(anchored.X - pointer.X), 0, 0.75);
                Assert.InRange(Math.Abs(anchored.Y - pointer.Y), 0, 0.75);
            }

            Assert.Equal(4, zoomEvents);
            Assert.Equal(2d, viewport.View.Zoom);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DragPanCanMoveFromTheInitialPositionInEveryDirection()
    {
        using var renderer = new CetzDocumentRenderer(RendererOptions());
        using var viewport = new CetzViewport();
        var window = Show(viewport);
        try
        {
            viewport.View.SetDocument(await RenderDocument(renderer));
            Flush();
            var start = viewport.Offset;
            var pointer = new Point(300, 250);
            var presses = 0;
            window.PointerPressed += (_, _) => presses++;
            window.MouseMove(pointer, RawInputModifiers.None);

            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(pointer + new Vector(80, 60), RawInputModifiers.LeftMouseButton);
            window.MouseUp(pointer + new Vector(80, 60), MouseButton.Left, RawInputModifiers.None);
            Assert.True(viewport.Offset.X < start.X,
                $"presses={presses}, start={start}, actual={viewport.Offset}, extent={viewport.Extent}, scrollViewport={viewport.Viewport}, client={window.ClientSize}, viewport={viewport.Bounds}");
            Assert.True(viewport.Offset.Y < start.Y);

            var moved = viewport.Offset;
            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(pointer - new Vector(80, 60), RawInputModifiers.LeftMouseButton);
            window.MouseUp(pointer - new Vector(80, 60), MouseButton.Left, RawInputModifiers.None);
            Assert.True(viewport.Offset.X > moved.X);
            Assert.True(viewport.Offset.Y > moved.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RenderedDocumentProducesAHeadlessFrame()
    {
        using var renderer = new CetzDocumentRenderer(RendererOptions());
        using var viewport = new CetzViewport();
        var window = Show(viewport);
        try
        {
            viewport.View.SetDocument(await RenderDocument(renderer));
            Flush();

            using var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.True(frame.PixelSize.Width > 0);
            Assert.True(frame.PixelSize.Height > 0);
            Assert.True(ContainsDifferentPixels(frame));
        }
        finally { window.Close(); }
    }

    private static Window Show(CetzViewport viewport)
    {
        var window = new Window { Width = 800, Height = 600, Content = viewport };
        window.Show();
        Flush();
        return window;
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static unsafe bool ContainsDifferentPixels(global::Avalonia.Media.Imaging.WriteableBitmap bitmap)
    {
        using var framebuffer = bitmap.Lock();
        var pixels = new ReadOnlySpan<uint>(
            framebuffer.Address.ToPointer(),
            framebuffer.RowBytes / sizeof(uint) * framebuffer.Size.Height);
        var first = pixels[0];
        return pixels[1..].ContainsAnyExcept(first);
    }

    private static Task<CetzRenderedDocument> RenderDocument(CetzDocumentRenderer renderer) =>
        renderer.RenderSourceAsync(
            "#set page(width: 400pt, height: 500pt, margin: 20pt)\n#set text(size: 36pt)\nCeTZ headless zoom test",
            options: new CetzDocumentRenderOptions { Ppi = 96 });

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
