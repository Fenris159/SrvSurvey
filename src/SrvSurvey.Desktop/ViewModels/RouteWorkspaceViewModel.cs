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
    Justification = "The view model is window-scoped and its gate may have in-flight waiters."
)]
public sealed partial class RouteWorkspaceViewModel : INotifyPropertyChanged
{
    private SrvSurvey.Core.Exploration.ExplorationPoiCatalog poiCatalog = SrvSurvey
        .Core
        .Exploration
        .ExplorationPoiCatalog
        .Empty;

    /// <summary>Annotates existing route hops with local attractions without replacing route planning.</summary>
    public void ApplyPoiCatalog(SrvSurvey.Core.Exploration.ExplorationPoiCatalog catalog)
    {
        poiCatalog = catalog;
        foreach (RouteHopItemViewModel row in Hops)
        {
            row.ApplyPoiSummary(poiCatalog.ForSystem(row.Name, row.Hop.SystemAddress));
        }
    }

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
    private IReadOnlyList<FollowRouteHop> presentedHops = [];
    private IReadOnlyList<RouteHopItemViewModel> hops = [];
    private int lastReachedIndex = -1;
    private bool isActive;
    private bool autoCopy;
    private bool restockOnlyWhenLow;
    private FollowRouteRepeatMode repeatMode;
    private bool isReversed;
    private string carrierFuelPlanMessage = string.Empty;
    private bool isBusy;
    private EliteStatus? status;
    private string? musicTrack;
    private bool destinationMatchesNextHop;
    private string? lastCopiedHopName;
    private StatusDestination? lastDestination;
    private string statusMessage = "Waiting for a commander profile.";
    private Func<Task<bool>>? windowOpener;
    private Func<string, Task>? clipboardWriter;
    private FleetCarrierJumpCountdownState carrierJumpCountdownState = FleetCarrierJumpCountdownState.Inactive;
    private int profileRevision;
    private Task workspaceSave = Task.CompletedTask;

    /// <summary>
    /// Connects the route service, importers and optional native planner to one workspace.
    /// </summary>
    public RouteWorkspaceViewModel(
        FollowRouteService routeService,
        RouteNameImporter nameImporter,
        ISpanshRouteClient spanshClient,
        FollowRouteKind routeKind = FollowRouteKind.Standard,
        Func<DateTimeOffset>? utcNow = null,
        ISpanshPlannerClient? plannerClient = null,
        CurrentShipExportViewModel? currentShip = null
    )
        : this(
            routeService,
            nameImporter,
            spanshClient,
            new RouteWorkspaceOptions(routeKind, utcNow, plannerClient, currentShip)
        ) { }

    public RouteWorkspaceViewModel(
        FollowRouteService routeService,
        RouteNameImporter nameImporter,
        ISpanshRouteClient spanshClient,
        RouteWorkspaceOptions options
    )
    {
        this.routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        this.nameImporter = nameImporter ?? throw new ArgumentNullException(nameof(nameImporter));
        this.spanshClient = spanshClient ?? throw new ArgumentNullException(nameof(spanshClient));
        routeKind = options.Kind;
        dedicatedCarrier = options.DedicatedCarrier;
        selectedCarrierKind = dedicatedCarrier ?? FollowRouteCarrierKind.Personal;
        carrierSnapshotSource = options.CarrierSnapshotSource;
        refreshCarrierSnapshot = options.RefreshCarrierSnapshot;
        carrierNextJump = options.CarrierNextJump;
        carrierDestinationLookup = carrierNextJump is null ? null : new CarrierDestinationLookup(carrierNextJump);
        showCarrierNextJumpInformation = carrierNextJump?.SettingsStore.Load() ?? true;
        utcNow = options.UtcNow ?? (() => DateTimeOffset.UtcNow);
        openWindowCommand = new AsyncCommand(OpenWindowAsync, CanOpenWindow);
        refreshCommand = new AsyncCommand(RefreshAsync, HasProfileAndNotBusy);
        saveCommand = new AsyncCommand(SaveAsync, () => CanSaveChanges);
        discardCommand = new AsyncCommand(DiscardAsync, () => IsDirty && !IsBusy && !IsDialogVisible);
        copyNextHopCommand = new AsyncCommand(
            CopyNextHopAsync,
            () => NextHop is not null && clipboardWriter is not null && !IsBusy
        );
        loadSelectedRouteCommand = new AsyncCommand(
            LoadSelectedRouteAsync,
            () => SelectedSavedRoute is not null && !IsDirty && !IsBusy && !IsDialogVisible
        );
        newCommand = new AsyncCommand(() => ShowDialogAsync(RouteDialog.New), HasProfileAndIdleWorkspace);
        confirmNewCommand = new AsyncCommand(ConfirmNewAsync, HasProfileAndNotBusy);
        resetCommand = new AsyncCommand(ResetAsync, () => HasRoute && !IsBusy && !IsDialogVisible);
        saveAsCommand = new AsyncCommand(
            () => ShowDialogAsync(RouteDialog.SaveAs),
            () => HasRoute && !IsBusy && !IsDialogVisible
        );
        confirmSaveAsCommand = new AsyncCommand(ConfirmSaveAsAsync, () => HasRoute && !IsBusy && IsSaveAsVisible);
        notesCommand = new AsyncCommand(() => ShowDialogAsync(RouteDialog.Notes), HasProfileAndIdleWorkspace);
        saveNotesCommand = new AsyncCommand(SaveNotesAsync, () => !IsBusy && IsNotesVisible);
        deleteCommand = new AsyncCommand(
            () => ShowDialogAsync(RouteDialog.Delete),
            () => HasSavedRoute && !IsBusy && !IsDialogVisible
        );
        confirmDeleteCommand = new AsyncCommand(
            ConfirmDeleteAsync,
            () => HasSavedRoute && !IsBusy && IsDeleteConfirmationVisible
        );
        cancelDialogCommand = new AsyncCommand(CancelDialogAsync, () => IsDialogVisible);
        if (options.PlannerClient is not null)
        {
            SpanshSearch = new SpanshSearchViewModel(
                this,
                options.PlannerClient,
                options.CurrentShip ?? new CurrentShipExportViewModel()
            );
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CatalogChanged;

    internal event EventHandler? AutoCopySelected;

    public FollowRouteKind RouteKind => routeKind;

    public SpanshSearchViewModel? SpanshSearch { get; }

    public bool CanSearchSpansh => SpanshSearch is not null && HasProfile && !IsBusy && !IsDialogVisible;

    public bool IsFleetCarrierWorkspace => routeKind == FollowRouteKind.FleetCarrier;

    public bool IsSquadronCarrierWorkspace =>
        IsFleetCarrierWorkspace && dedicatedCarrier == FollowRouteCarrierKind.Squadron;

    public string CarrierRouteLabel => IsSquadronCarrierWorkspace ? "SC Routes" : "FC Routes";

    public string WindowTitle => WorkspaceText("Route Workspace", "FC Route Workspace", "SC Route Workspace");
    public string WorkspaceEyebrow => WorkspaceText("FOLLOWED ROUTE", "FLEET CARRIER ROUTE", "SQUADRON CARRIER ROUTE");
    public string WorkspaceHeading =>
        WorkspaceText("Navigation itinerary", "Fleet carrier itinerary", "Squadron carrier itinerary");
    public string WorkspaceDescription =>
        WorkspaceText(
            "Import a route, track each arrival, and keep the next system ready for the Galaxy Map.",
            "Import a fleet-carrier route, track each carrier jump, and keep the next destination ready.",
            "Plan your squadron carrier's itinerary and track its jumps independently of your personal carrier."
        );

    internal string WorkspaceText(string standard, string fleetCarrier, string squadron)
    {
        if (!IsFleetCarrierWorkspace)
        {
            return standard;
        }
        return IsSquadronCarrierWorkspace ? squadron : fleetCarrier;
    }

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
        private set
        {
            if (SetField(ref saveAsError, value))
            {
                OnPropertyChanged(nameof(HasSaveAsError));
            }
        }
    }

    public bool HasSaveAsError => !string.IsNullOrWhiteSpace(SaveAsError);

    public string NotesDraft
    {
        get => notesDraft;
        set => SetField(ref notesDraft, value);
    }

    public bool IsNewConfirmationVisible
    {
        get => isNewConfirmationVisible;
        private set => SetDialogField(ref isNewConfirmationVisible, value, nameof(IsNewConfirmationVisible));
    }

    public bool IsDeleteConfirmationVisible
    {
        get => isDeleteConfirmationVisible;
        private set => SetDialogField(ref isDeleteConfirmationVisible, value, nameof(IsDeleteConfirmationVisible));
    }

    public bool IsSaveAsVisible
    {
        get => isSaveAsVisible;
        private set => SetDialogField(ref isSaveAsVisible, value, nameof(IsSaveAsVisible));
    }

    public bool IsNotesVisible
    {
        get => isNotesVisible;
        private set => SetDialogField(ref isNotesVisible, value, nameof(IsNotesVisible));
    }

    public bool IsDialogVisible =>
        IsNewConfirmationVisible
        || IsDeleteConfirmationVisible
        || IsSaveAsVisible
        || IsNotesVisible
        || IsFuelWarningVisible;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseCommands();
                OnPropertyChanged(nameof(ImportButtonText));
                OnPropertyChanged(nameof(CanSearchSpansh));
                OnPropertyChanged(nameof(CanRepeatFromStart));
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
            bool normalized = value && CanActivate;
            if (SetField(ref isActive, normalized))
            {
                RefreshPresentation();
                _ = RetainWorkspaceAsync();
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
                _ = RetainWorkspaceAsync();
                if (value && HasRoute)
                {
                    AutoCopySelected?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public bool CanActivate => HasRoute && lastReachedIndex < draftHops.Length - 1;

    public bool RepeatFromStart
    {
        get => repeatMode == FollowRouteRepeatMode.FromStart;
        set
        {
            if (value && !FollowRouteProgression.CanRepeatFromStart(routeKind, draftHops))
            {
                return;
            }
            if (value || RepeatFromStart)
            {
                SetRepeatMode(value ? FollowRouteRepeatMode.FromStart : FollowRouteRepeatMode.None);
            }
        }
    }

    public bool CanRepeatFromStart => !IsBusy && FollowRouteProgression.CanRepeatFromStart(routeKind, draftHops);

    public string RepeatFromStartHint =>
        IsFleetCarrierWorkspace
            ? "Available when the first and last waypoints are the same system."
            : "Restart at the first waypoint whenever the route finishes.";

    public bool RepeatAndReverse
    {
        get => repeatMode == FollowRouteRepeatMode.Reverse;
        set
        {
            if (value || RepeatAndReverse)
            {
                SetRepeatMode(value ? FollowRouteRepeatMode.Reverse : FollowRouteRepeatMode.None);
            }
        }
    }

    private void SetRepeatMode(FollowRouteRepeatMode value)
    {
        if (repeatMode == value)
        {
            return;
        }
        repeatMode = value;
        OnPropertyChanged(nameof(RepeatFromStart));
        OnPropertyChanged(nameof(RepeatAndReverse));
        if (IsComplete && value != FollowRouteRepeatMode.None && RouteCount > 1)
        {
            FollowRouteDocument updated = FollowRouteProgression.SetProgress(
                CreateSnapshot()! with
                {
                    IsActive = true,
                },
                lastReachedIndex
            );
            ApplyWorkingProgress(updated);
            CaptureCarrierFuel();
            StatusMessage = CreateRepeatStatusMessage();
        }
        else
        {
            RefreshPresentation();
        }
        _ = RetainWorkspaceAsync();
    }

    private void ApplyWorkingProgress(FollowRouteDocument route)
    {
        ApplyDraft(
            route.GetNavigationHops(),
            route.LastReachedIndex,
            route.IsActive,
            route.AutoCopy,
            route.RestockOnlyWhenLow,
            route.RepeatMode,
            route.IsReversed
        );
    }

    private string CreateRepeatStatusMessage() =>
        RepeatAndReverse
            ? $"Route reversed. Next system: {NextHopName}."
            : $"Route restarted. Next system: {NextHopName}.";

    /// <summary>Applies a reversible carrier fuel forecast while retaining the imported source rows.</summary>
    public bool RestockOnlyWhenLow
    {
        get => restockOnlyWhenLow;
        set
        {
            if (SetField(ref restockOnlyWhenLow, IsFleetCarrierWorkspace && value))
            {
                RefreshPresentation();
                UpdateFuelWarning();
                _ = RetainWorkspaceAsync();
            }
        }
    }

    public string CarrierFuelPlanMessage
    {
        get => carrierFuelPlanMessage;
        private set
        {
            if (SetField(ref carrierFuelPlanMessage, value))
            {
                OnPropertyChanged(nameof(HasCarrierFuelPlanMessage));
            }
        }
    }

    public bool HasCarrierFuelPlanMessage => CarrierFuelPlanMessage.Length > 0;

    public bool IsComplete => HasRoute && lastReachedIndex >= draftHops.Length - 1;

    private IEnumerable<FollowRouteHop> SourceHops => isReversed ? draftHops.Reverse() : draftHops;

    public bool HasDefinitionChanges => loadedRoute is not null && !loadedRoute.Hops.SequenceEqual(SourceHops);

    public bool HasProgressChanges =>
        loadedRoute is not null
        && (
            (loadedRoute.IsActive && loadedRoute.Hops.Count > 0 && !loadedRoute.IsComplete) != IsActive
            || loadedRoute.AutoCopy != AutoCopy
            || loadedRoute.RestockOnlyWhenLow != RestockOnlyWhenLow
            || loadedRoute.LastReachedIndex != lastReachedIndex
            || loadedRoute.RepeatMode != repeatMode
            || loadedRoute.IsReversed != isReversed
            || loadedRoute.CarrierKind != selectedCarrierKind
        );

    public bool HasNotesChanges =>
        loadedRoute is not null
        && !string.Equals(loadedRoute.Notes?.Trim(), draftNotes?.Trim(), StringComparison.Ordinal);

    public bool IsDirty => HasDefinitionChanges || HasProgressChanges || HasNotesChanges;

    public bool CanSaveChanges =>
        HasSavedRoute && HasProgressChanges && !HasDefinitionChanges && !IsBusy && !IsDialogVisible;

    public bool CanEditWaypoints => HasRoute && !IsBusy && !IsDialogVisible && bioProgressLock.CurrentCount > 0;

    public int RouteCount => draftHops.Length;

    public int ReachedCount => Math.Clamp(lastReachedIndex + 1, 0, draftHops.Length);

    public int RemainingJumps => Math.Max(0, RouteCount - ReachedCount - 1);

    public bool HasCarrierEstimatedTimeRemaining => IsFleetCarrierWorkspace && RemainingJumps > 0;

    public string CarrierEstimatedTimeRemaining => FormatCarrierEstimatedTimeRemaining(RemainingJumps);

    /// <summary>
    /// Formats twenty minutes per remaining jump, omitting zero units and empty estimates.
    /// </summary>
    internal static string FormatCarrierEstimatedTimeRemaining(int remainingJumps)
    {
        if (remainingJumps <= 0)
        {
            return string.Empty;
        }
        var remaining = TimeSpan.FromMinutes(remainingJumps * 20d);
        List<string> parts = [];
        if (remaining.Days > 0)
        {
            parts.Add($"{remaining.Days}d");
        }
        if (remaining.Hours > 0)
        {
            parts.Add($"{remaining.Hours}h");
        }
        if (remaining.Minutes > 0)
        {
            parts.Add($"{remaining.Minutes}min");
        }
        return "ETR: " + string.Join(' ', parts);
    }

    public FollowRouteHop? NextHop
    {
        get
        {
            int nextIndex = lastReachedIndex + 1;
            return IsActive && nextIndex >= 0 && nextIndex < presentedHops.Count ? presentedHops[nextIndex] : null;
        }
    }

    public FollowRouteDocument? CreateSnapshot()
    {
        return loadedRoute is null
            ? null
            : loadedRoute with
            {
                Hops = SourceHops.ToArray(),
                LastReachedIndex = lastReachedIndex,
                IsActive = IsActive,
                AutoCopy = AutoCopy,
                RestockOnlyWhenLow = RestockOnlyWhenLow,
                RepeatMode = repeatMode,
                IsReversed = isReversed,
                CarrierKind = selectedCarrierKind,
                Notes = draftNotes,
            };
    }

    public string NextHopName =>
        NextHop?.Name
        ?? (
            IsComplete
                ? "Route complete"
                : (HasRoute) switch
                {
                    true => "Route paused",
                    false => "No route loaded",
                }
        );

    public bool HasNextHopSystemAddress => NextHop?.SystemAddress is > 0;

    public long? NextHopSystemAddress => NextHop?.SystemAddress is > 0 ? NextHop.SystemAddress : null;

    public string NextHopSystemAddressText => SystemAddressFormatter.Format(NextHop?.SystemAddress);

    public string ProgressSummary
    {
        get
        {
            if (!HasRoute)
            {
                return "Import a route to begin.";
            }

            if (IsComplete)
            {
                return $"All {RouteCount:N0} systems reached.";
            }

            if (lastReachedIndex < 0)
            {
                return $"Not started \u2022 {RouteCount:N0} systems";
            }

            return $"Reached {ReachedCount:N0} of {RouteCount:N0} systems";
        }
    }

    public string AutoCopySummary =>
        AutoCopy ? "Next-hop clipboard guidance is enabled." : "Next-hop clipboard guidance is disabled.";

    public bool ShouldAutoCopyNextHop => IsActive && AutoCopy && NextHop is not null;

    public bool ShouldShowGalaxyMapOverlay => IsGalaxyMapOpen && IsActive && NextHop is not null;

    private bool IsGalaxyMapOpen =>
        OverlayGameModeResolver.Resolve(status, musicTrack: musicTrack) == OverlayGameMode.GalaxyMap;

    public string NextHopDistance
    {
        get
        {
            FollowRouteHop? carrierWaypoint =
                IsFleetCarrierWorkspace && lastReachedIndex >= 0
                    ? draftHops.ElementAtOrDefault(lastReachedIndex)
                    : null;
            GalacticCoordinate? origin = carrierWaypoint?.Position ?? currentPosition;
            string originName = carrierWaypoint?.Name ?? CurrentSystem;
            double? distance =
                origin is { } start && NextHop?.Position is { } end ? start.DistanceTo(end) : (double?)null;
            return distance is null ? "Distance unavailable" : $"{distance:N2} ly from {originName}";
        }
    }

    public string NextHopGuidance => NextHop is { } hop ? CreateNotes(hop) : Unavailable;

    public bool HasNextHopGuidance =>
        NextHop is { } hop && (hop.Refuel || hop.Neutron || !string.IsNullOrWhiteSpace(hop.Notes));

    public string NextHopDestinationStatus => destinationMatchesNextHop ? "SELECTED IN GALAXY MAP" : "ROUTE TARGET";

    public string NextHopClipboardStatus =>
        string.Equals(lastCopiedHopName, NextHop?.Name, StringComparison.Ordinal)
            ? "NEXT SYSTEM COPIED"
            : (AutoCopy) switch
            {
                true => "AUTO-COPY READY",
                false => "MANUAL COPY",
            };

    public string CurrentSystem => string.IsNullOrWhiteSpace(currentSystemName) ? Unavailable : currentSystemName;

    public string RouteFileName =>
        loadedRoute is null
            ? Unavailable
            : (HasSavedRoute) switch
            {
                true => Path.GetFileName(loadedRoute.FilePath),
                false => "Not saved",
            };

    public string RouteName =>
        loadedRoute?.Name
        ?? (
            HasSavedRoute
                ? Path.GetFileNameWithoutExtension(RouteFileName)
                : (HasRoute) switch
                {
                    true => "New route",
                    false => "No active route",
                }
        );

    public string RouteNotesPreview => string.IsNullOrWhiteSpace(draftNotes) ? "No route notes." : draftNotes;

    public bool HasRouteNotes => !string.IsNullOrWhiteSpace(draftNotes);

    public string? LoadedSavedRoutePath => HasSavedRoute ? loadedRoute?.FilePath : null;

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

    public RouteHopItemViewModel? CurrentBioHop => Hops.FirstOrDefault(hop => hop.IsCurrent && hop.HasBioTargets);

    public IReadOnlyList<RouteBioTargetItemViewModel> CurrentBioTargets =>
        CurrentBioHop?.BioTargets ?? Array.Empty<RouteBioTargetItemViewModel>();

    public bool HasCurrentBioTargets => CurrentBioTargets.Count > 0;

    public bool ShouldShowRouteBioOverlay => HasRoute && HasCurrentBioTargets && (IsActive || IsComplete);

    public string CurrentBioSystemName => CurrentBioHop?.Name ?? CurrentSystem;

    public bool ShouldShowFleetCarrierRouteOverlay =>
        IsFleetCarrierWorkspace && HasRoute && IsActive && NextHop is not null;

    public bool HasCarrierJumpCountdown => carrierJumpCountdownState.IsActive;

    public string CarrierJumpCountdownTitle => carrierJumpCountdownState.Title;

    public string CarrierJumpCountdownValue => carrierJumpCountdownState.Countdown;

    public string CarrierJumpPhaseLabel => carrierJumpCountdownState.PhaseLabel;

    public string CarrierJumpPhaseCountdown => carrierJumpCountdownState.PhaseCountdown;

    public bool HasCarrierJumpPhaseCountdown => carrierJumpCountdownState.HasPhaseCountdown;

    public string CarrierJumpDestination => carrierJumpCountdownState.Destination;

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
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );
    }

    public void ApplyExternalNotes(string path, string? notes)
    {
        if (!IsLoadedSavedRoute(path) || loadedRoute is null)
        {
            return;
        }

        string? normalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        loadedRoute = loadedRoute with { Notes = normalized };
        draftNotes = normalized;
        RaiseRouteMetadataProperties();
        _ = RetainWorkspaceAsync();
    }

    public void ApplyExternalFavorite(string path, bool isFavorite)
    {
        if (!IsLoadedSavedRoute(path) || loadedRoute is null)
        {
            return;
        }

        loadedRoute = loadedRoute with { IsFavorite = isFavorite };
    }

    public async Task HandleRouteRenamedAsync(FollowRouteRenameResult result, bool notifyPeer = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        await workspaceSave;
        if (IsLoadedSavedRoute(result.PreviousPath) && loadedRoute is { } current)
        {
            loadedRoute = current with { FilePath = result.Route.FilePath, Name = result.Route.Name };
            RaiseRouteMetadataProperties();
        }

        await RefreshCatalogAsync(refreshPeer: false);
        if (IsLoadedSavedRoute(result.Route.FilePath))
        {
            SelectCatalogPath(result.Route.FilePath);
            await RetainWorkspaceAsync();
        }
        if (notifyPeer)
        {
            await NotifySharedCarrierRenameAsync(result);
        }
    }

    public async Task HandleLoadedRouteDeletedAsync()
    {
        await workspaceSave;
        if (frontierId is null)
        {
            return;
        }

        HasSavedRoute = false;
        ApplyDocument(await routeService.CreateNewAsync(frontierId));
        await RefreshCatalogAsync();
        StatusMessage = "The loaded route was removed. A new route workspace is ready.";
    }

    /// <summary>
    /// Loads the commander's route context and cancels searches across profile changes.
    /// </summary>
    public async Task<bool> UpdateContextAsync(
        string? nextFrontierId,
        string? nextSystemName,
        long? nextSystemAddress,
        GalacticCoordinate? nextPosition
    )
    {
        await workspaceSave;
        string? normalizedFrontierId = string.IsNullOrWhiteSpace(nextFrontierId) ? null : nextFrontierId;
        bool contextChanged =
            !string.Equals(currentSystemName, nextSystemName, StringComparison.Ordinal)
            || currentSystemAddress != nextSystemAddress
            || currentPosition != nextPosition;
        if (!string.Equals(frontierId, normalizedFrontierId, StringComparison.OrdinalIgnoreCase))
        {
            profileRevision++;
            SpanshSearch?.Cancel();
            ResetCarrierFuelContext();
            ResetCarrierDestination();
        }
        frontierId = normalizedFrontierId;
        currentSystemName = nextSystemName;
        currentSystemAddress = nextSystemAddress;
        currentPosition = nextPosition;
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(CurrentSystem));
        OnPropertyChanged(nameof(CanSearchSpansh));

        if (string.Equals(initializedFrontierId, frontierId, StringComparison.OrdinalIgnoreCase))
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
            ApplyCarrierKind(FollowRouteCarrierKind.Personal);
            ApplyDraft([], -1, false, false, nextRepeatMode: FollowRouteRepeatMode.None, nextIsReversed: false);
            StatusMessage = "Waiting for a commander profile.";
            return true;
        }

        await LoadAsync();
        PrepareCarrierSearch();
        return true;
    }

    public async Task ApplyJournalEventsAsync(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        ApplyFleetCarrierJumpEvents(journalEvents);
        if (loadedRoute is null || !HasRoute || journalEvents.Count == 0)
        {
            return;
        }

        try
        {
            if (
                !HasSavedRoute
                || HasDefinitionChanges
                || loadedRoute.RepeatMode != repeatMode
                || loadedRoute.IsReversed != isReversed
                || loadedRoute.CarrierKind != selectedCarrierKind
                || (repeatMode != FollowRouteRepeatMode.None && IsDirty)
            )
            {
                await ApplyDraftArrivalEventsAsync(journalEvents);
            }
            else
            {
                await ApplyRouteArrivalEventsAsync(journalEvents);
            }
            await ApplyBioArrivalEventsAsync(journalEvents);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Route progress could not be saved: " + exception.Message;
        }
    }

    /// <summary>Advances an unsaved working itinerary without changing its saved library baseline.</summary>
    private async Task ApplyDraftArrivalEventsAsync(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        string arrivalEvent = IsFleetCarrierWorkspace ? "CarrierJump" : "FSDJump";
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            if (!TryGetArrivalTarget(journalEvent, arrivalEvent, NextHop, out string? name, out long? address))
            {
                continue;
            }

            FollowRouteArrivalResult result = await routeService.ApplyArrivalAsync(
                CreateSnapshot()!,
                name,
                address,
                persist: false
            );
            if (!result.Changed)
            {
                continue;
            }

            ApplyDraft(
                result.Route.GetNavigationHops(),
                result.Route.LastReachedIndex,
                result.Route.IsActive,
                result.Route.AutoCopy,
                result.Route.RestockOnlyWhenLow,
                result.Route.RepeatMode,
                result.Route.IsReversed
            );
            if (result.Restarted)
            {
                CaptureCarrierFuel(journalEvent);
                StatusMessage = CreateRepeatStatusMessage();
            }
            else
            {
                StatusMessage = result.Route.IsComplete
                    ? $"Route complete after arriving at {name}."
                    : $"Arrived at hop #{result.ReachedIndex + 1:N0}: {name}.";
            }
            await RetainWorkspaceAsync();
        }
    }

    private async Task ApplyRouteArrivalEventsAsync(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        bool hadUnsavedChanges = IsDirty;
        (bool changed, int? reachedIndex, bool restarted) = await ProcessArrivalEventsAsync(journalEvents)
            .ConfigureAwait(true);
        if (!changed || loadedRoute is null)
        {
            return;
        }

        if (!hadUnsavedChanges)
        {
            ApplyDocument(loadedRoute);
        }
        else
        {
            RefreshPresentation();
        }

        StatusMessage = BuildArrivalStatusMessage(reachedIndex, hadUnsavedChanges, restarted);
        await RetainWorkspaceAsync();
    }

    private async Task<(bool Changed, int? ReachedIndex, bool Restarted)> ProcessArrivalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents
    )
    {
        bool changed = false;
        int? reachedIndex = null;
        bool restarted = false;
        string arrivalEvent = IsFleetCarrierWorkspace ? "CarrierJump" : "FSDJump";
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            if (
                !TryGetArrivalTarget(
                    journalEvent,
                    arrivalEvent,
                    loadedRoute!.NextHop,
                    out string? name,
                    out long? address
                )
            )
            {
                continue;
            }

            FollowRouteArrivalResult result = await routeService.ApplyArrivalAsync(loadedRoute, name, address);
            if (!result.Changed)
            {
                continue;
            }

            loadedRoute = result.Route;
            if (result.Restarted)
            {
                ApplyDocument(result.Route);
                CaptureCarrierFuel(journalEvent);
            }
            changed = true;
            reachedIndex = result.ReachedIndex;
            restarted = result.Restarted;
        }

        return (changed, reachedIndex, restarted);
    }

    private bool TryGetArrivalTarget(
        JournalEventEnvelope journalEvent,
        string arrivalEvent,
        FollowRouteHop? nextHop,
        out string name,
        out long? address
    )
    {
        name = string.Empty;
        address = null;
        if (
            !string.Equals(journalEvent.EventName, arrivalEvent, StringComparison.Ordinal)
            || !MatchesCarrierArrival(journalEvent, nextHop)
        )
        {
            return false;
        }

        string? systemName = GetString(journalEvent.Payload, "StarSystem");
        address = GetInt64(journalEvent.Payload, "SystemAddress");
        if (string.IsNullOrWhiteSpace(systemName) && address is null)
        {
            return false;
        }

        name = systemName ?? string.Empty;
        return true;
    }

    private string BuildArrivalStatusMessage(int? reachedIndex, bool hadUnsavedChanges, bool restarted)
    {
        FollowRouteDocument route =
            loadedRoute
            ?? throw new InvalidOperationException("A loaded route is required when reporting arrival status.");
        if (restarted)
        {
            return CreateRepeatStatusMessage();
        }
        string name = currentSystemName ?? "the route";
        if (reachedIndex is { } index && index >= 0 && index < route.Hops.Count)
        {
            name = route.GetNavigationHop(index).Name;
        }

        if (route.IsComplete)
        {
            return $"Route complete after arriving at {name}.";
        }

        string message = $"Arrived at hop #{reachedIndex + 1:N0}: {name}.";
        if (hadUnsavedChanges)
        {
            message += " Unsaved route edits were kept.";
        }

        return message;
    }

    private async Task ApplyBioArrivalEventsAsync(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        if (!HasCurrentBioTargets)
        {
            return;
        }

        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            if (!IsBioArrivalEvent(journalEvent.EventName))
            {
                continue;
            }

            string? bodyName = GetString(journalEvent.Payload, "Body") ?? GetString(journalEvent.Payload, "BodyName");
            long? bodyId = GetInt64(journalEvent.Payload, "BodyID");
            if (string.IsNullOrWhiteSpace(bodyName) && bodyId is null)
            {
                continue;
            }

            long? eventSystemAddress = GetInt64(journalEvent.Payload, "SystemAddress");
            RouteHopItemViewModel? currentHop = CurrentBioHop;
            if (
                currentHop is null
                || (
                    eventSystemAddress is { } address
                    && currentHop.Hop.SystemAddress is { } routeAddress
                    && address != routeAddress
                )
            )
            {
                continue;
            }

            RouteBioTargetItemViewModel? target = CurrentBioTargets.FirstOrDefault(candidate =>
                !candidate.IsCompleted && candidate.MatchesBody(bodyId, bodyName, currentHop.Name)
            );
            if (target is not null)
            {
                await SetBioTargetCompletedAsync(target, isCompleted: true);
            }
        }
    }

    private static bool IsBioArrivalEvent(string eventName)
    {
        return eventName is "ApproachBody" or "SupercruiseExit" or "Touchdown" or "Disembark";
    }

    public void ApplyFleetCarrierJumpEvents(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!IsFleetCarrierWorkspace || journalEvents.Count == 0)
        {
            return;
        }

        DateTimeOffset now = utcNow();
        double? projectedFuel = CarrierFuelAfterJump;
        ObserveCarrierFuel(journalEvents);
        ReadObservedCarrierFuel();
        bool changed = false;
        foreach (JournalEventEnvelope journalEvent in journalEvents.Where(MatchesSelectedCarrier))
        {
            changed |= carrierJumpCountdown.Apply(journalEvent, now);
        }

        if (changed)
        {
            if (
                carrierJumpCountdown.Current.Kind == FleetCarrierJumpCountdownKind.PostJumpCooldown
                && CarrierJumpKind != FleetCarrierJumpCountdownKind.PostJumpCooldown
            )
            {
                carrierArrivedFuelEstimate = projectedFuel;
                carrierArrivedFuelSystem = carrierJumpCountdown.Current.Destination;
            }
            ApplyCarrierJumpCountdownState();
        }
        RefreshCarrierDestination();
    }

    public void RefreshCarrierJumpCountdown()
    {
        if (IsFleetCarrierWorkspace && carrierJumpCountdown.Refresh(utcNow()))
        {
            ApplyCarrierJumpCountdownState();
        }
    }

    public async Task UpdateStatusAsync(
        EliteStatus nextStatus,
        string? nextMusicTrack = null,
        bool allowAutoCopy = true
    )
    {
        ArgumentNullException.ThrowIfNull(nextStatus);
        bool wasGalaxyMapOpen = IsGalaxyMapOpen;
        status = nextStatus;
        musicTrack = nextMusicTrack;
        bool enteredGalaxyMap = !wasGalaxyMapOpen && IsGalaxyMapOpen;
        lastDestination = nextStatus.Destination;
        destinationMatchesNextHop = IsGalaxyMapOpen && IsNextHop(lastDestination);
        if (!IsGalaxyMapOpen)
        {
            lastCopiedHopName = null;
        }

        RaiseOverlayProperties();
        if (enteredGalaxyMap && ShouldAutoCopyNextHop && allowAutoCopy)
        {
            await CopyNextHopAsync();
        }
    }

    public async Task RefreshAsync()
    {
        if (!IsFleetCarrierWorkspace || !HasRoute)
        {
            await LoadAsync();
            if (IsFleetCarrierWorkspace)
            {
                PrepareCarrierSearch();
                CaptureCarrierFuel();
            }
            return;
        }
        int revision = profileRevision;
        try
        {
            IsBusy = true;
            if (refreshCarrierSnapshot is not null)
            {
                await refreshCarrierSnapshot();
            }
            if (revision != profileRevision)
            {
                return;
            }
            CaptureCarrierFuel();
            StatusMessage = "Carrier fuel recalculated from the current waypoint.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Carrier fuel could not be refreshed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportNamesAsync(IEnumerable<string> names, bool appendToRoute = false)
    {
        if (!HasProfile || IsBusy)
        {
            return;
        }

        int revision = profileRevision;
        try
        {
            IsBusy = true;
            StatusMessage = "Resolving route systems with Spansh\u2026";
            RouteNameImportResult result = await nameImporter.ImportAsync(names);
            if (revision != profileRevision)
            {
                return;
            }
            if (result.Hops.Count == 0)
            {
                StatusMessage = "No system names were found to import.";
                return;
            }
            if (appendToRoute && HasRoute)
            {
                ApplyDraft([.. draftHops, .. result.Hops], lastReachedIndex, IsActive, AutoCopy, RestockOnlyWhenLow);
            }
            else
            {
                ApplyImportedHops(result.Hops);
            }
            StatusMessage =
                $"Imported {result.Hops.Count:N0} systems; "
                + $"{result.ResolvedCount:N0} resolved and "
                + $"{result.UnresolvedCount:N0} kept by name.";
            await RetainWorkspaceAsync();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "System names could not be imported: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ImportNamesTextAsync(string? text, bool appendToRoute = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "No system names were available to import.";
            return Task.CompletedTask;
        }

        return ImportNamesAsync(RouteNameImporter.ParseNames(text), appendToRoute);
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

        if (!SpanshRouteUrlParser.TryParse(text, out SpanshRouteReference? reference) || reference is null)
        {
            StatusMessage = "The clipboard does not contain a valid Spansh route URL or job ID.";
            return;
        }

        if (IsFleetCarrierWorkspace && reference.Kind != SpanshRouteKind.FleetCarrier)
        {
            StatusMessage = $"{CarrierRouteLabel} accepts Spansh Fleet Carrier Router result URLs only.";
            return;
        }

        if (!IsFleetCarrierWorkspace && reference.Kind == SpanshRouteKind.FleetCarrier)
        {
            StatusMessage = "Import Fleet Carrier Router results from the FC Routes or SC Routes tab.";
            return;
        }

        int revision = profileRevision;
        try
        {
            IsBusy = true;
            StatusMessage = $"Importing Spansh {GetRouteKindLabel(reference.Kind)} route\u2026";
            IReadOnlyList<FollowRouteHop> importedHops = await spanshClient.GetRouteAsync(reference);
            if (revision != profileRevision)
            {
                return;
            }
            if (importedHops.Count == 0)
            {
                StatusMessage = "Spansh returned a route with no systems.";
                return;
            }
            ApplyImportedHops(importedHops, reference.Kind);
            StatusMessage = $"Imported {importedHops.Count:N0} systems from Spansh. Review it, then use Save As...";
            await RetainWorkspaceAsync();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The Spansh route could not be imported: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Submits and imports a planner result without replacing drafts on failures or cancellation.
    /// </summary>
    public async Task<bool> SearchSpanshAsync(
        ISpanshPlannerClient plannerClient,
        SpanshPlannerRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!CanSearchSpansh || (request.Planner.Kind == SpanshRouteKind.FleetCarrier) != IsFleetCarrierWorkspace)
        {
            return false;
        }
        int revision = profileRevision;
        try
        {
            IsBusy = true;
            StatusMessage = $"Searching Spansh {request.Planner.Name}…";
            SpanshRouteReference reference = await plannerClient.SubmitAsync(request, cancellationToken);
            IReadOnlyList<FollowRouteHop> importedHops = await spanshClient.GetRouteAsync(reference, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (revision != profileRevision)
            {
                return false;
            }
            if (importedHops.Count == 0)
            {
                StatusMessage = "Spansh returned a route with no systems.";
                return false;
            }
            ApplyImportedHops(importedHops, reference.Kind);
            StatusMessage = $"Imported {importedHops.Count:N0} systems from Spansh. Review it, then use Save As...";
            await RetainWorkspaceAsync();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Spansh search cancelled.";
            return false;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "The Spansh search timed out. Please try again.";
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception) || exception is JsonException)
        {
            StatusMessage = "The Spansh search could not be loaded: " + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RemoveHop(RouteHopItemViewModel hop)
    {
        ArgumentNullException.ThrowIfNull(hop);
        if (!CanEditWaypoints || !hops.Contains(hop))
        {
            return;
        }

        int index = hop.Index;
        int nextLastReachedIndex = index <= lastReachedIndex ? lastReachedIndex - 1 : lastReachedIndex;
        ApplyDraft(
            [.. draftHops[..index], .. draftHops[(index + 1)..]],
            nextLastReachedIndex,
            IsActive,
            AutoCopy,
            RestockOnlyWhenLow
        );
        StatusMessage = $"Removed {hop.Name} from the route draft.";
        _ = RetainWorkspaceAsync();
    }

    public void SetProgressThrough(int index, bool reached)
    {
        if (index < 0 || index >= draftHops.Length)
        {
            return;
        }

        FollowRouteDocument updated = FollowRouteProgression.SetProgress(
            CreateSnapshot()!,
            reached ? index : index - 1
        );
        ApplyWorkingProgress(updated);
        if (reached && index == draftHops.Length - 1 && updated.IsActive)
        {
            CaptureCarrierFuel();
            StatusMessage = CreateRepeatStatusMessage();
        }
        _ = RetainWorkspaceAsync();
    }

    public async Task SetBioTargetCompletedAsync(RouteBioTargetItemViewModel target, bool isCompleted)
    {
        ArgumentNullException.ThrowIfNull(target);
        await bioProgressLock.WaitAsync();
        try
        {
            OnPropertyChanged(nameof(CanEditWaypoints));
            if (
                target.HopIndex < 0
                || target.HopIndex >= draftHops.Length
                || target.TargetIndex < 0
                || target.TargetIndex >= draftHops[target.HopIndex].BioTargets.Count
            )
            {
                return;
            }

            FollowRouteBioTarget current = draftHops[target.HopIndex].BioTargets[target.TargetIndex];
            if (!target.Matches(current) || current.IsCompleted == isCompleted)
            {
                return;
            }

            FollowRouteHop originalHop = draftHops[target.HopIndex];
            bool saveLibraryProgress = HasSavedRoute && !HasDefinitionChanges;
            ReplaceDraftBioTarget(target.HopIndex, target.TargetIndex, current with { IsCompleted = isCompleted });
            RefreshPresentation();
            if (loadedRoute is null || !saveLibraryProgress)
            {
                StatusMessage = isCompleted
                    ? $"Marked {target.BodyName} complete in the route draft."
                    : $"Reopened {target.BodyName} in the route draft.";
                await RetainWorkspaceAsync();
                return;
            }

            try
            {
                int libraryHopIndex =
                    loadedRoute.IsReversed == isReversed ? target.HopIndex : draftHops.Length - target.HopIndex - 1;
                loadedRoute = await routeService.SetBioTargetCompletedAsync(
                    loadedRoute,
                    libraryHopIndex,
                    target.TargetIndex,
                    isCompleted
                );
                draftHops[target.HopIndex] = loadedRoute.GetNavigationHop(libraryHopIndex);
                RefreshPresentation();
                StatusMessage = isCompleted ? $"Marked {target.BodyName} complete." : $"Reopened {target.BodyName}.";
                await RetainWorkspaceAsync();
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                draftHops[target.HopIndex] = originalHop;
                RefreshPresentation();
                StatusMessage = "Body destination progress could not be saved: " + exception.Message;
            }
        }
        finally
        {
            bioProgressLock.Release();
            OnPropertyChanged(nameof(CanEditWaypoints));
        }
    }

    public async Task SaveAsync()
    {
        await workspaceSave;
        if (loadedRoute is null || !CanSaveChanges)
        {
            return;
        }

        try
        {
            IsBusy = true;
            FollowRouteDocument saved = await routeService.SaveProgressAsync(
                loadedRoute with
                {
                    LastReachedIndex = lastReachedIndex,
                    IsActive = IsActive,
                    AutoCopy = AutoCopy,
                    RestockOnlyWhenLow = RestockOnlyWhenLow,
                    RepeatMode = repeatMode,
                    IsReversed = isReversed,
                    CarrierKind = selectedCarrierKind,
                }
            );
            ApplyDocument(saved, requestAutoCopyOwnership: true);
            await RetainWorkspaceAsync();
            StatusMessage = saved.IsComplete
                ? "Route progress saved as complete."
                : (saved.IsActive) switch
                {
                    true => $"Changes saved. Next system: {saved.NextHop?.Name ?? Unavailable}.",
                    false => "Route progress saved in a paused state.",
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
        await workspaceSave;
        if (loadedRoute is null)
        {
            return;
        }

        if (!HasSavedRoute)
        {
            ApplyDocument(loadedRoute);
            StatusMessage = "The new route draft was restored to its initial state.";
            await RetainWorkspaceAsync();
            return;
        }

        try
        {
            IsBusy = true;
            FollowRouteLoadResult result = await routeService.ReloadAsync(loadedRoute);
            if (result.Route is null)
            {
                StatusMessage = result.Error ?? "The saved route could not be restored.";
                return;
            }

            ApplyDocument(result.Route, requestAutoCopyOwnership: true);
            StatusMessage = "Changes were undone to the route's last saved state.";
            await RetainWorkspaceAsync();
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

    public async Task ResetAsync()
    {
        if (!HasRoute)
        {
            return;
        }

        lastReachedIndex = -1;
        isActive = draftHops.Length > 0;
        CaptureCarrierFuel();
        RefreshPresentation();
        StatusMessage = "Route progress reset in the draft. Save Changes to keep it.";
        await RetainWorkspaceAsync();
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
        await workspaceSave;
        if (frontierId is null)
        {
            return;
        }

        CloseDialogs();
        try
        {
            IsBusy = true;
            FollowRouteDocument route = await routeService.CreateNewAsync(frontierId);
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
        await workspaceSave;
        FollowRouteDocument? snapshot = CreateSnapshot();
        if (snapshot is null || !HasRoute)
        {
            return;
        }

        try
        {
            IsBusy = true;
            FollowRouteDocument saved = await routeService.SaveAsAsync(snapshot, SaveAsName);
            HasSavedRoute = true;
            ApplyDocument(saved, requestAutoCopyOwnership: true);
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
        string? normalized = string.IsNullOrWhiteSpace(NotesDraft) ? null : NotesDraft.Trim();
        try
        {
            IsBusy = true;
            if (HasSavedRoute && loadedRoute is not null)
            {
                loadedRoute = await routeService.SaveNotesAsync(loadedRoute, normalized);
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
            await RetainWorkspaceAsync();
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
        await workspaceSave;
        if (!HasSavedRoute || loadedRoute is null || frontierId is null)
        {
            return;
        }

        CloseDialogs();
        try
        {
            IsBusy = true;
            string deletedPath = loadedRoute.FilePath;
            await routeService.DeleteAsync(loadedRoute);
            await NotifySharedCarrierRouteDeletedAsync(deletedPath);
            HasSavedRoute = false;
            FollowRouteDocument route = await routeService.CreateNewAsync(frontierId);
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
        await workspaceSave;
        if (frontierId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            FollowRouteLoadResult result = await routeService.LoadNamedAsync(frontierId, fileName, isLegacy);
            if (result.Route is null)
            {
                StatusMessage = result.Error ?? "The saved route could not be loaded.";
                return;
            }

            HasSavedRoute = true;
            ApplyDocument(result.Route, requestAutoCopyOwnership: true);
            CaptureCarrierFuel();
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

    public async Task<bool> ActivateSavedRouteAsync(string fileName, bool isLegacy, string filePath)
    {
        await workspaceSave;
        if (frontierId is null || IsBusy)
        {
            return false;
        }

        if (IsDirty)
        {
            StatusMessage = "Save or undo the current Route Workspace changes before activating another route.";
            return false;
        }

        try
        {
            IsBusy = true;
            if (loadedRoute is not null && HasSavedRoute && loadedRoute.IsActive && !IsLoadedSavedRoute(filePath))
            {
                FollowRouteDocument paused = await routeService.SetActiveAsync(
                    loadedRoute,
                    isActive: false,
                    currentSystemAddress: currentSystemAddress
                );
                ApplyDocument(paused);
            }

            FollowRouteLoadResult result = await routeService.LoadNamedAsync(frontierId, fileName, isLegacy);
            if (result.Route is null)
            {
                StatusMessage = result.Error ?? "The saved route could not be activated.";
                return false;
            }

            FollowRouteDocument activated = await routeService.SetActiveAsync(
                result.Route,
                isActive: true,
                currentSystemAddress: currentSystemAddress
            );
            HasSavedRoute = true;
            ApplyDocument(activated, requestAutoCopyOwnership: true);
            CaptureCarrierFuel();
            SelectCatalogPath(result.Path);
            if (activated.IsActive)
            {
                StatusMessage =
                    $"Activated {activated.Name ?? Path.GetFileNameWithoutExtension(result.Path)}."
                    + $" Next system: {activated.NextHop?.Name ?? Unavailable}.";
                return true;
            }

            StatusMessage =
                activated.Hops.Count == 0
                    ? "The selected route has no systems and cannot be activated."
                    : "The selected route is complete. Reset its progress before activating it.";
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The saved route could not be activated: " + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DeactivateCurrentRouteAsync()
    {
        await workspaceSave;
        if (frontierId is null || loadedRoute is null || !HasRoute || IsBusy)
        {
            return false;
        }

        if (HasSavedRoute && (HasDefinitionChanges || HasNotesChanges))
        {
            StatusMessage =
                "Save or undo the current Route Workspace definition and note changes before deactivating it.";
            return false;
        }

        try
        {
            IsBusy = true;
            bool wasSavedRoute = HasSavedRoute;
            if (wasSavedRoute)
            {
                await routeService.SaveProgressAsync(
                    loadedRoute with
                    {
                        LastReachedIndex = lastReachedIndex,
                        IsActive = false,
                        AutoCopy = AutoCopy,
                        RestockOnlyWhenLow = RestockOnlyWhenLow,
                        RepeatMode = repeatMode,
                        IsReversed = isReversed,
                        CarrierKind = selectedCarrierKind,
                    }
                );
            }
            FollowRouteDocument blankRoute = await routeService.CreateNewAsync(frontierId);
            HasSavedRoute = false;
            ApplyDocument(blankRoute);
            await RefreshCatalogAsync();
            StatusMessage = wasSavedRoute
                ? "Route tracking deactivated. The saved route and its progress were preserved."
                : "No active route";
            return true;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The current route could not be deactivated: " + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SetAutoCopyAsync(bool enabled)
    {
        await workspaceSave;
        if (frontierId is null || loadedRoute is null || !HasRoute || IsBusy)
        {
            StatusMessage = "Activate a saved route before changing next-hop auto-copy.";
            return false;
        }

        if (!HasSavedRoute)
        {
            AutoCopy = enabled;
            await RetainWorkspaceAsync();
            return true;
        }

        bool loadedIsActive = loadedRoute.IsActive && loadedRoute.Hops.Count > 0 && !loadedRoute.IsComplete;
        bool hasTrackingChanges =
            loadedIsActive != IsActive
            || loadedRoute.LastReachedIndex != lastReachedIndex
            || loadedRoute.RepeatMode != repeatMode
            || loadedRoute.IsReversed != isReversed
            || loadedRoute.CarrierKind != selectedCarrierKind
            || loadedRoute.RestockOnlyWhenLow != RestockOnlyWhenLow;
        if (HasDefinitionChanges || HasNotesChanges || hasTrackingChanges)
        {
            StatusMessage = "Save or undo the current Route Workspace changes before changing next-hop auto-copy here.";
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
            FollowRouteDocument saved = await routeService.SaveProgressAsync(
                loadedRoute with
                {
                    LastReachedIndex = lastReachedIndex,
                    IsActive = IsActive,
                    AutoCopy = enabled,
                    RestockOnlyWhenLow = RestockOnlyWhenLow,
                    RepeatMode = repeatMode,
                    IsReversed = isReversed,
                    CarrierKind = selectedCarrierKind,
                }
            );
            ApplyDocument(saved, requestAutoCopyOwnership: true);
            await RetainWorkspaceAsync();
            StatusMessage = enabled
                ? "Next-hop auto-copy enabled for this route."
                : "Next-hop auto-copy disabled for this route.";
            return true;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Next-hop auto-copy could not be changed: " + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisableAutoCopyForCompetingRouteAsync()
    {
        await workspaceSave;
        if (!AutoCopy)
        {
            return;
        }

        if (
            frontierId is null
            || loadedRoute is null
            || !HasSavedRoute
            || IsBusy
            || HasDefinitionChanges
            || HasNotesChanges
        )
        {
            if (!HasSavedRoute && loadedRoute is not null)
            {
                loadedRoute = loadedRoute with { AutoCopy = false };
            }

            AutoCopy = false;
            StatusMessage =
                "Next-hop auto-copy was disabled because another Galaxy Map auto-copy setting was selected.";
            await workspaceSave;
            return;
        }

        try
        {
            IsBusy = true;
            FollowRouteDocument saved = await routeService.SaveProgressAsync(
                loadedRoute with
                {
                    LastReachedIndex = lastReachedIndex,
                    IsActive = IsActive,
                    AutoCopy = false,
                    RestockOnlyWhenLow = RestockOnlyWhenLow,
                    RepeatMode = repeatMode,
                    IsReversed = isReversed,
                    CarrierKind = selectedCarrierKind,
                }
            );
            ApplyDocument(saved);
            await RetainWorkspaceAsync();
            StatusMessage =
                "Next-hop auto-copy was disabled because another Galaxy Map auto-copy setting was selected.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AutoCopy = false;
            StatusMessage =
                "Next-hop auto-copy was disabled for this session, "
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
            StatusMessage = "The next hop could not be copied: " + exception.Message;
        }
    }

    private async Task LoadAsync()
    {
        await workspaceSave;
        if (frontierId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            FollowRouteLoadResult result = await routeService.LoadAsync(frontierId);
            if (result.Route is null)
            {
                loadedRoute = null;
                HasSavedRoute = false;
                ApplyCarrierKind(FollowRouteCarrierKind.Personal);
                ApplyDraft([], -1, false, false, nextRepeatMode: FollowRouteRepeatMode.None, nextIsReversed: false);
                await RefreshCatalogAsync();
                StatusMessage = result.Error ?? "The route could not be loaded.";
                return;
            }

            HasSavedRoute = result.Exists;
            ApplyDocument(result.Route);
            if (result.WorkspaceDraft is { } draft)
            {
                loadedRoute = result.Route with { SourceSpanshKind = draft.SourceSpanshKind };
                draftNotes = draft.Notes;
                ApplyCarrierKind(draft.CarrierKind);
                ApplyDraft(
                    draft.GetNavigationHops(),
                    draft.LastReachedIndex,
                    draft.IsActive,
                    draft.AutoCopy,
                    draft.RestockOnlyWhenLow,
                    draft.RepeatMode,
                    draft.IsReversed
                );
                RaiseRouteMetadataProperties();
            }
            await RefreshCatalogAsync();
            SelectCatalogPath(result.Path);
            StatusMessage = result.Exists
                ? $"Loaded {result.Route.Hops.Count:N0} route systems from " + Path.GetFileName(result.Path) + "."
                : "No followed route is saved for this commander.";
            if (result.WorkspaceDraft is { Hops.Count: > 0 })
            {
                StatusMessage = "Restored the working route. Save As... adds it to your route library.";
            }
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

    /// <summary>Waits for all queued working-route updates before shutdown or a new session.</summary>
    public Task FlushWorkspaceAsync() => workspaceSave;

    /// <summary>Queues immutable snapshots so rapid edits cannot restore an older working itinerary.</summary>
    private Task RetainWorkspaceAsync()
    {
        FollowRouteDocument? snapshot = CreateSnapshot();
        if (snapshot is null)
        {
            return workspaceSave;
        }

        workspaceSave = SaveWorkspaceAfterAsync(workspaceSave, snapshot, HasSavedRoute, profileRevision);
        return workspaceSave;
    }

    /// <summary>Serializes workspace writes and reports storage failures in the originating commander context.</summary>
    private async Task SaveWorkspaceAfterAsync(
        Task previous,
        FollowRouteDocument snapshot,
        bool hasSavedRoute,
        int revision
    )
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportWorkspaceSaveFailure(exception, revision);
        }

        try
        {
            await routeService.SaveWorkspaceAsync(snapshot, hasSavedRoute).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // This background boundary reports failures without faulting subsequent edits or shutdown.
            ReportWorkspaceSaveFailure(exception, revision);
        }
    }

    private void ReportWorkspaceSaveFailure(Exception exception, int revision)
    {
        if (revision == profileRevision)
        {
            StatusMessage = "The working route could not be retained: " + exception.Message;
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

    internal void SelectCarrierAutoCopyTarget()
    {
        if (IsFleetCarrierWorkspace && HasRoute && AutoCopy)
        {
            AutoCopySelected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyImportedHops(IReadOnlyList<FollowRouteHop> importedHops, SpanshRouteKind? sourceKind = null)
    {
        if (loadedRoute is not null)
        {
            loadedRoute = loadedRoute with { SourceSpanshKind = sourceKind };
        }

        ClearCarrierForecast();
        FollowRouteHop[] nextHops = importedHops.ToArray();
        FleetCarrierFuelSnapshot? carrier = IsFleetCarrierWorkspace ? ReadCarrierFuel() : null;
        bool startsAtCarrier =
            carrier is not null
            && nextHops.Length > 0
            && string.Equals(nextHops[0].Name, carrier.SystemName, StringComparison.OrdinalIgnoreCase);
        int nextLastIndex =
            startsAtCarrier
            || (nextHops.Length > 0 && currentSystemAddress is { } address && nextHops[0].SystemAddress == address)
                ? 0
                : -1;
        ApplyDraft(
            nextHops,
            nextLastIndex,
            nextHops.Length > 0 && nextLastIndex < nextHops.Length - 1,
            AutoCopy,
            nextIsReversed: false
        );
        CaptureCarrierFuel();
        if (HasRoute && AutoCopy)
        {
            AutoCopySelected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyDocument(FollowRouteDocument route, bool requestAutoCopyOwnership = false)
    {
        loadedRoute = route;
        draftNotes = route.Notes;
        ApplyCarrierKind(route.CarrierKind);
        ApplyDraft(
            route.GetNavigationHops(),
            route.LastReachedIndex,
            route.IsActive && !route.IsComplete,
            route.AutoCopy,
            route.RestockOnlyWhenLow,
            route.RepeatMode,
            route.IsReversed
        );
        RaiseRouteMetadataProperties();
        if (requestAutoCopyOwnership && HasRoute && AutoCopy)
        {
            AutoCopySelected?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RefreshCatalogAsync(bool refreshPeer = true)
    {
        if (frontierId is null)
        {
            SavedRoutes = [];
            SelectedSavedRoute = null;
            return;
        }

        int revision = profileRevision;
        IReadOnlyList<FollowRouteCatalogEntry> entries = await routeService.ListAsync(frontierId);
        if (revision != profileRevision)
        {
            return;
        }
        SavedRoutes = entries
            .Select(entry => new SavedRouteItemViewModel(
                entry.Name,
                entry.FileName,
                entry.FilePath,
                entry.IsLegacy,
                entry.LastModified
            ))
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
        if (refreshPeer && SharedCarrierWorkspace is { } peer)
        {
            await peer.RefreshCatalogAsync(refreshPeer: false);
        }
    }

    private void SelectCatalogPath(string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        SelectedSavedRoute = SavedRoutes.FirstOrDefault(route => string.Equals(route.FilePath, path, comparison));
    }

    private void ApplyDraft(
        IReadOnlyList<FollowRouteHop> nextHops,
        int nextLastReachedIndex,
        bool nextIsActive,
        bool nextAutoCopy,
        bool nextRestockOnlyWhenLow = false,
        FollowRouteRepeatMode? nextRepeatMode = null,
        bool? nextIsReversed = null
    )
    {
        if (nextIsReversed is { } direction && direction != isReversed)
        {
            ClearCarrierForecast();
        }
        draftHops = nextHops.ToArray();
        if (draftHops.Length == 0)
        {
            ClearCarrierForecast();
        }
        lastReachedIndex = draftHops.Length == 0 ? -1 : Math.Clamp(nextLastReachedIndex, -1, draftHops.Length - 1);
        isActive = nextIsActive && draftHops.Length > 0 && lastReachedIndex < draftHops.Length - 1;
        autoCopy = nextAutoCopy;
        restockOnlyWhenLow = IsFleetCarrierWorkspace && nextRestockOnlyWhenLow;
        repeatMode = nextRepeatMode ?? repeatMode;
        if (
            repeatMode == FollowRouteRepeatMode.FromStart
            && !FollowRouteProgression.CanRepeatFromStart(routeKind, draftHops)
        )
        {
            repeatMode = FollowRouteRepeatMode.None;
        }
        isReversed = nextIsReversed ?? isReversed;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(AutoCopy));
        OnPropertyChanged(nameof(RestockOnlyWhenLow));
        OnPropertyChanged(nameof(RepeatFromStart));
        OnPropertyChanged(nameof(RepeatAndReverse));
        RefreshPresentation();
    }

    /// <summary>
    /// Computes displayed fuel balances from the unchanged source itinerary and selected behavior.
    /// </summary>
    private void RefreshCarrierFuelPlan()
    {
        presentedHops = draftHops;
        CarrierFuelPlanMessage = string.Empty;
        if (IsFleetCarrierWorkspace && GetCapturedFuelPlan() is { } captured)
        {
            presentedHops = captured.Hops;
            if (!captured.HasRequiredData)
            {
                CarrierFuelPlanMessage = "Fuel estimates require known fuel, cargo capacity, and waypoint coordinates.";
            }
            else if (captured.AdditionalFuelTonnes is > 0)
            {
                CarrierFuelPlanMessage = $"Additional tritium needed: {captured.AdditionalFuelTonnes:N0} t.";
            }
            return;
        }
        if (IsFleetCarrierWorkspace && isReversed && HasRoute)
        {
            presentedHops = draftHops.Select(FleetCarrierFuelPlanner.WithoutForecast).ToArray();
            CarrierFuelPlanMessage = "Fuel forecasts are unavailable while following the route in reverse.";
            return;
        }
        if (!RestockOnlyWhenLow || !HasRoute)
        {
            return;
        }

        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(draftHops);
        presentedHops = plan.Hops;
        if (!plan.HasRequiredData)
        {
            CarrierFuelPlanMessage = "Fuel calculations require starting tank, market, and jump fuel values.";
        }
        else if (plan.UnfundedHopIndex is { } index)
        {
            CarrierFuelPlanMessage = $"Insufficient tritium for the jump to {draftHops[index].Name}.";
        }
    }

    /// <summary>Refreshes guidance and commands using the current working route and displayed fuel plan.</summary>
    private void RefreshPresentation()
    {
        RefreshCarrierFuelPlan();
        destinationMatchesNextHop = IsGalaxyMapOpen && IsNextHop(lastDestination);
        if (!string.Equals(lastCopiedHopName, NextHop?.Name, StringComparison.Ordinal))
        {
            lastCopiedHopName = null;
        }

        RefreshHopRows();
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanRepeatFromStart));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasDefinitionChanges));
        OnPropertyChanged(nameof(HasProgressChanges));
        OnPropertyChanged(nameof(HasNotesChanges));
        OnPropertyChanged(nameof(CanSaveChanges));
        OnPropertyChanged(nameof(RouteCount));
        OnPropertyChanged(nameof(ReachedCount));
        OnPropertyChanged(nameof(RemainingJumps));
        OnPropertyChanged(nameof(HasCarrierEstimatedTimeRemaining));
        OnPropertyChanged(nameof(CarrierEstimatedTimeRemaining));
        OnPropertyChanged(nameof(NextHop));
        OnPropertyChanged(nameof(NextHopName));
        OnPropertyChanged(nameof(HasNextHopSystemAddress));
        OnPropertyChanged(nameof(NextHopSystemAddress));
        OnPropertyChanged(nameof(NextHopSystemAddressText));
        OnPropertyChanged(nameof(ProgressSummary));
        OnPropertyChanged(nameof(AutoCopySummary));
        OnPropertyChanged(nameof(ShouldAutoCopyNextHop));
        OnPropertyChanged(nameof(CurrentBioHop));
        OnPropertyChanged(nameof(CurrentBioTargets));
        OnPropertyChanged(nameof(HasCurrentBioTargets));
        OnPropertyChanged(nameof(ShouldShowRouteBioOverlay));
        OnPropertyChanged(nameof(CurrentBioSystemName));
        OnPropertyChanged(nameof(ShouldShowFleetCarrierRouteOverlay));
        RefreshCarrierDestination();
        RaiseOverlayProperties();
        RaiseCommands();
    }

    /// <summary>Rebuilds route rows from route progress, visits and current catalogue annotations.</summary>
    private void RefreshHopRows()
    {
        bool canReuseRows = hops.Count == draftHops.Length;
        if (canReuseRows)
        {
            for (int index = 0; index < draftHops.Length; index++)
            {
                if (hops[index].Index != index || !hops[index].MatchesIdentity(draftHops[index]))
                {
                    canReuseRows = false;
                    break;
                }
            }
        }

        List<RouteHopItemViewModel>? rows = canReuseRows ? null : new List<RouteHopItemViewModel>(draftHops.Length);
        for (int index = 0; index < draftHops.Length; index++)
        {
            UpdateOrCreateHopRow(index, canReuseRows, rows);
        }

        if (rows is not null)
        {
            Hops = rows;
        }
        ApplyPoiCatalog(poiCatalog);
    }

    private void UpdateOrCreateHopRow(int index, bool canReuseRows, List<RouteHopItemViewModel>? rows)
    {
        FollowRouteHop hop = presentedHops[index];
        GalacticCoordinate? from;
        if (index == 0)
        {
            from = lastReachedIndex < 0 ? currentPosition : hop.Position;
        }
        else
        {
            from = draftHops[index - 1].Position;
        }
        double? distance = from is { } start && hop.Position is { } end ? start.DistanceTo(end) : (double?)null;
        bool isCurrent = IsFleetCarrierWorkspace ? index == lastReachedIndex : IsCurrentSystem(hop);
        bool isNext = IsActive && index == lastReachedIndex + 1;
        string distanceText = distance is null ? "?" : $"{distance:N2} ly";
        string notes = CreateNotes(hop);
        if (canReuseRows)
        {
            hops[index]
                .UpdatePresentation(
                    hop,
                    distanceText,
                    notes,
                    draftHops.Length - index - 1,
                    index <= lastReachedIndex,
                    isCurrent,
                    isNext
                );
            return;
        }

        rows!.Add(
            new RouteHopItemViewModel(
                index,
                index + 1,
                hop,
                new RouteHopItemPresentation(
                    distanceText,
                    notes,
                    draftHops.Length - index - 1,
                    IsFleetCarrierWorkspace,
                    index <= lastReachedIndex,
                    isCurrent,
                    isNext
                )
            )
        );
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
        IsFuelWarningVisible = false;
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

        return (destination.System > 0 && nextHop.SystemAddress == destination.System)
            || (
                !string.IsNullOrWhiteSpace(destination.Name)
                && string.Equals(nextHop.Name, destination.Name, StringComparison.OrdinalIgnoreCase)
            );
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
        FleetCarrierJumpCountdownState next = carrierJumpCountdown.Current;
        if (next == carrierJumpCountdownState)
        {
            return;
        }

        bool contextChanged =
            next.Kind != carrierJumpCountdownState.Kind || next.Destination != carrierJumpCountdownState.Destination;
        carrierJumpCountdownState = next;
        OnPropertyChanged(nameof(HasCarrierJumpCountdown));
        OnPropertyChanged(nameof(CarrierJumpCountdownTitle));
        OnPropertyChanged(nameof(CarrierJumpCountdownValue));
        OnPropertyChanged(nameof(CarrierJumpPhaseLabel));
        OnPropertyChanged(nameof(CarrierJumpPhaseCountdown));
        OnPropertyChanged(nameof(HasCarrierJumpPhaseCountdown));
        if (contextChanged)
        {
            OnPropertyChanged(nameof(CarrierJumpDestination));
            OnPropertyChanged(nameof(CarrierJumpKind));
            OnPropertyChanged(nameof(ShouldShowCarrierNextJumpOverlay));
            RefreshCarrierDestination();
        }
    }

    private bool IsCurrentSystem(FollowRouteHop hop)
    {
        return (currentSystemAddress is not null && hop.SystemAddress == currentSystemAddress)
            || (
                !string.IsNullOrWhiteSpace(currentSystemName)
                && string.Equals(hop.Name, currentSystemName, StringComparison.OrdinalIgnoreCase)
            );
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

    private void ReplaceDraftBioTarget(int hopIndex, int targetIndex, FollowRouteBioTarget target)
    {
        FollowRouteBioTarget[] bioTargets = draftHops[hopIndex].BioTargets.ToArray();
        bioTargets[targetIndex] = target;
        FollowRouteHop[] updatedHops = draftHops.ToArray();
        updatedHops[hopIndex] = updatedHops[hopIndex] with { Bio = bioTargets };
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
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long result)
            ? result
            : null;
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception
            is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or HttpRequestException
                or TimeoutException;
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

    /// <summary>
    /// Updates a dialog field and refreshes dependent visibility and command state.
    /// </summary>
    private void SetDialogField(ref bool field, bool value, string propertyName)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        OnPropertyChanged(nameof(IsDialogVisible));
        OnPropertyChanged(nameof(CanSearchSpansh));
        RaiseCommands();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaiseCommands()
    {
        OnPropertyChanged(nameof(CanEditWaypoints));
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

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
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
    DateTimeOffset LastModified
)
{
    public string DisplayName => Name;
}

public sealed record RouteHopItemPresentation(
    string Distance,
    string Notes,
    int JumpsRemaining,
    bool IsFleetCarrierHop,
    bool IsReached,
    bool IsCurrent,
    bool IsNext
);

public sealed class RouteHopItemViewModel : INotifyPropertyChanged
{
    public string PoiText { get; private set; } = string.Empty;
    public bool HasPois => PoiText.Length > 0;

    /// <summary>Adds the matching attractions to one existing route-hop presentation.</summary>
    public void ApplyPoiSummary(IReadOnlyList<SrvSurvey.Core.Exploration.ExplorationPoi> pois)
    {
        PoiText = string.Join(" · ", pois.Select(p => p.Name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PoiText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPois)));
    }

    private FollowRouteHop hop;
    private string distance;
    private string notes;
    private int jumpsRemaining;
    private bool isReached;
    private bool isCurrent;
    private bool isNext;

    public RouteHopItemViewModel(int index, int number, FollowRouteHop hop, RouteHopItemPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(hop);
        ArgumentNullException.ThrowIfNull(presentation);
        Index = index;
        Number = number;
        this.hop = hop;
        distance = presentation.Distance;
        notes = presentation.Notes;
        jumpsRemaining = presentation.JumpsRemaining;
        IsFleetCarrierHop = presentation.IsFleetCarrierHop;
        isReached = presentation.IsReached;
        isCurrent = presentation.IsCurrent;
        isNext = presentation.IsNext;
        BioTargets = CreateBioTargets(hop);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public int Number { get; }

    public FollowRouteHop Hop => hop;

    public string Name => Hop.Name;

    public bool HasSystemAddress => Hop.SystemAddress is > 0;

    public string SystemAddressText => SystemAddressFormatter.Format(Hop.SystemAddress);

    public string Distance => distance;

    public string CarrierDistance
    {
        get
        {
            if (Hop.Carrier?.DistanceLy is { } value)
            {
                return $"{value:N2}";
            }

            return Distance == "?" ? "\u2014" : Distance.Replace(" ly", string.Empty, StringComparison.Ordinal);
        }
    }

    public string CarrierRemaining => FormatCarrierNumber(Hop.Carrier?.RemainingLy, 2);

    public int JumpsRemaining => jumpsRemaining;

    public bool IsFleetCarrierHop { get; }

    public bool IsStandardHop => !IsFleetCarrierHop;

    public string CarrierFuelRemaining => FormatCarrierNumber(Hop.Carrier?.FuelRemainingTonnes, 0);

    public string CarrierTritiumInMarket => FormatCarrierNumber(Hop.Carrier?.TritiumInMarketTonnes, 0);

    public string CarrierFuelUsed => FormatCarrierNumber(Hop.Carrier?.FuelUsedTonnes, 0);

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

    public string CarrierRestock => Hop.Carrier?.MustRestock == true ? "YES" : string.Empty;

    public string CarrierRestockAmount =>
        Hop.Carrier is { MustRestock: true, RestockAmountTonnes: > 0 } carrier
            ? FormatCarrierNumber(carrier.RestockAmountTonnes, 0)
            : string.Empty;

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

    public string State =>
        IsCurrent
            ? "CURRENT"
            : (IsNext) switch
            {
                true => "NEXT",
                false => (IsReached) switch
                {
                    true => "VISITED",
                    false => string.Empty,
                },
            };

    public bool HasState => State.Length > 0;

    public bool MatchesIdentity(FollowRouteHop candidate)
    {
        return Hop.SystemAddress is { } address && candidate.SystemAddress is { } candidateAddress
            ? address == candidateAddress
            : string.Equals(Hop.Name, candidate.Name, StringComparison.OrdinalIgnoreCase);
    }

    public void UpdatePresentation(
        FollowRouteHop nextHop,
        string nextDistance,
        string nextNotes,
        int nextJumpsRemaining,
        bool nextIsReached,
        bool nextIsCurrent,
        bool nextIsNext
    )
    {
        bool hopChanged = !Equals(hop, nextHop);
        hop = nextHop;
        if (hopChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hop)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSystemAddress)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SystemAddressText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNotes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RouteNotes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Refuel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Neutron)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasGuidance)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierDistance)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierRemaining)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierFuelRemaining)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierTritiumInMarket)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierFuelUsed)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierIcyRing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierRestock)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierRestockAmount)));
            RefreshBioTargets(nextHop);
        }

        bool distanceChanged = !string.Equals(distance, nextDistance, StringComparison.Ordinal);
        SetField(ref distance, nextDistance, nameof(Distance));
        if (distanceChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarrierDistance)));
        }
        SetField(ref notes, nextNotes, nameof(Notes));
        SetField(ref jumpsRemaining, nextJumpsRemaining, nameof(JumpsRemaining));
        bool stateChanged = isReached != nextIsReached || isCurrent != nextIsCurrent || isNext != nextIsNext;
        SetField(ref isReached, nextIsReached, nameof(IsReached));
        SetField(ref isCurrent, nextIsCurrent, nameof(IsCurrent));
        SetField(ref isNext, nextIsNext, nameof(IsNext));
        if (stateChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasState)));
        }
    }

    private RouteBioTargetItemViewModel[] CreateBioTargets(FollowRouteHop source)
    {
        return source
            .BioTargets.Select((target, index) => new RouteBioTargetItemViewModel(Index, index, target))
            .ToArray();
    }

    private void RefreshBioTargets(FollowRouteHop source)
    {
        bool canReuse = BioTargets.Count == source.BioTargets.Count;
        if (canReuse)
        {
            for (int index = 0; index < BioTargets.Count; index++)
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
            for (int index = 0; index < BioTargets.Count; index++)
            {
                BioTargets[index].Update(source.BioTargets[index]);
            }
        }
        else
        {
            BioTargets = CreateBioTargets(source);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BioTargets)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasBioTargets)));
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
        return value is { } number ? number.ToString($"N{decimals}", CultureInfo.CurrentCulture) : "—";
    }
}

public sealed class RouteBioTargetItemViewModel : INotifyPropertyChanged
{
    private FollowRouteBioTarget target;
    private RouteBodyVisual bodyVisual;
    private IReadOnlyList<RouteBioDetailSegmentViewModel> compactDetailSegments;
    private IReadOnlyList<RouteBioDetailSegmentViewModel> inlineSegments;

    public RouteBioTargetItemViewModel(int hopIndex, int targetIndex, FollowRouteBioTarget target)
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

    public string DistanceToArrival =>
        target.DistanceToArrivalLs is { } distance
            ? (distance < 100) switch
            {
                true => $"{distance:N2} LS",
                false => $"{distance:N0} LS",
            }
            : string.Empty;

    public bool HasDistanceToArrival => target.DistanceToArrivalLs is not null;

    public string EstimatedScanValue => FormatCredits(target.EstimatedScanValue);

    public bool HasEstimatedScanValue => target.EstimatedScanValue is not null;

    public string EstimatedMappingValue => FormatCredits(target.EstimatedMappingValue);

    public bool HasEstimatedMappingValue => target.EstimatedMappingValue is not null;

    public string EstimatedBiologyValue => FormatCredits(target.EstimatedBiologyValue);

    public bool HasEstimatedBiologyValue => target.EstimatedBiologyValue is not null;

    public bool IsTerraformable => target.IsTerraformable;

    public bool HasDetails =>
        HasSubtype
        || HasDistanceToArrival
        || HasEstimatedScanValue
        || HasEstimatedMappingValue
        || HasEstimatedBiologyValue
        || IsTerraformable;

    public IReadOnlyList<RouteBioDetailSegmentViewModel> CompactDetailSegments => compactDetailSegments;

    public IReadOnlyList<RouteBioDetailSegmentViewModel> InlineSegments => inlineSegments;

    public string CompactDetails => string.Join(" | ", compactDetailSegments.Select(segment => segment.Text));

    public bool IsCompleted => target.IsCompleted;

    public string CompletionLabel => IsCompleted ? "COMPLETE" : "TO VISIT";

    public bool Matches(FollowRouteBioTarget candidate)
    {
        return BodyId is { } bodyId && candidate.BodyId is { } candidateId
            ? bodyId == candidateId
            : string.Equals(BodyName, candidate.BodyName, StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesBody(long? bodyId, string? bodyName, string systemName)
    {
        if (BodyId is { } targetBodyId && bodyId is { } eventBodyId)
        {
            return targetBodyId == eventBodyId;
        }

        if (string.IsNullOrWhiteSpace(bodyName))
        {
            return false;
        }

        string normalizedEventName = bodyName.Trim();
        if (
            !string.IsNullOrWhiteSpace(systemName)
            && normalizedEventName.StartsWith(systemName + " ", StringComparison.OrdinalIgnoreCase)
        )
        {
            normalizedEventName = normalizedEventName[(systemName.Length + 1)..];
        }

        return string.Equals(BodyName.Trim(), normalizedEventName, StringComparison.OrdinalIgnoreCase)
            || bodyName.EndsWith(" " + BodyName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void Update(FollowRouteBioTarget next)
    {
        if (Equals(target, next))
        {
            return;
        }

        bool identityChanged = !Matches(next);
        bool speciesChanged = !target.Species.SequenceEqual(next.Species, StringComparer.Ordinal);
        bool completionChanged = target.IsCompleted != next.IsCompleted;
        bool subtypeChanged = !string.Equals(target.Subtype, next.Subtype, StringComparison.Ordinal);
        bool distanceChanged = !EquivalentDistance(target.DistanceToArrivalLs, next.DistanceToArrivalLs);
        bool scanValueChanged = target.EstimatedScanValue != next.EstimatedScanValue;
        bool mappingValueChanged = target.EstimatedMappingValue != next.EstimatedMappingValue;
        bool biologyValueChanged = target.EstimatedBiologyValue != next.EstimatedBiologyValue;
        bool terraformableChanged = target.IsTerraformable != next.IsTerraformable;
        bool biologicalChanged = target.IsBiological != next.IsBiological;
        RouteBodyVisual nextBodyVisual = subtypeChanged ? RouteBodyAssetResolver.Resolve(next.Subtype) : bodyVisual;
        bool bodyVisualChanged = bodyVisual != nextBodyVisual;
        target = next;
        bodyVisual = nextBodyVisual;
        RaiseChanges(identityChanged, nameof(BodyName), nameof(BodyId));
        RaiseChanges(speciesChanged, nameof(Species), nameof(HasSpecies), nameof(NeedsScan));
        RaiseChanges(subtypeChanged, nameof(Subtype), nameof(HasSubtype));
        RaiseChanges(bodyVisualChanged, nameof(BodyIconAssetPath), nameof(BodyIconAccessibleName));
        RaiseChanges(distanceChanged, nameof(DistanceToArrival), nameof(HasDistanceToArrival));
        RaiseChanges(scanValueChanged, nameof(EstimatedScanValue), nameof(HasEstimatedScanValue));
        RaiseChanges(mappingValueChanged, nameof(EstimatedMappingValue), nameof(HasEstimatedMappingValue));
        RaiseChanges(biologyValueChanged, nameof(EstimatedBiologyValue), nameof(HasEstimatedBiologyValue));
        RaiseChanges(terraformableChanged, nameof(IsTerraformable));
        RaiseChanges(biologicalChanged, nameof(NeedsScan));

        bool compactDetailsChanged =
            subtypeChanged
            || distanceChanged
            || scanValueChanged
            || mappingValueChanged
            || biologyValueChanged
            || terraformableChanged;
        RefreshCompactDetails(compactDetailsChanged);
        RefreshInlineDetails(identityChanged || compactDetailsChanged);
        RaiseChanges(completionChanged, nameof(IsCompleted), nameof(CompletionLabel));
    }

    private void RaiseChanges(
        bool changed,
        string propertyName,
        string? secondPropertyName = null,
        string? thirdPropertyName = null
    )
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        return !left.HasValue || Math.Abs(left.Value - right!.Value) <= 0.0000001d;
    }

    private static string FormatCompactCredits(long? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return Math.Abs(value.Value) >= 1_000_000 ? $"{value.Value / 1_000_000d:0.#} M CR" : $"{value.Value:N0} CR";
    }

    private RouteBioDetailSegmentViewModel[] BuildCompactDetailSegments()
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
            .Select((text, index) => new RouteBioDetailSegmentViewModel(text, index < details.Count - 1))
            .ToArray();
    }

    private IReadOnlyList<RouteBioDetailSegmentViewModel> BuildInlineSegments()
    {
        return
        [
            new RouteBioDetailSegmentViewModel(BodyName, HasSeparator: false, IsBodyName: true),
            .. compactDetailSegments,
        ];
    }
}

public sealed record RouteBioDetailSegmentViewModel(string Text, bool HasSeparator, bool IsBodyName = false)
{
    public bool IsDetail => !IsBodyName;
}
