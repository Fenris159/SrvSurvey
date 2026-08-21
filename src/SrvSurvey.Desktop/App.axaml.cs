using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

public sealed partial class App : Application
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "CodeQuality",
        "S4487:Unread private fields should be removed",
        Justification = "The Avalonia adapter retains the runtime for the desktop application lifetime.")]
    private DesktopRuntime? desktopRuntime;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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
                    Program.ApplicationLog));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
