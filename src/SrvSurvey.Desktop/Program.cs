using System.Diagnostics;
using Avalonia;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop;

internal static class Program
{
    internal static string[] StartupArguments { get; private set; } = [];

    internal static ApplicationLogService? ApplicationLog { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var updateStartup = ApplicationUpdateBootstrap.ParseStartupArguments(args);
        if (updateStartup.Mode == ApplicationUpdateStartupMode.Apply)
        {
            Environment.ExitCode = ApplicationUpdateBootstrap.RunHelperAsync(
                    updateStartup.PlanPath!)
                .GetAwaiter()
                .GetResult();
            return;
        }

        StartupArguments = updateStartup.ApplicationArguments.ToArray();
        ApplicationUpdateBootstrap.SetPendingConfirmation(
            updateStartup.Mode == ApplicationUpdateStartupMode.Confirm
                ? updateStartup.PlanPath
                : null);
        var appDataPaths = AppDataPaths.ResolveCurrent();
        var language = LocalizationSettingsStore.ResolveCurrent(appDataPaths);
        LocalizationCatalog.Initialize(language);
        LocalizationCatalog.ApplyCulture(language);
        var applicationLog = new ApplicationLogService(appDataPaths.DataDirectory);
        ApplicationLog = applicationLog;
        applicationLog.Append(
            $"SrvSurvey {typeof(Program).Assembly.GetName().Version}");
        applicationLog.Append($"New log path: {applicationLog.CurrentLogPath}");
        applicationLog.Append($"Data folder: {appDataPaths.DataDirectory}");
        applicationLog.Append(
            $"Platform: {Environment.OSVersion.Platform} ({Environment.OSVersion.VersionString})");
        using var traceListener = new ApplicationLogTraceListener(applicationLog);
        void HandleUnhandledException(
            object sender,
            UnhandledExceptionEventArgs eventArgs)
        {
            applicationLog.Append(
                "Unhandled process exception: " + eventArgs.ExceptionObject);
        }

        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        Trace.Listeners.Add(traceListener);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            applicationLog.Append("Fatal application error: " + exception);
            throw;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
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
