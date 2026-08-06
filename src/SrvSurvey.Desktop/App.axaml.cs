using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Avalonia owns the App lifetime; OnExit disposes all application services.")]
public sealed partial class App : Application
{
    private GuardianOverlayCoordinator? guardianOverlayCoordinator;
    private ColonizationCommodityOverlayCoordinator?
        colonizationCommodityOverlayCoordinator;
    private SphericalSearchOverlayCoordinator? sphericalSearchOverlayCoordinator;
    private JumpInfoOverlayCoordinator? jumpInfoOverlayCoordinator;
    private RouteBioOverlayCoordinator? routeBioOverlayCoordinator;
    private FleetCarrierRouteOverlayCoordinator?
        fleetCarrierRouteOverlayCoordinator;
    private FleetCarrierJumpCountdownCoordinator?
        fleetCarrierJumpCountdownCoordinator;
    private GroundTargetOverlayCoordinator? groundTargetOverlayCoordinator;
    private CombatOverlayCoordinator? combatOverlayCoordinator;
    private StationInfoOverlayCoordinator? stationInfoOverlayCoordinator;
    private HumanSiteOverlayCoordinator? humanSiteOverlayCoordinator;
    private SystemSurveyOverlayCoordinator? systemSurveyOverlayCoordinator;
    private QuestIndicatorOverlayCoordinator? questIndicatorOverlayCoordinator;
    private NotificationOverlayCoordinator? notificationOverlayCoordinator;
    private PulseOverlayCoordinator? pulseOverlayCoordinator;
    private StreamOverlayCoordinator? streamOverlayCoordinator;
    private VrOverlayCoordinator? vrOverlayCoordinator;
    private GalaxyMapOverlayCoordinator? galaxyMapOverlayCoordinator;
    private MultiGameCommanderOverlayCoordinator?
        multiGameCommanderOverlayCoordinator;
    private SystemNotesWindowCoordinator? systemNotesWindowCoordinator;
    private JourneyWindowCoordinator? journeyWindowCoordinator;
    private RouteWindowCoordinator? routeWindowCoordinator;
    private RouteWindowCoordinator? fleetCarrierRouteWindowCoordinator;
    private BiologyPredictionsWindowCoordinator?
        biologyPredictionsWindowCoordinator;
    private BiologyCodexWindowCoordinator? biologyCodexWindowCoordinator;
    private BiologyCodexBingoWindowCoordinator?
        biologyCodexBingoWindowCoordinator;
    private ErrorReportWindowCoordinator? errorReportWindowCoordinator;
    private GlobalKeyboardHookService? globalKeyboardHookService;
    private GlobalControllerInputService? globalControllerInputService;
    private OverlayPresentationSession? overlayPresentationSession;

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

            try
            {
                var organicMigration = new LegacyOrganicProfileMigrator(
                        appDataPaths.DataDirectory)
                    .MigrateAsync()
                    .GetAwaiter()
                    .GetResult();
                if (organicMigration.Migrated)
                {
                    applicationLog.Append(
                        "Migrated retired organic history into legacy-compatible "
                            + $"profile/system data: {organicMigration.MigratedProfileCount:N0} "
                            + $"profile(s), {organicMigration.MigratedBodyCount:N0} body file(s), "
                            + $"{organicMigration.MigratedScanCount:N0} scan(s), and "
                            + $"{organicMigration.MigratedOrganismCount:N0} organism(s).");
                }

                foreach (var error in organicMigration.Errors)
                {
                    applicationLog.Append(
                        "Legacy organic history was preserved without conversion: "
                            + error);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                applicationLog.Append(
                    "Legacy organic migration was skipped without modifying its "
                        + "source data: "
                        + exception.Message);
            }

            var overlayTheme = new LegacyOverlayThemeStore(
                Path.Combine(appDataPaths.DataDirectory, "theme.json"))
                .Load();
            if (overlayTheme.Error is not null)
            {
                applicationLog.Append(overlayTheme.Error);
            }

            var overlayLayoutStore = new LegacyOverlayLayoutStore(
                appDataPaths.DataDirectory);
            var overlayLayout = overlayLayoutStore.Load();
            if (overlayLayout.Error is not null)
            {
                applicationLog.Append(overlayLayout.Error);
            }

            var themeService = new RavenThemeService(
                this,
                new ThemePreferenceStore(appDataPaths.UiSettingsPath),
                overlayTheme);
            themeService.ApplyCurrent();
            themeService.OverlayThemeChanged += (_, _) =>
            {
                OverlayThemeResources.RefreshAll();
            };
            var capabilities = OverlayPlatformCapabilities.DetectCurrent();
            var inputSettings = new GlobalInputSettingsViewModel(
                new GlobalInputSettingsStore(appDataPaths.UiSettingsPath),
                capabilities);
            var overlayPresentation = OverlayPresentationSession.CreateCurrent();
            overlayPresentationSession = overlayPresentation;
            applicationLog.Append(
                $"Overlay presentation: {overlayPresentation.Decision.Mode}. "
                + overlayPresentation.Decision.Reason);
            var overlayInteraction = new OverlayInteractionViewModel(
                overlayPresentation.CreatePlatformService(),
                GameWindowTracker.CreateCurrent(),
                overlayLayoutStore,
                overlayLayout);
            var gameTextInputService = GameTextInputService.CreateCurrent();
            var configuredJournalDirectory = StartupOptions.GetJournalDirectory(
                Program.StartupArguments);
            var commandLineFrontierId = StartupOptions.GetFrontierId(
                Program.StartupArguments);
            var commanderPreferenceStore = new CommanderPreferenceSettingsStore(
                appDataPaths.UiSettingsPath);
            var commanderPreferenceResolution = new CommanderPreferenceResolver(
                    commanderPreferenceStore,
                    new CommanderProfileCatalog(appDataPaths.DataDirectory))
                .ResolveAsync(commandLineFrontierId)
                .GetAwaiter()
                .GetResult();
            if (commanderPreferenceResolution.StatusMessage is not null)
            {
                applicationLog.Append(
                    commanderPreferenceResolution.StatusMessage);
            }

            var targetFrontierId =
                commanderPreferenceResolution.TargetFrontierId;
            var firstFootfallInferenceService =
                FirstFootfallInferenceService.CreateCurrent();
            var canonnHumanSiteClient = new CanonnHumanSiteClient();
            var viewModel = new MainWindowViewModel(
                configuredJournalDirectory,
                themeService,
                appDataPaths,
                inputSettings: inputSettings,
                applicationLogService: applicationLog,
                overlayLayoutStore: overlayLayoutStore,
                overlayLayout: overlayLayout,
                overlayInteraction: overlayInteraction,
                targetFrontierId: targetFrontierId,
                commanderPreferenceSettingsStore: commanderPreferenceStore,
                commanderPreferenceCommandLineOverride:
                    commanderPreferenceResolution.IsCommandLineOverride,
                commanderPreferenceInitialStatus:
                    commanderPreferenceResolution.StatusMessage,
                firstFootfallInferenceService:
                    firstFootfallInferenceService,
                systemBodyDataClient: new SystemBodyDataClient(),
                canonnHumanSiteClient: canonnHumanSiteClient,
                canonnHumanSitePublisher: canonnHumanSiteClient);
            IGameWindowTracker CreateOverlayGameWindowTracker()
            {
                return new OverlayGameWindowTracker(
                    GameWindowTracker.CreateCurrent(),
                    () => viewModel.OverlayBehavior.KeepWhenGameLosesFocus
                        || viewModel.OverlayInteraction.IsEditing
                        || viewModel.OverlayInteraction.IsLiveInteractionEnabled);
            }

            var mainWindow = new MainWindow(viewModel);
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = mainWindow;
            void HandleFrontierAuthorizationCallback(
                object? sender,
                EventArgs eventArgs)
            {
                mainWindow.RestoreAndActivate();
            }

            viewModel.FrontierProfile.AuthorizationCallbackReceived +=
                HandleFrontierAuthorizationCallback;
            async Task RestartApplicationAsync(string reason)
            {
                new ApplicationRestartService().StartReplacement();
                applicationLog.Append(reason + "; replacement process started.");
                await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown());
            }

            Task RestartAfterProfileImportAsync()
            {
                return RestartApplicationAsync("Profile import verified");
            }

            Task RestartAfterJournalChangeAsync()
            {
                return RestartApplicationAsync("Journal folder changed");
            }

            Task RestartAfterCommanderPreferenceChangeAsync()
            {
                return RestartApplicationAsync("Commander preference changed");
            }

            viewModel.ReferenceDataUpdates.SetRestartHandler(() =>
                RestartApplicationAsync("Published reference data refreshed"));
            viewModel.Localization.SetRestartHandler(() =>
                RestartApplicationAsync("Language preference changed"));
            var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
            viewModel.ReleaseUpdates.ConfigureInstaller(
                new ReleaseInstallerConfiguration(
                    new ReleasePackageDownloadService(),
                    new ReleasePackageStagingService(),
                    new ReleaseInstallationPreparer(),
                    new ApplicationUpdateHandoffService(),
                    appDataPaths.DataDirectory,
                    AppContext.BaseDirectory,
                    Program.StartupArguments,
                    async () => await Dispatcher.UIThread.InvokeAsync(
                        () => desktop.Shutdown()),
                    string.IsNullOrWhiteSpace(appImagePath)
                        ? null
                        : "This AppImage is mounted read-only and cannot replace itself; open the selected release and install its AppImage manually.",
                    IsAppImage: !string.IsNullOrWhiteSpace(appImagePath)));

            viewModel.ProfileImportCompleted += RestartAfterProfileImportAsync;
            viewModel.JournalSettings.RestartRequested +=
                RestartAfterJournalChangeAsync;
            viewModel.CommanderPreference.RestartRequested +=
                RestartAfterCommanderPreferenceChangeAsync;
            async Task WriteClipboardAsync(string text)
            {
                var clipboard = mainWindow.Clipboard
                    ?? throw new InvalidOperationException(
                        "The desktop clipboard is not available.");
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            }

            async Task<string?> ReadClipboardAsync()
            {
                var clipboard = mainWindow.Clipboard
                    ?? throw new InvalidOperationException(
                        "The desktop clipboard is not available.");
                return await clipboard.TryGetValueAsync(DataFormat.Text);
            }

            viewModel.SetJournalCommandPlatformServices(
                directory => mainWindow.Launcher.LaunchDirectoryInfoAsync(
                    directory),
                async () => await Dispatcher.UIThread.InvokeAsync(
                    () => desktop.Shutdown()),
                WriteClipboardAsync);

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
            fleetCarrierRouteWindowCoordinator = new RouteWindowCoordinator(
                viewModel.FleetCarrierRoute,
                mainWindow);
            fleetCarrierJumpCountdownCoordinator =
                new FleetCarrierJumpCountdownCoordinator(
                    viewModel.FleetCarrierRoute);
            biologyPredictionsWindowCoordinator =
                new BiologyPredictionsWindowCoordinator(
                    viewModel.BiologyPredictions,
                    mainWindow);
            biologyCodexWindowCoordinator = new BiologyCodexWindowCoordinator(
                viewModel.BiologyCodex,
                mainWindow,
                viewModel.CodexImages);
            biologyCodexBingoWindowCoordinator =
                new BiologyCodexBingoWindowCoordinator(
                    viewModel.CodexBingo,
                    mainWindow,
                    viewModel.OpenCodexBingoNearestSearchAsync);
            sphericalSearchOverlayCoordinator = new SphericalSearchOverlayCoordinator(
                viewModel.Search,
                viewModel.BoxelSearch,
                viewModel.Route,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout,
                viewModel.SystemNicknames);
            guardianOverlayCoordinator = new GuardianOverlayCoordinator(
                viewModel.Guardian,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            jumpInfoOverlayCoordinator = new JumpInfoOverlayCoordinator(
                viewModel.JumpInfo,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout,
                viewModel.SystemNicknames);
            routeBioOverlayCoordinator = new RouteBioOverlayCoordinator(
                viewModel.Route,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            fleetCarrierRouteOverlayCoordinator =
                new FleetCarrierRouteOverlayCoordinator(
                    viewModel.FleetCarrierRoute,
                    overlayPresentation.CreatePlatformService(),
                    CreateOverlayGameWindowTracker(),
                    overlayLayout);
            groundTargetOverlayCoordinator = new GroundTargetOverlayCoordinator(
                viewModel.GroundTarget,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            combatOverlayCoordinator = new CombatOverlayCoordinator(
                viewModel.Combat,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            stationInfoOverlayCoordinator = new StationInfoOverlayCoordinator(
                viewModel.StationInfo,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            humanSiteOverlayCoordinator = new HumanSiteOverlayCoordinator(
                viewModel.HumanSite,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            systemSurveyOverlayCoordinator = new SystemSurveyOverlayCoordinator(
                viewModel.SystemSurvey,
                viewModel.SurfaceSurvey,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                () => viewModel.CommanderName,
                exobiologyCatalog: viewModel.SystemSurvey.BiologyReferenceCatalog,
                overlayLayout: overlayLayout,
                fssDiagnosticDirectory: Path.Combine(
                    appDataPaths.CacheDirectory,
                    "fss-diagnostics"));
            questIndicatorOverlayCoordinator = new QuestIndicatorOverlayCoordinator(
                viewModel.QuestIndicator,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            notificationOverlayCoordinator = new NotificationOverlayCoordinator(
                viewModel.Notifications,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            pulseOverlayCoordinator = new PulseOverlayCoordinator(
                viewModel.PulseOverlay,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            streamOverlayCoordinator = new StreamOverlayCoordinator(
                viewModel.StreamOverlay,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker());
            vrOverlayCoordinator = new VrOverlayCoordinator(
                viewModel.VrOverlay,
                modeProvider: () => viewModel.CurrentVrOverlayMode);
            galaxyMapOverlayCoordinator = new GalaxyMapOverlayCoordinator(
                viewModel.GalaxyMap,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
            multiGameCommanderOverlayCoordinator =
                new MultiGameCommanderOverlayCoordinator(
                    viewModel.CommanderInstances,
                    viewModel.OverlayBehavior,
                    overlayPresentation.CreatePlatformService(),
                    GameWindowTracker.CreateCurrent(),
                    () => desktop.Windows.Any(window => window.IsActive),
                    overlayLayout);

            void SynchronizeOverlayPriority()
            {
                var liveGuardianSite =
                    guardianOverlayCoordinator?.IsLiveSiteVisible == true;
                var humanSite =
                    humanSiteOverlayCoordinator?.IsVisible == true;
                var guardianSystemSummary = guardianOverlayCoordinator
                    ?.IsSystemSummaryVisible == true;
                systemSurveyOverlayCoordinator?.SetFssObscured(
                    guardianSystemSummary);
                systemSurveyOverlayCoordinator?.SetBodyInfoObscured(
                    guardianSystemSummary);
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
                guardianOverlayCoordinator?.SetLiveStatusObscured(
                    jumpInfoOverlayCoordinator?.IsVisible == true);
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
                    overlayPresentation.CreatePlatformService(),
                    CreateOverlayGameWindowTracker(),
                    overlayLayout);
            var manualOverlaySuppressed = false;
            void ApplyOverlaySuppression()
            {
                var suppress = manualOverlaySuppressed
                    || viewModel.OverlayBehavior.ShouldSuppressForSuit
                    || viewModel.OverlayBehavior.ShouldSuppressForSession;
                jumpInfoOverlayCoordinator?.SetSuppressed(suppress);
                routeBioOverlayCoordinator?.SetSuppressed(suppress);
                fleetCarrierRouteOverlayCoordinator?.SetSuppressed(suppress);
                systemSurveyOverlayCoordinator?.SetSuppressed(suppress);
                groundTargetOverlayCoordinator?.SetSuppressed(suppress);
                combatOverlayCoordinator?.SetSuppressed(suppress);
                guardianOverlayCoordinator?.SetSuppressed(suppress);
                stationInfoOverlayCoordinator?.SetSuppressed(suppress);
                humanSiteOverlayCoordinator?.SetSuppressed(suppress);
                sphericalSearchOverlayCoordinator?.SetSuppressed(suppress);
                colonizationCommodityOverlayCoordinator?.SetSuppressed(suppress);
                questIndicatorOverlayCoordinator?.SetSuppressed(suppress);
                notificationOverlayCoordinator?.SetSuppressed(suppress);
                pulseOverlayCoordinator?.SetSuppressed(suppress);
                galaxyMapOverlayCoordinator?.SetSuppressed(suppress);
                multiGameCommanderOverlayCoordinator?.SetSuppressed(suppress);
            }

            void HandleOverlayBehaviorChanged(
                object? sender,
                System.ComponentModel.PropertyChangedEventArgs eventArgs)
            {
                if (eventArgs.PropertyName is
                    nameof(OverlayBehaviorViewModel.ShouldSuppressForSuit)
                    or nameof(OverlayBehaviorViewModel.ShouldSuppressForSession)
                    or nameof(OverlayBehaviorViewModel.HideInDominatorSuit)
                    or nameof(OverlayBehaviorViewModel.HideInMaverickSuit))
                {
                    ApplyOverlaySuppression();
                }
            }

            viewModel.OverlayBehavior.PropertyChanged +=
                HandleOverlayBehaviorChanged;
            ApplyOverlaySuppression();
            globalKeyboardHookService = new GlobalKeyboardHookService(
                inputSettings.CurrentSettings,
                capabilities.Host,
                GameWindowTracker.CreateCurrent(),
                () => mainWindow.InputContext.AreShortcutsActive
                    || ((viewModel.OverlayInteraction.IsEditing
                            || viewModel.OverlayInteraction.IsLiveInteractionEnabled)
                        && !mainWindow.IsActive
                        && desktop.Windows.Any(window => window.IsActive)));
            globalControllerInputService = new GlobalControllerInputService(
                inputSettings.CurrentSettings,
                capabilities.Host,
                GameWindowTracker.CreateCurrent(),
                () => mainWindow.InputContext.AreShortcutsActive
                    || ((viewModel.OverlayInteraction.IsEditing
                            || viewModel.OverlayInteraction.IsLiveInteractionEnabled)
                        && !mainWindow.IsActive
                        && desktop.Windows.Any(window => window.IsActive)));
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
                            handled = guardianOverlayCoordinator
                                ?.AdjustZoom(zoomIn: true) == true
                                || humanSiteOverlayCoordinator
                                ?.AdjustZoom(zoomIn: true) == true
                                || systemSurveyOverlayCoordinator
                                    ?.AdjustSurfaceZoom(zoomIn: true) == true;
                            break;

                        case GlobalInputAction.MapZoomOut:
                            handled = guardianOverlayCoordinator
                                ?.AdjustZoom(zoomIn: false) == true
                                || humanSiteOverlayCoordinator
                                ?.AdjustZoom(zoomIn: false) == true
                                || systemSurveyOverlayCoordinator
                                    ?.AdjustSurfaceZoom(zoomIn: false) == true;
                            break;

                        case GlobalInputAction.MapZoomAuto:
                            handled = guardianOverlayCoordinator
                                ?.ResetZoom() == true
                                || humanSiteOverlayCoordinator
                                ?.ResetZoom() == true
                                || systemSurveyOverlayCoordinator
                                    ?.ResetSurfaceZoom() == true;
                            break;

                        case GlobalInputAction.MapBeHuge:
                            handled = humanSiteOverlayCoordinator
                                ?.ToggleHuge() == true;
                            break;

                        case GlobalInputAction.ToggleAllVisibility:
                            manualOverlaySuppressed =
                                !manualOverlaySuppressed;
                            ApplyOverlaySuppression();
                            handled = true;
                            break;

                        case GlobalInputAction.ToggleOverlayInteraction:
                            handled = viewModel.OverlayInteraction
                                .ToggleLiveOverlayInteraction();
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

                        case GlobalInputAction.NextWindow:
                            handled = viewModel.CommanderInstances
                                .SwitchToNextGameWindow();
                            break;

                        case GlobalInputAction.QuestShow:
                            viewModel.ShowQuests();
                            mainWindow.Show();
                            mainWindow.Activate();
                            handled = true;
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

                        case GlobalInputAction.CopyNextBoxel:
                            if (viewModel.BoxelSearch.ShouldShowGalaxyMapOverlay
                                && viewModel.BoxelSearch.NextSystemForInput
                                    is not null)
                            {
                                viewModel.BoxelSearch.SetClipboardWriter(
                                    WriteClipboardAsync);
                                await viewModel.BoxelSearch.CopyNextSystemAsync();
                                handled = true;
                            }

                            break;

                        case GlobalInputAction.PasteGalMap:
                            var isGalaxyMapOpen = viewModel.SystemSurvey
                                .CurrentStatus?.GuiFocus == GuiFocus.GalaxyMap;
                            var routeNextHop = viewModel.Route
                                .ShouldShowGalaxyMapOverlay
                                    ? viewModel.Route.NextHop?.Name
                                    : null;
                            var resolvedText = GalaxyMapTextResolver.Resolve(
                                isGalaxyMapOpen,
                                routeNextHop,
                                viewModel.BoxelSearch.NextSystemForInput,
                                viewModel.BoxelSearch.ShouldPasteNextSystem,
                                clipboardText: null);
                            if (resolvedText is null && isGalaxyMapOpen)
                            {
                                resolvedText = GalaxyMapTextResolver.Resolve(
                                    true,
                                    null,
                                    null,
                                    useBoxelNextSystem: false,
                                    await ReadClipboardAsync());
                            }

                            if (resolvedText is not null)
                            {
                                handled = gameTextInputService
                                    .EnterText(resolvedText)
                                    .Succeeded;
                            }

                            break;

                        case GlobalInputAction.ToggleFirstFootfall:
                            handled = await viewModel
                                .ToggleCurrentBodyFirstFootfallAsync();
                            break;

                        case GlobalInputAction.StreamOne:
                            handled = streamOverlayCoordinator?.Toggle() == true;
                            break;

                        case GlobalInputAction.AdjustVr:
                            handled = viewModel.BeginVrAdjustment();
                            mainWindow.Show();
                            mainWindow.Activate();
                            break;

                        case GlobalInputAction.ResetVr:
                            handled = vrOverlayCoordinator
                                ?.ResetOrientation() == true;
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

                        case GlobalInputAction.ToggleImageEmbed:
                            handled = viewModel.ScreenshotProcessing
                                .ToggleBanner();
                            if (handled)
                            {
                                viewModel.Notifications.ShowBannerPreference(
                                    viewModel.ScreenshotProcessing.AddBanner);
                            }

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
                viewModel.SetJournalCommandPlatformServices(null, null, null);
                viewModel.ProfileImportCompleted -=
                    RestartAfterProfileImportAsync;
                viewModel.JournalSettings.RestartRequested -=
                    RestartAfterJournalChangeAsync;
                viewModel.CommanderPreference.RestartRequested -=
                    RestartAfterCommanderPreferenceChangeAsync;
                viewModel.OverlayBehavior.PropertyChanged -=
                    HandleOverlayBehaviorChanged;
                viewModel.FrontierProfile.AuthorizationCallbackReceived -=
                    HandleFrontierAuthorizationCallback;
                Dispatcher.UIThread.UnhandledException -= HandleUiException;
                TaskScheduler.UnobservedTaskException -=
                    HandleUnobservedTaskException;
                applicationLog.Append("Application exit");
                multiGameCommanderOverlayCoordinator?.Dispose();
                multiGameCommanderOverlayCoordinator = null;
                viewModel.Dispose();
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
                fleetCarrierRouteWindowCoordinator?.Dispose();
                fleetCarrierRouteWindowCoordinator = null;
                fleetCarrierJumpCountdownCoordinator?.Dispose();
                fleetCarrierJumpCountdownCoordinator = null;
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
                routeBioOverlayCoordinator?.Dispose();
                routeBioOverlayCoordinator = null;
                fleetCarrierRouteOverlayCoordinator?.Dispose();
                fleetCarrierRouteOverlayCoordinator = null;
                groundTargetOverlayCoordinator?.Dispose();
                groundTargetOverlayCoordinator = null;
                combatOverlayCoordinator?.Dispose();
                combatOverlayCoordinator = null;
                stationInfoOverlayCoordinator?.Dispose();
                stationInfoOverlayCoordinator = null;
                viewModel.StationInfo.Dispose();
                humanSiteOverlayCoordinator?.Dispose();
                humanSiteOverlayCoordinator = null;
                questIndicatorOverlayCoordinator?.Dispose();
                questIndicatorOverlayCoordinator = null;
                notificationOverlayCoordinator?.Dispose();
                notificationOverlayCoordinator = null;
                pulseOverlayCoordinator?.Dispose();
                pulseOverlayCoordinator = null;
                streamOverlayCoordinator?.Dispose();
                streamOverlayCoordinator = null;
                vrOverlayCoordinator?.Dispose();
                vrOverlayCoordinator = null;
                galaxyMapOverlayCoordinator?.Dispose();
                galaxyMapOverlayCoordinator = null;
                systemSurveyOverlayCoordinator?.Dispose();
                systemSurveyOverlayCoordinator = null;
                guardianOverlayCoordinator?.Dispose();
                guardianOverlayCoordinator = null;
                overlayPresentationSession?.Dispose();
                overlayPresentationSession = null;
            };
            try
            {
                var updateOutcome = ApplicationUpdateBootstrap
                    .ConsumePendingOutcomeAsync(appDataPaths)
                    .GetAwaiter()
                    .GetResult();
                if (updateOutcome is not null)
                {
                    viewModel.ReleaseUpdates.SetPreviousInstallationOutcome(
                        updateOutcome);
                    applicationLog.Append(
                        $"Update {updateOutcome.Version} outcome: "
                            + updateOutcome.Status
                            + (string.IsNullOrWhiteSpace(updateOutcome.Error)
                                ? string.Empty
                                : " - " + updateOutcome.Error));
                }

                var confirmedUpdate = ApplicationUpdateBootstrap
                    .ConfirmPendingHealthyAsync(appDataPaths)
                    .GetAwaiter()
                    .GetResult();
                if (confirmedUpdate is not null)
                {
                    viewModel.ReleaseUpdates.SetPreviousInstallationOutcome(
                        new ReleaseInstallationOutcome(
                            ReleaseInstallationOutcomeStatus.Installed,
                            confirmedUpdate.Preparation.RequestId,
                            confirmedUpdate.Preparation.Version,
                            DateTimeOffset.UtcNow,
                            confirmedUpdate.Preparation.BackupDirectory,
                            null,
                            null));
                    applicationLog.Append(
                        "Verified update replacement startup with the handoff helper.");
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or InvalidOperationException)
            {
                applicationLog.Append(
                    "Update replacement health confirmation failed: "
                    + exception.Message);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
