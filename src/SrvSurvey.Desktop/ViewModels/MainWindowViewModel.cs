using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "—";

    private readonly JournalFolderResolution folderResolution;
    private readonly JournalDirectoryMonitor? journalMonitor;
    private readonly JournalSessionState journalState = new();
    private readonly ExplorationState explorationState = new();
    private readonly ExobiologyState exobiologyState;
    private readonly CommanderProfileStore commanderProfileStore;
    private readonly CommanderCodexJournalTracker commanderCodexJournalTracker;
    private readonly RavenThemeService? themeService;
    private readonly LegacyProfileImporter profileImporter;
    private readonly AsyncCommand importLegacyProfileCommand;
    private readonly AsyncCommand resetExplorationCommand;
    private readonly AsyncCommand cancelResetExplorationCommand;
    private readonly AsyncCommand resetExobiologyCommand;
    private readonly AsyncCommand cancelResetExobiologyCommand;
    private bool isBusy;
    private bool isImportingProfile;
    private string statusMessage;
    private string commanderName = Unavailable;
    private string frontierId = Unavailable;
    private string gameDescription = Unavailable;
    private string gameMode = Unavailable;
    private string systemDescription = Unavailable;
    private string bodyName = Unavailable;
    private string sessionState = "Waiting for journal";
    private string lastUpdated = string.Empty;
    private string themeStatusMessage = string.Empty;
    private string vehicleState = Unavailable;
    private string surfacePosition = Unavailable;
    private string headingAndAltitude = Unavailable;
    private string gameUiFocus = Unavailable;
    private string estimatedExplorationValue = "0 CR";
    private string explorationJumps = "0";
    private string explorationDistance = "0.0 ly";
    private string explorationBodies = "Scanned: 0, DSS: 0, Landed: 0";
    private string explorationStatusMessage = "Waiting for commander profile.";
    private bool isResetExplorationPending;
    private string unclaimedBioRewards = "0 CR";
    private string unclaimedBioScans = "0 samples";
    private string organicScanProgress = "Ready for sample 1 of 3";
    private string activeOrganicSpecies = Unavailable;
    private string organicSampleRange = Unavailable;
    private string bioFirstFootfall = "Unknown";
    private string exobiologyStatusMessage = "Waiting for commander profile.";
    private string commanderCodexStatusMessage =
        "Waiting for Commander Codex journal entries.";
    private bool isResetExobiologyPending;
    private string? activeProfileFrontierId;
    private string? activeProfileCommanderName;
    private bool activeProfileIsOdyssey = true;
    private NavigationItemViewModel selectedNavigation;
    private ThemeOptionViewModel selectedTheme;
    private LegacyProfileOptionViewModel? selectedLegacyProfile;
    private string profileStatusMessage;

    public MainWindowViewModel(
        string? configuredJournalDirectory,
        RavenThemeService? themeService = null,
        AppDataPaths? appDataPaths = null,
        LegacyProfileImporter? profileImporter = null,
        ExobiologyReferenceCatalog? exobiologyCatalog = null,
        IStarSystemResolver? starSystemResolver = null,
        IBoxelSystemResolver? boxelSystemResolver = null,
        GlobalInputSettingsViewModel? inputSettings = null,
        ColonizationViewModel? colonization = null,
        INearestSystemsClient? nearestSystemsClient = null,
        ISystemSummaryClient? systemSummaryClient = null,
        JumpInfoSettingsStore? jumpInfoSettingsStore = null,
        SystemSurveySettingsStore? systemSurveySettingsStore = null,
        BiologyPredictionsSettingsStore? biologyPredictionsSettingsStore = null)
    {
        this.themeService = themeService;
        this.profileImporter = profileImporter ?? new LegacyProfileImporter();
        AppDataPaths = appDataPaths ?? AppDataPaths.ResolveCurrent();
        folderResolution = JournalFolderLocator.ResolveCurrent(
            configuredJournalDirectory);
        commanderProfileStore = new CommanderProfileStore(
            AppDataPaths.DataDirectory);
        commanderCodexJournalTracker = new CommanderCodexJournalTracker(
            new CommanderCodexStore(AppDataPaths.DataDirectory));
        InputSettings = inputSettings ?? new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(AppDataPaths.UiSettingsPath),
            OverlayPlatformCapabilities.DetectCurrent());
        Colonization = colonization ?? new ColonizationViewModel(
            new ColonizationSettingsStore(AppDataPaths.UiSettingsPath),
            commanderProfileStore: commanderProfileStore);
        var sharedSystemResolver = starSystemResolver
            ?? new SpanshStarSystemResolver();
        var sharedExobiologyCatalog = exobiologyCatalog
            ?? ExobiologyReferenceCatalog.LoadEmbedded();
        var systemNoteStore = new SystemNoteStore(AppDataPaths.DataDirectory);
        var systemNotesSettingsStore = new SystemNotesSettingsStore(
            AppDataPaths.DataDirectory);
        var journeyService = new JourneyService(
            new JourneyStore(AppDataPaths.DataDirectory),
            new JourneyJournalHistoryReader(
                folderResolution.SelectedPath
                    ?? folderResolution.CandidatePaths.FirstOrDefault()
                    ?? Path.Combine(AppDataPaths.DataDirectory, "journals")),
            commanderProfileStore,
            sharedExobiologyCatalog);
        Search = new SphereLimitViewModel(
            commanderProfileStore,
            sharedSystemResolver);
        NearestSystems = new NearestSystemsViewModel(
            nearestSystemsClient ?? new NearestSystemsClient(),
            sharedSystemResolver);
        BoxelSearch = new BoxelSearchViewModel(
            commanderProfileStore,
            new LegacySystemDataReader(AppDataPaths.DataDirectory),
            new EmptyBoxelStore(AppDataPaths.DataDirectory),
            boxelSystemResolver ?? new SpanshBoxelClient());
        GroundTarget = new GroundTargetViewModel(
            new GroundTargetSettingsStore(AppDataPaths.DataDirectory));
        SystemNotes = new SystemNotesViewModel(
            systemNoteStore,
            systemNotesSettingsStore,
            journeyService);
        Journey = new JourneyWorkspaceViewModel(
            journeyService,
            sharedSystemResolver,
            systemNoteStore,
            systemNotesSettingsStore);
        Route = new RouteWorkspaceViewModel(
            new FollowRouteService(
                new FollowRouteStore(AppDataPaths.DataDirectory)),
            new RouteNameImporter(sharedSystemResolver),
            new SpanshRouteClient());
        JumpInfo = new JumpInfoViewModel(
            systemSummaryClient ?? new SystemSummaryClient(),
            jumpInfoSettingsStore
                ?? new JumpInfoSettingsStore(AppDataPaths.UiSettingsPath));
        SystemSurvey = new SystemSurveyViewModel(
            systemSurveySettingsStore
                ?? new SystemSurveySettingsStore(AppDataPaths.UiSettingsPath));
        BiologyPredictions = new BiologyPredictionsViewModel(
            SystemSurvey,
            biologyPredictionsSettingsStore
                ?? new BiologyPredictionsSettingsStore(
                    AppDataPaths.UiSettingsPath));
        RamTah = new RamTahViewModel(commanderProfileStore);
        Guardian = new GuardianViewModel(
            AppDataPaths.DataDirectory,
            ramTah: RamTah);
        exobiologyState = new ExobiologyState(sharedExobiologyCatalog);
        ProfileBackupDirectory = Path.Combine(
            Path.GetDirectoryName(AppDataPaths.DataDirectory)
                ?? AppDataPaths.ConfigDirectory,
            "legacy-backups");
        LegacyProfiles = LegacyProfileLocator.Discover(
                AppDataPaths.LegacyProfileCandidates)
            .Select(discovery => new LegacyProfileOptionViewModel(discovery))
            .ToArray();
        selectedLegacyProfile = LegacyProfiles.FirstOrDefault();
        profileStatusMessage = GetInitialProfileStatus();
        importLegacyProfileCommand = new AsyncCommand(
            ImportLegacyProfileAsync,
            CanImportLegacyProfile);
        ImportLegacyProfileCommand = importLegacyProfileCommand;
        JournalFolderPath = folderResolution.SelectedPath
            ?? folderResolution.CandidatePaths.FirstOrDefault()
            ?? "No journal location is configured.";
        CandidatePaths = folderResolution.CandidatePaths.Count == 0
            ? "No default locations are available for this platform."
            : string.Join(Environment.NewLine, folderResolution.CandidatePaths);
        statusMessage = folderResolution.IsFound
            ? "Ready to read the newest Journal.*.log file."
            : $"Journal folder not found. Set {JournalFolderLocator.EnvironmentVariableName} "
                + "or start with --journal-directory <path>.";
        journalMonitor = folderResolution.SelectedPath is null
            ? null
            : new JournalDirectoryMonitor(folderResolution.SelectedPath);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        resetExplorationCommand = new AsyncCommand(
            ResetExplorationAsync,
            () => activeProfileFrontierId is not null);
        ResetExplorationCommand = resetExplorationCommand;
        cancelResetExplorationCommand = new AsyncCommand(
            CancelResetExplorationAsync,
            () => IsResetExplorationPending);
        CancelResetExplorationCommand = cancelResetExplorationCommand;
        resetExobiologyCommand = new AsyncCommand(
            ResetExobiologyAsync,
            () => activeProfileFrontierId is not null);
        ResetExobiologyCommand = resetExobiologyCommand;
        cancelResetExobiologyCommand = new AsyncCommand(
            CancelResetExobiologyAsync,
            () => IsResetExobiologyPending);
        CancelResetExobiologyCommand = cancelResetExobiologyCommand;

        NavigationItems =
        [
            new("overview", "Overview", "01", "Commander and current journal state", true),
            new("exploration", "Exploration", "02", "Trip totals and body scans", true),
            new("exobiology", "Exobiology", "03", "Organic scans and unclaimed rewards", true),
            new("travel", "Travel", "04", "Ground targets, journeys, and routes", true),
            new("search", "Search", "05", "Spherical and boxel searches", true),
            new("guardian", "Guardian", "06", "Sites, maps, and Ram Tah", true),
            new("colonisation", "Colonisation", "07", "Raven Colonial projects", true),
            new("diagnostics", "Diagnostics", "08", "Journal source and parsed state", true),
            new("settings", "Settings", "09", "Appearance and application options", true),
        ];
        selectedNavigation = NavigationItems[0];

        var currentTheme = themeService?.Current
            ?? RavenThemeCatalog.Get(RavenThemeCatalog.DefaultThemeKey);
        ThemeOptions = RavenThemeCatalog.All
            .Select(theme => new ThemeOptionViewModel(theme, SelectTheme))
            .ToArray();
        selectedTheme = ThemeOptions.Single(
            option => option.Definition.Key == currentTheme.Key);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public IReadOnlyList<ThemeOptionViewModel> ThemeOptions { get; }

    public GlobalInputSettingsViewModel InputSettings { get; }

    public AppDataPaths AppDataPaths { get; }

    public GroundTargetViewModel GroundTarget { get; }

    public SystemNotesViewModel SystemNotes { get; }

    public JourneyWorkspaceViewModel Journey { get; }

    public RouteWorkspaceViewModel Route { get; }

    public JumpInfoViewModel JumpInfo { get; }

    public SystemSurveyViewModel SystemSurvey { get; }

    public BiologyPredictionsViewModel BiologyPredictions { get; }

    public SphereLimitViewModel Search { get; }

    public BoxelSearchViewModel BoxelSearch { get; }

    public NearestSystemsViewModel NearestSystems { get; }

    public GuardianViewModel Guardian { get; }

    public RamTahViewModel RamTah { get; }

    public ColonizationViewModel Colonization { get; }

    public IReadOnlyList<LegacyProfileOptionViewModel> LegacyProfiles { get; }

    public string ProfileDataDirectory => AppDataPaths.DataDirectory;

    public string ProfileBackupDirectory { get; }

    public ICommand ImportLegacyProfileCommand { get; }

    public LegacyProfileOptionViewModel? SelectedLegacyProfile
    {
        get => selectedLegacyProfile;
        set
        {
            if (SetField(ref selectedLegacyProfile, value))
            {
                importLegacyProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProfileStatusMessage
    {
        get => profileStatusMessage;
        private set => SetField(ref profileStatusMessage, value);
    }

    public string ImportProfileButtonText => IsImportingProfile
        ? "Importing profile…"
        : "Back up and import profile";

    public bool IsImportingProfile
    {
        get => isImportingProfile;
        private set
        {
            if (SetField(ref isImportingProfile, value))
            {
                importLegacyProfileCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ImportProfileButtonText));
            }
        }
    }

    public string JournalFolderPath { get; }

    public string CandidatePaths { get; }

    public ICommand RefreshCommand { get; }

    public NavigationItemViewModel SelectedNavigation
    {
        get => selectedNavigation;
        set
        {
            if (!SetField(ref selectedNavigation, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsOverviewSelected));
            OnPropertyChanged(nameof(IsExplorationSelected));
            OnPropertyChanged(nameof(IsExobiologySelected));
            OnPropertyChanged(nameof(IsTravelSelected));
            OnPropertyChanged(nameof(IsSearchSelected));
            OnPropertyChanged(nameof(IsGuardianSelected));
            OnPropertyChanged(nameof(IsColonizationSelected));
            OnPropertyChanged(nameof(IsDiagnosticsSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsPendingSelected));
            OnPropertyChanged(nameof(PendingPageTitle));
            OnPropertyChanged(nameof(PendingPageDescription));
            OnPropertyChanged(nameof(PendingPageGlyph));
        }
    }

    public bool IsOverviewSelected => SelectedNavigation.Key == "overview";

    public bool IsExplorationSelected => SelectedNavigation.Key == "exploration";

    public bool IsExobiologySelected => SelectedNavigation.Key == "exobiology";

    public bool IsTravelSelected => SelectedNavigation.Key == "travel";

    public bool IsSearchSelected => SelectedNavigation.Key == "search";

    public bool IsGuardianSelected => SelectedNavigation.Key == "guardian";

    public bool IsColonizationSelected =>
        SelectedNavigation.Key == "colonisation";

    public bool IsDiagnosticsSelected => SelectedNavigation.Key == "diagnostics";

    public bool IsSettingsSelected => SelectedNavigation.Key == "settings";

    public bool IsPendingSelected => !SelectedNavigation.IsImplemented;

    public string PendingPageTitle => SelectedNavigation.Label;

    public string PendingPageDescription => SelectedNavigation.Description;

    public string PendingPageGlyph => SelectedNavigation.Glyph;

    public string SelectedThemeName => selectedTheme.DisplayName;

    public string ThemeStatusMessage
    {
        get => themeStatusMessage;
        private set => SetField(ref themeStatusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                ((AsyncCommand)RefreshCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(RefreshButtonText));
            }
        }
    }

    public string RefreshButtonText => IsBusy ? "Refreshing…" : "Refresh";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string CommanderName
    {
        get => commanderName;
        private set => SetField(ref commanderName, value);
    }

    public string FrontierId
    {
        get => frontierId;
        private set => SetField(ref frontierId, value);
    }

    public string GameDescription
    {
        get => gameDescription;
        private set => SetField(ref gameDescription, value);
    }

    public string GameMode
    {
        get => gameMode;
        private set => SetField(ref gameMode, value);
    }

    public string SystemDescription
    {
        get => systemDescription;
        private set => SetField(ref systemDescription, value);
    }

    public string BodyName
    {
        get => bodyName;
        private set => SetField(ref bodyName, value);
    }

    public string SessionState
    {
        get => sessionState;
        private set => SetField(ref sessionState, value);
    }

    public string LastUpdated
    {
        get => lastUpdated;
        private set => SetField(ref lastUpdated, value);
    }

    public string VehicleState
    {
        get => vehicleState;
        private set => SetField(ref vehicleState, value);
    }

    public string SurfacePosition
    {
        get => surfacePosition;
        private set => SetField(ref surfacePosition, value);
    }

    public string HeadingAndAltitude
    {
        get => headingAndAltitude;
        private set => SetField(ref headingAndAltitude, value);
    }

    public string GameUiFocus
    {
        get => gameUiFocus;
        private set => SetField(ref gameUiFocus, value);
    }

    public string EstimatedExplorationValue
    {
        get => estimatedExplorationValue;
        private set => SetField(ref estimatedExplorationValue, value);
    }

    public string ExplorationJumps
    {
        get => explorationJumps;
        private set => SetField(ref explorationJumps, value);
    }

    public string ExplorationDistance
    {
        get => explorationDistance;
        private set => SetField(ref explorationDistance, value);
    }

    public string ExplorationBodies
    {
        get => explorationBodies;
        private set => SetField(ref explorationBodies, value);
    }

    public string ExplorationStatusMessage
    {
        get => explorationStatusMessage;
        private set => SetField(ref explorationStatusMessage, value);
    }

    public ICommand ResetExplorationCommand { get; }

    public ICommand CancelResetExplorationCommand { get; }

    public bool IsResetExplorationPending
    {
        get => isResetExplorationPending;
        private set
        {
            if (SetField(ref isResetExplorationPending, value))
            {
                OnPropertyChanged(nameof(ResetExplorationButtonText));
                cancelResetExplorationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ResetExplorationButtonText => IsResetExplorationPending
        ? "Confirm reset"
        : "Reset totals";

    public string UnclaimedBioRewards
    {
        get => unclaimedBioRewards;
        private set => SetField(ref unclaimedBioRewards, value);
    }

    public string UnclaimedBioScans
    {
        get => unclaimedBioScans;
        private set => SetField(ref unclaimedBioScans, value);
    }

    public string OrganicScanProgress
    {
        get => organicScanProgress;
        private set => SetField(ref organicScanProgress, value);
    }

    public string ActiveOrganicSpecies
    {
        get => activeOrganicSpecies;
        private set => SetField(ref activeOrganicSpecies, value);
    }

    public string OrganicSampleRange
    {
        get => organicSampleRange;
        private set => SetField(ref organicSampleRange, value);
    }

    public string BioFirstFootfall
    {
        get => bioFirstFootfall;
        private set => SetField(ref bioFirstFootfall, value);
    }

    public string ExobiologyStatusMessage
    {
        get => exobiologyStatusMessage;
        private set => SetField(ref exobiologyStatusMessage, value);
    }

    public string CommanderCodexStatusMessage
    {
        get => commanderCodexStatusMessage;
        private set => SetField(ref commanderCodexStatusMessage, value);
    }

    public ICommand ResetExobiologyCommand { get; }

    public ICommand CancelResetExobiologyCommand { get; }

    public bool IsResetExobiologyPending
    {
        get => isResetExobiologyPending;
        private set
        {
            if (SetField(ref isResetExobiologyPending, value))
            {
                OnPropertyChanged(nameof(ResetExobiologyButtonText));
                cancelResetExobiologyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ResetExobiologyButtonText => IsResetExobiologyPending
        ? "Confirm clear"
        : "Clear unclaimed";

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (journalMonitor is null)
        {
            StatusMessage = $"Journal folder not found. Set "
                + $"{JournalFolderLocator.EnvironmentVariableName} or use "
                + "--journal-directory <path>.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Reading journal and status updates…";

            var update = await journalMonitor.PollAsync();
            await ApplyMonitorUpdateAsync(update, isManualRefresh: true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            LastUpdated = $"Last refresh: {DateTimeOffset.Now:G}";
        }
    }

    public async Task MonitorAsync(
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        if (journalMonitor is null)
        {
            return;
        }

        var interval = pollingInterval ?? TimeSpan.FromMilliseconds(250);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var update = await journalMonitor.PollAsync(cancellationToken);
                await ApplyMonitorUpdateAsync(update, isManualRefresh: false);
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal desktop shutdown.
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            StatusMessage = "Live journal monitoring stopped: " + exception.Message;
        }
    }

    public async Task ImportLegacyProfileAsync()
    {
        if (!CanImportLegacyProfile() || SelectedLegacyProfile is null)
        {
            return;
        }

        try
        {
            IsImportingProfile = true;
            ProfileStatusMessage = "Creating and verifying the legacy profile backup…";
            var result = await profileImporter.ImportAsync(
                SelectedLegacyProfile.Path,
                AppDataPaths.DataDirectory,
                ProfileBackupDirectory);
            ProfileStatusMessage = $"Imported {result.Manifest.Entries.Count:N0} files. "
                + $"Verified backup: {result.BackupDirectory}";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            ProfileStatusMessage = $"Profile import failed without changing the legacy data: "
                + exception.Message;
        }
        finally
        {
            IsImportingProfile = false;
            importLegacyProfileCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanImportLegacyProfile()
    {
        return !IsImportingProfile
            && SelectedLegacyProfile is not null
            && !Directory.Exists(AppDataPaths.DataDirectory)
            && !File.Exists(AppDataPaths.DataDirectory);
    }

    private string GetInitialProfileStatus()
    {
        if (Directory.Exists(AppDataPaths.DataDirectory)
            || File.Exists(AppDataPaths.DataDirectory))
        {
            return $"Cross-platform profile data already exists at "
                + $"{AppDataPaths.DataDirectory}. It will not be overwritten.";
        }

        return LegacyProfiles.Count == 0
            ? "No legacy Windows profile was found in the desktop or Microsoft Store locations."
            : $"Found {LegacyProfiles.Count:N0} legacy profile source(s). "
                + "Import creates a checksum-verified backup before activating the copy.";
    }

    private void SelectTheme(ThemeOptionViewModel option)
    {
        try
        {
            themeService?.Select(option.Definition.Key);
            selectedTheme = option;
            ThemeStatusMessage = string.Empty;
            OnPropertyChanged(nameof(SelectedThemeName));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            ThemeStatusMessage = $"The theme changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void ApplySnapshot(JournalSnapshot snapshot)
    {
        CommanderName = Display(snapshot.CommanderName);
        FrontierId = Display(snapshot.FrontierId);
        GameDescription = string.Join(
            " ",
            new[]
            {
                snapshot.GameVersion,
                snapshot.GameBuild is null ? null : $"({snapshot.GameBuild})",
                snapshot.IsOdyssey switch
                {
                    true => "Odyssey",
                    false => "Horizons",
                    null => null,
                },
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(GameDescription))
        {
            GameDescription = Unavailable;
        }

        GameMode = Display(snapshot.GameMode);
        SystemDescription = snapshot.SystemAddress is null
            ? Display(snapshot.SystemName)
            : $"{Display(snapshot.SystemName)} ({snapshot.SystemAddress})";
        BodyName = Display(snapshot.BodyName);
        SessionState = snapshot.IsShutdown ? "Session closed" : "Session active";

        var malformedSuffix = snapshot.MalformedLineCount == 0
            ? string.Empty
            : $"; ignored {snapshot.MalformedLineCount} malformed/partial line(s)";
        StatusMessage = $"Loaded {snapshot.ValidLineCount} events from "
            + $"{Path.GetFileName(snapshot.SourcePath)}; "
            + $"{snapshot.RecognizedEventCount} bootstrap events recognized"
            + malformedSuffix
            + ".";
    }

    private async Task ApplyMonitorUpdateAsync(
        JournalMonitorUpdate update,
        bool isManualRefresh)
    {
        if (update.Status is not null)
        {
            exobiologyState.UpdateStatus(update.Status);
            GroundTarget.UpdateStatus(update.Status);
            Colonization.UpdateStatus(update.Status);
        }

        foreach (var journalEvent in update.JournalEvents)
        {
            journalState.Apply(journalEvent);
        }

        var commanderCodexResult =
            await commanderCodexJournalTracker.ApplyAsync(update.JournalEvents);
        if (commanderCodexResult.Warnings.Count > 0)
        {
            CommanderCodexStatusMessage = string.Join(
                Environment.NewLine,
                commanderCodexResult.Warnings);
        }
        else if (commanderCodexResult.DiscoveryEventCount > 0)
        {
            CommanderCodexStatusMessage = commanderCodexResult.HasChanges
                ? $"Recorded {commanderCodexResult.ChangedEntryCount:N0} "
                    + "Commander Codex ledger entries across "
                    + $"{commanderCodexResult.ChangedFileCount:N0} files."
                : "Commander Codex is current; no earlier firsts were found.";
        }

        Colonization.ApplyJournalEvents(update.JournalEvents);
        await Colonization.SetCommanderAsync(journalState.CommanderName);
        Colonization.UpdateSystemContext(
            journalState.SystemName,
            journalState.StarPosition);

        Search.UpdateCurrentSystem(
            journalState.SystemName,
            journalState.StarPosition);
        NearestSystems.UpdateContext(
            journalState.SystemName,
            journalState.StarPosition,
            journalState.CommanderName);
        SystemNotes.UpdateContext(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition);
        BoxelSearch.UpdateCurrentSystem(
            journalState.SystemName,
            journalState.StarPosition);
        Guardian.UpdateCurrentSystem(
            journalState.SystemName,
            journalState.StarPosition);

        var loadedExistingProfile = await EnsureCommanderProfileAsync();
        var initializedJourney = await Journey.UpdateContextAsync(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.IsOdyssey ?? true,
            journalState.SystemName,
            journalState.SystemAddress);
        if (!initializedJourney)
        {
            await Journey.ApplyJournalEventsAsync(update.JournalEvents);
        }

        await Route.UpdateContextAsync(
            journalState.FrontierId,
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition);
        if (!update.IsBootstrapRead)
        {
            await Route.ApplyJournalEventsAsync(update.JournalEvents);
        }

        var explorationBefore = explorationState.CreateSnapshot();
        var exobiologyVersionBefore = exobiologyState.Version;
        var skipPersistedBootstrapEvents = update.IsBootstrapRead
            && loadedExistingProfile;
        if (update.NavRoute is not null)
        {
            await BoxelSearch.UpdateRouteAsync(update.NavRoute);
        }

        if (!skipPersistedBootstrapEvents)
        {
            await BoxelSearch.ApplyJournalEventsAsync(update.JournalEvents);
        }

        await Guardian.ApplyJournalEventsAsync(
            update.JournalEvents,
            activeProfileCommanderName);
        await RamTah.ApplyJournalEventsAsync(update.JournalEvents);
        Guardian.UpdateCargo(update.Cargo);
        Colonization.UpdateCargo(update.Cargo);
        await Colonization.UpdateMarketAsync(update.Market);

        if (update.Status is not null)
        {
            Guardian.UpdateStatus(update.Status);
            await Route.UpdateStatusAsync(update.Status);
            await BoxelSearch.UpdateStatusAsync(
                update.Status,
                allowAutoCopy: !Route.ShouldAutoCopyNextHop);
        }

        JumpInfo.ApplyUpdate(
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition,
            update.NavRoute,
            update.JournalEvents,
            update.Status,
            Route.CreateSnapshot(),
            update.IsBootstrapRead);
        foreach (var journalEvent in update.JournalEvents)
        {
            if (!skipPersistedBootstrapEvents
                || journalEvent.EventName is "Fileheader" or "LoadGame")
            {
                explorationState.Apply(journalEvent);
            }

            if (!skipPersistedBootstrapEvents
                || IsExobiologyContextEvent(journalEvent.EventName))
            {
                exobiologyState.Apply(journalEvent);
            }
        }

        var explorationAfter = explorationState.CreateSnapshot();
        if (explorationAfter != explorationBefore)
        {
            UpdateExplorationDisplay(explorationAfter);
            await SaveExplorationAsync(explorationAfter);
        }

        var exobiologyAfter = exobiologyState.CreateSnapshot();
        SystemSurvey.ApplyUpdate(
            update.JournalEvents,
            update.Status,
            exobiologyAfter);
        if (exobiologyState.Version != exobiologyVersionBefore)
        {
            await SaveExobiologyAsync(exobiologyAfter);
        }

        if (update.JournalEvents.Count > 0 || update.Status is not null)
        {
            UpdateExobiologyDisplay(exobiologyAfter);
        }

        if (update.JournalEvents.Count > 0)
        {
            ApplySnapshot(journalState.CreateSnapshot(update.JournalPath));
        }
        else if (isManualRefresh)
        {
            StatusMessage = update.JournalPath is null
                ? $"No Journal.*.log files were found in {JournalFolderPath}."
                : $"Monitoring {Path.GetFileName(update.JournalPath)}; no new events.";
        }

        if (update.Status is not null)
        {
            ApplyStatus(update.Status);
        }

        if (update.Errors.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, update.Errors);
        }

        if (update.JournalEvents.Count > 0
            || update.Status is not null
            || update.NavRoute is not null
            || update.Cargo is not null
            || update.Market is not null
            || update.Errors.Count > 0
            || isManualRefresh)
        {
            LastUpdated = $"Last update: {DateTimeOffset.Now:G}";
        }
    }

    private async Task<bool> EnsureCommanderProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(journalState.FrontierId))
        {
            return false;
        }

        var isOdyssey = journalState.IsOdyssey ?? true;
        if (string.Equals(
                activeProfileFrontierId,
                journalState.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            && activeProfileIsOdyssey == isOdyssey)
        {
            activeProfileCommanderName = journalState.CommanderName
                ?? activeProfileCommanderName;
            return false;
        }

        var result = await commanderProfileStore.LoadAsync(
            journalState.FrontierId,
            isOdyssey);
        activeProfileFrontierId = journalState.FrontierId;
        activeProfileCommanderName = journalState.CommanderName
            ?? result.Data?.CommanderName;
        activeProfileIsOdyssey = isOdyssey;
        resetExplorationCommand.RaiseCanExecuteChanged();
        resetExobiologyCommand.RaiseCanExecuteChanged();

        if (result.Data is null)
        {
            Colonization.SetCommanderProfile(null, isOdyssey, apiKey: null);
            ExplorationStatusMessage = result.Error
                ?? "The commander profile could not be loaded.";
            ExobiologyStatusMessage = result.Error
                ?? "The commander profile could not be loaded.";
            Search.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            BoxelSearch.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            Guardian.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            RamTah.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            return false;
        }

        Colonization.SetCommanderProfile(
            result.Data.FrontierId,
            result.Data.IsOdyssey,
            result.Data.RavenColonialApiKey);

        explorationState.Reset(result.Data.Exploration);
        exobiologyState.Reset(result.Data.Exobiology);
        Search.LoadProfile(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.SphereLimit);
        await BoxelSearch.LoadProfileAsync(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.BoxelSearch);
        await Guardian.LoadProfileAsync(
            result.Data.FrontierId,
            result.Data.IsOdyssey);
        RamTah.LoadProfile(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.RamTah);
        UpdateExplorationDisplay(result.Data.Exploration);
        UpdateExobiologyDisplay(result.Data.Exobiology);
        ExplorationStatusMessage = result.Exists
            ? $"Loaded compatible totals from {Path.GetFileName(result.Path)}."
            : $"No existing profile was found; session totals will be saved to "
                + Path.GetFileName(result.Path)
                + ".";
        ExobiologyStatusMessage = result.Exists
            ? $"Loaded legacy-compatible organic scan state from "
                + Path.GetFileName(result.Path)
                + "."
            : $"No existing profile was found; organic scan state will be saved to "
                + Path.GetFileName(result.Path)
                + ".";
        return result.Exists;
    }

    private async Task SaveExplorationAsync(ExplorationSnapshot snapshot)
    {
        if (activeProfileFrontierId is null)
        {
            return;
        }

        try
        {
            await commanderProfileStore.SaveExplorationAsync(
                activeProfileFrontierId,
                activeProfileCommanderName,
                activeProfileIsOdyssey,
                snapshot);
            ExplorationStatusMessage = $"Totals saved to "
                + Path.GetFileName(commanderProfileStore.GetProfilePath(
                    activeProfileFrontierId,
                    activeProfileIsOdyssey))
                + ".";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            ExplorationStatusMessage = "Totals changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateExplorationDisplay(ExplorationSnapshot snapshot)
    {
        EstimatedExplorationValue = $"{snapshot.EstimatedRewards:N0} CR";
        ExplorationJumps = snapshot.JumpCount.ToString("N0");
        ExplorationDistance = $"{snapshot.DistanceTravelled:N1} ly";
        ExplorationBodies = $"Scanned: {snapshot.ScanCount:N0}, "
            + $"DSS: {snapshot.DetailedSurfaceScanCount:N0}, "
            + $"Landed: {snapshot.LandedBodyCount:N0}";
    }

    private async Task SaveExobiologyAsync(ExobiologySnapshot snapshot)
    {
        if (activeProfileFrontierId is null)
        {
            return;
        }

        try
        {
            await commanderProfileStore.SaveExobiologyAsync(
                activeProfileFrontierId,
                activeProfileCommanderName,
                activeProfileIsOdyssey,
                snapshot);
            ExobiologyStatusMessage = $"Organic scan state saved to "
                + Path.GetFileName(commanderProfileStore.GetProfilePath(
                    activeProfileFrontierId,
                    activeProfileIsOdyssey))
                + ".";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            ExobiologyStatusMessage =
                "Organic scan state changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateExobiologyDisplay(ExobiologySnapshot snapshot)
    {
        UnclaimedBioRewards = $"{snapshot.OrganicRewards:N0} CR";
        UnclaimedBioScans = snapshot.ScannedBioEntryIds.Count == 1
            ? "1 organism"
            : $"{snapshot.ScannedBioEntryIds.Count:N0} organisms";
        var activeSample = snapshot.ScanTwo ?? snapshot.ScanOne;
        ActiveOrganicSpecies = activeSample is null
            ? Unavailable
            : exobiologyState.ActiveSpeciesDisplayName
                ?? activeSample.Species;
        OrganicSampleRange = activeSample is null
            ? Unavailable
            : exobiologyState.NearestActiveSampleDistance is double distance
                ? exobiologyState.RemainingSampleDistance is > 0
                    ? $"{distance:N0} m from nearest sample · "
                        + $"{exobiologyState.RemainingSampleDistance:N0} m remaining"
                    : $"{distance:N0} m from nearest sample · clear to sample"
                : $"{activeSample.Radius:N0} m minimum separation";
        OrganicScanProgress = snapshot.ScanOne is null
            ? "Ready for sample 1 of 3"
            : snapshot.ScanTwo is null
                ? "Sample 1 of 3 recorded"
                : "Samples 1 and 2 of 3 recorded";
        BioFirstFootfall = exobiologyState.CurrentBodyFirstFootfall switch
        {
            true => "Confirmed; 5x reward applies",
            false => "Not first footfall",
            null => "Unknown for current body",
        };
    }

    private static bool IsExobiologyContextEvent(string eventName)
    {
        return eventName is "Location"
            or "FSDJump"
            or "CarrierJump"
            or "ApproachBody"
            or "Scan"
            or "Disembark";
    }

    public async Task ResetExplorationAsync()
    {
        if (!IsResetExplorationPending)
        {
            IsResetExplorationPending = true;
            ExplorationStatusMessage = "Select Confirm reset to clear all six exploration totals.";
            return;
        }

        explorationState.Reset();
        var snapshot = explorationState.CreateSnapshot();
        UpdateExplorationDisplay(snapshot);
        IsResetExplorationPending = false;
        await SaveExplorationAsync(snapshot);
    }

    private Task CancelResetExplorationAsync()
    {
        IsResetExplorationPending = false;
        ExplorationStatusMessage = "Reset cancelled; totals were not changed.";
        return Task.CompletedTask;
    }

    public async Task ResetExobiologyAsync()
    {
        if (!IsResetExobiologyPending)
        {
            IsResetExobiologyPending = true;
            ExobiologyStatusMessage = "Select Confirm clear to remove all unclaimed "
                + "organic rewards. Active sample progress will be kept.";
            return;
        }

        exobiologyState.ClearUnclaimedRewards();
        var snapshot = exobiologyState.CreateSnapshot();
        UpdateExobiologyDisplay(snapshot);
        IsResetExobiologyPending = false;
        await SaveExobiologyAsync(snapshot);
    }

    private Task CancelResetExobiologyAsync()
    {
        IsResetExobiologyPending = false;
        ExobiologyStatusMessage = "Clear cancelled; unclaimed rewards were not changed.";
        return Task.CompletedTask;
    }

    private void ApplyStatus(EliteStatus status)
    {
        VehicleState = status.OnFoot
            ? "On foot"
            : status.InSrv
                ? "SRV"
                : status.InFighter
                    ? "Fighter"
                    : status.InMainShip
                        ? "Main ship"
                        : status.InTaxi
                            ? "Taxi / shuttle"
                            : "Unknown";
        SurfacePosition = status.HasLatitudeLongitude
            ? $"{status.Latitude:F6}, {status.Longitude:F6}"
            : Unavailable;
        HeadingAndAltitude = status.HasLatitudeLongitude
            ? $"{status.NormalizedHeading}° / {status.Altitude:N0} m"
            : Unavailable;
        GameUiFocus = status.GuiFocus.ToString();
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unavailable : value;
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
