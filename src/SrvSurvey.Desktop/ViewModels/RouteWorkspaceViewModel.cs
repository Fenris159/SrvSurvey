using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.ViewModels;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The view model is window-scoped and its gate may have in-flight waiters.")]
public sealed class RouteWorkspaceViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";

    private readonly FollowRouteService routeService;
    private readonly RouteNameImporter nameImporter;
    private readonly ISpanshRouteClient spanshClient;
    private readonly FollowRouteKind routeKind;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly FleetCarrierJumpCountdownTracker carrierJumpCountdown = new();
    private readonly SemaphoreSlim bioProgressLock = new(1, 1);
    private readonly AsyncCommand openWindowCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand saveCommand;
    private readonly AsyncCommand discardCommand;
    private readonly AsyncCommand copyNextHopCommand;
    private readonly AsyncCommand loadSelectedRouteCommand;
    private readonly AsyncCommand newCommand;
    private readonly AsyncCommand confirmNewCommand;
    private readonly AsyncCommand resetCommand;
    private readonly AsyncCommand saveAsCommand;
    private readonly AsyncCommand confirmSaveAsCommand;
    private readonly AsyncCommand notesCommand;
    private readonly AsyncCommand saveNotesCommand;
    private readonly AsyncCommand deleteCommand;
    private readonly AsyncCommand confirmDeleteCommand;
    private readonly AsyncCommand cancelDialogCommand;
    private string? frontierId;
    private string? initializedFrontierId;
    private string? currentSystemName;
    private long? currentSystemAddress;
    private GalacticCoordinate? currentPosition;
    private FollowRouteDocument? loadedRoute;
    private bool hasSavedRoute;
    private string? draftNotes;
    private IReadOnlyList<SavedRouteItemViewModel> savedRoutes = [];
    private SavedRouteItemViewModel? selectedSavedRoute;
    private string saveAsName = string.Empty;
    private string saveAsError = string.Empty;
    private string notesDraft = string.Empty;
    private bool isNewConfirmationVisible;
    private bool isDeleteConfirmationVisible;
    private bool isSaveAsVisible;
    private bool isNotesVisible;
    private FollowRouteHop[] draftHops = [];
    private IReadOnlyList<RouteHopItemViewModel> hops = [];
    private int lastReachedIndex = -1;
    private bool isActive;
    private bool autoCopy = true;
    private bool isBusy;
    private EliteStatus? status;
    private string? musicTrack;
    private bool destinationMatchesNextHop;
    private string? lastCopiedHopName;
    private StatusDestination? lastDestination;
    private string statusMessage = "Waiting for a commander profile.";
    private Func<Task<bool>>? windowOpener;
    private Func<string, Task>? clipboardWriter;
    private FleetCarrierJumpCountdownState carrierJumpCountdownState =
        FleetCarrierJumpCountdownState.Inactive;

    public RouteWorkspaceViewModel(
        FollowRouteService routeService,
        RouteNameImporter nameImporter,
        ISpanshRouteClient spanshClient,
        FollowRouteKind routeKind = FollowRouteKind.Standard,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.routeService = routeService
            ?? throw new ArgumentNullException(nameof(routeService));
        this.nameImporter = nameImporter
            ?? throw new ArgumentNullException(nameof(nameImporter));
        this.spanshClient = spanshClient
            ?? throw new ArgumentNullException(nameof(spanshClient));
        this.routeKind = routeKind;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        openWindowCommand = new AsyncCommand(OpenWindowAsync, CanOpenWindow);
        refreshCommand = new AsyncCommand(RefreshAsync, HasProfileAndNotBusy);
        saveCommand = new AsyncCommand(SaveAsync, () => CanSaveChanges);
        discardCommand = new AsyncCommand(
            DiscardAsync,
            () => IsDirty && !IsBusy && !IsDialogVisible);
        copyNextHopCommand = new AsyncCommand(
            CopyNextHopAsync,
            () => NextHop is not null && clipboardWriter is not null && !IsBusy);
        loadSelectedRouteCommand = new AsyncCommand(
            LoadSelectedRouteAsync,
            () => SelectedSavedRoute is not null
                && !IsDirty
                && !IsBusy
                && !IsDialogVisible);
        newCommand = new AsyncCommand(
            () => ShowDialogAsync(RouteDialog.New),
            HasProfileAndIdleWorkspace);
        confirmNewCommand = new AsyncCommand(ConfirmNewAsync, HasProfileAndNotBusy);
        resetCommand = new AsyncCommand(
            ResetAsync,
            () => HasRoute && !IsBusy && !IsDialogVisible);
        saveAsCommand = new AsyncCommand(
            () => ShowDialogAsync(RouteDialog.SaveAs),
            () => HasRoute && !IsBusy && !IsDialogVisible);
        confirmSaveAsCommand = new AsyncCommand(
            ConfirmSaveAsAsync,
            () => HasRoute && !IsBusy && IsSaveAsVisible);
        notesCommand = new AsyncCommand(
            () => ShowDialogAsync(RouteDialog.Notes),
            HasProfileAndIdleWorkspace);
        saveNotesCommand = new AsyncCommand(
            SaveNotesAsync,
            () => !IsBusy && IsNotesVisible);
        deleteCommand = new AsyncCommand(
            () => ShowDialogAsync(RouteDialog.Delete),
            () => HasSavedRoute && !IsBusy && !IsDialogVisible);
        confirmDeleteCommand = new AsyncCommand(
            ConfirmDeleteAsync,
            () => HasSavedRoute && !IsBusy && IsDeleteConfirmationVisible);
        cancelDialogCommand = new AsyncCommand(
            CancelDialogAsync,
            () => IsDialogVisible);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CatalogChanged;

    public FollowRouteKind RouteKind => routeKind;

    public bool IsFleetCarrierWorkspace => routeKind == FollowRouteKind.FleetCarrier;

    public string WindowTitle => IsFleetCarrierWorkspace
        ? "FC Route Workspace"
        : "Route Workspace";

    public string WorkspaceEyebrow => IsFleetCarrierWorkspace
        ? "FLEET CARRIER ROUTE"
        : "FOLLOWED ROUTE";

    public string WorkspaceHeading => IsFleetCarrierWorkspace
        ? "Fleet carrier itinerary"
        : "Navigation itinerary";

    public string WorkspaceDescription => IsFleetCarrierWorkspace
        ? "Import a fleet-carrier route, track each carrier jump, and keep the next destination ready."
        : "Import a route, track each arrival, and keep the next system ready for the Galaxy Map.";

    public bool HasProfile => !string.IsNullOrWhiteSpace(frontierId);

    public bool HasRoute => draftHops.Length > 0;

    public bool HasSavedRoute
    {
        get => hasSavedRoute;
        private set
        {
            if (SetField(ref hasSavedRoute, value))
            {
                OnPropertyChanged(nameof(RouteFileName));
                OnPropertyChanged(nameof(ShouldShowRouteBioOverlay));
                OnPropertyChanged(nameof(ShouldShowFleetCarrierRouteOverlay));
                RaiseCommands();
            }
        }
    }

    public IReadOnlyList<SavedRouteItemViewModel> SavedRoutes
    {
        get => savedRoutes;
        private set => SetField(ref savedRoutes, value);
    }

    public SavedRouteItemViewModel? SelectedSavedRoute
    {
        get => selectedSavedRoute;
        set
        {
            if (SetField(ref selectedSavedRoute, value))
            {
                loadSelectedRouteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SaveAsName
    {
        get => saveAsName;
        set
        {
            if (SetField(ref saveAsName, value))
            {
                SaveAsError = string.Empty;
            }
        }
    }

    public string SaveAsError
    {
        get => saveAsError;
        private set => SetField(ref saveAsError, value);
    }

    public string NotesDraft
    {
        get => notesDraft;
        set => SetField(ref notesDraft, value);
    }

    public bool IsNewConfirmationVisible
    {
        get => isNewConfirmationVisible;
        private set => SetDialogField(
            ref isNewConfirmationVisible,
            value,
            nameof(IsNewConfirmationVisible));
    }

    public bool IsDeleteConfirmationVisible
    {
        get => isDeleteConfirmationVisible;
        private set => SetDialogField(
            ref isDeleteConfirmationVisible,
            value,
            nameof(IsDeleteConfirmationVisible));
    }

    public bool IsSaveAsVisible
    {
        get => isSaveAsVisible;
        private set => SetDialogField(
            ref isSaveAsVisible,
            value,
            nameof(IsSaveAsVisible));
    }

    public bool IsNotesVisible
    {
        get => isNotesVisible;
        private set => SetDialogField(
            ref isNotesVisible,
            value,
            nameof(IsNotesVisible));
    }

    public bool IsDialogVisible => IsNewConfirmationVisible
        || IsDeleteConfirmationVisible
        || IsSaveAsVisible
        || IsNotesVisible;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseCommands();
                OnPropertyChanged(nameof(ImportButtonText));
            }
        }
    }

    public string ImportButtonText => IsBusy ? "Importing\u2026" : "Import";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public IReadOnlyList<RouteHopItemViewModel> Hops
    {
        get => hops;
        private set => SetField(ref hops, value);
    }

    public bool IsActive
    {
        get => isActive;
        set
        {
            var normalized = value && CanActivate;
            if (SetField(ref isActive, normalized))
            {
                RefreshPresentation();
            }
        }
    }

    public bool AutoCopy
    {
        get => autoCopy;
        set
        {
            if (SetField(ref autoCopy, value))
            {
                RefreshPresentation();
            }
        }
    }

    public bool CanActivate => HasRoute
        && lastReachedIndex < draftHops.Length - 1;

    public bool IsComplete => HasRoute
        && lastReachedIndex >= draftHops.Length - 1;

    public bool HasDefinitionChanges => loadedRoute is not null
        && !loadedRoute.Hops.SequenceEqual(draftHops);

    public bool HasProgressChanges => loadedRoute is not null
        && ((loadedRoute.IsActive
                && loadedRoute.Hops.Count > 0
                && !loadedRoute.IsComplete) != IsActive
            || loadedRoute.AutoCopy != AutoCopy
            || loadedRoute.LastReachedIndex != lastReachedIndex);

    public bool HasNotesChanges => loadedRoute is not null
        && !string.Equals(
            loadedRoute.Notes?.Trim(),
            draftNotes?.Trim(),
            StringComparison.Ordinal);

    public bool IsDirty => HasDefinitionChanges
        || HasProgressChanges
        || HasNotesChanges;

    public bool CanSaveChanges => HasSavedRoute
        && HasProgressChanges
        && !HasDefinitionChanges
        && !IsBusy
        && !IsDialogVisible;

    public int RouteCount => draftHops.Length;

    public int ReachedCount => Math.Clamp(
        lastReachedIndex + 1,
        0,
        draftHops.Length);

    public FollowRouteHop? NextHop
    {
        get
        {
            var nextIndex = lastReachedIndex + 1;
            return IsActive
                && nextIndex >= 0
                && nextIndex < draftHops.Length
                    ? draftHops[nextIndex]
                    : null;
        }
    }

    public FollowRouteDocument? CreateSnapshot()
    {
        return loadedRoute is null
            ? null
            : loadedRoute with
            {
                Hops = draftHops.ToArray(),
                LastReachedIndex = lastReachedIndex,
                IsActive = IsActive,
                AutoCopy = AutoCopy,
                Notes = draftNotes,
            };
    }

    public string NextHopName => NextHop?.Name
        ?? (IsComplete
            ? "Route complete"
            : (HasRoute) switch
            {
                true => "Route paused",
                false => "No route loaded"
            });

    public string ProgressSummary => !HasRoute
        ? "Import a route to begin."
        : (IsComplete) switch
        {
            true => $"All {RouteCount:N0} systems reached.",
            false => (lastReachedIndex < 0) switch
            {
                true => $"Not started \u2022 {RouteCount:N0} systems",
                false => $"Reached {ReachedCount:N0} of {RouteCount:N0} systems"
            }
        };

    public string AutoCopySummary => AutoCopy
        ? "Next-hop clipboard guidance is enabled."
        : "Next-hop clipboard guidance is disabled.";

    public bool ShouldAutoCopyNextHop => IsActive
        && AutoCopy
        && NextHop is not null;

    public bool ShouldShowGalaxyMapOverlay => IsGalaxyMapOpen
        && IsActive
        && NextHop is not null;

    private bool IsGalaxyMapOpen => OverlayGameModeResolver.Resolve(
        status,
        musicTrack: musicTrack) == OverlayGameMode.GalaxyMap;

    public string NextHopDistance
    {
        get
        {
            var distance = currentPosition is { } start
                && NextHop?.Position is { } end
                    ? start.DistanceTo(end)
                    : (double?)null;
            return distance is null
                ? "Distance unavailable"
                : $"{distance:N2} ly from {CurrentSystem}";
        }
    }

    public string NextHopGuidance => NextHop is { } hop
        ? CreateNotes(hop)
        : Unavailable;

    public bool HasNextHopGuidance => NextHop is { } hop
        && (hop.Refuel || hop.Neutron || !string.IsNullOrWhiteSpace(hop.Notes));

    public string NextHopDestinationStatus => destinationMatchesNextHop
        ? "SELECTED IN GALAXY MAP"
        : "ROUTE TARGET";

    public string NextHopClipboardStatus => string.Equals(
        lastCopiedHopName,
        NextHop?.Name,
        StringComparison.Ordinal)
            ? "NEXT SYSTEM COPIED"
            : (AutoCopy) switch
            {
                true => "AUTO-COPY READY",
                false => "MANUAL COPY"
            };

    public string CurrentSystem => string.IsNullOrWhiteSpace(currentSystemName)
        ? Unavailable
        : currentSystemName;

    public string RouteFileName => loadedRoute is null
        ? Unavailable
        : (HasSavedRoute) switch
        {
            true => Path.GetFileName(loadedRoute.FilePath),
            false => "Not saved"
        };

    public string RouteName => loadedRoute?.Name
        ?? (HasSavedRoute
            ? Path.GetFileNameWithoutExtension(RouteFileName)
            : (HasRoute) switch
            {
                true => "New route",
                false => "No active route"
            });

    public string RouteNotesPreview => string.IsNullOrWhiteSpace(draftNotes)
        ? "No route notes."
        : draftNotes;

    public bool HasRouteNotes => !string.IsNullOrWhiteSpace(draftNotes);

    public string? LoadedSavedRoutePath => HasSavedRoute
        ? loadedRoute?.FilePath
        : null;

    public ICommand OpenWindowCommand => openWindowCommand;

    public ICommand RefreshCommand => refreshCommand;

    public ICommand SaveCommand => saveCommand;

    public ICommand DiscardCommand => discardCommand;

    public ICommand CopyNextHopCommand => copyNextHopCommand;

    public ICommand LoadSelectedRouteCommand => loadSelectedRouteCommand;

    public ICommand NewCommand => newCommand;

    public ICommand ConfirmNewCommand => confirmNewCommand;

    public ICommand ResetCommand => resetCommand;

    public ICommand SaveAsCommand => saveAsCommand;

    public ICommand ConfirmSaveAsCommand => confirmSaveAsCommand;

    public ICommand NotesCommand => notesCommand;

    public ICommand SaveNotesCommand => saveNotesCommand;

    public ICommand DeleteCommand => deleteCommand;

    public ICommand ConfirmDeleteCommand => confirmDeleteCommand;

    public ICommand CancelDialogCommand => cancelDialogCommand;

    public void DismissDialogs()
    {
        CloseDialogs();
    }

    public RouteHopItemViewModel? CurrentBioHop => Hops.FirstOrDefault(hop =>
        hop.IsCurrent && hop.HasBioTargets);

    public IReadOnlyList<RouteBioTargetItemViewModel> CurrentBioTargets =>
        CurrentBioHop?.BioTargets
        ?? Array.Empty<RouteBioTargetItemViewModel>();

    public bool HasCurrentBioTargets => CurrentBioTargets.Count > 0;

    public bool ShouldShowRouteBioOverlay => HasSavedRoute
        && HasCurrentBioTargets
        && (IsActive || IsComplete);

    public string CurrentBioSystemName => CurrentBioHop?.Name ?? CurrentSystem;

    public bool ShouldShowFleetCarrierRouteOverlay =>
        IsFleetCarrierWorkspace
        && HasSavedRoute
        && IsActive
        && NextHop is not null;

    public bool HasCarrierJumpCountdown => carrierJumpCountdownState.IsActive;

    public string CarrierJumpCountdownTitle => carrierJumpCountdownState.Title;

    public string CarrierJumpCountdownValue => carrierJumpCountdownState.Countdown;

    public string CarrierJumpPhaseLabel => carrierJumpCountdownState.PhaseLabel;

    public string CarrierJumpPhaseCountdown =>
        carrierJumpCountdownState.PhaseCountdown;

    public bool HasCarrierJumpPhaseCountdown =>
        carrierJumpCountdownState.HasPhaseCountdown;

    public string CarrierJumpDestination =>
        carrierJumpCountdownState.Destination;

    public void SetWindowOpener(Func<Task<bool>>? opener)
    {
        windowOpener = opener;
        openWindowCommand.RaiseCanExecuteChanged();
    }

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
        copyNextHopCommand.RaiseCanExecuteChanged();
    }

    public async Task<bool> OpenWorkspaceAsync()
    {
        if (windowOpener is null || !await windowOpener())
        {
            StatusMessage = "The route workspace requires a commander profile.";
            return false;
        }

        return true;
    }

    public bool IsLoadedSavedRoute(string path)
    {
        if (!HasSavedRoute || loadedRoute is null)
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(loadedRoute.FilePath),
            Path.GetFullPath(path),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    public void ApplyExternalNotes(string path, string? notes)
    {
        if (!IsLoadedSavedRoute(path) || loadedRoute is null)
        {
            return;
        }

        var normalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        loadedRoute = loadedRoute with { Notes = normalized };
        draftNotes = normalized;
        RaiseRouteMetadataProperties();
    }

    public void ApplyExternalFavorite(string path, bool isFavorite)
    {
        if (!IsLoadedSavedRoute(path) || loadedRoute is null)
        {
            return;
        }

        loadedRoute = loadedRoute with { IsFavorite = isFavorite };
    }

    public async Task HandleRouteRenamedAsync(FollowRouteRenameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (IsLoadedSavedRoute(result.PreviousPath))
        {
            loadedRoute = result.Route;
            draftNotes = result.Route.Notes;
            RaiseRouteMetadataProperties();
        }

        await RefreshCatalogAsync();
        if (IsLoadedSavedRoute(result.Route.FilePath))
        {
            SelectCatalogPath(result.Route.FilePath);
        }
    }

    public async Task HandleLoadedRouteDeletedAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        HasSavedRoute = false;
        ApplyDocument(await routeService.CreateNewAsync(frontierId));
        await RefreshCatalogAsync();
        StatusMessage =
            "The loaded route was removed. A new route workspace is ready.";
    }

    public async Task<bool> UpdateContextAsync(
        string? nextFrontierId,
        string? nextSystemName,
        long? nextSystemAddress,
        GalacticCoordinate? nextPosition)
    {
        var normalizedFrontierId = string.IsNullOrWhiteSpace(nextFrontierId)
            ? null
            : nextFrontierId;
        var contextChanged = !string.Equals(
                currentSystemName,
                nextSystemName,
                StringComparison.Ordinal)
            || currentSystemAddress != nextSystemAddress
            || currentPosition != nextPosition;
        frontierId = normalizedFrontierId;
        currentSystemName = nextSystemName;
        currentSystemAddress = nextSystemAddress;
        currentPosition = nextPosition;
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(CurrentSystem));

        if (string.Equals(
            initializedFrontierId,
            frontierId,
            StringComparison.OrdinalIgnoreCase))
        {
            if (contextChanged)
            {
                RefreshPresentation();
            }

            return false;
        }

        ResetCarrierJumpCountdown();
        initializedFrontierId = frontierId;
        if (frontierId is null)
        {
            loadedRoute = null;
            HasSavedRoute = false;
            SavedRoutes = [];
            SelectedSavedRoute = null;
            draftNotes = null;
            ApplyDraft([], -1, false, true);
            StatusMessage = "Waiting for a commander profile.";
            return true;
        }

        await LoadAsync();
        return true;
    }

    public async Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        ApplyFleetCarrierJumpEvents(journalEvents);
        if (loadedRoute is null || !HasSavedRoute || journalEvents.Count == 0)
        {
            return;
        }

        try
        {
            var hadUnsavedChanges = IsDirty;
            var changed = false;
            int? reachedIndex = null;
            var arrivalEvent = IsFleetCarrierWorkspace
                ? "CarrierJump"
                : "FSDJump";
            foreach (var journalEvent in journalEvents)
            {
                if (!string.Equals(
                    journalEvent.EventName,
                    arrivalEvent,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                var name = GetString(journalEvent.Payload, "StarSystem");
                var address = GetInt64(journalEvent.Payload, "SystemAddress");
                if (string.IsNullOrWhiteSpace(name) && address is null)
                {
                    continue;
                }

                var result = await routeService.ApplyArrivalAsync(
                    loadedRoute,
                    name ?? string.Empty,
                    address);
                if (!result.Changed)
                {
                    continue;
                }

                loadedRoute = result.Route;
                changed = true;
                reachedIndex = result.ReachedIndex;
            }

            if (changed)
            {
                if (!hadUnsavedChanges)
                {
                    ApplyDocument(loadedRoute);
                }
                else
                {
                    RefreshPresentation();
                }

                var reachedName = reachedIndex is { } index
                    && index >= 0
                    && index < loadedRoute.Hops.Count
                        ? loadedRoute.Hops[index].Name
                        : currentSystemName ?? "the route";
                StatusMessage = loadedRoute.IsComplete
                    ? $"Route complete after arriving at {reachedName}."
                    : $"Arrived at hop #{reachedIndex + 1:N0}: {reachedName}."
                        + ((hadUnsavedChanges) switch
                        {
                            true => " Unsaved route edits were kept.",
                            false => string.Empty
                        });
            }

            await ApplyBioArrivalEventsAsync(journalEvents);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Route progress could not be saved: "
                + exception.Message;
        }
    }

    private async Task ApplyBioArrivalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        if (!HasCurrentBioTargets)
        {
            return;
        }

        foreach (var journalEvent in journalEvents)
        {
            if (!IsBioArrivalEvent(journalEvent.EventName))
            {
                continue;
            }

            var bodyName = GetString(journalEvent.Payload, "Body")
                ?? GetString(journalEvent.Payload, "BodyName");
            var bodyId = GetInt64(journalEvent.Payload, "BodyID");
            if (string.IsNullOrWhiteSpace(bodyName) && bodyId is null)
            {
                continue;
            }

            var eventSystemAddress = GetInt64(
                journalEvent.Payload,
                "SystemAddress");
            var currentHop = CurrentBioHop;
            if (currentHop is null
                || (eventSystemAddress is { } address
                    && currentHop.Hop.SystemAddress is { } routeAddress
                    && address != routeAddress))
            {
                continue;
            }

            var target = CurrentBioTargets.FirstOrDefault(candidate =>
                !candidate.IsCompleted
                && candidate.MatchesBody(bodyId, bodyName, currentHop.Name));
            if (target is not null)
            {
                await SetBioTargetCompletedAsync(target, isCompleted: true);
            }
        }
    }

    private static bool IsBioArrivalEvent(string eventName)
    {
        return eventName is "ApproachBody"
            or "SupercruiseExit"
            or "Touchdown"
            or "Disembark";
    }

    public void ApplyFleetCarrierJumpEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!IsFleetCarrierWorkspace || journalEvents.Count == 0)
        {
            return;
        }

        var now = utcNow();
        var changed = false;
        foreach (var journalEvent in journalEvents)
        {
            changed |= carrierJumpCountdown.Apply(journalEvent, now);
        }

        if (changed)
        {
            ApplyCarrierJumpCountdownState();
        }
    }

    public void RefreshCarrierJumpCountdown()
    {
        if (IsFleetCarrierWorkspace
            && carrierJumpCountdown.Refresh(utcNow()))
        {
            ApplyCarrierJumpCountdownState();
        }
    }

    public async Task UpdateStatusAsync(
        EliteStatus nextStatus,
        string? nextMusicTrack = null)
    {
        ArgumentNullException.ThrowIfNull(nextStatus);
        var wasGalaxyMapOpen = IsGalaxyMapOpen;
        status = nextStatus;
        musicTrack = nextMusicTrack;
        var enteredGalaxyMap = !wasGalaxyMapOpen && IsGalaxyMapOpen;
        lastDestination = nextStatus.Destination;
        destinationMatchesNextHop = IsGalaxyMapOpen
            && IsNextHop(lastDestination);
        if (!IsGalaxyMapOpen)
        {
            lastCopiedHopName = null;
        }

        RaiseOverlayProperties();
        if (enteredGalaxyMap && ShouldAutoCopyNextHop)
        {
            await CopyNextHopAsync();
        }
    }

    public Task RefreshAsync()
    {
        return LoadAsync();
    }

    public async Task ImportNamesAsync(IEnumerable<string> names)
    {
        if (!HasProfile || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Resolving route systems with Spansh\u2026";
            var result = await nameImporter.ImportAsync(names);
            ApplyImportedHops(result.Hops);
            StatusMessage = result.Hops.Count == 0
                ? "No system names were found to import."
                : $"Imported {result.Hops.Count:N0} systems; "
                    + $"{result.ResolvedCount:N0} resolved and "
                    + $"{result.UnresolvedCount:N0} kept by name.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "System names could not be imported: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ImportNamesTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "No system names were available to import.";
            return Task.CompletedTask;
        }

        return ImportNamesAsync(RouteNameImporter.ParseNames(text));
    }

    public void ReportImportError(string message)
    {
        StatusMessage = message;
    }

    public async Task ImportSpanshUrlAsync(string? text)
    {
        if (!HasProfile || IsBusy)
        {
            return;
        }

        if (!SpanshRouteUrlParser.TryParse(text, out var reference)
            || reference is null)
        {
            StatusMessage = "The clipboard does not contain a valid Spansh route URL or job ID.";
            return;
        }

        if (IsFleetCarrierWorkspace
            && reference.Kind != SpanshRouteKind.FleetCarrier)
        {
            StatusMessage = "FC Routes accepts Spansh Fleet Carrier Router result URLs only.";
            return;
        }

        if (!IsFleetCarrierWorkspace
            && reference.Kind == SpanshRouteKind.FleetCarrier)
        {
            StatusMessage = "Import Fleet Carrier Router results from the FC Routes tab.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Importing Spansh {GetRouteKindLabel(reference.Kind)} route\u2026";
            var importedHops = await spanshClient.GetRouteAsync(reference);
            ApplyImportedHops(importedHops, reference.Kind);
            StatusMessage = importedHops.Count == 0
                ? "Spansh returned a route with no systems."
                : $"Imported {importedHops.Count:N0} systems from Spansh. Review it, then use Save As...";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The Spansh route could not be imported: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetProgressThrough(int index, bool reached)
    {
        if (index < 0 || index >= draftHops.Length)
        {
            return;
        }

        lastReachedIndex = reached ? index : index - 1;
        if (lastReachedIndex >= draftHops.Length - 1)
        {
            isActive = false;
        }

        RefreshPresentation();
    }

    public async Task SetBioTargetCompletedAsync(
        RouteBioTargetItemViewModel target,
        bool isCompleted)
    {
        ArgumentNullException.ThrowIfNull(target);
        await bioProgressLock.WaitAsync();
        try
        {
            if (target.HopIndex < 0
                || target.HopIndex >= draftHops.Length
                || target.TargetIndex < 0
                || target.TargetIndex
                    >= draftHops[target.HopIndex].BioTargets.Count)
            {
                return;
            }

            var current = draftHops[target.HopIndex]
                .BioTargets[target.TargetIndex];
            if (!target.Matches(current) || current.IsCompleted == isCompleted)
            {
                return;
            }

            var original = current;
            ReplaceDraftBioTarget(
                target.HopIndex,
                target.TargetIndex,
                current with { IsCompleted = isCompleted });
            RefreshPresentation();
            if (loadedRoute is null || !HasSavedRoute)
            {
                StatusMessage = isCompleted
                    ? $"Marked {target.BodyName} complete in the route draft."
                    : $"Reopened {target.BodyName} in the route draft.";
                return;
            }

            try
            {
                loadedRoute = await routeService.SetBioTargetCompletedAsync(
                    loadedRoute,
                    target.HopIndex,
                    target.TargetIndex,
                    isCompleted);
                ReplaceDraftBioTarget(
                    target.HopIndex,
                    target.TargetIndex,
                    loadedRoute.Hops[target.HopIndex]
                        .BioTargets[target.TargetIndex]);
                RefreshPresentation();
                StatusMessage = isCompleted
                    ? $"Marked {target.BodyName} complete."
                    : $"Reopened {target.BodyName}.";
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                ReplaceDraftBioTarget(
                    target.HopIndex,
                    target.TargetIndex,
                    original);
                RefreshPresentation();
                StatusMessage = "Body destination progress could not be saved: "
                    + exception.Message;
            }
        }
        finally
        {
            bioProgressLock.Release();
        }
    }

    public async Task SaveAsync()
    {
        if (loadedRoute is null || !CanSaveChanges)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await routeService.SaveProgressAsync(
                loadedRoute with
                {
                    LastReachedIndex = lastReachedIndex,
                    IsActive = IsActive,
                    AutoCopy = AutoCopy,
                });
            ApplyDocument(saved);
            StatusMessage = saved.IsComplete
                ? "Route progress saved as complete."
                : (saved.IsActive) switch
                {
                    true => $"Changes saved. Next system: {saved.NextHop?.Name ?? Unavailable}.",
                    false => "Route progress saved in a paused state."
                };
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route could not be saved: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DiscardAsync()
    {
        if (loadedRoute is null)
        {
            return;
        }

        if (!HasSavedRoute)
        {
            ApplyDocument(loadedRoute);
            StatusMessage = "The new route draft was restored to its initial state.";
            return;
        }

        try
        {
            IsBusy = true;
            var result = await routeService.ReloadAsync(loadedRoute);
            if (result.Route is null)
            {
                StatusMessage = result.Error ?? "The saved route could not be restored.";
                return;
            }

            ApplyDocument(result.Route);
            StatusMessage = "Changes were undone to the route's last saved state.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route could not be restored: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ResetAsync()
    {
        if (!HasRoute)
        {
            return Task.CompletedTask;
        }

        lastReachedIndex = -1;
        isActive = draftHops.Length > 0;
        RefreshPresentation();
        StatusMessage = "Route progress reset in the draft. Save Changes to keep it.";
        return Task.CompletedTask;
    }

    private Task ShowDialogAsync(RouteDialog dialog)
    {
        CloseDialogs();
        switch (dialog)
        {
            case RouteDialog.New:
                IsNewConfirmationVisible = true;
                break;
            case RouteDialog.SaveAs:
                SaveAsName = loadedRoute?.Name ?? string.Empty;
                SaveAsError = string.Empty;
                IsSaveAsVisible = true;
                break;
            case RouteDialog.Notes:
                NotesDraft = draftNotes ?? string.Empty;
                IsNotesVisible = true;
                break;
            case RouteDialog.Delete:
                IsDeleteConfirmationVisible = true;
                break;
        }

        return Task.CompletedTask;
    }

    private Task CancelDialogAsync()
    {
        CloseDialogs();
        return Task.CompletedTask;
    }

    public async Task ConfirmNewAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        CloseDialogs();
        try
        {
            IsBusy = true;
            var route = await routeService.CreateNewAsync(frontierId);
            HasSavedRoute = false;
            ApplyDocument(route);
            await RefreshCatalogAsync();
            StatusMessage = "New route workspace ready. Import a route, then use Save As...";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "A new route could not be started: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ConfirmSaveAsAsync()
    {
        var snapshot = CreateSnapshot();
        if (snapshot is null || !HasRoute)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await routeService.SaveAsAsync(snapshot, SaveAsName);
            HasSavedRoute = true;
            ApplyDocument(saved);
            CloseDialogs();
            await RefreshCatalogAsync();
            SelectCatalogPath(saved.FilePath);
            StatusMessage = $"Saved route as {saved.Name}.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            SaveAsError = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveNotesAsync()
    {
        var normalized = string.IsNullOrWhiteSpace(NotesDraft)
            ? null
            : NotesDraft.Trim();
        try
        {
            IsBusy = true;
            if (HasSavedRoute && loadedRoute is not null)
            {
                loadedRoute = await routeService.SaveNotesAsync(
                    loadedRoute,
                    normalized);
            }

            draftNotes = normalized;
            RaiseRouteMetadataProperties();
            CloseDialogs();
            StatusMessage = HasSavedRoute
                ? "Route notes saved."
                : "Notes added to the draft. Use Save As... to keep them.";
            if (HasSavedRoute)
            {
                CatalogChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route notes could not be saved: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ConfirmDeleteAsync()
    {
        if (!HasSavedRoute || loadedRoute is null || frontierId is null)
        {
            return;
        }

        CloseDialogs();
        try
        {
            IsBusy = true;
            await routeService.DeleteAsync(loadedRoute);
            HasSavedRoute = false;
            var route = await routeService.CreateNewAsync(frontierId);
            ApplyDocument(route);
            await RefreshCatalogAsync();
            StatusMessage = "The saved route was removed and moved to route recovery storage.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route could not be deleted: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadSelectedRouteAsync()
    {
        if (frontierId is null || SelectedSavedRoute is not { } selected)
        {
            return;
        }

        await LoadSavedRouteAsync(selected.FileName, selected.IsLegacy);
    }

    public async Task LoadSavedRouteAsync(string fileName, bool isLegacy)
    {
        if (frontierId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await routeService.LoadNamedAsync(
                frontierId,
                fileName,
                isLegacy);
            if (result.Route is null)
            {
                StatusMessage = result.Error ?? "The saved route could not be loaded.";
                return;
            }

            HasSavedRoute = true;
            ApplyDocument(result.Route);
            SelectCatalogPath(result.Path);
            StatusMessage =
                $"Loaded {result.Route.Hops.Count:N0} systems from "
                + $"{result.Route.Name ?? Path.GetFileNameWithoutExtension(result.Path)}.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The saved route could not be loaded: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ActivateSavedRouteAsync(
        string fileName,
        bool isLegacy,
        string filePath)
    {
        if (frontierId is null || IsBusy)
        {
            return false;
        }

        if (IsDirty)
        {
            StatusMessage =
                "Save or undo the current Route Workspace changes before activating another route.";
            return false;
        }

        try
        {
            IsBusy = true;
            if (loadedRoute is not null
                && HasSavedRoute
                && loadedRoute.IsActive
                && !IsLoadedSavedRoute(filePath))
            {
                var paused = await routeService.SetActiveAsync(
                    loadedRoute,
                    isActive: false,
                    currentSystemAddress: currentSystemAddress);
                ApplyDocument(paused);
            }

            var result = await routeService.LoadNamedAsync(
                frontierId,
                fileName,
                isLegacy);
            if (result.Route is null)
            {
                StatusMessage = result.Error
                    ?? "The saved route could not be activated.";
                return false;
            }

            var activated = await routeService.SetActiveAsync(
                result.Route,
                isActive: true,
                currentSystemAddress: currentSystemAddress);
            HasSavedRoute = true;
            ApplyDocument(activated);
            SelectCatalogPath(result.Path);
            if (activated.IsActive)
            {
                StatusMessage =
                    $"Activated {activated.Name ?? Path.GetFileNameWithoutExtension(result.Path)}."
                    + $" Next system: {activated.NextHop?.Name ?? Unavailable}.";
                return true;
            }

            StatusMessage = activated.Hops.Count == 0
                ? "The selected route has no systems and cannot be activated."
                : "The selected route is complete. Reset its progress before activating it.";
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The saved route could not be activated: "
                + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DeactivateCurrentRouteAsync()
    {
        if (frontierId is null
            || loadedRoute is null
            || !HasSavedRoute
            || IsBusy)
        {
            return false;
        }

        if (HasDefinitionChanges || HasNotesChanges)
        {
            StatusMessage =
                "Save or undo the current Route Workspace definition and note changes before deactivating it.";
            return false;
        }

        try
        {
            IsBusy = true;
            await routeService.SaveProgressAsync(
                loadedRoute with
                {
                    LastReachedIndex = lastReachedIndex,
                    IsActive = false,
                    AutoCopy = AutoCopy,
                });
            var blankRoute = await routeService.CreateNewAsync(frontierId);
            HasSavedRoute = false;
            ApplyDocument(blankRoute);
            await RefreshCatalogAsync();
            StatusMessage =
                "Route tracking deactivated. The saved route and its progress were preserved.";
            return true;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The current route could not be deactivated: "
                + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SetAutoCopyAsync(bool enabled)
    {
        if (frontierId is null
            || loadedRoute is null
            || !HasSavedRoute
            || IsBusy)
        {
            StatusMessage =
                "Activate a saved route before changing next-hop auto-copy.";
            return false;
        }

        var loadedIsActive = loadedRoute.IsActive
            && loadedRoute.Hops.Count > 0
            && !loadedRoute.IsComplete;
        var hasTrackingChanges = loadedIsActive != IsActive
            || loadedRoute.LastReachedIndex != lastReachedIndex;
        if (HasDefinitionChanges || HasNotesChanges || hasTrackingChanges)
        {
            StatusMessage =
                "Save or undo the current Route Workspace changes before changing next-hop auto-copy here.";
            return false;
        }

        if (loadedRoute.AutoCopy == enabled && AutoCopy == enabled)
        {
            StatusMessage = enabled
                ? "Next-hop auto-copy is already enabled."
                : "Next-hop auto-copy is already disabled.";
            return true;
        }

        try
        {
            IsBusy = true;
            var saved = await routeService.SaveProgressAsync(
                loadedRoute with
                {
                    LastReachedIndex = lastReachedIndex,
                    IsActive = IsActive,
                    AutoCopy = enabled,
                });
            ApplyDocument(saved);
            StatusMessage = enabled
                ? "Next-hop auto-copy enabled for this route."
                : "Next-hop auto-copy disabled for this route.";
            return true;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Next-hop auto-copy could not be changed: "
                + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisableAutoCopyForCompetingRouteAsync()
    {
        if (!AutoCopy)
        {
            return;
        }

        if (frontierId is null
            || loadedRoute is null
            || !HasSavedRoute
            || IsBusy
            || HasDefinitionChanges
            || HasNotesChanges)
        {
            AutoCopy = false;
            StatusMessage =
                "Next-hop auto-copy moved to the other active route workspace.";
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await routeService.SaveProgressAsync(
                loadedRoute with
                {
                    LastReachedIndex = lastReachedIndex,
                    IsActive = IsActive,
                    AutoCopy = false,
                });
            ApplyDocument(saved);
            StatusMessage =
                "Next-hop auto-copy moved to the other active route workspace.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AutoCopy = false;
            StatusMessage = "Next-hop auto-copy was disabled for this session, "
                + "but the route file could not be updated: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CopyNextHopAsync()
    {
        if (NextHop is not { } nextHop || clipboardWriter is null)
        {
            StatusMessage = "There is no active next hop to copy.";
            return;
        }

        try
        {
            await clipboardWriter(nextHop.Name);
            lastCopiedHopName = nextHop.Name;
            StatusMessage = $"Copied {nextHop.Name} to the clipboard.";
            RaiseOverlayProperties();
        }
        catch (Exception exception)
        {
            StatusMessage = "The next hop could not be copied: "
                + exception.Message;
        }
    }

    private async Task LoadAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await routeService.LoadAsync(frontierId);
            if (result.Route is null)
            {
                loadedRoute = null;
                HasSavedRoute = false;
                ApplyDraft([], -1, false, true);
                await RefreshCatalogAsync();
                StatusMessage = result.Error ?? "The route could not be loaded.";
                return;
            }

            HasSavedRoute = result.Exists;
            ApplyDocument(result.Route);
            await RefreshCatalogAsync();
            SelectCatalogPath(result.Path);
            StatusMessage = result.Exists
                ? $"Loaded {result.Route.Hops.Count:N0} route systems from "
                    + Path.GetFileName(result.Path)
                    + "."
                : "No followed route is saved for this commander.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route could not be loaded: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
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
        await OpenWorkspaceAsync();
    }

    private void ApplyImportedHops(
        IReadOnlyList<FollowRouteHop> importedHops,
        SpanshRouteKind? sourceKind = null)
    {
        if (loadedRoute is not null)
        {
            loadedRoute = loadedRoute with { SourceSpanshKind = sourceKind };
        }

        var nextHops = importedHops.ToArray();
        var nextLastIndex = nextHops.Length > 0
            && currentSystemAddress is { } address
            && nextHops[0].SystemAddress == address
                ? 0
                : -1;
        ApplyDraft(
            nextHops,
            nextLastIndex,
            nextHops.Length > 0 && nextLastIndex < nextHops.Length - 1,
            AutoCopy);
    }

    private void ApplyDocument(FollowRouteDocument route)
    {
        loadedRoute = route;
        draftNotes = route.Notes;
        ApplyDraft(
            route.Hops,
            route.LastReachedIndex,
            route.IsActive && !route.IsComplete,
            route.AutoCopy);
        RaiseRouteMetadataProperties();
    }

    private async Task RefreshCatalogAsync()
    {
        if (frontierId is null)
        {
            SavedRoutes = [];
            SelectedSavedRoute = null;
            return;
        }

        var entries = await routeService.ListAsync(frontierId);
        SavedRoutes = entries
            .Select(entry => new SavedRouteItemViewModel(
                entry.Name,
                entry.FileName,
                entry.FilePath,
                entry.IsLegacy,
                entry.LastModified))
            .ToArray();
        if (loadedRoute is not null && HasSavedRoute)
        {
            SelectCatalogPath(loadedRoute.FilePath);
        }
        else
        {
            SelectedSavedRoute = null;
        }

        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectCatalogPath(string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        SelectedSavedRoute = SavedRoutes.FirstOrDefault(route =>
            string.Equals(route.FilePath, path, comparison));
    }

    private void ApplyDraft(
        IReadOnlyList<FollowRouteHop> nextHops,
        int nextLastReachedIndex,
        bool nextIsActive,
        bool nextAutoCopy)
    {
        draftHops = nextHops.ToArray();
        lastReachedIndex = draftHops.Length == 0
            ? -1
            : Math.Clamp(nextLastReachedIndex, -1, draftHops.Length - 1);
        isActive = nextIsActive
            && draftHops.Length > 0
            && lastReachedIndex < draftHops.Length - 1;
        autoCopy = nextAutoCopy;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(AutoCopy));
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        destinationMatchesNextHop = IsGalaxyMapOpen
            && IsNextHop(lastDestination);
        if (!string.Equals(
            lastCopiedHopName,
            NextHop?.Name,
            StringComparison.Ordinal))
        {
            lastCopiedHopName = null;
        }

        var canReuseRows = hops.Count == draftHops.Length;
        if (canReuseRows)
        {
            for (var index = 0; index < draftHops.Length; index++)
            {
                if (hops[index].Index != index
                    || !hops[index].MatchesIdentity(draftHops[index]))
                {
                    canReuseRows = false;
                    break;
                }
            }
        }

        List<RouteHopItemViewModel>? rows = canReuseRows
            ? null
            : new List<RouteHopItemViewModel>(draftHops.Length);
        for (var index = 0; index < draftHops.Length; index++)
        {
            var hop = draftHops[index];
            GalacticCoordinate? from = index == 0
                ? (lastReachedIndex < 0) switch
                {
                    true => currentPosition,
                    false => hop.Position
                }
                : draftHops[index - 1].Position;
            var distance = from is { } start && hop.Position is { } end
                ? start.DistanceTo(end)
                : (double?)null;
            var isCurrent = IsCurrentSystem(hop);
            var isNext = IsActive && index == lastReachedIndex + 1;
            var distanceText = distance is null ? "?" : $"{distance:N2} ly";
            var notes = CreateNotes(hop);
            if (canReuseRows)
            {
                hops[index].UpdatePresentation(
                    hop,
                    distanceText,
                    notes,
                    draftHops.Length - index - 1,
                    index <= lastReachedIndex,
                    isCurrent,
                    isNext);
            }
            else
            {
                rows!.Add(new RouteHopItemViewModel(
                    index,
                    index + 1,
                    hop,
                    distanceText,
                    notes,
                    draftHops.Length - index - 1,
                    IsFleetCarrierWorkspace,
                    index <= lastReachedIndex,
                    isCurrent,
                    isNext));
            }
        }

        if (rows is not null)
        {
            Hops = rows;
        }
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasDefinitionChanges));
        OnPropertyChanged(nameof(HasProgressChanges));
        OnPropertyChanged(nameof(HasNotesChanges));
        OnPropertyChanged(nameof(CanSaveChanges));
        OnPropertyChanged(nameof(RouteCount));
        OnPropertyChanged(nameof(ReachedCount));
        OnPropertyChanged(nameof(NextHop));
        OnPropertyChanged(nameof(NextHopName));
        OnPropertyChanged(nameof(ProgressSummary));
        OnPropertyChanged(nameof(AutoCopySummary));
        OnPropertyChanged(nameof(ShouldAutoCopyNextHop));
        OnPropertyChanged(nameof(CurrentBioHop));
        OnPropertyChanged(nameof(CurrentBioTargets));
        OnPropertyChanged(nameof(HasCurrentBioTargets));
        OnPropertyChanged(nameof(ShouldShowRouteBioOverlay));
        OnPropertyChanged(nameof(CurrentBioSystemName));
        OnPropertyChanged(nameof(ShouldShowFleetCarrierRouteOverlay));
        RaiseOverlayProperties();
        RaiseCommands();
    }

    private void RaiseRouteMetadataProperties()
    {
        OnPropertyChanged(nameof(RouteFileName));
        OnPropertyChanged(nameof(RouteName));
        OnPropertyChanged(nameof(RouteNotesPreview));
        OnPropertyChanged(nameof(HasRouteNotes));
        OnPropertyChanged(nameof(HasNotesChanges));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSaveChanges));
        RaiseCommands();
    }

    private void CloseDialogs()
    {
        IsNewConfirmationVisible = false;
        IsDeleteConfirmationVisible = false;
        IsSaveAsVisible = false;
        IsNotesVisible = false;
        SaveAsError = string.Empty;
    }

    private bool HasProfileAndIdleWorkspace()
    {
        return HasProfile && !IsBusy && !IsDialogVisible;
    }

    private bool IsNextHop(StatusDestination? destination)
    {
        if (destination is null || NextHop is not { } nextHop)
        {
            return false;
        }

        return (destination.System > 0
                && nextHop.SystemAddress == destination.System)
            || (!string.IsNullOrWhiteSpace(destination.Name)
                && string.Equals(
                    nextHop.Name,
                    destination.Name,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void RaiseOverlayProperties()
    {
        OnPropertyChanged(nameof(ShouldShowGalaxyMapOverlay));
        OnPropertyChanged(nameof(NextHopDistance));
        OnPropertyChanged(nameof(NextHopGuidance));
        OnPropertyChanged(nameof(HasNextHopGuidance));
        OnPropertyChanged(nameof(NextHopDestinationStatus));
        OnPropertyChanged(nameof(NextHopClipboardStatus));
    }

    private void ResetCarrierJumpCountdown()
    {
        if (carrierJumpCountdown.Reset())
        {
            ApplyCarrierJumpCountdownState();
        }
    }

    private void ApplyCarrierJumpCountdownState()
    {
        var next = carrierJumpCountdown.Current;
        if (next == carrierJumpCountdownState)
        {
            return;
        }

        carrierJumpCountdownState = next;
        OnPropertyChanged(nameof(HasCarrierJumpCountdown));
        OnPropertyChanged(nameof(CarrierJumpCountdownTitle));
        OnPropertyChanged(nameof(CarrierJumpCountdownValue));
        OnPropertyChanged(nameof(CarrierJumpPhaseLabel));
        OnPropertyChanged(nameof(CarrierJumpPhaseCountdown));
        OnPropertyChanged(nameof(HasCarrierJumpPhaseCountdown));
        OnPropertyChanged(nameof(CarrierJumpDestination));
    }

    private bool IsCurrentSystem(FollowRouteHop hop)
    {
        return (currentSystemAddress is not null
                && hop.SystemAddress == currentSystemAddress)
            || (!string.IsNullOrWhiteSpace(currentSystemName)
                && string.Equals(
                    hop.Name,
                    currentSystemName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateNotes(FollowRouteHop hop)
    {
        var parts = new List<string>();
        if (hop.Refuel)
        {
            parts.Add("\u26fd Refuel");
        }

        if (hop.Neutron)
        {
            parts.Add("\u26a0 Neutron");
        }

        if (!string.IsNullOrWhiteSpace(hop.Notes))
        {
            parts.Add(hop.Notes);
        }

        return parts.Count == 0 ? Unavailable : string.Join(" \u2022 ", parts);
    }

    private void ReplaceDraftBioTarget(
        int hopIndex,
        int targetIndex,
        FollowRouteBioTarget target)
    {
        var bioTargets = draftHops[hopIndex].BioTargets.ToArray();
        bioTargets[targetIndex] = target;
        var updatedHops = draftHops.ToArray();
        updatedHops[hopIndex] = updatedHops[hopIndex] with
        {
            Bio = bioTargets,
        };
        draftHops = updatedHops;
    }

    private static string GetRouteKindLabel(SpanshRouteKind kind)
    {
        return kind switch
        {
            SpanshRouteKind.Generic => "route",
            SpanshRouteKind.Riches => "valuable-world",
            SpanshRouteKind.Exobiology => "exobiology",
            SpanshRouteKind.Tourist => "tourist",
            SpanshRouteKind.Neutron => "neutron",
            SpanshRouteKind.Galaxy => "galaxy",
            SpanshRouteKind.FleetCarrier => "fleet-carrier",
            SpanshRouteKind.Colonisation => "colonisation",
            SpanshRouteKind.Trade => "trade",
            _ => "followed",
        };
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or HttpRequestException
            or TimeoutException;
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

    private void SetDialogField(
        ref bool field,
        bool value,
        string propertyName)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        OnPropertyChanged(nameof(IsDialogVisible));
        RaiseCommands();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaiseCommands()
    {
        openWindowCommand.RaiseCanExecuteChanged();
        refreshCommand.RaiseCanExecuteChanged();
        saveCommand.RaiseCanExecuteChanged();
        discardCommand.RaiseCanExecuteChanged();
        copyNextHopCommand.RaiseCanExecuteChanged();
        loadSelectedRouteCommand.RaiseCanExecuteChanged();
        newCommand.RaiseCanExecuteChanged();
        confirmNewCommand.RaiseCanExecuteChanged();
        resetCommand.RaiseCanExecuteChanged();
        saveAsCommand.RaiseCanExecuteChanged();
        confirmSaveAsCommand.RaiseCanExecuteChanged();
        notesCommand.RaiseCanExecuteChanged();
        saveNotesCommand.RaiseCanExecuteChanged();
        deleteCommand.RaiseCanExecuteChanged();
        confirmDeleteCommand.RaiseCanExecuteChanged();
        cancelDialogCommand.RaiseCanExecuteChanged();
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !isExecuting && canExecute();
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private enum RouteDialog
    {
        New,
        SaveAs,
        Notes,
        Delete,
    }
}

public sealed record SavedRouteItemViewModel(
    string Name,
    string FileName,
    string FilePath,
    bool IsLegacy,
    DateTimeOffset LastModified)
{
    public string DisplayName => Name;
}

public sealed class RouteHopItemViewModel : INotifyPropertyChanged
{
    private FollowRouteHop hop;
    private string distance;
    private string notes;
    private int jumpsRemaining;
    private bool isReached;
    private bool isCurrent;
    private bool isNext;

    public RouteHopItemViewModel(
        int index,
        int number,
        FollowRouteHop hop,
        string distance,
        string notes,
        int jumpsRemaining,
        bool isFleetCarrierHop,
        bool isReached,
        bool isCurrent,
        bool isNext)
    {
        Index = index;
        Number = number;
        this.hop = hop;
        this.distance = distance;
        this.notes = notes;
        this.jumpsRemaining = jumpsRemaining;
        IsFleetCarrierHop = isFleetCarrierHop;
        this.isReached = isReached;
        this.isCurrent = isCurrent;
        this.isNext = isNext;
        BioTargets = CreateBioTargets(hop);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public int Number { get; }

    public FollowRouteHop Hop => hop;

    public string Name => Hop.Name;

    public string Distance => distance;

    public string CarrierDistance
    {
        get
        {
            if (Hop.Carrier?.DistanceLy is { } value)
            {
                return $"{value:N2}";
            }

            return Distance == "?"
                ? "\u2014"
                : Distance.Replace(" ly", string.Empty, StringComparison.Ordinal);
        }
    }

    public string CarrierRemaining => FormatCarrierNumber(Hop.Carrier?.RemainingLy, 2);

    public int JumpsRemaining => jumpsRemaining;

    public bool IsFleetCarrierHop { get; }

    public bool IsStandardHop => !IsFleetCarrierHop;

    public string CarrierFuelRemaining => FormatCarrierNumber(
        Hop.Carrier?.FuelRemainingTonnes,
        0);

    public string CarrierTritiumInMarket => FormatCarrierNumber(
        Hop.Carrier?.TritiumInMarketTonnes,
        0);

    public string CarrierFuelUsed => FormatCarrierNumber(
        Hop.Carrier?.FuelUsedTonnes,
        0);

    public string CarrierIcyRing
    {
        get
        {
            if (Hop.Carrier?.HasIcyRing != true)
            {
                return "\u2014";
            }

            return Hop.Carrier.IsSystemPristine ? "PRISTINE" : "YES";
        }
    }

    public string CarrierRestock => Hop.Carrier?.MustRestock == true
        ? "YES"
        : "—";

    public string CarrierRestockAmount => FormatCarrierNumber(
        Hop.Carrier?.RestockAmountTonnes,
        0);

    public string Notes => notes;

    public string RouteNotes => Hop.Notes ?? string.Empty;

    public bool HasNotes => !string.IsNullOrWhiteSpace(Hop.Notes);

    public bool Refuel => Hop.Refuel;

    public bool Neutron => Hop.Neutron;

    public bool HasGuidance => HasNotes || Refuel || Neutron;

    public IReadOnlyList<RouteBioTargetItemViewModel> BioTargets { get; private set; }

    public bool HasBioTargets => BioTargets.Count > 0;

    public bool IsReached => isReached;

    public bool IsCurrent => isCurrent;

    public bool IsNext => isNext;

    public string State => IsCurrent
        ? "CURRENT"
        : (IsNext) switch
        {
            true => "NEXT",
            false => (IsReached) switch
            {
                true => "VISITED",
                false => string.Empty
            }
        };

    public bool HasState => State.Length > 0;

    public bool MatchesIdentity(FollowRouteHop candidate)
    {
        return Hop.SystemAddress is { } address
            && candidate.SystemAddress is { } candidateAddress
                ? address == candidateAddress
                : string.Equals(
                    Hop.Name,
                    candidate.Name,
                    StringComparison.OrdinalIgnoreCase);
    }

    public void UpdatePresentation(
        FollowRouteHop nextHop,
        string nextDistance,
        string nextNotes,
        int nextJumpsRemaining,
        bool nextIsReached,
        bool nextIsCurrent,
        bool nextIsNext)
    {
        var hopChanged = !Equals(hop, nextHop);
        hop = nextHop;
        if (hopChanged)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Hop)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(HasNotes)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(RouteNotes)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Refuel)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Neutron)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(HasGuidance)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierDistance)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierRemaining)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierFuelRemaining)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierTritiumInMarket)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierFuelUsed)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierIcyRing)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierRestock)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierRestockAmount)));
            RefreshBioTargets(nextHop);
        }

        var distanceChanged = !string.Equals(
            distance,
            nextDistance,
            StringComparison.Ordinal);
        SetField(ref distance, nextDistance, nameof(Distance));
        if (distanceChanged)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CarrierDistance)));
        }
        SetField(ref notes, nextNotes, nameof(Notes));
        SetField(
            ref jumpsRemaining,
            nextJumpsRemaining,
            nameof(JumpsRemaining));
        var stateChanged = isReached != nextIsReached
            || isCurrent != nextIsCurrent
            || isNext != nextIsNext;
        SetField(ref isReached, nextIsReached, nameof(IsReached));
        SetField(ref isCurrent, nextIsCurrent, nameof(IsCurrent));
        SetField(ref isNext, nextIsNext, nameof(IsNext));
        if (stateChanged)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(HasState)));
        }
    }

    private RouteBioTargetItemViewModel[] CreateBioTargets(
        FollowRouteHop source)
    {
        return source.BioTargets
            .Select((target, index) => new RouteBioTargetItemViewModel(
                Index,
                index,
                target))
            .ToArray();
    }

    private void RefreshBioTargets(FollowRouteHop source)
    {
        var canReuse = BioTargets.Count == source.BioTargets.Count;
        if (canReuse)
        {
            for (var index = 0; index < BioTargets.Count; index++)
            {
                if (!BioTargets[index].Matches(source.BioTargets[index]))
                {
                    canReuse = false;
                    break;
                }
            }
        }

        if (canReuse)
        {
            for (var index = 0; index < BioTargets.Count; index++)
            {
                BioTargets[index].Update(source.BioTargets[index]);
            }
        }
        else
        {
            BioTargets = CreateBioTargets(source);
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(BioTargets)));
        }

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(HasBioTargets)));
    }

    private void SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatCarrierNumber(double? value, int decimals)
    {
        return value is { } number
            ? number.ToString($"N{decimals}", CultureInfo.CurrentCulture)
            : "—";
    }
}

public sealed class RouteBioTargetItemViewModel : INotifyPropertyChanged
{
    private FollowRouteBioTarget target;
    private RouteBodyVisual bodyVisual;
    private IReadOnlyList<RouteBioDetailSegmentViewModel> compactDetailSegments;
    private IReadOnlyList<RouteBioDetailSegmentViewModel> inlineSegments;

    public RouteBioTargetItemViewModel(
        int hopIndex,
        int targetIndex,
        FollowRouteBioTarget target)
    {
        HopIndex = hopIndex;
        TargetIndex = targetIndex;
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        bodyVisual = RouteBodyAssetResolver.Resolve(target.Subtype);
        compactDetailSegments = BuildCompactDetailSegments();
        inlineSegments = BuildInlineSegments();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int HopIndex { get; }

    public int TargetIndex { get; }

    public string BodyName => target.BodyName;

    public long? BodyId => target.BodyId;

    public IReadOnlyList<string> Species => target.Species;

    public bool HasSpecies => Species.Count > 0;

    public bool NeedsScan => target.IsBiological && Species.Count == 0;

    public string Subtype => target.Subtype ?? string.Empty;

    public bool HasSubtype => !string.IsNullOrWhiteSpace(target.Subtype);

    public string BodyIconAssetPath => bodyVisual.AssetPath;

    public string BodyIconAccessibleName => bodyVisual.AccessibleName;

    public string DistanceToArrival => target.DistanceToArrivalLs is { } distance
        ? (distance < 100) switch
        {
            true => $"{distance:N2} LS",
            false => $"{distance:N0} LS"
        }
        : string.Empty;

    public bool HasDistanceToArrival => target.DistanceToArrivalLs is not null;

    public string EstimatedScanValue => FormatCredits(target.EstimatedScanValue);

    public bool HasEstimatedScanValue => target.EstimatedScanValue is not null;

    public string EstimatedMappingValue => FormatCredits(
        target.EstimatedMappingValue);

    public bool HasEstimatedMappingValue => target.EstimatedMappingValue is not null;

    public string EstimatedBiologyValue => FormatCredits(
        target.EstimatedBiologyValue);

    public bool HasEstimatedBiologyValue => target.EstimatedBiologyValue is not null;

    public bool IsTerraformable => target.IsTerraformable;

    public bool HasDetails => HasSubtype
        || HasDistanceToArrival
        || HasEstimatedScanValue
        || HasEstimatedMappingValue
        || HasEstimatedBiologyValue
        || IsTerraformable;

    public IReadOnlyList<RouteBioDetailSegmentViewModel> CompactDetailSegments =>
        compactDetailSegments;

    public IReadOnlyList<RouteBioDetailSegmentViewModel> InlineSegments =>
        inlineSegments;

    public string CompactDetails => string.Join(
        " | ",
        compactDetailSegments.Select(segment => segment.Text));

    public bool IsCompleted => target.IsCompleted;

    public string CompletionLabel => IsCompleted ? "COMPLETE" : "TO VISIT";

    public bool Matches(FollowRouteBioTarget candidate)
    {
        return BodyId is { } bodyId && candidate.BodyId is { } candidateId
            ? bodyId == candidateId
            : string.Equals(
                BodyName,
                candidate.BodyName,
                StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesBody(
        long? bodyId,
        string? bodyName,
        string systemName)
    {
        if (BodyId is { } targetBodyId && bodyId is { } eventBodyId)
        {
            return targetBodyId == eventBodyId;
        }

        if (string.IsNullOrWhiteSpace(bodyName))
        {
            return false;
        }

        var normalizedEventName = bodyName.Trim();
        if (!string.IsNullOrWhiteSpace(systemName)
            && normalizedEventName.StartsWith(
                systemName + " ",
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedEventName = normalizedEventName[
                (systemName.Length + 1)..];
        }

        return string.Equals(
                BodyName.Trim(),
                normalizedEventName,
                StringComparison.OrdinalIgnoreCase)
            || bodyName.EndsWith(
                " " + BodyName.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    public void Update(FollowRouteBioTarget next)
    {
        if (Equals(target, next))
        {
            return;
        }

        var identityChanged = !Matches(next);
        var speciesChanged = !target.Species.SequenceEqual(
            next.Species,
            StringComparer.Ordinal);
        var completionChanged = target.IsCompleted != next.IsCompleted;
        var subtypeChanged = !string.Equals(
            target.Subtype,
            next.Subtype,
            StringComparison.Ordinal);
        var distanceChanged = !EquivalentDistance(
            target.DistanceToArrivalLs,
            next.DistanceToArrivalLs);
        var scanValueChanged = target.EstimatedScanValue
            != next.EstimatedScanValue;
        var mappingValueChanged = target.EstimatedMappingValue
            != next.EstimatedMappingValue;
        var biologyValueChanged = target.EstimatedBiologyValue
            != next.EstimatedBiologyValue;
        var terraformableChanged = target.IsTerraformable
            != next.IsTerraformable;
        var biologicalChanged = target.IsBiological != next.IsBiological;
        var nextBodyVisual = subtypeChanged
            ? RouteBodyAssetResolver.Resolve(next.Subtype)
            : bodyVisual;
        var bodyVisualChanged = bodyVisual != nextBodyVisual;
        target = next;
        bodyVisual = nextBodyVisual;
        RaiseChanges(identityChanged, nameof(BodyName), nameof(BodyId));
        RaiseChanges(
            speciesChanged,
            nameof(Species),
            nameof(HasSpecies),
            nameof(NeedsScan));
        RaiseChanges(subtypeChanged, nameof(Subtype), nameof(HasSubtype));
        RaiseChanges(
            bodyVisualChanged,
            nameof(BodyIconAssetPath),
            nameof(BodyIconAccessibleName));
        RaiseChanges(
            distanceChanged,
            nameof(DistanceToArrival),
            nameof(HasDistanceToArrival));
        RaiseChanges(
            scanValueChanged,
            nameof(EstimatedScanValue),
            nameof(HasEstimatedScanValue));
        RaiseChanges(
            mappingValueChanged,
            nameof(EstimatedMappingValue),
            nameof(HasEstimatedMappingValue));
        RaiseChanges(
            biologyValueChanged,
            nameof(EstimatedBiologyValue),
            nameof(HasEstimatedBiologyValue));
        RaiseChanges(terraformableChanged, nameof(IsTerraformable));
        RaiseChanges(biologicalChanged, nameof(NeedsScan));

        var compactDetailsChanged = subtypeChanged
            || distanceChanged
            || scanValueChanged
            || mappingValueChanged
            || biologyValueChanged
            || terraformableChanged;
        RefreshCompactDetails(compactDetailsChanged);
        RefreshInlineDetails(identityChanged || compactDetailsChanged);
        RaiseChanges(
            completionChanged,
            nameof(IsCompleted),
            nameof(CompletionLabel));
    }

    private void RaiseChanges(
        bool changed,
        string propertyName,
        string? secondPropertyName = null,
        string? thirdPropertyName = null)
    {
        if (!changed)
        {
            return;
        }

        Raise(propertyName);
        if (secondPropertyName is not null)
        {
            Raise(secondPropertyName);
        }

        if (thirdPropertyName is not null)
        {
            Raise(thirdPropertyName);
        }
    }

    private void RefreshCompactDetails(bool changed)
    {
        if (!changed)
        {
            return;
        }

        compactDetailSegments = BuildCompactDetailSegments();
        Raise(nameof(HasDetails));
        Raise(nameof(CompactDetailSegments));
        Raise(nameof(CompactDetails));
    }

    private void RefreshInlineDetails(bool changed)
    {
        if (!changed)
        {
            return;
        }

        inlineSegments = BuildInlineSegments();
        Raise(nameof(InlineSegments));
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatCredits(long? value)
    {
        return value is null ? string.Empty : $"{value.Value:N0} CR";
    }

    private static bool EquivalentDistance(double? left, double? right)
    {
        if (left.HasValue != right.HasValue)
        {
            return false;
        }

        return !left.HasValue
            || Math.Abs(left.Value - right!.Value) <= 0.0000001d;
    }

    private static string FormatCompactCredits(long? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return Math.Abs(value.Value) >= 1_000_000
            ? $"{value.Value / 1_000_000d:0.#} M CR"
            : $"{value.Value:N0} CR";
    }

    private RouteBioDetailSegmentViewModel[]
        BuildCompactDetailSegments()
    {
        var details = new List<string>(6);
        if (HasSubtype)
        {
            details.Add(Subtype);
        }

        if (HasDistanceToArrival)
        {
            details.Add(DistanceToArrival);
        }

        if (HasEstimatedScanValue)
        {
            details.Add($"Scan {EstimatedScanValue}");
        }

        if (HasEstimatedMappingValue)
        {
            details.Add($"Map {EstimatedMappingValue}");
        }

        if (HasEstimatedBiologyValue)
        {
            details.Add($"Bio {FormatCompactCredits(target.EstimatedBiologyValue)}");
        }

        if (IsTerraformable)
        {
            details.Add("Terraformable");
        }

        return details
            .Select((text, index) => new RouteBioDetailSegmentViewModel(
                text,
                index < details.Count - 1))
            .ToArray();
    }

    private IReadOnlyList<RouteBioDetailSegmentViewModel>
        BuildInlineSegments()
    {
        return
        [
            new RouteBioDetailSegmentViewModel(
                BodyName,
                HasSeparator: false,
                IsBodyName: true),
            .. compactDetailSegments,
        ];
    }
}

public sealed record RouteBioDetailSegmentViewModel(
    string Text,
    bool HasSeparator,
    bool IsBodyName = false)
{
    public bool IsDetail => !IsBodyName;
}
