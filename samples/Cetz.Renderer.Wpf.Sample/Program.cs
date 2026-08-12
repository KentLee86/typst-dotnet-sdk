using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Cetz.Renderer.Wpf.Sample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--software-rendering", StringComparer.OrdinalIgnoreCase))
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        application.Run(new MainWindow());
    }
}
