using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JourneyWorkspaceViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";

    private readonly JourneyService journeyService;
    private readonly IStarSystemResolver systemResolver;
    private readonly SystemNoteStore noteStore;
    private readonly SystemNotesSettingsStore settingsStore;
    private readonly AsyncCommand openWindowCommand;
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
    private bool suppressSystemLoad;
    private int systemLoadVersion;
    private bool isCreating;
    private string newJourneyName = string.Empty;
    private string newJourneyDescription = string.Empty;
    private bool useCurrentStart = true;
    private string startSystemQuery = string.Empty;
    private IReadOnlyList<JourneyStartSystemViewModel> startSystemResults = [];
    private JourneyStartSystemViewModel? selectedStartSystem;
    private JourneyJournalSystemEntry? startingEntry;
    private string startStatus = "Choose where this journey should begin.";
    private bool isConcludePending;
    private bool isReprocessPending;
    private bool alwaysOnTop;
    private bool useGalacticTime;
    private Func<Task<bool>>? windowOpener;
    private Func<DirectoryInfo, Task<bool>>? directoryLauncher;

    public JourneyWorkspaceViewModel(
        JourneyService journeyService,
        IStarSystemResolver systemResolver,
        SystemNoteStore noteStore,
        SystemNotesSettingsStore settingsStore)
    {
        this.journeyService = journeyService
            ?? throw new ArgumentNullException(nameof(journeyService));
        this.systemResolver = systemResolver
            ?? throw new ArgumentNullException(nameof(systemResolver));
        this.noteStore = noteStore
            ?? throw new ArgumentNullException(nameof(noteStore));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        var settings = settingsStore.Load();
        alwaysOnTop = settings.Snapshot?.JourneyAlwaysOnTop ?? false;
        useGalacticTime = settings.Snapshot?.JourneyUseGalacticTime ?? false;
        if (!settings.IsSuccess)
        {
            statusMessage = settings.Error
                ?? "The Journey window preferences could not be loaded.";
        }

        openWindowCommand = new AsyncCommand(OpenWindowAsync, CanOpenWindow);
        refreshCommand = new AsyncCommand(RefreshAsync, HasProfileAndNotBusy);
        newJourneyCommand = new AsyncCommand(
            StartNewJourneyAsync,
            () => HasProfileAndNotBusy() && !HasActiveJourney);
        cancelNewJourneyCommand = new AsyncCommand(
            CancelNewJourneyAsync,
            () => IsCreating && !IsBusy);
        searchSystemsCommand = new AsyncCommand(
            SearchSystemsAsync,
            () => IsCreating
                && !UseCurrentStart
                && !string.IsNullOrWhiteSpace(StartSystemQuery)
                && !IsBusy);
        findStartCommand = new AsyncCommand(
            FindStartAsync,
            CanFindStart);
        beginJourneyCommand = new AsyncCommand(
            BeginJourneyAsync,
            () => IsCreating
                && !HasActiveJourney
                && !string.IsNullOrWhiteSpace(NewJourneyName)
                && CanFindStart());
        saveCommand = new AsyncCommand(SaveAsync, () => IsDirty && !IsBusy);
        discardCommand = new AsyncCommand(
            DiscardAsync,
            () => IsDirty && !IsBusy);
        requestConcludeCommand = new AsyncCommand(
            RequestConcludeAsync,
            CanConclude);
        confirmConcludeCommand = new AsyncCommand(
            ConfirmConcludeAsync,
            () => IsConcludePending && CanConclude());
        cancelConcludeCommand = new AsyncCommand(
            CancelConcludeAsync,
            () => IsConcludePending && !IsBusy);
        requestReprocessCommand = new AsyncCommand(
            RequestReprocessAsync,
            () => selectedDocument is not null && !IsDirty && !IsBusy);
        confirmReprocessCommand = new AsyncCommand(
            ConfirmReprocessAsync,
            () => IsReprocessPending && selectedDocument is not null && !IsBusy);
        cancelReprocessCommand = new AsyncCommand(
            CancelReprocessAsync,
            () => IsReprocessPending && !IsBusy);
        openImagesCommand = new AsyncCommand(
            OpenImagesAsync,
            () => HasImagesDirectory && directoryLauncher is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasProfile => !string.IsNullOrWhiteSpace(frontierId);

    public bool HasActiveJourney => journeyService.ActiveJourney is not null;

    public string ActiveJourneyName => journeyService.ActiveJourney?.Name
        ?? "No active journey";

    public string ActiveJourneySummary => journeyService.ActiveJourney is { } active
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
        private set => SetField(ref journeys, value);
    }

    public bool HasJourneys => Journeys.Count > 0;

    public JourneyListItemViewModel? SelectedJourney
    {
        get => selectedJourney;
        set
        {
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
            if (SetField(ref journeyName, value ?? string.Empty)
                && !isApplyingDocument)
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
            if (SetField(ref journeyDescription, value ?? string.Empty)
                && !isApplyingDocument)
            {
                RaiseDirtyState();
            }
        }
    }

    public string JourneyByline => selectedDocument is null
        ? Unavailable
        : $"CMDR {selectedDocument.CommanderName} \u2022 "
            + $"set out {FormatTime(selectedDocument.StartTime)}"
            + (selectedDocument.EndTime switch
            {
                DateTimeOffset end => $" \u2022 concluded {FormatTime(end)}",
                null => " \u2022 active"
            });

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
            if (!SetField(ref selectedSystem, value))
            {
                return;
            }

            if (!suppressSystemLoad)
            {
                _ = LoadSelectedSystemAsync(value);
            }

            OnPropertyChanged(nameof(SelectedSystemName));
            OnPropertyChanged(nameof(SelectedSystemAddressText));
        }
    }

    public bool HasSelectedSystem => SelectedSystem is not null;

    public string SelectedSystemName => SelectedSystem?.Name ?? string.Empty;

    public string SelectedSystemAddressText => SelectedSystem is null
        ? string.Empty
        : $"System address {SelectedSystem.Address}";

    public string SelectedSystemNotes
    {
        get => selectedSystemNotes;
        set
        {
            if (SetField(ref selectedSystemNotes, value ?? string.Empty)
                && !isApplyingDocument)
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

    public bool HasImagesDirectory => !string.IsNullOrWhiteSpace(imagesDirectory)
        && Directory.Exists(imagesDirectory);

    public bool IsDirty => selectedDocument is not null
        && (!string.Equals(
                JourneyName,
                selectedDocument.Name,
                StringComparison.Ordinal)
            || !string.Equals(
                JourneyDescription,
                selectedDocument.Description,
                StringComparison.Ordinal)
            || !string.Equals(
                SelectedSystemNotes,
                loadedSystemNotes,
                StringComparison.Ordinal));

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

            startingEntry = null;
            OnPropertyChanged(nameof(UsePriorStart));
            StartStatus = value
                ? $"Use the most recent visit to {currentSystemName ?? "the current system"}."
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

    public string CurrentStartSystem => currentSystemAddress is > 0
        ? $"{currentSystemName ?? "Current system"} ({currentSystemAddress})"
        : "No current journal system";

    public string StartSystemQuery
    {
        get => startSystemQuery;
        set
        {
            if (SetField(ref startSystemQuery, value ?? string.Empty))
            {
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

            startingEntry = null;
            StartStatus = value is null
                ? "Select a system from the search results."
                : $"Find the last recorded FSD jump into {value.Name}.";
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

    public ICommand OpenWindowCommand => openWindowCommand;

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

    public void SetWindowOpener(Func<Task<bool>>? opener)
    {
        windowOpener = opener;
        openWindowCommand.RaiseCanExecuteChanged();
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
        long? nextSystemAddress)
    {
        var normalizedFrontierId = string.IsNullOrWhiteSpace(nextFrontierId)
            ? null
            : nextFrontierId;
        if (string.Equals(
                frontierId,
                normalizedFrontierId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                commanderName,
                nextCommanderName,
                StringComparison.Ordinal)
            && isOdyssey == nextIsOdyssey
            && string.Equals(
                currentSystemName,
                nextSystemName,
                StringComparison.Ordinal)
            && currentSystemAddress == nextSystemAddress)
        {
            return false;
        }

        frontierId = normalizedFrontierId;
        commanderName = nextCommanderName;
        isOdyssey = nextIsOdyssey;
        currentSystemName = nextSystemName;
        currentSystemAddress = nextSystemAddress;
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(CurrentStartSystem));

        var profileKey = frontierId is null
            ? null
            : $"{frontierId}|{isOdyssey}";
        if (string.Equals(
                initializedProfileKey,
                profileKey,
                StringComparison.OrdinalIgnoreCase))
        {
            RaiseCommands();
            return false;
        }

        initializedProfileKey = profileKey;
        if (frontierId is null)
        {
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
            var active = await journeyService.InitializeActiveAsync(
                frontierId,
                isOdyssey);
            await RefreshCatalogAsync(active.Journey?.FileName);
            StatusMessage = active.Errors.Count > 0
                ? string.Join(Environment.NewLine, active.Errors)
                : active.Journey is null
                    ? "No active journey. Browse history or begin a new expedition."
                    : active.ProcessedEventCount > 0
                        ? $"Caught up {active.ProcessedEventCount:N0} Journey journal events."
                        : $"Active journey: {active.Journey.Name}.";
            RaiseActiveState();
            return true;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Journey data could not be initialized: "
                + exception.Message;
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        if (journalEvents.Count == 0 || journeyService.ActiveJourney is null)
        {
            return;
        }

        try
        {
            var result = await journeyService.ApplyLiveAsync(journalEvents);
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
            StatusMessage = "The live Journey update was not saved: "
                + exception.Message;
        }
    }

    public Task RefreshAsync()
    {
        return RefreshCatalogAsync(selectedDocument?.FileName);
    }

    public async Task SetPreferencesAsync(
        bool nextAlwaysOnTop,
        bool nextUseGalacticTime)
    {
        var previousAlwaysOnTop = AlwaysOnTop;
        var previousUseGalacticTime = UseGalacticTime;
        AlwaysOnTop = nextAlwaysOnTop;
        UseGalacticTime = nextUseGalacticTime;
        try
        {
            await settingsStore.SaveJourneyPreferencesAsync(
                nextAlwaysOnTop,
                nextUseGalacticTime);
            if (selectedDocument is not null)
            {
                ApplyDocument(selectedDocument, preserveEdits: IsDirty);
            }

            OnPropertyChanged(nameof(ActiveJourneySummary));
            StatusMessage = "Journey window preferences saved.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AlwaysOnTop = previousAlwaysOnTop;
            UseGalacticTime = previousUseGalacticTime;
            StatusMessage = "Journey window preferences were not saved: "
                + exception.Message;
        }
    }

    private bool HasProfileAndNotBusy()
    {
        return HasProfile && !IsBusy;
    }

    private bool CanOpenWindow()
    {
        return HasProfile && windowOpener is not null && !IsBusy;
    }

    private async Task OpenWindowAsync()
    {
        if (windowOpener is null || !await windowOpener())
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

        try
        {
            IsBusy = true;
            var result = await journeyService.LoadAllAsync(frontierId);
            Journeys = result.Journeys
                .Select(journey => new JourneyListItemViewModel(
                    journey,
                    FormatTime(journey.StartTime),
                    FormatTime(journey.Watermark)))
                .ToArray();
            OnPropertyChanged(nameof(HasJourneys));
            SelectedJourney = Journeys.FirstOrDefault(item => string.Equals(
                    item.Document.FileName,
                    preferredFileName,
                    StringComparison.Ordinal))
                ?? Journeys.FirstOrDefault(item => item.Document.IsActive)
                ?? (Journeys.Count > 0 ? Journeys[0] : null);
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
                        false => $"Loaded journey: {SelectedJourney.Document.Name}."
                    };
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Journey history could not be loaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
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
        startingEntry = null;
        StartStatus = currentSystemAddress is > 0
            ? $"Find the most recent FSD jump into {currentSystemName}."
            : "A current journal system is required, or search for a prior system.";
        return Task.CompletedTask;
    }

    private Task CancelNewJourneyAsync()
    {
        IsCreating = false;
        return Task.CompletedTask;
    }

    public async Task SearchSystemsAsync()
    {
        try
        {
            IsBusy = true;
            StartStatus = $"Searching for {StartSystemQuery.Trim()}\u2026";
            var results = await systemResolver.SearchAsync(StartSystemQuery.Trim());
            StartSystemResults = results
                .Select(system => new JourneyStartSystemViewModel(system))
                .ToArray();
            SelectedStartSystem = StartSystemResults.FirstOrDefault(system =>
                    string.Equals(
                        system.Name,
                        StartSystemQuery.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                ?? (StartSystemResults.Count > 0
                    ? StartSystemResults[0]
                    : null);
            StartStatus = StartSystemResults.Count == 0
                ? "No matching systems were found."
                : $"Found {StartSystemResults.Count:N0} matching systems.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StartStatus = "The system search failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanFindStart()
    {
        return IsCreating
            && frontierId is not null
            && !IsBusy
            && (UseCurrentStart
                ? currentSystemAddress is > 0
                : SelectedStartSystem is not null);
    }

    public async Task FindStartAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        var address = UseCurrentStart
            ? currentSystemAddress
            : SelectedStartSystem?.SystemAddress;
        var name = UseCurrentStart
            ? currentSystemName
            : SelectedStartSystem?.Name;
        if (address is not > 0)
        {
            StartStatus = "Choose a valid starting system.";
            return;
        }

        try
        {
            IsBusy = true;
            StartStatus = $"Reading journals for the last visit to {name}\u2026";
            var result = await journeyService.FindLatestStartAsync(
                frontierId,
                isOdyssey,
                address.Value);
            startingEntry = result.Entry;
            StartStatus = result.Entry is null
                ? $"No FSD jump into {name} was found in this commander's journals."
                : $"Last arrived in {result.Entry.System.Name} on "
                    + $"{FormatTime(result.Entry.Event.Timestamp!.Value)} "
                    + $"({result.Entry.JournalFileName}).";
            if (result.Errors.Count > 0)
            {
                StartStatus += $" Ignored {result.Errors.Count:N0} malformed journal line(s).";
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            startingEntry = null;
            StartStatus = "The starting visit could not be found: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
            beginJourneyCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task BeginJourneyAsync()
    {
        if (frontierId is null || string.IsNullOrWhiteSpace(NewJourneyName))
        {
            return;
        }

        if (startingEntry is null)
        {
            await FindStartAsync();
        }

        if (startingEntry is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await journeyService.BeginAsync(new JourneyBeginRequest(
                frontierId,
                commanderName ?? string.Empty,
                isOdyssey,
                NewJourneyName,
                NewJourneyDescription,
                startingEntry));
            IsCreating = false;
            await RefreshCatalogAsync(result.Journey?.FileName);
            StatusMessage = result.Errors.Count > 0
                ? string.Join(Environment.NewLine, result.Errors)
                : $"Journey {result.Journey!.Name} is active and caught up.";
            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StartStatus = "The journey was not started: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
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

        var selectedIdentity = SelectedSystem is { } system
            ? (system.Visit.StarSystem.SystemAddress, system.Visit.Arrived)
            : ((long Address, DateTimeOffset Arrived)?)null;
        try
        {
            IsBusy = true;
            var saved = await journeyService.SaveAsync(selectedDocument with
            {
                Name = JourneyName,
                Description = JourneyDescription,
            });
            if (SelectedSystem is { } selected
                && !string.Equals(
                    SelectedSystemNotes,
                    loadedSystemNotes,
                    StringComparison.Ordinal))
            {
                await noteStore.SaveAsync(
                    new SystemNoteContext(
                        saved.FrontierId,
                        saved.CommanderName,
                        selected.Visit.StarSystem.Name,
                        selected.Visit.StarSystem.SystemAddress,
                        selected.Visit.StarSystem.Position),
                    SelectedSystemNotes);
                await journeyService.IncrementNoteCountAsync(
                    selected.Visit.StarSystem.SystemAddress);
                loadedSystemNotes = SelectedSystemNotes;
                if (journeyService.ActiveJourney?.FileName == saved.FileName)
                {
                    saved = journeyService.ActiveJourney;
                }
            }

            ApplyDocument(saved, preferredSystem: selectedIdentity);
            UpdateCatalogDocument(saved);
            StatusMessage = $"Saved {saved.Name}.";
            RaiseDirtyState();
            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Journey changes were not fully saved: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task DiscardAsync()
    {
        if (selectedDocument is not null)
        {
            ApplyDocument(selectedDocument, preferredSystem: SelectedSystem is { } system
                ? (system.Visit.StarSystem.SystemAddress, system.Visit.Arrived)
                : null);
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
            var concluded = await journeyService.ConcludeActiveAsync(
                commanderName ?? selectedDocument?.CommanderName ?? string.Empty,
                DateTimeOffset.Now);
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
            var result = await journeyService.ReprocessAsync(
                selectedDocument,
                isOdyssey);
            IsReprocessPending = false;
            if (result.Journey is not null)
            {
                ApplyDocument(result.Journey);
                UpdateCatalogDocument(result.Journey);
                StatusMessage = result.Errors.Count > 0
                    ? string.Join(Environment.NewLine, result.Errors)
                    : $"Reprocessed {result.ProcessedEventCount:N0} journal events.";
            }

            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The Journey could not be reprocessed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDocument(
        JourneyDocument? document,
        bool preserveEdits = false,
        (long Address, DateTimeOffset Arrived)? preferredSystem = null)
    {
        var previousName = JourneyName;
        var previousDescription = JourneyDescription;
        var previousNotes = SelectedSystemNotes;
        preferredSystem ??= SelectedSystem is { } prior
            ? (prior.Visit.StarSystem.SystemAddress, prior.Visit.Arrived)
            : null;
        selectedDocument = document;
        isApplyingDocument = true;
        try
        {
            JourneyName = preserveEdits ? previousName : document?.Name ?? string.Empty;
            JourneyDescription = preserveEdits
                ? previousDescription
                : document?.Description ?? string.Empty;
            QuickStatistics = document is null
                ? []
                : CreateStatistics(JourneyStatistics.Calculate(document));
            VisitedSystems = document?.VisitedSystems
                .OrderByDescending(visit => visit.Arrived)
                .Select(visit => new JourneySystemItemViewModel(
                    visit,
                    CreateInterestFlags(document, visit),
                    FormatTime(visit.Arrived),
                    visit.Departed is { } departed
                        ? FormatTime(departed)
                        : "Current"))
                .ToArray()
                ?? [];
            suppressSystemLoad = preserveEdits;
            try
            {
                var firstVisitedSystem = VisitedSystems.Count > 0
                    ? VisitedSystems[0]
                    : null;
                SelectedSystem = preferredSystem is { } identity
                    ? VisitedSystems.FirstOrDefault(item =>
                        item.Visit.StarSystem.SystemAddress == identity.Address
                        && item.Visit.Arrived == identity.Arrived)
                        ?? firstVisitedSystem
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

        OnPropertyChanged(nameof(HasSelectedJourney));
        OnPropertyChanged(nameof(JourneyByline));
        OnPropertyChanged(nameof(HasSelectedSystem));
        RaiseDirtyState();
        RaiseCommands();
    }

    private async Task LoadSelectedSystemAsync(JourneySystemItemViewModel? item)
    {
        var version = ++systemLoadVersion;
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
            var visit = item.Visit;
            var load = await noteStore.LoadAsync(
                selectedDocument.FrontierId,
                visit.StarSystem.Name,
                visit.StarSystem.SystemAddress);
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
                imagesDirectory = settingsStore.GetImagesDirectory(
                    visit.StarSystem.Name);
                ScreenshotFiles = GetScreenshotFiles(imagesDirectory);
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
                StatusMessage = "System details could not be loaded: "
                    + exception.Message;
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
            StatusMessage = "The screenshot folder could not be opened: "
                + exception.Message;
        }
    }

    private void UpdateCatalogDocument(JourneyDocument document)
    {
        Journeys = Journeys
            .Select(item => item.Document.FileName == document.FileName
                ? new JourneyListItemViewModel(
                    document,
                    FormatTime(document.StartTime),
                    FormatTime(document.Watermark))
                : item)
            .ToArray();
        selectedJourney = Journeys.FirstOrDefault(item =>
            item.Document.FileName == document.FileName);
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
        saveCommand.RaiseCanExecuteChanged();
        discardCommand.RaiseCanExecuteChanged();
        requestConcludeCommand.RaiseCanExecuteChanged();
        requestReprocessCommand.RaiseCanExecuteChanged();
    }

    private void RaiseCommands()
    {
        openWindowCommand.RaiseCanExecuteChanged();
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
    }

    private string FormatTime(DateTimeOffset value)
    {
        return UseGalacticTime
            ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'")
            : value.LocalDateTime.ToString("g");
    }

    private string CreateSystemDetails(JourneySystemVisit visit)
    {
        var bodyCount = Math.Max(0, visit.Counts.BodyCount - visit.Counts.Stars);
        var lines = new List<string>
        {
            $"Arrived {FormatTime(visit.Arrived)}",
            visit.Departed is { } departed
                ? $"Departed {FormatTime(departed)}"
                : "Currently in this system",
            $"FSS {visit.Counts.BodyScans:N0} of {visit.Counts.BodyCount:N0} bodies; DSS {visit.Counts.DetailedSurfaceScans:N0} of {bodyCount:N0}",
            $"Exploration rewards ~{visit.Counts.ExplorationRewards:N0} CR",
            $"Biology {visit.Counts.Organisms:N0} scans / {visit.Counts.ExobiologyRewards:N0} CR",
            $"Codex {visit.CodexScanned?.Count ?? 0:N0} scans / {visit.Counts.NewCodexEntries:N0} regional firsts",
            $"Touchdowns {visit.Counts.Touchdowns:N0}; screenshots {visit.Counts.Screenshots:N0}; notes {visit.Counts.Notes:N0}",
        };
        if (visit.SurfaceSignals is { Count: > 0 })
        {
            lines.Add("Surface signals: " + string.Join(
                ", ",
                visit.SurfaceSignals.Select(signal =>
                    $"{signal.Key} {signal.Value:N0}")));
        }

        if (visit.FssSignals is { Count: > 0 })
        {
            lines.Add("FSS signals: " + string.Join(
                ", ",
                visit.FssSignals.Select(signal =>
                    $"{signal.Key} {signal.Value:N0}")));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<JourneyStatisticViewModel> CreateStatistics(
        JourneyQuickStatistics statistics)
    {
        var values = new List<JourneyStatisticViewModel>
        {
            new("FSD jumps", statistics.JumpCount.ToString("N0")),
            new("Total distance", $"{statistics.TotalDistance:N1} ly"),
            new("Systems visited", statistics.UniqueSystemCount.ToString("N0")),
            new("FSS complete", statistics.FssCompletedSystemCount.ToString("N0")),
            new("Bodies scanned", statistics.Counts.BodyScans.ToString("N0")),
            new("Detailed scans", statistics.Counts.DetailedSurfaceScans.ToString("N0")),
            new("Touchdowns", statistics.TotalLandingCount.ToString("N0")),
            new("Screenshots", statistics.Counts.Screenshots.ToString("N0")),
            new("Notes", statistics.Counts.Notes.ToString("N0")),
            new("Organisms", statistics.Counts.Organisms.ToString("N0")),
            new("Bio rewards", $"{statistics.Counts.ExobiologyRewards:N0} CR"),
            new("Exploration rewards", $"~{statistics.Counts.ExplorationRewards:N0} CR"),
            new("Codex scans", statistics.CodexScanCount.ToString("N0")),
            new("New Codex", statistics.Counts.NewCodexEntries.ToString("N0")),
        };
        values.AddRange(statistics.SubCategoryCounts.Select(category =>
            new JourneyStatisticViewModel(
                category.Key,
                category.Value.ToString("N0"))));
        return values;
    }

    private static string CreateInterestFlags(
        JourneyDocument journey,
        JourneySystemVisit visit)
    {
        var sameNameVisits = journey.VisitedSystems.Where(candidate =>
            string.Equals(
                candidate.StarSystem.Name,
                visit.StarSystem.Name,
                StringComparison.Ordinal));
        var flags = string.Empty;
        flags += visit.Counts.Screenshots > 0
            ? "P"
            : (sameNameVisits.Any(candidate => candidate.Counts.Screenshots > 0)) switch
            {
                true => "p",
                false => string.Empty
            };
        flags += visit.Counts.Organisms > 0 ? "B" : string.Empty;
        flags += visit.Counts.Notes > 0
            ? "N"
            : (sameNameVisits.Any(candidate => candidate.Counts.Notes > 0)) switch
            {
                true => "n",
                false => string.Empty
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
            return Directory.EnumerateFiles(
                    directory,
                    "*.png",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or HttpRequestException;
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

public sealed record JourneyListItemViewModel(
    JourneyDocument Document,
    string Started,
    string Updated)
{
    public string Name => Document.Name;

    public string Description => string.IsNullOrWhiteSpace(Document.Description)
        ? "No description"
        : Document.Description;

    public string State => Document.IsActive ? "ACTIVE" : "COMPLETE";
}

public sealed record JourneyStatisticViewModel(string Label, string Value);

public sealed record JourneySystemItemViewModel(
    JourneySystemVisit Visit,
    string InterestFlags,
    string Arrived,
    string Departed)
{
    public string Name => Visit.StarSystem.Name;

    public string Address => Visit.StarSystem.SystemAddress.ToString();
}

public sealed record JourneyStartSystemViewModel(StarSystemReference System)
{
    public string Name => System.Name;

    public long SystemAddress => System.SystemAddress;

    public string DisplayName => $"{Name} ({SystemAddress})";
}
