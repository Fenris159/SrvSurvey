using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class App : Application
{
    private GuardianOverlayCoordinator? guardianOverlayCoordinator;
    private GlobalKeyboardHookService? globalKeyboardHookService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appDataPaths = AppDataPaths.ResolveCurrent();
            var themeService = new RavenThemeService(
                this,
                new ThemePreferenceStore(appDataPaths.UiSettingsPath));
            themeService.ApplyCurrent();
            var capabilities = OverlayPlatformCapabilities.DetectCurrent();
            var inputSettings = new GlobalInputSettingsViewModel(
                new GlobalInputSettingsStore(appDataPaths.UiSettingsPath),
                capabilities);

            var configuredJournalDirectory = StartupOptions.GetJournalDirectory(
                Program.StartupArguments);
            var viewModel = new MainWindowViewModel(
                configuredJournalDirectory,
                themeService,
                appDataPaths,
                inputSettings: inputSettings);
            var mainWindow = new MainWindow(viewModel);
            desktop.MainWindow = mainWindow;
            guardianOverlayCoordinator = new GuardianOverlayCoordinator(
                viewModel.Guardian,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            globalKeyboardHookService = new GlobalKeyboardHookService(
                inputSettings.CurrentSettings,
                capabilities.Host,
                GameWindowTracker.CreateCurrent(),
                () => mainWindow.InputContext.AreShortcutsActive);
            globalKeyboardHookService.StatusChanged += (_, _) =>
            {
                var status = globalKeyboardHookService?.Status;
                if (status is not null)
                {
                    Dispatcher.UIThread.Post(
                        () => inputSettings.UpdateRuntimeStatus(status));
                }
            };
            globalKeyboardHookService.ActionTriggered += (_, eventArgs) =>
                Dispatcher.UIThread.Post(() =>
                {
                    var handled = eventArgs.Action
                        == GlobalInputAction.ToggleAllVisibility;
                    if (handled)
                    {
                        guardianOverlayCoordinator?.ToggleVisibility();
                    }

                    inputSettings.ReportAction(eventArgs.Action, handled);
                });
            inputSettings.SettingsChanged += (_, eventArgs) =>
                globalKeyboardHookService?.Update(eventArgs.Settings);
            inputSettings.UpdateRuntimeStatus(
                globalKeyboardHookService.Status);
            globalKeyboardHookService.Start();
            desktop.Exit += (_, _) =>
            {
                globalKeyboardHookService?.Dispose();
                globalKeyboardHookService = null;
                guardianOverlayCoordinator?.Dispose();
                guardianOverlayCoordinator = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
