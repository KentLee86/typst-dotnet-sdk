using Avalonia;

namespace Typst.Renderer.Avalonia.Sample;

internal static class Program
{
    public static string? InitialDemoId { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        InitialDemoId = ReadOption(args, "--demo");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(name + "=", StringComparison.Ordinal))
                return args[index][(name.Length + 1)..];
            if (args[index].Equals(name, StringComparison.Ordinal) && index + 1 < args.Length)
                return args[index + 1];
        }
        return null;
    }
}
