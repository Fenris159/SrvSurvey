using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class App : Application
{
    private GuardianOverlayCoordinator? guardianOverlayCoordinator;
    private ColonizationCommodityOverlayCoordinator?
        colonizationCommodityOverlayCoordinator;
    private RouteOverlayCoordinator? routeOverlayCoordinator;
    private JumpInfoOverlayCoordinator? jumpInfoOverlayCoordinator;
    private SystemSurveyOverlayCoordinator? systemSurveyOverlayCoordinator;
    private SystemNotesWindowCoordinator? systemNotesWindowCoordinator;
    private JourneyWindowCoordinator? journeyWindowCoordinator;
    private RouteWindowCoordinator? routeWindowCoordinator;
    private BiologyPredictionsWindowCoordinator?
        biologyPredictionsWindowCoordinator;
    private BiologyCodexWindowCoordinator? biologyCodexWindowCoordinator;
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
            systemNotesWindowCoordinator = new SystemNotesWindowCoordinator(
                viewModel.SystemNotes,
                mainWindow);
            journeyWindowCoordinator = new JourneyWindowCoordinator(
                viewModel.Journey,
                mainWindow);
            routeWindowCoordinator = new RouteWindowCoordinator(
                viewModel.Route,
                mainWindow);
            biologyPredictionsWindowCoordinator =
                new BiologyPredictionsWindowCoordinator(
                    viewModel.BiologyPredictions,
                    mainWindow);
            biologyCodexWindowCoordinator = new BiologyCodexWindowCoordinator(
                viewModel.BiologyCodex,
                mainWindow,
                Path.Combine(appDataPaths.CacheDirectory, "codex-images"));
            routeOverlayCoordinator = new RouteOverlayCoordinator(
                viewModel.Route,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            guardianOverlayCoordinator = new GuardianOverlayCoordinator(
                viewModel.Guardian,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            jumpInfoOverlayCoordinator = new JumpInfoOverlayCoordinator(
                viewModel.JumpInfo,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            systemSurveyOverlayCoordinator = new SystemSurveyOverlayCoordinator(
                viewModel.SystemSurvey,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());

            void SynchronizeGuardianPriority()
            {
                systemSurveyOverlayCoordinator?.SetFssObscured(
                    guardianOverlayCoordinator?.IsVisible == true);
                systemSurveyOverlayCoordinator?.SetBodyInfoObscured(
                    guardianOverlayCoordinator?.IsVisible == true);
                systemSurveyOverlayCoordinator?.SetBiologyObscured(
                    guardianOverlayCoordinator?.IsVisible == true);
                systemSurveyOverlayCoordinator?.SetBiologyStatusObscured(
                    guardianOverlayCoordinator?.IsVisible == true
                    || jumpInfoOverlayCoordinator?.IsVisible == true);
                guardianOverlayCoordinator?.SetObscured(
                    jumpInfoOverlayCoordinator?.IsVisible == true
                    || (systemSurveyOverlayCoordinator?.IsFssVisible == true
                        && viewModel.SystemSurvey.IsFssInfoForced)
                    || (systemSurveyOverlayCoordinator?.IsBodyInfoVisible == true
                        && viewModel.SystemSurvey.IsBodyInfoForced));
            }

            jumpInfoOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeGuardianPriority();
            systemSurveyOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeGuardianPriority();
            guardianOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeGuardianPriority();
            SynchronizeGuardianPriority();
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
                Dispatcher.UIThread.Post(async () =>
                {
                    var handled = false;
                    switch (eventArgs.Action)
                    {
                        case GlobalInputAction.ToggleAllVisibility:
                            var suppress =
                                guardianOverlayCoordinator?.IsVisible == true
                                || jumpInfoOverlayCoordinator?.IsVisible == true
                                || systemSurveyOverlayCoordinator?.IsVisible == true
                                || routeOverlayCoordinator?.IsVisible == true
                                || colonizationCommodityOverlayCoordinator
                                    ?.IsVisible == true;
                            jumpInfoOverlayCoordinator?.SetSuppressed(suppress);
                            systemSurveyOverlayCoordinator?.SetSuppressed(suppress);
                            guardianOverlayCoordinator?.SetSuppressed(suppress);
                            routeOverlayCoordinator?.SetSuppressed(suppress);
                            colonizationCommodityOverlayCoordinator
                                ?.SetSuppressed(suppress);
                            handled = true;
                            break;

                        case GlobalInputAction.ShowJumpInfo:
                            handled = viewModel.JumpInfo.ToggleForcedVisibility();
                            break;

                        case GlobalInputAction.ShowFssInfo:
                            handled = viewModel.SystemSurvey
                                .ToggleFssInfoVisibility();
                            break;

                        case GlobalInputAction.ShowBodyInfo:
                            handled = viewModel.SystemSurvey
                                .ToggleBodyInfoVisibility();
                            break;

                        case GlobalInputAction.ShowColonyShopping:
                            colonizationCommodityOverlayCoordinator
                                ?.ToggleVisibility();
                            handled = true;
                            break;

                        case GlobalInputAction.ShowSystemNotes:
                            handled = systemNotesWindowCoordinator is not null
                                && await systemNotesWindowCoordinator
                                    .ShowOrActivateAsync();
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
                viewModel.JumpInfo.Dispose();
                globalControllerInputService?.Dispose();
                globalControllerInputService = null;
                globalKeyboardHookService?.Dispose();
                globalKeyboardHookService = null;
                colonizationCommodityOverlayCoordinator?.Dispose();
                colonizationCommodityOverlayCoordinator = null;
                systemNotesWindowCoordinator?.Dispose();
                systemNotesWindowCoordinator = null;
                journeyWindowCoordinator?.Dispose();
                journeyWindowCoordinator = null;
                routeWindowCoordinator?.Dispose();
                routeWindowCoordinator = null;
                biologyPredictionsWindowCoordinator?.Dispose();
                biologyPredictionsWindowCoordinator = null;
                viewModel.BiologyPredictions.Dispose();
                biologyCodexWindowCoordinator?.Dispose();
                biologyCodexWindowCoordinator = null;
                viewModel.BiologyCodex.Dispose();
                routeOverlayCoordinator?.Dispose();
                routeOverlayCoordinator = null;
                jumpInfoOverlayCoordinator?.Dispose();
                jumpInfoOverlayCoordinator = null;
                systemSurveyOverlayCoordinator?.Dispose();
                systemSurveyOverlayCoordinator = null;
                guardianOverlayCoordinator?.Dispose();
                guardianOverlayCoordinator = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
