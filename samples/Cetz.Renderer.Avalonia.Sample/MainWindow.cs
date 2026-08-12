using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;

namespace Cetz.Renderer.Avalonia.Sample;

public sealed class MainWindow : Window
{
    private readonly CetzDocumentRenderer _renderer;
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
    private bool _opened;

    public MainWindow()
    {
        Title = "Cetz.Renderer Avalonia Sample";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        Background = new SolidColorBrush(Color.Parse("#EEF2F7"));

        _renderer = new CetzDocumentRenderer(new CetzRendererOptions
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
        Closed += (_, _) => _renderer.Dispose();
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

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("430,*") };
        split.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#D6DCE7")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = editor
        });
        Grid.SetColumn(preview, 1);
        split.Children.Add(preview);
        return split;
    }

    private async Task RenderAsync()
    {
        if (_demoPicker.SelectedItem is not CetzDemo demo)
            return;

        _renderButton.IsEnabled = false;
        _demoPicker.IsEnabled = false;
        _status.Text = "렌더링 중…";
        _status.Foreground = Brushes.SlateGray;
        try
        {
            var document = await _renderer.RenderProjectAsync(
                demo.CreateProject(_source.Text ?? string.Empty),
                options: new CetzDocumentRenderOptions { Ppi = 144 });
            _view.Document = document;
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
            _renderButton.IsEnabled = true;
            _demoPicker.IsEnabled = true;
        }
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
