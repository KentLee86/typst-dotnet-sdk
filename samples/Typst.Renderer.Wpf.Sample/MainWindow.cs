using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Typst.Renderer.Core;
using Typst.Renderer.Demo.Shared;

namespace Typst.Renderer.Wpf.Sample;

public sealed class MainWindow : Window
{
    private static readonly Brush MutedBrush = BrushFrom("#64748B");
    private static readonly Brush SuccessBrush = BrushFrom("#087F5B");
    private readonly TypstRenderController _renderController;
    private readonly TypstViewportInteractionController _viewportInteraction;
    private readonly global::Typst.Renderer.Wpf.TypstView _view = new()
    {
        Margin = new Thickness(28),
        Zoom = 0.9
    };
    private readonly TextBox _source = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 13,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        TextWrapping = TextWrapping.NoWrap
    };
    private readonly ComboBox _demoPicker = new()
    {
        ItemsSource = TypstDemoCatalog.All,
        SelectedIndex = 6,
        HorizontalContentAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _description = new()
    {
        Foreground = MutedBrush,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _status = new()
    {
        Text = "Ready",
        Foreground = MutedBrush,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Button _renderButton = new()
    {
        Content = "Render",
        MinWidth = 92,
        Padding = new Thickness(16, 7, 16, 7)
    };
    private readonly ComboBox _zoomModePicker = new()
    {
        ItemsSource = Enum.GetValues<TypstZoomMode>(),
        SelectedItem = TypstZoomMode.Custom,
        MinWidth = 105
    };
    private readonly ComboBox _viewModePicker = new()
    {
        ItemsSource = Enum.GetValues<TypstPageViewMode>(),
        SelectedItem = TypstPageViewMode.ContinuousSingle,
        MinWidth = 150
    };
    private readonly ComboBox _qualityPicker = new()
    {
        ItemsSource = RasterQualityChoices,
        SelectedIndex = 2,
        MinWidth = 150
    };
    private readonly Button _previousButton = new() { Content = "Previous", Padding = new Thickness(12, 5, 12, 5) };
    private readonly Button _nextButton = new() { Content = "Next", Padding = new Thickness(12, 5, 12, 5) };
    private readonly TextBox _pageInput = new() { Text = "1", Width = 52, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _pageIndicator = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Center };
    private readonly ScrollViewer _preview = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        CanContentScroll = false,
        Background = BrushFrom("#DDE4EE")
    };
    private bool _opened;
    private bool _closing;
    private bool _syncingPageInput;
    private readonly DispatcherTimer _qualityTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public MainWindow()
    {
        Title = "Typst.Renderer WPF Sample";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = BrushFrom("#EEF2F7");

        _renderController = new TypstRenderController(
            _view,
            new TypstRendererOptions
            {
                NativeLibraryPath = ResolveNativeLibrary(),
                PackageResolution = TypstPackageResolution.EmbeddedOnly
            },
            new DispatcherSynchronizationContext(Dispatcher));
        _viewportInteraction = new TypstViewportInteractionController(_view);

        SelectDemo();
        _demoPicker.SelectionChanged += DemoSelectionChanged;
        _renderButton.Click += RenderClicked;
        _zoomModePicker.SelectionChanged += ZoomModeSelectionChanged;
        _viewModePicker.SelectionChanged += ViewModeSelectionChanged;
        _qualityPicker.SelectionChanged += QualitySelectionChanged;
        _qualityTimer.Tick += QualityTimerTick;
        _pageInput.KeyDown += PageInputKeyDown;
        _pageInput.LostFocus += (_, _) => NavigateFromPageInput();
        _previousButton.Click += PreviousClicked;
        _nextButton.Click += NextClicked;
        _preview.Content = _view;
        _preview.Loaded += PreviewViewportChanged;
        _preview.SizeChanged += PreviewViewportChanged;
        _preview.ScrollChanged += PreviewScrollChanged;
        _preview.PreviewMouseLeftButtonDown += BeginPan;
        _preview.PreviewMouseMove += ContinuePan;
        _preview.PreviewMouseLeftButtonUp += EndPan;
        _preview.LostMouseCapture += (_, _) => _viewportInteraction.EndPan();
        _preview.PreviewMouseWheel += ZoomWithWheel;
        Content = BuildLayout();
        ContentRendered += WindowContentRendered;
        Closing += WindowClosing;
        Closed += WindowClosed;
    }

    private UIElement BuildLayout()
    {
        var selector = new StackPanel();
        selector.Children.Add(new TextBlock
        {
            Text = "Demo resource",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        selector.Children.Add(_demoPicker);
        _description.Margin = new Thickness(0, 6, 0, 0);
        selector.Children.Add(_description);

        var editorHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        editorHeader.ColumnDefinitions.Add(new ColumnDefinition());
        editorHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editorHeader.Children.Add(new TextBlock
        {
            Text = "Typst / CeTZ source",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_renderButton, 1);
        editorHeader.Children.Add(_renderButton);

        var editor = new Grid { Margin = new Thickness(22) };
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.Children.Add(selector);
        Grid.SetRow(editorHeader, 2);
        editor.Children.Add(editorHeader);
        Grid.SetRow(_source, 3);
        editor.Children.Add(_source);
        _status.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(_status, 4);
        editor.Children.Add(_status);

        var previewToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 8, 12, 8)
        };
        previewToolbar.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        previewToolbar.Children.Add(_zoomModePicker);
        previewToolbar.Children.Add(new TextBlock { Text = "View", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        previewToolbar.Children.Add(_viewModePicker);
        previewToolbar.Children.Add(new TextBlock { Text = "Quality", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        previewToolbar.Children.Add(_qualityPicker);
        _previousButton.Margin = new Thickness(14, 0, 4, 0);
        previewToolbar.Children.Add(_previousButton);
        previewToolbar.Children.Add(_pageInput);
        previewToolbar.Children.Add(_pageIndicator);
        previewToolbar.Children.Add(_nextButton);

        var previewPanel = new Grid();
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        previewPanel.Children.Add(previewToolbar);
        Grid.SetRow(_preview, 1);
        previewPanel.Children.Add(_preview);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = BrushFrom("#D6DCE7"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = editor
        });
        Grid.SetColumn(previewPanel, 1);
        split.Children.Add(previewPanel);
        UpdatePageIndicator();
        return split;
    }

    private async void WindowContentRendered(object? sender, EventArgs args)
    {
        ContentRendered -= WindowContentRendered;
        _opened = true;
        await RenderAsync();
    }

    private async void DemoSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        SelectDemo();
        if (_opened)
            await RenderAsync();
    }

    private async void RenderClicked(object sender, RoutedEventArgs args) => await RenderAsync();

    private void ZoomModeSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_zoomModePicker.SelectedItem is TypstZoomMode mode)
            _view.SetZoomMode(mode);
        UpdatePageIndicator();
    }

    private void ViewModeSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_viewModePicker.SelectedItem is TypstPageViewMode mode)
            _view.SetViewMode(mode);
        UpdatePageIndicator();
    }

    private void PreviousClicked(object sender, RoutedEventArgs args)
    {
        _view.MovePrevious();
        UpdatePageIndicator();
    }

    private void NextClicked(object sender, RoutedEventArgs args)
    {
        _view.MoveNext();
        UpdatePageIndicator();
    }

    private void PreviewViewportChanged(object sender, RoutedEventArgs args) => UpdateViewport();

    private void PreviewScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (args.ViewportWidthChange != 0 || args.ViewportHeightChange != 0)
            UpdateViewport();
        UpdateVisibleRegion();
    }

    private void UpdateViewport()
    {
        var width = _preview.ViewportWidth > 0 ? _preview.ViewportWidth : _preview.ActualWidth;
        var height = _preview.ViewportHeight > 0 ? _preview.ViewportHeight : _preview.ActualHeight;
        _view.SetViewport(width, height);
        UpdateVisibleRegion();
    }

    private void BeginPan(object sender, MouseButtonEventArgs args)
    {
        var point = args.GetPosition(_preview);
        _viewportInteraction.BeginPan(point.X, point.Y, _preview.HorizontalOffset, _preview.VerticalOffset);
        _preview.CaptureMouse();
        _preview.Cursor = Cursors.Hand;
        args.Handled = true;
    }

    private void ContinuePan(object sender, MouseEventArgs args)
    {
        var point = args.GetPosition(_preview);
        if (!_viewportInteraction.TryPanTo(point.X, point.Y, out var offset)) return;
        _preview.ScrollToHorizontalOffset(offset.X);
        _preview.ScrollToVerticalOffset(offset.Y);
        args.Handled = true;
    }

    private void EndPan(object sender, MouseButtonEventArgs args)
    {
        _viewportInteraction.EndPan();
        _preview.ReleaseMouseCapture();
        _preview.Cursor = null;
        args.Handled = true;
    }

    private void ZoomWithWheel(object sender, MouseWheelEventArgs args)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var point = args.GetPosition(_preview);
        var offset = _viewportInteraction.ZoomByWheel(
            args.Delta, point.X, point.Y, _preview.HorizontalOffset, _preview.VerticalOffset);
        _zoomModePicker.SelectedItem = TypstZoomMode.Custom;
        Dispatcher.BeginInvoke(() =>
        {
            _preview.ScrollToHorizontalOffset(offset.X);
            _preview.ScrollToVerticalOffset(offset.Y);
        }, DispatcherPriority.Loaded);
        UpdatePageIndicator();
        ScheduleAutomaticQualityRefresh();
        args.Handled = true;
    }

    private async Task RenderAsync()
    {
        if (_closing || _demoPicker.SelectedItem is not TypstDemo demo)
            return;

        _renderButton.IsEnabled = false;
        _status.Text = "Rendering...";
        _status.Foreground = MutedBrush;
        try
        {
            var document = await _renderController.RenderProjectAsync(
                demo.CreateProject(_source.Text),
                new TypstDocumentRenderOptions
                {
                    Ppi = TypstRasterQualityPolicy.ResolvePpi(SelectedQualityMode, _view.Zoom)
                });
            if (_closing || document is null)
                return;

            UpdatePageIndicator();
            _status.Text = $"{demo.DisplayName} · {document.Pages.Count} page(s) · {document.Ppi:F0} PPI · {document.Timing.TotalMilliseconds:F0} ms";
            _status.Foreground = SuccessBrush;
            Title = $"Typst.Renderer WPF Sample — {_status.Text} · Typst {document.TypstVersion}";
            ScheduleAutomaticQualityRefresh();
        }
        catch (Exception exception)
        {
            if (!_closing)
            {
                _status.Text = exception.Message;
                _status.Foreground = Brushes.Crimson;
                Title = "Typst.Renderer WPF Sample — Error";
            }
        }
        finally
        {
            if (!_closing && !_renderController.IsRendering)
                _renderButton.IsEnabled = true;
        }
    }

    private void SelectDemo()
    {
        if (_demoPicker.SelectedItem is not TypstDemo demo)
            return;

        _source.Text = demo.Source;
        _description.Text = demo.Description;
    }

    private void WindowClosing(object? sender, CancelEventArgs args)
    {
        _closing = true;
        _qualityTimer.Stop();
        _renderController.Cancel();
    }

    private void WindowClosed(object? sender, EventArgs args)
    {
        _renderController.Dispose();
        _view.Dispose();
    }

    private void UpdatePageIndicator()
    {
        var count = _view.PageCount;
        _syncingPageInput = true;
        try
        {
            _pageInput.Text = Math.Max(1, _view.CurrentPageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _pageInput.IsEnabled = count > 0;
        }
        finally { _syncingPageInput = false; }
        _pageIndicator.Text = $"/ {count}";
        _previousButton.IsEnabled = count > 0 && _view.CurrentPageIndex > 0;
        _nextButton.IsEnabled = count > 0 && _view.CurrentPageIndex < count - 1;
    }

    private void PageInputKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        NavigateFromPageInput();
        args.Handled = true;
    }

    private void NavigateFromPageInput()
    {
        if (_syncingPageInput || _view.PageCount == 0) return;
        if (int.TryParse(_pageInput.Text, out var pageNumber))
            _view.GoToPage(Math.Clamp(pageNumber, 1, _view.PageCount) - 1);
        UpdatePageIndicator();
    }

    private void UpdateVisibleRegion()
    {
        var region = new Rect(
            _preview.HorizontalOffset - _view.Margin.Left,
            _preview.VerticalOffset - _view.Margin.Top,
            _preview.ViewportWidth,
            _preview.ViewportHeight);
        _view.SetVisibleRegion(region);
        var current = TypstVisiblePageSelector.SelectCurrentPage(
            _view.Layout, region.X, region.Y, region.Width, region.Height);
        if (current is { } selected && _view.TrackCurrentPage(selected))
            UpdatePageIndicator();
    }

    private TypstRasterQualityMode SelectedQualityMode =>
        (_qualityPicker.SelectedItem as RasterQualityChoice)?.Mode ?? TypstRasterQualityMode.Automatic;

    private async void QualitySelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        _qualityTimer.Stop();
        if (_opened) await RenderAsync();
    }

    private async void QualityTimerTick(object? sender, EventArgs args)
    {
        _qualityTimer.Stop();
        if (_opened) await RenderAsync();
    }

    private void ScheduleAutomaticQualityRefresh()
    {
        if (!_opened || SelectedQualityMode != TypstRasterQualityMode.Automatic) return;
        var ppi = TypstRasterQualityPolicy.ResolvePpi(SelectedQualityMode, _view.Zoom);
        if (_view.Document is { } document && Math.Abs(document.Ppi - ppi) < 0.5f) return;
        _qualityTimer.Stop();
        _qualityTimer.Start();
    }

    private static readonly RasterQualityChoice[] RasterQualityChoices =
    [
        new(TypstRasterQualityMode.Fixed, "Fixed · 144 PPI"),
        new(TypstRasterQualityMode.HighResolution, "High · 288 PPI"),
        new(TypstRasterQualityMode.Automatic, "Automatic")
    ];

    private sealed record RasterQualityChoice(TypstRasterQualityMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private static string ResolveNativeLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("TYPST_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var besideApp = Path.Combine(AppContext.BaseDirectory, "typst_dotnet_native.dll");
        if (File.Exists(besideApp))
            return besideApp;
        throw new FileNotFoundException(
            "Native runtime was not found. Build the Rust core or set TYPST_NATIVE_LIBRARY.",
            besideApp);
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
