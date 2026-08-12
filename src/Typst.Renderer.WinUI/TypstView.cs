using System.Runtime.InteropServices.WindowsRuntime;
using Typst.Renderer.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Typst.Renderer.WinUI;

/// <summary>
/// WinUI 3 adapter for the common document-view controller. Core owns every
/// layout and navigation decision; this class only owns WinUI image resources,
/// dispatcher access, and scrolling the selected Core placement into view.
/// </summary>
public sealed class TypstView : Grid, ITypstDocumentView, IDisposable
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(TypstRenderedDocument), typeof(TypstView),
        new PropertyMetadata(null, OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(TypstView),
        new PropertyMetadata(TypstDocumentViewController.DefaultZoom, OnZoomChanged));

    public static readonly DependencyProperty ZoomModeProperty = DependencyProperty.Register(
        nameof(ZoomMode), typeof(TypstZoomMode), typeof(TypstView),
        new PropertyMetadata(TypstZoomMode.Custom, OnZoomModeChanged));

    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(
        nameof(ViewMode), typeof(TypstPageViewMode), typeof(TypstView),
        new PropertyMetadata(TypstPageViewMode.ContinuousSingle, OnViewModeChanged));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing), typeof(double), typeof(TypstView),
        new PropertyMetadata(TypstDocumentViewController.DefaultPageSpacing, OnPageSpacingChanged));

    private readonly TypstDocumentViewController _controller = new();
    private readonly TypstViewportInteractionController _viewportInteraction;
    private readonly ScrollViewer _scrollViewer;
    private readonly Canvas _pageCanvas;
    private readonly WinUiPageResourceSet<WinUiPageResource> _pageResources = new();
    private TypstRenderedDocument? _resourceDocument;
    private bool _syncingProperties;
    private bool _disposed;
    private double _reportedZoom = TypstDocumentViewController.DefaultZoom;

    public TypstView()
    {
        _viewportInteraction = new TypstViewportInteractionController(this);
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
        _scrollViewer.ViewChanged += ScrollViewerOnViewChanged;
        _pageCanvas.AddHandler(PointerPressedEvent, new PointerEventHandler(BeginPan), true);
        _pageCanvas.AddHandler(PointerMovedEvent, new PointerEventHandler(ContinuePan), true);
        _pageCanvas.AddHandler(PointerReleasedEvent, new PointerEventHandler(EndPan), true);
        _pageCanvas.PointerCaptureLost += OnPointerCaptureLost;
        _scrollViewer.PointerWheelChanged += ZoomWithWheel;
        _controller.Changed += ControllerOnChanged;
        Children.Add(_scrollViewer);
    }

    public TypstRenderedDocument? Document
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

    public TypstZoomMode ZoomMode
    {
        get => _controller.ZoomMode;
        set => SetZoomMode(value);
    }

    public TypstPageViewMode ViewMode
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
    public TypstDocumentViewLayout Layout => _controller.Layout;
    public int RealizedPageCount => _pageResources.Count;
    public IReadOnlyCollection<int> RealizedPageIndices => _pageResources.PageIndices;

    public event EventHandler? CurrentPageChanged;
    public event EventHandler? ZoomChanged;

    public void SetDocument(TypstRenderedDocument document)
    {
        VerifyAccess();
        _controller.SetDocument(document);
    }

    public void SetZoom(double zoom)
    {
        VerifyAccess();
        _controller.SetZoom(zoom);
    }

    public void SetZoomMode(TypstZoomMode mode)
    {
        VerifyAccess();
        _controller.SetZoomMode(mode);
    }

    public void SetViewMode(TypstPageViewMode mode)
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

    public bool TrackCurrentPage(int pageIndex)
    {
        VerifyAccess();
        return _controller.TrackCurrentPage(pageIndex);
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
    public Task SetDocumentAsync(TypstRenderedDocument? document)
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
        _scrollViewer.ViewChanged -= ScrollViewerOnViewChanged;
        _pageCanvas.RemoveHandler(PointerPressedEvent, new PointerEventHandler(BeginPan));
        _pageCanvas.RemoveHandler(PointerMovedEvent, new PointerEventHandler(ContinuePan));
        _pageCanvas.RemoveHandler(PointerReleasedEvent, new PointerEventHandler(EndPan));
        _pageCanvas.PointerCaptureLost -= OnPointerCaptureLost;
        _scrollViewer.PointerWheelChanged -= ZoomWithWheel;
        ClearPageResources();
        _scrollViewer.Content = null;
        Children.Clear();
        GC.SuppressFinalize(this);
    }

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (view._syncingProperties || view._disposed) return;
        if (args.NewValue is TypstRenderedDocument document) view._controller.SetDocument(document);
        else view._controller.ReleaseDocument();
    }

    private static void OnZoomChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (!view._syncingProperties && !view._disposed) view._controller.SetZoom((double)args.NewValue);
    }

    private static void OnZoomModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (!view._syncingProperties && !view._disposed) view._controller.SetZoomMode((TypstZoomMode)args.NewValue);
    }

    private static void OnViewModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (view._syncingProperties || view._disposed) return;
        view._controller.SetViewMode((TypstPageViewMode)args.NewValue);
        view.ScrollCurrentPageIntoView();
    }

    private static void OnPageSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (TypstView)sender;
        if (!view._syncingProperties && !view._disposed) view._controller.SetPageSpacing((double)args.NewValue);
    }

    private void ControllerOnChanged(object? sender, EventArgs args)
    {
        SyncDependencyProperties();
        ApplyLayout();
        if (Math.Abs(_reportedZoom - _controller.Zoom) > double.Epsilon)
        {
            _reportedZoom = _controller.Zoom;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
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
            _resourceDocument = null;
            _pageCanvas.Width = 0;
            _pageCanvas.Height = 0;
            return;
        }

        if (!ReferenceEquals(_resourceDocument, document))
        {
            ClearPageResources();
            _resourceDocument = document;
        }

        var desired = VisiblePageIndices(layout);
        _pageResources.RetainOnly(desired);

        _pageCanvas.Children.Clear();
        _pageCanvas.Width = layout.ExtentWidth;
        _pageCanvas.Height = layout.ExtentHeight;
        foreach (var placement in layout.Pages)
        {
            if (!desired.Contains(placement.PageIndex)) continue;
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
        UpdateViewportState();
    }

    private void ScrollViewerOnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) =>
        UpdateViewportState();

    private void UpdateViewportState()
    {
        if (_disposed || _controller.Document is null) return;
        var pageIndex = TypstVisiblePageSelector.SelectCurrentPage(
            _controller.Layout,
            _scrollViewer.HorizontalOffset,
            _scrollViewer.VerticalOffset,
            _scrollViewer.ViewportWidth,
            _scrollViewer.ViewportHeight);
        if (pageIndex is { } selected && _controller.TrackCurrentPage(selected))
            CurrentPageChanged?.Invoke(this, EventArgs.Empty);
        ApplyLayout();
    }

    private HashSet<int> VisiblePageIndices(TypstDocumentViewLayout layout)
    {
        if (_scrollViewer.ViewportWidth <= 0 || _scrollViewer.ViewportHeight <= 0)
            return layout.Pages.Select(page => page.PageIndex).ToHashSet();
        return TypstVisiblePageSelector.Select(
            layout,
            _scrollViewer.HorizontalOffset,
            _scrollViewer.VerticalOffset,
            _scrollViewer.ViewportWidth,
            _scrollViewer.ViewportHeight,
            overscanPages: 1).ToHashSet();
    }

    private void BeginPan(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_scrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _pageCanvas.CancelDirectManipulations();
        if (!_pageCanvas.CapturePointer(args.Pointer))
            return;

        _viewportInteraction.BeginPan(
            point.Position.X,
            point.Position.Y,
            _scrollViewer.HorizontalOffset,
            _scrollViewer.VerticalOffset);
        args.Handled = true;
    }

    private void ContinuePan(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_scrollViewer);
        if (!point.Properties.IsLeftButtonPressed ||
            !_viewportInteraction.TryPanTo(point.Position.X, point.Position.Y, out var offset))
            return;

        _scrollViewer.ChangeView(offset.X, offset.Y, null, true);
        args.Handled = true;
    }

    private void EndPan(object sender, PointerRoutedEventArgs args)
    {
        if (!_viewportInteraction.IsPanning)
            return;

        _viewportInteraction.EndPan();
        _pageCanvas.ReleasePointerCapture(args.Pointer);
        args.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) =>
        _viewportInteraction.EndPan();

    private void ZoomWithWheel(object sender, PointerRoutedEventArgs args)
    {
        if ((args.KeyModifiers & VirtualKeyModifiers.Control) == 0)
            return;

        var point = args.GetCurrentPoint(_scrollViewer);
        var offset = _viewportInteraction.ZoomByWheel(
            point.Properties.MouseWheelDelta,
            point.Position.X,
            point.Position.Y,
            _scrollViewer.HorizontalOffset,
            _scrollViewer.VerticalOffset);
        DispatcherQueue.TryEnqueue(() => _scrollViewer.ChangeView(offset.X, offset.Y, null, true));
        args.Handled = true;
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
            throw new InvalidOperationException("TypstView must be accessed on its WinUI thread. Use SetDocumentAsync for background rendering.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal sealed class WinUiPageResource : IDisposable
{
    private object? _source;

    private WinUiPageResource(Image image, object source) => (Image, _source) = (image, source);

    public Image Image { get; }

    public static WinUiPageResource Create(TypstRenderedPage page)
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
