using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cetz.Renderer.WinUI.Sample;

public sealed class MainWindow : Window
{
    private readonly CetzDocumentRenderer _renderer;
    private readonly global::Cetz.Renderer.WinUI.CetzView _view = new()
    {
        Zoom = 0.9,
        PageSpacing = 24
    };
    private readonly TextBox _source = new()
    {
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 13,
        TextWrapping = TextWrapping.NoWrap
    };
    private readonly ComboBox _demoPicker = new()
    {
        ItemsSource = CetzDemoCatalog.All,
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _description = new()
    {
        Foreground = Brush(88, 99, 115),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _status = new()
    {
        Text = "Ready",
        Foreground = Brush(88, 99, 115),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly Button _renderButton = new()
    {
        Content = "Render",
        HorizontalAlignment = HorizontalAlignment.Right,
        Padding = new Thickness(18, 8, 18, 8)
    };
    private CancellationTokenSource? _renderCancellation;
    private bool _opened;
    private bool _closed;

    public MainWindow()
    {
        Title = "Cetz.Renderer WinUI 3 Sample";
        var root = BuildLayout();
        Content = root;

        _renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            NativeLibraryPath = ResolveNativeLibrary(),
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });

        ScrollViewer.SetHorizontalScrollBarVisibility(_source, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_source, ScrollBarVisibility.Auto);

        SelectDemo();
        _demoPicker.SelectionChanged += DemoPickerOnSelectionChanged;
        _renderButton.Click += RenderButtonOnClick;
        root.Loaded += RootOnLoaded;
        Closed += WindowOnClosed;
    }

    private Grid BuildLayout()
    {
        var selector = new StackPanel { Spacing = 6 };
        selector.Children.Add(new TextBlock { Text = "Demo", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        selector.Children.Add(_demoPicker);
        selector.Children.Add(_description);

        var editorHeader = new Grid { ColumnSpacing = 12 };
        editorHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editorHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editorHeader.Children.Add(new TextBlock
        {
            Text = "Typst / CeTZ source",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_renderButton, 1);
        editorHeader.Children.Add(_renderButton);

        var editor = new Grid
        {
            Padding = new Thickness(22),
            RowSpacing = 12,
            Background = Brush(255, 255, 255)
        };
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.Children.Add(selector);
        Grid.SetRow(editorHeader, 1);
        editor.Children.Add(editorHeader);
        Grid.SetRow(_source, 2);
        editor.Children.Add(_source);
        Grid.SetRow(_status, 3);
        editor.Children.Add(_status);

        var previewBorder = new Border
        {
            Background = Brush(221, 228, 238),
            Padding = new Thickness(28),
            Child = _view
        };

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.Children.Add(editor);
        Grid.SetColumn(previewBorder, 1);
        split.Children.Add(previewBorder);
        return split;
    }

    private void DemoPickerOnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        SelectDemo();
        if (_opened)
            _ = RenderAsync();
    }

    private void RenderButtonOnClick(object sender, RoutedEventArgs args)
        => _ = RenderAsync();

    private void RootOnLoaded(object sender, RoutedEventArgs args)
    {
        if (_opened)
            return;
        _opened = true;
        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        if (_closed || _demoPicker.SelectedItem is not CetzDemo demo)
            return;

        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        var cancellation = _renderCancellation = new CancellationTokenSource();
        _renderButton.IsEnabled = false;
        _demoPicker.IsEnabled = false;
        SetStatus("Rendering…", Brush(88, 99, 115));

        try
        {
            var document = await _renderer.RenderProjectAsync(
                demo.CreateProject(_source.Text ?? string.Empty),
                new CetzDocumentRenderOptions { Ppi = 144 },
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            await _view.SetDocumentAsync(document);
            var pageLabel = document.Pages.Count == 1 ? "page" : "pages";
            var status = $"{demo.DisplayName} · {document.Pages.Count} {pageLabel} · {document.Timing.TotalMilliseconds:F0} ms";
            SetStatus(status, Brush(8, 127, 91));
            Title = $"Cetz.Renderer WinUI 3 Sample — {status} · Typst {document.TypstVersion}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("Render canceled", Brush(88, 99, 115));
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, Brush(190, 35, 58));
            Title = $"Cetz.Renderer WinUI 3 Sample — Error: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_renderCancellation, cancellation) && !_closed)
            {
                _renderButton.IsEnabled = true;
                _demoPicker.IsEnabled = true;
            }
        }
    }

    private void SelectDemo()
    {
        if (_demoPicker.SelectedItem is not CetzDemo demo)
            return;
        _source.Text = demo.Source;
        _description.Text = demo.Description;
    }

    private void SetStatus(string text, Brush foreground)
    {
        _status.Text = text;
        _status.Foreground = foreground;
    }

    private void WindowOnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = null;
        _renderer.Dispose();
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
        => new(Color.FromArgb(255, red, green, blue));

    private static string ResolveNativeLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var besideApp = Path.Combine(AppContext.BaseDirectory, "cetz_dotnet_native.dll");
        if (File.Exists(besideApp))
            return besideApp;
        throw new FileNotFoundException(
            "Native runtime was not found. Build it, copy it beside the app, or set CETZ_NATIVE_LIBRARY.",
            besideApp);
    }
}
