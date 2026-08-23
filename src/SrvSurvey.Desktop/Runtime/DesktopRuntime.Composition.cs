using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Runtime;

internal sealed partial class DesktopRuntime
{
    // Production phases dispose these resources through failure-isolating
    // helpers. CA2213 cannot follow that delegated ownership boundary.
#pragma warning disable CA2213
    private GuardianOverlayCoordinator? guardianOverlayCoordinator;
    private PosixSignalRegistration? linuxTerminationRegistration;
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
    private MainWindowViewModel? mainViewModel;
    private MainWindow? mainWindow;
    private ApplicationLogService? applicationLogService;
    private ApplicationInstanceManager? applicationInstanceManager;
    private IDisposable? diagnosticNetworkClientOwnership;
    private DiagnosticReplayContext? diagnosticReplayContext;
    private readonly CancellationTokenSource releaseHistoryCleanupCancellation =
        new();
#pragma warning restore CA2213
    private readonly ReleaseUpdateHistoryCleanupCoordinator releaseHistoryCleanup =
        new();
    private Task? releaseHistoryCleanupTask;
    private GlobalInputSettingsViewModel? globalInputSettings;
    private IGameTextInputService? gameTextInputService;
    private readonly JournalMonitorSession journalMonitorSession = new();
    private bool manualOverlaySuppressed;

    private void InitializeDesktopApplication(
        Application application,
        IClassicDesktopStyleApplicationLifetime desktop,
        DesktopStartup startup)
    {

        var diagnosticReplay = startup.DiagnosticReplay;
        diagnosticReplayContext = diagnosticReplay;
        var externalNetworkClient = DiagnosticReplayContext.CreateNetworkClient(
            diagnosticReplay);
        diagnosticNetworkClientOwnership = externalNetworkClient;
        var appDataPaths = startup.AppDataPathsOverride
            ?? AppDataPaths.ResolveCurrent();
        var applicationLog = startup.ApplicationLog
            ?? new ApplicationLogService(appDataPaths.DataDirectory);
        applicationLogService = applicationLog;
        MigrateLegacyStateIfNeeded(
            diagnosticReplay,
            appDataPaths,
            applicationLog);

        var overlayTheme = LoadOverlayTheme(appDataPaths, applicationLog);
        var overlayLayoutStore = new LegacyOverlayLayoutStore(
            appDataPaths.DataDirectory);
        var overlayLayout = LoadOverlayLayout(overlayLayoutStore, applicationLog);

        var themeService = new RavenThemeService(
            application,
            new ThemePreferenceStore(appDataPaths.UiSettingsPath),
            overlayTheme);
        themeService.ApplyCurrent();
        themeService.OverlayThemeChanged += (_, _) =>
        {
            OverlayThemeResources.RefreshAll();
        };
        var capabilities = OverlayPlatformCapabilities.DetectCurrent();
        globalInputSettings = new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(appDataPaths.UiSettingsPath),
            capabilities);
        var inputSettings = globalInputSettings;
        var overlayPresentation = OverlayPresentationSession.CreateCurrent(
            gameWindowTracker: CreateRawGameWindowTracker(),
            registry: null,
            overlayLayout,
            () => mainViewModel is { } current
                && (current.OverlayBehavior.KeepWhenGameLosesFocus
                    || current.OverlayInteraction.IsEditing
                    || current.OverlayInteraction.IsLiveInteractionEnabled),
            diagnostic => applicationLog.Append(
                $"Overlay host '{diagnostic.PlotterName}' "
                + $"{diagnostic.Phase} -> {diagnostic.Health}: "
                + diagnostic.Status),
            CreateRawGameWindowTracker);
        overlayPresentationSession = overlayPresentation;
        applicationLog.Append(
            $"Overlay presentation: {overlayPresentation.Decision.Mode}. "
            + overlayPresentation.Decision.Reason);
        var overlayInteraction = new OverlayInteractionViewModel(
            overlayPresentation.CreatePlatformService(),
            CreateRawGameWindowTracker(),
            overlayLayoutStore,
            overlayLayout);
        using var overlayInteractionOwnership =
            new MainWindowViewModelStartupResource<OverlayInteractionViewModel>(
                overlayInteraction,
                exception => applicationLog.Append(
                    "Main window startup cleanup failed: "
                    + exception.Message));
        gameTextInputService = diagnosticReplay is null
            ? GameTextInputService.CreateCurrent()
            : null;
        startup.Checkpoint?.Invoke(
            DesktopStartupCheckpoint.OverlayInfrastructureReady);
        var configuredJournalDirectory = diagnosticReplay?.JournalDirectory
            ?? StartupOptions.GetJournalDirectory(startup.Arguments);
        var commanderPreferenceStore = new CommanderPreferenceSettingsStore(
            appDataPaths.UiSettingsPath);
        var commanderPreferenceResolution = ResolveCommanderPreference(
            diagnosticReplay,
            startup.Arguments,
            commanderPreferenceStore,
            appDataPaths.DataDirectory);
        if (commanderPreferenceResolution.StatusMessage is not null)
        {
            applicationLog.Append(
                commanderPreferenceResolution.StatusMessage);
        }

        var targetFrontierId =
            commanderPreferenceResolution.TargetFrontierId;
        var firstFootfallInferenceService = diagnosticReplay is null
            ? FirstFootfallInferenceService.CreateCurrent()
            : new UnavailableFirstFootfallInferenceService();
        var canonnHumanSiteClient = new CanonnHumanSiteClient(
            externalNetworkClient);
        using var mainViewModelStartup = new MainWindowViewModelStartup(
            configuredJournalDirectory,
            new MainWindowFoundationInputs
            {
                ThemeService = themeService,
                AppDataPaths = appDataPaths,
                InputSettings = inputSettings,
                ApplicationLogService = applicationLog,
                TargetFrontierId = targetFrontierId,
                CommanderPreferenceSettingsStore = commanderPreferenceStore,
                CommanderPreferenceCommandLineOverride =
                    commanderPreferenceResolution.IsCommandLineOverride,
                CommanderPreferenceInitialStatus =
                    commanderPreferenceResolution.StatusMessage,
                FrontierProfile = diagnosticReplay is null
                    ? null
                    : new CommanderProfileViewModel(
                        new DiagnosticReplayFrontierAccountService()),
                IsDiagnosticReplay = diagnosticReplay is not null,
                DiagnosticReplayStatus = diagnosticReplay is null
                    ? null
                    : $"Diagnostic replay: {diagnosticReplay.Commander.Name} "
                        + $"({diagnosticReplay.Commander.FrontierId}); external effects disabled.",
                ExternalNetworkClient = externalNetworkClient,
                ReplayViewportProvider = CaptureReplayViewport,
            },
            new MainWindowOverlayInputs
            {
                OverlayLayoutStore = overlayLayoutStore,
                OverlayLayout = overlayLayout,
                OverlayInteraction = overlayInteractionOwnership.Transfer(),
                ScreenshotProcessingService = diagnosticReplay is null
                    ? null
                    : new DiagnosticReplayScreenshotProcessingService(),
            },
            new MainWindowExplorationInputs
            {
                FirstFootfallInferenceService =
                    firstFootfallInferenceService,
                SystemBodyDataClient = new SystemBodyDataClient(
                    externalNetworkClient),
            },
            new MainWindowTravelInputs
            {
                GameWindowSwitcher = diagnosticReplay is null
                    ? null
                    : new DiagnosticReplayGameWindowSwitcher(),
            },
            new MainWindowOnlineInputs
            {
                CanonnHumanSiteClient = canonnHumanSiteClient,
                CanonnHumanSitePublisher = canonnHumanSiteClient,
            });
        startup.Checkpoint?.Invoke(
            DesktopStartupCheckpoint.MainViewModelDependenciesReady);
        mainViewModel = MainWindowViewModelFactory.Create(
            mainViewModelStartup);
        var viewModel = mainViewModel;
        mainWindow = new MainWindow(viewModel);
        mainWindow.Opened += HandleMainWindowOpened;
        AttachMainWindow(mainWindow);
        if (diagnosticReplay is null)
        {
            viewModel.ProfileImportPreparing +=
                StopJournalMonitorForProfileImportAsync;
            viewModel.BoxelClipboard.SetWriter(WriteClipboardAsync);
        }
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (diagnosticReplay is null)
        {
            viewModel.FrontierProfile.AuthorizationCallbackReceived +=
                HandleFrontierAuthorizationCallback;
            viewModel.ReferenceDataUpdates.SetRestartHandler(() =>
                RestartApplicationAsync("Published reference data refreshed"));
            viewModel.Localization.SetRestartHandler(() =>
                RestartApplicationAsync("Language preference changed"));
        }
        if (diagnosticReplay is null)
        {
            ConfigureReleaseInstaller(
                viewModel,
                desktop,
                appDataPaths,
                applicationLog,
                startup.Arguments);
            viewModel.ProfileImportCompleted += RestartAfterProfileImportAsync;
            viewModel.JournalSettings.RestartRequested +=
                RestartAfterJournalChangeAsync;
            viewModel.CommanderPreference.RestartRequested +=
                RestartAfterCommanderPreferenceChangeAsync;
            viewModel.SetJournalCommandPlatformServices(
                directory => mainWindow.Launcher.LaunchDirectoryInfoAsync(
                    directory),
                () => RequestShutdownOnUiThreadAsync(
                    DesktopShutdownReason.JournalCommand,
                    CancellationToken.None),
                WriteClipboardAsync);
        }

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
        Dispatcher.UIThread.UnhandledException += HandleUiException;
        TaskScheduler.UnobservedTaskException +=
            HandleUnobservedTaskException;
        startup.Checkpoint?.Invoke(
            DesktopStartupCheckpoint.MainWindowReady);
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
            viewModel.CodexImages,
            diagnosticReplay is null
                ? null
                : new CodexImageCache(
                    () => new CodexImageLocations(
                        viewModel.CodexImages.EffectiveCacheDirectory,
                        viewModel.CodexImages.EffectiveLocalFloraDirectory),
                    externalNetworkClient));
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
            new SphericalSearchOverlayCoordinatorOptions
            {
                OverlayLayout = overlayLayout,
                SystemNicknames = viewModel.SystemNicknames,
                InputSettings = viewModel.InputSettings,
            });
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
            overlayPresentation);
        combatOverlayCoordinator = new CombatOverlayCoordinator(
            viewModel.Combat,
            overlayPresentation.CreatePlatformService(),
            CreateOverlayGameWindowTracker(),
            overlayLayout);
        stationInfoOverlayCoordinator = new StationInfoOverlayCoordinator(
            viewModel.StationInfo,
            overlayPresentation);
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
            new SystemSurveyOverlayCoordinatorOptions
            {
                CommanderNameProvider = () => viewModel.CommanderName,
                ExobiologyCatalog =
                    viewModel.SystemSurvey.BiologyReferenceCatalog,
                OverlayLayout = overlayLayout,
                FssDiagnosticDirectory = Path.Combine(
                    appDataPaths.CacheDirectory,
                    "fss-diagnostics"),
            });
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
                CreateRawGameWindowTracker(),
                () => desktop.Windows.Any(window => window.IsActive),
                overlayLayout);

        viewModel.SystemSurvey.PropertyChanged +=
            HandleOverlayPriorityFactsChanged;
        SynchronizeOverlayPriorityFacts();
        colonizationCommodityOverlayCoordinator =
            new ColonizationCommodityOverlayCoordinator(
                viewModel.Colonization.CommodityOverlay,
                overlayPresentation.CreatePlatformService(),
                CreateOverlayGameWindowTracker(),
                overlayLayout);
        viewModel.OverlayBehavior.PropertyChanged +=
            HandleOverlayBehaviorChanged;
        ApplyOverlaySuppression();
        startup.Checkpoint?.Invoke(
            DesktopStartupCheckpoint.OverlayDependentsReady);
        if (diagnosticReplay is null)
        {
            StartGlobalInputServices(
                inputSettings,
                capabilities,
                viewModel,
                desktop);
        }
        linuxTerminationRegistration = RegisterLinuxTermination();
        if (diagnosticReplay is null)
        {
            ConfirmUpdateReplacementHealth(appDataPaths, viewModel, applicationLog);
            releaseHistoryCleanupTask = CleanReleaseUpdateHistoryAsync(
                appDataPaths,
                applicationLog,
                releaseHistoryCleanupCancellation.Token);
        }
        startup.Checkpoint?.Invoke(
            DesktopStartupCheckpoint.ProducersReady);
        desktop.MainWindow = mainWindow;
    }

    private void HandleMainWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (mainWindow is { } window)
        {
            window.Opened -= HandleMainWindowOpened;
        }

        _ = journalMonitorSession.Start(
            RunJournalMonitorAsync,
            exception => applicationLogService?.Append(
                "Journal monitor stopped unexpectedly: " + exception));
    }

    private async Task RunJournalMonitorAsync(
        CancellationToken cancellationToken)
    {
        if (mainViewModel is not { } viewModel)
        {
            return;
        }

        if (!viewModel.IsDiagnosticReplay)
        {
            _ = viewModel.ReleaseUpdates.CheckAsync();
            _ = viewModel.ReferenceDataUpdates.RefreshAsync();
        }
        await viewModel.RefreshAsync();
        if (!cancellationToken.IsCancellationRequested)
        {
            if (!viewModel.IsDiagnosticReplay)
            {
                _ = viewModel.DesktopBehavior.RequestStartupFocus();
            }
            await viewModel.MonitorAsync(cancellationToken: cancellationToken);
        }
    }

    private Task StopJournalMonitorForProfileImportAsync()
    {
        return journalMonitorSession.StopAsync();
    }

    private void ConfigureReleaseInstaller(
        MainWindowViewModel viewModel,
        IClassicDesktopStyleApplicationLifetime desktop,
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog,
        string[] startupArguments)
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        applicationInstanceManager = new ApplicationInstanceManager(
            appDataPaths.DataDirectory,
            () => RequestShutdownOnUiThreadAsync(
                DesktopShutdownReason.RemoteInstanceRequest,
                CancellationToken.None),
            message => applicationLog.Append(message));
        var installationWorkflow = new ReleaseInstallationWorkflow(
            new ReleaseInstallationWorkflowAdapters(
                new ReleasePackageDownloadService(),
                new ReleasePackageStagingService(),
                new ReleaseInstallationPreparer(
                    historyCleanup: releaseHistoryCleanup),
                new ApplicationUpdateHandoffService(),
                applicationInstanceManager,
                (scan, _, cancellationToken) =>
                    ConfirmMultipleApplicationInstancesAsync(
                        desktop,
                        scan,
                        cancellationToken)),
            new ReleaseInstallationWorkflowContext(
                appDataPaths.DataDirectory,
                AppContext.BaseDirectory,
                startupArguments,
                cancellationToken => RequestShutdownOnUiThreadAsync(
                    DesktopShutdownReason.UpdateHandoff,
                    cancellationToken),
                !string.IsNullOrWhiteSpace(appImagePath),
                message => applicationLog.Append(message)));
        viewModel.ReleaseUpdates.ConfigureInstallationWorkflow(
            installationWorkflow);
    }

    private static async Task<bool> ConfirmMultipleApplicationInstancesAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        ApplicationInstanceScan scan,
        CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await await Dispatcher.UIThread.InvokeAsync(
                () => ConfirmMultipleApplicationInstancesAsync(
                    desktop,
                    scan,
                    cancellationToken),
                DispatcherPriority.Normal,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (desktop.MainWindow is not Window owner)
        {
            return false;
        }

        var dialog = new MultipleApplicationInstancesDialog(
            scan.TotalCount,
            scan.UnverifiedCount);
        return await dialog.ShowDialog<bool>(owner);
    }

    private PosixSignalRegistration? RegisterLinuxTermination()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        return PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                PostShutdownOnUiThread(
                    DesktopShutdownReason.LinuxTermination);
            });
    }

    internal void PostShutdownOnUiThread(DesktopShutdownReason reason)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _ = RequestShutdownAsync(reason);
        });
    }

    private static void MigrateLegacyUiSettings(
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog)
    {
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
            return;
        }

        if (settingsMigration.Error is not null)
        {
            applicationLog.Append(
                "Legacy UI settings migration was skipped: "
                + settingsMigration.Error);
        }
    }

    private static void MigrateLegacyOverlayLayout(
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog)
    {
        var migration = LegacyOverlayLayoutImportMigrator.MigrateIfNeeded(
            appDataPaths);
        if (migration.Migrated)
        {
            applicationLog.Append(
                $"Converted {migration.NormalizedPlacementCount:N0} imported "
                + "absolute overlay placement(s) to game-window-relative anchors."
                + (migration.BackupPath is null
                    ? string.Empty
                    : " Previous layout backup: " + migration.BackupPath));
            return;
        }

        if (migration.Error is not null)
        {
            applicationLog.Append(
                "Imported overlay placement conversion was skipped: "
                + migration.Error);
        }
    }

    private static void MigrateLegacyOrganicProfiles(
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog)
    {
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
    }

    private static LegacyOverlayTheme LoadOverlayTheme(
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog)
    {
        var overlayTheme = new LegacyOverlayThemeStore(
            Path.Combine(appDataPaths.DataDirectory, "theme.json"))
            .Load();
        if (overlayTheme.Error is not null)
        {
            applicationLog.Append(overlayTheme.Error);
        }

        return overlayTheme;
    }

    private static LegacyOverlayLayout LoadOverlayLayout(
        LegacyOverlayLayoutStore overlayLayoutStore,
        ApplicationLogService applicationLog)
    {
        var overlayLayout = overlayLayoutStore.Load();
        if (overlayLayout.Error is not null)
        {
            applicationLog.Append(overlayLayout.Error);
        }

        return overlayLayout;
    }

    private void StartGlobalInputServices(
        GlobalInputSettingsViewModel inputSettings,
        OverlayPlatformCapabilities capabilities,
        MainWindowViewModel viewModel,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        Func<bool> areShortcutsActive = () =>
            AreGlobalInputShortcutsActive(viewModel, desktop);
        globalKeyboardHookService = new GlobalKeyboardHookService(
            inputSettings.CurrentSettings,
            capabilities.Host,
            CreateRawGameWindowTracker(),
            areShortcutsActive);
        globalControllerInputService = new GlobalControllerInputService(
            inputSettings.CurrentSettings,
            capabilities.Host,
            CreateRawGameWindowTracker(),
            areShortcutsActive);
        globalKeyboardHookService.StatusChanged += (_, _) =>
            PostKeyboardRuntimeStatus(inputSettings);
        globalControllerInputService.StatusChanged += (_, _) =>
            PostControllerRuntimeStatus(inputSettings);

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
    }

    private bool AreGlobalInputShortcutsActive(
        MainWindowViewModel viewModel,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        return mainWindow!.InputContext.AreShortcutsActive
            || ((viewModel.OverlayInteraction.IsEditing
                    || viewModel.OverlayInteraction.IsLiveInteractionEnabled)
                && !mainWindow.IsActive
                && desktop.Windows.Any(window => window.IsActive));
    }

    private void PostKeyboardRuntimeStatus(
        GlobalInputSettingsViewModel inputSettings)
    {
        var status = globalKeyboardHookService?.Status;
        if (status is not null)
        {
            Dispatcher.UIThread.Post(
                () => inputSettings.UpdateRuntimeStatus(status));
        }
    }

    private void PostControllerRuntimeStatus(
        GlobalInputSettingsViewModel inputSettings)
    {
        var status = globalControllerInputService?.Status;
        if (status is not null)
        {
            Dispatcher.UIThread.Post(
                () => inputSettings.UpdateControllerRuntimeStatus(status));
        }
    }

    private void QuiesceDesktopRuntime(DesktopShutdownReason reason)
    {
        DisposeResource(ref linuxTerminationRegistration);
        if (mainWindow is { } window)
        {
            window.Opened -= HandleMainWindowOpened;
            TryCleanup(window.RememberCurrentPositionForShutdown);
            if (reason is DesktopShutdownReason.Restart
                or DesktopShutdownReason.UpdateHandoff)
            {
                manualOverlaySuppressed = true;
                TryCleanup(ApplyOverlaySuppression);
            }
        }

        if (mainViewModel is { } viewModel)
        {
            viewModel.BoxelClipboard.SetWriter(null);
            viewModel.SetJournalCommandPlatformServices(null, null, null);
            viewModel.ProfileImportPreparing -=
                StopJournalMonitorForProfileImportAsync;
            viewModel.ProfileImportCompleted -=
                RestartAfterProfileImportAsync;
            viewModel.JournalSettings.RestartRequested -=
                RestartAfterJournalChangeAsync;
            viewModel.CommanderPreference.RestartRequested -=
                RestartAfterCommanderPreferenceChangeAsync;
            viewModel.OverlayBehavior.PropertyChanged -=
                HandleOverlayBehaviorChanged;
            viewModel.SystemSurvey.PropertyChanged -=
                HandleOverlayPriorityFactsChanged;
            viewModel.FrontierProfile.AuthorizationCallbackReceived -=
                HandleFrontierAuthorizationCallback;
            viewModel.ReferenceDataUpdates.SetRestartHandler(null);
            viewModel.Localization.SetRestartHandler(null);
        }

        Dispatcher.UIThread.UnhandledException -= HandleUiException;
        TaskScheduler.UnobservedTaskException -=
            HandleUnobservedTaskException;
    }

    private async Task StopDesktopProducersAsync()
    {
        await TryCleanupAsync(journalMonitorSession.StopAsync);
        await TryCleanupAsync(StopReleaseUpdateHistoryCleanupAsync);

        var instanceManager = applicationInstanceManager;
        applicationInstanceManager = null;
        if (instanceManager is not null)
        {
            await TryCleanupAsync(
                () => instanceManager.DisposeAsync().AsTask());
        }

        var controllerInput = globalControllerInputService;
        globalControllerInputService = null;
        if (controllerInput is not null)
        {
            await TryCleanupAsync(
                () => controllerInput.DisposeAsync().AsTask());
        }

        var keyboardInput = globalKeyboardHookService;
        globalKeyboardHookService = null;
        if (keyboardInput is not null)
        {
            await TryCleanupAsync(
                () => keyboardInput.DisposeAsync().AsTask());
        }
    }

    private Task DisposeDesktopDependentsAsync()
    {
        if (mainWindow is { } window)
        {
            TryCleanup(window.ReleaseRuntimeDependents);
        }

        DisposeResource(ref multiGameCommanderOverlayCoordinator);
        DisposeResource(ref errorReportWindowCoordinator);
        DisposeResource(ref colonizationCommodityOverlayCoordinator);
        DisposeResource(ref systemNotesWindowCoordinator);
        DisposeResource(ref journeyWindowCoordinator);
        DisposeResource(ref routeWindowCoordinator);
        DisposeResource(ref fleetCarrierRouteWindowCoordinator);
        DisposeResource(ref fleetCarrierJumpCountdownCoordinator);
        DisposeResource(ref biologyPredictionsWindowCoordinator);
        DisposeResource(ref biologyCodexWindowCoordinator);
        DisposeResource(ref biologyCodexBingoWindowCoordinator);
        DisposeResource(ref sphericalSearchOverlayCoordinator);
        DisposeResource(ref jumpInfoOverlayCoordinator);
        DisposeResource(ref routeBioOverlayCoordinator);
        DisposeResource(ref fleetCarrierRouteOverlayCoordinator);
        DisposeResource(ref groundTargetOverlayCoordinator);
        DisposeResource(ref combatOverlayCoordinator);
        DisposeResource(ref stationInfoOverlayCoordinator);
        DisposeResource(ref humanSiteOverlayCoordinator);
        DisposeResource(ref questIndicatorOverlayCoordinator);
        DisposeResource(ref notificationOverlayCoordinator);
        DisposeResource(ref pulseOverlayCoordinator);
        DisposeResource(ref streamOverlayCoordinator);
        DisposeResource(ref vrOverlayCoordinator);
        DisposeResource(ref galaxyMapOverlayCoordinator);
        DisposeResource(ref systemSurveyOverlayCoordinator);
        DisposeResource(ref guardianOverlayCoordinator);
        return Task.CompletedTask;
    }

    private async Task DisposeMainViewModelAsync()
    {
        var viewModel = mainViewModel;
        mainViewModel = null;
        if (viewModel is not null)
        {
            await TryCleanupAsync(() => viewModel.DisposeAsync().AsTask());
        }

    }

    private Task DisposeDesktopInfrastructureAsync()
    {
        DisposeResource(ref overlayPresentationSession);
        DisposeResource(ref diagnosticNetworkClientOwnership);
        diagnosticReplayContext = null;
        TryCleanup(ResetOverlayRegistryState);
        TryCleanup(releaseHistoryCleanupCancellation.Dispose);
        applicationLogService?.Append("Application exit");
        return Task.CompletedTask;
    }

    private static void ResetOverlayRegistryState()
    {
        OverlayWindowRegistry.Shared.SetGlobalSuppression(
            manualSuppressed: false,
            suitSuppressed: false,
            sessionSuppressed: false);
        OverlayWindowRegistry.Shared.SetPriorityFacts(default);
    }

    private static CommanderPreferenceResolution ResolveCommanderPreference(
        DiagnosticReplayContext? diagnosticReplay,
        IReadOnlyList<string> startupArguments,
        CommanderPreferenceSettingsStore preferenceStore,
        string dataDirectory)
    {
        if (diagnosticReplay is not null)
        {
            return new CommanderPreferenceResolution(
                TargetFrontierId: null,
                IsCommandLineOverride: false,
                StatusMessage:
                    "Diagnostic replay is waiting for commander identity from the imported journal.");
        }

        var commandLineFrontierId = StartupOptions.GetFrontierId(
            startupArguments);
        return new CommanderPreferenceResolver(
                preferenceStore,
                new CommanderProfileCatalog(dataDirectory))
            .ResolveAsync(commandLineFrontierId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void MigrateLegacyStateIfNeeded(
        DiagnosticReplayContext? diagnosticReplay,
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog)
    {
        if (diagnosticReplay is not null)
        {
            return;
        }

        MigrateLegacyOverlayLayout(appDataPaths, applicationLog);
        MigrateLegacyUiSettings(appDataPaths, applicationLog);
        MigrateLegacyOrganicProfiles(appDataPaths, applicationLog);
    }

    private void DisposeResource<T>(ref T? resource)
        where T : class, IDisposable
    {
        var current = resource;
        resource = null;
        if (current is not null)
        {
            TryCleanup(current.Dispose);
        }
    }

    private void TryCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            TryAppendShutdownFailure(exception);
        }
    }

    private async Task TryCleanupAsync(Func<Task> cleanup)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            TryAppendShutdownFailure(exception);
        }
    }

    private void TryAppendShutdownFailure(Exception exception)
    {
        try
        {
            applicationLogService?.Append(
                "Desktop runtime shutdown failed: " + exception);
        }
        catch
        {
            // Shutdown must continue when logging is unavailable.
        }
    }

    private void TryAppendStartupFailure(Exception exception)
    {
        try
        {
            applicationLogService?.Append(
                "Fatal desktop runtime startup error: " + exception);
        }
        catch
        {
            // Startup rollback still runs when logging is unavailable.
        }
    }

    private static void ConfirmUpdateReplacementHealth(
        AppDataPaths appDataPaths,
        MainWindowViewModel viewModel,
        ApplicationLogService applicationLog)
    {
        try
        {
            ApplyPendingUpdateOutcome(appDataPaths, viewModel, applicationLog);
            ConfirmPendingHealthyUpdate(appDataPaths, viewModel, applicationLog);
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

    private async Task CleanReleaseUpdateHistoryAsync(
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog,
        CancellationToken cancellationToken)
    {
        try
        {
            var cacheResult = await releaseHistoryCleanup.CleanPackageCacheAsync(
                    new ReleasePackageCacheCleaner(),
                    appDataPaths.DataDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            if (cacheResult.DeletedVersions > 0)
            {
                applicationLog.Append(
                    $"Removed {cacheResult.DeletedVersions:N0} stale update cache versions "
                    + $"({cacheResult.DeletedPackageVersions:N0} package, "
                    + $"{cacheResult.DeletedStagedVersions:N0} staged).");
            }

            foreach (var failure in cacheResult.Failures)
            {
                applicationLog.Append(
                    "Update cache cleanup retained an inaccessible directory: "
                    + failure);
            }

            var planResult = await releaseHistoryCleanup.CleanPlansAsync(
                    new ReleaseInstallationPlanCleaner(),
                    appDataPaths.DataDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            if (planResult.DeletedPlans > 0)
            {
                applicationLog.Append(
                    $"Removed {planResult.DeletedPlans:N0} stale installation plans.");
            }

            foreach (var failure in planResult.Failures)
            {
                applicationLog.Append(
                    "Installation-plan cleanup retained an inaccessible directory: "
                    + failure);
            }

            if (!File.Exists(Path.Combine(
                AppContext.BaseDirectory,
                "release-package.json")))
            {
                return;
            }

            var installationResult = await releaseHistoryCleanup
                .CleanInstallationAsync(
                    new ReleaseInstallationHistoryCleaner(),
                    AppContext.BaseDirectory,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (installationResult.DeletedDirectories > 0)
            {
                applicationLog.Append(
                    $"Removed {installationResult.DeletedDirectories:N0} stale update directories "
                    + $"({installationResult.DeletedBackupDirectories:N0} backup, "
                    + $"{installationResult.DeletedUpdateDirectories:N0} candidate, "
                    + $"{installationResult.DeletedFailedDirectories:N0} failed).");
            }

            foreach (var failure in installationResult.Failures)
            {
                applicationLog.Append(
                    "Update history cleanup retained an inaccessible directory: "
                    + failure);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown cancels background history cleanup.
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            applicationLog.Append(
                "Update history cleanup could not run: " + exception.Message);
        }
    }

    private async Task StopReleaseUpdateHistoryCleanupAsync()
    {
        var cleanupTask = releaseHistoryCleanupTask;
        if (cleanupTask is null)
        {
            return;
        }

        releaseHistoryCleanupTask = null;
        await releaseHistoryCleanupCancellation.CancelAsync();
        try
        {
            await cleanupTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The cancellation request stops any remaining background cleanup.
        }
        finally
        {
            releaseHistoryCleanupCancellation.Dispose();
        }
    }

    private static void ApplyPendingUpdateOutcome(
        AppDataPaths appDataPaths,
        MainWindowViewModel viewModel,
        ApplicationLogService applicationLog)
    {
        var updateOutcome = ApplicationUpdateBootstrap
            .ConsumePendingOutcomeAsync(appDataPaths)
            .GetAwaiter()
            .GetResult();
        if (updateOutcome is null)
        {
            return;
        }

        viewModel.ReleaseUpdates.SetPreviousInstallationOutcome(updateOutcome);
        applicationLog.Append(
            $"Update {updateOutcome.Version} outcome: "
                + updateOutcome.Status
                + (string.IsNullOrWhiteSpace(updateOutcome.Error)
                    ? string.Empty
                    : " - " + updateOutcome.Error));
    }

    private static void ConfirmPendingHealthyUpdate(
        AppDataPaths appDataPaths,
        MainWindowViewModel viewModel,
        ApplicationLogService applicationLog)
    {
        var confirmedUpdate = ApplicationUpdateBootstrap
            .ConfirmPendingHealthyAsync(appDataPaths)
            .GetAwaiter()
            .GetResult();
        if (confirmedUpdate is null)
        {
            return;
        }

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

    private OverlayGameWindowTracker CreateOverlayGameWindowTracker()
    {
        var viewModel = mainViewModel
            ?? throw new InvalidOperationException("Main view model is not ready.");
        return new OverlayGameWindowTracker(
            CreateRawGameWindowTracker(),
            () => viewModel.OverlayBehavior.KeepWhenGameLosesFocus
                || viewModel.OverlayInteraction.IsEditing
                || viewModel.OverlayInteraction.IsLiveInteractionEnabled);
    }

    private IGameWindowTracker CreateRawGameWindowTracker()
    {
        return diagnosticReplayContext?.CreateGameWindowTracker()
            ?? GameWindowTracker.CreateCurrent();
    }

    private PixelRect? CaptureReplayViewport()
    {
        using var tracker = CreateRawGameWindowTracker();
        var snapshot = tracker.GetSnapshot();
        return snapshot.IsAvailable ? snapshot.ClientBounds : null;
    }

    private void HandleFrontierAuthorizationCallback(
        object? sender,
        EventArgs eventArgs)
    {
        mainWindow?.RestoreAndActivate();
    }

    private async Task RestartApplicationAsync(string reason)
    {
        await RestartAsync(() =>
        {
            new ApplicationRestartService().StartReplacement();
            applicationLogService?.Append(
                reason + "; replacement process started.");
        });
    }

    private async Task RequestShutdownOnUiThreadAsync(
        DesktopShutdownReason reason,
        CancellationToken cancellationToken = default)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await RequestShutdownAsync(reason);
            return;
        }

        var dispatchedShutdownTask = await Dispatcher.UIThread.InvokeAsync(
            () => RequestShutdownAsync(reason),
            DispatcherPriority.Normal,
            cancellationToken);
        await dispatchedShutdownTask;
    }

    private Task RestartAfterProfileImportAsync()
    {
        return RestartApplicationAsync("Profile import verified");
    }

    private Task RestartAfterJournalChangeAsync()
    {
        return RestartApplicationAsync("Journal folder changed");
    }

    private Task RestartAfterCommanderPreferenceChangeAsync()
    {
        return RestartApplicationAsync("Commander preference changed");
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = mainWindow?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }

    private async Task<string?> ReadClipboardAsync()
    {
        var clipboard = mainWindow?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        return await clipboard.TryGetValueAsync(DataFormat.Text);
    }

    private void HandleUiException(
        object? sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        errorReportWindowCoordinator?.Show(eventArgs.Exception);
        eventArgs.Handled = true;
    }

    private void HandleUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        errorReportWindowCoordinator?.Show(eventArgs.Exception);
        eventArgs.SetObserved();
    }

    private void SynchronizeOverlayPriorityFacts()
    {
        var viewModel = mainViewModel;
        if (viewModel is null)
        {
            return;
        }

        var facts = OverlayPriorityFacts.None;
        if (viewModel.SystemSurvey.IsFssInfoForced)
        {
            facts |= OverlayPriorityFacts.FssInfoForced;
        }

        if (viewModel.SystemSurvey.IsBodyInfoForced)
        {
            facts |= OverlayPriorityFacts.BodyInfoForced;
        }

        OverlayWindowRegistry.Shared.SetPriorityFacts(facts);
    }

    private void HandleOverlayPriorityFactsChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemSurveyViewModel.IsFssInfoForced)
            or nameof(SystemSurveyViewModel.IsBodyInfoForced))
        {
            SynchronizeOverlayPriorityFacts();
        }
    }

    private void ApplyOverlaySuppression()
    {
        var viewModel = mainViewModel;
        if (viewModel is null)
        {
            return;
        }

        var suppressForSuit = viewModel.OverlayBehavior.ShouldSuppressForSuit;
        var suppressForSession =
            viewModel.OverlayBehavior.ShouldSuppressForSession;
        OverlayWindowRegistry.Shared.SetGlobalSuppression(
            manualOverlaySuppressed,
            suppressForSuit,
            suppressForSession);
        var suppress = manualOverlaySuppressed
            || suppressForSuit
            || suppressForSession;
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

    private void HandleOverlayBehaviorChanged(
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

    private void HandleAction(GlobalInputActionTriggeredEventArgs eventArgs)
    {
        if (ShortcutCaptureSession.IsActive)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            var viewModel = mainViewModel;
            var window = mainWindow;
            var inputSettings = globalInputSettings;
            if (viewModel is null || window is null || inputSettings is null)
            {
                return;
            }

            var handled = await ExecuteGlobalInputActionAsync(eventArgs.Action);
            inputSettings.ReportAction(eventArgs.Action, handled);
        });
    }

    private async Task<bool> ExecuteGlobalInputActionAsync(GlobalInputAction action)
    {
        var viewModel = mainViewModel
            ?? throw new InvalidOperationException("Main view model is not ready.");
        if (mainWindow is null)
        {
            throw new InvalidOperationException("Main window is not ready.");
        }

        if (GlobalInputActionCatalog.TryGetOverlayPlotterName(
                action,
                out var plotterName))
        {
            return viewModel.OverlayPanelVisibility.Toggle(plotterName);
        }

        return action switch
        {
            GlobalInputAction.MapZoomIn =>
                guardianOverlayCoordinator?.AdjustZoom(zoomIn: true) == true
                || humanSiteOverlayCoordinator?.AdjustZoom(zoomIn: true) == true
                || systemSurveyOverlayCoordinator?.AdjustSurfaceZoom(zoomIn: true)
                    == true,
            GlobalInputAction.MapZoomOut =>
                guardianOverlayCoordinator?.AdjustZoom(zoomIn: false) == true
                || humanSiteOverlayCoordinator?.AdjustZoom(zoomIn: false) == true
                || systemSurveyOverlayCoordinator?.AdjustSurfaceZoom(zoomIn: false)
                    == true,
            GlobalInputAction.MapZoomAuto =>
                guardianOverlayCoordinator?.ResetZoom() == true
                || humanSiteOverlayCoordinator?.ResetZoom() == true
                || systemSurveyOverlayCoordinator?.ResetSurfaceZoom() == true,
            GlobalInputAction.MapBeHuge =>
                humanSiteOverlayCoordinator?.ToggleHuge() == true,
            GlobalInputAction.ToggleAllVisibility => ToggleAllOverlayVisibility(),
            GlobalInputAction.ToggleOverlayInteraction => ToggleOverlayInteraction(),
            GlobalInputAction.ShowJumpInfo =>
                viewModel.JumpInfo.ToggleForcedVisibility(),
            GlobalInputAction.ShowFssInfo =>
                viewModel.SystemSurvey.ToggleFssInfoVisibility(),
            GlobalInputAction.ShowBodyInfo =>
                viewModel.SystemSurvey.ToggleBodyInfoVisibility(),
            GlobalInputAction.ShowStationInfo =>
                viewModel.StationInfo.ToggleForcedVisibility(),
            GlobalInputAction.NextWindow =>
                viewModel.CommanderInstances.SwitchToNextGameWindow(),
            GlobalInputAction.QuestShow => ShowQuestsWindow(),
            GlobalInputAction.ShowColonyShopping => ToggleColonyShopping(),
            GlobalInputAction.ShowSystemNotes =>
                systemNotesWindowCoordinator is not null
                && await systemNotesWindowCoordinator.ShowOrActivateAsync(),
            GlobalInputAction.CopyNextBoxel => await CopyNextBoxelAsync(),
            GlobalInputAction.PasteGalMap => await PasteGalaxyMapAsync(),
            GlobalInputAction.ToggleFirstFootfall =>
                await viewModel.ToggleCurrentBodyFirstFootfallAsync(),
            GlobalInputAction.StreamOne => streamOverlayCoordinator?.Toggle() == true,
            GlobalInputAction.AdjustVr => BeginVrAdjustment(),
            GlobalInputAction.ResetVr =>
                vrOverlayCoordinator?.ResetOrientation() == true,
            GlobalInputAction.Track1 => await ToggleQuickTrackerAsync(1),
            GlobalInputAction.Track2 => await ToggleQuickTrackerAsync(2),
            GlobalInputAction.Track3 => await ToggleQuickTrackerAsync(3),
            GlobalInputAction.Track4 => await ToggleQuickTrackerAsync(4),
            GlobalInputAction.Track5 => await ToggleQuickTrackerAsync(5),
            GlobalInputAction.Track6 => await ToggleQuickTrackerAsync(6),
            GlobalInputAction.Track7 => await ToggleQuickTrackerAsync(7),
            GlobalInputAction.Track8 => await ToggleQuickTrackerAsync(8),
            GlobalInputAction.RefreshColonyData => RefreshColonyData(),
            GlobalInputAction.CollapseColonyData => CollapseColonyData(),
            GlobalInputAction.ToggleImageEmbed => ToggleImageEmbed(),
            _ => false,
        };
    }

    private bool ToggleAllOverlayVisibility()
    {
        manualOverlaySuppressed = !manualOverlaySuppressed;
        ApplyOverlaySuppression();
        return true;
    }

    private bool ShowQuestsWindow()
    {
        var viewModel = mainViewModel;
        var window = mainWindow;
        if (viewModel is null || window is null)
        {
            return false;
        }

        viewModel.ShowQuests();
        window.Show();
        window.Activate();
        return true;
    }

    private bool ToggleColonyShopping()
    {
        colonizationCommodityOverlayCoordinator?.ToggleVisibility();
        return true;
    }

    private async Task<bool> CopyNextBoxelAsync()
    {
        var viewModel = mainViewModel;
        if (viewModel is null
            || !viewModel.BoxelSearch.ShouldShowGalaxyMapOverlay
            || viewModel.BoxelSearch.NextSystemForInput is null)
        {
            return false;
        }

        await viewModel.BoxelSearch.CopyNextSystemAsync();
        return true;
    }

    private async Task<bool> PasteGalaxyMapAsync()
    {
        var viewModel = mainViewModel;
        if (viewModel is null || gameTextInputService is null)
        {
            return false;
        }

        var isGalaxyMapOpen = viewModel.SystemSurvey.CurrentStatus?.GuiFocus
            == GuiFocus.GalaxyMap;
        var routeNextHop = viewModel.Route.ShouldShowGalaxyMapOverlay
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

        return resolvedText is not null
            && gameTextInputService.EnterText(resolvedText).Succeeded;
    }

    private bool BeginVrAdjustment()
    {
        var viewModel = mainViewModel;
        var window = mainWindow;
        if (viewModel is null || window is null)
        {
            return false;
        }

        var handled = viewModel.BeginVrAdjustment();
        window.Show();
        window.Activate();
        return handled;
    }

    private Task<bool> ToggleQuickTrackerAsync(int trackerNumber)
    {
        var viewModel = mainViewModel;
        if (viewModel is null)
        {
            return Task.FromResult(false);
        }

        return viewModel.SurfaceSurvey.ToggleQuickTrackerAsync(
            trackerNumber,
            CancellationToken.None);
    }

    private bool RefreshColonyData()
    {
        var viewModel = mainViewModel;
        if (viewModel is null || !viewModel.Colonization.IsEnabled)
        {
            return false;
        }

        _ = viewModel.Colonization.RefreshAsync();
        return true;
    }

    private bool CollapseColonyData()
    {
        mainViewModel?.Colonization.CommodityOverlay.ToggleSatisfiedGroups();
        return true;
    }

    private bool ToggleImageEmbed()
    {
        var viewModel = mainViewModel;
        if (viewModel is null)
        {
            return false;
        }

        var handled = viewModel.ScreenshotProcessing.ToggleBanner();
        if (handled)
        {
            viewModel.Notifications.ShowBannerPreference(
                viewModel.ScreenshotProcessing.AddBanner);
        }

        return handled;
    }

    private bool ToggleOverlayInteraction()
    {
        var viewModel = mainViewModel;
        if (viewModel is null
            || !viewModel.OverlayInteraction.ToggleLiveOverlayInteraction())
        {
            return false;
        }

        viewModel.Notifications.ShowOverlayInteraction(
            viewModel.OverlayInteraction.IsLiveInteractionEnabled);
        return true;
    }

}
