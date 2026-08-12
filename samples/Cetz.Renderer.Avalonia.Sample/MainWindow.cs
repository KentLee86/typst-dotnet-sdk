using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.Avalonia.Sample;

public sealed class MainWindow : Window
{
    private const string ExampleSource = """
        #import "@preview/cetz:0.5.2": canvas, draw
        #set page(width: 520pt, height: 330pt, margin: 26pt, fill: rgb("f8fafc"))
        #set text(font: "Noto Sans KR", fill: rgb("172033"))
        #align(center)[
          #text(size: 20pt, weight: "bold")[CeTZ · Avalonia]
          #v(5pt)
          #text(size: 10pt, fill: rgb("667085"))[네이티브 core에서 렌더링한 실제 화면]
        ]
        #v(16pt)
        #canvas(length: 1cm, {
          draw.rect((0, 0), (11, 4.4), radius: .25, fill: rgb("ffffff"), stroke: rgb("d8e0ef"))
          draw.circle((2.0, 2.2), radius: 1.1, fill: rgb("4f7cff"))
          draw.circle((5.5, 2.2), radius: 1.1, fill: rgb("8b5cf6"))
          draw.circle((9.0, 2.2), radius: 1.1, fill: rgb("10b981"))
          draw.line((3.15, 2.2), (4.35, 2.2), stroke: rgb("94a3b8"))
          draw.line((6.65, 2.2), (7.85, 2.2), stroke: rgb("94a3b8"))
        })
        #v(8pt)
        #grid(columns: (1fr, 1fr, 1fr), align: center,
          text(weight: "bold")[Core],
          text(weight: "bold")[RGBA],
          text(weight: "bold")[Avalonia],
        )
        """;

    private readonly CetzDocumentRenderer _renderer;
    private readonly global::Cetz.Renderer.Avalonia.CetzView _view = new()
    {
        Margin = new Thickness(28),
        Zoom = 0.9
    };
    private readonly TextBox _source = new()
    {
        Text = ExampleSource,
        AcceptsReturn = true,
        FontFamily = FontFamily.Default,
        FontSize = 13
    };
    private readonly TextBlock _status = new() { Text = "준비", Foreground = Brushes.SlateGray };
    private readonly Button _renderButton = new() { Content = "렌더링", HorizontalAlignment = HorizontalAlignment.Right };

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

        _renderButton.Click += async (_, _) => await RenderAsync();
        Closed += (_, _) => _renderer.Dispose();
        Content = BuildLayout();
        Opened += async (_, _) => await RenderAsync();
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

        var editor = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(22),
            RowSpacing = 12
        };
        editor.Children.Add(editorHeader);
        Grid.SetRow(_source, 1);
        editor.Children.Add(_source);
        Grid.SetRow(_status, 2);
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
        _renderButton.IsEnabled = false;
        _status.Text = "렌더링 중…";
        _status.Foreground = Brushes.SlateGray;
        try
        {
            var document = await _renderer.RenderSourceAsync(
                _source.Text ?? string.Empty,
                options: new CetzDocumentRenderOptions { Ppi = 144 });
            _view.Document = document;
            _status.Text = $"{document.Pages.Count} page · {document.Timing.TotalMilliseconds:F0} ms · Typst {document.TypstVersion}";
            _status.Foreground = new SolidColorBrush(Color.Parse("#087F5B"));
            Title = $"Cetz.Renderer Avalonia Sample — {_status.Text}";
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
        }
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
