using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.Avalonia;

/// <summary>Displays every page of a UI-neutral CeTZ rendered document.</summary>
public sealed class CetzView : Control, ICetzDocumentView, IDisposable
{
    public static readonly StyledProperty<CetzRenderedDocument?> DocumentProperty =
        AvaloniaProperty.Register<CetzView, CetzRenderedDocument?>(nameof(Document));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<CetzView, double>(nameof(Zoom), 1d, coerce: CoerceZoom);

    public static readonly StyledProperty<double> PageSpacingProperty =
        AvaloniaProperty.Register<CetzView, double>(nameof(PageSpacing), CetzDocumentViewController.DefaultPageSpacing,
            coerce: (_, value) => CetzDocumentViewController.NormalizePageSpacing(value));

    public static readonly StyledProperty<CetzZoomMode> ZoomModeProperty =
        AvaloniaProperty.Register<CetzView, CetzZoomMode>(nameof(ZoomMode));

    public static readonly StyledProperty<CetzPageViewMode> ViewModeProperty =
        AvaloniaProperty.Register<CetzView, CetzPageViewMode>(nameof(ViewMode));

    private readonly Dictionary<int, Bitmap> _bitmaps = [];
    private readonly CetzDocumentViewController _controller = new();
    private Rect? _visibleRegion;
    private int _overscanPages = 1;
    private bool _disposed;
    private bool _synchronizingProperties;

    static CetzView()
    {
        AffectsMeasure<CetzView>(DocumentProperty, ZoomProperty, PageSpacingProperty, ZoomModeProperty, ViewModeProperty);
        AffectsRender<CetzView>(DocumentProperty, ZoomProperty, PageSpacingProperty, ZoomModeProperty, ViewModeProperty);
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

    public CetzZoomMode ZoomMode
    {
        get => GetValue(ZoomModeProperty);
        set => SetValue(ZoomModeProperty, value);
    }

    public CetzPageViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    public int CurrentPageIndex => _controller.CurrentPageIndex;
    public int PageCount => _controller.PageCount;
    public CetzDocumentViewLayout Layout => _controller.Layout;
    public int RealizedPageCount => _bitmaps.Count;
    public IReadOnlyCollection<int> RealizedPageIndices => _bitmaps.Keys.Order().ToArray();

    public void SetDocument(CetzRenderedDocument document) => Document = document;
    public void SetZoom(double zoom) => Zoom = zoom;
    public void SetZoomMode(CetzZoomMode mode) => ZoomMode = mode;
    public void SetViewMode(CetzPageViewMode mode) => ViewMode = mode;
    public void SetViewport(double width, double height)
    {
        _controller.SetViewport(width, height);
        SynchronizeProperties();
        InvalidateMeasure();
        InvalidateVisual();
    }
    public void SetPageSpacing(double pageSpacing) => PageSpacing = pageSpacing;
    public void GoToPage(int pageIndex) { _controller.GoToPage(pageIndex); NavigationChanged(); }
    public bool TrackCurrentPage(int pageIndex) => _controller.TrackCurrentPage(pageIndex);
    public bool MoveNext() { var changed = _controller.MoveNext(); if (changed) NavigationChanged(); return changed; }
    public bool MovePrevious() { var changed = _controller.MovePrevious(); if (changed) NavigationChanged(); return changed; }
    public void ReleaseDocument() => Document = null;

    public void SetVisibleRegion(Rect region, int overscanPages = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(overscanPages);
        _visibleRegion = region;
        _overscanPages = overscanPages;
        RefreshBitmaps();
    }

    public void ClearVisibleRegion()
    {
        if (_visibleRegion is null) return;
        _visibleRegion = null;
        RefreshBitmaps();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_synchronizingProperties) return;
        if (change.Property == DocumentProperty)
        {
            var document = change.GetNewValue<CetzRenderedDocument?>();
            if (document is null) _controller.ReleaseDocument(); else _controller.SetDocument(document);
            RebuildBitmaps();
        }
        else if (change.Property == ZoomProperty)
        {
            _controller.SetZoom(change.GetNewValue<double>());
            SynchronizeProperties();
        }
        else if (change.Property == PageSpacingProperty)
        {
            _controller.SetPageSpacing(change.GetNewValue<double>());
        }
        else if (change.Property == ZoomModeProperty)
        {
            _controller.SetZoomMode(change.GetNewValue<CetzZoomMode>());
            SynchronizeProperties();
        }
        else if (change.Property == ViewModeProperty)
            _controller.SetViewMode(change.GetNewValue<CetzPageViewMode>());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(Layout.ExtentWidth, Layout.ExtentHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var document = Document;
        if (document is null)
            return;

        foreach (var placement in Layout.Pages)
        {
            if (!_bitmaps.TryGetValue(placement.PageIndex, out var bitmap)) continue;
            var page = document.Pages[placement.PageIndex];
            context.DrawImage(
                bitmap,
                new Rect(0, 0, page.PixelWidth, page.PixelHeight),
                new Rect(placement.X, placement.Y, placement.Width, placement.Height));
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_bitmaps.Count == 0 && Document is not null)
            RefreshBitmaps();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeBitmaps();
        base.OnDetachedFromVisualTree(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseDocument();
        DisposeBitmaps();
        GC.SuppressFinalize(this);
    }

    private unsafe void RebuildBitmaps()
    {
        DisposeBitmaps();
        RefreshBitmaps();
    }

    private unsafe void RefreshBitmaps()
    {
        if (Document is null)
            return;

        var desired = DesiredPageIndices().ToHashSet();
        var changed = false;
        foreach (var pageIndex in _bitmaps.Keys.Where(index => !desired.Contains(index)).ToArray())
        {
            _bitmaps[pageIndex].Dispose();
            _bitmaps.Remove(pageIndex);
            changed = true;
        }

        foreach (var pageIndex in desired.Where(index => !_bitmaps.ContainsKey(index)))
        {
            var page = Document.Pages[pageIndex];
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
            _bitmaps.Add(pageIndex, bitmap);
            changed = true;
        }

        if (changed) InvalidateVisual();
    }

    private void DisposeBitmaps()
    {
        foreach (var bitmap in _bitmaps.Values)
            bitmap.Dispose();
        _bitmaps.Clear();
    }

    private IReadOnlyList<int> DesiredPageIndices()
    {
        if (_visibleRegion is not { } region)
            return Layout.Pages.Select(page => page.PageIndex).ToArray();
        return CetzVisiblePageSelector.Select(
            Layout, region.X, region.Y, region.Width, region.Height, _overscanPages);
    }

    private static double CoerceZoom(AvaloniaObject owner, double value)
        => CetzDocumentViewController.NormalizeZoom(value);

    private void NavigationChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
        var placement = Layout.Pages.FirstOrDefault(page => page.PageIndex == CurrentPageIndex);
        if (placement.Width <= 0 || placement.Height <= 0) return;
        Dispatcher.UIThread.Post(() => ControlExtensions.BringIntoView(this,
            new Rect(placement.X, placement.Y, placement.Width, placement.Height)));
    }

    private void SynchronizeProperties()
    {
        _synchronizingProperties = true;
        try
        {
            SetCurrentValue(ZoomModeProperty, _controller.ZoomMode);
            SetCurrentValue(ZoomProperty, _controller.Zoom);
            SetCurrentValue(ViewModeProperty, _controller.ViewMode);
            SetCurrentValue(PageSpacingProperty, _controller.PageSpacing);
        }
        finally { _synchronizingProperties = false; }
    }

}
