using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Cetz.Renderer.Uno.Sample;

public partial class App : Application
{
    public App() => InitializeComponent();

    public Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window
        {
            Title = "Cetz.Renderer Uno Sample",
            Content = new MainPage()
        };
        MainWindow.Activate();
        MainWindow.AppWindow.Resize(new SizeInt32 { Width = 1280, Height = 820 });
    }
}
