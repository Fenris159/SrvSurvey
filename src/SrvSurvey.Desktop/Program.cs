using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    internal const string SoftwareRenderingEnvironmentVariable = "SRVSURVEY_SOFTWARE_RENDERING";

    internal static string[] StartupArguments { get; private set; } = [];

    internal static ApplicationLogService? ApplicationLog { get; private set; }

    internal static DesktopStartupContext? StartupContext { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (ApplicationRestartService.TryRunRestartHelper(args))
        {
            return;
        }

        ApplicationUpdateStartup updateStartup = ApplicationUpdateBootstrap.ParseStartupArguments(args);
        if (TryRunUpdateHelper(updateStartup))
        {
            return;
        }

        StartupArguments = updateStartup.ApplicationArguments.ToArray();
        ApplicationUpdateBootstrap.SetPendingConfirmation(
            updateStartup.Mode == ApplicationUpdateStartupMode.Confirm ? updateStartup.PlanPath : null
        );
        ApplicationUpdateBootstrap.SetPendingOutcome(
            updateStartup.Mode == ApplicationUpdateStartupMode.Result ? updateStartup.PlanPath : null
        );
        if (!TryResolveStartupContext(out DesktopStartupContext? startupContext))
        {
            return;
        }

        StartupContext = startupContext;
        AppDataPaths appDataPaths = startupContext.AppDataPaths;
        string language = LocalizationSettingsStore.ResolveCurrent(appDataPaths);
        LocalizationCatalog.Initialize(language);
        LocalizationCatalog.ApplyCulture(language);
        var applicationLog = new ApplicationLogService(
            startupContext.DiagnosticReplay?.LogsDirectory ?? appDataPaths.DataDirectory
        );
        ApplicationLog = applicationLog;
        if (TryHandleFrontierCallback(startupContext, appDataPaths, applicationLog))
        {
            return;
        }

        var displayCapabilities = OverlayPlatformCapabilities.DetectCurrent();
        bool? x11ThreadingInitialized = displayCapabilities.UsesX11Compatibility
            ? X11Native.TryInitializeThreading()
            : (bool?)null;
        applicationLog.Append($"SrvSurvey {typeof(Program).Assembly.GetName().Version}");
        applicationLog.Append($"New log path: {applicationLog.CurrentLogPath}");
        applicationLog.Append($"Data folder: {appDataPaths.DataDirectory}");
        if (startupContext.DiagnosticReplay is { } diagnosticReplay)
        {
            applicationLog.Append(
                "Diagnostic replay mode: external effects disabled; "
                    + $"commander will be established by {diagnosticReplay.Commander.Name} "
                    + $"({diagnosticReplay.Commander.FrontierId})."
            );
            applicationLog.Append($"Replay session: {diagnosticReplay.Session.SessionDirectory}");
        }
        applicationLog.Append($"Platform: {Environment.OSVersion.Platform} ({Environment.OSVersion.VersionString})");
        applicationLog.Append($"Display host: {displayCapabilities.Host}");
        if (x11ThreadingInitialized is not null)
        {
            applicationLog.Append(
                x11ThreadingInitialized.Value
                    ? "X11 threading: initialized before platform startup."
                    : "X11 threading: initialization was unavailable; native X11 access may be unsafe."
            );
        }

        bool useSoftwareRendering = IsSoftwareRenderingRequested(
            Environment.GetEnvironmentVariable(SoftwareRenderingEnvironmentVariable)
        );
        applicationLog.Append(
            useSoftwareRendering
                ? "Application renderer: software (diagnostic override)."
                : "Application renderer: automatic."
        );
        using var traceListener = new ApplicationLogTraceListener(applicationLog);
        void HandleUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            applicationLog.Append("Unhandled process exception: " + eventArgs.ExceptionObject);
        }

        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        Trace.Listeners.Add(traceListener);
        try
        {
            BuildAvaloniaApp(useSoftwareRendering).StartWithClassicDesktopLifetime(args);
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

    private static bool TryRunUpdateHelper(ApplicationUpdateStartup updateStartup)
    {
        if (updateStartup.Mode != ApplicationUpdateStartupMode.Apply)
        {
            return false;
        }

        Environment.ExitCode = ApplicationUpdateBootstrap
            .RunHelperAsync(updateStartup.PlanPath!)
            .GetAwaiter()
            .GetResult();
        return true;
    }

    private static bool TryResolveStartupContext([NotNullWhen(true)] out DesktopStartupContext? startupContext)
    {
        try
        {
            startupContext = DesktopStartupContext
                .ResolveAsync(StartupArguments, AppDataPaths.ResolveCurrent, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(GetStartupFailureMessage(StartupArguments, exception));
            Environment.ExitCode = 2;
            startupContext = null;
            return false;
        }
    }

    internal static string GetStartupFailureMessage(IReadOnlyList<string> arguments, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(exception);
        string startupMode = StartupOptions.HasDiagnosticReplayOption(arguments)
            ? "diagnostic replay"
            : "normal startup";
        return $"SrvSurvey {startupMode} could not start: {exception.Message}";
    }

    private static bool TryHandleFrontierCallback(
        DesktopStartupContext startupContext,
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog
    )
    {
        FrontierOAuthCallback? frontierCallback = startupContext.IsDiagnosticReplay
            ? null
            : FrontierOAuthCallback.Find(StartupArguments);
        if (frontierCallback is null)
        {
            return false;
        }

        try
        {
            using var frontier = FrontierAccountService.CreateCurrent(appDataPaths.DataDirectory);
            frontier.HandleCallbackAsync(frontierCallback).GetAwaiter().GetResult();
            bool activated = DesktopApplicationActivator.TryActivateExistingInstance();
            applicationLog.Append("Frontier authorization callback completed securely.");
            applicationLog.Append(
                activated
                    ? "Frontier callback restored the running application."
                    : "Frontier callback completed without a running application window to restore."
            );
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or InvalidOperationException
                        or NotSupportedException
                        or HttpRequestException
                        or TaskCanceledException
                        or UnauthorizedAccessException
            )
        {
            applicationLog.Append("Frontier authorization callback failed: " + exception.Message);
            Environment.ExitCode = 1;
        }

        return true;
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(useSoftwareRendering: false);

    internal static bool IsSoftwareRenderingRequested(string? value) =>
        value is not null
        && (
            string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "software", StringComparison.OrdinalIgnoreCase)
        );

    private static AppBuilder BuildAvaloniaApp(bool useSoftwareRendering)
    {
        AppBuilder builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (useSoftwareRendering && OperatingSystem.IsWindows())
        {
            builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        }
        else if (OperatingSystem.IsLinux())
        {
            builder = builder.With(CreateX11Options(useSoftwareRendering));
        }

        return builder.WithInterFont().With(SrvSurveyFontConfiguration.CreateOptions()).LogToTrace();
    }

    internal static X11PlatformOptions CreateX11Options(bool useSoftwareRendering)
    {
        var options = new X11PlatformOptions
        {
            UseDBusMenu = false,
            // Keep tooltips, combo-box drop-downs, and flyouts in the owning
            // window. Separate X11 popup windows can briefly intercept pointer
            // input and race the overlay window scanner while they close.
            OverlayPopups = true,
#pragma warning disable AVALONIA_X11_FORCE_CSD // Force reserved Raven-themed chrome on X11 and XWayland.
            ForceDrawnDecorations = true,
#pragma warning restore AVALONIA_X11_FORCE_CSD
        };
        if (useSoftwareRendering)
        {
            options.RenderingMode = [X11RenderingMode.Software];
        }

        return options;
    }
}
