using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";

    private readonly JourneyService journeyService;
    private readonly IStarSystemResolver systemResolver;
    private readonly SystemNoteStore noteStore;
    private readonly SystemNotesSettingsStore settingsStore;
    private readonly Func<string, string?> screenshotDirectoryProvider;
    private readonly AsyncCommand openWorkspaceCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand newJourneyCommand;
    private readonly AsyncCommand cancelNewJourneyCommand;
    private readonly AsyncCommand searchSystemsCommand;
    private readonly AsyncCommand findStartCommand;
    private readonly AsyncCommand beginJourneyCommand;
    private readonly AsyncCommand saveCommand;
    private readonly AsyncCommand discardCommand;
    private readonly AsyncCommand requestConcludeCommand;
    private readonly AsyncCommand confirmConcludeCommand;
    private readonly AsyncCommand cancelConcludeCommand;
    private readonly AsyncCommand requestReprocessCommand;
    private readonly AsyncCommand confirmReprocessCommand;
    private readonly AsyncCommand cancelReprocessCommand;
    private readonly AsyncCommand openImagesCommand;
    private string? frontierId;
    private string? commanderName;
    private bool isOdyssey = true;
    private string? currentSystemName;
    private long? currentSystemAddress;
    private string? initializedProfileKey;
    private int contextVersion;
    private bool isBusy;
    private string statusMessage = "Waiting for a commander profile.";
    private IReadOnlyList<JourneyListItemViewModel> journeys = [];
    private JourneyListItemViewModel? selectedJourney;
    private JourneyDocument? selectedDocument;
    private IReadOnlyList<JourneyStatisticViewModel> quickStatistics = [];
    private IReadOnlyList<JourneySystemItemViewModel> visitedSystems = [];
    private JourneySystemItemViewModel? selectedSystem;
    private string journeyName = string.Empty;
    private string journeyDescription = string.Empty;
    private string selectedSystemNotes = string.Empty;
    private string loadedSystemNotes = string.Empty;
    private string selectedSystemDetails = string.Empty;
    private IReadOnlyList<string> screenshotFiles = [];
    private string? imagesDirectory;
    private bool isApplyingDocument;
    private bool isUpdatingLibrary;
    private bool suppressSystemLoad;
    private int systemLoadVersion;
    private bool isCreating;
    private string newJourneyName = string.Empty;
    private string newJourneyDescription = string.Empty;
    private bool useCurrentStart = true;
    private string startSystemQuery = string.Empty;
    private IReadOnlyList<JourneyStartSystemViewModel> startSystemResults = [];
    private JourneyStartSystemViewModel? selectedStartSystem;
    private string startStatus = "Choose where this journey should begin.";
    private bool isConcludePending;
    private bool isReprocessPending;
    private bool alwaysOnTop;
    private bool useGalacticTime;
    private Func<Task<bool>>? workspaceOpener;
    private Func<DirectoryInfo, Task<bool>>? directoryLauncher;

    public JourneyWorkspaceViewModel(
        JourneyService journeyService,
        IStarSystemResolver systemResolver,
        SystemNoteStore noteStore,
        SystemNotesSettingsStore settingsStore,
        Func<string, string?>? screenshotDirectoryProvider = null
    )
    {
        this.journeyService = journeyService ?? throw new ArgumentNullException(nameof(journeyService));
        this.systemResolver = systemResolver ?? throw new ArgumentNullException(nameof(systemResolver));
        this.noteStore = noteStore ?? throw new ArgumentNullException(nameof(noteStore));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.screenshotDirectoryProvider = screenshotDirectoryProvider ?? settingsStore.GetImagesDirectory;
        SystemNotesSettingsLoadResult settings = settingsStore.Load();
        alwaysOnTop = settings.Snapshot?.JourneyAlwaysOnTop ?? false;
        useGalacticTime = settings.Snapshot?.JourneyUseGalacticTime ?? false;
        if (!settings.IsSuccess)
        {
            statusMessage = settings.Error ?? "The Journey preferences could not be loaded.";
        }

        openWorkspaceCommand = new AsyncCommand(OpenWorkspaceAsync, CanOpenWorkspace);
        refreshCommand = new AsyncCommand(RefreshAsync, () => HasProfileAndNotBusy() && !IsDirty);
        newJourneyCommand = new AsyncCommand(StartNewJourneyAsync, () => HasProfileAndNotBusy() && !HasActiveJourney);
        cancelNewJourneyCommand = new AsyncCommand(CancelNewJourneyAsync, CanCancelNewJourney);
        searchSystemsCommand = new AsyncCommand(
            SearchSystemsAsync,
            () => IsCreating && !UseCurrentStart && !string.IsNullOrWhiteSpace(StartSystemQuery) && !IsBusy
        );
        findStartCommand = new AsyncCommand(FindStartAsync, CanFindStart);
        beginJourneyCommand = new AsyncCommand(BeginJourneyAsync, CanBeginJourney);
        saveCommand = new AsyncCommand(SaveAsync, () => IsDirty && !IsBusy);
        discardCommand = new AsyncCommand(DiscardAsync, () => IsDirty && !IsBusy);
        requestConcludeCommand = new AsyncCommand(RequestConcludeAsync, CanConclude);
        confirmConcludeCommand = new AsyncCommand(ConfirmConcludeAsync, () => IsConcludePending && CanConclude());
        cancelConcludeCommand = new AsyncCommand(CancelConcludeAsync, () => IsConcludePending && !IsBusy);
        requestReprocessCommand = new AsyncCommand(
            RequestReprocessAsync,
            () => selectedDocument is not null && !IsDirty && !IsBusy
        );
        confirmReprocessCommand = new AsyncCommand(
            ConfirmReprocessAsync,
            () => IsReprocessPending && selectedDocument is not null && !IsBusy
        );
        cancelReprocessCommand = new AsyncCommand(CancelReprocessAsync, () => IsReprocessPending && !IsBusy);
        openImagesCommand = new AsyncCommand(
            OpenImagesAsync,
            () => HasImagesDirectory && directoryLauncher is not null
        );
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasProfile => !string.IsNullOrWhiteSpace(frontierId);

    public bool HasActiveJourney =>
        journeyService.ActiveJourney is { } active
        && string.Equals(active.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase);

    private JourneyDocument? CurrentActiveJourney => HasActiveJourney ? journeyService.ActiveJourney : null;

    public string ActiveJourneyName => CurrentActiveJourney?.Name ?? "No active journey";

    public string ActiveJourneySummary =>
        CurrentActiveJourney is { } active
            ? $"{active.VisitedSystems.Count:N0} visits \u2022 set out {FormatTime(active.StartTime)}"
            : "Start a journey to track systems, scans, notes, screenshots, and rewards.";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseCommands();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public IReadOnlyList<JourneyListItemViewModel> Journeys
    {
        get => journeys;
        private set
        {
            if (SetField(ref journeys, value))
            {
                RaiseLibraryState();
            }
        }
    }

    public bool HasJourneys => Journeys.Count > 0;

    public JourneyListItemViewModel? SelectedJourney
    {
        get => selectedJourney;
        set
        {
            if (isUpdatingLibrary)
            {
                return;
            }
            if (IsDirty && selectedDocument?.FileName != value?.Document.FileName)
            {
                StatusMessage = "Save or discard changes before selecting another journey.";
                OnPropertyChanged();
                return;
            }
            if (!SetField(ref selectedJourney, value))
            {
                return;
            }

            ApplyDocument(value?.Document);
        }
    }

    public bool HasSelectedJourney => selectedDocument is not null;

    public string JourneyName
    {
        get => journeyName;
        set
        {
            if (SetField(ref journeyName, value ?? string.Empty) && !isApplyingDocument)
            {
                RaiseDirtyState();
            }
        }
    }

    public string JourneyDescription
    {
        get => journeyDescription;
        set
        {
            if (SetField(ref journeyDescription, value ?? string.Empty) && !isApplyingDocument)
            {
                RaiseDirtyState();
            }
        }
    }

    public string JourneyByline =>
        selectedDocument is null
            ? Unavailable
            : $"CMDR {selectedDocument.CommanderName} \u2022 "
                + $"set out {FormatTime(selectedDocument.StartTime)}"
                + (
                    selectedDocument.EndTime switch
                    {
                        DateTimeOffset end => $" \u2022 concluded {FormatTime(end)}",
                        null => " \u2022 active",
                    }
                );

    public IReadOnlyList<JourneyStatisticViewModel> QuickStatistics
    {
        get => quickStatistics;
        private set => SetField(ref quickStatistics, value);
    }

    public IReadOnlyList<JourneySystemItemViewModel> VisitedSystems
    {
        get => visitedSystems;
        private set => SetField(ref visitedSystems, value);
    }

    public JourneySystemItemViewModel? SelectedSystem
    {
        get => selectedSystem;
        set
        {
            if (!isApplyingDocument && SelectedSystemNotes != loadedSystemNotes)
            {
                StatusMessage = "Save or discard the system notes before selecting another visit.";
                OnPropertyChanged();
                return;
            }
            if (!SetField(ref selectedSystem, value))
            {
                return;
            }

            if (!suppressSystemLoad)
            {
                _ = LoadSelectedSystemAsync(value);
            }

            OnPropertyChanged(nameof(SelectedSystemName));
            OnPropertyChanged(nameof(SelectedSystemAddress));
            OnPropertyChanged(nameof(SelectedSystemAddressText));
        }
    }

    public bool HasSelectedSystem => SelectedSystem is not null;

    public string SelectedSystemName => SelectedSystem?.Name ?? string.Empty;

    public string SelectedSystemAddress => SelectedSystem?.Address ?? string.Empty;

    public string SelectedSystemAddressText =>
        SystemAddressFormatter.Format(SelectedSystem?.Visit.StarSystem.SystemAddress);

    public string SelectedSystemNotes
    {
        get => selectedSystemNotes;
        set
        {
            if (SetField(ref selectedSystemNotes, value ?? string.Empty) && !isApplyingDocument)
            {
                RaiseDirtyState();
            }
        }
    }

    public string SelectedSystemDetails
    {
        get => selectedSystemDetails;
        private set => SetField(ref selectedSystemDetails, value);
    }

    public IReadOnlyList<string> ScreenshotFiles
    {
        get => screenshotFiles;
        private set
        {
            if (SetField(ref screenshotFiles, value))
            {
                OnPropertyChanged(nameof(HasScreenshots));
            }
        }
    }

    public bool HasScreenshots => ScreenshotFiles.Count > 0;

    public bool HasImagesDirectory => !string.IsNullOrWhiteSpace(imagesDirectory) && Directory.Exists(imagesDirectory);

    public void RefreshScreenshotAvailability()
    {
        try
        {
            imagesDirectory =
                selectedDocument is not null && SelectedSystem is { } selected
                    ? screenshotDirectoryProvider(selected.Visit.StarSystem.Name)
                    : null;
            ScreenshotFiles = GetScreenshotFiles(imagesDirectory);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            imagesDirectory = null;
            ScreenshotFiles = [];
            StatusMessage = "The screenshot folder could not be opened: " + exception.Message;
        }
        OnPropertyChanged(nameof(HasImagesDirectory));
        RaiseCommands();
    }

    public bool IsDirty =>
        selectedDocument is not null
        && (
            !string.Equals(JourneyName, selectedDocument.Name, StringComparison.Ordinal)
            || !string.Equals(JourneyDescription, selectedDocument.Description, StringComparison.Ordinal)
            || !string.Equals(SelectedSystemNotes, loadedSystemNotes, StringComparison.Ordinal)
        );

    public bool IsCreating
    {
        get => isCreating;
        private set
        {
            if (SetField(ref isCreating, value))
            {
                RaiseCommands();
            }
        }
    }

    public string NewJourneyName
    {
        get => newJourneyName;
        set
        {
            if (SetField(ref newJourneyName, value ?? string.Empty))
            {
                beginJourneyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewJourneyDescription
    {
        get => newJourneyDescription;
        set => SetField(ref newJourneyDescription, value ?? string.Empty);
    }

    public bool UseCurrentStart
    {
        get => useCurrentStart;
        set
        {
            if (!SetField(ref useCurrentStart, value))
            {
                return;
            }

            ClearStartVisits();
            OnPropertyChanged(nameof(UsePriorStart));
            StartStatus = value
                ? $"Find recorded arrivals in {currentSystemName ?? "the current system"}."
                : "Search for a previously visited system.";
            RaiseCommands();
        }
    }

    public bool UsePriorStart
    {
        get => !UseCurrentStart;
        set
        {
            if (value)
            {
                UseCurrentStart = false;
            }
        }
    }

    public string CurrentStartSystem =>
        currentSystemAddress is > 0
            ? $"{currentSystemName ?? "Current system"} ({currentSystemAddress})"
            : "No current journal system";

    public string StartSystemQuery
    {
        get => startSystemQuery;
        set
        {
            if (SetField(ref startSystemQuery, value ?? string.Empty))
            {
                if (UsePriorStart)
                {
                    SelectedStartSystem = null;
                    StartSystemResults = [];
                    ClearStartVisits();
                    StartStatus = "Search for a previously visited system.";
                }
                searchSystemsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<JourneyStartSystemViewModel> StartSystemResults
    {
        get => startSystemResults;
        private set => SetField(ref startSystemResults, value);
    }

    public JourneyStartSystemViewModel? SelectedStartSystem
    {
        get => selectedStartSystem;
        set
        {
            if (!SetField(ref selectedStartSystem, value))
            {
                return;
            }

            ClearStartVisits();
            StartStatus = value is null
                ? "Select a system from the search results."
                : $"Find recorded arrivals in {value.Name}.";
            RaiseCommands();
        }
    }

    public string StartStatus
    {
        get => startStatus;
        private set => SetField(ref startStatus, value);
    }

    public bool IsConcludePending
    {
        get => isConcludePending;
        private set
        {
            if (SetField(ref isConcludePending, value))
            {
                RaiseCommands();
            }
        }
    }

    public bool IsReprocessPending
    {
        get => isReprocessPending;
        private set
        {
            if (SetField(ref isReprocessPending, value))
            {
                RaiseCommands();
            }
        }
    }

    public bool AlwaysOnTop
    {
        get => alwaysOnTop;
        private set => SetField(ref alwaysOnTop, value);
    }

    public bool UseGalacticTime
    {
        get => useGalacticTime;
        private set => SetField(ref useGalacticTime, value);
    }

    public ICommand OpenWorkspaceCommand => openWorkspaceCommand;

    public ICommand RefreshCommand => refreshCommand;

    public ICommand NewJourneyCommand => newJourneyCommand;

    public ICommand CancelNewJourneyCommand => cancelNewJourneyCommand;

    public ICommand SearchSystemsCommand => searchSystemsCommand;

    public ICommand FindStartCommand => findStartCommand;

    public ICommand BeginJourneyCommand => beginJourneyCommand;

    public ICommand SaveCommand => saveCommand;

    public ICommand DiscardCommand => discardCommand;

    public ICommand RequestConcludeCommand => requestConcludeCommand;

    public ICommand ConfirmConcludeCommand => confirmConcludeCommand;

    public ICommand CancelConcludeCommand => cancelConcludeCommand;

    public ICommand RequestReprocessCommand => requestReprocessCommand;

    public ICommand ConfirmReprocessCommand => confirmReprocessCommand;

    public ICommand CancelReprocessCommand => cancelReprocessCommand;

    public ICommand OpenImagesCommand => openImagesCommand;

    public void SetWorkspaceOpener(Func<Task<bool>>? opener)
    {
        workspaceOpener = opener;
        openWorkspaceCommand.RaiseCanExecuteChanged();
    }

    public void SetDirectoryLauncher(Func<DirectoryInfo, Task<bool>>? launcher)
    {
        directoryLauncher = launcher;
        openImagesCommand.RaiseCanExecuteChanged();
    }

    public async Task<bool> UpdateContextAsync(
        string? nextFrontierId,
        string? nextCommanderName,
        bool nextIsOdyssey,
        string? nextSystemName,
        long? nextSystemAddress
    )
    {
        string? normalizedFrontierId = string.IsNullOrWhiteSpace(nextFrontierId) ? null : nextFrontierId;
        if (
            string.Equals(frontierId, normalizedFrontierId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(commanderName, nextCommanderName, StringComparison.Ordinal)
            && isOdyssey == nextIsOdyssey
            && string.Equals(currentSystemName, nextSystemName, StringComparison.Ordinal)
            && currentSystemAddress == nextSystemAddress
        )
        {
            return false;
        }

        string? profileKey = normalizedFrontierId is null ? null : $"{normalizedFrontierId}|{nextIsOdyssey}";
        if (!string.Equals(initializedProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
        {
            RememberJourneyDraft();
        }
        InvalidateStartVisitContext(profileKey, nextSystemAddress);
        frontierId = normalizedFrontierId;
        commanderName = nextCommanderName;
        isOdyssey = nextIsOdyssey;
        currentSystemName = nextSystemName;
        currentSystemAddress = nextSystemAddress;
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(CurrentStartSystem));

        if (string.Equals(initializedProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
        {
            RaiseCommands();
            return false;
        }

        initializedProfileKey = profileKey;
        int version = ++contextVersion;
        selectedJourney = null;
        OnPropertyChanged(nameof(SelectedJourney));
        ApplyDocument(null);
        Journeys = [];
        showArchived = false;
        OnPropertyChanged(nameof(ShowArchived));
        IsCreating = false;
        IsConcludePending = false;
        IsReprocessPending = false;
        RaiseLibraryState();
        if (frontierId is null)
        {
            IsBusy = false;
            ApplyDocument(null);
            Journeys = [];
            OnPropertyChanged(nameof(HasJourneys));
            StatusMessage = "Waiting for a commander profile.";
            RaiseActiveState();
            return true;
        }

        try
        {
            IsBusy = true;
            JourneyServiceResult active = await journeyService.InitializeActiveAsync(frontierId, isOdyssey);
            if (version != contextVersion)
            {
                return true;
            }

            string? preferredFile = PreferredJourneyFile(profileKey, active.Journey?.FileName);
            await RefreshCatalogAsync(preferredFile);
            if (version != contextVersion)
            {
                return true;
            }

            SetInitializationStatus(active);
            if (profileKey is not null)
            {
                IsBusy = true;
                await RestoreJourneyDraftAsync(profileKey, version);
            }
            RaiseActiveState();
            return true;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "Journey data could not be initialized: " + exception.Message;
            }

            return true;
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    private void SetInitializationStatus(JourneyServiceResult active)
    {
        if (active.Errors.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, active.Errors);
        }
        else if (active.Journey is null)
        {
            StatusMessage = "No active journey. Browse history or begin a new expedition.";
        }
        else if (active.ProcessedEventCount > 0)
        {
            StatusMessage = $"Caught up {active.ProcessedEventCount:N0} Journey journal events.";
        }
        else
        {
            StatusMessage = $"Active journey: {active.Journey.Name}.";
        }
    }

    public async Task ApplyJournalEventsAsync(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        if (journalEvents.Count == 0 || !HasActiveJourney)
        {
            return;
        }

        try
        {
            JourneyServiceResult result = await journeyService.ApplyLiveAsync(journalEvents);
            if (result.ProcessedEventCount == 0 || result.Journey is null)
            {
                return;
            }

            UpdateCatalogDocument(result.Journey);
            if (selectedDocument?.FileName == result.Journey.FileName)
            {
                ApplyDocument(result.Journey, preserveEdits: IsDirty);
            }

            StatusMessage = $"Journey updated through {FormatTime(result.Journey.Watermark)}.";
            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The live Journey update was not saved: " + exception.Message;
        }
    }

    public Task RefreshAsync()
    {
        if (IsDirty)
        {
            StatusMessage = "Save or discard changes before refreshing journeys.";
            return Task.CompletedTask;
        }
        return RefreshCatalogAsync(selectedDocument?.FileName);
    }

    public async Task SetPreferencesAsync(bool nextAlwaysOnTop, bool nextUseGalacticTime)
    {
        bool previousAlwaysOnTop = AlwaysOnTop;
        bool previousUseGalacticTime = UseGalacticTime;
        AlwaysOnTop = nextAlwaysOnTop;
        UseGalacticTime = nextUseGalacticTime;
        try
        {
            await settingsStore.SaveJourneyPreferencesAsync(nextAlwaysOnTop, nextUseGalacticTime);
            if (selectedDocument is not null)
            {
                ApplyDocument(selectedDocument, preserveEdits: IsDirty);
            }

            OnPropertyChanged(nameof(ActiveJourneySummary));
            StatusMessage = "Journey preferences saved.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AlwaysOnTop = previousAlwaysOnTop;
            UseGalacticTime = previousUseGalacticTime;
            StatusMessage = "Journey preferences were not saved: " + exception.Message;
        }
    }

    private bool HasProfileAndNotBusy()
    {
        return HasProfile && !IsBusy;
    }

    private bool CanOpenWorkspace()
    {
        return workspaceOpener is not null && !IsBusy;
    }

    private async Task OpenWorkspaceAsync()
    {
        if (workspaceOpener is null || !await workspaceOpener())
        {
            StatusMessage = "The Journey workspace requires a commander profile.";
        }
    }

    private async Task RefreshCatalogAsync(string? preferredFileName)
    {
        if (frontierId is null)
        {
            return;
        }

        string profile = frontierId;
        int version = contextVersion;
        try
        {
            IsBusy = true;
            JourneyCatalogResult result = await journeyService.LoadAllAsync(profile);
            if (version != contextVersion)
            {
                return;
            }

            Journeys = result
                .Journeys.Select(journey => new JourneyListItemViewModel(
                    journey,
                    FormatTime(journey.StartTime),
                    FormatTime(journey.Watermark)
                ))
                .ToArray();
            OnPropertyChanged(nameof(HasJourneys));
            SelectedJourney =
                VisibleJourneys.FirstOrDefault(item =>
                    string.Equals(item.Document.FileName, preferredFileName, StringComparison.Ordinal)
                )
                ?? VisibleJourneys.FirstOrDefault(item => item.Document.IsActive)
                ?? (VisibleJourneys.Count > 0 ? VisibleJourneys[0] : null);
            if (result.Errors.Count > 0)
            {
                StatusMessage = string.Join(Environment.NewLine, result.Errors);
            }
            else
            {
                StatusMessage = SelectedJourney is null
                    ? "No Journey history has been recorded for this commander."
                    : (SelectedJourney.Document.IsActive) switch
                    {
                        true => $"Active journey: {SelectedJourney.Document.Name}.",
                        false => $"Loaded journey: {SelectedJourney.Document.Name}.",
                    };
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "Journey history could not be loaded: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    public Task StartNewJourneyAsync()
    {
        IsCreating = true;
        NewJourneyName = string.Empty;
        NewJourneyDescription = string.Empty;
        UseCurrentStart = true;
        StartSystemQuery = currentSystemName ?? string.Empty;
        StartSystemResults = [];
        SelectedStartSystem = null;
        ClearStartVisits();
        StartStatus = currentSystemAddress is > 0
            ? $"Find recorded arrivals in {currentSystemName}."
            : "A current journal system is required, or search for a prior system.";
        return Task.CompletedTask;
    }

    private Task CancelNewJourneyAsync()
    {
        ClearStartVisits();
        IsCreating = false;
        return Task.CompletedTask;
    }

    public async Task SearchSystemsAsync()
    {
        string query = StartSystemQuery.Trim();
        int version = contextVersion;
        try
        {
            IsBusy = true;
            StartStatus = $"Searching for {StartSystemQuery.Trim()}\u2026";
            IReadOnlyList<StarSystemReference> results = await systemResolver.SearchAsync(query);
            if (version != contextVersion || !IsCreating || UseCurrentStart || query != StartSystemQuery.Trim())
            {
                return;
            }
            StartSystemResults = results.Select(system => new JourneyStartSystemViewModel(system)).ToArray();
            SelectedStartSystem =
                StartSystemResults.FirstOrDefault(system =>
                    string.Equals(system.Name, StartSystemQuery.Trim(), StringComparison.OrdinalIgnoreCase)
                ) ?? (StartSystemResults.Count > 0 ? StartSystemResults[0] : null);
            StartStatus =
                StartSystemResults.Count == 0
                    ? "No matching systems were found."
                    : $"Found {StartSystemResults.Count:N0} matching systems.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StartStatus = "The system search failed: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    private bool CanFindStart()
    {
        return IsCreating
            && frontierId is not null
            && !IsBusy
            && (UseCurrentStart ? currentSystemAddress is > 0 : SelectedStartSystem is not null);
    }

    public async Task BeginJourneyAsync()
    {
        JourneyJournalSystemEntry? start = SelectedStartVisit?.Entry;
        if (frontierId is null || start is null || !CanBeginJourney())
        {
            StartStatus = "Find visits and choose a starting arrival before beginning the journey.";
            return;
        }
        int version = contextVersion;
        try
        {
            IsBusy = true;
            JourneyServiceResult result = await journeyService.BeginAsync(
                new JourneyBeginRequest(
                    frontierId,
                    commanderName ?? string.Empty,
                    isOdyssey,
                    NewJourneyName,
                    NewJourneyDescription,
                    start
                )
            );
            if (version != contextVersion)
            {
                return;
            }
            IsCreating = false;
            ClearStartVisits();
            await RefreshCatalogAsync(result.Journey?.FileName);
            if (version != contextVersion)
            {
                return;
            }
            StatusMessage =
                result.Errors.Count > 0
                    ? string.Join(Environment.NewLine, result.Errors)
                    : $"Journey {result.Journey!.Name} is active and caught up.";
            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StartStatus = "The journey was not started: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    public async Task SaveAsync()
    {
        if (selectedDocument is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(JourneyName))
        {
            StatusMessage = "The journey name cannot be blank.";
            return;
        }

        (long Address, DateTimeOffset Arrived)? selectedIdentity = SelectedSystem is { } system
            ? (system.Visit.StarSystem.SystemAddress, system.Visit.Arrived)
            : ((long Address, DateTimeOffset Arrived)?)null;
        int version = contextVersion;
        try
        {
            IsBusy = true;
            JourneyDocument saved = await journeyService.SaveAsync(
                selectedDocument with
                {
                    Name = JourneyName,
                    Description = JourneyDescription,
                }
            );
            if (version != contextVersion)
            {
                return;
            }
            saved = await SaveSelectedSystemNotesAsync(saved, version);
            if (version != contextVersion)
            {
                return;
            }

            ApplyDocument(saved, preferredSystem: selectedIdentity);
            ClearJourneyDraft();
            UpdateCatalogDocument(saved);
            StatusMessage = $"Saved {saved.Name}.";
            RaiseDirtyState();
            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "Journey changes were not fully saved: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    private async Task<JourneyDocument> SaveSelectedSystemNotesAsync(JourneyDocument saved, int version)
    {
        if (
            SelectedSystem is not { } selected
            || string.Equals(SelectedSystemNotes, loadedSystemNotes, StringComparison.Ordinal)
        )
        {
            return saved;
        }
        await noteStore.SaveAsync(
            new SystemNoteContext(
                saved.FrontierId,
                saved.CommanderName,
                selected.Visit.StarSystem.Name,
                selected.Visit.StarSystem.SystemAddress,
                selected.Visit.StarSystem.Position
            ),
            SelectedSystemNotes
        );
        if (version != contextVersion)
        {
            return saved;
        }
        await journeyService.IncrementNoteCountAsync(selected.Visit.StarSystem.SystemAddress);
        if (version != contextVersion)
        {
            return saved;
        }
        loadedSystemNotes = SelectedSystemNotes;
        return journeyService.ActiveJourney?.FileName == saved.FileName ? journeyService.ActiveJourney : saved;
    }

    public Task DiscardAsync()
    {
        if (selectedDocument is not null)
        {
            ClearJourneyDraft();
            ApplyDocument(
                selectedDocument,
                preferredSystem: SelectedSystem is { } system
                    ? (system.Visit.StarSystem.SystemAddress, system.Visit.Arrived)
                    : null
            );
            StatusMessage = "Unsaved Journey changes were discarded.";
        }

        return Task.CompletedTask;
    }

    private bool CanConclude()
    {
        return selectedDocument is { IsActive: true }
            && journeyService.ActiveJourney?.FileName == selectedDocument.FileName
            && !IsDirty
            && !IsBusy;
    }

    private Task RequestConcludeAsync()
    {
        IsConcludePending = true;
        IsReprocessPending = false;
        return Task.CompletedTask;
    }

    private Task CancelConcludeAsync()
    {
        IsConcludePending = false;
        return Task.CompletedTask;
    }

    public async Task ConfirmConcludeAsync()
    {
        try
        {
            IsBusy = true;
            JourneyDocument? concluded = await journeyService.ConcludeActiveAsync(
                commanderName ?? selectedDocument?.CommanderName ?? string.Empty,
                DateTimeOffset.Now
            );
            IsConcludePending = false;
            if (concluded is not null)
            {
                ApplyDocument(concluded);
                UpdateCatalogDocument(concluded);
                StatusMessage = $"Concluded {concluded.Name}.";
            }

            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The journey was not concluded: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RequestReprocessAsync()
    {
        IsReprocessPending = true;
        IsConcludePending = false;
        return Task.CompletedTask;
    }

    private Task CancelReprocessAsync()
    {
        IsReprocessPending = false;
        return Task.CompletedTask;
    }

    public async Task ConfirmReprocessAsync()
    {
        if (selectedDocument is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            JourneyServiceResult result = await journeyService.ReprocessAsync(selectedDocument, isOdyssey);
            IsReprocessPending = false;
            if (result.Journey is not null)
            {
                ApplyDocument(result.Journey);
                UpdateCatalogDocument(result.Journey);
                StatusMessage =
                    result.Errors.Count > 0
                        ? string.Join(Environment.NewLine, result.Errors)
                        : $"Reprocessed {result.ProcessedEventCount:N0} journal events.";
            }

            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The Journey could not be reprocessed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDocument(
        JourneyDocument? document,
        bool preserveEdits = false,
        (long Address, DateTimeOffset Arrived)? preferredSystem = null
    )
    {
        string previousName = JourneyName;
        string previousDescription = JourneyDescription;
        string previousNotes = SelectedSystemNotes;
        preferredSystem ??= SelectedSystem is { } prior
            ? (prior.Visit.StarSystem.SystemAddress, prior.Visit.Arrived)
            : null;
        selectedDocument = document;
        isApplyingDocument = true;
        try
        {
            JourneyName = preserveEdits ? previousName : document?.Name ?? string.Empty;
            JourneyDescription = preserveEdits ? previousDescription : document?.Description ?? string.Empty;
            QuickStatistics = document is null ? [] : CreateStatistics(JourneyStatistics.Calculate(document));
            primaryStatistics = QuickStatistics.Take(4).ToArray();
            IReadOnlySet<long> systemsWithPhotos =
                document
                    ?.VisitedSystems.Where(visit => visit.Counts.Screenshots > 0)
                    .Select(visit => visit.StarSystem.SystemAddress)
                    .ToHashSet()
                ?? [];
            IReadOnlySet<long> systemsWithNotes =
                document
                    ?.VisitedSystems.Where(visit => visit.Counts.Notes > 0)
                    .Select(visit => visit.StarSystem.SystemAddress)
                    .ToHashSet()
                ?? [];
            VisitedSystems =
                document
                    ?.VisitedSystems.OrderByDescending(visit => visit.Arrived)
                    .Select(visit => new JourneySystemItemViewModel(
                        visit,
                        CreateInterestFlags(visit, systemsWithPhotos, systemsWithNotes),
                        FormatTime(visit.Arrived),
                        visit.Departed is { } departed ? FormatTime(departed) : "Current"
                    ))
                    .ToArray()
                ?? [];
            suppressSystemLoad = preserveEdits;
            try
            {
                JourneySystemItemViewModel? firstVisitedSystem = VisitedSystems.Count > 0 ? VisitedSystems[0] : null;
                SelectedSystem = preferredSystem is { } identity
                    ? VisitedSystems.FirstOrDefault(item =>
                        item.Visit.StarSystem.SystemAddress == identity.Address
                        && item.Visit.Arrived == identity.Arrived
                    ) ?? firstVisitedSystem
                    : firstVisitedSystem;
            }
            finally
            {
                suppressSystemLoad = false;
            }

            if (preserveEdits)
            {
                SelectedSystemNotes = previousNotes;
                SelectedSystemDetails = SelectedSystem is { } selected
                    ? CreateSystemDetails(selected.Visit)
                    : string.Empty;
            }
        }
        finally
        {
            isApplyingDocument = false;
        }

        IsDeletePending = false;
        OnPropertyChanged(nameof(HasSelectedJourney));
        OnPropertyChanged(nameof(PrimaryStatistics));
        OnPropertyChanged(nameof(SelectedJourneyState));
        OnPropertyChanged(nameof(HasVisitedSystems));
        OnPropertyChanged(nameof(RouteOrigin));
        OnPropertyChanged(nameof(RouteDestination));
        OnPropertyChanged(nameof(IsCompletedJourney));
        OnPropertyChanged(nameof(IsSelectedActiveJourney));
        OnPropertyChanged(nameof(ArchiveButtonText));
        OnPropertyChanged(nameof(JourneyByline));
        OnPropertyChanged(nameof(HasSelectedSystem));
        RaiseDirtyState();
        RaiseCommands();
    }

    private async Task LoadSelectedSystemAsync(JourneySystemItemViewModel? item)
    {
        int version = ++systemLoadVersion;
        if (item is null || selectedDocument is null)
        {
            isApplyingDocument = true;
            try
            {
                loadedSystemNotes = string.Empty;
                SelectedSystemNotes = string.Empty;
                SelectedSystemDetails = string.Empty;
                ScreenshotFiles = [];
                imagesDirectory = null;
            }
            finally
            {
                isApplyingDocument = false;
            }

            OnPropertyChanged(nameof(HasSelectedSystem));
            OnPropertyChanged(nameof(HasImagesDirectory));
            RaiseCommands();
            return;
        }

        try
        {
            JourneySystemVisit visit = item.Visit;
            SystemNoteLoadResult load = await noteStore.LoadAsync(
                selectedDocument.FrontierId,
                visit.StarSystem.Name,
                visit.StarSystem.SystemAddress
            );
            if (version != systemLoadVersion)
            {
                return;
            }

            isApplyingDocument = true;
            try
            {
                loadedSystemNotes = load.Notes ?? string.Empty;
                SelectedSystemNotes = loadedSystemNotes;
                SelectedSystemDetails = CreateSystemDetails(visit);
                RefreshScreenshotAvailability();
            }
            finally
            {
                isApplyingDocument = false;
            }

            OnPropertyChanged(nameof(HasSelectedSystem));
            OnPropertyChanged(nameof(HasImagesDirectory));
            RaiseDirtyState();
            RaiseCommands();
            if (!load.IsSuccess)
            {
                StatusMessage = load.Error ?? "System notes could not be loaded.";
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == systemLoadVersion)
            {
                StatusMessage = "System details could not be loaded: " + exception.Message;
            }
        }
    }

    private async Task OpenImagesAsync()
    {
        if (directoryLauncher is null || imagesDirectory is null)
        {
            return;
        }

        try
        {
            StatusMessage = await directoryLauncher(new DirectoryInfo(imagesDirectory))
                ? "Opened the system screenshot folder."
                : "The operating system could not open the screenshot folder.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The screenshot folder could not be opened: " + exception.Message;
        }
    }

    private void UpdateCatalogDocument(JourneyDocument document)
    {
        Journeys = Journeys
            .Select(item =>
                item.Document.FileName == document.FileName
                    ? new JourneyListItemViewModel(
                        document,
                        FormatTime(document.StartTime),
                        FormatTime(document.Watermark)
                    )
                    : item
            )
            .ToArray();
        selectedJourney = VisibleJourneys.FirstOrDefault(item => item.Document.FileName == selectedDocument?.FileName);
        OnPropertyChanged(nameof(SelectedJourney));
        OnPropertyChanged(nameof(HasJourneys));
    }

    private void RaiseActiveState()
    {
        OnPropertyChanged(nameof(HasActiveJourney));
        OnPropertyChanged(nameof(ActiveJourneyName));
        OnPropertyChanged(nameof(ActiveJourneySummary));
        RaiseCommands();
    }

    private void RaiseDirtyState()
    {
        OnPropertyChanged(nameof(IsDirty));
        refreshCommand.RaiseCanExecuteChanged();
        saveCommand.RaiseCanExecuteChanged();
        discardCommand.RaiseCanExecuteChanged();
        requestConcludeCommand.RaiseCanExecuteChanged();
        requestReprocessCommand.RaiseCanExecuteChanged();
        RaiseHistoryCommands();
    }

    private void RaiseCommands()
    {
        openWorkspaceCommand.RaiseCanExecuteChanged();
        refreshCommand.RaiseCanExecuteChanged();
        newJourneyCommand.RaiseCanExecuteChanged();
        cancelNewJourneyCommand.RaiseCanExecuteChanged();
        searchSystemsCommand.RaiseCanExecuteChanged();
        findStartCommand.RaiseCanExecuteChanged();
        beginJourneyCommand.RaiseCanExecuteChanged();
        saveCommand.RaiseCanExecuteChanged();
        discardCommand.RaiseCanExecuteChanged();
        requestConcludeCommand.RaiseCanExecuteChanged();
        confirmConcludeCommand.RaiseCanExecuteChanged();
        cancelConcludeCommand.RaiseCanExecuteChanged();
        requestReprocessCommand.RaiseCanExecuteChanged();
        confirmReprocessCommand.RaiseCanExecuteChanged();
        cancelReprocessCommand.RaiseCanExecuteChanged();
        openImagesCommand.RaiseCanExecuteChanged();
        RaiseHistoryCommands();
    }

    private string FormatTime(DateTimeOffset value)
    {
        return UseGalacticTime
            ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    }

    private string CreateSystemDetails(JourneySystemVisit visit)
    {
        int bodyCount = Math.Max(0, visit.Counts.BodyCount - visit.Counts.Stars);
        string departure = visit.Departed switch
        {
            { } departed => $"Departed {FormatTime(departed)}",
            _ when selectedDocument?.EndTime is { } ended => $"Journey ended here {FormatTime(ended)}",
            _ => "Currently in this system",
        };
        var lines = new List<string>
        {
            $"Arrived {FormatTime(visit.Arrived)}",
            departure,
            $"FSS {visit.Counts.BodyScans:N0} of {visit.Counts.BodyCount:N0} bodies; DSS {visit.Counts.DetailedSurfaceScans:N0} of {bodyCount:N0}",
            $"Exploration rewards ~{visit.Counts.ExplorationRewards:N0} CR",
            $"Biology {visit.Counts.Organisms:N0} analysed / base value {visit.Counts.ExobiologyRewards:N0} CR",
            $"Codex {visit.CodexScanned?.Count ?? 0:N0} scans / {visit.Counts.NewCodexEntries:N0} regional firsts",
            $"Touchdowns {visit.Counts.Touchdowns:N0}; screenshots {visit.Counts.Screenshots:N0}; notes {visit.Counts.Notes:N0}",
        };
        if (visit.SurfaceSignals is { Count: > 0 })
        {
            lines.Add(
                "Surface signals: "
                    + string.Join(", ", visit.SurfaceSignals.Select(signal => $"{signal.Key} {signal.Value:N0}"))
            );
        }

        if (visit.FssSignals is { Count: > 0 })
        {
            lines.Add(
                "FSS signals: "
                    + string.Join(", ", visit.FssSignals.Select(signal => $"{signal.Key} {signal.Value:N0}"))
            );
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<JourneyStatisticViewModel> CreateStatistics(JourneyQuickStatistics statistics)
    {
        var values = new List<JourneyStatisticViewModel>
        {
            new("FSD jumps", statistics.JumpCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Total distance", $"{statistics.TotalDistance:N1} ly"),
            new("Systems visited", statistics.UniqueSystemCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("FSS complete", statistics.FssCompletedSystemCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Bodies scanned", statistics.Counts.BodyScans.ToString("N0", CultureInfo.CurrentCulture)),
            new("Detailed scans", statistics.Counts.DetailedSurfaceScans.ToString("N0", CultureInfo.CurrentCulture)),
            new("Touchdowns", statistics.TotalLandingCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Screenshots", statistics.Counts.Screenshots.ToString("N0", CultureInfo.CurrentCulture)),
            new("Notes", statistics.Counts.Notes.ToString("N0", CultureInfo.CurrentCulture)),
            new("Organisms", statistics.Counts.Organisms.ToString("N0", CultureInfo.CurrentCulture)),
            new("Biology base value", $"{statistics.Counts.ExobiologyRewards:N0} CR"),
            new("Exploration rewards", $"~{statistics.Counts.ExplorationRewards:N0} CR"),
            new("Codex scans", statistics.CodexScanCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("New Codex", statistics.Counts.NewCodexEntries.ToString("N0", CultureInfo.CurrentCulture)),
            new("Stars", statistics.Counts.Stars.ToString("N0", CultureInfo.CurrentCulture)),
            new("Bodies catalogued", statistics.Counts.BodyCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Landing sites by visit", statistics.LandedBodyCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Journal touchdowns", statistics.Counts.Touchdowns.ToString("N0", CultureInfo.CurrentCulture)),
        };
        values.AddRange(
            statistics.SubCategoryCounts.Select(category => new JourneyStatisticViewModel(
                category.Key,
                category.Value.ToString("N0", CultureInfo.CurrentCulture)
            ))
        );
        return values;
    }

    private static string CreateInterestFlags(
        JourneySystemVisit visit,
        IReadOnlySet<long> systemsWithPhotos,
        IReadOnlySet<long> systemsWithNotes
    )
    {
        string flags = string.Empty;
        flags +=
            visit.Counts.Screenshots > 0
                ? "P"
                : (systemsWithPhotos.Contains(visit.StarSystem.SystemAddress)) switch
                {
                    true => "p",
                    false => string.Empty,
                };
        flags += visit.Counts.Organisms > 0 ? "B" : string.Empty;
        flags +=
            visit.Counts.Notes > 0
                ? "N"
                : (systemsWithNotes.Contains(visit.StarSystem.SystemAddress)) switch
                {
                    true => "n",
                    false => string.Empty,
                };
        flags += visit.Counts.NewCodexEntries > 0 ? "C" : string.Empty;
        flags += visit.Counts.Touchdowns > 0 ? "T" : string.Empty;
        return flags;
    }

    private static string[] GetScreenshotFiles(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception
            is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or HttpRequestException;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
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

public sealed record JourneyListItemViewModel(JourneyDocument Document, string Started, string Updated)
{
    public string Name => Document.Name;

    public string Description =>
        string.IsNullOrWhiteSpace(Document.Description) ? "No description" : Document.Description;

    public string State => Document.IsActive ? "Active" : "Complete";

    public string VisitSummary => $"{Document.VisitedSystems.Count:N0} recorded visits";
}

public sealed record JourneyStatisticViewModel(string Label, string Value);

public sealed record JourneySystemItemViewModel(
    JourneySystemVisit Visit,
    string InterestFlags,
    string Arrived,
    string Departed
)
{
    public string Name => Visit.StarSystem.Name;

    public string ActivitySummary =>
        string.Join(
            ", ",
            new[]
            {
                Visit.Counts.BodyScans > 0 ? $"{Visit.Counts.BodyScans:N0} scanned" : "",
                Visit.Counts.DetailedSurfaceScans > 0 ? $"{Visit.Counts.DetailedSurfaceScans:N0} mapped" : "",
                Visit.Counts.Organisms > 0 ? $"{Visit.Counts.Organisms:N0} organisms" : "",
                Visit.Counts.NewCodexEntries > 0 ? $"{Visit.Counts.NewCodexEntries:N0} new Codex" : "",
                ScreenshotSummary,
                NotesSummary,
                LandingSummary,
            }.Where(value => value.Length > 0)
        );

    private string ScreenshotSummary =>
        Visit.Counts.Screenshots switch
        {
            > 1 => $"{Visit.Counts.Screenshots:N0} screenshots",
            1 => "1 screenshot",
            _ when InterestFlags.Contains('p') => "Screenshots on another visit",
            _ => "",
        };

    private string NotesSummary =>
        Visit.Counts.Notes switch
        {
            > 1 => $"{Visit.Counts.Notes:N0} notes",
            1 => "1 note",
            _ when InterestFlags.Contains('n') => "Notes on another visit",
            _ => "",
        };

    private string LandingSummary =>
        Visit.Counts.Touchdowns switch
        {
            > 1 => $"{Visit.Counts.Touchdowns:N0} landings",
            1 => "1 landing",
            _ => "",
        };

    public string Address => Visit.StarSystem.SystemAddress.ToString(CultureInfo.InvariantCulture);
}

public sealed record JourneyStartSystemViewModel(StarSystemReference System)
{
    public string Name => System.Name;

    public long SystemAddress => System.SystemAddress;

    public string DisplayName => $"{Name} ({SystemAddress})";
}
