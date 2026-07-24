using Avalonia;

namespace SrvSurvey.Desktop;

internal static class Program
{
    internal static string[] StartupArguments { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        StartupArguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
