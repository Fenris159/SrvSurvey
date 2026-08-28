using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
        Justification = "The Avalonia adapter retains the runtime for the desktop application lifetime.")]
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
            desktopRuntime = DesktopRuntime.Start(
                this,
                desktop,
                new DesktopStartup(
                    Program.StartupArguments,
                    Program.ApplicationLog)
                {
                    AppDataPathsOverride = Program.StartupContext?.AppDataPaths,
                    DiagnosticReplay = Program.StartupContext?.DiagnosticReplay,
                });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
