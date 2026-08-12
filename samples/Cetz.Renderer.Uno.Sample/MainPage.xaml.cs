using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Cetz.Renderer.Uno.Sample;

public sealed partial class MainPage : Page
{
    private CetzDocumentRenderer? _renderer;
    private bool _disposed;

    public MainPage()
    {
        InitializeComponent();
        DemoPicker.ItemsSource = CetzDemoCatalog.All;
        DemoPicker.SelectionChanged += OnDemoSelectionChanged;
        DemoPicker.SelectedIndex = 0;
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
        ((IDisposable)Preview).Dispose();
        _renderer?.Dispose();
        _renderer = null;
        GC.SuppressFinalize(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) => await RenderAsync();

    private void OnUnloaded(object sender, RoutedEventArgs args) => DisposeResources();

    private void OnDemoSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (DemoPicker.SelectedItem is not CetzDemo demo)
            return;

        SourceEditor.Text = demo.Source;
        DescriptionText.Text = demo.Description;
        StatusText.Text = $"Loaded {demo.DisplayName}. Select Render to refresh the preview.";
        StatusText.Foreground = new SolidColorBrush(Colors.SlateGray);
    }

    private async void OnRenderClicked(object sender, RoutedEventArgs args) => await RenderAsync();

    private void OnZoomChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (Preview is null || ZoomText is null)
            return;

        Preview.Zoom = args.NewValue;
        ZoomText.Text = $"{args.NewValue:P0}";
    }

    private async Task RenderAsync()
    {
        if (_disposed || DemoPicker.SelectedItem is not CetzDemo demo)
            return;

        RenderButton.IsEnabled = false;
        DemoPicker.IsEnabled = false;
        StatusText.Text = "Rendering…";
        StatusText.Foreground = new SolidColorBrush(Colors.SlateGray);

        try
        {
            _renderer ??= CreateRenderer();
            var document = await _renderer.RenderProjectAsync(
                demo.CreateProject(SourceEditor.Text ?? string.Empty),
                new CetzDocumentRenderOptions { Ppi = 144 });

            if (_disposed)
                return;

            Preview.Document = document;
            var milliseconds = document.Timing.TotalMilliseconds;
            StatusText.Text = $"{document.Pages.Count} page(s) · {milliseconds:F1} ms · {document.Ppi:F0} DPI";
            StatusText.Foreground = new SolidColorBrush(Colors.SeaGreen);
        }
        catch (Exception exception)
        {
            if (_disposed)
                return;

            Preview.Document = null;
            StatusText.Text = exception.Message;
            StatusText.Foreground = new SolidColorBrush(Colors.Crimson);
        }
        finally
        {
            if (!_disposed)
            {
                RenderButton.IsEnabled = true;
                DemoPicker.IsEnabled = true;
            }
        }
    }

    private static CetzDocumentRenderer CreateRenderer() => new(new CetzRendererOptions
    {
        NativeLibraryPath = ResolveNativeLibrary(),
        PackageResolution = CetzPackageResolution.EmbeddedOnly
    });

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
