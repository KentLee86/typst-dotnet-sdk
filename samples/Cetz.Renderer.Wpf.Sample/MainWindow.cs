using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;

namespace Cetz.Renderer.Wpf.Sample;

public sealed class MainWindow : Window
{
    private static readonly Brush MutedBrush = BrushFrom("#64748B");
    private static readonly Brush SuccessBrush = BrushFrom("#087F5B");
    private readonly CetzDocumentRenderer _renderer;
    private readonly global::Cetz.Renderer.Wpf.CetzView _view = new()
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
        ItemsSource = CetzDemoCatalog.All,
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
    private bool _opened;
    private bool _closing;

    public MainWindow()
    {
        Title = "Cetz.Renderer WPF Sample";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = BrushFrom("#EEF2F7");

        _renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            NativeLibraryPath = ResolveNativeLibrary(),
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });

        SelectDemo();
        _demoPicker.SelectionChanged += DemoSelectionChanged;
        _renderButton.Click += RenderClicked;
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

        var preview = new ScrollViewer
        {
            Content = _view,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Background = BrushFrom("#DDE4EE")
        };

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
        Grid.SetColumn(preview, 1);
        split.Children.Add(preview);
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

    private async Task RenderAsync()
    {
        if (_closing || _demoPicker.SelectedItem is not CetzDemo demo)
            return;

        _renderButton.IsEnabled = false;
        _demoPicker.IsEnabled = false;
        _status.Text = "Rendering...";
        _status.Foreground = MutedBrush;
        try
        {
            var document = await _renderer.RenderProjectAsync(
                demo.CreateProject(_source.Text),
                new CetzDocumentRenderOptions { Ppi = 144 });
            if (_closing)
                return;

            _view.Document = document;
            _status.Text = $"{demo.DisplayName} · {document.Pages.Count} page(s) · {document.Timing.TotalMilliseconds:F0} ms";
            _status.Foreground = SuccessBrush;
            Title = $"Cetz.Renderer WPF Sample — {_status.Text} · Typst {document.TypstVersion}";
        }
        catch (Exception exception)
        {
            if (!_closing)
            {
                _status.Text = exception.Message;
                _status.Foreground = Brushes.Crimson;
                Title = "Cetz.Renderer WPF Sample — Error";
            }
        }
        finally
        {
            if (!_closing)
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

    private void WindowClosing(object? sender, CancelEventArgs args) => _closing = true;

    private void WindowClosed(object? sender, EventArgs args)
    {
        _view.Dispose();
        _renderer.Dispose();
    }

    private static string ResolveNativeLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var besideApp = Path.Combine(AppContext.BaseDirectory, "cetz_dotnet_native.dll");
        if (File.Exists(besideApp))
            return besideApp;
        throw new FileNotFoundException(
            "Native runtime was not found. Build the Rust core or set CETZ_NATIVE_LIBRARY.",
            besideApp);
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
