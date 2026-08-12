using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Cetz.Renderer.Uno.Sample;

public sealed partial class MainPage : Page
{
    private CetzRenderController? _renderController;
    private readonly CetzViewportInteractionController _viewportInteraction;
    private bool _loaded;
    private bool _disposed;

    public MainPage()
    {
        InitializeComponent();
        _viewportInteraction = new CetzViewportInteractionController(Preview);
        PreviewScroller.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(BeginPan), true);
        PreviewScroller.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(ContinuePan), true);
        PreviewScroller.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndPan), true);
        PreviewScroller.PointerCaptureLost += OnPointerCaptureLost;
        PreviewScroller.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(ZoomWithWheel), true);
        DemoPicker.ItemsSource = CetzDemoCatalog.All;
        DemoPicker.SelectedIndex = 0;
        LoadSelectedDemo();
        DemoPicker.SelectionChanged += OnDemoSelectionChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void DisposeResources()
    {
        if (_disposed)
            return;

        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        DemoPicker.SelectionChanged -= OnDemoSelectionChanged;
        PreviewScroller.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(BeginPan));
        PreviewScroller.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(ContinuePan));
        PreviewScroller.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndPan));
        PreviewScroller.PointerCaptureLost -= OnPointerCaptureLost;
        PreviewScroller.RemoveHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(ZoomWithWheel));
        if (_renderController is not null)
        {
            _renderController.StateChanged -= OnRenderStateChanged;
            _renderController.Dispose();
            _renderController = null;
        }
        ((IDisposable)Preview).Dispose();
        GC.SuppressFinalize(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        Preview.SetViewport(
            Math.Max(0, PreviewScroller.ActualWidth - 56),
            Math.Max(0, PreviewScroller.ActualHeight - 56));
        await RenderAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => DisposeResources();

    private async void OnDemoSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        LoadSelectedDemo();
        if (_loaded)
            await RenderAsync();
    }

    private void LoadSelectedDemo()
    {
        if (DemoPicker.SelectedItem is not CetzDemo demo)
            return;

        SourceEditor.Text = demo.Source;
        DescriptionText.Text = demo.Description;
        StatusText.Text = $"Loaded {demo.DisplayName}; rendering automatically.";
        StatusText.Foreground = new SolidColorBrush(Colors.SlateGray);
    }

    private async void OnRenderClicked(object sender, RoutedEventArgs args) => await RenderAsync();

    private void OnZoomChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (Preview is null || ZoomText is null)
            return;

        Preview.SetZoom(args.NewValue);
        if (ZoomModePicker is not null)
            ZoomModePicker.SelectedIndex = 0;
        UpdateViewStatus();
    }

    private void OnZoomModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (Preview is null || ZoomModePicker is null)
            return;

        var mode = ZoomModePicker.SelectedIndex switch
        {
            1 => CetzZoomMode.FitWidth,
            2 => CetzZoomMode.FitPage,
            _ => CetzZoomMode.Custom
        };
        Preview.SetZoomMode(mode);
        ZoomSlider.IsEnabled = mode == CetzZoomMode.Custom;
        UpdateViewStatus();
    }

    private void OnViewModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (Preview is null || ViewModePicker is null)
            return;

        Preview.SetViewMode(ViewModePicker.SelectedIndex switch
        {
            1 => CetzPageViewMode.ContinuousFacing,
            2 => CetzPageViewMode.SinglePage,
            3 => CetzPageViewMode.FacingPages,
            _ => CetzPageViewMode.ContinuousSingle
        });
        UpdateViewStatus();
    }

    private void OnPreviousClicked(object sender, RoutedEventArgs args)
    {
        Preview.MovePrevious();
        UpdateViewStatus();
    }

    private void OnNextClicked(object sender, RoutedEventArgs args)
    {
        Preview.MoveNext();
        UpdateViewStatus();
    }

    private void OnPreviewViewportChanged(object sender, SizeChangedEventArgs args)
    {
        if (Preview is null)
            return;

        Preview.SetViewport(Math.Max(0, args.NewSize.Width - 56), Math.Max(0, args.NewSize.Height - 56));
        UpdateViewStatus();
    }

    private void BeginPan(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(PreviewScroller);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (!PreviewScroller.CapturePointer(args.Pointer))
            return;

        _viewportInteraction.BeginPan(
            point.Position.X,
            point.Position.Y,
            PreviewScroller.HorizontalOffset,
            PreviewScroller.VerticalOffset);
        args.Handled = true;
    }

    private void ContinuePan(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(PreviewScroller);
        if (!point.Properties.IsLeftButtonPressed ||
            !_viewportInteraction.TryPanTo(point.Position.X, point.Position.Y, out var offset))
            return;

        PreviewScroller.ChangeView(offset.X, offset.Y, null, true);
        args.Handled = true;
    }

    private void EndPan(object sender, PointerRoutedEventArgs args)
    {
        if (!_viewportInteraction.IsPanning)
            return;

        _viewportInteraction.EndPan();
        PreviewScroller.ReleasePointerCapture(args.Pointer);
        args.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) =>
        _viewportInteraction.EndPan();

    private void ZoomWithWheel(object sender, PointerRoutedEventArgs args)
    {
        if ((args.KeyModifiers & VirtualKeyModifiers.Control) == 0)
            return;

        var point = args.GetCurrentPoint(PreviewScroller);
        var offset = _viewportInteraction.ZoomByWheel(
            point.Properties.MouseWheelDelta,
            point.Position.X,
            point.Position.Y,
            PreviewScroller.HorizontalOffset,
            PreviewScroller.VerticalOffset);
        ZoomModePicker.SelectedIndex = 0;
        ZoomSlider.Value = Preview.Zoom;
        PreviewScroller.ChangeView(offset.X, offset.Y, null, true);
        UpdateViewStatus();
        args.Handled = true;
    }

    private void UpdateViewStatus()
    {
        if (Preview is null || ZoomText is null || PageIndicator is null)
            return;

        ZoomText.Text = $"{Preview.Zoom:P0}";
        PageIndicator.Text = Preview.PageCount == 0
            ? "0 / 0"
            : $"{Preview.CurrentPageIndex + 1} / {Preview.PageCount}";
    }

    private async Task RenderAsync()
    {
        if (_disposed || DemoPicker.SelectedItem is not CetzDemo demo)
            return;

        var controller = EnsureRenderController();
        StatusText.Text = "Rendering…";
        StatusText.Foreground = new SolidColorBrush(Colors.SlateGray);

        try
        {
            var document = await controller.RenderProjectAsync(
                demo.CreateProject(SourceEditor.Text ?? string.Empty),
                new CetzDocumentRenderOptions { Ppi = 144 });

            if (_disposed || document is null)
                return;

            UpdateViewStatus();
            var milliseconds = document.Timing.TotalMilliseconds;
            StatusText.Text = $"{document.Pages.Count} page(s) · {milliseconds:F1} ms · {document.Ppi:F0} DPI";
            StatusText.Foreground = new SolidColorBrush(Colors.SeaGreen);
        }
        catch (Exception exception)
        {
            if (_disposed || !ReferenceEquals(controller.LastError, exception))
                return;

            StatusText.Text = exception.Message;
            StatusText.Foreground = new SolidColorBrush(Colors.Crimson);
        }
    }

    private CetzRenderController EnsureRenderController()
    {
        if (_renderController is not null)
            return _renderController;

        _renderController = new CetzRenderController(
            Preview,
            new CetzRendererOptions
            {
                NativeLibraryPath = ResolveNativeLibrary(),
                PackageResolution = CetzPackageResolution.EmbeddedOnly
            },
            SynchronizationContext.Current);
        _renderController.StateChanged += OnRenderStateChanged;
        return _renderController;
    }

    private void OnRenderStateChanged(object? sender, EventArgs args)
    {
        if (!_disposed && _renderController is not null)
            RenderButton.IsEnabled = !_renderController.IsRendering;
    }

    private static string ResolveNativeLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var besideApp = Path.Combine(AppContext.BaseDirectory, "cetz_dotnet_native.dll");
        if (File.Exists(besideApp))
            return besideApp;

        throw new FileNotFoundException(
            "Native runtime was not found. Build the Rust core or set CETZ_NATIVE_LIBRARY.",
            besideApp);
    }
}
