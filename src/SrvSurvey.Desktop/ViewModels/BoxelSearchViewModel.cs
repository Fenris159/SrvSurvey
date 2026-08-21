using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BoxelSearchViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";
    private const int SystemsPerPage = 10;
    private const int MaximumLastSystemAvailable = 99_999;
    private const int LargeAuditConfirmationThreshold = 1_000;

    private readonly IBoxelSearchSession session;
    private readonly ISystemNameSuggestionClient? systemNameSuggestionClient;
    private readonly TimeSpan systemSuggestionDelay;
    private readonly KnownSystemAddressCatalog knownSystems;
    private readonly BoxelSurveyStatsCoordinator? surveyStats;
    private BoxelSearchSessionSearchSnapshot searchState;
    private readonly Dictionary<string, BoxelNavigationOptionViewModel>
        navigationOptions = new(StringComparer.Ordinal);
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
    private volatile bool isActivating;
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
    private IReadOnlyList<string> searchPrefixes = [];
    private bool surveyStatsUnsubscribed;
    private bool sessionUnsubscribed;
    private Task pendingOptionUpdate = Task.CompletedTask;
    private long appliedSessionVersion = -1;
    private long appliedProfileGeneration = -1;
    private NavRouteSnapshot? latestRoute;
    private EliteStatus? status;
    private string? musicTrack;
    private StatusDestination? lastDestination;
    private string destinationStatus = "No Galaxy Map destination selected";
    private bool isDestinationValid;
    private string? lastCopiedSystemName;
    private string statsGlanceText = string.Empty;

    public BoxelSearchViewModel(
        IBoxelSearchSession session,
        KnownSystemAddressCatalog? knownSystems = null,
        ISystemNameSuggestionClient? systemNameSuggestionClient = null,
        TimeSpan? systemSuggestionDelay = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        searchState = session.Current.Search;
        this.session.Changed += OnSessionChanged;
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
        this.knownSystems = knownSystems
            ?? KnownSystemAddressCatalog.Empty;
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
            () => IsAuditing);
        CancelAuditCommand = cancelAuditCommand;
        ApplySessionSnapshot(session.Current);
        UpdateDisplay();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
        get => searchState.TopBoxel is not null && pendingOptionUpdate.IsCompleted
            ? session.Current.Search.AutoCopy
            : autoCopy;
        set
        {
            if (AutoCopy == value || suppressOptionPersistence)
            {
                return;
            }

            SetField(ref autoCopy, value);
            RaiseOverlayProperties();
            if (searchState.TopBoxel is null)
            {
                return;
            }

            RunSessionAction(new SetBoxelAutoCopy(value));
        }
    }

    public bool SortDescending
    {
        get => sortDescending;
        set
        {
            if (SortDescending == value || suppressOptionPersistence)
            {
                return;
            }

            SetField(ref sortDescending, value);
            showNextSystemPageOnUpdate = true;
            NextSystem = GetPresentedNextSystem() ?? Unavailable;
            UpdateSystemRows();
            if (searchState.TopBoxel is null)
            {
                return;
            }

            RunSessionAction(new SetBoxelSortDirection(value));
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

    public bool IsActive => searchState.IsActive;

    public BoxelSurveyStatsCoordinator? SurveyStats => surveyStats;

    internal IBoxelSearchSession Session => session;

    public IReadOnlyList<string> SearchPrefixes => searchPrefixes;

    public char SearchLowMassCode => searchState.LowMassCode;

    public string? CurrentBoxelPrefix => searchState.CurrentBoxel?.Prefix;

    public int CurrentExpectedSystemCount => searchState.CurrentCount;

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
            searchState.IsActive,
            searchState.CompletionMode,
            searchState.CompletedSystemCount,
            Math.Max(searchState.CurrentCount, searchState.Systems.Count),
            searchState.CurrentSystemsComplete,
            searchState.NextSystem);
    }

    public bool ShouldShowGalaxyMapOverlay => IsGalaxyMapOpen && searchState.IsActive;

    private bool IsGalaxyMapOpen => OverlayGameModeResolver.Resolve(
        status,
        musicTrack: musicTrack) == OverlayGameMode.GalaxyMap;

    public string? NextSystemForInput => searchState.NextSystem;

    public bool ShouldPasteNextSystem => ShouldShowGalaxyMapOverlay
        && !AutoCopy
        && searchState.NextSystem is not null
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
        GetPresentedNextSystem(),
        StringComparison.Ordinal)
            ? "NEXT SEARCH COPIED"
            : AutoCopy switch
            {
                true => "AUTO-COPY READY",
                false => "MANUAL COPY"
            };

    public bool RequiresManualCopy => !AutoCopy
        && !string.Equals(
            lastCopiedSystemName,
            GetPresentedNextSystem(),
            StringComparison.Ordinal);

    public bool IsCurrentEmpty => searchState.CurrentIsEmpty;

    public string StatusLabel => searchState.IsActive ? "ACTIVE" : "INACTIVE";

    public string RefreshButtonText => IsBusy && !IsAuditing
        ? "Refreshing\u2026"
        : "Refresh boxel";

    public string AuditButtonText => IsAuditing ? "Auditing\u2026" : "Audit all boxels";

    public bool ShowLargeAuditConfirmation => searchState.TotalBoxelCount
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
            || parsed < searchState.CurrentMaximumSystemNumber);

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

    public bool CanNavigateSearchTree => searchState.IsActive
        && searchState.TotalBoxelCount > 1;

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

    public bool CanSaveProgress => session.Current.Context.Profile is not null
        && searchState.TopBoxel is not null
        && searchState.SavedSearchFileName is null;

    public bool IsSavedToLibrary => searchState.SavedSearchFileName is not null;

    public string LibrarySaveButtonText => IsSavedToLibrary
        ? "Saved to Library"
        : "Save to Library";

    public string SuggestedSaveName => searchState.TopBoxel?.Name
        ?? TopBoxelText.Trim();

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
        var outcome = await SwitchSessionProfileAsync(new BoxelSearchProfile(
            profileFrontierId,
            profileCommanderName,
            profileIsOdyssey,
            snapshot));
        ApplyOutcome(outcome);
    }

    public async Task SetProfileErrorAsync(string message)
    {
        await ClearSessionProfileAsync();
        ApplySessionSnapshot(session.Current);
        StatusMessage = message;
    }

    public void ReportSaveProgressFailure(string message)
    {
        StatusMessage = "The boxel search could not be saved: " + message;
    }

    public async Task UpdateCurrentSystemAsync(
        string? systemName,
        GalacticCoordinate? position,
        long? systemAddress = null)
    {
        await ApplySessionUpdateAsync(new BoxelSearchUpdate
        {
            HasCurrentSystem = true,
            CurrentSystemName = systemName,
            CurrentPosition = position,
            CurrentSystemAddress = systemAddress,
        });
        ApplySessionSnapshot(session.Current);
    }

    public async Task UpdateRouteAsync(NavRouteSnapshot? route)
    {
        await ApplySessionUpdateAsync(new BoxelSearchUpdate
        {
            HasRoute = true,
            Route = route,
        });
        ApplySessionSnapshot(session.Current);
    }

    public async Task ApplyJournalEventsAsync(
        IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        await ApplySessionUpdateAsync(new BoxelSearchUpdate
        {
            JournalEvents = journalEvents as IReadOnlyList<JournalEventEnvelope>
                ?? journalEvents.ToArray(),
        });
        ApplySessionSnapshot(session.Current);
    }

    public async Task UpdateStatusAsync(
        EliteStatus nextStatus,
        bool allowAutoCopy = true,
        string? nextMusicTrack = null)
    {
        ArgumentNullException.ThrowIfNull(nextStatus);
        var outcome = await ApplySessionUpdateAsync(new BoxelSearchUpdate
        {
            HasStatus = true,
            Status = nextStatus,
            MusicTrack = nextMusicTrack,
            IsGalaxyMapOpen = OverlayGameModeResolver.Resolve(
                nextStatus,
                musicTrack: nextMusicTrack) == OverlayGameMode.GalaxyMap,
            AllowAutoCopy = allowAutoCopy,
        });
        ApplyOutcome(outcome);
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
        SetLastSystemAvailableEditState(false);
        isActivating = true;
        try
        {
            var outcome = await ExecuteSessionActionAsync(new ActivateBoxelSearch(
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
                    SortDescending = SortDescending,
                }));

            ApplySessionSnapshot(session.Current);
            SetField(
                ref lastSystemAvailable,
                FormatLastSystemAvailable(),
                nameof(LastSystemAvailable));
            ApplyOutcome(outcome);
        }
        finally
        {
            isActivating = false;
        }
    }

    public async Task DisableAsync()
    {
        SetLastSystemAvailableEditState(false);
        ApplyOutcome(await ExecuteSessionActionAsync(StopBoxelSearch.Instance));
    }

    public async Task<SaveBoxelProgressResult> SaveProgressAsync(
        string? name = null,
        string? notes = null)
    {
        var outcome = await ExecuteSessionActionAsync(
            new SaveBoxelSearchToLibrary(name, notes));
        ApplyOutcome(outcome);
        return outcome.Code switch
        {
            BoxelSearchMessageCode.LibraryDetailsRequired =>
                SaveBoxelProgressResult.RequiresDetails,
            BoxelSearchMessageCode.SearchSavedToLibrary
                or BoxelSearchMessageCode.SearchAlreadySavedToLibrary =>
                SaveBoxelProgressResult.Saved,
            BoxelSearchMessageCode.SearchNotConfigured =>
                SaveBoxelProgressResult.Unavailable,
            _ => SaveBoxelProgressResult.Failed,
        };
    }

    public async Task<IReadOnlyList<SavedBoxelSearchCatalogEntry>>
        ListSavedSearchesAsync()
    {
        return (await GetSessionLibraryAsync()).Entries;
    }

    public async Task<SavedBoxelSearchDocument> RenameSavedSearchAsync(
        string fileName,
        string name)
    {
        return RequireSavedSearch(await ExecuteSessionActionAsync(
            new RenameSavedBoxelSearch(fileName, name)));
    }

    public async Task<SavedBoxelSearchDocument> SaveSavedSearchNotesAsync(
        string fileName,
        string? notes)
    {
        return RequireSavedSearch(await ExecuteSessionActionAsync(
            new UpdateSavedBoxelSearchNotes(fileName, notes)));
    }

    public async Task<SavedBoxelSearchDocument> SetSavedSearchFavoriteAsync(
        string fileName,
        bool isFavorite)
    {
        return RequireSavedSearch(await ExecuteSessionActionAsync(
            new SetSavedBoxelSearchFavorite(fileName, isFavorite)));
    }

    public async Task DeleteSavedSearchAsync(string fileName)
    {
        var outcome = await ExecuteSessionActionAsync(new DeleteSavedBoxelSearch(fileName));
        ApplyOutcome(outcome);
        ThrowForRejectedLibraryOutcome(outcome);
    }

    public async Task ResumeSavedSearchAsync(string fileName)
    {
        var outcome = await ExecuteSessionActionAsync(new ResumeSavedBoxelSearch(fileName));
        ApplyOutcome(outcome);
        ThrowForRejectedLibraryOutcome(outcome);
    }

    public async Task DisableAutoCopyForCompetingRouteAsync()
    {
        if (!AutoCopy)
        {
            return;
        }

        var outcome = await ExecuteSessionActionAsync(new SetBoxelAutoCopy(false));
        ApplyOutcome(outcome, competingAutoCopy: true);
    }

    public async Task RefreshCurrentAsync()
    {
        ApplyOutcome(await ExecuteSessionActionAsync(new RefreshCurrentBoxel()));
    }

    public async Task AuditAllAsync()
    {
        if (!CanAuditAll())
        {
            StatusMessage = GetAuditUnavailableStatus();
            return;
        }

        ApplyOutcome(await ExecuteSessionActionAsync(new AuditAllBoxels()));
    }

    private string GetAuditUnavailableStatus()
    {
        return ShowLargeAuditConfirmation && !ConfirmLargeAudit
            ? "Confirm the large network audit before starting it."
            : "Activate a boxel search before auditing its full area.";
    }

    public async Task CancelAuditAsync()
    {
        ApplyOutcome(await ExecuteSessionActionAsync(new CancelBoxelAudit()));
    }

    public void CancelPendingOperations()
    {
        CancelSystemSuggestions();
        if (!sessionUnsubscribed)
        {
            session.Changed -= OnSessionChanged;
            sessionUnsubscribed = true;
        }

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

    private void OnSessionChanged(
        object? sender,
        BoxelSearchSessionChangedEventArgs eventArgs)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySessionSnapshot(eventArgs.Current, eventArgs.Previous);
            return;
        }

        Dispatcher.UIThread.Post(
            () => ApplySessionSnapshot(eventArgs.Current, eventArgs.Previous));
    }

    private void ApplySessionSnapshot(
        BoxelSearchSessionSnapshot snapshot,
        BoxelSearchSessionSnapshot? previous = null)
    {
        if (snapshot.Version <= appliedSessionVersion)
        {
            return;
        }

        appliedSessionVersion = snapshot.Version;
        var profileGeneration = snapshot.Context.Profile?.Generation ?? -1;
        if (profileGeneration != appliedProfileGeneration)
        {
            navigationOptions.Clear();
            appliedProfileGeneration = profileGeneration;
        }

        var searchChanged = !ReferenceEquals(searchState, snapshot.Search);
        searchState = snapshot.Search;
        latestRoute = snapshot.Context.Route;
        status = snapshot.Context.Status;
        musicTrack = snapshot.Context.MusicTrack;
        lastDestination = snapshot.Context.Status?.Destination;
        lastCopiedSystemName = snapshot.Context.LastCopiedSystemName;
        currentPosition = snapshot.Context.CurrentPosition;
        currentSystemAddress = snapshot.Context.CurrentSystemAddress;
        CurrentSystemName = string.IsNullOrWhiteSpace(snapshot.Context.CurrentSystemName)
            ? Unavailable
            : snapshot.Context.CurrentSystemName;
        OnPropertyChanged(nameof(HasCurrentSystemAddress));
        OnPropertyChanged(nameof(CurrentSystemAddress));
        OnPropertyChanged(nameof(CurrentSystemAddressText));

        if (searchChanged)
        {
            ApplySearchConfiguration(snapshot.Search.Persistence);
            UpdateDisplay();
        }
        else
        {
            UpdateSystemRows();
            RaiseOverlayProperties();
        }

        UpdateDestinationStatus();

        var activity = snapshot.Activity;
        IsAuditing = activity.Kind is BoxelSearchActivityKind.Auditing
            or BoxelSearchActivityKind.CancellingAudit;
        IsBusy = activity.Kind != BoxelSearchActivityKind.Idle;
        AuditProcessed = activity.Processed;
        AuditTotal = Math.Max(1, activity.Total);
        AuditProgress = activity.Kind switch
        {
            BoxelSearchActivityKind.Auditing =>
                $"Audited {activity.Processed:N0} of {activity.Total:N0}: {activity.Prefix}",
            BoxelSearchActivityKind.CancellingAudit =>
                "Cancelling the full-area audit after the current request…",
            _ when activity.Total > 0 =>
                $"Audited {activity.Processed:N0} of {activity.Total:N0} boxels.",
            _ => AuditProgress,
        };

        if (previous is not null
            && !previous.Health.IsHealthy
            && snapshot.Health.IsHealthy)
        {
            StatusMessage = "Boxel search synchronization restored.";
        }
    }

    private void ApplySearchConfiguration(BoxelSearchSnapshot snapshot)
    {
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
            SetField(ref autoCopy, snapshot.AutoCopy, nameof(AutoCopy));
            SetField(ref sortDescending, snapshot.SortDescending, nameof(SortDescending));
        }
        finally
        {
            suppressOptionPersistence = false;
        }
    }

    private void ApplyOutcome(
        BoxelSearchOutcome outcome,
        bool competingAutoCopy = false)
    {
        ApplySessionSnapshot(session.Current);
        ApplyAuditOutcome(outcome);
        StatusMessage = GetOutcomeStatus(outcome, competingAutoCopy);
    }

    private void ApplyAuditOutcome(BoxelSearchOutcome outcome)
    {
        if (outcome.Code is not (BoxelSearchMessageCode.AuditCompleted
            or BoxelSearchMessageCode.AuditCancelled))
        {
            return;
        }

        AuditProcessed = outcome.Count;
        AuditTotal = Math.Max(1, outcome.Total);
        AuditProgress = GetAuditOutcomeStatus(outcome);
    }

    private string GetOutcomeStatus(
        BoxelSearchOutcome outcome,
        bool competingAutoCopy)
    {
        return outcome.Code switch
        {
            BoxelSearchMessageCode.SearchNotConfigured =>
                "No boxel search is configured for this commander.",
            BoxelSearchMessageCode.SearchLoadedInactive =>
                "Loaded the saved boxel search; it is currently disabled.",
            BoxelSearchMessageCode.ProfileLoaded =>
                "Loaded the active boxel search.",
            BoxelSearchMessageCode.ProfileUnavailable =>
                "Waiting for a commander profile.",
            BoxelSearchMessageCode.SearchInvalid =>
                outcome.PrimaryValue ?? "The boxel search configuration is invalid.",
            BoxelSearchMessageCode.SearchStopped =>
                "Boxel search disabled; its progress was retained.",
            BoxelSearchMessageCode.SearchSavedToLibrary =>
                $"Saved boxel search as {outcome.PrimaryValue}.",
            BoxelSearchMessageCode.SearchAlreadySavedToLibrary =>
                "This boxel search is already saved to the library.",
            BoxelSearchMessageCode.LibraryDetailsRequired =>
                StatusMessage,
            BoxelSearchMessageCode.LibraryUnavailable =>
                "The saved boxel search library is temporarily unavailable.",
            BoxelSearchMessageCode.SavedSearchResumed =>
                $"Resumed saved boxel search {outcome.PrimaryValue}.",
            BoxelSearchMessageCode.RefreshCompleted when outcome.Warnings is { Count: > 0 } =>
                $"Refreshed {outcome.Count:N0} known systems with warnings.",
            BoxelSearchMessageCode.RefreshCompleted =>
                $"Refreshed {outcome.Count:N0} known systems in {outcome.PrimaryValue}.",
            BoxelSearchMessageCode.RefreshFailed =>
                "The boxel refresh could not be completed.",
            BoxelSearchMessageCode.AuditCompleted when outcome.Warnings is { Count: > 0 } =>
                $"Audited all {outcome.Total:N0} boxels with {outcome.Warnings.Count:N0} warnings.",
            BoxelSearchMessageCode.AuditCompleted =>
                $"Audited all {outcome.Total:N0} boxels and saved the refreshed progress.",
            BoxelSearchMessageCode.AuditCancelled =>
                GetAuditOutcomeStatus(outcome),
            BoxelSearchMessageCode.AuditFailed =>
                "The full-area audit could not be completed.",
            BoxelSearchMessageCode.ExpectedSystemCountChanged =>
                GetExpectedSystemCountStatus(outcome),
            BoxelSearchMessageCode.SystemCompleted =>
                GetSystemCompletedStatus(outcome),
            BoxelSearchMessageCode.SystemReopened =>
                GetSystemReopenedStatus(outcome),
            BoxelSearchMessageCode.SystemDeferred =>
                GetSystemDeferredStatus(outcome),
            BoxelSearchMessageCode.SurveyStartChanged =>
                GetSurveyStartStatus(outcome),
            BoxelSearchMessageCode.NextSystemMarkedEmpty =>
                GetNextSystemMarkedEmptyStatus(outcome),
            BoxelSearchMessageCode.NextSystemCopied =>
                GetNextSystemCopiedStatus(outcome),
            BoxelSearchMessageCode.ClipboardNotReady =>
                "The desktop clipboard is not available.",
            BoxelSearchMessageCode.ClipboardFailed =>
                "The next system could not be copied.",
            BoxelSearchMessageCode.AutoCopyChanged when competingAutoCopy =>
                "Boxel auto-copy was disabled because another Galaxy Map auto-copy setting was selected.",
            BoxelSearchMessageCode.SynchronizationDegraded =>
                "The boxel search changed for this session but could not be saved.",
            _ => StatusMessage,
        };
    }

    private static string GetAuditOutcomeStatus(BoxelSearchOutcome outcome)
    {
        return outcome.Code == BoxelSearchMessageCode.AuditCancelled
            ? $"Audit cancelled after {outcome.Count:N0} of {outcome.Total:N0} boxels; partial progress was saved."
            : $"Audited all {outcome.Total:N0} boxels and saved the refreshed progress.";
    }

    private static string GetExpectedSystemCountStatus(BoxelSearchOutcome outcome)
    {
        return outcome.Kind == BoxelSearchOutcomeKind.Rejected
            ? $"Last system available cannot be below recorded suffix {outcome.Count:N0}."
            : $"Last system available updated to {outcome.Count:N0}.";
    }

    private static string GetSystemCompletedStatus(BoxelSearchOutcome outcome)
    {
        return outcome.Kind == BoxelSearchOutcomeKind.Rejected
            ? outcome.PrimaryValue ?? "The system was not marked complete."
            : $"Marked {outcome.PrimaryValue} complete.";
    }

    private static string GetSystemReopenedStatus(BoxelSearchOutcome outcome)
    {
        return outcome.Kind == BoxelSearchOutcomeKind.Rejected
            ? outcome.PrimaryValue ?? "The system was not reopened."
            : $"Reopened {outcome.PrimaryValue}.";
    }

    private static string GetSystemDeferredStatus(BoxelSearchOutcome outcome)
    {
        return outcome.Kind == BoxelSearchOutcomeKind.Rejected
            ? outcome.PrimaryValue ?? "The system was not deferred."
            : $"Deferred {outcome.PrimaryValue}.";
    }

    private static string GetSurveyStartStatus(BoxelSearchOutcome outcome)
    {
        if (outcome.Kind == BoxelSearchOutcomeKind.Rejected)
        {
            return outcome.PrimaryValue ?? "The survey start point was not changed.";
        }

        return outcome.Count == 0
            ? $"Survey will start at {outcome.PrimaryValue}."
            : $"Survey will start at {outcome.PrimaryValue}; deferred {outcome.Count:N0} earlier systems.";
    }

    private static string GetNextSystemMarkedEmptyStatus(BoxelSearchOutcome outcome)
    {
        if (outcome.Kind == BoxelSearchOutcomeKind.Rejected)
        {
            return outcome.PrimaryValue
                ?? "The next incomplete system was not marked empty.";
        }

        return string.IsNullOrWhiteSpace(outcome.SecondaryValue)
            ? $"Marked {outcome.PrimaryValue} empty. No incomplete systems remain."
            : $"Marked {outcome.PrimaryValue} empty. Next incomplete system: {outcome.SecondaryValue}.";
    }

    private static string GetNextSystemCopiedStatus(BoxelSearchOutcome outcome)
    {
        return outcome.Kind == BoxelSearchOutcomeKind.Rejected
            ? "No next boxel system is available to copy."
            : $"Copied {outcome.PrimaryValue} to the clipboard.";
    }

    private void RunSessionAction(IBoxelSearchAction action)
    {
        pendingOptionUpdate = RunSessionActionAsync(pendingOptionUpdate, action);
    }

    private Task<BoxelSearchOutcome> SwitchSessionProfileAsync(
        BoxelSearchProfile profile)
    {
        return session.SwitchProfileAsync(profile, CancellationToken.None);
    }

    private Task<BoxelSearchOutcome> ClearSessionProfileAsync()
    {
        return session.ClearProfileAsync(cancellationToken: CancellationToken.None);
    }

    private Task<BoxelSearchOutcome> ApplySessionUpdateAsync(BoxelSearchUpdate update)
    {
        return session.ApplyAsync(update, CancellationToken.None);
    }

    private Task<BoxelSearchOutcome> ExecuteSessionActionAsync(IBoxelSearchAction action)
    {
        return session.ExecuteAsync(action, CancellationToken.None);
    }

    private Task<BoxelSearchLibrarySnapshot> GetSessionLibraryAsync()
    {
        return session.GetLibraryAsync(CancellationToken.None);
    }

    private async Task RunSessionActionAsync(
        Task precedingUpdate,
        IBoxelSearchAction action)
    {
        try
        {
            await precedingUpdate;
            ApplyOutcome(await ExecuteSessionActionAsync(action));
        }
        catch (ObjectDisposedException)
        {
            // The app-scoped session owns shutdown and drains accepted work.
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The boxel search option could not be saved: "
                + exception.Message;
        }
    }

    private static SavedBoxelSearchDocument RequireSavedSearch(
        BoxelSearchOutcome outcome)
    {
        ThrowForRejectedLibraryOutcome(outcome);
        return outcome.SavedSearch
            ?? throw new InvalidOperationException(
                "The library action did not return a saved search.");
    }

    private static void ThrowForRejectedLibraryOutcome(BoxelSearchOutcome outcome)
    {
        if (outcome.Kind == BoxelSearchOutcomeKind.Rejected)
        {
            throw new InvalidOperationException(
                "The saved boxel search action could not be completed.");
        }
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

        if (parsedLastSystemAvailable < searchState.CurrentMaximumSystemNumber)
        {
            StatusMessage = $"Last system available cannot be below recorded suffix "
                + $"{searchState.CurrentMaximumSystemNumber:N0}.";
            return;
        }

        SetLastSystemAvailableEditState(false);
        showNextSystemPageOnUpdate = true;
        ApplyOutcome(await ExecuteSessionActionAsync(
            new SetExpectedSystemCount(parsedLastSystemAvailable + 1)));
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
        showNextSystemPageOnUpdate = true;
        var outcome = await ExecuteSessionActionAsync(new MarkNextBoxelSystemEmpty());
        ApplyOutcome(outcome);
        if (outcome.Kind != BoxelSearchOutcomeKind.Rejected
            && searchState.AutoCopy
            && !string.IsNullOrWhiteSpace(searchState.NextSystem))
        {
            ApplyOutcome(await ExecuteSessionActionAsync(new CopyNextBoxelSystem()));
        }
    }

    public async Task CopyNextSystemAsync()
    {
        ApplyOutcome(await ExecuteSessionActionAsync(new CopyNextBoxelSystem()));
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
        showNextSystemPageOnUpdate = true;
        ApplyOutcome(await ExecuteSessionActionAsync(new NavigateToBoxel(boxel)));
    }

    private async Task CompleteSystemAsync(string systemName)
    {
        await ExecuteSystemActionAsync(new CompleteBoxelSystem(systemName));
    }

    private async Task ReopenSystemAsync(string systemName)
    {
        await ExecuteSystemActionAsync(new ReopenBoxelSystem(systemName));
    }

    private async Task DeferSystemAsync(string systemName)
    {
        await ExecuteSystemActionAsync(new DeferBoxelSystem(systemName));
    }

    private async Task StartAtSystemAsync(string systemName)
    {
        await ExecuteSystemActionAsync(new StartBoxelSurveyAt(systemName));
    }

    private async Task ExecuteSystemActionAsync(IBoxelSearchAction action)
    {
        showNextSystemPageOnUpdate = true;
        var outcome = await ExecuteSessionActionAsync(action);
        ApplyOutcome(outcome);
        if (outcome.Kind != BoxelSearchOutcomeKind.Rejected
            && searchState.AutoCopy
            && !string.IsNullOrWhiteSpace(searchState.NextSystem))
        {
            ApplyOutcome(await ExecuteSessionActionAsync(new CopyNextBoxelSystem()));
        }
    }

    private BoxelAddress? GetParent()
    {
        if (searchState.TopBoxel is null
            || searchState.CurrentBoxel is null
            || string.Equals(
                searchState.TopBoxel.Prefix,
                searchState.CurrentBoxel.Prefix,
                StringComparison.Ordinal))
        {
            return null;
        }

        var parent = searchState.CurrentBoxel.Parent;
        return searchState.TopBoxel.Contains(parent) ? parent : null;
    }

    private BoxelAddress? GetSibling(int offset)
    {
        if (searchState.TopBoxel is null
            || searchState.CurrentBoxel is null
            || string.Equals(
                searchState.TopBoxel.Prefix,
                searchState.CurrentBoxel.Prefix,
                StringComparison.Ordinal))
        {
            return null;
        }

        var siblings = searchState.CurrentBoxel.Parent.Children;
        var index = siblings.ToList().FindIndex(sibling => string.Equals(
            sibling.Prefix,
            searchState.CurrentBoxel.Prefix,
            StringComparison.Ordinal));
        var targetIndex = index + offset;
        return targetIndex >= 0 && targetIndex < siblings.Count
            ? siblings[targetIndex]
            : null;
    }

    private bool IsCurrentSystemInsideSearch()
    {
        return searchState.TopBoxel is not null
            && TryParseBoxelInput(CurrentSystemName, out var currentSystem)
            && searchState.TopBoxel.Contains(currentSystem);
    }

    private bool CanActivate()
    {
        return !IsBusy
            && session.Current.Context.Profile is not null
            && !searchState.IsActive
            && !string.IsNullOrWhiteSpace(TopBoxelText);
    }

    private bool CanDisable()
    {
        return !IsBusy
            && session.Current.Context.Profile is not null
            && searchState.IsActive;
    }

    private bool CanUseActiveSearch()
    {
        return !IsBusy
            && session.Current.Context.Profile is not null
            && searchState.IsActive;
    }

    private bool CanApplyLastSystemAvailable()
    {
        return CanUseActiveSearch()
            && TryParseLastSystemAvailable(
                LastSystemAvailable,
                out var parsedLastSystemAvailable)
            && parsedLastSystemAvailable >= searchState.CurrentMaximumSystemNumber
            && parsedLastSystemAvailable != Math.Max(0, searchState.CurrentCount - 1);
    }

    private bool CanCopyNext()
    {
        return !IsBusy
            && searchState.IsActive
            && searchState.NextSystem is not null;
    }

    private bool CanAuditAll()
    {
        return !IsBusy
            && session.Current.Context.Profile is not null
            && searchState.IsActive
            && searchState.TopBoxel is not null
            && (!ShowLargeAuditConfirmation || ConfirmLargeAudit);
    }

    private void UpdateDisplay()
    {
        searchPrefixes = searchState.Boxels.Select(boxel => boxel.Prefix).ToArray();
        OnPropertyChanged(nameof(SearchPrefixes));
        OnPropertyChanged(nameof(CanSaveProgress));
        OnPropertyChanged(nameof(IsSavedToLibrary));
        OnPropertyChanged(nameof(LibrarySaveButtonText));
        OnPropertyChanged(nameof(SuggestedSaveName));
        CurrentBoxelName = searchState.CurrentBoxel?.Prefix ?? Unavailable;
        CurrentBoxelDescription = searchState.CurrentBoxel is null
            ? string.Empty
            : $"System-name range: {searchState.CurrentBoxel.WithSystemNumber(0).Name} through "
                + searchState.CurrentBoxel.WithSystemNumber(
                    Math.Max(0, searchState.CurrentCount - 1)).Name;
        NextSystem = GetPresentedNextSystem() ?? Unavailable;
        if (!hasUnappliedLastSystemAvailableEdit && !isActivating)
        {
            SetField(
                ref lastSystemAvailable,
                FormatLastSystemAvailable(),
                nameof(LastSystemAvailable));
        }
        SetLastSystemAvailableEditState(hasUnappliedLastSystemAvailableEdit);
        SystemProgress = $"{searchState.CompletedSystemCount:N0} of "
            + $"{Math.Max(searchState.CurrentCount, searchState.Systems.Count):N0} systems complete";
        BoxelProgress = $"{searchState.CompletedBoxelCount:N0} of "
            + $"{searchState.TotalBoxelCount:N0} boxels complete";
        AuditTotal = Math.Max(1, searchState.TotalBoxelCount);
        AuditDescription = searchState.IsActive
            ? $"Checks saved system history, the current NavRoute, empty-boxel records, "
                + $"and Spansh across all {searchState.TotalBoxelCount:N0} boxels. Network requests "
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

        if (searchState.TopBoxel is null || !searchState.TopBoxel.Contains(destinationBoxel))
        {
            DestinationStatus = $"{destinationBoxel.Prefix} · outside search boxel";
            IsDestinationValid = false;
            return;
        }

        if (destinationBoxel.MassCode < searchState.LowMassCode)
        {
            DestinationStatus = $"{destinationBoxel.Prefix} · mass code too low";
            IsDestinationValid = false;
            return;
        }

        if (searchState.Systems.Any(system =>
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
        OnPropertyChanged(nameof(RequiresManualCopy));
    }

    private void UpdateSystemRows()
    {
        if (!searchState.IsActive || searchState.CurrentBoxel is null || searchState.CurrentIsEmpty)
        {
            systemPageIndex = 0;
            systemPagePrefix = searchState.IsActive ? searchState.CurrentBoxel?.Prefix : null;
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

        var systemsByNumber = searchState.Systems.ToDictionary(
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
        var presentedNextSystem = GetPresentedNextSystem();
        var nextSystemPageIndex = GetNextSystemPageIndex();
        var nextSystemLocationChanged = !string.Equals(
                systemPageTarget,
                presentedNextSystem,
                StringComparison.Ordinal)
            || systemPageTargetIndex != nextSystemPageIndex;
        if (!ShowOnlyDeferred
            && (!string.Equals(
                systemPagePrefix,
                searchState.CurrentBoxel.Prefix,
                StringComparison.Ordinal)
                || showNextSystemPageOnUpdate
                || nextSystemLocationChanged))
        {
            systemPageIndex = nextSystemPageIndex;
        }

        systemPagePrefix = searchState.CurrentBoxel.Prefix;
        systemPageTarget = presentedNextSystem;
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
        var nextSystemName = presentedNextSystem;
        Systems = rowNumbers
            .Select(number =>
            {
                systemsByNumber.TryGetValue(number, out var system);
                var boxel = system?.Boxel ?? searchState.CurrentBoxel.WithSystemNumber(number);
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
                        IsEmpty = searchState.EmptySystems.Contains(boxel.GeneratedName),
                        IsDeferred = searchState.IsSystemDeferred(boxel.Prefix, boxel.N2),
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
        return searchState.CurrentBoxel is null || searchState.CurrentIsEmpty
            ? 0
            : Math.Max(searchState.CurrentCount, searchState.CurrentMaximumSystemNumber + 1);
    }

    private string? GetPresentedNextSystem()
    {
        return SortDescending
            ? searchState.NextSystemDescending
            : searchState.NextSystemAscending;
    }

    private (int[] Numbers, int DeferredCount) GetOrderedSystemNumbers(
        int rowCount)
    {
        var numbers = SortDescending
            ? Enumerable.Range(0, rowCount).Reverse()
            : Enumerable.Range(0, rowCount);
        var ordered = numbers.ToArray();
        if (searchState.CurrentBoxel is null)
        {
            return (ordered, 0);
        }

        var deferredNumbers = ordered
            .Where(number => searchState.IsSystemDeferred(searchState.CurrentBoxel.Prefix, number))
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
        var nextSystemName = GetPresentedNextSystem();
        if (searchState.CurrentBoxel is null || string.IsNullOrWhiteSpace(nextSystemName))
        {
            return 0;
        }

        var known = searchState.Systems.FirstOrDefault(system =>
            string.Equals(system.Boxel.Name, nextSystemName, StringComparison.Ordinal)
            || string.Equals(
                system.Boxel.GeneratedName,
                nextSystemName,
                StringComparison.Ordinal));
        if (known is not null)
        {
            return GetSystemPageIndex(known.Boxel.N2);
        }

        return BoxelAddress.TryParse(nextSystemName, out var next)
            && next is not null
            && string.Equals(next.Prefix, searchState.CurrentBoxel.Prefix, StringComparison.Ordinal)
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
        return Math.Max(0, searchState.CurrentCount - 1)
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

        return parsed < searchState.CurrentMaximumSystemNumber
            ? $"Enter {searchState.CurrentMaximumSystemNumber:N0} or higher; that suffix is already recorded."
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
            && !string.IsNullOrWhiteSpace(searchState.NextSystem)
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
        if (searchState.TopBoxel is null || searchState.CurrentBoxel is null)
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

        var path = GetBreadcrumbPath(searchState.TopBoxel, searchState.CurrentBoxel);
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
            siblings = searchState.CurrentBoxel.Parent.Children.ToArray();
            siblingIndex = Array.FindIndex(siblings, sibling => string.Equals(
                sibling.Prefix,
                searchState.CurrentBoxel.Prefix,
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

        ChildBoxels = searchState.CurrentBoxel.MassCode > searchState.LowMassCode
            ? searchState.CurrentBoxel.Children
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
            searchState.GetProgress(boxel),
            string.Equals(
                searchState.CurrentBoxel?.Prefix,
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
