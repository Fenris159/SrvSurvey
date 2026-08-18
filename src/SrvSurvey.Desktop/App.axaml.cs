using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Runtime.InteropServices;
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
    private IClassicDesktopStyleApplicationLifetime? desktopLifetime;
    private ApplicationLogService? applicationLogService;
    private ApplicationInstanceManager? applicationInstanceManager;
    private readonly CancellationTokenSource releaseHistoryCleanupCancellation =
        new();
    private readonly ReleaseUpdateHistoryCleanupCoordinator releaseHistoryCleanup =
        new();
    private Task? releaseHistoryCleanupTask;
    private GlobalInputSettingsViewModel? globalInputSettings;
    private IGameTextInputService? gameTextInputService;
    private bool manualOverlaySuppressed;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            InitializeDesktopApplication(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeDesktopApplication(
        IClassicDesktopStyleApplicationLifetime desktop)
    {

        var appDataPaths = AppDataPaths.ResolveCurrent();
        var applicationLog = Program.ApplicationLog
            ?? new ApplicationLogService(appDataPaths.DataDirectory);
        MigrateLegacyUiSettings(appDataPaths, applicationLog);
        MigrateLegacyOrganicProfiles(appDataPaths, applicationLog);

        var overlayTheme = LoadOverlayTheme(appDataPaths, applicationLog);
        var overlayLayoutStore = new LegacyOverlayLayoutStore(
            appDataPaths.DataDirectory);
        var overlayLayout = LoadOverlayLayout(overlayLayoutStore, applicationLog);

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
        globalInputSettings = new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(appDataPaths.UiSettingsPath),
            capabilities);
        var inputSettings = globalInputSettings;
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
        gameTextInputService = GameTextInputService.CreateCurrent();
        var configuredJournalDirectory = StartupOptions.GetJournalDirectory(
            Program.StartupArguments);
        var commandLineFrontierId = StartupOptions.GetFrontierId(
            Program.StartupArguments);
        var commanderPreferenceStore = new CommanderPreferenceSettingsStore(
            appDataPaths.UiSettingsPath);
        var commanderPreferenceResolution = new CommanderPreferenceResolver(
                commanderPreferenceStore,
                new CommanderProfileCatalog(appDataPaths.DataDirectory))
            .ResolveAsync(commandLineFrontierId, CancellationToken.None)
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
        mainViewModel = new MainWindowViewModel(
            configuredJournalDirectory,
            new MainWindowViewModelOptions
            {
                ThemeService = themeService,
                AppDataPaths = appDataPaths,
                InputSettings = inputSettings,
                ApplicationLogService = applicationLog,
                OverlayLayoutStore = overlayLayoutStore,
                OverlayLayout = overlayLayout,
                OverlayInteraction = overlayInteraction,
                TargetFrontierId = targetFrontierId,
                CommanderPreferenceSettingsStore = commanderPreferenceStore,
                CommanderPreferenceCommandLineOverride =
                    commanderPreferenceResolution.IsCommandLineOverride,
                CommanderPreferenceInitialStatus =
                    commanderPreferenceResolution.StatusMessage,
                FirstFootfallInferenceService =
                    firstFootfallInferenceService,
                SystemBodyDataClient = new SystemBodyDataClient(),
                CanonnHumanSiteClient = canonnHumanSiteClient,
                CanonnHumanSitePublisher = canonnHumanSiteClient,
            });
        var viewModel = mainViewModel;
        desktopLifetime = desktop;
        applicationLogService = applicationLog;

        mainWindow = new MainWindow(viewModel);
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.MainWindow = mainWindow;

        viewModel.FrontierProfile.AuthorizationCallbackReceived +=
            HandleFrontierAuthorizationCallback;
        viewModel.ReferenceDataUpdates.SetRestartHandler(() =>
            RestartApplicationAsync("Published reference data refreshed"));
        viewModel.Localization.SetRestartHandler(() =>
            RestartApplicationAsync("Language preference changed"));
        ConfigureReleaseInstaller(
            viewModel,
            desktop,
            appDataPaths,
            applicationLog);

        viewModel.ProfileImportCompleted += RestartAfterProfileImportAsync;
        viewModel.JournalSettings.RestartRequested +=
            RestartAfterJournalChangeAsync;
        viewModel.CommanderPreference.RestartRequested +=
            RestartAfterCommanderPreferenceChangeAsync;
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
                GameWindowTracker.CreateCurrent(),
                () => desktop.Windows.Any(window => window.IsActive),
                overlayLayout);

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
        viewModel.OverlayBehavior.PropertyChanged +=
            HandleOverlayBehaviorChanged;
        ApplyOverlaySuppression();
        StartGlobalInputServices(
            inputSettings,
            capabilities,
            viewModel,
            desktop);
        linuxTerminationRegistration = RegisterLinuxTermination(desktop);
        desktop.Exit += async (_, _) =>
            await HandleDesktopExitAsync(viewModel, applicationLog);
        ConfirmUpdateReplacementHealth(appDataPaths, viewModel, applicationLog);
        releaseHistoryCleanupTask = CleanReleaseUpdateHistoryAsync(
            appDataPaths,
            applicationLog,
            releaseHistoryCleanupCancellation.Token);
    }

    private void ConfigureReleaseInstaller(
        MainWindowViewModel viewModel,
        IClassicDesktopStyleApplicationLifetime desktop,
        AppDataPaths appDataPaths,
        ApplicationLogService applicationLog)
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        applicationInstanceManager = new ApplicationInstanceManager(
            appDataPaths.DataDirectory,
            async () => await Dispatcher.UIThread.InvokeAsync(
                () => desktop.Shutdown()),
            message => applicationLog.Append(message));
        viewModel.ReleaseUpdates.ConfigureInstaller(
            new ReleaseInstallerConfiguration
            {
                DownloadService = new ReleasePackageDownloadService(),
                StagingService = new ReleasePackageStagingService(),
                InstallationPreparer = new ReleaseInstallationPreparer(
                    historyCleanup: releaseHistoryCleanup),
                HandoffService = new ApplicationUpdateHandoffService(),
                InstanceManager = applicationInstanceManager,
                ConfirmMultipleInstances = scan =>
                    ConfirmMultipleApplicationInstancesAsync(
                        desktop,
                        scan),
                DataDirectory = appDataPaths.DataDirectory,
                InstallationDirectory = AppContext.BaseDirectory,
                StartupArguments = Program.StartupArguments,
                Shutdown = async () => await Dispatcher.UIThread.InvokeAsync(
                    () => desktop.Shutdown()),
                AutomaticInstallationUnavailableReason =
                    string.IsNullOrWhiteSpace(appImagePath)
                        ? null
                        : "This AppImage is mounted read-only and cannot replace itself; open the selected release and install its AppImage manually.",
                IsAppImage = !string.IsNullOrWhiteSpace(appImagePath),
            });
    }

    private static async Task<bool> ConfirmMultipleApplicationInstancesAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        ApplicationInstanceScan scan)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                "The update confirmation must be displayed on the UI thread.");
        }

        if (desktop.MainWindow is not Window owner)
        {
            return false;
        }

        var dialog = new MultipleApplicationInstancesDialog(
            scan.TotalCount,
            scan.UnverifiedCount);
        return await dialog.ShowDialog<bool>(owner);
    }

    private static PosixSignalRegistration? RegisterLinuxTermination(
        IClassicDesktopStyleApplicationLifetime desktop)
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
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
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
            GameWindowTracker.CreateCurrent(),
            areShortcutsActive);
        globalControllerInputService = new GlobalControllerInputService(
            inputSettings.CurrentSettings,
            capabilities.Host,
            GameWindowTracker.CreateCurrent(),
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

    private async Task HandleDesktopExitAsync(
        MainWindowViewModel viewModel,
        ApplicationLogService applicationLog)
    {
        linuxTerminationRegistration?.Dispose();
        linuxTerminationRegistration = null;
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
        await StopReleaseUpdateHistoryCleanupAsync();
        if (applicationInstanceManager is not null)
        {
            await applicationInstanceManager.DisposeAsync();
            applicationInstanceManager = null;
        }

        applicationLog.Append("Application exit");
        await DisposeDesktopServicesAsync(viewModel);
    }

    private async Task DisposeDesktopServicesAsync(MainWindowViewModel viewModel)
    {
        multiGameCommanderOverlayCoordinator?.Dispose();
        multiGameCommanderOverlayCoordinator = null;
        await viewModel.DisposeAsync();
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
            GameWindowTracker.CreateCurrent(),
            () => viewModel.OverlayBehavior.KeepWhenGameLosesFocus
                || viewModel.OverlayInteraction.IsEditing
                || viewModel.OverlayInteraction.IsLiveInteractionEnabled);
    }

    private void HandleFrontierAuthorizationCallback(
        object? sender,
        EventArgs eventArgs)
    {
        mainWindow?.RestoreAndActivate();
    }

    private async Task RestartApplicationAsync(string reason)
    {
        new ApplicationRestartService().StartReplacement();
        applicationLogService?.Append(reason + "; replacement process started.");
        if (desktopLifetime is { } desktop)
        {
            await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown());
        }
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

    private void SynchronizeOverlayPriority()
    {
        var viewModel = mainViewModel;
        if (viewModel is null)
        {
            return;
        }

        var liveGuardianSite =
            guardianOverlayCoordinator?.IsLiveSiteVisible == true;
        var humanSite = humanSiteOverlayCoordinator?.IsVisible == true;
        var guardianSystemSummary = guardianOverlayCoordinator
            ?.IsSystemSummaryVisible == true;
        systemSurveyOverlayCoordinator?.SetFssObscured(guardianSystemSummary);
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

    private void ApplyOverlaySuppression()
    {
        var viewModel = mainViewModel;
        if (viewModel is null)
        {
            return;
        }

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

        viewModel.BoxelSearch.SetClipboardWriter(WriteClipboardAsync);
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
