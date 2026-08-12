using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.Avalonia;

/// <summary>A scroll host with unrestricted drag panning and pointer-anchored Ctrl+wheel zoom.</summary>
public sealed class CetzViewport : Grid, IDisposable
{
    private const double DocumentMargin = 28;
    private readonly Border _workspace;
    private readonly ScrollViewer _scrollViewer;
    private readonly CetzViewportInteractionController _interaction;
    private CetzViewportOffset? _pendingZoomOffset;
    private double _pendingWorkspaceShiftX;
    private double _pendingWorkspaceShiftY;
    private double _workspaceInsetX;
    private double _workspaceInsetY;
    private bool _disposed;

    public CetzViewport()
    {
        View = new CetzView
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _workspace = new Border { Child = View };
        _scrollViewer = new ScrollViewer
        {
            Content = _workspace,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = Brushes.Transparent
        };
        _interaction = new CetzViewportInteractionController(View);
        Background = Brushes.Transparent;
        Children.Add(_scrollViewer);
        _scrollViewer.SizeChanged += OnViewportSizeChanged;
        _scrollViewer.ScrollChanged += OnScrollChanged;
        _scrollViewer.LayoutUpdated += OnLayoutUpdated;
        _scrollViewer.PointerPressed += BeginPan;
        _scrollViewer.PointerMoved += ContinuePan;
        _scrollViewer.PointerReleased += EndPan;
        _scrollViewer.PointerCaptureLost += OnPointerCaptureLost;
        _scrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, ZoomWithWheel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public CetzView View { get; }
    public Vector Offset { get => _scrollViewer.Offset; set => _scrollViewer.Offset = value; }
    public Size Extent => _scrollViewer.Extent;
    public Size Viewport => _scrollViewer.Viewport;

    public event EventHandler? ZoomChanged;
    public event EventHandler? CurrentPageChanged;

    internal Point DocumentOrigin => new(_workspace.Padding.Left, _workspace.Padding.Top);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scrollViewer.SizeChanged -= OnViewportSizeChanged;
        _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer.LayoutUpdated -= OnLayoutUpdated;
        _scrollViewer.PointerPressed -= BeginPan;
        _scrollViewer.PointerMoved -= ContinuePan;
        _scrollViewer.PointerReleased -= EndPan;
        _scrollViewer.PointerCaptureLost -= OnPointerCaptureLost;
        _scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, ZoomWithWheel);
        View.Dispose();
        _scrollViewer.Content = null;
        Children.Clear();
    }

    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        var previousX = _workspaceInsetX;
        var previousY = _workspaceInsetY;
        _workspaceInsetX = Math.Max(0, args.NewSize.Width);
        _workspaceInsetY = Math.Max(0, args.NewSize.Height);
        _workspace.Padding = new Thickness(
            _workspaceInsetX + DocumentMargin,
            _workspaceInsetY + DocumentMargin);
        View.SetViewport(
            Math.Max(0, args.NewSize.Width - DocumentMargin * 2),
            Math.Max(0, args.NewSize.Height - DocumentMargin * 2));
        _pendingWorkspaceShiftX += _workspaceInsetX - previousX;
        _pendingWorkspaceShiftY += _workspaceInsetY - previousY;
    }

    private void BeginPan(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(_scrollViewer).Properties.IsLeftButtonPressed) return;
        var point = args.GetPosition(_scrollViewer);
        _interaction.BeginPan(point.X, point.Y, Offset.X, Offset.Y);
        args.Pointer.Capture(_scrollViewer);
        args.Handled = true;
    }

    private void ContinuePan(object? sender, PointerEventArgs args)
    {
        var point = args.GetPosition(_scrollViewer);
        if (!_interaction.TryPanTo(point.X, point.Y, out var offset)) return;
        Offset = new Vector(offset.X, offset.Y);
        args.Handled = true;
    }

    private void EndPan(object? sender, PointerReleasedEventArgs args)
    {
        _interaction.EndPan();
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args) => _interaction.EndPan();

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args) => UpdateVisibleRegion();

    private void ZoomWithWheel(object? sender, PointerWheelEventArgs args)
    {
        if (!args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        var point = args.GetPosition(_scrollViewer);
        var currentOffset = _pendingZoomOffset ?? new CetzViewportOffset(Offset.X, Offset.Y);
        var anchor = CetzDocumentAnchor.Capture(
            View.Layout, point.X, point.Y, currentOffset.X, currentOffset.Y,
            _workspace.Padding.Left, _workspace.Padding.Top);
        _interaction.ZoomByWheel(args.Delta.Y, point.X, point.Y, currentOffset.X, currentOffset.Y);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        _pendingZoomOffset = anchor.Resolve(
            View.Layout, _workspace.Padding.Left, _workspace.Padding.Top);
        args.Handled = true;
    }

    private void OnLayoutUpdated(object? sender, EventArgs args)
    {
        if (_pendingWorkspaceShiftX != 0 || _pendingWorkspaceShiftY != 0)
        {
            var shiftX = _pendingWorkspaceShiftX;
            var shiftY = _pendingWorkspaceShiftY;
            _pendingWorkspaceShiftX = 0;
            _pendingWorkspaceShiftY = 0;
            Offset = new Vector(
                Math.Max(0, Offset.X + shiftX),
                Math.Max(0, Offset.Y + shiftY));
        }

        if (_pendingZoomOffset is { } offset)
        {
            _pendingZoomOffset = null;
            Offset = new Vector(offset.X, offset.Y);
        }
        UpdateVisibleRegion();
    }

    private void UpdateVisibleRegion()
    {
        var region = new Rect(
            Offset.X - _workspace.Padding.Left,
            Offset.Y - _workspace.Padding.Top,
            Viewport.Width,
            Viewport.Height);
        View.SetVisibleRegion(region);
        var pageIndex = CetzVisiblePageSelector.SelectCurrentPage(
            View.Layout, region.X, region.Y, region.Width, region.Height);
        if (pageIndex is { } selected && View.TrackCurrentPage(selected))
            CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }
}
