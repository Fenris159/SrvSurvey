using System.Diagnostics;
using Avalonia;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop;

internal static class Program
{
    internal static string[] StartupArguments { get; private set; } = [];

    internal static ApplicationLogService? ApplicationLog { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartupArguments = args;
        var appDataPaths = AppDataPaths.ResolveCurrent();
        ApplicationLog = new ApplicationLogService(appDataPaths.DataDirectory);
        ApplicationLog.Append(
            $"SrvSurvey {typeof(Program).Assembly.GetName().Version}");
        ApplicationLog.Append($"New log path: {ApplicationLog.CurrentLogPath}");
        ApplicationLog.Append($"Data folder: {appDataPaths.DataDirectory}");
        ApplicationLog.Append(
            $"Platform: {Environment.OSVersion.Platform} ({Environment.OSVersion.VersionString})");
        using var traceListener = new ApplicationLogTraceListener(ApplicationLog);
        Trace.Listeners.Add(traceListener);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            ApplicationLog.Append("Fatal application error: " + exception);
            throw;
        }
        finally
        {
            Trace.Listeners.Remove(traceListener);
            traceListener.Flush();
        }
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
