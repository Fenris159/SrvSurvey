using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The view model is application-scoped; its background workers own their cancellation sources.")]
public sealed class BoxelSearchViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";
    private const int SystemsPerPage = 10;
    private const int MaximumLastSystemAvailable = 99_999;
    private const int LargeAuditConfirmationThreshold = 1_000;

    private readonly CommanderProfileStore profileStore;
    private readonly LegacySystemDataReader localSystemReader;
    private readonly EmptyBoxelStore emptyBoxelStore;
    private readonly SavedBoxelSearchStore savedSearchStore;
    private readonly IBoxelSystemResolver systemResolver;
    private readonly ISystemNameSuggestionClient? systemNameSuggestionClient;
    private readonly TimeSpan systemSuggestionDelay;
    private readonly KnownSystemAddressCatalog knownSystems;
    private readonly BoxelCompletionAuditor completionAuditor;
    private readonly BoxelSurveyStatsCoordinator? surveyStats;
    private readonly BoxelSearchState state = new();
    private readonly Dictionary<string, BoxelNavigationOptionViewModel>
        navigationOptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly AsyncCommand activateCommand;
    private readonly AsyncCommand disableCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand copyNextCommand;
    private readonly AsyncCommand markNextEmptyCommand;
    private readonly AsyncCommand applyLastSystemAvailableCommand;
    private readonly AsyncCommand nextJumpPageCommand;
    private readonly AsyncCommand previousSystemPageCommand;
    private readonly AsyncCommand nextSystemPageCommand;
    private readonly AsyncCommand navigateParentCommand;
    private readonly AsyncCommand navigatePreviousCommand;
    private readonly AsyncCommand navigateNextCommand;
    private readonly AsyncCommand auditAllCommand;
    private readonly AsyncCommand cancelAuditCommand;
    private string topBoxelText = string.Empty;
    private IReadOnlyList<SystemNameSuggestion> systemNameSuggestions = [];
    private int selectedSystemSuggestionIndex = -1;
    private bool isSearchingSystemSuggestions;
    private string systemSuggestionStatus = string.Empty;
    private long selectedSystemAddress;
    private string? selectedSystemName;
    private CancellationTokenSource? systemSuggestionCancellation;
    private string lowMassCode = "c";
    private DateTimeOffset startedOn = new(DateTime.Today);
    private bool skipAlreadyVisited;
    private bool skipKnownToSpansh;
    private bool completeOnFssAllBodies;
    private bool autoCopy;
    private bool sortDescending;
    private bool showOnlyDeferred;
    private bool suppressOptionPersistence;
    private bool isBusy;
    private bool isAuditing;
    private bool confirmLargeAudit;
    private string statusMessage = "Waiting for a commander profile.";
    private string currentBoxelName = Unavailable;
    private string currentBoxelDescription = string.Empty;
    private string nextSystem = Unavailable;
    private string lastSystemAvailable = "0";
    private bool hasUnappliedLastSystemAvailableEdit;
    private string systemProgress = "0 of 0 complete";
    private string boxelProgress = "0 of 0 boxels complete";
    private string searchSize = "Enter a generated system name.";
    private string currentSystemName = Unavailable;
    private long? currentSystemAddress;
    private string systemListNote = string.Empty;
    private string systemPageText = "Page 1 of 1";
    private IReadOnlyList<int> systemPageNumbers = [1];
    private int[] orderedSystemNumbers = [];
    private Dictionary<int, int> systemNumberPositions = [];
    private int currentDeferredSystemCount;
    private int systemPageIndex;
    private string? systemPagePrefix;
    private string? systemPageTarget;
    private int? systemPageTargetIndex;
    private bool showNextSystemPageOnUpdate;
    private string auditDescription = "Activate a boxel search to audit its full area.";
    private string auditProgress = "No full-area audit has run in this session.";
    private int auditProcessed;
    private int auditTotal = 1;
    private GalacticCoordinate? currentPosition;
    private IReadOnlyList<BoxelSystemRowViewModel> systems = [];
    private IReadOnlyList<BoxelNavigationOptionViewModel> childBoxels = [];
    private IReadOnlyList<BoxelNavigationOptionViewModel> breadcrumbBoxels = [];
    private BoxelNavigationOptionViewModel? currentHierarchyBoxel;
    private BoxelNavigationOptionViewModel? parentBoxel;
    private BoxelNavigationOptionViewModel? previousSiblingBoxel;
    private BoxelNavigationOptionViewModel? nextSiblingBoxel;
    private string siblingPosition = "Search root";
    private string? frontierId;
    private IReadOnlyList<string> searchPrefixes = [];
    private bool surveyStatsUnsubscribed;
    private string? commanderName;
    private bool isOdyssey = true;
    private NavRouteSnapshot? latestRoute;
    private EliteStatus? status;
    private string? musicTrack;
    private StatusDestination? lastDestination;
    private string destinationStatus = "No Galaxy Map destination selected";
    private bool isDestinationValid;
    private string? lastCopiedSystemName;
    private Func<string, Task>? clipboardWriter;
    private CancellationTokenSource? auditCancellation;
    private string statsGlanceText = string.Empty;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "S107:Methods should not have too many parameters",
        Justification = "The constructor composes independent optional services; grouping them would only move the same dependencies into a parameter object.")]
    public BoxelSearchViewModel(
        CommanderProfileStore profileStore,
        LegacySystemDataReader localSystemReader,
        EmptyBoxelStore emptyBoxelStore,
        IBoxelSystemResolver systemResolver,
        Func<string, Task>? clipboardWriter = null,
        KnownSystemAddressCatalog? knownSystems = null,
        SavedBoxelSearchStore? savedSearchStore = null,
        ISystemNameSuggestionClient? systemNameSuggestionClient = null,
        TimeSpan? systemSuggestionDelay = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        this.profileStore = profileStore
            ?? throw new ArgumentNullException(nameof(profileStore));
        this.localSystemReader = localSystemReader
            ?? throw new ArgumentNullException(nameof(localSystemReader));
        this.emptyBoxelStore = emptyBoxelStore
            ?? throw new ArgumentNullException(nameof(emptyBoxelStore));
        this.systemResolver = systemResolver
            ?? throw new ArgumentNullException(nameof(systemResolver));
        this.surveyStats = surveyStats;
        if (this.surveyStats is not null)
        {
            this.surveyStats.Changed += OnSurveyStatsChanged;
        }

        this.systemNameSuggestionClient = systemNameSuggestionClient;
        this.systemSuggestionDelay = systemSuggestionDelay
            ?? TimeSpan.FromMilliseconds(450);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            this.systemSuggestionDelay,
            TimeSpan.Zero);
        this.savedSearchStore = savedSearchStore
            ?? new SavedBoxelSearchStore(this.profileStore.ProfileDirectory);
        this.knownSystems = knownSystems
            ?? KnownSystemAddressCatalog.Empty;
        completionAuditor = new BoxelCompletionAuditor(
            this.localSystemReader,
            this.systemResolver);
        this.clipboardWriter = clipboardWriter;
        activateCommand = new AsyncCommand(ActivateAsync, CanActivate);
        ActivateCommand = activateCommand;
        disableCommand = new AsyncCommand(DisableAsync, CanDisable);
        DisableCommand = disableCommand;
        refreshCommand = new AsyncCommand(RefreshCurrentAsync, CanUseActiveSearch);
        RefreshCommand = refreshCommand;
        copyNextCommand = new AsyncCommand(CopyNextSystemAsync, CanCopyNext);
        CopyNextCommand = copyNextCommand;
        markNextEmptyCommand = new AsyncCommand(
            MarkNextEmptyAsync,
            CanUseActiveSearch);
        MarkNextEmptyCommand = markNextEmptyCommand;
        applyLastSystemAvailableCommand = new AsyncCommand(
            ApplyLastSystemAvailableAsync,
            CanApplyLastSystemAvailable);
        ApplyLastSystemAvailableCommand = applyLastSystemAvailableCommand;
        nextJumpPageCommand = new AsyncCommand(
            ShowNextJumpPageAsync,
            CanShowNextJumpPage);
        NextJumpPageCommand = nextJumpPageCommand;
        previousSystemPageCommand = new AsyncCommand(
            () => ChangeSystemPageAsync(-1),
            () => !IsBusy && systemPageIndex > 0);
        PreviousSystemPageCommand = previousSystemPageCommand;
        nextSystemPageCommand = new AsyncCommand(
            () => ChangeSystemPageAsync(1),
            () => !IsBusy && systemPageIndex + 1 < SystemPageCount);
        NextSystemPageCommand = nextSystemPageCommand;
        navigateParentCommand = new AsyncCommand(
            NavigateParentAsync,
            () => !IsBusy && GetParent() is not null);
        NavigateParentCommand = navigateParentCommand;
        navigatePreviousCommand = new AsyncCommand(
            NavigatePreviousAsync,
            () => !IsBusy && GetSibling(-1) is not null);
        NavigatePreviousCommand = navigatePreviousCommand;
        navigateNextCommand = new AsyncCommand(
            NavigateNextAsync,
            () => !IsBusy && GetSibling(1) is not null);
        NavigateNextCommand = navigateNextCommand;
        auditAllCommand = new AsyncCommand(AuditAllAsync, CanAuditAll);
        AuditAllCommand = auditAllCommand;
        cancelAuditCommand = new AsyncCommand(
            CancelAuditAsync,
            () => IsAuditing && auditCancellation is not null);
        CancelAuditCommand = cancelAuditCommand;
        UpdateDisplay();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? AutoCopySelected;

    public IReadOnlyList<string> MassCodes { get; } =
        ["a", "b", "c", "d", "e", "f", "g"];

    public string TopBoxelText
    {
        get => topBoxelText;
        set
        {
            if (SetField(ref topBoxelText, value))
            {
                if (!string.Equals(
                    value?.Trim(),
                    selectedSystemName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    selectedSystemName = null;
                    selectedSystemAddress = 0;
                }

                UpdateSearchSize();
                activateCommand.RaiseCanExecuteChanged();
                ScheduleSystemSuggestions(value);
            }
        }
    }

    public IReadOnlyList<SystemNameSuggestion> SystemNameSuggestions
    {
        get => systemNameSuggestions;
        private set
        {
            if (SetField(ref systemNameSuggestions, value))
            {
                OnPropertyChanged(nameof(HasSystemNameSuggestions));
            }
        }
    }

    public bool HasSystemNameSuggestions => SystemNameSuggestions.Count > 0;

    public bool IsSearchingSystemSuggestions
    {
        get => isSearchingSystemSuggestions;
        private set => SetField(ref isSearchingSystemSuggestions, value);
    }

    public string SystemSuggestionStatus
    {
        get => systemSuggestionStatus;
        private set => SetField(ref systemSuggestionStatus, value);
    }

    public int SelectedSystemSuggestionIndex
    {
        get => selectedSystemSuggestionIndex;
        set => SetField(ref selectedSystemSuggestionIndex, value);
    }

    public string LowMassCode
    {
        get => lowMassCode;
        set
        {
            if (SetField(ref lowMassCode, value))
            {
                UpdateSearchSize();
            }
        }
    }

    public DateTimeOffset StartedOn
    {
        get => startedOn;
        set => SetField(ref startedOn, value);
    }

    public bool SkipAlreadyVisited
    {
        get => skipAlreadyVisited;
        set => SetField(ref skipAlreadyVisited, value);
    }

    public bool SkipKnownToSpansh
    {
        get => skipKnownToSpansh;
        set => SetField(ref skipKnownToSpansh, value);
    }

    public bool CompleteOnFssAllBodies
    {
        get => completeOnFssAllBodies;
        set => SetField(ref completeOnFssAllBodies, value);
    }

    public bool AutoCopy
    {
        get => autoCopy;
        set
        {
            if (!SetField(ref autoCopy, value) || suppressOptionPersistence)
            {
                return;
            }

            state.SetAutoCopy(value);
            OnPropertyChanged(nameof(NextSystemClipboardStatus));
            OnPropertyChanged(nameof(RequiresManualCopy));
            _ = SaveAsync();
            if (value)
            {
                AutoCopySelected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool SortDescending
    {
        get => sortDescending;
        set
        {
            if (!SetField(ref sortDescending, value) || suppressOptionPersistence)
            {
                return;
            }

            state.SetSortDescending(value);
            showNextSystemPageOnUpdate = true;
            UpdateDisplay();
            _ = SaveAsync();
        }
    }

    public bool ShowOnlyDeferred
    {
        get => showOnlyDeferred;
        set
        {
            if (!SetField(ref showOnlyDeferred, value))
            {
                return;
            }

            systemPageIndex = 0;
            showNextSystemPageOnUpdate = !value;
            UpdateSystemRows();
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RefreshButtonText));
            RaiseCommandStates();
        }
    }

    public bool IsAuditing
    {
        get => isAuditing;
        private set
        {
            if (!SetField(ref isAuditing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RefreshButtonText));
            OnPropertyChanged(nameof(AuditButtonText));
            RaiseCommandStates();
        }
    }

    public bool ConfirmLargeAudit
    {
        get => confirmLargeAudit;
        set
        {
            if (SetField(ref confirmLargeAudit, value))
            {
                auditAllCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsActive => state.IsActive;

    public BoxelSurveyStatsCoordinator? SurveyStats => surveyStats;

    public IReadOnlyList<string> SearchPrefixes => searchPrefixes;

    public char SearchLowMassCode => state.LowMassCode;

    public string? CurrentBoxelPrefix => state.Current?.Prefix;

    public int CurrentExpectedSystemCount => state.CurrentCount;

    public string StatsGlanceText
    {
        get => statsGlanceText;
        private set
        {
            if (SetField(ref statsGlanceText, value))
            {
                OnPropertyChanged(nameof(HasStatsGlance));
            }
        }
    }

    public bool HasStatsGlance => !string.IsNullOrWhiteSpace(StatsGlanceText);

    public BoxelSearchNotificationState CreateNotificationState()
    {
        return new BoxelSearchNotificationState(
            state.IsActive,
            state.CompletionMode,
            state.CompletedSystemCount,
            Math.Max(state.CurrentCount, state.Systems.Count),
            state.CurrentSystemsComplete,
            state.NextSystem);
    }

    public bool ShouldShowGalaxyMapOverlay => IsGalaxyMapOpen && state.IsActive;

    private bool IsGalaxyMapOpen => OverlayGameModeResolver.Resolve(
        status,
        musicTrack: musicTrack) == OverlayGameMode.GalaxyMap;

    public string? NextSystemForInput => state.NextSystem;

    public bool ShouldPasteNextSystem => ShouldShowGalaxyMapOverlay
        && !AutoCopy
        && state.NextSystem is not null
        && IsCurrentSystemInsideSearch();

    public string DestinationStatus
    {
        get => destinationStatus;
        private set => SetField(ref destinationStatus, value);
    }

    public bool IsDestinationValid
    {
        get => isDestinationValid;
        private set => SetField(ref isDestinationValid, value);
    }

    public string NextSystemClipboardStatus => string.Equals(
        lastCopiedSystemName,
        state.NextSystem,
        StringComparison.Ordinal)
            ? "NEXT SEARCH COPIED"
            : (state.AutoCopy) switch
            {
                true => "AUTO-COPY READY",
                false => "MANUAL COPY"
            };

    public bool RequiresManualCopy => !state.AutoCopy
        && !string.Equals(
            lastCopiedSystemName,
            state.NextSystem,
            StringComparison.Ordinal);

    public bool IsCurrentEmpty => state.CurrentIsEmpty;

    public string StatusLabel => state.IsActive ? "ACTIVE" : "INACTIVE";

    public string RefreshButtonText => IsBusy && !IsAuditing
        ? "Refreshing\u2026"
        : "Refresh boxel";

    public string AuditButtonText => IsAuditing ? "Auditing\u2026" : "Audit all boxels";

    public bool ShowLargeAuditConfirmation => state.TotalBoxelCount
        > LargeAuditConfirmationThreshold;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string CurrentBoxelName
    {
        get => currentBoxelName;
        private set => SetField(ref currentBoxelName, value);
    }

    public string CurrentBoxelDescription
    {
        get => currentBoxelDescription;
        private set => SetField(ref currentBoxelDescription, value);
    }

    public string NextSystem
    {
        get => nextSystem;
        private set => SetField(ref nextSystem, value);
    }

    public string LastSystemAvailable
    {
        get => lastSystemAvailable;
        set
        {
            if (SetField(ref lastSystemAvailable, value))
            {
                SetLastSystemAvailableEditState(true);
            }
        }
    }

    public bool HasLastSystemAvailableError =>
        hasUnappliedLastSystemAvailableEdit
        && (!TryParseLastSystemAvailable(LastSystemAvailable, out var parsed)
            || parsed < state.CurrentMaximumSystemNumber);

    public string LastSystemAvailableValidationMessage =>
        GetLastSystemAvailableValidationMessage();

    public string SystemProgress
    {
        get => systemProgress;
        private set => SetField(ref systemProgress, value);
    }

    public string BoxelProgress
    {
        get => boxelProgress;
        private set => SetField(ref boxelProgress, value);
    }

    public string SearchSize
    {
        get => searchSize;
        private set => SetField(ref searchSize, value);
    }

    public string CurrentSystemName
    {
        get => currentSystemName;
        private set => SetField(ref currentSystemName, value);
    }

    public bool HasCurrentSystemAddress => currentSystemAddress is > 0;

    public long? CurrentSystemAddress => currentSystemAddress;

    public string CurrentSystemAddressText => SystemAddressFormatter.Format(
        currentSystemAddress);

    public IReadOnlyList<BoxelSystemRowViewModel> Systems
    {
        get => systems;
        private set
        {
            if (SetField(ref systems, value))
            {
                OnPropertyChanged(nameof(HasSystems));
            }
        }
    }

    public bool HasSystems => Systems.Count > 0;

    public string SystemListNote
    {
        get => systemListNote;
        private set => SetField(ref systemListNote, value);
    }

    public string SystemPageText
    {
        get => systemPageText;
        private set => SetField(ref systemPageText, value);
    }

    public int SystemPageNumber => systemPageIndex + 1;

    public int SystemPageCount => Math.Max(
        1,
        (orderedSystemNumbers.Length + SystemsPerPage - 1) / SystemsPerPage);

    public IReadOnlyList<int> SystemPageNumbers
    {
        get => systemPageNumbers;
        private set => SetField(ref systemPageNumbers, value);
    }

    public int SelectedSystemPageIndex
    {
        get => systemPageIndex;
        set
        {
            if (value < 0
                || value >= SystemPageCount
                || value == systemPageIndex)
            {
                return;
            }

            systemPageIndex = value;
            UpdateSystemRows();
        }
    }

    public double SystemPagePickerWidth => Math.Max(
        120,
        64 + (SystemPageCount
            .ToString(CultureInfo.CurrentCulture).Length * 12));

    public string AuditDescription
    {
        get => auditDescription;
        private set => SetField(ref auditDescription, value);
    }

    public string AuditProgress
    {
        get => auditProgress;
        private set => SetField(ref auditProgress, value);
    }

    public int AuditProcessed
    {
        get => auditProcessed;
        private set => SetField(ref auditProcessed, value);
    }

    public int AuditTotal
    {
        get => auditTotal;
        private set => SetField(ref auditTotal, value);
    }

    public IReadOnlyList<BoxelNavigationOptionViewModel> ChildBoxels
    {
        get => childBoxels;
        private set
        {
            if (NavigationListsMatch(childBoxels, value))
            {
                return;
            }

            childBoxels = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChildBoxels));
        }
    }

    public bool HasChildBoxels => ChildBoxels.Count > 0;

    public IReadOnlyList<BoxelNavigationOptionViewModel> BreadcrumbBoxels =>
        breadcrumbBoxels;

    public BoxelNavigationOptionViewModel? CurrentHierarchyBoxel
    {
        get => currentHierarchyBoxel;
        private set => SetField(ref currentHierarchyBoxel, value);
    }

    public BoxelNavigationOptionViewModel? ParentBoxel
    {
        get => parentBoxel;
        private set => SetField(ref parentBoxel, value);
    }

    public BoxelNavigationOptionViewModel? PreviousSiblingBoxel
    {
        get => previousSiblingBoxel;
        private set => SetField(ref previousSiblingBoxel, value);
    }

    public BoxelNavigationOptionViewModel? NextSiblingBoxel
    {
        get => nextSiblingBoxel;
        private set => SetField(ref nextSiblingBoxel, value);
    }

    public string CurrentHierarchyBoxelLabel =>
        CurrentHierarchyBoxel?.Label ?? string.Empty;

    public string CurrentHierarchyBoxelProgressLabel =>
        CurrentHierarchyBoxel?.ProgressLabel ?? string.Empty;

    public string ParentBoxelLabel => ParentBoxel?.Label ?? string.Empty;

    public string PreviousSiblingBoxelLabel =>
        PreviousSiblingBoxel?.Label ?? string.Empty;

    public string NextSiblingBoxelLabel =>
        NextSiblingBoxel?.Label ?? string.Empty;

    public string SiblingPosition
    {
        get => siblingPosition;
        private set => SetField(ref siblingPosition, value);
    }

    public bool CanNavigateSearchTree => state.IsActive
        && state.TotalBoxelCount > 1;

    public bool CanNavigateParent => GetParent() is not null;

    public bool CanNavigatePreviousSibling => GetSibling(-1) is not null;

    public bool CanNavigateNextSibling => GetSibling(1) is not null;

    public ICommand ActivateCommand { get; }

    public ICommand DisableCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand CopyNextCommand { get; }

    public ICommand MarkNextEmptyCommand { get; }

    public ICommand ApplyLastSystemAvailableCommand { get; }

    public ICommand NextJumpPageCommand { get; }

    public ICommand PreviousSystemPageCommand { get; }

    public ICommand NextSystemPageCommand { get; }

    public ICommand NavigateParentCommand { get; }

    public ICommand NavigatePreviousCommand { get; }

    public ICommand NavigateNextCommand { get; }

    public ICommand AuditAllCommand { get; }

    public ICommand CancelAuditCommand { get; }

    public bool CanSaveProgress => frontierId is not null && state.TopBoxel is not null;

    public string SuggestedSaveName => state.TopBoxel?.Name
        ?? TopBoxelText.Trim();

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
        copyNextCommand.RaiseCanExecuteChanged();
    }

    public void MoveSystemSuggestionSelection(int offset)
    {
        if (SystemNameSuggestions.Count == 0 || offset == 0)
        {
            return;
        }

        SelectedSystemSuggestionIndex = Math.Clamp(
            SelectedSystemSuggestionIndex + offset,
            0,
            SystemNameSuggestions.Count - 1);
    }

    public bool SelectCurrentSystemSuggestion()
    {
        return SelectedSystemSuggestionIndex >= 0
            && SelectedSystemSuggestionIndex < SystemNameSuggestions.Count
            && SelectSystemSuggestion(
                SystemNameSuggestions[SelectedSystemSuggestionIndex]);
    }

    public bool SelectSystemSuggestion(SystemNameSuggestion? suggestion)
    {
        if (suggestion is null
            || suggestion.SystemAddress <= 0
            || string.IsNullOrWhiteSpace(suggestion.Name))
        {
            return false;
        }

        CancelSystemSuggestions();
        selectedSystemName = suggestion.Name.Trim();
        selectedSystemAddress = suggestion.SystemAddress;
        TopBoxelText = selectedSystemName;
        SystemNameSuggestions = [];
        SelectedSystemSuggestionIndex = -1;
        IsSearchingSystemSuggestions = false;
        SystemSuggestionStatus = $"Selected {selectedSystemName}.";
        return true;
    }

    public void DismissSystemSuggestions()
    {
        CancelSystemSuggestions();
        SystemNameSuggestions = [];
        SelectedSystemSuggestionIndex = -1;
        IsSearchingSystemSuggestions = false;
        SystemSuggestionStatus = string.Empty;
    }

    public async Task LoadProfileAsync(
        string profileFrontierId,
        string? profileCommanderName,
        bool profileIsOdyssey,
        BoxelSearchSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFrontierId);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (auditCancellation is not null)
        {
            await auditCancellation.CancelAsync();
        }

        frontierId = profileFrontierId;
        commanderName = profileCommanderName;
        isOdyssey = profileIsOdyssey;
        await ApplySnapshotAsync(snapshot);

        UpdateDisplay();
        if (state.IsActive)
        {
            await RefreshCurrentAsync();
        }
        else
        {
            StatusMessage = state.TopBoxel is null
                ? "No boxel search is configured for this commander."
                : "Loaded the saved boxel search; it is currently disabled.";
        }
    }

    public void SetProfileError(string message)
    {
        auditCancellation?.Cancel();
        frontierId = null;
        SetLastSystemAvailableEditState(false);
        state.Reset();
        navigationOptions.Clear();
        StatusMessage = message;
        UpdateDisplay();
    }

    public void ReportSaveProgressFailure(string message)
    {
        StatusMessage = "The boxel search could not be saved: " + message;
    }

    public void UpdateCurrentSystem(
        string? systemName,
        GalacticCoordinate? position,
        long? systemAddress = null)
    {
        var nextSystemName = string.IsNullOrWhiteSpace(systemName)
            ? Unavailable
            : systemName;
        var nextSystemAddress = systemAddress is > 0 ? systemAddress : null;
        if (string.Equals(
                currentSystemName,
                nextSystemName,
                StringComparison.OrdinalIgnoreCase)
            && currentPosition == position
            && currentSystemAddress == nextSystemAddress)
        {
            return;
        }

        CurrentSystemName = nextSystemName;
        currentPosition = position;
        currentSystemAddress = nextSystemAddress;
        OnPropertyChanged(nameof(HasCurrentSystemAddress));
        OnPropertyChanged(nameof(CurrentSystemAddress));
        OnPropertyChanged(nameof(CurrentSystemAddressText));
        UpdateSystemRows();
    }

    public async Task UpdateRouteAsync(NavRouteSnapshot? route)
    {
        latestRoute = route;
        UpdateDestinationStatus();
        if (!state.IsActive || route is null)
        {
            return;
        }

        await operationLock.WaitAsync(CancellationToken.None);
        try
        {
            var changed = state.MergeRoute(route.Route
                .Select(entry => entry.ToBoxelObservation())
                .OfType<BoxelSystemObservation>());
            if (changed)
            {
                UpdateDisplay();
                await SaveAsync();
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task ApplyJournalEventsAsync(
        IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!state.IsActive)
        {
            return;
        }

        await operationLock.WaitAsync(CancellationToken.None);
        try
        {
            var changed = false;
            foreach (var journalEvent in journalEvents)
            {
                changed |= state.Apply(journalEvent);
            }

            if (changed)
            {
                UpdateDisplay();
                await SaveAsync();
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task UpdateStatusAsync(
        EliteStatus nextStatus,
        bool allowAutoCopy = true,
        string? nextMusicTrack = null)
    {
        ArgumentNullException.ThrowIfNull(nextStatus);
        var wasGalaxyMapOpen = IsGalaxyMapOpen;
        status = nextStatus;
        musicTrack = nextMusicTrack;
        var enteredGalaxyMap = !wasGalaxyMapOpen && IsGalaxyMapOpen;
        lastDestination = nextStatus.Destination;
        if (!IsGalaxyMapOpen)
        {
            lastCopiedSystemName = null;
        }

        UpdateDestinationStatus();
        RaiseOverlayProperties();
        if (!enteredGalaxyMap
            || !allowAutoCopy
            || !state.IsActive
            || !state.AutoCopy
            || !IsCurrentSystemInsideSearch())
        {
            return;
        }

        await CopyNextSystemAsync();
    }

    public async Task ActivateAsync()
    {
        if (!TryParseBoxelInput(TopBoxelText, out var topBoxel))
        {
            StatusMessage = "Enter a valid generated system or boxel name.";
            return;
        }

        var selectedMassCode = string.IsNullOrWhiteSpace(LowMassCode)
            ? '\0'
            : char.ToLowerInvariant(LowMassCode[0]);
        if (!state.TryActivate(
                new BoxelSearchActivationRequest
                {
                    TopBoxel = topBoxel,
                    LowMassCode = selectedMassCode,
                    StartedOn = StartedOn,
                    SkipAlreadyVisited = SkipAlreadyVisited,
                    SkipKnownToSpansh = SkipKnownToSpansh,
                    CompletionMode = CompleteOnFssAllBodies
                        ? BoxelCompletionMode.FssAllBodies
                        : BoxelCompletionMode.EnterSystem,
                    AutoCopy = AutoCopy,
                    SortDescending = SortDescending
                },
                out var error))
        {
            StatusMessage = error ?? "The boxel search configuration is invalid.";
            return;
        }

        SetLastSystemAvailableEditState(false);
        await RefreshCurrentAsync(preserveLastSystemAvailableEdit: false);
    }

    public async Task DisableAsync()
    {
        state.Disable();
        SetLastSystemAvailableEditState(false);
        UpdateDisplay();
        await SaveAsync("Boxel search disabled; its progress was retained.");
    }

    public async Task<SaveBoxelProgressResult> SaveProgressAsync(
        string? name = null,
        string? notes = null)
    {
        if (!CanSaveProgress || frontierId is null)
        {
            StatusMessage = "Start a boxel search before saving its progress.";
            return SaveBoxelProgressResult.Unavailable;
        }

        if (state.SavedSearchFileName is { } linkedFileName)
        {
            if (await savedSearchStore.ExistsAsync(
                    frontierId,
                    linkedFileName,
                    CancellationToken.None))
            {
                await SaveAsync("Saved the current boxel search progress.");
                return SaveBoxelProgressResult.Saved;
            }

            state.SetSavedSearchFileName(null);
            await profileStore.SaveBoxelSearchAsync(
                frontierId,
                commanderName,
                isOdyssey,
                state.CreateSnapshot(),
                CancellationToken.None);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return SaveBoxelProgressResult.RequiresDetails;
        }

        try
        {
            var saved = await savedSearchStore.CreateAsync(
                frontierId,
                name,
                notes,
                state.CreateSnapshot(),
                CancellationToken.None);
            state.SetSavedSearchFileName(saved.FileName);
            await SaveAsync($"Saved boxel search as {saved.Name}.");
            return SaveBoxelProgressResult.Saved;
        }
        catch (Exception exception) when (IsExpectedSavedSearchException(exception))
        {
            StatusMessage = "The boxel search could not be saved: "
                + exception.Message;
            return SaveBoxelProgressResult.Failed;
        }
    }

    public Task<IReadOnlyList<SavedBoxelSearchCatalogEntry>> ListSavedSearchesAsync()
    {
        return frontierId is null
            ? Task.FromResult<IReadOnlyList<SavedBoxelSearchCatalogEntry>>([])
            : savedSearchStore.ListAsync(frontierId, CancellationToken.None);
    }

    public Task<SavedBoxelSearchDocument> RenameSavedSearchAsync(
        string fileName,
        string name)
    {
        return savedSearchStore.RenameAsync(
            RequireFrontierId(),
            fileName,
            name,
            CancellationToken.None);
    }

    public Task<SavedBoxelSearchDocument> SaveSavedSearchNotesAsync(
        string fileName,
        string? notes)
    {
        return savedSearchStore.SaveNotesAsync(
            RequireFrontierId(),
            fileName,
            notes,
            CancellationToken.None);
    }

    public Task<SavedBoxelSearchDocument> SetSavedSearchFavoriteAsync(
        string fileName,
        bool isFavorite)
    {
        return savedSearchStore.SetFavoriteAsync(
            RequireFrontierId(),
            fileName,
            isFavorite,
            CancellationToken.None);
    }

    public async Task DeleteSavedSearchAsync(string fileName)
    {
        var activeFrontierId = RequireFrontierId();
        await savedSearchStore.DeleteAsync(
            activeFrontierId,
            fileName,
            CancellationToken.None);
        if (string.Equals(
                state.SavedSearchFileName,
                fileName,
                StringComparison.OrdinalIgnoreCase))
        {
            state.SetSavedSearchFileName(null);
            await profileStore.SaveBoxelSearchAsync(
                activeFrontierId,
                commanderName,
                isOdyssey,
                state.CreateSnapshot(),
                CancellationToken.None);
        }
    }

    public async Task ResumeSavedSearchAsync(string fileName)
    {
        var document = await savedSearchStore.LoadAsync(
            RequireFrontierId(),
            fileName,
            CancellationToken.None);
        var snapshot = document.Search with
        {
            Active = true,
            SavedSearchFileName = document.FileName
        };
        await ApplySnapshotAsync(snapshot);
        UpdateDisplay();
        await SaveAsync($"Resumed saved boxel search {document.Name}.");
        if (AutoCopy)
        {
            AutoCopySelected?.Invoke(this, EventArgs.Empty);
        }

        await RefreshCurrentAsync();
    }

    private async Task ApplySnapshotAsync(BoxelSearchSnapshot snapshot)
    {
        SetLastSystemAvailableEditState(false);
        state.Reset(snapshot);
        navigationOptions.Clear();
        ConfirmLargeAudit = false;
        AuditProcessed = 0;
        AuditProgress = "No full-area audit has run in this session.";
        suppressOptionPersistence = true;
        try
        {
            DismissSystemSuggestions();
            selectedSystemName = snapshot.TopBoxel?.Name;
            selectedSystemAddress = snapshot.TopBoxel?.SystemAddress > 0
                ? snapshot.TopBoxel.SystemAddress
                : 0;
            TopBoxelText = selectedSystemName ?? string.Empty;
            LowMassCode = snapshot.LowMassCode.ToString();
            StartedOn = snapshot.StartedOn == DateTimeOffset.MinValue
                ? new DateTimeOffset(DateTime.Today)
                : snapshot.StartedOn;
            SkipAlreadyVisited = snapshot.SkipAlreadyVisited;
            SkipKnownToSpansh = snapshot.SkipKnownToSpansh;
            CompleteOnFssAllBodies =
                snapshot.CompletionMode == BoxelCompletionMode.FssAllBodies;
            AutoCopy = snapshot.AutoCopy;
            SortDescending = snapshot.SortDescending;
            state.SetAutoCopy(AutoCopy);
        }
        finally
        {
            suppressOptionPersistence = false;
        }

        if (state.TopBoxel is null)
        {
            return;
        }

        try
        {
            state.ApplyEmptyBoxels(
                await emptyBoxelStore.LoadGroupAsync(
                    state.TopBoxel,
                    CancellationToken.None));
        }
        catch (InvalidDataException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public async Task DisableAutoCopyForCompetingRouteAsync()
    {
        if (!AutoCopy)
        {
            return;
        }

        SetField(ref autoCopy, false, nameof(AutoCopy));
        state.SetAutoCopy(false);
        await SaveAsync(
            "Boxel auto-copy was disabled because another Galaxy Map auto-copy setting was selected.");
    }

    public Task RefreshCurrentAsync()
    {
        return RefreshCurrentAsync(preserveLastSystemAvailableEdit: true);
    }

    private async Task RefreshCurrentAsync(bool preserveLastSystemAvailableEdit)
    {
        if (!state.IsActive || state.Current is null)
        {
            StatusMessage = "Activate a boxel search before refreshing systems.";
            return;
        }

        await operationLock.WaitAsync(CancellationToken.None);
        try
        {
            IsBusy = true;
            StatusMessage = $"Refreshing {state.Current.Prefix}\u2026";
            var warnings = new List<string>();
            try
            {
                state.ApplyEmptyBoxels(
                    await emptyBoxelStore.LoadGroupAsync(
                        state.Current,
                        CancellationToken.None));
            }
            catch (InvalidDataException exception)
            {
                warnings.Add(exception.Message);
            }

            if (!state.CurrentIsEmpty)
            {
                var local = await localSystemReader.ReadAsync(
                    frontierId!,
                    state.Current,
                    CancellationToken.None);
                state.MergeLocalSystems(local.Systems);
                warnings.AddRange(local.Errors);
                if (latestRoute is not null)
                {
                    state.MergeRoute(latestRoute.Route
                        .Select(entry => entry.ToBoxelObservation())
                        .OfType<BoxelSystemObservation>());
                }

                try
                {
                    state.MergeSpanshSystems(
                        await systemResolver.SearchAsync(
                            state.Current,
                            CancellationToken.None));
                }
                catch (Exception exception) when (
                    exception is HttpRequestException
                        or TaskCanceledException
                        or InvalidDataException
                        or System.Text.Json.JsonException)
                {
                    warnings.Add("Spansh refresh failed: " + exception.Message);
                }
            }

            if (!preserveLastSystemAvailableEdit)
            {
                SetLastSystemAvailableEditState(false);
            }

            UpdateDisplay();
            await SaveAsync();
            StatusMessage = warnings.Count == 0
                ? $"Refreshed {state.Systems.Count:N0} known systems in "
                    + state.Current.Prefix
                    + "."
                : string.Join(Environment.NewLine, warnings);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The boxel refresh could not be completed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
            operationLock.Release();
        }
    }

    public async Task AuditAllAsync()
    {
        if (!CanAuditAll()
            || frontierId is null
            || state.TopBoxel is null)
        {
            StatusMessage = GetAuditUnavailableStatus();
            return;
        }

        BeginAuditProgress();
        var cancellation = auditCancellation!;
        var auditFrontierId = frontierId;
        var auditTopPrefix = state.TopBoxel.Prefix;
        var request = CreateCompletionAuditRequest(auditFrontierId);
        var progress = CreateAuditProgressReporter(cancellation);

        try
        {
            var result = await completionAuditor.AuditAsync(
                request,
                progress,
                cancellation.Token);
            await ApplyAuditResultAsync(result, auditFrontierId, auditTopPrefix, cancellation);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            AuditProgress = $"Audit stopped after {AuditProcessed:N0} boxels.";
            StatusMessage = "The full-area audit could not be completed: "
                + exception.Message;
        }
        finally
        {
            CompleteAudit(cancellation);
        }
    }

    private string GetAuditUnavailableStatus()
    {
        return ShowLargeAuditConfirmation && !ConfirmLargeAudit
            ? "Confirm the large network audit before starting it."
            : "Activate a boxel search before auditing its full area.";
    }

    private void BeginAuditProgress()
    {
        IsBusy = true;
        IsAuditing = true;
        AuditProcessed = 0;
        AuditTotal = Math.Max(1, state.TotalBoxelCount);
        AuditProgress = $"Preparing to audit {state.TotalBoxelCount:N0} boxels\u2026";
        StatusMessage = "The full-area audit is running in the background.";
        auditCancellation = new CancellationTokenSource();
    }

    private BoxelCompletionAuditRequest CreateCompletionAuditRequest(
        string auditFrontierId)
    {
        var snapshot = state.CreateSnapshot();
        var routeSystems = latestRoute?.Route
            .Select(entry => entry.ToBoxelObservation())
            .OfType<BoxelSystemObservation>()
            .ToArray() ?? [];
        return new BoxelCompletionAuditRequest(
            auditFrontierId,
            state.Boxels,
            state.EmptyBoxelPrefixes,
            state.Current?.Prefix,
            snapshot.StartedOn,
            snapshot.SkipAlreadyVisited,
            snapshot.SkipKnownToSpansh,
            snapshot.CompletionMode,
            routeSystems);
    }

    private Progress<BoxelCompletionAuditProgress> CreateAuditProgressReporter(
        CancellationTokenSource cancellation)
    {
        return new Progress<BoxelCompletionAuditProgress>(update =>
        {
            if (!IsAuditing || !ReferenceEquals(auditCancellation, cancellation))
            {
                return;
            }

            if (update.Processed <= AuditProcessed)
            {
                return;
            }

            AuditProcessed = update.Processed;
            AuditProgress = $"Audited {update.Processed:N0} of {update.Total:N0}: "
                + update.Prefix;
        });
    }

    private async Task ApplyAuditResultAsync(
        BoxelCompletionAuditResult result,
        string auditFrontierId,
        string auditTopPrefix,
        CancellationTokenSource cancellation)
    {
        AuditProcessed = result.Processed;
        AuditTotal = Math.Max(1, result.Total);
        await operationLock.WaitAsync(cancellation.Token);
        try
        {
            if (!IsAuditStillCurrent(auditFrontierId, auditTopPrefix))
            {
                StatusMessage = "The audit finished for a profile that is no longer active; its results were not applied.";
                return;
            }

            state.ApplyCompletionAudit(result.Entries);
            showNextSystemPageOnUpdate = true;
            UpdateDisplay();
            await SaveAsync();
        }
        finally
        {
            operationLock.Release();
        }

        AuditProgress = result.WasCancelled
            ? $"Cancelled after {result.Processed:N0} of {result.Total:N0} boxels."
            : $"Audited all {result.Total:N0} boxels.";
        StatusMessage = BuildAuditStatus(result);
    }

    private bool IsAuditStillCurrent(string auditFrontierId, string auditTopPrefix)
    {
        return string.Equals(frontierId, auditFrontierId, StringComparison.Ordinal)
            && string.Equals(
                state.TopBoxel?.Prefix,
                auditTopPrefix,
                StringComparison.Ordinal);
    }

    private void CompleteAudit(CancellationTokenSource cancellation)
    {
        cancellation.Dispose();
        if (ReferenceEquals(auditCancellation, cancellation))
        {
            auditCancellation = null;
        }

        IsAuditing = false;
        IsBusy = false;
    }

    public Task CancelAuditAsync()
    {
        auditCancellation?.Cancel();
        StatusMessage = "Cancelling the full-area audit after the current request\u2026";
        return Task.CompletedTask;
    }

    public void CancelPendingOperations()
    {
        auditCancellation?.Cancel();
        if (surveyStatsUnsubscribed || surveyStats is null)
        {
            return;
        }

        surveyStats.Changed -= OnSurveyStatsChanged;
        surveyStatsUnsubscribed = true;
    }

    public void ReportStatisticsFailure(string message)
    {
        StatusMessage = "Could not open boxel statistics: " + message;
    }

    private void OnSurveyStatsChanged(object? sender, EventArgs eventArgs)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateStatsGlance();
            return;
        }

        Dispatcher.UIThread.Post(UpdateStatsGlance);
    }

    private void UpdateStatsGlance()
    {
        var snapshot = surveyStats?.Current;
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Prefix))
        {
            StatsGlanceText = string.Empty;
            return;
        }

        var helium = snapshot.MinHeliumPercent is null && snapshot.MaxHeliumPercent is null
            ? Unavailable
            : string.Create(
                CultureInfo.CurrentCulture,
                $"HE {snapshot.MinHeliumPercent ?? snapshot.MaxHeliumPercent:0.#}–{snapshot.MaxHeliumPercent ?? snapshot.MinHeliumPercent:0.#}%");
        var highestSuffix = snapshot.HighestRecordedSuffix?.ToString(
            "N0",
            CultureInfo.CurrentCulture) ?? Unavailable;
        StatsGlanceText = string.Create(
            CultureInfo.CurrentCulture,
            $"{snapshot.Prefix}  ·  {snapshot.Visited:N0} recorded  ·  highest suffix {highestSuffix}  ·  {helium}");
    }

    public async Task ApplyLastSystemAvailableAsync()
    {
        if (!TryParseLastSystemAvailable(
                LastSystemAvailable,
                out var parsedLastSystemAvailable))
        {
            StatusMessage = "Last system available must be a whole number from 0 to 99,999.";
            return;
        }

        if (parsedLastSystemAvailable < state.CurrentMaximumSystemNumber)
        {
            StatusMessage = $"Last system available cannot be below recorded suffix "
                + $"{state.CurrentMaximumSystemNumber:N0}.";
            return;
        }

        state.SetExpectedSystemCount(parsedLastSystemAvailable + 1);
        SetLastSystemAvailableEditState(false);
        showNextSystemPageOnUpdate = true;
        UpdateDisplay();
        await SaveAsync($"Last system available updated to {parsedLastSystemAvailable:N0}.");
    }

    public void RestoreLastSystemAvailable()
    {
        if (!hasUnappliedLastSystemAvailableEdit)
        {
            return;
        }

        SetLastSystemAvailableEditState(false);
        SetField(
            ref lastSystemAvailable,
            FormatLastSystemAvailable(),
            nameof(LastSystemAvailable));
    }

    public async Task MarkNextEmptyAsync()
    {
        if (!state.IsActive)
        {
            return;
        }

        await operationLock.WaitAsync(CancellationToken.None);
        try
        {
            IsBusy = true;
            if (!state.TryMarkNextSystemEmpty(out var markedSystem, out var error))
            {
                StatusMessage = error ?? "The next incomplete system was not marked empty.";
                return;
            }

            showNextSystemPageOnUpdate = true;
            UpdateDisplay();
            var next = state.NextSystem;
            await SaveAsync(string.IsNullOrWhiteSpace(next)
                ? $"Marked {markedSystem} empty. No incomplete systems remain."
                : $"Marked {markedSystem} empty. Next incomplete system: {next}.");
            if (state.AutoCopy && !string.IsNullOrWhiteSpace(next))
            {
                await CopyNextSystemAsync();
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The empty-system marker was not changed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
            operationLock.Release();
        }
    }

    public async Task CopyNextSystemAsync()
    {
        if (state.NextSystem is null)
        {
            StatusMessage = "No next boxel system is available to copy.";
            return;
        }

        if (clipboardWriter is null)
        {
            StatusMessage = "The desktop clipboard is not available.";
            return;
        }

        try
        {
            await clipboardWriter(state.NextSystem);
            lastCopiedSystemName = state.NextSystem;
            StatusMessage = $"Copied {state.NextSystem} to the clipboard.";
            OnPropertyChanged(nameof(NextSystemClipboardStatus));
        }
        catch (Exception exception)
        {
            StatusMessage = "The next system could not be copied: "
                + exception.Message;
        }
    }

    private async Task NavigateParentAsync()
    {
        var parent = GetParent();
        if (parent is not null)
        {
            await NavigateAsync(parent);
        }
    }

    private async Task NavigatePreviousAsync()
    {
        var sibling = GetSibling(-1);
        if (sibling is not null)
        {
            await NavigateAsync(sibling);
        }
    }

    private async Task NavigateNextAsync()
    {
        var sibling = GetSibling(1);
        if (sibling is not null)
        {
            await NavigateAsync(sibling);
        }
    }

    private async Task NavigateAsync(BoxelAddress boxel)
    {
        if (!state.TrySetCurrent(boxel, out var error))
        {
            StatusMessage = error ?? "The selected boxel could not be opened.";
            return;
        }

        showNextSystemPageOnUpdate = true;
        UpdateDisplay();
        await SaveAsync();
        await RefreshCurrentAsync();
    }

    private Task CompleteSystemAsync(string systemName)
    {
        return RunSystemActionAsync(async () =>
        {
            if (!state.TrySetSystemComplete(systemName, true, out var error))
            {
                StatusMessage = error ?? "The system was not marked complete.";
                return;
            }

            await FinishSystemActionAsync($"Marked {systemName} complete.");
        });
    }

    private Task ReopenSystemAsync(string systemName)
    {
        return RunSystemActionAsync(async () =>
        {
            bool changed;
            string? error;
            if (state.IsSystemDeferred(systemName))
            {
                changed = state.TrySetSystemDeferred(systemName, false, out error);
            }
            else if (state.IsSystemEmpty(systemName))
            {
                changed = state.TrySetSystemEmpty(systemName, false, out error);
            }
            else
            {
                changed = state.TrySetSystemComplete(systemName, false, out error);
            }
            if (!changed)
            {
                StatusMessage = error ?? "The system was not reopened.";
                return;
            }

            await FinishSystemActionAsync($"Reopened {systemName}.");
        });
    }

    private Task DeferSystemAsync(string systemName)
    {
        return RunSystemActionAsync(async () =>
        {
            if (!state.TrySetSystemDeferred(systemName, true, out var error))
            {
                StatusMessage = error ?? "The system was not deferred.";
                return;
            }

            await FinishSystemActionAsync($"Deferred {systemName}.");
        });
    }

    private Task StartAtSystemAsync(string systemName)
    {
        return RunSystemActionAsync(async () =>
        {
            if (!state.TryStartAtSystem(systemName, out var deferredCount, out var error))
            {
                StatusMessage = error ?? "The survey start point was not changed.";
                return;
            }

            var message = deferredCount == 0
                ? $"Survey will start at {systemName}."
                : $"Survey will start at {systemName}; deferred {deferredCount:N0} earlier systems.";
            await FinishSystemActionAsync(message);
        });
    }

    private async Task RunSystemActionAsync(Func<Task> action)
    {
        await operationLock.WaitAsync(CancellationToken.None);
        try
        {
            await action();
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task FinishSystemActionAsync(string message)
    {
        showNextSystemPageOnUpdate = true;
        UpdateDisplay();
        await SaveAsync(message);
        if (state.AutoCopy && !string.IsNullOrWhiteSpace(state.NextSystem))
        {
            await CopyNextSystemAsync();
        }
    }

    private BoxelAddress? GetParent()
    {
        if (state.TopBoxel is null
            || state.Current is null
            || string.Equals(
                state.TopBoxel.Prefix,
                state.Current.Prefix,
                StringComparison.Ordinal))
        {
            return null;
        }

        var parent = state.Current.Parent;
        return state.TopBoxel.Contains(parent) ? parent : null;
    }

    private BoxelAddress? GetSibling(int offset)
    {
        if (state.TopBoxel is null
            || state.Current is null
            || string.Equals(
                state.TopBoxel.Prefix,
                state.Current.Prefix,
                StringComparison.Ordinal))
        {
            return null;
        }

        var siblings = state.Current.Parent.Children;
        var index = siblings.ToList().FindIndex(sibling => string.Equals(
            sibling.Prefix,
            state.Current.Prefix,
            StringComparison.Ordinal));
        var targetIndex = index + offset;
        return targetIndex >= 0 && targetIndex < siblings.Count
            ? siblings[targetIndex]
            : null;
    }

    private bool IsCurrentSystemInsideSearch()
    {
        return state.TopBoxel is not null
            && TryParseBoxelInput(CurrentSystemName, out var currentSystem)
            && state.TopBoxel.Contains(currentSystem);
    }

    private bool CanActivate()
    {
        return !IsBusy
            && frontierId is not null
            && !state.IsActive
            && !string.IsNullOrWhiteSpace(TopBoxelText);
    }

    private bool CanDisable()
    {
        return !IsBusy && frontierId is not null && state.IsActive;
    }

    private bool CanUseActiveSearch()
    {
        return !IsBusy && frontierId is not null && state.IsActive;
    }

    private bool CanApplyLastSystemAvailable()
    {
        return CanUseActiveSearch()
            && TryParseLastSystemAvailable(
                LastSystemAvailable,
                out var parsedLastSystemAvailable)
            && parsedLastSystemAvailable >= state.CurrentMaximumSystemNumber
            && parsedLastSystemAvailable != Math.Max(0, state.CurrentCount - 1);
    }

    private bool CanCopyNext()
    {
        return !IsBusy
            && state.IsActive
            && state.NextSystem is not null
            && clipboardWriter is not null;
    }

    private bool CanAuditAll()
    {
        return !IsBusy
            && frontierId is not null
            && state.IsActive
            && state.TopBoxel is not null
            && (!ShowLargeAuditConfirmation || ConfirmLargeAudit);
    }

    private static string BuildAuditStatus(BoxelCompletionAuditResult result)
    {
        var outcome = result.WasCancelled
            ? $"Audit cancelled after {result.Processed:N0} of {result.Total:N0} boxels; partial progress was saved."
            : $"Audited all {result.Total:N0} boxels and saved the refreshed progress.";
        return result.Errors.Count == 0
            ? outcome
            : outcome + $" {result.Errors.Count:N0} warning"
                + ((result.Errors.Count == 1) switch
                {
                    true => string.Empty,
                    false => "s"
                })
                + $" occurred. First: {result.Errors[0]}";
    }

    private async Task SaveAsync(string? successMessage = null)
    {
        if (frontierId is null || state.TopBoxel is null)
        {
            return;
        }

        try
        {
            var snapshot = state.CreateSnapshot();
            await profileStore.SaveBoxelSearchAsync(
                frontierId,
                commanderName,
                isOdyssey,
                snapshot,
                CancellationToken.None);
            if (state.SavedSearchFileName is { } savedFileName)
            {
                try
                {
                    await savedSearchStore.SaveProgressAsync(
                        frontierId,
                        savedFileName,
                        snapshot,
                        CancellationToken.None);
                }
                catch (FileNotFoundException)
                {
                    state.SetSavedSearchFileName(null);
                    await profileStore.SaveBoxelSearchAsync(
                        frontierId,
                        commanderName,
                        isOdyssey,
                        state.CreateSnapshot(),
                        CancellationToken.None);
                }
            }

            if (successMessage is not null)
            {
                StatusMessage = successMessage;
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The boxel search changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateDisplay()
    {
        searchPrefixes = state.Boxels.Select(boxel => boxel.Prefix).ToArray();
        OnPropertyChanged(nameof(SearchPrefixes));
        OnPropertyChanged(nameof(CanSaveProgress));
        OnPropertyChanged(nameof(SuggestedSaveName));
        CurrentBoxelName = state.Current?.Prefix ?? Unavailable;
        CurrentBoxelDescription = state.Current is null
            ? string.Empty
            : $"System-name range: {state.Current.WithSystemNumber(0).Name} through "
                + state.Current.WithSystemNumber(
                    Math.Max(0, state.CurrentCount - 1)).Name;
        NextSystem = state.NextSystem ?? Unavailable;
        if (!hasUnappliedLastSystemAvailableEdit)
        {
            SetField(
                ref lastSystemAvailable,
                FormatLastSystemAvailable(),
                nameof(LastSystemAvailable));
        }
        SetLastSystemAvailableEditState(hasUnappliedLastSystemAvailableEdit);
        SystemProgress = $"{state.CompletedSystemCount:N0} of "
            + $"{Math.Max(state.CurrentCount, state.Systems.Count):N0} systems complete";
        BoxelProgress = $"{state.CompletedBoxelCount:N0} of "
            + $"{state.TotalBoxelCount:N0} boxels complete";
        AuditTotal = Math.Max(1, state.TotalBoxelCount);
        AuditDescription = state.IsActive
            ? $"Checks saved system history, the current NavRoute, empty-boxel records, "
                + $"and Spansh across all {state.TotalBoxelCount:N0} boxels. Network requests "
                + "run sequentially; cancellation keeps completed audit progress."
            : "Activate a boxel search to audit its full area.";
        OnPropertyChanged(nameof(IsActive));
        RaiseOverlayProperties();
        OnPropertyChanged(nameof(IsCurrentEmpty));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ShowLargeAuditConfirmation));
        OnPropertyChanged(nameof(CanNavigateSearchTree));
        OnPropertyChanged(nameof(CanNavigateParent));
        OnPropertyChanged(nameof(CanNavigatePreviousSibling));
        OnPropertyChanged(nameof(CanNavigateNextSibling));
        UpdateSystemRows();
        UpdateNavigation();
        RaiseCommandStates();
    }

    private string RequireFrontierId()
    {
        return frontierId
            ?? throw new InvalidOperationException(
                "A commander profile must be loaded first.");
    }

    private static bool IsExpectedSavedSearchException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or System.Text.Json.JsonException;
    }

    private void UpdateDestinationStatus()
    {
        BoxelAddress? destinationBoxel = null;
        var routeDestination = latestRoute?.Route.Count > 1
            ? latestRoute.Route[^1]
            : null;
        if (routeDestination is not null)
        {
            destinationBoxel = routeDestination.ToBoxelObservation()?.Boxel;
        }
        else if (lastDestination is { Body: 0 } destination)
        {
            var resolved = destination.System > 0
                ? BoxelAddress.TryFromSystemAddress(
                    destination.System,
                    destination.Name ?? string.Empty,
                    out destinationBoxel)
                : BoxelAddress.TryParse(
                    destination.Name,
                    out destinationBoxel);
            if (!resolved)
            {
                destinationBoxel = null;
            }
        }

        if (destinationBoxel is null)
        {
            DestinationStatus = "No generated-system destination selected";
            IsDestinationValid = false;
            return;
        }

        if (state.TopBoxel is null || !state.TopBoxel.Contains(destinationBoxel))
        {
            DestinationStatus = $"{destinationBoxel.Prefix} · outside search boxel";
            IsDestinationValid = false;
            return;
        }

        if (destinationBoxel.MassCode < state.LowMassCode)
        {
            DestinationStatus = $"{destinationBoxel.Prefix} · mass code too low";
            IsDestinationValid = false;
            return;
        }

        if (state.Systems.Any(system =>
                system.IsComplete
                && string.Equals(
                    system.Boxel.Name,
                    destinationBoxel.Name,
                    StringComparison.Ordinal)))
        {
            DestinationStatus = $"{destinationBoxel.Name} · already surveyed";
            IsDestinationValid = false;
            return;
        }

        DestinationStatus = $"{destinationBoxel.Name} · destination is valid";
        IsDestinationValid = true;
    }

    private void RaiseOverlayProperties()
    {
        OnPropertyChanged(nameof(ShouldShowGalaxyMapOverlay));
        OnPropertyChanged(nameof(NextSystemClipboardStatus));
    }

    private void UpdateSystemRows()
    {
        if (!state.IsActive || state.Current is null || state.CurrentIsEmpty)
        {
            systemPageIndex = 0;
            systemPagePrefix = state.IsActive ? state.Current?.Prefix : null;
            systemPageTarget = null;
            systemPageTargetIndex = null;
            showNextSystemPageOnUpdate = false;
            orderedSystemNumbers = [];
            systemNumberPositions = new Dictionary<int, int>();
            currentDeferredSystemCount = 0;
            SystemListNote = string.Empty;
            SystemPageText = "Page 1 of 1";
            Systems = [];
            RaiseSystemPageState();
            return;
        }

        var systemsByNumber = state.Systems.ToDictionary(
            system => system.Boxel.N2);
        var totalRowCount = Math.Max(1, GetSystemRowCount());
        (orderedSystemNumbers, currentDeferredSystemCount) =
            GetOrderedSystemNumbers(totalRowCount);
        systemNumberPositions = currentDeferredSystemCount == 0
            ? new Dictionary<int, int>()
            : orderedSystemNumbers
                .Select((number, position) => (number, position))
                .ToDictionary(entry => entry.number, entry => entry.position);
        var rowCount = orderedSystemNumbers.Length;
        var nextSystemPageIndex = GetNextSystemPageIndex();
        var nextSystemLocationChanged = !string.Equals(
                systemPageTarget,
                state.NextSystem,
                StringComparison.Ordinal)
            || systemPageTargetIndex != nextSystemPageIndex;
        if (!ShowOnlyDeferred
            && (!string.Equals(
                systemPagePrefix,
                state.Current.Prefix,
                StringComparison.Ordinal)
                || showNextSystemPageOnUpdate
                || nextSystemLocationChanged))
        {
            systemPageIndex = nextSystemPageIndex;
        }

        systemPagePrefix = state.Current.Prefix;
        systemPageTarget = state.NextSystem;
        systemPageTargetIndex = nextSystemPageIndex;
        showNextSystemPageOnUpdate = false;
        var pageCount = Math.Max(1, (rowCount + SystemsPerPage - 1) / SystemsPerPage);
        systemPageIndex = Math.Clamp(systemPageIndex, 0, pageCount - 1);
        var pageOffset = systemPageIndex * SystemsPerPage;
        var rowsOnPage = Math.Min(SystemsPerPage, Math.Max(0, rowCount - pageOffset));
        var rowNumbers = orderedSystemNumbers
            .Skip(pageOffset)
            .Take(rowsOnPage)
            .ToArray();
        SystemListNote = FormatSystemListNote(
            totalRowCount,
            pageOffset,
            rowsOnPage,
            rowNumbers);
        SystemPageText = string.Create(
            CultureInfo.CurrentCulture,
            $"Page {systemPageIndex + 1:N0} of {pageCount:N0}");
        var resolvedCurrentSystemName = TryParseBoxelInput(
                CurrentSystemName,
                out var currentSystem)
            ? currentSystem?.Name
            : null;
        var nextSystemName = state.NextSystem;
        Systems = rowNumbers
            .Select(number =>
            {
                systemsByNumber.TryGetValue(number, out var system);
                var boxel = system?.Boxel ?? state.Current.WithSystemNumber(number);
                var distance = system?.Position is { } position
                    && currentPosition is { } from
                        ? $"{from.DistanceTo(position):N2} ly"
                        : Unavailable;
                var isCurrent = string.Equals(
                    resolvedCurrentSystemName,
                    boxel.Name,
                    StringComparison.Ordinal);
                return new BoxelSystemRowViewModel(
                    new BoxelSystemRowOptions
                    {
                        Name = boxel.Name,
                        IsComplete = system?.IsComplete == true,
                        IsKnown = system is not null,
                        IsEmpty = state.EmptySystems.Contains(boxel.GeneratedName),
                        IsDeferred = state.IsSystemDeferred(boxel.Prefix, boxel.N2),
                        IsCurrent = isCurrent,
                        IsNextIncomplete = string.Equals(
                            nextSystemName,
                            boxel.Name,
                            StringComparison.Ordinal),
                        Distance = distance,
                        VisitedAt = FormatDate(system?.VisitedAt),
                        SpanshUpdatedAt = FormatDate(system?.SpanshUpdatedAt),
                        Complete = () => CompleteSystemAsync(boxel.Name),
                        Reopen = () => ReopenSystemAsync(boxel.Name),
                        Defer = () => DeferSystemAsync(boxel.Name),
                        StartHere = () => StartAtSystemAsync(boxel.Name),
                    });
            })
            .ToArray();
        RaiseSystemPageState();
    }

    private int GetSystemRowCount()
    {
        return state.Current is null || state.CurrentIsEmpty
            ? 0
            : Math.Max(state.CurrentCount, state.CurrentMaximumSystemNumber + 1);
    }

    private (int[] Numbers, int DeferredCount) GetOrderedSystemNumbers(
        int rowCount)
    {
        var numbers = SortDescending
            ? Enumerable.Range(0, rowCount).Reverse()
            : Enumerable.Range(0, rowCount);
        var ordered = numbers.ToArray();
        if (state.Current is null)
        {
            return (ordered, 0);
        }

        var deferredNumbers = ordered
            .Where(number => state.IsSystemDeferred(state.Current.Prefix, number))
            .ToArray();
        if (ShowOnlyDeferred)
        {
            return (deferredNumbers, deferredNumbers.Length);
        }

        var deferredSet = deferredNumbers.ToHashSet();
        var result = ordered
            .Where(number => !deferredSet.Contains(number))
            .Concat(deferredNumbers)
            .ToArray();
        return (result, deferredNumbers.Length);
    }

    private string FormatSystemListNote(
        int totalRowCount,
        int pageOffset,
        int rowsOnPage,
        int[] rowNumbers)
    {
        if (rowsOnPage == 0)
        {
            return ShowOnlyDeferred
                ? "No deferred systems in this boxel."
                : "No systems are available in this boxel.";
        }

        var visibleTotal = orderedSystemNumbers.Length;
        var firstPosition = pageOffset + 1;
        var lastPosition = pageOffset + rowsOnPage;
        var suffixRange = rowNumbers.Length == 1
            ? $"suffix {rowNumbers[0]:N0}"
            : $"suffixes {rowNumbers[0]:N0}\u2013{rowNumbers[^1]:N0}";
        if (ShowOnlyDeferred)
        {
            return $"Showing deferred systems {firstPosition:N0}\u2013{lastPosition:N0} "
                + $"of {visibleTotal:N0} ({suffixRange}).";
        }

        if (currentDeferredSystemCount == 0)
        {
            return SortDescending
                ? $"Showing systems {rowNumbers[0]:N0}\u2013{rowNumbers[^1]:N0} "
                    + $"of {totalRowCount:N0} (descending)."
                : $"Showing systems {rowNumbers[0]:N0}\u2013{rowNumbers[^1]:N0} "
                    + $"of {totalRowCount:N0}.";
        }

        var direction = SortDescending ? " Descending order is active." : string.Empty;
        return $"Showing systems {firstPosition:N0}\u2013{lastPosition:N0} "
            + $"of {totalRowCount:N0} ({suffixRange}). Deferred systems are grouped last."
            + direction;
    }

    private int GetNextSystemPageIndex()
    {
        if (state.Current is null || string.IsNullOrWhiteSpace(state.NextSystem))
        {
            return 0;
        }

        var known = state.Systems.FirstOrDefault(system =>
            string.Equals(system.Boxel.Name, state.NextSystem, StringComparison.Ordinal)
            || string.Equals(
                system.Boxel.GeneratedName,
                state.NextSystem,
                StringComparison.Ordinal));
        if (known is not null)
        {
            return GetSystemPageIndex(known.Boxel.N2);
        }

        return BoxelAddress.TryParse(state.NextSystem, out var next)
            && next is not null
            && string.Equals(next.Prefix, state.Current.Prefix, StringComparison.Ordinal)
                ? GetSystemPageIndex(next.N2)
                : 0;
    }

    private int GetSystemPageIndex(int systemNumber)
    {
        if (currentDeferredSystemCount == 0 && !ShowOnlyDeferred)
        {
            var rowCount = GetSystemRowCount();
            var calculatedPosition = SortDescending
                ? rowCount - systemNumber - 1
                : systemNumber;
            return calculatedPosition >= 0 && calculatedPosition < rowCount
                ? calculatedPosition / SystemsPerPage
                : 0;
        }

        return systemNumberPositions.TryGetValue(systemNumber, out var cachedPosition)
            ? cachedPosition / SystemsPerPage
            : 0;
    }

    private string FormatLastSystemAvailable()
    {
        return Math.Max(0, state.CurrentCount - 1)
            .ToString(CultureInfo.CurrentCulture);
    }

    private static bool TryParseLastSystemAvailable(
        string? value,
        out int parsedLastSystemAvailable)
    {
        return int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.CurrentCulture,
                out parsedLastSystemAvailable)
            && parsedLastSystemAvailable <= MaximumLastSystemAvailable;
    }

    private string GetLastSystemAvailableValidationMessage()
    {
        if (!hasUnappliedLastSystemAvailableEdit)
        {
            return string.Empty;
        }

        if (!TryParseLastSystemAvailable(LastSystemAvailable, out var parsed))
        {
            return "Enter numbers only, from 0 to 99,999.";
        }

        return parsed < state.CurrentMaximumSystemNumber
            ? $"Enter {state.CurrentMaximumSystemNumber:N0} or higher; that suffix is already recorded."
            : string.Empty;
    }

    private void SetLastSystemAvailableEditState(bool hasUnappliedEdit)
    {
        hasUnappliedLastSystemAvailableEdit = hasUnappliedEdit;
        OnPropertyChanged(nameof(HasLastSystemAvailableError));
        OnPropertyChanged(nameof(LastSystemAvailableValidationMessage));
        applyLastSystemAvailableCommand.RaiseCanExecuteChanged();
    }

    private Task ChangeSystemPageAsync(int offset)
    {
        systemPageIndex = Math.Clamp(
            systemPageIndex + offset,
            0,
            SystemPageCount - 1);
        UpdateSystemRows();
        return Task.CompletedTask;
    }

    private bool CanShowNextJumpPage()
    {
        return !IsBusy
            && HasSystems
            && !ShowOnlyDeferred
            && !string.IsNullOrWhiteSpace(state.NextSystem)
            && systemPageIndex != GetNextSystemPageIndex();
    }

    private Task ShowNextJumpPageAsync()
    {
        systemPageIndex = Math.Clamp(
            GetNextSystemPageIndex(),
            0,
            SystemPageCount - 1);
        UpdateSystemRows();
        return Task.CompletedTask;
    }

    private void RaiseSystemPageState()
    {
        if (SystemPageNumbers.Count != SystemPageCount)
        {
            SystemPageNumbers = Enumerable.Range(1, SystemPageCount).ToArray();
            OnPropertyChanged(nameof(SystemPagePickerWidth));
        }

        OnPropertyChanged(nameof(SystemPageNumber));
        OnPropertyChanged(nameof(SystemPageCount));
        OnPropertyChanged(nameof(SelectedSystemPageIndex));
        nextJumpPageCommand.RaiseCanExecuteChanged();
        previousSystemPageCommand.RaiseCanExecuteChanged();
        nextSystemPageCommand.RaiseCanExecuteChanged();
    }

    private void UpdateNavigation()
    {
        if (state.TopBoxel is null || state.Current is null)
        {
            SetBreadcrumbBoxels([]);
            ChildBoxels = [];
            CurrentHierarchyBoxel = null;
            ParentBoxel = null;
            PreviousSiblingBoxel = null;
            NextSiblingBoxel = null;
            SiblingPosition = "Search root";
            RaiseNavigationBindings();
            return;
        }

        var path = GetBreadcrumbPath(state.TopBoxel, state.Current);
        var breadcrumbs = path
            .Select(GetNavigationOption)
            .ToArray();
        SetBreadcrumbBoxels(breadcrumbs);
        CurrentHierarchyBoxel = breadcrumbs[^1];
        ParentBoxel = breadcrumbs.Length > 1
            ? breadcrumbs[^2]
            : null;

        var siblings = Array.Empty<BoxelAddress>();
        var siblingIndex = -1;
        if (ParentBoxel is not null)
        {
            siblings = state.Current.Parent.Children.ToArray();
            siblingIndex = Array.FindIndex(siblings, sibling => string.Equals(
                sibling.Prefix,
                state.Current.Prefix,
                StringComparison.Ordinal));
        }

        PreviousSiblingBoxel = siblingIndex > 0
            ? GetNavigationOption(siblings[siblingIndex - 1])
            : null;
        NextSiblingBoxel = siblingIndex >= 0
            && siblingIndex + 1 < siblings.Length
                ? GetNavigationOption(siblings[siblingIndex + 1])
                : null;
        SiblingPosition = siblingIndex >= 0
            ? $"{siblingIndex + 1:N0} of {siblings.Length:N0} at this level"
            : "Search root";

        ChildBoxels = state.Current.MassCode > state.LowMassCode
            ? state.Current.Children
                .Select(GetNavigationOption)
                .ToArray()
            : [];
        RaiseNavigationBindings();
    }

    private void RaiseNavigationBindings()
    {
        OnPropertyChanged(nameof(CurrentHierarchyBoxelLabel));
        OnPropertyChanged(nameof(CurrentHierarchyBoxelProgressLabel));
        OnPropertyChanged(nameof(ParentBoxelLabel));
        OnPropertyChanged(nameof(PreviousSiblingBoxelLabel));
        OnPropertyChanged(nameof(NextSiblingBoxelLabel));
    }

    private BoxelNavigationOptionViewModel GetNavigationOption(BoxelAddress boxel)
    {
        if (!navigationOptions.TryGetValue(boxel.Prefix, out var option))
        {
            option = new BoxelNavigationOptionViewModel(
                boxel.Prefix,
                () => NavigateAsync(boxel));
            navigationOptions.Add(boxel.Prefix, option);
        }

        option.Update(
            state.GetProgress(boxel),
            string.Equals(
                state.Current?.Prefix,
                boxel.Prefix,
                StringComparison.Ordinal));
        return option;
    }

    private static List<BoxelAddress> GetBreadcrumbPath(
        BoxelAddress topBoxel,
        BoxelAddress current)
    {
        if (!topBoxel.Contains(current))
        {
            return [topBoxel];
        }

        var path = new List<BoxelAddress> { current };
        var cursor = current;
        while (!string.Equals(
            cursor.Prefix,
            topBoxel.Prefix,
            StringComparison.Ordinal))
        {
            var parent = cursor.Parent;
            if (string.Equals(
                parent.Prefix,
                cursor.Prefix,
                StringComparison.Ordinal))
            {
                break;
            }

            cursor = parent;
            path.Add(parent);
        }

        path.Reverse();
        return path;
    }

    private void SetBreadcrumbBoxels(
        IReadOnlyList<BoxelNavigationOptionViewModel> next)
    {
        if (NavigationListsMatch(breadcrumbBoxels, next))
        {
            return;
        }

        breadcrumbBoxels = next;
        OnPropertyChanged(nameof(BreadcrumbBoxels));
    }

    private static bool NavigationListsMatch(
        IReadOnlyList<BoxelNavigationOptionViewModel> left,
        IReadOnlyList<BoxelNavigationOptionViewModel> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!ReferenceEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateSearchSize()
    {
        if (!TryParseBoxelInput(TopBoxelText, out var boxel)
            || boxel is null
            || string.IsNullOrWhiteSpace(LowMassCode)
            || !BoxelAddress.IsValidMassCode(
                char.ToLowerInvariant(LowMassCode[0]))
            || char.ToLowerInvariant(LowMassCode[0]) > boxel.MassCode)
        {
            SearchSize = "Enter a valid generated system and lower mass code.";
            return;
        }

        var count = BoxelAddress.GetTotalChildCount(
            boxel.MassCode - char.ToLowerInvariant(LowMassCode[0]));
        SearchSize = $"{count:N0} boxel{(count == 1 ? string.Empty : "s")} "
            + $"from mass code {boxel.MassCode} through {LowMassCode}.";
    }

    private void ScheduleSystemSuggestions(string? value)
    {
        CancelSystemSuggestions();
        var query = value?.Trim() ?? string.Empty;
        if (systemNameSuggestionClient is null
            || query.Length < 3
            || string.Equals(
                query,
                selectedSystemName,
                StringComparison.OrdinalIgnoreCase))
        {
            SystemNameSuggestions = [];
            SelectedSystemSuggestionIndex = -1;
            IsSearchingSystemSuggestions = false;
            SystemSuggestionStatus = query.Length is > 0 and < 3
                ? "Type at least 3 characters for system suggestions."
                : string.Empty;
            return;
        }

        var cancellation = new CancellationTokenSource();
        systemSuggestionCancellation = cancellation;
        _ = LoadSystemSuggestionsAsync(query, cancellation);
    }

    private async Task LoadSystemSuggestionsAsync(
        string query,
        CancellationTokenSource cancellation)
    {
        var suggestionClient = systemNameSuggestionClient;
        if (suggestionClient is null)
        {
            return;
        }

        try
        {
            IsSearchingSystemSuggestions = true;
            SystemSuggestionStatus = "Searching for system suggestions…";
            await Task.Delay(systemSuggestionDelay, cancellation.Token);
            var suggestions = await suggestionClient.SearchAsync(
                query,
                cancellation.Token);
            if (!ReferenceEquals(systemSuggestionCancellation, cancellation)
                || !string.Equals(
                    TopBoxelText.Trim(),
                    query,
                    StringComparison.Ordinal))
            {
                return;
            }

            SystemNameSuggestions = suggestions;
            SelectedSystemSuggestionIndex = suggestions.Count > 0 ? 0 : -1;
            SystemSuggestionStatus = BuildSystemSuggestionStatus(suggestions);
        }
        catch (OperationCanceledException)
        {
            // A newer input superseded this request.
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or System.Text.Json.JsonException)
        {
            if (ReferenceEquals(systemSuggestionCancellation, cancellation))
            {
                SystemNameSuggestions = [];
                SelectedSystemSuggestionIndex = -1;
                SystemSuggestionStatus =
                    "System suggestions are temporarily unavailable; you can still enter a system name manually.";
            }
        }
        finally
        {
            if (ReferenceEquals(systemSuggestionCancellation, cancellation))
            {
                systemSuggestionCancellation = null;
                IsSearchingSystemSuggestions = false;
            }

            cancellation.Dispose();
        }
    }

    private static string BuildSystemSuggestionStatus(
        IReadOnlyList<SystemNameSuggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            return "No matching systems found.";
        }

        var pluralSuffix = suggestions.Count == 1 ? string.Empty : "s";
        return $"{suggestions.Count:N0} system suggestion{pluralSuffix} "
            + $"from {suggestions[0].Source}.";
    }

    private void CancelSystemSuggestions()
    {
        var cancellation = systemSuggestionCancellation;
        systemSuggestionCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
        }
    }

    private void RaiseCommandStates()
    {
        activateCommand.RaiseCanExecuteChanged();
        disableCommand.RaiseCanExecuteChanged();
        refreshCommand.RaiseCanExecuteChanged();
        copyNextCommand.RaiseCanExecuteChanged();
        markNextEmptyCommand.RaiseCanExecuteChanged();
        applyLastSystemAvailableCommand.RaiseCanExecuteChanged();
        nextJumpPageCommand.RaiseCanExecuteChanged();
        navigateParentCommand.RaiseCanExecuteChanged();
        navigatePreviousCommand.RaiseCanExecuteChanged();
        navigateNextCommand.RaiseCanExecuteChanged();
        previousSystemPageCommand.RaiseCanExecuteChanged();
        nextSystemPageCommand.RaiseCanExecuteChanged();
        auditAllCommand.RaiseCanExecuteChanged();
        cancelAuditCommand.RaiseCanExecuteChanged();
    }

    private bool TryParseBoxelInput(
        string? value,
        out BoxelAddress? boxel)
    {
        var systemName = value?.Trim();
        var normalized = systemName;
        if (normalized?.EndsWith('-') == true)
        {
            normalized += "0";
        }

        if (BoxelAddress.TryParse(normalized, out boxel))
        {
            return true;
        }

        if (selectedSystemAddress > 0
            && string.Equals(
                systemName,
                selectedSystemName,
                StringComparison.OrdinalIgnoreCase)
            && BoxelAddress.TryFromSystemAddress(
                selectedSystemAddress,
                selectedSystemName,
                out boxel))
        {
            return true;
        }

        return knownSystems.TryResolve(systemName, out var systemAddress)
            && BoxelAddress.TryFromSystemAddress(
                systemAddress,
                systemName,
                out boxel);
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            ?? Unavailable;
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

public sealed class BoxelSystemRowViewModel
{
    private readonly Func<Task> complete;
    private readonly Func<Task> defer;
    private readonly Func<Task> reopen;
    private readonly Func<Task> startHere;

    public BoxelSystemRowViewModel(BoxelSystemRowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        complete = options.Complete;
        defer = options.Defer;
        reopen = options.Reopen;
        startHere = options.StartHere;
        Name = options.Name;
        IsComplete = options.IsComplete;
        IsKnown = options.IsKnown;
        IsEmpty = options.IsEmpty;
        IsDeferred = options.IsDeferred;
        IsCurrent = options.IsCurrent;
        IsNextIncomplete = options.IsNextIncomplete;
        Distance = options.Distance;
        VisitedAt = options.VisitedAt;
        SpanshUpdatedAt = options.SpanshUpdatedAt;
        if (options.IsDeferred)
        {
            Status = "DEFERRED";
        }
        else if (options.IsEmpty)
        {
            Status = "EMPTY";
        }
        else if (options.IsComplete)
        {
            Status = "COMPLETE";
        }
        else if (options.IsKnown)
        {
            Status = "KNOWN";
        }
        else
        {
            Status = "UNKNOWN";
        }
        CompleteCommand = new RowCommand(
            CompleteAsync,
            () => options.IsKnown
                && !options.IsComplete
                && !options.IsEmpty);
        ReopenCommand = new RowCommand(
            ReopenAsync,
            () => options.IsComplete || options.IsEmpty || options.IsDeferred);
        DeferCommand = new RowCommand(
            DeferAsync,
            () => !options.IsComplete
                && !options.IsEmpty
                && !options.IsDeferred);
        StartHereCommand = new RowCommand(
            StartHereAsync,
            () => !options.IsComplete && !options.IsEmpty);
    }

    public string Name { get; }

    public bool IsComplete { get; }

    public bool IsKnown { get; }

    public bool IsEmpty { get; }

    public bool IsDeferred { get; }

    public bool IsCurrent { get; }

    public bool IsNextIncomplete { get; }

    public bool ShowNextIncompleteHighlight => IsNextIncomplete && !IsCurrent;

    public bool ShowCurrentNextHighlight => IsNextIncomplete && IsCurrent;

    public bool HasRowIndicator => IsCurrent || IsNextIncomplete;

    public string RowIndicator => (IsCurrent, IsNextIncomplete) switch
    {
        (true, true) => "CURRENT SYSTEM · NEXT INCOMPLETE SYSTEM",
        (true, false) => "CURRENT SYSTEM",
        (false, true) => "NEXT INCOMPLETE SYSTEM",
        _ => string.Empty,
    };

    public string Distance { get; }

    public string VisitedAt { get; }

    public string SpanshUpdatedAt { get; }

    public string Status { get; }

    public ICommand CompleteCommand { get; }

    public ICommand ReopenCommand { get; }

    public ICommand DeferCommand { get; }

    public ICommand StartHereCommand { get; }

    internal Task CompleteAsync() => complete();

    internal Task DeferAsync() => defer();

    internal Task ReopenAsync() => reopen();

    internal Task StartHereAsync() => startHere();

    private sealed class RowCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { /* Availability is evaluated when the command is queried. */ }
            remove { /* Availability is evaluated when the command is queried. */ }
        }

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
    }
}

public sealed class BoxelNavigationOptionViewModel : INotifyPropertyChanged
{
    private readonly Func<Task> navigate;
    private string progressLabel = "Not searched";
    private string statusLabel = "NOT STARTED";
    private bool isCurrent;

    public BoxelNavigationOptionViewModel(
        string label,
        Func<Task> navigate)
    {
        Label = label;
        this.navigate = navigate;
        NavigateCommand = new NavigationCommand(NavigateAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public string ProgressLabel
    {
        get => progressLabel;
        private set => SetField(ref progressLabel, value);
    }

    public string StatusLabel
    {
        get => statusLabel;
        private set => SetField(ref statusLabel, value);
    }

    public bool IsCurrent
    {
        get => isCurrent;
        private set
        {
            if (SetField(ref isCurrent, value))
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(CanNavigate)));
            }
        }
    }

    public bool CanNavigate => !IsCurrent;

    public ICommand NavigateCommand { get; }

    public Task NavigateAsync() => navigate();

    public void Update(BoxelProgress progress, bool current)
    {
        IsCurrent = current;
        if (progress.IsEmpty)
        {
            ProgressLabel = "Marked empty";
            StatusLabel = "EMPTY";
        }
        else if (progress.IsComplete)
        {
            ProgressLabel = $"{progress.ExpectedSystemCount:N0} of "
                + $"{progress.ExpectedSystemCount:N0} systems complete";
            StatusLabel = "COMPLETE";
        }
        else if (progress.ExpectedSystemCount <= 0)
        {
            ProgressLabel = "Not searched";
            StatusLabel = "NOT STARTED";
        }
        else
        {
            ProgressLabel = $"{progress.CompletedSystemCount:N0} of "
                + $"{progress.ExpectedSystemCount:N0} systems complete";
            StatusLabel = progress.CompletedSystemCount > 0
                ? "IN PROGRESS"
                : "NOT STARTED";
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private sealed class NavigationCommand(Func<Task> navigate) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { /* This command is always executable. */ }
            remove { /* This command is always executable. */ }
        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public async void Execute(object? parameter)
        {
            await navigate();
        }
    }
}

public enum SaveBoxelProgressResult
{
    Unavailable,
    RequiresDetails,
    Saved,
    Failed,
}
