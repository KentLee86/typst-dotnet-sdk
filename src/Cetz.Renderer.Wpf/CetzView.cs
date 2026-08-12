using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.Wpf;

/// <summary>
/// Displays every page of a UI-neutral CeTZ rendered document. Place the view
/// inside a <see cref="System.Windows.Controls.ScrollViewer"/> for scrolling.
/// </summary>
public sealed class CetzView : FrameworkElement, IDisposable
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(CetzRenderedDocument),
        typeof(CetzView),
        new FrameworkPropertyMetadata(
            default(CetzRenderedDocument),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(CetzView),
        new FrameworkPropertyMetadata(
            1d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            propertyChangedCallback: null,
            coerceValueCallback: CoerceZoom));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing),
        typeof(double),
        typeof(CetzView),
        new FrameworkPropertyMetadata(
            24d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            propertyChangedCallback: null,
            coerceValueCallback: CoercePageSpacing));

    private readonly List<BitmapSource> _bitmaps = [];
    private bool _disposed;

    public CetzView()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CetzRenderedDocument? Document
    {
        get => (CetzRenderedDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Scale applied to device-independent page dimensions, clamped to 0.1-8.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Device-independent gap between pages, clamped to 0-1000.</summary>
    public double PageSpacing
    {
        get => (double)GetValue(PageSpacingProperty);
        set => SetValue(PageSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var document = Document;
        if (document is null || document.Pages.Count == 0)
            return default;

        var width = document.Pages.Max(page => page.Width) * Zoom;
        var height = document.Pages.Sum(page => page.Height) * Zoom
            + Math.Max(0, document.Pages.Count - 1) * PageSpacing;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var document = Document;
        if (document is null || _bitmaps.Count != document.Pages.Count)
            return;

        var contentWidth = document.Pages.Max(page => page.Width) * Zoom;
        var availableWidth = Math.Max(ActualWidth, contentWidth);
        var y = 0d;
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var width = page.Width * Zoom;
            var height = page.Height * Zoom;
            var x = (availableWidth - width) / 2d;
            drawingContext.DrawImage(_bitmaps[index], new Rect(x, y, width, height));
            y += height + PageSpacing;
        }
    }

    /// <summary>Releases cached page images and the document reference.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ClearBitmaps();
        ClearValue(DocumentProperty);
        GC.SuppressFinalize(this);
    }

    private static void OnDocumentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        => ((CetzView)dependencyObject).RebuildBitmaps();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_disposed && _bitmaps.Count == 0 && Document is not null)
            RebuildBitmaps();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ClearBitmaps();

    private void RebuildBitmaps()
    {
        ClearBitmaps();
        if (_disposed || !IsLoaded || Document is null)
            return;

        foreach (var page in Document.Pages)
            _bitmaps.Add(CetzBitmapSource.Create(page));
    }

    private void ClearBitmaps()
    {
        _bitmaps.Clear();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static object CoerceZoom(DependencyObject owner, object value)
    {
        var zoom = (double)value;
        return double.IsFinite(zoom) ? Math.Clamp(zoom, 0.1d, 8d) : 1d;
    }

    private static object CoercePageSpacing(DependencyObject owner, object value)
    {
        var spacing = (double)value;
        return double.IsFinite(spacing) ? Math.Clamp(spacing, 0d, 1000d) : 24d;
    }
}
