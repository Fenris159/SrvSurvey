using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "—";

    private readonly JournalFolderResolution folderResolution;
    private readonly JournalDirectoryMonitor? journalMonitor;
    private readonly JournalSessionState journalState = new();
    private readonly RavenThemeService? themeService;
    private readonly LegacyProfileImporter profileImporter;
    private readonly AsyncCommand importLegacyProfileCommand;
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
    private NavigationItemViewModel selectedNavigation;
    private ThemeOptionViewModel selectedTheme;
    private LegacyProfileOptionViewModel? selectedLegacyProfile;
    private string profileStatusMessage;

    public MainWindowViewModel(
        string? configuredJournalDirectory,
        RavenThemeService? themeService = null,
        AppDataPaths? appDataPaths = null,
        LegacyProfileImporter? profileImporter = null)
    {
        this.themeService = themeService;
        this.profileImporter = profileImporter ?? new LegacyProfileImporter();
        AppDataPaths = appDataPaths ?? AppDataPaths.ResolveCurrent();
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
        folderResolution = JournalFolderLocator.ResolveCurrent(configuredJournalDirectory);
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

        NavigationItems =
        [
            new("overview", "Overview", "01", "Commander and current journal state", true),
            new("exploration", "Exploration", "02", "Trip totals and body scans", false),
            new("exobiology", "Exobiology", "03", "Organic scans, rewards, and Codex", false),
            new("travel", "Travel", "04", "Targets, journeys, and routes", false),
            new("search", "Search", "05", "Spherical and boxel searches", false),
            new("guardian", "Guardian", "06", "Sites, maps, and Ram Tah", false),
            new("colonisation", "Colonisation", "07", "Raven Colonial projects", false),
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

    public AppDataPaths AppDataPaths { get; }

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
            OnPropertyChanged(nameof(IsDiagnosticsSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsPendingSelected));
            OnPropertyChanged(nameof(PendingPageTitle));
            OnPropertyChanged(nameof(PendingPageDescription));
            OnPropertyChanged(nameof(PendingPageGlyph));
        }
    }

    public bool IsOverviewSelected => SelectedNavigation.Key == "overview";

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
            ApplyMonitorUpdate(update, isManualRefresh: true);
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
                ApplyMonitorUpdate(update, isManualRefresh: false);
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

    private void ApplyMonitorUpdate(
        JournalMonitorUpdate update,
        bool isManualRefresh)
    {
        foreach (var journalEvent in update.JournalEvents)
        {
            journalState.Apply(journalEvent);
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
            || update.Errors.Count > 0
            || isManualRefresh)
        {
            LastUpdated = $"Last update: {DateTimeOffset.Now:G}";
        }
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
