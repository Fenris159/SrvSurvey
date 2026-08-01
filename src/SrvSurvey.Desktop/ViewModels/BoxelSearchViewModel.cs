using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BoxelSearchViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";
    private const int MaximumVisibleSystemRows = 500;
    private const int LargeAuditConfirmationThreshold = 1_000;

    private readonly CommanderProfileStore profileStore;
    private readonly LegacySystemDataReader localSystemReader;
    private readonly EmptyBoxelStore emptyBoxelStore;
    private readonly IBoxelSystemResolver systemResolver;
    private readonly KnownSystemAddressCatalog knownSystems;
    private readonly BoxelCompletionAuditor completionAuditor;
    private readonly BoxelSearchState state = new();
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly AsyncCommand activateCommand;
    private readonly AsyncCommand disableCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand copyNextCommand;
    private readonly AsyncCommand toggleEmptyCommand;
    private readonly AsyncCommand applyExpectedCountCommand;
    private readonly AsyncCommand navigateParentCommand;
    private readonly AsyncCommand navigatePreviousCommand;
    private readonly AsyncCommand navigateNextCommand;
    private readonly AsyncCommand auditAllCommand;
    private readonly AsyncCommand cancelAuditCommand;
    private string topBoxelText = string.Empty;
    private string lowMassCode = "c";
    private DateTimeOffset startedOn = new(DateTime.Today);
    private bool skipAlreadyVisited;
    private bool skipKnownToSpansh;
    private bool completeOnFssAllBodies;
    private bool autoCopy = true;
    private bool suppressOptionPersistence;
    private bool isBusy;
    private bool isAuditing;
    private bool confirmLargeAudit;
    private string statusMessage = "Waiting for a commander profile.";
    private string currentBoxelName = Unavailable;
    private string nextSystem = Unavailable;
    private string expectedSystemCount = "1";
    private string systemProgress = "0 of 0 complete";
    private string boxelProgress = "0 of 0 boxels complete";
    private string searchSize = "Enter a generated system name.";
    private string currentSystemName = Unavailable;
    private string systemListNote = string.Empty;
    private string auditDescription = "Activate a boxel search to audit its full area.";
    private string auditProgress = "No full-area audit has run in this session.";
    private int auditProcessed;
    private int auditTotal = 1;
    private GalacticCoordinate? currentPosition;
    private IReadOnlyList<BoxelSystemRowViewModel> systems = [];
    private IReadOnlyList<BoxelNavigationOptionViewModel> childBoxels = [];
    private string? frontierId;
    private string? commanderName;
    private bool isOdyssey = true;
    private NavRouteSnapshot? latestRoute;
    private GuiFocus lastGuiFocus;
    private StatusDestination? lastDestination;
    private string destinationStatus = "No Galaxy Map destination selected";
    private bool isDestinationValid;
    private string? lastCopiedSystemName;
    private Func<string, Task>? clipboardWriter;
    private CancellationTokenSource? auditCancellation;

    public BoxelSearchViewModel(
        CommanderProfileStore profileStore,
        LegacySystemDataReader localSystemReader,
        EmptyBoxelStore emptyBoxelStore,
        IBoxelSystemResolver systemResolver,
        Func<string, Task>? clipboardWriter = null,
        KnownSystemAddressCatalog? knownSystems = null)
    {
        this.profileStore = profileStore
            ?? throw new ArgumentNullException(nameof(profileStore));
        this.localSystemReader = localSystemReader
            ?? throw new ArgumentNullException(nameof(localSystemReader));
        this.emptyBoxelStore = emptyBoxelStore
            ?? throw new ArgumentNullException(nameof(emptyBoxelStore));
        this.systemResolver = systemResolver
            ?? throw new ArgumentNullException(nameof(systemResolver));
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
        toggleEmptyCommand = new AsyncCommand(
            ToggleCurrentEmptyAsync,
            CanUseActiveSearch);
        ToggleEmptyCommand = toggleEmptyCommand;
        applyExpectedCountCommand = new AsyncCommand(
            ApplyExpectedSystemCountAsync,
            CanUseActiveSearch);
        ApplyExpectedCountCommand = applyExpectedCountCommand;
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

    public IReadOnlyList<string> MassCodes { get; } =
        ["a", "b", "c", "d", "e", "f", "g"];

    public string TopBoxelText
    {
        get => topBoxelText;
        set
        {
            if (SetField(ref topBoxelText, value))
            {
                UpdateSearchSize();
                activateCommand.RaiseCanExecuteChanged();
            }
        }
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
            _ = SaveAsync();
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

    public bool ShouldShowGalaxyMapOverlay =>
        lastGuiFocus == GuiFocus.GalaxyMap && state.IsActive;

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
            : state.AutoCopy
                ? "AUTO-COPY READY"
                : "MANUAL COPY";

    public bool IsCurrentEmpty => state.CurrentIsEmpty;

    public string StatusLabel => state.IsActive ? "ACTIVE" : "INACTIVE";

    public string RefreshButtonText => IsBusy && !IsAuditing
        ? "Refreshing\u2026"
        : "Refresh boxel";

    public string AuditButtonText => IsAuditing ? "Auditing\u2026" : "Audit all boxels";

    public bool ShowLargeAuditConfirmation => state.TotalBoxelCount
        > LargeAuditConfirmationThreshold;

    public string EmptyButtonText => state.CurrentIsEmpty
        ? "Mark as not empty"
        : "Mark current empty";

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

    public string NextSystem
    {
        get => nextSystem;
        private set => SetField(ref nextSystem, value);
    }

    public string ExpectedSystemCount
    {
        get => expectedSystemCount;
        set => SetField(ref expectedSystemCount, value);
    }

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
            if (SetField(ref childBoxels, value))
            {
                OnPropertyChanged(nameof(HasChildBoxels));
            }
        }
    }

    public bool HasChildBoxels => ChildBoxels.Count > 0;

    public ICommand ActivateCommand { get; }

    public ICommand DisableCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand CopyNextCommand { get; }

    public ICommand ToggleEmptyCommand { get; }

    public ICommand ApplyExpectedCountCommand { get; }

    public ICommand NavigateParentCommand { get; }

    public ICommand NavigatePreviousCommand { get; }

    public ICommand NavigateNextCommand { get; }

    public ICommand AuditAllCommand { get; }

    public ICommand CancelAuditCommand { get; }

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
        copyNextCommand.RaiseCanExecuteChanged();
    }

    public async Task LoadProfileAsync(
        string profileFrontierId,
        string? profileCommanderName,
        bool profileIsOdyssey,
        BoxelSearchSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFrontierId);
        ArgumentNullException.ThrowIfNull(snapshot);
        auditCancellation?.Cancel();
        frontierId = profileFrontierId;
        commanderName = profileCommanderName;
        isOdyssey = profileIsOdyssey;
        state.Reset(snapshot);
        ConfirmLargeAudit = false;
        AuditProcessed = 0;
        AuditProgress = "No full-area audit has run in this session.";
        suppressOptionPersistence = true;
        try
        {
            TopBoxelText = snapshot.TopBoxel?.Name ?? string.Empty;
            LowMassCode = snapshot.LowMassCode.ToString();
            StartedOn = snapshot.StartedOn == DateTimeOffset.MinValue
                ? new DateTimeOffset(DateTime.Today)
                : snapshot.StartedOn;
            SkipAlreadyVisited = snapshot.SkipAlreadyVisited;
            SkipKnownToSpansh = snapshot.SkipKnownToSpansh;
            CompleteOnFssAllBodies =
                snapshot.CompletionMode == BoxelCompletionMode.FssAllBodies;
            AutoCopy = snapshot.TopBoxel is null || snapshot.AutoCopy;
            state.SetAutoCopy(AutoCopy);
        }
        finally
        {
            suppressOptionPersistence = false;
        }

        if (state.TopBoxel is not null)
        {
            try
            {
                state.ApplyEmptyBoxels(
                    await emptyBoxelStore.LoadGroupAsync(state.TopBoxel));
            }
            catch (InvalidDataException exception)
            {
                StatusMessage = exception.Message;
            }
        }

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
        state.Reset();
        StatusMessage = message;
        UpdateDisplay();
    }

    public void UpdateCurrentSystem(
        string? systemName,
        GalacticCoordinate? position)
    {
        var nextSystemName = string.IsNullOrWhiteSpace(systemName)
            ? Unavailable
            : systemName;
        if (string.Equals(
                currentSystemName,
                nextSystemName,
                StringComparison.OrdinalIgnoreCase)
            && currentPosition == position)
        {
            return;
        }

        CurrentSystemName = nextSystemName;
        currentPosition = position;
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

        await operationLock.WaitAsync();
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

        await operationLock.WaitAsync();
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
        EliteStatus status,
        bool allowAutoCopy = true)
    {
        ArgumentNullException.ThrowIfNull(status);
        var enteredGalaxyMap = lastGuiFocus != GuiFocus.GalaxyMap
            && status.GuiFocus == GuiFocus.GalaxyMap;
        lastGuiFocus = status.GuiFocus;
        lastDestination = status.Destination;
        if (lastGuiFocus != GuiFocus.GalaxyMap)
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
                topBoxel,
                selectedMassCode,
                StartedOn,
                SkipAlreadyVisited,
                SkipKnownToSpansh,
                CompleteOnFssAllBodies
                    ? BoxelCompletionMode.FssAllBodies
                    : BoxelCompletionMode.EnterSystem,
                AutoCopy,
                out var error))
        {
            StatusMessage = error ?? "The boxel search configuration is invalid.";
            return;
        }

        try
        {
            state.ApplyEmptyBoxels(
                await emptyBoxelStore.LoadGroupAsync(topBoxel!));
            UpdateDisplay();
            await SaveAsync();
            await RefreshCurrentAsync();
        }
        catch (InvalidDataException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public async Task DisableAsync()
    {
        state.Disable();
        UpdateDisplay();
        await SaveAsync("Boxel search disabled; its progress was retained.");
    }

    public async Task RefreshCurrentAsync()
    {
        if (!state.IsActive || state.Current is null)
        {
            StatusMessage = "Activate a boxel search before refreshing systems.";
            return;
        }

        await operationLock.WaitAsync();
        try
        {
            IsBusy = true;
            StatusMessage = $"Refreshing {state.Current.Prefix}\u2026";
            var warnings = new List<string>();
            try
            {
                state.ApplyEmptyBoxels(
                    await emptyBoxelStore.LoadGroupAsync(state.Current));
            }
            catch (InvalidDataException exception)
            {
                warnings.Add(exception.Message);
            }

            if (!state.CurrentIsEmpty)
            {
                var local = await localSystemReader.ReadAsync(
                    frontierId!,
                    state.Current);
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
                        await systemResolver.SearchAsync(state.Current));
                }
                catch (Exception exception) when (
                    exception is HttpRequestException
                        or TaskCanceledException
                        or System.Text.Json.JsonException)
                {
                    warnings.Add("Spansh refresh failed: " + exception.Message);
                }
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
            StatusMessage = ShowLargeAuditConfirmation && !ConfirmLargeAudit
                ? "Confirm the large network audit before starting it."
                : "Activate a boxel search before auditing its full area.";
            return;
        }

        IsBusy = true;
        IsAuditing = true;
        AuditProcessed = 0;
        AuditTotal = Math.Max(1, state.TotalBoxelCount);
        AuditProgress = $"Preparing to audit {state.TotalBoxelCount:N0} boxels\u2026";
        StatusMessage = "The full-area audit is running in the background.";
        auditCancellation = new CancellationTokenSource();
        var cancellation = auditCancellation;
        var auditFrontierId = frontierId;
        var auditTopPrefix = state.TopBoxel.Prefix;
        var snapshot = state.CreateSnapshot();
        var routeSystems = latestRoute?.Route
            .Select(entry => entry.ToBoxelObservation())
            .OfType<BoxelSystemObservation>()
            .ToArray() ?? [];
        var request = new BoxelCompletionAuditRequest(
            auditFrontierId,
            state.Boxels,
            state.EmptyBoxelPrefixes,
            state.Current?.Prefix,
            snapshot.StartedOn,
            snapshot.SkipAlreadyVisited,
            snapshot.SkipKnownToSpansh,
            snapshot.CompletionMode,
            routeSystems);
        var progress = new Progress<BoxelCompletionAuditProgress>(update =>
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

        try
        {
            var result = await completionAuditor.AuditAsync(
                request,
                progress,
                cancellation.Token);
            AuditProcessed = result.Processed;
            AuditTotal = Math.Max(1, result.Total);
            await operationLock.WaitAsync();
            try
            {
                if (!string.Equals(frontierId, auditFrontierId, StringComparison.Ordinal)
                    || !string.Equals(
                        state.TopBoxel?.Prefix,
                        auditTopPrefix,
                        StringComparison.Ordinal))
                {
                    StatusMessage = "The audit finished for a profile that is no longer active; its results were not applied.";
                    return;
                }

                state.ApplyCompletionAudit(result.Entries);
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
            cancellation.Dispose();
            if (ReferenceEquals(auditCancellation, cancellation))
            {
                auditCancellation = null;
            }

            IsAuditing = false;
            IsBusy = false;
        }
    }

    public Task CancelAuditAsync()
    {
        auditCancellation?.Cancel();
        StatusMessage = "Cancelling the full-area audit after the current request\u2026";
        return Task.CompletedTask;
    }

    public async Task ApplyExpectedSystemCountAsync()
    {
        if (!int.TryParse(
                ExpectedSystemCount,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var count)
            || count < 1
            || count > 100_000)
        {
            StatusMessage = "Expected systems must be a whole number from 1 to 100,000.";
            return;
        }

        state.SetExpectedSystemCount(count);
        UpdateDisplay();
        await SaveAsync($"Expected system count updated to {state.CurrentCount:N0}.");
    }

    public async Task ToggleCurrentEmptyAsync()
    {
        if (state.Current is null)
        {
            return;
        }

        await operationLock.WaitAsync();
        try
        {
            IsBusy = true;
            var original = state.Current;
            var markEmpty = !state.CurrentIsEmpty;
            await emptyBoxelStore.SetEmptyAsync(original, markEmpty);
            state.SetCurrentEmpty(markEmpty);
            var moved = false;
            if (markEmpty
                && TryParseBoxelInput(state.NextSystem, out var nextBoxel)
                && nextBoxel is not null
                && !string.Equals(
                    nextBoxel.Prefix,
                    original.Prefix,
                    StringComparison.Ordinal))
            {
                moved = state.TrySetCurrent(nextBoxel, out _);
            }

            UpdateDisplay();
            await SaveAsync();
            StatusMessage = markEmpty
                ? moved
                    ? $"Marked {original.Prefix} empty and advanced to "
                        + state.Current?.Prefix
                        + "."
                    : $"Marked {original.Prefix} empty."
                : $"Removed the empty marker from {original.Prefix}.";
            if (moved)
            {
                await RefreshCurrentWithoutLockAsync();
                if (state.AutoCopy)
                {
                    await CopyNextSystemAsync();
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The empty-boxel marker was not changed: "
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

        UpdateDisplay();
        await SaveAsync();
        await RefreshCurrentAsync();
    }

    private async Task ToggleSystemAsync(string systemName)
    {
        var current = state.Systems.FirstOrDefault(system =>
            string.Equals(system.Boxel.Name, systemName, StringComparison.Ordinal));
        if (!state.TrySetSystemComplete(
                systemName,
                current?.IsComplete != true,
                out var error))
        {
            StatusMessage = error ?? "The system completion state was not changed.";
            return;
        }

        UpdateDisplay();
        await SaveAsync("System completion updated.");
    }

    private async Task RefreshCurrentWithoutLockAsync()
    {
        if (state.Current is null || state.CurrentIsEmpty)
        {
            return;
        }

        var local = await localSystemReader.ReadAsync(frontierId!, state.Current);
        state.MergeLocalSystems(local.Systems);
        if (latestRoute is not null)
        {
            state.MergeRoute(latestRoute.Route
                .Select(entry => entry.ToBoxelObservation())
                .OfType<BoxelSystemObservation>());
        }

        try
        {
            state.MergeSpanshSystems(await systemResolver.SearchAsync(state.Current));
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or System.Text.Json.JsonException)
        {
            StatusMessage = "Advanced to the next boxel, but its Spansh refresh failed: "
                + exception.Message;
        }

        UpdateDisplay();
        await SaveAsync();
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
                + (result.Errors.Count == 1 ? string.Empty : "s")
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
            await profileStore.SaveBoxelSearchAsync(
                frontierId,
                commanderName,
                isOdyssey,
                state.CreateSnapshot());
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
        CurrentBoxelName = state.Current?.Prefix ?? Unavailable;
        NextSystem = state.NextSystem ?? Unavailable;
        ExpectedSystemCount = Math.Max(1, state.CurrentCount)
            .ToString(CultureInfo.CurrentCulture);
        SystemProgress = $"{state.CompletedSystemCount:N0} of "
            + $"{Math.Max(state.CurrentCount, state.Systems.Count):N0} complete";
        BoxelProgress = $"{state.CompletedBoxelCount:N0} of "
            + $"{state.TotalBoxelCount:N0} boxels complete";
        AuditTotal = Math.Max(1, state.TotalBoxelCount);
        AuditDescription = state.IsActive
            ? $"Checks local history and Spansh for all {state.TotalBoxelCount:N0} "
                + "boxels, one request at a time. You can cancel safely and keep partial progress."
            : "Activate a boxel search to audit its full area.";
        OnPropertyChanged(nameof(IsActive));
        RaiseOverlayProperties();
        OnPropertyChanged(nameof(IsCurrentEmpty));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(EmptyButtonText));
        OnPropertyChanged(nameof(ShowLargeAuditConfirmation));
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
        if (state.Current is null || state.CurrentIsEmpty)
        {
            Systems = [];
            return;
        }

        var knownSystems = state.Systems.ToDictionary(
            system => system.Boxel.N2);
        var rowCount = Math.Max(
            state.CurrentCount,
            state.CurrentMaximumSystemNumber + 1);
        var rowNumbers = Enumerable.Range(
                0,
                Math.Min(Math.Max(1, rowCount), MaximumVisibleSystemRows))
            .Concat(knownSystems.Keys)
            .Distinct()
            .Order()
            .ToArray();
        SystemListNote = rowCount > rowNumbers.Length
            ? $"Showing the first {MaximumVisibleSystemRows:N0} rows plus all known systems "
                + $"from {rowCount:N0} expected systems."
            : string.Empty;
        Systems = rowNumbers
            .Select(number =>
            {
                knownSystems.TryGetValue(number, out var system);
                var boxel = system?.Boxel ?? state.Current.WithSystemNumber(number);
                var distance = system?.Position is { } position
                    && currentPosition is { } from
                        ? $"{from.DistanceTo(position):N2} ly"
                        : Unavailable;
                var isCurrent = TryParseBoxelInput(
                        CurrentSystemName,
                        out var currentSystem)
                    && string.Equals(
                        currentSystem?.Name,
                        boxel.Name,
                        StringComparison.Ordinal);
                return new BoxelSystemRowViewModel(
                    boxel.Name,
                    system?.IsComplete == true,
                    system is not null,
                    isCurrent,
                    distance,
                    FormatDate(system?.VisitedAt),
                    FormatDate(system?.SpanshUpdatedAt),
                    () => ToggleSystemAsync(boxel.Name));
            })
            .ToArray();
    }

    private void UpdateNavigation()
    {
        ChildBoxels = state.Current is not null
            && state.Current.MassCode > state.LowMassCode
                ? state.Current.Children
                    .Select(child => new BoxelNavigationOptionViewModel(
                        child.Prefix,
                        () => NavigateAsync(child)))
                    .ToArray()
                : [];
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

    private void RaiseCommandStates()
    {
        activateCommand.RaiseCanExecuteChanged();
        disableCommand.RaiseCanExecuteChanged();
        refreshCommand.RaiseCanExecuteChanged();
        copyNextCommand.RaiseCanExecuteChanged();
        toggleEmptyCommand.RaiseCanExecuteChanged();
        applyExpectedCountCommand.RaiseCanExecuteChanged();
        navigateParentCommand.RaiseCanExecuteChanged();
        navigatePreviousCommand.RaiseCanExecuteChanged();
        navigateNextCommand.RaiseCanExecuteChanged();
        auditAllCommand.RaiseCanExecuteChanged();
        cancelAuditCommand.RaiseCanExecuteChanged();
    }

    private bool TryParseBoxelInput(
        string? value,
        out BoxelAddress? boxel)
    {
        var systemName = value?.Trim();
        var normalized = systemName;
        if (normalized?.EndsWith("-", StringComparison.Ordinal) == true)
        {
            normalized += "0";
        }

        if (BoxelAddress.TryParse(normalized, out boxel))
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
    public BoxelSystemRowViewModel(
        string name,
        bool isComplete,
        bool isKnown,
        bool isCurrent,
        string distance,
        string visitedAt,
        string spanshUpdatedAt,
        Func<Task> toggle)
    {
        Name = name;
        IsComplete = isComplete;
        IsKnown = isKnown;
        IsCurrent = isCurrent;
        Distance = distance;
        VisitedAt = visitedAt;
        SpanshUpdatedAt = spanshUpdatedAt;
        Status = isComplete ? "COMPLETE" : isKnown ? "KNOWN" : "UNKNOWN";
        ToggleCommand = new RowCommand(toggle, () => isKnown);
    }

    public string Name { get; }

    public bool IsComplete { get; }

    public bool IsKnown { get; }

    public bool IsCurrent { get; }

    public string Distance { get; }

    public string VisitedAt { get; }

    public string SpanshUpdatedAt { get; }

    public string Status { get; }

    public string ToggleButtonText => IsComplete ? "Reopen" : "Complete";

    public ICommand ToggleCommand { get; }

    private sealed class RowCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
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

public sealed class BoxelNavigationOptionViewModel
{
    public BoxelNavigationOptionViewModel(
        string label,
        Func<Task> navigate)
    {
        Label = label;
        NavigateCommand = new NavigationCommand(navigate);
    }

    public string Label { get; }

    public ICommand NavigateCommand { get; }

    private sealed class NavigationCommand(Func<Task> navigate) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
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
