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
    private ColonizationCommodityOverlayCoordinator?
        colonizationCommodityOverlayCoordinator;
    private GlobalKeyboardHookService? globalKeyboardHookService;
    private GlobalControllerInputService? globalControllerInputService;

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
            colonizationCommodityOverlayCoordinator =
                new ColonizationCommodityOverlayCoordinator(
                    viewModel.Colonization.CommodityOverlay,
                    OverlayPlatformService.CreateCurrent(),
                    GameWindowTracker.CreateCurrent());
            globalKeyboardHookService = new GlobalKeyboardHookService(
                inputSettings.CurrentSettings,
                capabilities.Host,
                GameWindowTracker.CreateCurrent(),
                () => mainWindow.InputContext.AreShortcutsActive);
            globalControllerInputService = new GlobalControllerInputService(
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
            globalControllerInputService.StatusChanged += (_, _) =>
            {
                var status = globalControllerInputService?.Status;
                if (status is not null)
                {
                    Dispatcher.UIThread.Post(
                        () => inputSettings.UpdateControllerRuntimeStatus(
                            status));
                }
            };

            void HandleAction(GlobalInputActionTriggeredEventArgs eventArgs)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var handled = false;
                    switch (eventArgs.Action)
                    {
                        case GlobalInputAction.ToggleAllVisibility:
                            var suppress =
                                guardianOverlayCoordinator?.IsVisible == true
                                || colonizationCommodityOverlayCoordinator
                                    ?.IsVisible == true;
                            guardianOverlayCoordinator?.SetSuppressed(suppress);
                            colonizationCommodityOverlayCoordinator
                                ?.SetSuppressed(suppress);
                            handled = true;
                            break;

                        case GlobalInputAction.ShowColonyShopping:
                            colonizationCommodityOverlayCoordinator
                                ?.ToggleVisibility();
                            handled = true;
                            break;

                        case GlobalInputAction.RefreshColonyData:
                            handled = viewModel.Colonization.IsEnabled;
                            if (handled)
                            {
                                _ = viewModel.Colonization.RefreshAsync();
                            }

                            break;

                        case GlobalInputAction.CollapseColonyData:
                            viewModel.Colonization.CommodityOverlay
                                .ToggleSatisfiedGroups();
                            handled = true;
                            break;
                    }

                    inputSettings.ReportAction(eventArgs.Action, handled);
                });
            }

            globalKeyboardHookService.ActionTriggered += (_, eventArgs) =>
                HandleAction(eventArgs);
            globalControllerInputService.ActionTriggered += (_, eventArgs) =>
                HandleAction(eventArgs);
            inputSettings.SettingsChanged += (_, eventArgs) =>
            {
                globalKeyboardHookService?.Update(eventArgs.Settings);
                globalControllerInputService?.Update(eventArgs.Settings);
            };
            inputSettings.UpdateRuntimeStatus(
                globalKeyboardHookService.Status);
            inputSettings.UpdateControllerRuntimeStatus(
                globalControllerInputService.Status);
            globalKeyboardHookService.Start();
            globalControllerInputService.Start();
            desktop.Exit += (_, _) =>
            {
                globalControllerInputService?.Dispose();
                globalControllerInputService = null;
                globalKeyboardHookService?.Dispose();
                globalKeyboardHookService = null;
                colonizationCommodityOverlayCoordinator?.Dispose();
                colonizationCommodityOverlayCoordinator = null;
                guardianOverlayCoordinator?.Dispose();
                guardianOverlayCoordinator = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
