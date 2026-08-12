using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;

namespace Cetz.Renderer.Avalonia.Sample;

public sealed class MainWindow : Window
{
    private readonly CetzRenderController _renderController;
    private readonly global::Cetz.Renderer.Avalonia.CetzView _view = new()
    {
        Margin = new Thickness(28),
        Zoom = 0.9
    };
    private readonly TextBox _source = new()
    {
        AcceptsReturn = true,
        FontFamily = FontFamily.Default,
        FontSize = 13
    };
    private readonly ComboBox _demoPicker = new()
    {
        ItemsSource = CetzDemoCatalog.All,
        SelectedIndex = 1,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _description = new()
    {
        Foreground = Brushes.SlateGray,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _status = new() { Text = "준비", Foreground = Brushes.SlateGray };
    private readonly Button _renderButton = new() { Content = "렌더링", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ComboBox _zoomModePicker = new()
    {
        ItemsSource = Enum.GetValues<CetzZoomMode>(),
        SelectedItem = CetzZoomMode.FitWidth,
        Width = 130
    };
    private readonly ComboBox _viewModePicker = new()
    {
        ItemsSource = Enum.GetValues<CetzPageViewMode>(),
        SelectedItem = CetzPageViewMode.ContinuousSingle,
        Width = 180
    };
    private readonly Button _previousPage = new() { Content = "◀ 이전" };
    private readonly Button _nextPage = new() { Content = "다음 ▶" };
    private readonly TextBlock _pageStatus = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _opened;

    public MainWindow()
    {
        Title = "Cetz.Renderer Avalonia Sample";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        Background = new SolidColorBrush(Color.Parse("#EEF2F7"));

        _renderController = new CetzRenderController(_view, new CetzRendererOptions
        {
            NativeLibraryPath = ResolveNativeLibrary(),
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });

        SelectDemo();
        _demoPicker.SelectionChanged += async (_, _) =>
        {
            SelectDemo();
            if (_opened)
                await RenderAsync();
        };
        _renderButton.Click += async (_, _) => await RenderAsync();
        _zoomModePicker.SelectionChanged += (_, _) =>
        {
            if (_zoomModePicker.SelectedItem is CetzZoomMode mode) _view.SetZoomMode(mode);
        };
        _viewModePicker.SelectionChanged += (_, _) =>
        {
            if (_viewModePicker.SelectedItem is CetzPageViewMode mode) _view.SetViewMode(mode);
            UpdatePageStatus();
        };
        _previousPage.Click += (_, _) => { _view.MovePrevious(); UpdatePageStatus(); };
        _nextPage.Click += (_, _) => { _view.MoveNext(); UpdatePageStatus(); };
        Closed += (_, _) =>
        {
            _renderController.Dispose();
            _view.Dispose();
        };
        Content = BuildLayout();
        Opened += async (_, _) =>
        {
            _opened = true;
            await RenderAsync();
        };
    }

    private Control BuildLayout()
    {
        var editorHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        editorHeader.Children.Add(new TextBlock
        {
            Text = "Typst / CeTZ source",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_renderButton, 1);
        editorHeader.Children.Add(_renderButton);

        var demoSelector = new StackPanel { Spacing = 6 };
        demoSelector.Children.Add(new TextBlock
        {
            Text = "Demo resource",
            FontWeight = FontWeight.SemiBold
        });
        demoSelector.Children.Add(_demoPicker);
        demoSelector.Children.Add(_description);

        var editor = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(22),
            RowSpacing = 12
        };
        editor.Children.Add(demoSelector);
        Grid.SetRow(editorHeader, 1);
        editor.Children.Add(editorHeader);
        Grid.SetRow(_source, 2);
        editor.Children.Add(_source);
        Grid.SetRow(_status, 3);
        editor.Children.Add(_status);

        var preview = new ScrollViewer
        {
            Content = _view,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.Parse("#DDE4EE"))
        };
        preview.SizeChanged += (_, args) => _view.SetViewport(
            Math.Max(0, args.NewSize.Width - _view.Margin.Left - _view.Margin.Right),
            Math.Max(0, args.NewSize.Height - _view.Margin.Top - _view.Margin.Bottom));

        var previewToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8)
        };
        previewToolbar.Children.Add(new TextBlock { Text = "맞춤", VerticalAlignment = VerticalAlignment.Center });
        previewToolbar.Children.Add(_zoomModePicker);
        previewToolbar.Children.Add(new TextBlock { Text = "보기", VerticalAlignment = VerticalAlignment.Center });
        previewToolbar.Children.Add(_viewModePicker);
        previewToolbar.Children.Add(_previousPage);
        previewToolbar.Children.Add(_pageStatus);
        previewToolbar.Children.Add(_nextPage);

        var previewPane = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        previewPane.Children.Add(previewToolbar);
        Grid.SetRow(preview, 1);
        previewPane.Children.Add(preview);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("430,*") };
        split.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#D6DCE7")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = editor
        });
        Grid.SetColumn(previewPane, 1);
        split.Children.Add(previewPane);
        return split;
    }

    private async Task RenderAsync()
    {
        if (_demoPicker.SelectedItem is not CetzDemo demo)
            return;

        _renderButton.IsEnabled = false;
        _status.Text = "렌더링 중…";
        _status.Foreground = Brushes.SlateGray;
        try
        {
            var document = await _renderController.RenderProjectAsync(
                demo.CreateProject(_source.Text ?? string.Empty),
                options: new CetzDocumentRenderOptions { Ppi = 144 });
            if (document is null) return;
            UpdatePageStatus();
            _status.Text = $"{demo.DisplayName} · {document.Pages.Count} page · {document.Timing.TotalMilliseconds:F0} ms";
            _status.Foreground = new SolidColorBrush(Color.Parse("#087F5B"));
            Title = $"Cetz.Renderer Avalonia Sample — {_status.Text} · Typst {document.TypstVersion}";
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.Foreground = Brushes.Crimson;
            Title = $"Cetz.Renderer Avalonia Sample — Error: {exception.Message}";
        }
        finally
        {
            _renderButton.IsEnabled = !_renderController.IsRendering;
        }
    }

    private void UpdatePageStatus()
    {
        var first = _view.PageCount == 0 ? 0 : _view.CurrentPageIndex + 1;
        var last = _view.ViewMode is CetzPageViewMode.FacingPages or CetzPageViewMode.ContinuousFacing
            ? Math.Min(_view.PageCount, first + 1)
            : first;
        _pageStatus.Text = first == last ? $"{first} / {_view.PageCount}" : $"{first}-{last} / {_view.PageCount}";
        _previousPage.IsEnabled = _view.CurrentPageIndex > 0;
        _nextPage.IsEnabled = _view.CurrentPageIndex +
            (_view.ViewMode is CetzPageViewMode.FacingPages or CetzPageViewMode.ContinuousFacing ? 2 : 1) < _view.PageCount;
    }

    private void SelectDemo()
    {
        if (_demoPicker.SelectedItem is not CetzDemo demo)
            return;
        _source.Text = demo.Source;
        _description.Text = demo.Description;
    }

    private static string ResolveNativeLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var fileName = OperatingSystem.IsWindows()
            ? "cetz_dotnet_native.dll"
            : "libcetz_dotnet_native.so";
        var besideApp = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(besideApp))
            return besideApp;
        throw new FileNotFoundException(
            "Native runtime was not found. Build the Rust core or set CETZ_NATIVE_LIBRARY.",
            besideApp);
    }
}
