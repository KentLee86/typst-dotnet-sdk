using System.Runtime.InteropServices.WindowsRuntime;
using Cetz.Renderer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cetz.Renderer.Uno;

/// <summary>
/// Displays a framework-neutral CeTZ document using WinUI/Uno images.
/// All document state and page placement come from <see cref="CetzDocumentViewController"/>;
/// this adapter owns only the Uno visual and bitmap resources.
/// </summary>
public sealed class CetzView : Canvas, ICetzDocumentView, IDisposable
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(CetzRenderedDocument),
        typeof(CetzView),
        new PropertyMetadata(null, OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(CetzView),
        new PropertyMetadata(CetzDocumentViewController.DefaultZoom, OnZoomChanged));

    public static readonly DependencyProperty ZoomModeProperty = DependencyProperty.Register(
        nameof(ZoomMode),
        typeof(CetzZoomMode),
        typeof(CetzView),
        new PropertyMetadata(CetzZoomMode.Custom, OnZoomModeChanged));

    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(
        nameof(ViewMode),
        typeof(CetzPageViewMode),
        typeof(CetzView),
        new PropertyMetadata(CetzPageViewMode.ContinuousSingle, OnViewModeChanged));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing),
        typeof(double),
        typeof(CetzView),
        new PropertyMetadata(CetzDocumentViewController.DefaultPageSpacing, OnPageSpacingChanged));

    private readonly CetzDocumentViewController _controller = new();
    private readonly List<PageResource> _pageResources = [];
    private bool _synchronizingProperties;
    private bool _disposed;

    public CetzView()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        _controller.Changed += OnControllerChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CetzRenderedDocument? Document
    {
        get => _controller.Document;
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => _controller.Zoom;
        set => SetValue(ZoomProperty, value);
    }

    public CetzZoomMode ZoomMode
    {
        get => _controller.ZoomMode;
        set => SetValue(ZoomModeProperty, value);
    }

    public CetzPageViewMode ViewMode
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
    public CetzDocumentViewLayout Layout => _controller.Layout;

    public void SetDocument(CetzRenderedDocument document)
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

    public void SetZoomMode(CetzZoomMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ZoomMode = mode;
    }

    public void SetViewMode(CetzPageViewMode mode)
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

        foreach (var page in _controller.Document.Pages)
        {
            var pixels = CetzUnoPixelConverter.ToBgra8Premultiplied(
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
            _pageResources.Add(new PageResource(image, bitmap));
            Children.Add(image);
        }

        ApplyLayout();
    }

    private void ApplyLayout()
    {
        Width = _controller.Layout.ExtentWidth;
        Height = _controller.Layout.ExtentHeight;

        foreach (var resource in _pageResources)
            resource.Image.Visibility = Visibility.Collapsed;

        foreach (var pageLayout in _controller.Layout.Pages)
        {
            if ((uint)pageLayout.PageIndex >= (uint)_pageResources.Count)
                continue;

            var image = _pageResources[pageLayout.PageIndex].Image;
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
        if ((uint)_controller.CurrentPageIndex < (uint)_pageResources.Count)
            _pageResources[_controller.CurrentPageIndex].Image.StartBringIntoView();
    }

    private void ReleasePageResources()
    {
        Children.Clear();
        foreach (var resource in _pageResources)
            resource.Release();
        _pageResources.Clear();
    }

    private void OnControllerChanged(object? sender, EventArgs args)
    {
        SynchronizeDependencyProperties();
        ApplyLayout();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_disposed && _pageResources.Count == 0 && _controller.Document is not null)
            RebuildPageResources();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleasePageResources();

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (view._synchronizingProperties || view._disposed)
            return;

        if (args.NewValue is CetzRenderedDocument document)
            view._controller.SetDocument(document);
        else
            view._controller.ReleaseDocument();
        view.RebuildPageResources();
    }

    private static void OnZoomChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetZoom((double)args.NewValue);
    }

    private static void OnZoomModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetZoomMode((CetzZoomMode)args.NewValue);
    }

    private static void OnViewModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetViewMode((CetzPageViewMode)args.NewValue);
    }

    private static void OnPageSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._synchronizingProperties && !view._disposed)
            view._controller.SetPageSpacing((double)args.NewValue);
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
