using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;

namespace Typst.Renderer.Avalonia.Sample;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new StyleInclude(new Uri("avares://Typst.Renderer.Avalonia.Sample/"))
        {
            Source = new Uri("avares://Avalonia.Themes.Fluent/FluentTheme.xaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(Program.InitialDemoId);
        base.OnFrameworkInitializationCompleted();
    }
}
