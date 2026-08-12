using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.Avalonia;

/// <summary>Displays every page of a UI-neutral CeTZ rendered document.</summary>
public sealed class CetzView : Control
{
    public static readonly StyledProperty<CetzRenderedDocument?> DocumentProperty =
        AvaloniaProperty.Register<CetzView, CetzRenderedDocument?>(nameof(Document));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<CetzView, double>(nameof(Zoom), 1d, coerce: CoerceZoom);

    public static readonly StyledProperty<double> PageSpacingProperty =
        AvaloniaProperty.Register<CetzView, double>(nameof(PageSpacing), 24d);

    private readonly List<Bitmap> _bitmaps = [];

    static CetzView()
    {
        AffectsMeasure<CetzView>(DocumentProperty, ZoomProperty, PageSpacingProperty);
        AffectsRender<CetzView>(DocumentProperty, ZoomProperty, PageSpacingProperty);
    }

    public CetzRenderedDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double PageSpacing
    {
        get => GetValue(PageSpacingProperty);
        set => SetValue(PageSpacingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
            RebuildBitmaps();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var document = Document;
        if (document is null || document.Pages.Count == 0)
            return default;

        var width = document.Pages.Max(page => page.Width) * Zoom;
        var height = document.Pages.Sum(page => page.Height) * Zoom +
            Math.Max(0, document.Pages.Count - 1) * PageSpacing;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var document = Document;
        if (document is null || _bitmaps.Count != document.Pages.Count)
            return;

        var y = 0d;
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var size = new Size(page.Width * Zoom, page.Height * Zoom);
            context.DrawImage(
                _bitmaps[index],
                new Rect(0, 0, page.PixelWidth, page.PixelHeight),
                new Rect(new Point(0, y), size));
            y += size.Height + PageSpacing;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_bitmaps.Count == 0 && Document is not null)
            RebuildBitmaps();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeBitmaps();
        base.OnDetachedFromVisualTree(e);
    }

    private unsafe void RebuildBitmaps()
    {
        DisposeBitmaps();
        if (Document is null)
            return;

        foreach (var page in Document.Pages)
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(page.PixelWidth, page.PixelHeight),
                new Vector(page.Ppi, page.Ppi),
                PixelFormat.Rgba8888,
                AlphaFormat.Premul);
            using (var framebuffer = bitmap.Lock())
            {
                var source = page.Pixels.Span;
                var destination = new Span<byte>(
                    framebuffer.Address.ToPointer(),
                    checked(framebuffer.RowBytes * page.PixelHeight));
                for (var row = 0; row < page.PixelHeight; row++)
                {
                    source.Slice(row * page.Stride, page.PixelWidth * 4)
                        .CopyTo(destination.Slice(row * framebuffer.RowBytes, page.PixelWidth * 4));
                }
            }
            _bitmaps.Add(bitmap);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void DisposeBitmaps()
    {
        foreach (var bitmap in _bitmaps)
            bitmap.Dispose();
        _bitmaps.Clear();
    }

    private static double CoerceZoom(AvaloniaObject owner, double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0.1, 8) : 1;
}
