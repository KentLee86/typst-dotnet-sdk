using System.Runtime.InteropServices.WindowsRuntime;
using Typst.Renderer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Typst.Renderer.Uno;

/// <summary>
/// Displays a framework-neutral CeTZ document using WinUI/Uno images.
/// All document state and page placement come from <see cref="TypstDocumentViewController"/>;
/// this adapter owns only the Uno visual and bitmap resources.
/// </summary>
public sealed class TypstView : Canvas, ITypstDocumentView, IDisposable
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(TypstRenderedDocument),
        typeof(TypstView),
        new PropertyMetadata(null, OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(TypstView),
        new PropertyMetadata(TypstDocumentViewController.DefaultZoom, OnZoomChanged));

    public static readonly DependencyProperty ZoomModeProperty = DependencyProperty.Register(
        nameof(ZoomMode),
        typeof(TypstZoomMode),
        typeof(TypstView),
        new PropertyMetadata(TypstZoomMode.Custom, OnZoomModeChanged));

    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(
        nameof(ViewMode),
        typeof(TypstPageViewMode),
        typeof(TypstView),
        new PropertyMetadata(TypstPageViewMode.ContinuousSingle, OnViewModeChanged));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing),
        typeof(double),
        typeof(TypstView),
        new PropertyMetadata(TypstDocumentViewController.DefaultPageSpacing, OnPageSpacingChanged));

    private readonly TypstDocumentViewController _controller = new();
    private readonly Dictionary<int, PageResource> _pageResources = [];
    private (double X, double Y, double Width, double Height)? _visibleRegion;
    private int _overscanPages = 1;
    private bool _synchronizingProperties;
    private bool _disposed;

    public TypstView()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        _controller.Changed += OnControllerChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public TypstRenderedDocument? Document
    {
        get => _controller.Document;
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => _controller.Zoom;
        set => SetValue(ZoomProperty, value);
    }

    public TypstZoomMode ZoomMode
    {
        get => _controller.ZoomMode;
        set => SetValue(ZoomModeProperty, value);
    }

    public TypstPageViewMode ViewMode
    {
        get => _controller.ViewMode;
        set => SetValue(ViewModeProperty, value);
    }

    public double PageSpacing
    {
        get => _controller.PageSpacing;
        set => SetValue(PageSpacingProperty, value);
    }

    public int CurrentPageIndex => _controller.CurrentPageIndex;
    public int PageCount => _controller.PageCount;
    public TypstDocumentViewLayout Layout => _controller.Layout;
    public int RealizedPageCount => _pageResources.Count;
    public IReadOnlyCollection<int> RealizedPageIndices => _pageResources.Keys.Order().ToArray();

    public void SetDocument(TypstRenderedDocument document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
    }

    public void SetZoom(double zoom)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Zoom = zoom;
    }

    public void SetZoomMode(TypstZoomMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ZoomMode = mode;
    }

    public void SetViewMode(TypstPageViewMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ViewMode = mode;
    }

    public void SetViewport(double width, double height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetViewport(width, height);
    }

    public void SetPageSpacing(double pageSpacing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PageSpacing = pageSpacing;
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
        if (moved)
            BringCurrentPageIntoView();
        return moved;
    }

    public bool MovePrevious()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var moved = _controller.MovePrevious();
        if (moved)
            BringCurrentPageIntoView();
        return moved;
    }

    public void SetVisibleRegion(double x, double y, double width, double height, int overscanPages = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(overscanPages);
        _visibleRegion = (x, y, width, height);
        _overscanPages = overscanPages;
        RefreshPageResources();
    }

    public void ReleaseDocument()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Document = null;
    }

    void IDisposable.Dispose()
    {
        if (_disposed)
            return;

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _controller.Changed -= OnControllerChanged;
        ReleasePageResources();
        _controller.ReleaseDocument();
        _synchronizingProperties = true;
        SetValue(DocumentProperty, null);
        _synchronizingProperties = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void RebuildPageResources()
    {
        ReleasePageResources();
        if (_disposed || _controller.Document is null)
            return;
        RefreshPageResources();
    }

    private void RefreshPageResources()
    {
        if (_disposed || _controller.Document is null) return;
        var desired = DesiredPageIndices().ToHashSet();
        foreach (var pageIndex in _pageResources.Keys.Where(index => !desired.Contains(index)).ToArray())
        {
            Children.Remove(_pageResources[pageIndex].Image);
            _pageResources[pageIndex].Release();
            _pageResources.Remove(pageIndex);
        }
        foreach (var pageIndex in desired.Where(index => !_pageResources.ContainsKey(index)))
        {
            var page = _controller.Document.Pages[pageIndex];
            var pixels = TypstUnoPixelConverter.ToBgra8Premultiplied(
                page.Pixels.Span,
                page.PixelWidth,
                page.PixelHeight,
                page.Stride);
            var bitmap = new WriteableBitmap(page.PixelWidth, page.PixelHeight);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Position = 0;
                stream.Write(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            _pageResources.Add(pageIndex, new PageResource(image, bitmap));
            Children.Add(image);
        }

        ApplyLayout();
    }

    private void ApplyLayout()
    {
        Width = _controller.Layout.ExtentWidth;
        Height = _controller.Layout.ExtentHeight;

        foreach (var resource in _pageResources.Values)
            resource.Image.Visibility = Visibility.Collapsed;

        foreach (var pageLayout in _controller.Layout.Pages)
        {
            if (!_pageResources.TryGetValue(pageLayout.PageIndex, out var resource))
                continue;

            var image = resource.Image;
            image.Visibility = Visibility.Visible;
            image.Width = pageLayout.Width;
            image.Height = pageLayout.Height;
            SetLeft(image, pageLayout.X);
            SetTop(image, pageLayout.Y);
        }
    }

    private void SynchronizeDependencyProperties()
    {
        _synchronizingProperties = true;
        try
        {
            SetValue(ZoomProperty, _controller.Zoom);
            SetValue(ZoomModeProperty, _controller.ZoomMode);
            SetValue(ViewModeProperty, _controller.ViewMode);
            SetValue(PageSpacingProperty, _controller.PageSpacing);
        }
        finally
        {
            _synchronizingProperties = false;
        }
    }

    private void BringCurrentPageIntoView()
    {
        if (_pageResources.TryGetValue(_controller.CurrentPageIndex, out var resource))
            resource.Image.StartBringIntoView();
    }

    private void ReleasePageResources()
    {
        Children.Clear();
        foreach (var resource in _pageResources.Values)
            resource.Release();
        _pageResources.Clear();
    }

    private void OnControllerChanged(object? sender, EventArgs args)
    {
        SynchronizeDependencyProperties();
        ApplyLayout();
        RefreshPageResources();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_disposed && _pageResources.Count == 0 && _controller.Document is not null)
            RebuildPageResources();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleasePageResources();

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (view._synchronizingProperties || view._disposed)
            return;

        if (args.NewValue is TypstRenderedDocument document)
            view._controller.SetDocument(document);
        else
            view._controller.ReleaseDocument();
        view.RebuildPageResources();
    }

    private static void OnZoomChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetZoom((double)args.NewValue);
    }

    private static void OnZoomModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetZoomMode((TypstZoomMode)args.NewValue);
    }

    private static void OnViewModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetViewMode((TypstPageViewMode)args.NewValue);
    }

    private static void OnPageSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetPageSpacing((double)args.NewValue);
    }

    private IReadOnlyList<int> DesiredPageIndices()
    {
        if (_visibleRegion is not { } region)
            return _controller.Layout.Pages.Select(page => page.PageIndex).ToArray();
        return TypstVisiblePageSelector.Select(
            _controller.Layout, region.X, region.Y, region.Width, region.Height, _overscanPages);
    }

    private sealed class PageResource(Image image, WriteableBitmap bitmap)
    {
        private WriteableBitmap? _bitmap = bitmap;

        public Image Image { get; } = image;

        public void Release()
        {
            Image.Source = null;
            _bitmap = null;
        }
    }
}
