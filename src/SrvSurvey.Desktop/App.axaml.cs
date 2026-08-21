using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

public sealed partial class App : Application
{
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
