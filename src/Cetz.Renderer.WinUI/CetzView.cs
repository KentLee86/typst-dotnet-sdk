using System.Runtime.InteropServices.WindowsRuntime;
using Cetz.Renderer.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cetz.Renderer.WinUI;

/// <summary>
/// WinUI 3 adapter for the common document-view controller. Core owns every
/// layout and navigation decision; this class only owns WinUI image resources,
/// dispatcher access, and scrolling the selected Core placement into view.
/// </summary>
public sealed class CetzView : Grid, ICetzDocumentView, IDisposable
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(CetzRenderedDocument), typeof(CetzView),
        new PropertyMetadata(null, OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CetzView),
        new PropertyMetadata(CetzDocumentViewController.DefaultZoom, OnZoomChanged));

    public static readonly DependencyProperty ZoomModeProperty = DependencyProperty.Register(
        nameof(ZoomMode), typeof(CetzZoomMode), typeof(CetzView),
        new PropertyMetadata(CetzZoomMode.Custom, OnZoomModeChanged));

    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(
        nameof(ViewMode), typeof(CetzPageViewMode), typeof(CetzView),
        new PropertyMetadata(CetzPageViewMode.ContinuousSingle, OnViewModeChanged));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing), typeof(double), typeof(CetzView),
        new PropertyMetadata(CetzDocumentViewController.DefaultPageSpacing, OnPageSpacingChanged));

    private readonly CetzDocumentViewController _controller = new();
    private readonly ScrollViewer _scrollViewer;
    private readonly Canvas _pageCanvas;
    private readonly WinUiPageResourceSet<WinUiPageResource> _pageResources = new();
    private bool _syncingProperties;
    private bool _disposed;

    public CetzView()
    {
        _pageCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _scrollViewer = new ScrollViewer
        {
            Content = _pageCanvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        _scrollViewer.SizeChanged += ScrollViewerOnSizeChanged;
        _controller.Changed += ControllerOnChanged;
        Children.Add(_scrollViewer);
    }

    public CetzRenderedDocument? Document
    {
        get => _controller.Document;
        set
        {
            ThrowIfDisposed();
            if (value is null) ReleaseDocument();
            else SetDocument(value);
        }
    }

    public double Zoom
    {
        get => _controller.Zoom;
        set => SetZoom(value);
    }

    public CetzZoomMode ZoomMode
    {
        get => _controller.ZoomMode;
        set => SetZoomMode(value);
    }

    public CetzPageViewMode ViewMode
    {
        get => _controller.ViewMode;
        set => SetViewMode(value);
    }

    public double PageSpacing
    {
        get => _controller.PageSpacing;
        set => SetPageSpacing(value);
    }

    public int CurrentPageIndex => _controller.CurrentPageIndex;
    public int PageCount => _controller.PageCount;
    public CetzDocumentViewLayout Layout => _controller.Layout;

    public void SetDocument(CetzRenderedDocument document)
    {
        VerifyAccess();
        _controller.SetDocument(document);
    }

    public void SetZoom(double zoom)
    {
        VerifyAccess();
        _controller.SetZoom(zoom);
    }

    public void SetZoomMode(CetzZoomMode mode)
    {
        VerifyAccess();
        _controller.SetZoomMode(mode);
    }

    public void SetViewMode(CetzPageViewMode mode)
    {
        VerifyAccess();
        _controller.SetViewMode(mode);
        ScrollCurrentPageIntoView();
    }

    public void SetViewport(double width, double height)
    {
        VerifyAccess();
        _controller.SetViewport(width, height);
    }

    public void SetPageSpacing(double pageSpacing)
    {
        VerifyAccess();
        _controller.SetPageSpacing(pageSpacing);
    }

    public void GoToPage(int pageIndex)
    {
        VerifyAccess();
        _controller.GoToPage(pageIndex);
        ScrollCurrentPageIntoView();
    }

    public bool MoveNext()
    {
        VerifyAccess();
        var moved = _controller.MoveNext();
        if (moved) ScrollCurrentPageIntoView();
        return moved;
    }

    public bool MovePrevious()
    {
        VerifyAccess();
        var moved = _controller.MovePrevious();
        if (moved) ScrollCurrentPageIntoView();
        return moved;
    }

    public void ReleaseDocument()
    {
        VerifyAccess();
        _controller.ReleaseDocument();
    }

    /// <summary>Assigns or releases a document from any thread.</summary>
    public Task SetDocumentAsync(CetzRenderedDocument? document)
    {
        ThrowIfDisposed();
        if (DispatcherQueue.HasThreadAccess)
        {
            if (document is null) ReleaseDocument(); else SetDocument(document);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    if (document is null) ReleaseDocument(); else SetDocument(document);
                    completion.SetResult();
                }
                catch (Exception exception) { completion.SetException(exception); }
            }))
            completion.SetException(new InvalidOperationException("The WinUI dispatcher is shutting down."));
        return completion.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        VerifyAccess();
        _controller.ReleaseDocument();
        _disposed = true;
        _controller.Changed -= ControllerOnChanged;
        _scrollViewer.SizeChanged -= ScrollViewerOnSizeChanged;
        ClearPageResources();
        _scrollViewer.Content = null;
        Children.Clear();
        GC.SuppressFinalize(this);
    }

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (view._syncingProperties || view._disposed) return;
        if (args.NewValue is CetzRenderedDocument document) view._controller.SetDocument(document);
        else view._controller.ReleaseDocument();
    }

    private static void OnZoomChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._syncingProperties && !view._disposed) view._controller.SetZoom((double)args.NewValue);
    }

    private static void OnZoomModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._syncingProperties && !view._disposed) view._controller.SetZoomMode((CetzZoomMode)args.NewValue);
    }

    private static void OnViewModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (view._syncingProperties || view._disposed) return;
        view._controller.SetViewMode((CetzPageViewMode)args.NewValue);
        view.ScrollCurrentPageIntoView();
    }

    private static void OnPageSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._syncingProperties && !view._disposed) view._controller.SetPageSpacing((double)args.NewValue);
    }

    private void ControllerOnChanged(object? sender, EventArgs args)
    {
        SyncDependencyProperties();
        ApplyLayout();
    }

    private void SyncDependencyProperties()
    {
        _syncingProperties = true;
        try
        {
            SetValue(DocumentProperty, _controller.Document);
            SetValue(ZoomProperty, _controller.Zoom);
            SetValue(ZoomModeProperty, _controller.ZoomMode);
            SetValue(ViewModeProperty, _controller.ViewMode);
            SetValue(PageSpacingProperty, _controller.PageSpacing);
        }
        finally { _syncingProperties = false; }
    }

    private void ApplyLayout()
    {
        var document = _controller.Document;
        var layout = _controller.Layout;
        if (document is null)
        {
            ClearPageResources();
            _pageCanvas.Width = 0;
            _pageCanvas.Height = 0;
            return;
        }

        _pageResources.RetainOnly(layout.Pages.Select(page => page.PageIndex));

        _pageCanvas.Children.Clear();
        _pageCanvas.Width = layout.ExtentWidth;
        _pageCanvas.Height = layout.ExtentHeight;
        foreach (var placement in layout.Pages)
        {
            var resource = _pageResources.GetOrAdd(placement.PageIndex,
                pageIndex => WinUiPageResource.Create(document.Pages[pageIndex]));
            resource.Image.Width = placement.Width;
            resource.Image.Height = placement.Height;
            Canvas.SetLeft(resource.Image, placement.X);
            Canvas.SetTop(resource.Image, placement.Y);
            _pageCanvas.Children.Add(resource.Image);
        }
    }

    private void ScrollViewerOnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var width = _scrollViewer.ViewportWidth;
        var height = _scrollViewer.ViewportHeight;
        _controller.SetViewport(width, height);
    }

    private void ScrollCurrentPageIntoView()
    {
        var placement = _controller.Layout.Pages.FirstOrDefault(
            page => page.PageIndex == _controller.CurrentPageIndex);
        if (!_controller.Layout.Pages.Any(page => page.PageIndex == _controller.CurrentPageIndex))
            return;
        _scrollViewer.UpdateLayout();
        _scrollViewer.ChangeView(placement.X, placement.Y, null, true);
    }

    private void ClearPageResources()
    {
        _pageCanvas.Children.Clear();
        _pageResources.Clear();
    }

    private void VerifyAccess()
    {
        ThrowIfDisposed();
        if (!DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("CetzView must be accessed on its WinUI thread. Use SetDocumentAsync for background rendering.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal sealed class WinUiPageResource : IDisposable
{
    private object? _source;

    private WinUiPageResource(Image image, object source) => (Image, _source) = (image, source);

    public Image Image { get; }

    public static WinUiPageResource Create(CetzRenderedPage page)
    {
        var bitmap = new WriteableBitmap(page.PixelWidth, page.PixelHeight);
        using (var stream = bitmap.PixelBuffer.AsStream())
            WinUiPixelBuffer.WriteBgraPremultiplied(page, stream);
        bitmap.Invalidate();
        return new WinUiPageResource(new Image { Source = bitmap, Stretch = Stretch.Fill }, bitmap);
    }

    public void Dispose()
    {
        Image.Source = null;
        if (_source is IDisposable disposable) disposable.Dispose();
        _source = null;
    }
}
