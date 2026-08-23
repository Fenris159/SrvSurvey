using System.Diagnostics;
using Avalonia;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

internal static class Program
{
    internal const string SoftwareRenderingEnvironmentVariable =
        "SRVSURVEY_SOFTWARE_RENDERING";

    internal static string[] StartupArguments { get; private set; } = [];

    internal static ApplicationLogService? ApplicationLog { get; private set; }

    internal static DesktopStartupContext? StartupContext { get; private set; }

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
        ApplicationUpdateBootstrap.SetPendingOutcome(
            updateStartup.Mode == ApplicationUpdateStartupMode.Result
                ? updateStartup.PlanPath
                : null);
        DesktopStartupContext startupContext;
        try
        {
            startupContext = DesktopStartupContext.ResolveAsync(
                    StartupArguments,
                    AppDataPaths.ResolveCurrent,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            Console.Error.WriteLine(
                "SrvSurvey diagnostic replay could not start: "
                + exception.Message);
            Environment.ExitCode = 2;
            return;
        }

        StartupContext = startupContext;
        var appDataPaths = startupContext.AppDataPaths;
        var language = LocalizationSettingsStore.ResolveCurrent(appDataPaths);
        LocalizationCatalog.Initialize(language);
        LocalizationCatalog.ApplyCulture(language);
        var applicationLog = new ApplicationLogService(
            startupContext.DiagnosticReplay?.LogsDirectory
                ?? appDataPaths.DataDirectory);
        ApplicationLog = applicationLog;
        var frontierCallback = startupContext.IsDiagnosticReplay
            ? null
            : FrontierOAuthCallback.Find(StartupArguments);
        if (frontierCallback is not null)
        {
            try
            {
                using var frontier = FrontierAccountService.CreateCurrent(
                    appDataPaths.DataDirectory);
                frontier.HandleCallbackAsync(frontierCallback)
                    .GetAwaiter()
                    .GetResult();
                var activated = DesktopApplicationActivator
                    .TryActivateExistingInstance();
                applicationLog.Append(
                    "Frontier authorization callback completed securely.");
                applicationLog.Append(
                    activated
                        ? "Frontier callback restored the running application."
                        : "Frontier callback completed without a running application window to restore.");
                Environment.ExitCode = 0;
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidDataException
                    or InvalidOperationException
                    or NotSupportedException
                    or HttpRequestException
                    or TaskCanceledException
                    or UnauthorizedAccessException)
            {
                applicationLog.Append(
                    "Frontier authorization callback failed: "
                    + exception.Message);
                Environment.ExitCode = 1;
            }

            return;
        }

        var displayCapabilities = OverlayPlatformCapabilities.DetectCurrent();
        var x11ThreadingInitialized = displayCapabilities.UsesX11Compatibility
            ? X11Native.TryInitializeThreading()
            : (bool?)null;
        applicationLog.Append(
            $"SrvSurvey {typeof(Program).Assembly.GetName().Version}");
        applicationLog.Append($"New log path: {applicationLog.CurrentLogPath}");
        applicationLog.Append($"Data folder: {appDataPaths.DataDirectory}");
        if (startupContext.DiagnosticReplay is { } diagnosticReplay)
        {
            applicationLog.Append(
                "Diagnostic replay mode: external effects disabled; "
                + $"commander will be established by {diagnosticReplay.Commander.Name} "
                + $"({diagnosticReplay.Commander.FrontierId}).");
            applicationLog.Append(
                $"Replay session: {diagnosticReplay.Session.SessionDirectory}");
        }
        applicationLog.Append(
            $"Platform: {Environment.OSVersion.Platform} ({Environment.OSVersion.VersionString})");
        applicationLog.Append(
            $"Display host: {displayCapabilities.Host}");
        if (x11ThreadingInitialized is not null)
        {
            applicationLog.Append(x11ThreadingInitialized.Value
                ? "X11 threading: initialized before platform startup."
                : "X11 threading: initialization was unavailable; native X11 access may be unsafe.");
        }

        var useSoftwareRendering = IsSoftwareRenderingRequested(
            Environment.GetEnvironmentVariable(SoftwareRenderingEnvironmentVariable));
        applicationLog.Append(
            useSoftwareRendering
                ? "Windows renderer: software (diagnostic override)."
                : "Windows renderer: automatic.");
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
            BuildAvaloniaApp(useSoftwareRendering)
                .StartWithClassicDesktopLifetime(args);
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
        => BuildAvaloniaApp(useSoftwareRendering: false);

    internal static bool IsSoftwareRenderingRequested(string? value)
        => value is not null
            && (string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "software", StringComparison.OrdinalIgnoreCase));

    private static AppBuilder BuildAvaloniaApp(bool useSoftwareRendering)
    {
        var builder = AppBuilder
            .Configure<App>()
            .UsePlatformDetect();
        if (useSoftwareRendering && OperatingSystem.IsWindows())
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
            });
        }

        return builder
            .WithInterFont()
            .LogToTrace();
    }
}
