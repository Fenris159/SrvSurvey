using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
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
    private SphericalSearchOverlayCoordinator? sphericalSearchOverlayCoordinator;
    private JumpInfoOverlayCoordinator? jumpInfoOverlayCoordinator;
    private GroundTargetOverlayCoordinator? groundTargetOverlayCoordinator;
    private CombatOverlayCoordinator? combatOverlayCoordinator;
    private StationInfoOverlayCoordinator? stationInfoOverlayCoordinator;
    private HumanSiteOverlayCoordinator? humanSiteOverlayCoordinator;
    private SystemSurveyOverlayCoordinator? systemSurveyOverlayCoordinator;
    private SystemNotesWindowCoordinator? systemNotesWindowCoordinator;
    private JourneyWindowCoordinator? journeyWindowCoordinator;
    private RouteWindowCoordinator? routeWindowCoordinator;
    private BiologyPredictionsWindowCoordinator?
        biologyPredictionsWindowCoordinator;
    private BiologyCodexWindowCoordinator? biologyCodexWindowCoordinator;
    private BiologyCodexBingoWindowCoordinator?
        biologyCodexBingoWindowCoordinator;
    private ErrorReportWindowCoordinator? errorReportWindowCoordinator;
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
            var applicationLog = Program.ApplicationLog
                ?? new ApplicationLogService(appDataPaths.DataDirectory);
            var settingsMigration = new LegacyUiSettingsMigrator()
                .MigrateIfNeeded(appDataPaths);
            if (settingsMigration.Migrated)
            {
                applicationLog.Append(
                    $"Migrated {settingsMigration.MappedPreferenceCount:N0} legacy UI preferences."
                    + (settingsMigration.PreviousSettingsBackupPath is null
                        ? string.Empty
                        : " Previous settings backup: "
                            + settingsMigration.PreviousSettingsBackupPath));
            }
            else if (settingsMigration.Error is not null)
            {
                applicationLog.Append(
                    "Legacy UI settings migration was skipped: "
                    + settingsMigration.Error);
            }

            var overlayTheme = new LegacyOverlayThemeStore(
                Path.Combine(appDataPaths.DataDirectory, "theme.json"))
                .Load();
            if (overlayTheme.Error is not null)
            {
                applicationLog.Append(overlayTheme.Error);
            }

            var themeService = new RavenThemeService(
                this,
                new ThemePreferenceStore(appDataPaths.UiSettingsPath),
                overlayTheme);
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
                inputSettings: inputSettings,
                applicationLogService: applicationLog);
            var mainWindow = new MainWindow(viewModel);
            desktop.MainWindow = mainWindow;
            var errorReports = new ErrorReportWindowCoordinator(
                mainWindow,
                applicationLog,
                () => viewModel.CurrentJournalPath,
                () =>
                {
                    viewModel.ShowDiagnostics();
                    mainWindow.Activate();
                });
            errorReportWindowCoordinator = errorReports;
            void HandleUiException(
                object? sender,
                DispatcherUnhandledExceptionEventArgs eventArgs)
            {
                errorReports.Show(eventArgs.Exception);
                eventArgs.Handled = true;
            }

            void HandleUnobservedTaskException(
                object? sender,
                UnobservedTaskExceptionEventArgs eventArgs)
            {
                errorReports.Show(eventArgs.Exception);
                eventArgs.SetObserved();
            }

            Dispatcher.UIThread.UnhandledException += HandleUiException;
            TaskScheduler.UnobservedTaskException +=
                HandleUnobservedTaskException;
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
            biologyCodexBingoWindowCoordinator =
                new BiologyCodexBingoWindowCoordinator(
                    viewModel.CodexBingo,
                    mainWindow,
                    viewModel.OpenCodexBingoNearestSearchAsync);
            sphericalSearchOverlayCoordinator = new SphericalSearchOverlayCoordinator(
                viewModel.Search,
                viewModel.BoxelSearch,
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
            groundTargetOverlayCoordinator = new GroundTargetOverlayCoordinator(
                viewModel.GroundTarget,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            combatOverlayCoordinator = new CombatOverlayCoordinator(
                viewModel.Combat,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            stationInfoOverlayCoordinator = new StationInfoOverlayCoordinator(
                viewModel.StationInfo,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            humanSiteOverlayCoordinator = new HumanSiteOverlayCoordinator(
                viewModel.HumanSite,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent());
            systemSurveyOverlayCoordinator = new SystemSurveyOverlayCoordinator(
                viewModel.SystemSurvey,
                viewModel.SurfaceSurvey,
                OverlayPlatformService.CreateCurrent(),
                GameWindowTracker.CreateCurrent(),
                () => viewModel.CommanderName);

            void SynchronizeOverlayPriority()
            {
                var liveGuardianSite =
                    guardianOverlayCoordinator?.IsLiveSiteVisible == true;
                var humanSite =
                    humanSiteOverlayCoordinator?.IsVisible == true;
                systemSurveyOverlayCoordinator?.SetFssObscured(
                    liveGuardianSite);
                systemSurveyOverlayCoordinator?.SetBodyInfoObscured(
                    liveGuardianSite);
                systemSurveyOverlayCoordinator?.SetBiologyObscured(
                    liveGuardianSite || humanSite);
                systemSurveyOverlayCoordinator?.SetBiologyStatusObscured(
                    liveGuardianSite
                    || humanSite
                    || jumpInfoOverlayCoordinator?.IsVisible == true);
                systemSurveyOverlayCoordinator?.SetPriorScansObscured(
                    liveGuardianSite
                    || humanSite
                    || stationInfoOverlayCoordinator?.IsVisible == true);
                systemSurveyOverlayCoordinator?.SetSurfaceObscured(
                    liveGuardianSite || humanSite);
                guardianOverlayCoordinator?.SetObscured(
                    jumpInfoOverlayCoordinator?.IsVisible == true
                    || (systemSurveyOverlayCoordinator?.IsFssVisible == true
                        && viewModel.SystemSurvey.IsFssInfoForced)
                    || (systemSurveyOverlayCoordinator?.IsBodyInfoVisible == true
                        && viewModel.SystemSurvey.IsBodyInfoForced));
                guardianOverlayCoordinator?.SetSystemSummaryObscured(
                    viewModel.SystemSurvey.IsFssInfoForced
                    || viewModel.SystemSurvey.IsBodyInfoForced);
            }

            jumpInfoOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeOverlayPriority();
            systemSurveyOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeOverlayPriority();
            guardianOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeOverlayPriority();
            stationInfoOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeOverlayPriority();
            humanSiteOverlayCoordinator.VisibilityChanged += (_, _) =>
                SynchronizeOverlayPriority();
            SynchronizeOverlayPriority();
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
                        case GlobalInputAction.MapZoomIn:
                            handled = humanSiteOverlayCoordinator
                                ?.AdjustZoom(zoomIn: true) == true
                                || systemSurveyOverlayCoordinator
                                    ?.AdjustSurfaceZoom(zoomIn: true) == true;
                            break;

                        case GlobalInputAction.MapZoomOut:
                            handled = humanSiteOverlayCoordinator
                                ?.AdjustZoom(zoomIn: false) == true
                                || systemSurveyOverlayCoordinator
                                    ?.AdjustSurfaceZoom(zoomIn: false) == true;
                            break;

                        case GlobalInputAction.MapZoomAuto:
                            handled = humanSiteOverlayCoordinator
                                ?.ResetZoom() == true
                                || systemSurveyOverlayCoordinator
                                    ?.ResetSurfaceZoom() == true;
                            break;

                        case GlobalInputAction.MapBeHuge:
                            handled = humanSiteOverlayCoordinator
                                ?.ToggleHuge() == true;
                            break;

                        case GlobalInputAction.ToggleAllVisibility:
                            var suppress =
                                guardianOverlayCoordinator?.IsVisible == true
                                || jumpInfoOverlayCoordinator?.IsVisible == true
                                || systemSurveyOverlayCoordinator?.IsVisible == true
                                || groundTargetOverlayCoordinator?.IsVisible == true
                                || combatOverlayCoordinator?.IsVisible == true
                                || stationInfoOverlayCoordinator?.IsVisible == true
                                || humanSiteOverlayCoordinator?.IsVisible == true
                                || sphericalSearchOverlayCoordinator?.IsVisible == true
                                || colonizationCommodityOverlayCoordinator
                                    ?.IsVisible == true;
                            jumpInfoOverlayCoordinator?.SetSuppressed(suppress);
                            systemSurveyOverlayCoordinator?.SetSuppressed(suppress);
                            groundTargetOverlayCoordinator?.SetSuppressed(suppress);
                            combatOverlayCoordinator?.SetSuppressed(suppress);
                            guardianOverlayCoordinator?.SetSuppressed(suppress);
                            stationInfoOverlayCoordinator?.SetSuppressed(suppress);
                            humanSiteOverlayCoordinator?.SetSuppressed(suppress);
                            sphericalSearchOverlayCoordinator?.SetSuppressed(suppress);
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

                        case GlobalInputAction.ShowStationInfo:
                            handled = viewModel.StationInfo
                                .ToggleForcedVisibility();
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

                        case GlobalInputAction.Track1:
                        case GlobalInputAction.Track2:
                        case GlobalInputAction.Track3:
                        case GlobalInputAction.Track4:
                        case GlobalInputAction.Track5:
                        case GlobalInputAction.Track6:
                        case GlobalInputAction.Track7:
                        case GlobalInputAction.Track8:
                            var trackerNumber = eventArgs.Action switch
                            {
                                GlobalInputAction.Track1 => 1,
                                GlobalInputAction.Track2 => 2,
                                GlobalInputAction.Track3 => 3,
                                GlobalInputAction.Track4 => 4,
                                GlobalInputAction.Track5 => 5,
                                GlobalInputAction.Track6 => 6,
                                GlobalInputAction.Track7 => 7,
                                _ => 8,
                            };
                            handled = await viewModel.SurfaceSurvey
                                .ToggleQuickTrackerAsync(trackerNumber);
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
                Dispatcher.UIThread.UnhandledException -= HandleUiException;
                TaskScheduler.UnobservedTaskException -=
                    HandleUnobservedTaskException;
                applicationLog.Append("Application exit");
                errorReportWindowCoordinator?.Dispose();
                errorReportWindowCoordinator = null;
                viewModel.DiagnosticsLog.Dispose();
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
                viewModel.SurfaceSurvey.Dispose();
                biologyCodexBingoWindowCoordinator?.Dispose();
                biologyCodexBingoWindowCoordinator = null;
                viewModel.CodexBingo.Dispose();
                sphericalSearchOverlayCoordinator?.Dispose();
                sphericalSearchOverlayCoordinator = null;
                jumpInfoOverlayCoordinator?.Dispose();
                jumpInfoOverlayCoordinator = null;
                groundTargetOverlayCoordinator?.Dispose();
                groundTargetOverlayCoordinator = null;
                combatOverlayCoordinator?.Dispose();
                combatOverlayCoordinator = null;
                stationInfoOverlayCoordinator?.Dispose();
                stationInfoOverlayCoordinator = null;
                viewModel.StationInfo.Dispose();
                humanSiteOverlayCoordinator?.Dispose();
                humanSiteOverlayCoordinator = null;
                systemSurveyOverlayCoordinator?.Dispose();
                systemSurveyOverlayCoordinator = null;
                guardianOverlayCoordinator?.Dispose();
                guardianOverlayCoordinator = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
