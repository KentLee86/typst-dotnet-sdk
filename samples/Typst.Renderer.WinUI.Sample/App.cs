using Microsoft.UI.Xaml;

namespace Typst.Renderer.WinUI.Sample;

public sealed partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        _window = window;
        window.Activate();
    }
}
