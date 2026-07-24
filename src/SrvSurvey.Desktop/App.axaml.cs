using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var themeService = new RavenThemeService(
                this,
                new ThemePreferenceStore());
            themeService.ApplyCurrent();

            var configuredJournalDirectory = StartupOptions.GetJournalDirectory(
                Program.StartupArguments);
            desktop.MainWindow = new MainWindow(
                new MainWindowViewModel(configuredJournalDirectory, themeService));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
