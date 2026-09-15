using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

public sealed partial class App : Application
{
#if DEBUG
    private static int developerToolsAttached;
#endif

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "CodeQuality",
        "S4487:Unread private fields should be removed",
        Justification = "The Avalonia adapter retains the runtime for the desktop application lifetime."
    )]
    private DesktopRuntime? desktopRuntime;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        if (Interlocked.Exchange(ref developerToolsAttached, 1) == 0)
        {
            this.AttachDeveloperTools();
        }
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = StartDesktopAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartDesktopAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            ApplicationStartupInstanceDecision startupDecision = await EvaluateStartupAsync(desktop);
            if (startupDecision == ApplicationStartupInstanceDecision.Exit)
            {
                desktop.Shutdown();
                return;
            }

            desktopRuntime = DesktopRuntime.Start(
                this,
                desktop,
                new DesktopStartup(Program.StartupArguments, Program.ApplicationLog)
                {
                    AppDataPathsOverride = Program.StartupContext?.AppDataPaths,
                    DiagnosticReplay = Program.StartupContext?.DiagnosticReplay,
                    BringMainWindowToFront = startupDecision == ApplicationStartupInstanceDecision.ReplacedExisting,
                }
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
            )
        {
            Program.ApplicationLog?.Append("Application instance startup check failed: " + exception.Message);
            await Console.Error.WriteLineAsync(
                "SrvSurvey could not verify whether another instance is running: " + exception.Message
            );
            desktop.Shutdown(1);
        }
    }

    private static async Task<ApplicationStartupInstanceDecision> EvaluateStartupAsync(
        IClassicDesktopStyleApplicationLifetime desktop
    )
    {
        if (StartupOptions.AllowsConcurrentInstance(Program.StartupArguments))
        {
            return ApplicationStartupInstanceDecision.Continue;
        }

        AppDataPaths appDataPaths = Program.StartupContext?.AppDataPaths ?? AppDataPaths.ResolveCurrent();
        await using var manager = new ApplicationInstanceManager(
            appDataPaths.DataDirectory,
            () => RequestStartupShutdownAsync(desktop),
            message => Program.ApplicationLog?.Append(message)
        );
        var gate = new ApplicationStartupInstanceGate(manager);
        return await gate.EvaluateAsync(
            concurrentInstanceAuthorized: false,
            (scan, closeOtherInstances, cancellationToken) =>
                ShowExistingInstancePromptAsync(desktop, scan, closeOtherInstances, cancellationToken)
        );
    }

    private static async Task<bool> ShowExistingInstancePromptAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        ApplicationInstanceScan scan,
        Func<Task> closeOtherInstances,
        CancellationToken cancellationToken
    )
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await await Dispatcher.UIThread.InvokeAsync(
                () => ShowExistingInstancePromptAsync(desktop, scan, closeOtherInstances, cancellationToken),
                DispatcherPriority.Normal,
                cancellationToken
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new MultipleApplicationInstancesDialog(scan, closeOtherInstances);
        desktop.MainWindow = dialog;
        return await dialog.ShowForStartupAsync();
    }

    private static Task RequestStartupShutdownAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Dispatcher.UIThread.Post(() => desktop.Shutdown());
        return Task.CompletedTask;
    }
}
