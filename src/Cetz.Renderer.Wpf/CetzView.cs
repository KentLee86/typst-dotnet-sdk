using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.Wpf;

/// <summary>
/// WPF drawing adapter for the framework-neutral <see cref="CetzDocumentViewController"/>.
/// Place the view inside a <see cref="System.Windows.Controls.ScrollViewer"/> for scrolling.
/// </summary>
public sealed class CetzView : FrameworkElement, ICetzDocumentView, IDisposable
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(CetzRenderedDocument), typeof(CetzView),
        new FrameworkPropertyMetadata(default(CetzRenderedDocument), OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CetzView),
        new FrameworkPropertyMetadata(CetzDocumentViewController.DefaultZoom, OnZoomChanged));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing), typeof(double), typeof(CetzView),
        new FrameworkPropertyMetadata(CetzDocumentViewController.DefaultPageSpacing, OnPageSpacingChanged));

    public static readonly DependencyProperty ZoomModeProperty = DependencyProperty.Register(
        nameof(ZoomMode), typeof(CetzZoomMode), typeof(CetzView),
        new FrameworkPropertyMetadata(CetzZoomMode.Custom, OnZoomModeChanged));

    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(
        nameof(ViewMode), typeof(CetzPageViewMode), typeof(CetzView),
        new FrameworkPropertyMetadata(CetzPageViewMode.ContinuousSingle, OnViewModeChanged));

    private readonly CetzDocumentViewController _controller = new();
    private readonly Dictionary<int, BitmapSource> _bitmaps = [];
    private CetzRenderedDocument? _bitmapDocument;
    private Rect? _visibleRegion;
    private int _overscanPages = 1;
    private bool _synchronizing;
    private bool _disposed;

    public CetzView()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _controller.Changed += ControllerChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CetzRenderedDocument? Document
    {
        get => _controller.Document;
        set => SetCurrentValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => _controller.Zoom;
        set => SetCurrentValue(ZoomProperty, value);
    }

    public double PageSpacing
    {
        get => _controller.PageSpacing;
        set => SetCurrentValue(PageSpacingProperty, value);
    }

    public CetzZoomMode ZoomMode
    {
        get => _controller.ZoomMode;
        set => SetCurrentValue(ZoomModeProperty, value);
    }

    public CetzPageViewMode ViewMode
    {
        get => _controller.ViewMode;
        set => SetCurrentValue(ViewModeProperty, value);
    }

    public int CurrentPageIndex => _controller.CurrentPageIndex;
    public int PageCount => _controller.PageCount;
    public CetzDocumentViewLayout Layout => _controller.Layout;
    public int RealizedPageCount => _bitmaps.Count;
    public IReadOnlyCollection<int> RealizedPageIndices => _bitmaps.Keys.Order().ToArray();

    public void SetDocument(CetzRenderedDocument document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        SetCurrentValue(DocumentProperty, document);
    }

    public void SetZoom(double zoom)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetCurrentValue(ZoomProperty, zoom);
    }

    public void SetZoomMode(CetzZoomMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetCurrentValue(ZoomModeProperty, mode);
    }

    public void SetViewMode(CetzPageViewMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetCurrentValue(ViewModeProperty, mode);
    }

    public void SetViewport(double width, double height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetViewport(width, height);
    }

    public void SetPageSpacing(double pageSpacing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetCurrentValue(PageSpacingProperty, pageSpacing);
    }

    public void GoToPage(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.GoToPage(pageIndex);
        BringCurrentPageIntoView();
    }

    public bool TrackCurrentPage(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _controller.TrackCurrentPage(pageIndex);
    }

    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var moved = _controller.MoveNext();
        if (moved) BringCurrentPageIntoView();
        return moved;
    }

    public bool MovePrevious()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var moved = _controller.MovePrevious();
        if (moved) BringCurrentPageIntoView();
        return moved;
    }

    public void SetVisibleRegion(Rect region, int overscanPages = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(overscanPages);
        _visibleRegion = region;
        _overscanPages = overscanPages;
        RefreshBitmaps();
    }

    public void ClearVisibleRegion()
    {
        _visibleRegion = null;
        RefreshBitmaps();
    }

    public void ReleaseDocument()
    {
        if (_disposed) return;
        ClearValue(DocumentProperty);
        _controller.ReleaseDocument();
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(Layout.ExtentWidth, Layout.ExtentHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Document is null) return;

        foreach (var page in Layout.Pages)
            if (_bitmaps.TryGetValue(page.PageIndex, out var bitmap))
                drawingContext.DrawImage(bitmap, new Rect(page.X, page.Y, page.Width, page.Height));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Changed -= ControllerChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _controller.ReleaseDocument();
        ClearBitmaps();
        ClearValue(DocumentProperty);
        GC.SuppressFinalize(this);
    }

    private static void OnDocumentChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)owner;
        if (view._synchronizing || view._disposed) return;
        if (args.NewValue is CetzRenderedDocument document)
            view._controller.SetDocument(document);
        else
            view._controller.ReleaseDocument();
    }

    private static void OnZoomChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)owner;
        if (!view._synchronizing && !view._disposed)
        {
            view._controller.SetZoom((double)args.NewValue);
            view.SynchronizeDependencyProperties();
        }
    }

    private static void OnPageSpacingChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)owner;
        if (!view._synchronizing && !view._disposed)
        {
            view._controller.SetPageSpacing((double)args.NewValue);
            view.SynchronizeDependencyProperties();
        }
    }

    private static void OnZoomModeChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)owner;
        if (!view._synchronizing && !view._disposed)
            view._controller.SetZoomMode((CetzZoomMode)args.NewValue);
    }

    private static void OnViewModeChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)owner;
        if (!view._synchronizing && !view._disposed)
            view._controller.SetViewMode((CetzPageViewMode)args.NewValue);
    }

    private void ControllerChanged(object? sender, EventArgs args)
    {
        SynchronizeDependencyProperties();

        if (!ReferenceEquals(_bitmapDocument, _controller.Document))
            RebuildBitmaps();
        else
            RefreshBitmaps();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SynchronizeDependencyProperties()
    {
        _synchronizing = true;
        try
        {
            SetCurrentValue(DocumentProperty, _controller.Document);
            SetCurrentValue(ZoomProperty, _controller.Zoom);
            SetCurrentValue(PageSpacingProperty, _controller.PageSpacing);
            SetCurrentValue(ZoomModeProperty, _controller.ZoomMode);
            SetCurrentValue(ViewModeProperty, _controller.ViewMode);
        }
        finally
        {
            _synchronizing = false;
        }

    }

    private void BringCurrentPageIntoView()
    {
        var page = Layout.Pages.FirstOrDefault(candidate => candidate.PageIndex == CurrentPageIndex);
        if (page.Width > 0 && page.Height > 0)
            BringIntoView(new Rect(page.X, page.Y, page.Width, page.Height));
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_disposed && _bitmaps.Count == 0 && Document is not null)
            RebuildBitmaps();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ClearBitmaps();

    private void RebuildBitmaps()
    {
        ClearBitmaps();
        _bitmapDocument = Document;
        if (_disposed || !IsLoaded || Document is null) return;
        RefreshBitmaps();
    }

    private void RefreshBitmaps()
    {
        if (_disposed || !IsLoaded || Document is null) return;
        var desired = DesiredPageIndices().ToHashSet();
        foreach (var pageIndex in _bitmaps.Keys.Where(index => !desired.Contains(index)).ToArray())
            _bitmaps.Remove(pageIndex);
        foreach (var pageIndex in desired.Where(index => !_bitmaps.ContainsKey(index)))
            _bitmaps.Add(pageIndex, CetzBitmapSource.Create(Document.Pages[pageIndex]));
        InvalidateVisual();
    }

    private IReadOnlyList<int> DesiredPageIndices()
    {
        if (_visibleRegion is not { } region)
            return Layout.Pages.Select(page => page.PageIndex).ToArray();
        return CetzVisiblePageSelector.Select(
            Layout, region.X, region.Y, region.Width, region.Height, _overscanPages);
    }

    private void ClearBitmaps()
    {
        _bitmaps.Clear();
        _bitmapDocument = null;
        InvalidateMeasure();
        InvalidateVisual();
    }
}
