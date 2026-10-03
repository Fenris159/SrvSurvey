using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan DockingRefreshDelay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ProjectLocationCacheTtl = TimeSpan.FromSeconds(4);
    private const int MaximumBuildSiteRepairVisits = 50;
    private const string FleetCarrierStationType = "FleetCarrier";
    private const string FleetCarrierSyncOffMessage = "Automatic Fleet Carrier cargo sync is off.";

    private readonly IRavenColonialClient client;
    private readonly ColonizationBuildCatalog buildCatalog;
    private readonly ColonizationSettingsStore settingsStore;
    private readonly CommanderProfileStore? commanderProfileStore;
    private readonly LegacyColonizationProfileStore? legacyProfileStore;
    private ColonizationOverlayPreferences overlayPreferences;
    private readonly ColonizationConstructionState constructionState = new();
    private readonly ColonizationFleetCarrierIdentityTracker fleetCarrierIdentityTracker = new();
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand saveProjectsCommand;
    private readonly AsyncCommand saveRavenApiKeyCommand;
    private readonly AsyncCommand publishFleetCarrierCommand;
    private readonly AsyncCommand syncFleetCarrierCargoCommand;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Lock buildSiteRepairLock = new();
    private readonly Queue<ColonizationBuildSiteRepairVisit> buildSiteRepairVisits = new();
    private readonly HashSet<ColonizationBuildSiteRepairVisit> buildSiteRepairVisitSet = [];
    private readonly HashSet<(long SystemAddress, long MarketId, string StationKey)> buildSiteRepairsInFlight = [];
    private CancellationTokenSource? dockingRefreshCancellation;
    private IReadOnlyList<ColonizationProjectRowViewModel> projects = [];
    private IReadOnlyList<ColonizationResourceRowViewModel> constructionResources = [];
    private HashSet<string> hiddenProjectIds = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ColonizationFleetCarrier> fleetCarriers = [];
    private long? detectedSquadronCarrierMarketId;
    private string? detectedSquadronCommander;
    public IReadOnlyList<ColonizationFleetCarrier> LinkedFleetCarriers => fleetCarriers;
    public long? DetectedSquadronCarrierMarketId => detectedSquadronCarrierMarketId;
    private ColonizationProject? localUntrackedProject;
    private CargoSnapshot? shipCargo;
    private MarketSnapshot? currentMarket;
    private EliteStatus? latestStatus;
    private string? commanderName;
    private string? currentSystemName;
    private int? currentBodyId;
    private string? currentBodyName;
    private string? currentBodyCommanderName;
    private long? currentSystemAddress;
    private IReadOnlyList<double> currentStarPosition = [];
    private string? primaryProjectId;
    private bool isEnabled;
    private bool isBusy;
    private bool hasUnsavedProjectVisibility;
    private bool fleetCarrierCargoSyncEnabled;
    private bool shipCargoPublishingEnabled;
    private bool sharedCargoSuppressed;
    private bool isFleetCarrierSyncBusy;
    private bool isShipCargoPublishingBusy;
    private string ravenApiKey = string.Empty;
    private string? storedRavenApiKey;
    private string? profileFrontierId;
    private bool profileIsOdyssey = true;
    private (long MarketId, DateTimeOffset Timestamp)? lastSyncedMarket;

    /// <summary>
    /// Market IDs that already received a once-per-session CAPI full-manifest seed.
    /// Further Frontier refreshes are ignored for Raven cargo; journals keep totals live.
    /// </summary>
    private readonly HashSet<long> capiCargoSeededMarketIds = [];

    private int capiCargoSeedGeneration;

    /// <summary>
    /// EDMC-style dock/Market.json baseline: journal cargo deltas queue here until the
    /// async baseline finishes so MarketBuy/Sell/Transfer cannot race a replace.
    /// Nested CAPI/market/dock baselines share a depth count so an inner complete
    /// cannot replay deltas while an outer baseline is still in flight.
    /// </summary>
    private readonly Dictionary<long, int> cargoBaselinePendingDepth = [];

    private readonly HashSet<long> cargoBaselineReady = [];

    private readonly Dictionary<long, (Dictionary<string, int> Delta, DateTimeOffset? RecordedAt)> pendingCargoDeltas =
    [];

    /// <summary>
    /// When true, the next squadron cargo GetDiff is skipped because MarketBuy/Sell
    /// already sent AdjustFleetCarrierCargo for this linked squadron FC (legacy parity).
    /// </summary>
    private bool skipNextCargoEvent;
    private bool pendingContributionRemainingSync;
    private string? lastDepotPatchPayloadSignature;
    private (long SystemAddress, long MarketId, ColonizationProject Project, long MonotonicTicks)? projectLocationCache;
    private string ravenCredentialStatus = "Load a commander profile to configure a Raven API key.";
    private string fleetCarrierSyncStatus = FleetCarrierSyncOffMessage;
    private string shipCargoPublishingStatus = "Automatic ship cargo publishing is off.";
    private string? currentShipType;
    private string? currentShipName;
    private string statusMessage;
    private (long SystemAddress, string Message)? buildSiteRepairWarning;
    private int buildSiteRepairContextVersion;
    private int profileVersion;
    private bool refreshPending;
    private readonly Dictionary<JournalEventEnvelope, ColonizationDockingSnapshot?> journalDocks = new(
        ReferenceEqualityComparer.Instance
    );
    private readonly List<ColonizationPendingCargoAdjustment> failedCargoAdjustments = [];
    private readonly List<ColonizationPendingContribution> pendingContributions = [];
    private IReadOnlyList<ColonizationPendingContributionRowViewModel> unconfirmedContributions = [];
    private string? contributionRecoveryOwner;
    private DateTimeOffset nextWriteRetry;
    private bool retryingWrites;
    private readonly HashSet<(string Owner, long MarketId)> cargoWritesInFlight = [];
    private readonly HashSet<string> contributionsInFlight = [];

    private string projectSummary = "No projects loaded.";
    private string constructionTitle = "No construction depot active";
    private string constructionStatus = "Dock at a construction site and open Construction Services.";

    /// <summary>Initializes commander-scoped colonization workflows, delivery recovery, and bounded retry timing.</summary>
    public ColonizationViewModel(
        ColonizationSettingsStore settingsStore,
        IRavenColonialClient? client = null,
        ColonizationBuildCatalog? buildCatalog = null,
        CommanderProfileStore? commanderProfileStore = null,
        LegacyColonizationProfileStore? legacyProfileStore = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<DateTimeOffset>? utcNow = null
    )
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.client = client ?? new RavenColonialClient();
        this.commanderProfileStore = commanderProfileStore;
        this.legacyProfileStore = legacyProfileStore;
        this.delayAsync = delayAsync ?? Task.Delay;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        pendingContributions.AddRange(settingsStore.LoadPendingContributions());
        failedCargoAdjustments.AddRange(settingsStore.LoadPendingCargoAdjustments());
        this.buildCatalog = buildCatalog ?? ColonizationBuildCatalog.LoadEmbedded();
        overlayPreferences = settingsStore.LoadOverlayPreferences();
        fleetCarrierCargoSyncEnabled = settingsStore.LoadFleetCarrierCargoSyncEnabled();
        shipCargoPublishingEnabled = settingsStore.LoadShipCargoPublishingEnabled();
        isEnabled = settingsStore.LoadEnabled();
        foreach (ColonizationBuildSiteRepairVisit visit in settingsStore.LoadBuildSiteRepairVisits())
        {
            buildSiteRepairVisits.Enqueue(visit);
            buildSiteRepairVisitSet.Add(visit);
        }
        statusMessage = isEnabled
            ? "Raven Colonial access is enabled. Waiting for a commander profile."
            : "Raven Colonial access is off. No project data will be fetched or published.";
        refreshCommand = new AsyncCommand(RefreshAsync, () => IsEnabled && !IsBusy && CommanderName is not null);
        saveProjectsCommand = new AsyncCommand(
            SaveProjectVisibilityAsync,
            () => IsEnabled && !IsBusy && HasUnsavedProjectVisibility && CommanderName is not null
        );
        saveRavenApiKeyCommand = new AsyncCommand(SaveRavenApiKeyAsync, CanSaveRavenApiKey);
        publishFleetCarrierCommand = new AsyncCommand(PublishCurrentFleetCarrierAsync, CanPublishCurrentFleetCarrier);
        syncFleetCarrierCargoCommand = new AsyncCommand(
            () => SyncFleetCarrierCargoAsync(force: true),
            CanSyncFleetCarrierCargo
        );
        RetryUnconfirmedContributionsCommand = new AsyncCommand(
            RetryUnconfirmedContributionsAsync,
            () => IsEnabled && CanReconcileSelectedContributions()
        );
        DismissConfirmedContributionsCommand = new AsyncCommand(
            () =>
            {
                DismissConfirmedContributions();
                return Task.CompletedTask;
            },
            CanReconcileSelectedContributions
        );
        RefreshCommand = refreshCommand;
        SaveProjectsCommand = saveProjectsCommand;
        SaveRavenApiKeyCommand = saveRavenApiKeyCommand;
        PublishFleetCarrierCommand = publishFleetCarrierCommand;
        SyncFleetCarrierCargoCommand = syncFleetCarrierCargoCommand;
        ProjectEditor = new ColonizationProjectEditorViewModel(this.client, this.buildCatalog, OnProjectCreatedAsync);
        SystemEditor = new ColonizationSystemEditorViewModel(this.client, this.buildCatalog);
        CommodityOverlay = new ColonizationCommodityOverlayViewModel();
        CommodityOverlay.ApplyPreferences(overlayPreferences);
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
        UpdateCommodityPlan();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand SaveProjectsCommand { get; }

    public ICommand SaveRavenApiKeyCommand { get; }

    public ICommand PublishFleetCarrierCommand { get; }

    public ICommand SyncFleetCarrierCargoCommand { get; }

    public ColonizationProjectEditorViewModel ProjectEditor { get; }

    public ColonizationSystemEditorViewModel SystemEditor { get; }

    public ColonizationCommodityOverlayViewModel CommodityOverlay { get; }

    public bool AutoShowCommodityOverlay
    {
        get => overlayPreferences.AutoShow;
        set => SaveOverlayPreferences(overlayPreferences with { AutoShow = value });
    }

    public bool ShowCommodityOverlayOnRightPanel
    {
        get => overlayPreferences.ShowOnRightPanel;
        set => SaveOverlayPreferences(overlayPreferences with { ShowOnRightPanel = value });
    }

    public bool ShowFleetCarrierCargo
    {
        get => overlayPreferences.ShowFleetCarrierCargo;
        set => SaveOverlayPreferences(overlayPreferences with { ShowFleetCarrierCargo = value });
    }

    public bool ShowFleetCarrierDelta
    {
        get => overlayPreferences.ShowFleetCarrierDelta;
        set => SaveOverlayPreferences(overlayPreferences with { ShowFleetCarrierDelta = value });
    }

    public bool InlineFleetCarrierCargo
    {
        get => overlayPreferences.InlineFleetCarrierCargo;
        set => SaveOverlayPreferences(overlayPreferences with { InlineFleetCarrierCargo = value });
    }

    public bool CollapseCoveredCommodityGroups
    {
        get => overlayPreferences.CollapseCoveredGroups;
        set => SaveOverlayPreferences(overlayPreferences with { CollapseCoveredGroups = value });
    }

    public bool UseCompactScrollingCommoditiesList
    {
        get => overlayPreferences.UseCompactScrollingCommoditiesList;
        set => SaveOverlayPreferences(overlayPreferences with { UseCompactScrollingCommoditiesList = value });
    }

    public bool HighlightAlmostCoveredFleetCarrierLoads
    {
        get => overlayPreferences.HighlightAlmostCoveredFleetCarrierLoads;
        set => SaveOverlayPreferences(overlayPreferences with { HighlightAlmostCoveredFleetCarrierLoads = value });
    }

    public string RavenApiKey
    {
        get => ravenApiKey;
        set
        {
            if (SetField(ref ravenApiKey, value ?? string.Empty))
            {
                saveRavenApiKeyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasCommanderProfile => profileFrontierId is not null;

    public bool HasStoredRavenApiKey => !string.IsNullOrWhiteSpace(storedRavenApiKey);

    public string RavenCredentialStatus
    {
        get => ravenCredentialStatus;
        private set => SetField(ref ravenCredentialStatus, value);
    }

    public bool FleetCarrierCargoSyncEnabled
    {
        get => fleetCarrierCargoSyncEnabled;
        set
        {
            if (value == fleetCarrierCargoSyncEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveFleetCarrierCargoSyncEnabled(value);
                fleetCarrierCargoSyncEnabled = value;
                OnPropertyChanged();
                FleetCarrierSyncStatus = value
                    ? (HasStoredRavenApiKey) switch
                    {
                        true => "Fleet Carrier cargo will sync from matching Market.json updates.",
                        false => "Save a Raven API key before Fleet Carrier cargo can sync.",
                    }
                    : FleetCarrierSyncOffMessage;
                syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                FleetCarrierSyncStatus = "The Fleet Carrier sync preference could not be saved: " + exception.Message;
            }
        }
    }

    public bool IsFleetCarrierSyncBusy
    {
        get => isFleetCarrierSyncBusy;
        private set
        {
            if (SetField(ref isFleetCarrierSyncBusy, value))
            {
                OnPropertyChanged(nameof(FleetCarrierSyncButtonText));
                OnPropertyChanged(nameof(FleetCarrierPublishButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string FleetCarrierSyncButtonText => IsFleetCarrierSyncBusy ? "Syncing..." : "Sync current market";

    public string FleetCarrierPublishButtonText =>
        IsFleetCarrierSyncBusy ? "Working..." : "Publish/link current carrier";

    public string FleetCarrierSyncStatus
    {
        get => fleetCarrierSyncStatus;
        private set => SetField(ref fleetCarrierSyncStatus, value);
    }

    public bool ShipCargoPublishingEnabled
    {
        get => shipCargoPublishingEnabled;
        set
        {
            if (value == shipCargoPublishingEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveShipCargoPublishingEnabled(value);
                shipCargoPublishingEnabled = value;
                OnPropertyChanged();
                ShipCargoPublishingStatus = GetShipCargoReadyStatus();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ShipCargoPublishingStatus =
                    "The ship cargo publishing preference could not be saved: " + exception.Message;
            }
        }
    }

    public bool IsShipCargoPublishingBusy
    {
        get => isShipCargoPublishingBusy;
        private set => SetField(ref isShipCargoPublishingBusy, value);
    }

    public string ShipCargoPublishingStatus
    {
        get => shipCargoPublishingStatus;
        private set => SetField(ref shipCargoPublishingStatus, value);
    }

    public bool SharedCargoSuppressed => sharedCargoSuppressed;

    public void SetSharedCargoSuppressed(bool value)
    {
        if (sharedCargoSuppressed == value)
        {
            return;
        }

        sharedCargoSuppressed = value;
        OnPropertyChanged(nameof(SharedCargoSuppressed));
        if (value)
        {
            shipCargo = null;
            UpdateCommodityPlan();
        }

        ShipCargoPublishingStatus = GetShipCargoReadyStatus();
    }

    /// <summary>
    /// Enables Raven integration and invalidates pending repair warnings when it is disabled.
    /// </summary>
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (value == isEnabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveEnabled(value);
                isEnabled = value;
                OnPropertyChanged();
                RaiseCommandStates();
                if (value)
                {
                    StatusMessage = CommanderName is null
                        ? "Raven Colonial access is enabled. Waiting for a commander profile."
                        : "Raven Colonial access is enabled. Select Refresh projects to fetch data.";
                }
                else
                {
                    CancelDockingRefresh();
                    buildSiteRepairContextVersion++;
                    SetBuildSiteRepairWarning(null);
                    ClearProjects();
                    StatusMessage = "Raven Colonial access is off. No project data will be fetched or published.";
                }

                if (ShipCargoPublishingEnabled)
                {
                    ShipCargoPublishingStatus = GetShipCargoReadyStatus();
                }

                UpdateProjectEditorContext();
                UpdateSystemEditorContext();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusMessage = "The Raven Colonial preference could not be saved: " + exception.Message;
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                OnPropertyChanged(nameof(RefreshButtonText));
                OnPropertyChanged(nameof(SaveButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string RefreshButtonText => IsBusy ? "Refreshing..." : "Refresh projects";

    public string SaveButtonText => IsBusy ? "Saving..." : "Save selection";

    public string? CommanderName
    {
        get => commanderName;
        private set
        {
            if (SetField(ref commanderName, value))
            {
                OnPropertyChanged(nameof(CommanderStatus));
                RaiseCommandStates();
            }
        }
    }

    public string CommanderStatus =>
        CommanderName is null ? "No commander profile is active." : $"Commander: {CommanderName}";

    public IReadOnlyList<ColonizationProjectRowViewModel> Projects
    {
        get => projects;
        private set
        {
            if (ReferenceEquals(projects, value))
            {
                return;
            }

            projects = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(HasNoProjects));
        }
    }

    public bool HasProjects => Projects.Count > 0;

    public bool HasNoProjects => !HasProjects;

    public bool HasUnsavedProjectVisibility
    {
        get => hasUnsavedProjectVisibility;
        private set
        {
            if (SetField(ref hasUnsavedProjectVisibility, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ProjectSummary
    {
        get => projectSummary;
        private set => SetField(ref projectSummary, value);
    }

    /// <summary>
    /// Combines general Raven status with the current system's independent docking-repair warning.
    /// </summary>
    public string StatusMessage
    {
        get => CombineMessages(statusMessage, buildSiteRepairWarning?.Message) ?? string.Empty;
        private set => SetField(ref statusMessage, value);
    }

    public string ConstructionTitle
    {
        get => constructionTitle;
        private set => SetField(ref constructionTitle, value);
    }

    public string ConstructionStatus
    {
        get => constructionStatus;
        private set => SetField(ref constructionStatus, value);
    }

    public IReadOnlyList<ColonizationResourceRowViewModel> ConstructionResources
    {
        get => constructionResources;
        private set
        {
            if (ReferenceEquals(constructionResources, value))
            {
                return;
            }

            constructionResources = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConstructionResources));
        }
    }

    public bool HasConstructionResources => ConstructionResources.Count > 0;

    /// <summary>Updates credentials and invalidates requests owned by a superseded Frontier profile.</summary>
    public void SetCommanderProfile(string? frontierId, bool isOdyssey, string? apiKey)
    {
        if (
            profileFrontierId != frontierId?.Trim()
            || profileIsOdyssey != isOdyssey
            || storedRavenApiKey != apiKey?.Trim()
        )
        {
            profileVersion++;
        }
        profileFrontierId = string.IsNullOrWhiteSpace(frontierId) ? null : frontierId.Trim();
        profileIsOdyssey = isOdyssey;
        storedRavenApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        RavenApiKey = storedRavenApiKey ?? string.Empty;
        NotifyContributionRecovery();
        lastSyncedMarket = null;
        string apiKeyStatus = storedRavenApiKey is null
            ? "No Raven API key is saved for this commander."
            : "A Raven API key is saved for this commander.";
        RavenCredentialStatus = profileFrontierId is null
            ? "Load a commander profile to configure a Raven API key."
            : apiKeyStatus;
        if (!FleetCarrierCargoSyncEnabled)
        {
            FleetCarrierSyncStatus = FleetCarrierSyncOffMessage;
        }
        else if (storedRavenApiKey is null)
        {
            FleetCarrierSyncStatus = "Save a Raven API key before Fleet Carrier cargo can sync.";
        }
        else
        {
            FleetCarrierSyncStatus = "Fleet Carrier cargo will sync from matching Market.json updates.";
        }
        ShipCargoPublishingStatus = GetShipCargoReadyStatus();
        OnPropertyChanged(nameof(HasCommanderProfile));
        OnPropertyChanged(nameof(HasStoredRavenApiKey));
        RaiseCommandStates();
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
    }

    /// <summary>
    /// Switches commander data and discards repair warnings and pending results from the previous commander.
    /// </summary>
    public async Task SetCommanderAsync(string? value)
    {
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.Equals(CommanderName, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CancelDockingRefresh();
        profileVersion++;
        buildSiteRepairContextVersion++;
        SetBuildSiteRepairWarning(null);
        if (!string.Equals(detectedSquadronCommander, normalized, StringComparison.OrdinalIgnoreCase))
        {
            detectedSquadronCarrierMarketId = null;
        }
        CommanderName = normalized;
        NotifyContributionRecovery();
        if (!string.Equals(currentBodyCommanderName, normalized, StringComparison.OrdinalIgnoreCase))
        {
            currentBodyId = null;
            currentBodyName = null;
            currentBodyCommanderName = null;
        }
        ClearCapiCargoSeedSession();
        ClearAllCargoBaselines();
        ClearProjects();
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
        if (CommanderName is null)
        {
            StatusMessage = "No commander profile is active.";
            return;
        }

        if (IsEnabled)
        {
            await RefreshAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Applies journal context and expires repair warnings when docking moves to another known system.
    /// </summary>
    public void ApplyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? journalCommanderName = null
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        string? owner = journalCommanderName ?? CommanderName;
        if (!string.Equals(detectedSquadronCommander, owner, StringComparison.OrdinalIgnoreCase))
        {
            detectedSquadronCarrierMarketId = null;
            detectedSquadronCommander = owner;
        }
        SystemEditor.ApplyJournalEvents(journalEvents);
        ColonizationDockingSnapshot? dockBefore = constructionState.CurrentDock;
        long before = constructionState.Version;
        journalDocks.Clear();
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            constructionState.Apply(journalEvent);
            journalDocks[journalEvent] = constructionState.CurrentDock;
            if (
                journalEvent.EventName is "Docked" or "Location"
                && constructionState.CurrentDock is { } carrierDock
                && ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(carrierDock)
            )
            {
                detectedSquadronCarrierMarketId = carrierDock.MarketId;
            }
            fleetCarrierIdentityTracker.Apply(journalEvent);
            ApplyShipIdentity(journalEvent);
            RememberJournalBody(journalEvent, owner);
        }

        long? previousSystemAddress =
            dockBefore?.SystemAddress ?? buildSiteRepairWarning?.SystemAddress ?? currentSystemAddress;
        if (
            previousSystemAddress is > 0
            && constructionState.CurrentDock is { SystemAddress: > 0 } nextDock
            && previousSystemAddress != nextDock.SystemAddress
        )
        {
            buildSiteRepairContextVersion++;
            SetBuildSiteRepairWarning(null);
        }

        if (dockBefore is not null && constructionState.CurrentDock is null)
        {
            InvalidateProjectLocationCache();
            lastDepotPatchPayloadSignature = null;
            ClearCargoBaseline(dockBefore.MarketId);
        }
        else if (
            dockBefore is not null
            && constructionState.CurrentDock is { } dockAfter
            && dockBefore.MarketId != dockAfter.MarketId
        )
        {
            ClearCargoBaseline(dockBefore.MarketId);
        }

        if (constructionState.Version != before)
        {
            UpdateConstructionDisplay();
            UpdateProjectSummary();
            UpdateProjectEditorContext();
            publishFleetCarrierCommand.RaiseCanExecuteChanged();
            syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Synchronizes journal events using their event-time dock and retries retained writes during idle polls.</summary>
    public async Task SynchronizeLiveProjectsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool allowPublishing,
        CargoInventoryState? cargoInventory = null,
        bool cargoActivity = false,
        bool preferShipCargoDiffForSquadron = true,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!allowPublishing || !IsEnabled || CommanderName is null)
        {
            ClearSquadronCargoSyncState(cargoInventory);
            return;
        }

        // When ship cargo is not current (or suppressed), squadron carriers use
        // journal transfer adjustments. Otherwise use the full GetDiff path.
        bool preferSquadronCargoDiff =
            cargoInventory is not null
            && preferShipCargoDiffForSquadron
            && journalEvents
                .Where(item => item.EventName is "MarketBuy" or "MarketSell" or "CargoTransfer")
                .All(item => GetJournalDock(item)?.MarketId == constructionState.CurrentDock?.MarketId);
        var messages = new List<string>();
        string? retryMessage = await RetryPendingWritesAsync(cancellationToken: cancellationToken);
        if (retryMessage is not null)
        {
            messages.Add(retryMessage);
        }
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            string? message = await TrySynchronizeLiveJournalEventAsync(
                journalEvent,
                preferSquadronCargoDiff,
                cargoInventory,
                cancellationToken: cancellationToken
            );
            if (!string.IsNullOrWhiteSpace(message))
            {
                messages.Add(message);
            }
        }

        if (cargoInventory is { } squadronCargoInventory && preferSquadronCargoDiff)
        {
            string? squadronMessage = await TrySynchronizeSquadronCargoDiffAsync(
                squadronCargoInventory,
                cargoActivity,
                cancellationToken: cancellationToken
            );
            if (!string.IsNullOrWhiteSpace(squadronMessage))
            {
                messages.Add(squadronMessage);
            }
        }

        if (cargoInventory is not null && !preferSquadronCargoDiff)
        {
            cargoInventory.GetDiff();
            skipNextCargoEvent = false;
        }

        if (messages.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, messages);
        }
    }

    private void ClearSquadronCargoSyncState(CargoInventoryState? cargoInventory)
    {
        // Drop held squadron state when publishing is disabled so lastInventory /
        // skipNext cannot survive across later cargo updates.
        skipNextCargoEvent = false;
        if (cargoInventory?.HasPreservedSnapshot == true)
        {
            cargoInventory.ClearPreservedSnapshot();
        }
    }

    /// <summary>Routes one live event and converts expected remote failures into recoverable status messages.</summary>
    private async Task<string?> TrySynchronizeLiveJournalEventAsync(
        JournalEventEnvelope journalEvent,
        bool preferShipCargoDiffForSquadron,
        CargoInventoryState? cargoInventory,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return journalEvent.EventName switch
            {
                "Docked" => CombineMessages(
                    await SynchronizeDockedProjectAsync(journalEvent, cancellationToken: cancellationToken),
                    await SynchronizeBuildSiteRepairAsync(journalEvent, cancellationToken: cancellationToken),
                    await EnsureLinkedFleetCarrierDockBaselineAsync(
                        GetJournalDock(journalEvent),
                        cancellationToken: cancellationToken
                    )
                ),
                "Location" when GetJournalBoolean(journalEvent.Payload, "Docked") == true =>
                    await SynchronizeBuildSiteRepairAsync(journalEvent, cancellationToken: cancellationToken),
                "ColonisationContribution" => await SynchronizeContributionAsync(
                    journalEvent,
                    cancellationToken: cancellationToken
                ),
                "ColonisationConstructionDepot" => await SynchronizeDepotAsync(
                    journalEvent,
                    cancellationToken: cancellationToken
                ),
                "ColonisationBeaconDeployed" => await SynchronizeBeaconDeploymentAsync(
                    cancellationToken: cancellationToken
                ),
                "DockingGranted" => ScheduleDockingRefresh(journalEvent),
                "MarketBuy" or "MarketSell" or "CargoTransfer" => await SynchronizeFleetCarrierCargoAdjustmentAsync(
                    journalEvent,
                    preferShipCargoDiffForSquadron,
                    cargoInventory,
                    cancellationToken: cancellationToken
                ),
                _ => null,
            };
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            return $"Raven project sync skipped {journalEvent.EventName}: " + exception.Message;
        }
    }

    /// <summary>Converts expected squadron cargo synchronization failures into recoverable status messages.</summary>
    private async Task<string?> TrySynchronizeSquadronCargoDiffAsync(
        CargoInventoryState cargoInventory,
        bool cargoActivity,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await SynchronizeSquadronFleetCarrierCargoDiffAsync(
                cargoInventory,
                cargoActivity,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            return "Raven project sync skipped squadron cargo diff: " + exception.Message;
        }
    }

    private string? ScheduleDockingRefresh(JournalEventEnvelope journalEvent)
    {
        if (
            Projects.Count == 0
            && !ColonizationDockingSnapshot.IsConstructionSiteName(
                GetJournalString(journalEvent.Payload, "StationName")
            )
        )
        {
            return null;
        }

        CancelDockingRefresh();
        dockingRefreshCancellation = new CancellationTokenSource();
        _ = RefreshAfterDockingAsync(dockingRefreshCancellation.Token);
        return null;
    }

    private async Task RefreshAfterDockingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await delayAsync(DockingRefreshDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer docking event, commander change, disable, or shutdown superseded it.
        }
    }

    private void CancelDockingRefresh()
    {
        CancellationTokenSource? cancellation = dockingRefreshCancellation;
        dockingRefreshCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <summary>Refreshes planned sites after beacon deployment while honoring synchronization cancellation.</summary>
    private async Task<string?> SynchronizeBeaconDeploymentAsync(CancellationToken cancellationToken = default)
    {
        if (storedRavenApiKey is null)
        {
            return "Raven architect update was not sent because this commander has no saved API key.";
        }

        if (string.IsNullOrWhiteSpace(currentSystemName))
        {
            return "Raven architect update was not sent because the current system is unknown.";
        }

        await client.UpdateSystemSitesAsync(
            currentSystemName,
            new ColonizationSystemSiteUpdate { Architect = CommanderName },
            storedRavenApiKey,
            cancellationToken
        );
        return $"Registered {CommanderName} as the Raven architect for {currentSystemName}.";
    }

    /// <summary>
    /// Freeze ship cargo before CargoTransfer mutates the live projection when docked on a
    /// linked squadron fleet carrier. Squadron carriers do not use journal transfer deltas;
    /// they rely on <see cref="SynchronizeSquadronFleetCarrierCargoDiffAsync"/>.
    /// </summary>
    public void PrepareSquadronCargoTransferSnapshot(CargoInventoryState cargo)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        if (
            !FleetCarrierCargoSyncEnabled
            || storedRavenApiKey is null
            || constructionState.CurrentDock is not { } dock
            || !IsLinkedSquadronFleetCarrier(dock)
        )
        {
            return;
        }

        // Preserve the first before-state across multiple CargoTransfer events in one poll.
        if (!cargo.HasPreservedSnapshot)
        {
            cargo.CaptureBeforeSnapshot();
        }
    }

    /// <summary>
    /// After ship cargo is updated, compute the squadron FC cargo delta from the frozen
    /// before-snapshot (or the last pre-replace inventory) and send it to Raven Colonial.
    /// </summary>
    public async Task<string?> SynchronizeSquadronFleetCarrierCargoDiffAsync(
        CargoInventoryState cargo,
        bool cargoActivity,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(cargo);
        if (!FleetCarrierCargoSyncEnabled || storedRavenApiKey is null || constructionState.CurrentDock is not { } dock)
        {
            skipNextCargoEvent = false;
            if (cargo.HasPreservedSnapshot)
            {
                cargo.ClearPreservedSnapshot();
            }

            return null;
        }

        // MarketBuy/Sell already adjusted the FC. Suppress GetDiff only when there is
        // no preserved transfer snapshot — transfer capture baselines after market
        // events, so its GetDiff must still be sent (same-poll Market+Transfer).
        if (skipNextCargoEvent)
        {
            skipNextCargoEvent = false;
            if (!cargo.HasPreservedSnapshot)
            {
                return null;
            }
        }

        if (!IsLinkedSquadronFleetCarrier(dock))
        {
            if (cargo.HasPreservedSnapshot)
            {
                cargo.ClearPreservedSnapshot();
            }

            return null;
        }

        // Match legacy: only run after Cargo activity / preserved transfer snapshot.
        if (!cargoActivity && !cargo.HasPreservedSnapshot)
        {
            return null;
        }

        // Compute diff while inventory is stable; network I/O stays outside GetDiff's lock.
        Dictionary<string, int> shipDiff = cargo.GetDiff();
        if (shipDiff.Count == 0)
        {
            return null;
        }

        IReadOnlyDictionary<string, int> adjustments =
            ColonizationFleetCarrierCargoSynchronizer.CreateSquadronCargoDiffAdjustment(shipDiff);

        // This aggregate difference has no single originating event; do not date it using the local clock.
        if (TryQueuePendingCargoDelta(dock.MarketId, adjustments, null))
        {
            return $"Queued {adjustments.Count:N0} squadron Fleet Carrier cargo update(s) until dock baseline finishes.";
        }

        return await ApplyFleetCarrierCargoAdjustmentAsync(
            dock.MarketId,
            adjustments,
            "squadron cargo diff",
            true,
            cargo,
            null,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Attributes a carrier transaction to its captured dock and queues it behind a pending baseline.</summary>
    private async Task<string?> SynchronizeFleetCarrierCargoAdjustmentAsync(
        JournalEventEnvelope journalEvent,
        bool preferShipCargoDiffForSquadron,
        CargoInventoryState? cargoInventory = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !FleetCarrierCargoSyncEnabled
            || storedRavenApiKey is null
            || GetJournalDock(journalEvent) is not { } dock
            || !fleetCarriers.Any(carrier => carrier.MarketId == dock.MarketId)
        )
        {
            return null;
        }

        IReadOnlyDictionary<string, int> adjustments =
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                journalEvent,
                dock,
                latestStatus?.InMainShip == true,
                preferShipCargoDiffForSquadron
            );
        if (adjustments.Count == 0)
        {
            return null;
        }

        if (TryQueuePendingCargoDelta(dock.MarketId, adjustments, journalEvent.Timestamp))
        {
            SuppressSquadronCargoDiffAfterMarketAdjustment(
                journalEvent.EventName,
                dock,
                preferShipCargoDiffForSquadron
            );
            return $"Queued {adjustments.Count:N0} Fleet Carrier cargo update(s) until dock baseline finishes.";
        }

        return await ApplyFleetCarrierCargoAdjustmentAsync(
            dock.MarketId,
            adjustments,
            journalEvent.EventName,
            preferShipCargoDiffForSquadron,
            cargoInventory,
            journalEvent.Timestamp,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Retains relative cargo updates until acknowledged and prevents later updates overtaking uncertain writes.</summary>
    private async Task<string?> ApplyFleetCarrierCargoAdjustmentAsync(
        long marketId,
        IReadOnlyDictionary<string, int> adjustments,
        string sourceEventName,
        bool preferShipCargoDiffForSquadron,
        CargoInventoryState? cargoInventory,
        DateTimeOffset? recordedAt,
        CancellationToken cancellationToken = default
    )
    {
        if (storedRavenApiKey is null || adjustments.Count == 0)
        {
            return null;
        }

        int version = profileVersion;
        var pending = new ColonizationPendingCargoAdjustment(
            GetWriteOwner(),
            marketId,
            new Dictionary<string, int>(adjustments, StringComparer.OrdinalIgnoreCase),
            recordedAt,
            fleetCarriers
                .FirstOrDefault(carrier => carrier.MarketId == marketId)
                ?.Cargo.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        );
        bool blocked = failedCargoAdjustments.Any(item => item.Owner == pending.Owner && item.MarketId == marketId);
        pending = pending with { Attempted = !blocked };
        failedCargoAdjustments.Add(pending);
        SavePendingCargoAdjustments();
        if (blocked)
        {
            if (GetJournalDockForMarket(marketId) is { } blockedDock)
            {
                SuppressSquadronCargoDiffAfterMarketAdjustment(
                    sourceEventName,
                    blockedDock,
                    preferShipCargoDiffForSquadron
                );
            }
            return "Fleet Carrier cargo update queued behind an unconfirmed adjustment.";
        }
        cargoWritesInFlight.Add((pending.Owner, marketId));
        CommodityOverlay.ApplyPendingFleetCarrierCargo(adjustments.Keys);
        try
        {
            IReadOnlyDictionary<string, int> updatedCargo = await client.AdjustFleetCarrierCargoAsync(
                marketId,
                adjustments,
                storedRavenApiKey,
                cancellationToken
            );
            failedCargoAdjustments.Remove(pending);
            SavePendingCargoAdjustments();
            if (version != profileVersion)
            {
                return null;
            }
            if (
                !preferShipCargoDiffForSquadron
                && constructionState.CurrentDock is { } dock
                && dock.MarketId == marketId
                && IsLinkedSquadronFleetCarrier(dock)
            )
            {
                cargoInventory?.ClearPreservedSnapshot();
            }
            ColonizationFleetCarrier? localCarrier = fleetCarriers.FirstOrDefault(carrier =>
                carrier.MarketId == marketId
            );
            if (localCarrier is not null)
            {
                ReplaceLocalFleetCarrier(
                    localCarrier with
                    {
                        Cargo = updatedCargo.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.OrdinalIgnoreCase
                        ),
                    }
                );
            }

            // Market buy/sell already adjusted the FC; suppress market-only GetDiff so
            // squadron carriers are not double-counted. Transfer snapshots still send.
            if (constructionState.CurrentDock is { } currentDock && currentDock.MarketId == marketId)
            {
                SuppressSquadronCargoDiffAfterMarketAdjustment(
                    sourceEventName,
                    currentDock,
                    preferShipCargoDiffForSquadron
                );
            }

            return $"Updated {adjustments.Count:N0} linked Fleet Carrier cargo entry(s) from {sourceEventName}.";
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            RetainFailedCargoAdjustment(pending, exception, sourceEventName, preferShipCargoDiffForSquadron);
            throw;
        }
        finally
        {
            cargoWritesInFlight.Remove((pending.Owner, marketId));
            CommodityOverlay.ApplyPendingFleetCarrierCargo(null);
        }
    }

    /// <summary>Records failure only while the adjustment remains pending, preserving the original transport exception.</summary>
    private void RetainFailedCargoAdjustment(
        ColonizationPendingCargoAdjustment pending,
        Exception exception,
        string sourceEventName,
        bool preferShipCargoDiffForSquadron
    )
    {
        int index = failedCargoAdjustments.IndexOf(pending);
        if (index >= 0)
        {
            failedCargoAdjustments[index] = pending with { OutcomeUnknown = !IsDefiniteRejection(exception) };
            SavePendingCargoAdjustments();
        }
        nextWriteRetry = utcNow().AddSeconds(5);
        if (GetJournalDockForMarket(pending.MarketId) is { } dock)
        {
            SuppressSquadronCargoDiffAfterMarketAdjustment(sourceEventName, dock, preferShipCargoDiffForSquadron);
        }
    }

    private void SuppressSquadronCargoDiffAfterMarketAdjustment(
        string eventName,
        ColonizationDockingSnapshot dock,
        bool preferShipCargoDiffForSquadron
    )
    {
        if (
            preferShipCargoDiffForSquadron
            && eventName is "MarketBuy" or "MarketSell"
            && ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(dock)
        )
        {
            skipNextCargoEvent = true;
        }
    }

    private bool IsLinkedSquadronFleetCarrier(ColonizationDockingSnapshot dock)
    {
        return string.Equals(dock.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase)
            && ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(dock)
            && fleetCarriers.Any(carrier => carrier.MarketId == dock.MarketId);
    }

    /// <summary>Loads a docked construction project and repairs reliable body metadata independently of faction changes.</summary>
    private async Task<string?> SynchronizeDockedProjectAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        var parser = new ColonizationConstructionState();
        if (!parser.Apply(journalEvent) || parser.CurrentDock is not { IsConstructionSite: true } dock)
        {
            return null;
        }

        ColonizationProjectLookup lookup = await FindOrLoadProjectAsync(
            dock.SystemAddress,
            dock.MarketId,
            cancellationToken: cancellationToken
        );
        ColonizationProject? project = lookup.Project;
        if (project is null)
        {
            return null;
        }

        bool isUntracked = !lookup.LinkedCommander && localUntrackedProject?.BuildId == project.BuildId;
        string? loadMessage = null;
        if (lookup.LinkedCommander)
        {
            loadMessage = $"Linked Raven project {project.BuildName} into the active list for this construction site.";
        }
        else if (isUntracked)
        {
            loadMessage = $"Loaded untracked Raven project {project.BuildName} for this construction site.";
        }

        ColonizationProjectUpdate? update = isUntracked ? null : CreateDockMetadataUpdate(project, dock, journalEvent);
        if (update is null)
        {
            return loadMessage;
        }
        ColonizationProject updated = await client.UpdateProjectAsync(update, cancellationToken);
        updated = await ClearPhantomCommoditiesAsync(updated, cancellationToken: cancellationToken);
        UpsertProject(updated);
        return CombineMessages(loadMessage, $"Updated Raven project metadata for {updated.BuildName}.");
    }

    /// <summary>Builds a minimal faction/body patch from reliable dock metadata, preserving unknown fields.</summary>
    private ColonizationProjectUpdate? CreateDockMetadataUpdate(
        ColonizationProject project,
        ColonizationDockingSnapshot dock,
        JournalEventEnvelope journalEvent
    )
    {
        bool factionChanged = !string.IsNullOrWhiteSpace(dock.FactionName) && dock.FactionName != project.FactionName;
        (int? bodyId, string? bodyName) = ResolveDockBody(dock, journalEvent);
        bool bodyChanged =
            bodyId is >= 0
            && (project.BodyNumber != bodyId || (!string.IsNullOrWhiteSpace(bodyName) && project.BodyName != bodyName));
        if (!factionChanged && !bodyChanged)
        {
            return null;
        }
        return new ColonizationProjectUpdate
        {
            BuildId = project.BuildId,
            FactionName = factionChanged ? dock.FactionName : null,
            BodyNumber = bodyChanged ? bodyId : null,
            BodyName = bodyChanged && !string.IsNullOrWhiteSpace(bodyName) ? bodyName : null,
        };
    }

    /// <summary>Uses an event's own body first, falling back only to a body known for the current commander and system.</summary>
    private (int? BodyId, string? BodyName) ResolveDockBody(
        ColonizationDockingSnapshot dock,
        JournalEventEnvelope journalEvent
    )
    {
        if (currentSystemAddress != dock.SystemAddress)
        {
            return (null, null);
        }
        if (ColonizationBodyJournal.ReadBodyId(journalEvent.Payload) is >= 0 and var bodyId)
        {
            return (bodyId, ColonizationBodyJournal.ReadBodyName(journalEvent.Payload));
        }
        return string.Equals(currentBodyCommanderName, CommanderName, StringComparison.OrdinalIgnoreCase)
            ? (currentBodyId, currentBodyName)
            : (null, null);
    }

    /// <summary>
    /// Repairs eligible docked sites and keeps failures scoped to the active system and commander context.
    /// </summary>
    private async Task<string?> SynchronizeBuildSiteRepairAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        if (storedRavenApiKey is not { } apiKey)
        {
            return null;
        }

        JsonElement root = journalEvent.Payload;
        string? stationName = GetJournalString(root, "StationName");
        string? stationType = GetJournalString(root, "StationType");
        long? systemAddress = GetJournalInt64(root, "SystemAddress");
        long? marketId = GetJournalInt64(root, "MarketID");
        bool isConstructionShip = stationName?.Contains("ColonisationShip", StringComparison.Ordinal) == true;
        if (
            systemAddress is not > 0
            || marketId is not > 0
            || string.IsNullOrWhiteSpace(stationName)
            || !ColonizationBuildSiteRepair.IsPlayerColonyMarketId(marketId.Value)
            || ColonizationBuildSiteRepair.ShouldSkipDockContext(stationType, stationName, isConstructionShip)
        )
        {
            return null;
        }

        string stationKey = ColonizationBuildSiteRepair.NormalizeDockStationName(stationName).ToLowerInvariant();
        if (stationKey.Length == 0)
        {
            return null;
        }

        var visit = new ColonizationBuildSiteRepairVisit(marketId.Value, stationKey);
        (long, long, string stationKey) inFlight = (systemAddress.Value, marketId.Value, stationKey);
        lock (buildSiteRepairLock)
        {
            if (buildSiteRepairVisitSet.Contains(visit) || !buildSiteRepairsInFlight.Add(inFlight))
            {
                return null;
            }
        }

        int contextVersion = buildSiteRepairContextVersion;
        try
        {
            return await RepairBuildSiteAsync(
                systemAddress.Value,
                marketId.Value,
                stationName,
                apiKey,
                contextVersion,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            ReportBuildSiteRepairFailure(journalEvent, systemAddress.Value, contextVersion, exception);
            return null;
        }
        finally
        {
            lock (buildSiteRepairLock)
            {
                buildSiteRepairsInFlight.Remove(inFlight);
            }
        }
    }

    /// <summary>
    /// Looks up and optionally patches a site, clearing its warning after a successful repair or no-op lookup.
    /// </summary>
    private async Task<string?> RepairBuildSiteAsync(
        long systemAddress,
        long marketId,
        string stationName,
        string apiKey,
        int contextVersion,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ColonizationSystemSite> sites = await GetSystemSitesForRepairAsync(
            systemAddress,
            cancellationToken: cancellationToken
        );
        ColonizationBuildSiteRepairPlan? plan = ColonizationBuildSiteRepair.CreatePlan(sites, stationName, marketId);
        if (plan is null)
        {
            ClearBuildSiteRepairWarning(systemAddress, contextVersion);
            return null;
        }

        if (string.IsNullOrWhiteSpace(plan.Site.Id))
        {
            throw new InvalidDataException(
                "The matched Raven site has no persisted ID; its market information could not be repaired."
            );
        }

        await client.PatchSystemSiteAsync(
            systemAddress.ToString(CultureInfo.InvariantCulture),
            plan.Site.Id,
            plan.CreatePatch(),
            apiKey,
            cancellationToken
        );
        RememberBuildSiteRepairVisit(
            new ColonizationBuildSiteRepairVisit(
                marketId,
                ColonizationBuildSiteRepair.NormalizeDockStationName(stationName).ToLowerInvariant()
            )
        );
        ClearBuildSiteRepairWarning(systemAddress, contextVersion);
        if (contextVersion != buildSiteRepairContextVersion)
        {
            return null;
        }

        return plan.Field == ColonizationBuildSiteRepairField.MarketId
            ? $"Repaired Raven Market Info for {plan.NormalizedStationName}."
            : $"Repaired the Raven site name for {plan.NormalizedStationName}.";
    }

    /// <summary>
    /// Shows a repair failure only while the request's system and context are still active.
    /// </summary>
    private void ReportBuildSiteRepairFailure(
        JournalEventEnvelope journalEvent,
        long systemAddress,
        int contextVersion,
        Exception exception
    )
    {
        string? systemName = GetJournalString(journalEvent.Payload, "StarSystem");
        bool differentSystem;
        if (currentSystemAddress is > 0)
        {
            differentSystem = currentSystemAddress != systemAddress;
        }
        else if (currentSystemName is not null && systemName is not null)
        {
            differentSystem = !string.Equals(currentSystemName, systemName, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            differentSystem =
                constructionState.CurrentDock is { SystemAddress: > 0 } activeDock
                && activeDock.SystemAddress != systemAddress;
        }
        if (contextVersion != buildSiteRepairContextVersion || differentSystem)
        {
            return;
        }

        string systemLabel = systemName ?? systemAddress.ToString(CultureInfo.InvariantCulture);
        SetBuildSiteRepairWarning(
            (
                systemAddress,
                $"Raven project sync skipped {journalEvent.EventName} in {systemLabel}: " + exception.Message
            )
        );
    }

    /// <summary>
    /// Clears only the recovered system's warning, leaving unrelated Raven status untouched.
    /// </summary>
    private void ClearBuildSiteRepairWarning(long systemAddress, int contextVersion)
    {
        if (contextVersion == buildSiteRepairContextVersion && buildSiteRepairWarning?.SystemAddress == systemAddress)
        {
            SetBuildSiteRepairWarning(null);
        }
    }

    /// <summary>
    /// Updates the independent repair warning and notifies the existing status binding when it changes.
    /// </summary>
    private void SetBuildSiteRepairWarning((long SystemAddress, string Message)? warning)
    {
        if (buildSiteRepairWarning == warning)
        {
            return;
        }

        buildSiteRepairWarning = warning;
        OnPropertyChanged(nameof(StatusMessage));
    }

    /// <summary>Loads planned sites with bounded repair caching and caller cancellation.</summary>
    private async Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesForRepairAsync(
        long systemAddress,
        CancellationToken cancellationToken = default
    )
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await client.GetSystemSitesAsync(
                    systemAddress.ToString(CultureInfo.InvariantCulture),
                    cancellationToken
                );
            }
            catch (Exception exception) when (attempt < 2 && exception is HttpRequestException or TaskCanceledException)
            {
                await delayAsync(TimeSpan.FromSeconds(1.5 * (attempt + 1)), cancellationToken);
                attempt++;
            }
        }
    }

    private void RememberBuildSiteRepairVisit(ColonizationBuildSiteRepairVisit visit)
    {
        lock (buildSiteRepairLock)
        {
            if (!buildSiteRepairVisitSet.Add(visit))
            {
                return;
            }

            if (buildSiteRepairVisits.Count == MaximumBuildSiteRepairVisits)
            {
                buildSiteRepairVisitSet.Remove(buildSiteRepairVisits.Dequeue());
            }

            buildSiteRepairVisits.Enqueue(visit);
            try
            {
                settingsStore.SaveBuildSiteRepairVisits(buildSiteRepairVisits);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The server repair succeeded; a cache write failure must not
                // report that successful remote change as failed.
            }
        }
    }

    /// <summary>Retains the originating delivery before uploading and reconciles remaining requirements after acknowledgement.</summary>
    private async Task<string?> SynchronizeContributionAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        long? marketId = GetJournalInt64(journalEvent.Payload, "MarketID");
        Dictionary<string, int> contributions = ReadJournalContributions(journalEvent.Payload);
        if (marketId is not > 0 || contributions.Count == 0)
        {
            return null;
        }

        ColonizationDockingSnapshot? dock = GetJournalDock(journalEvent);
        ColonizationProject? project = (
            await FindOrLoadProjectAsync(
                dock?.MarketId == marketId ? dock.SystemAddress : currentSystemAddress,
                marketId.Value,
                cancellationToken: cancellationToken
            )
        ).Project;
        if (project is null)
        {
            return "Raven did not identify a project for the recorded construction contribution.";
        }

        string eventId = journalEvent.RawJson;
        if (pendingContributions.Any(item => item.Owner == GetWriteOwner() && item.EventId == eventId))
        {
            return "This construction delivery is already retained for reconciliation.";
        }
        var pending = new ColonizationPendingContribution(
            GetWriteOwner(),
            project.BuildId,
            CommanderName!,
            contributions,
            eventId,
            true
        );
        pendingContributions.Add(pending);
        contributionsInFlight.Add(eventId);
        SavePendingContributions();
        try
        {
            await client.ContributeToProjectAsync(project.BuildId, pending.Commander, contributions, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            pendingContributions[pendingContributions.IndexOf(pending)] = pending with
            {
                OutcomeUnknown = !IsDefiniteRejection(exception),
            };
            SavePendingContributions();
            nextWriteRetry = utcNow().AddSeconds(5);
            throw;
        }
        finally
        {
            contributionsInFlight.Remove(eventId);
            NotifyContributionRecovery();
        }
        pendingContributions.Remove(pending);
        SavePendingContributions();
        // Contribute only credits history. Remaining need rows stay stale until an absolute
        // depot/commodity update lands — force that publish here so Raven cannot track a
        // delivery without updating what is still required.
        pendingContributionRemainingSync = true;
        string? remainingMessage = await PublishRemainingAfterContributionAsync(
            project,
            marketId.Value,
            contributions,
            cancellationToken: cancellationToken
        );
        return CombineMessages(
            $"Published {contributions.Values.Sum(value => (long)value):N0} contributed cargo units to {project.BuildName}.",
            remainingMessage
        );
    }

    /// <summary>Publishes absolute remaining requirements after delivery acknowledgement without duplicating credit.</summary>
    private async Task<string?> PublishRemainingAfterContributionAsync(
        ColonizationProject project,
        long marketId,
        IReadOnlyDictionary<string, int> contributions,
        CancellationToken cancellationToken = default
    )
    {
        ColonizationConstructionDepotSnapshot? depot = constructionState.CurrentDepot;
        if (depot is not null && depot.MarketId == marketId)
        {
            Dictionary<string, int> depotRemaining = ToRemainingCommodities(depot);
            if (!DictionariesEqual(project.Commodities, depotRemaining))
            {
                // Depot already moved ahead of the cached project (depot before contribute).
                return await PublishProjectRemainingAsync(
                    project,
                    depot,
                    depotRemaining,
                    force: true,
                    cancellationToken: cancellationToken
                );
            }
        }

        Dictionary<string, int> remaining = ApplyContributionToRemaining(project.Commodities, contributions);
        return await PublishProjectRemainingAsync(
            project,
            depot is not null && depot.MarketId == marketId ? depot : null,
            remaining,
            force: true,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>
    /// Synchronizes live depot events, handling completion independently of remaining-cargo updates.
    /// </summary>
    private async Task<string?> SynchronizeDepotAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        var parser = new ColonizationConstructionState();
        if (!parser.Apply(journalEvent) || parser.CurrentDepot is not { } depot)
        {
            return null;
        }

        ColonizationDockingSnapshot? dock = constructionState.CurrentDock;
        ColonizationProject? project = (
            await FindOrLoadProjectAsync(
                dock?.MarketId == depot.MarketId ? dock.SystemAddress : currentSystemAddress,
                depot.MarketId,
                cancellationToken: cancellationToken
            )
        ).Project;
        if (project is null)
        {
            return "Raven did not identify a project for the current construction depot.";
        }

        if (depot.IsComplete)
        {
            if (project.IsComplete)
            {
                return null;
            }

            await client.MarkProjectCompleteAsync(project.BuildId, cancellationToken);
            UpsertProject(
                project with
                {
                    IsComplete = true,
                    RemainingRequired = 0,
                    Commodities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                }
            );
            pendingContributionRemainingSync = false;
            InvalidateProjectLocationCache();
            return $"Marked Raven project {project.BuildName} complete.";
        }

        Dictionary<string, int> remaining = ToRemainingCommodities(depot);
        long maximumRequiredLong = depot.Resources.Sum(resource => (long)resource.RequiredAmount);
        if (maximumRequiredLong > int.MaxValue)
        {
            return "Raven project sync rejected construction requirements above the supported total.";
        }

        int maximumRequired = (int)maximumRequiredLong;
        bool force = pendingContributionRemainingSync;
        bool requiresUpdate =
            force
            || project.MaximumRequired != maximumRequired
            || !DictionariesEqual(project.Commodities, remaining)
            || depot.IsFailed;
        if (!requiresUpdate)
        {
            return null;
        }

        return await PublishProjectRemainingAsync(
            project,
            depot,
            remaining,
            force,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Patches only remaining commodity requirements using the initiating cancellation token.</summary>
    private async Task<string?> PublishProjectRemainingAsync(
        ColonizationProject project,
        ColonizationConstructionDepotSnapshot? depot,
        Dictionary<string, int> remaining,
        bool force,
        CancellationToken cancellationToken = default
    )
    {
        remaining = ColonizationCommodityMaps.NormalizeNeedMap(remaining);
        int? maximumRequired = depot is null
            ? project.MaximumRequired
            : checked((int)depot.Resources.Sum(resource => (long)resource.RequiredAmount));
        if (
            !force
            && project.MaximumRequired == maximumRequired
            && DictionariesEqual(project.Commodities, remaining)
            && depot is not { IsFailed: true }
        )
        {
            return null;
        }

        string signature = ColonizationCommodityMaps.CreateDepotUpdateSignature(
            project.BuildId,
            maximumRequired,
            remaining,
            includeDepot: depot is not null,
            depotFailed: depot?.IsFailed == true
        );
        if (!force && string.Equals(signature, lastDepotPatchPayloadSignature, StringComparison.Ordinal))
        {
            return null;
        }

        await ClearPhantomCommoditiesAsync(project, cancellationToken: cancellationToken);

        ColonizationProject updated = await client.UpdateProjectAsync(
            new ColonizationProjectUpdate
            {
                BuildId = project.BuildId,
                MaximumRequired = maximumRequired,
                Commodities = remaining,
                ConstructionDepot = depot is null ? null : ColonizationConstructionDepotPayload.FromSnapshot(depot),
            },
            cancellationToken
        );
        updated = await ClearPhantomCommoditiesAsync(updated, cancellationToken: cancellationToken);
        UpsertProject(updated);
        lastDepotPatchPayloadSignature = signature;
        InvalidateProjectLocationCache();
        pendingContributionRemainingSync = false;
        return force
            ? $"Updated Raven remaining cargo after contribution for {updated.BuildName}."
            : $"Updated Raven construction requirements for {updated.BuildName}.";
    }

    /// <summary>Removes completed phantom requirements from a project without changing unrelated fields.</summary>
    private async Task<ColonizationProject> ClearPhantomCommoditiesAsync(
        ColonizationProject project,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, int> zeroes = ColonizationCommodityMaps.PhantomZeroPatchMap(project.Commodities);
        if (zeroes.Count == 0)
        {
            return project;
        }

        Dictionary<string, int> cleared = ColonizationCommodityMaps.ApplyPhantomZeros(project.Commodities);
        ColonizationProject updated = await client.UpdateProjectAsync(
            new ColonizationProjectUpdate { BuildId = project.BuildId, Commodities = zeroes },
            cancellationToken
        );
        // Prefer the cleared need map when the API echoes a partial merge response.
        if (ColonizationCommodityMaps.PhantomZeroPatchMap(updated.Commodities).Count > 0)
        {
            return updated with { Commodities = cleared, RemainingRequired = cleared.Values.Sum() };
        }

        return updated with
        {
            Commodities = ColonizationCommodityMaps.ApplyPhantomZeros(updated.Commodities),
        };
    }

    private static Dictionary<string, int> ToRemainingCommodities(ColonizationConstructionDepotSnapshot depot)
    {
        return depot.Resources.ToDictionary(
            resource => resource.Name,
            resource => resource.RemainingAmount,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static Dictionary<string, int> ApplyContributionToRemaining(
        IReadOnlyDictionary<string, int> commodities,
        IReadOnlyDictionary<string, int> contributions
    )
    {
        var remaining = new Dictionary<string, int>(commodities, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> contribution in contributions)
        {
            string name = ColonizationConstructionState.NormalizeCommodityName(contribution.Key);
            if (name.Length == 0 || contribution.Value <= 0)
            {
                continue;
            }

            int current = remaining.GetValueOrDefault(name);
            int next = Math.Max(0, current - contribution.Value);
            remaining[name] = next;
        }

        return remaining;
    }

    /// <summary>Resolves a project by system and market with cancellation-aware cache recovery.</summary>
    private async Task<ColonizationProjectLookup> FindOrLoadProjectAsync(
        long? systemAddress,
        long marketId,
        CancellationToken cancellationToken = default
    )
    {
        ColonizationProject? project =
            Projects.Select(row => row.Project).FirstOrDefault(candidate => candidate.MarketId == marketId)
            ?? (localUntrackedProject?.MarketId == marketId ? localUntrackedProject : null);
        if (project is not null || systemAddress is not > 0)
        {
            return new ColonizationProjectLookup(project, LinkedCommander: false);
        }

        if (TryGetCachedProject(systemAddress.Value, marketId, out ColonizationProject? cached))
        {
            project = cached;
        }
        else
        {
            project = await client.GetProjectAsync(systemAddress.Value, marketId, cancellationToken);
            if (project is not null)
            {
                RememberProjectLocation(systemAddress.Value, marketId, project);
            }
        }

        if (project is null)
        {
            return new ColonizationProjectLookup(null, LinkedCommander: false);
        }

        project = await ClearPhantomCommoditiesAsync(project, cancellationToken: cancellationToken);

        bool linkedCommander = false;
        if (ShouldAutoLinkDockedProject(project))
        {
            await client.LinkCommanderAsync(project.BuildId, CommanderName!, cancellationToken);
            localUntrackedProject = null;
            linkedCommander = true;
            InvalidateProjectLocationCache();
        }
        else
        {
            localUntrackedProject = project;
        }

        UpsertProject(project);
        return new ColonizationProjectLookup(project, linkedCommander);
    }

    private bool ShouldAutoLinkDockedProject(ColonizationProject project)
    {
        return !string.IsNullOrWhiteSpace(CommanderName)
            && !string.IsNullOrWhiteSpace(project.ArchitectName)
            && string.Equals(CommanderName.Trim(), project.ArchitectName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetCachedProject(long systemAddress, long marketId, out ColonizationProject? project)
    {
        project = null;
        if (projectLocationCache is not { } cache || cache.SystemAddress != systemAddress || cache.MarketId != marketId)
        {
            return false;
        }

        long ageMilliseconds = Environment.TickCount64 - cache.MonotonicTicks;
        if (ageMilliseconds < 0 || ageMilliseconds > ProjectLocationCacheTtl.TotalMilliseconds)
        {
            projectLocationCache = null;
            return false;
        }

        project = cache.Project;
        return true;
    }

    private void RememberProjectLocation(long systemAddress, long marketId, ColonizationProject project)
    {
        projectLocationCache = (systemAddress, marketId, project, Environment.TickCount64);
    }

    private void InvalidateProjectLocationCache()
    {
        projectLocationCache = null;
    }

    private readonly record struct ColonizationProjectLookup(ColonizationProject? Project, bool LinkedCommander);

    private void UpsertProject(ColonizationProject project)
    {
        if (localUntrackedProject?.BuildId == project.BuildId)
        {
            localUntrackedProject = project;
        }

        Projects = Projects
            .Select(row => row.Project)
            .Where(candidate => !string.Equals(candidate.BuildId, project.BuildId, StringComparison.OrdinalIgnoreCase))
            .Append(project)
            .OrderBy(candidate => candidate.SystemName)
            .ThenBy(candidate => candidate.BuildName)
            .Select(CreateRow)
            .ToArray();
        UpdateProjectSummary();
    }

    private static Dictionary<string, int> ReadJournalContributions(JsonElement root)
    {
        if (!root.TryGetProperty("Contributions", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement row in rows.EnumerateArray())
        {
            string name = ColonizationConstructionState.NormalizeCommodityName(GetJournalString(row, "Name"));
            int? amount = GetJournalInt32(row, "Amount");
            if (name.Length > 0 && amount is > 0)
            {
                int existing = result.GetValueOrDefault(name);
                if (existing > int.MaxValue - amount.Value)
                {
                    return [];
                }

                result[name] = existing + amount.Value;
            }
        }

        return result;
    }

    private static bool DictionariesEqual(Dictionary<string, int> left, Dictionary<string, int> right)
    {
        return left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out int value) && value == pair.Value);
    }

    public async Task UpdateCargoAsync(CargoSnapshot? cargo, bool publishCurrentShipCargo = true)
    {
        if (cargo is null || SharedCargoSuppressed)
        {
            return;
        }

        shipCargo = cargo;
        UpdateCommodityPlan();
        if (publishCurrentShipCargo)
        {
            await PublishCurrentShipCargoAsync(cargo);
        }
    }

    private async Task PublishCurrentShipCargoAsync(CargoSnapshot cargo)
    {
        if (!ShipCargoPublishingEnabled)
        {
            return;
        }

        string? blockReason = GetShipCargoPublishingBlockReason();
        if (blockReason is not null)
        {
            ShipCargoPublishingStatus = blockReason;
            return;
        }

        var cargoCounts = cargo
            .Inventory.Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => Math.Max(0, item.Count)),
                StringComparer.OrdinalIgnoreCase
            );
        IsShipCargoPublishingBusy = true;
        ShipCargoPublishingStatus = "Publishing current ship cargo...";
        try
        {
            await client.PublishCurrentShipAsync(
                new ColonizationCurrentShip
                {
                    CommanderName = CommanderName!,
                    Name = currentShipName ?? currentShipType!,
                    Type = currentShipType!,
                    MaximumCargo = constructionState.ShipCargoCapacity,
                    Cargo = cargoCounts,
                },
                storedRavenApiKey!,
                CancellationToken.None
            );
            ShipCargoPublishingStatus = $"Published {cargoCounts.Count:N0} ship cargo entries to Raven Colonial.";
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            ShipCargoPublishingStatus = "Ship cargo was not published: " + exception.Message;
        }
        finally
        {
            IsShipCargoPublishingBusy = false;
        }
    }

    public async Task UpdateMarketAsync(MarketSnapshot? market)
    {
        if (market is null)
        {
            return;
        }

        currentMarket = market;
        UpdateCommodityPlan();
        publishFleetCarrierCommand.RaiseCanExecuteChanged();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
        if (FleetCarrierCargoSyncEnabled)
        {
            await SyncFleetCarrierCargoAsync(force: false);
        }
    }

    public void UpdateStatus(EliteStatus? status)
    {
        if (status is null)
        {
            return;
        }

        latestStatus = status;
        SystemEditor.UpdateStatus(status);
        UpdateCommodityPlan();
    }

    public void UpdateMusicTrack(string? musicTrack)
    {
        CommodityOverlay.UpdateMusicTrack(musicTrack);
    }

    /// <summary>
    /// Updates the active system and prevents warnings from earlier systems from lingering or arriving late.
    /// </summary>
    public void UpdateSystemContext(string? systemName, GalacticCoordinate? position, long? systemAddress = null)
    {
        string? nextSystemName = string.IsNullOrWhiteSpace(systemName) ? null : systemName.Trim();
        long? nextSystemAddress = systemAddress is > 0 ? systemAddress : null;
        bool samePosition = position is GalacticCoordinate coordinate
            ? currentStarPosition.Count == 3
                && Math.Abs(currentStarPosition[0] - coordinate.X) <= 0.0000001d
                && Math.Abs(currentStarPosition[1] - coordinate.Y) <= 0.0000001d
                && Math.Abs(currentStarPosition[2] - coordinate.Z) <= 0.0000001d
            : currentStarPosition.Count == 0;
        if (
            string.Equals(currentSystemName, nextSystemName, StringComparison.OrdinalIgnoreCase)
            && currentSystemAddress == nextSystemAddress
            && samePosition
        )
        {
            return;
        }

        if (
            !string.Equals(currentSystemName, nextSystemName, StringComparison.OrdinalIgnoreCase)
            || (currentSystemAddress is > 0 && nextSystemAddress is > 0 && currentSystemAddress != nextSystemAddress)
        )
        {
            buildSiteRepairContextVersion++;
            SetBuildSiteRepairWarning(null);
            currentBodyId = null;
            currentBodyName = null;
            currentBodyCommanderName = null;
        }

        currentSystemName = nextSystemName;
        currentSystemAddress = nextSystemAddress;
        currentStarPosition = position is GalacticCoordinate nextCoordinate
            ? [nextCoordinate.X, nextCoordinate.Y, nextCoordinate.Z]
            : [];
        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
    }

    public void ReportLinkFailure(string message)
    {
        StatusMessage = "Raven Colonial could not be opened: " + message;
    }

    public void Dispose()
    {
        CancelDockingRefresh();
    }

    /// <summary>Validates and persists a Raven key only against the captured commander profile.</summary>
    public async Task SaveRavenApiKeyAsync()
    {
        if (!CanSaveRavenApiKey() || commanderProfileStore is null || profileFrontierId is null)
        {
            return;
        }

        int version = profileVersion;
        IsFleetCarrierSyncBusy = true;
        try
        {
            await PersistRavenApiKeyAsync();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or HttpRequestException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            if (version == profileVersion)
            {
                RavenCredentialStatus = "The Raven API key was not saved: " + exception.Message;
            }
        }
        finally
        {
            IsFleetCarrierSyncBusy = false;
            RaiseCommandStates();
        }
    }

    /// <summary>Saves credentials to the initiating Frontier identity and applies them only if that profile is still current.</summary>
    private async Task PersistRavenApiKeyAsync()
    {
        int version = profileVersion;
        string? capturedFrontierId = profileFrontierId;
        bool capturedOdyssey = profileIsOdyssey;
        string? capturedCommander = CommanderName;
        string? normalized = string.IsNullOrWhiteSpace(RavenApiKey) ? null : RavenApiKey.Trim();
        string? validatedCommander = null;
        if (
            normalized is not null
            && !await TryValidateRavenApiKeyAsync(normalized, commander => validatedCommander = commander)
        )
        {
            return;
        }

        if (version != profileVersion)
        {
            return;
        }
        CommanderProfileStore store =
            commanderProfileStore ?? throw new InvalidOperationException("Commander profile storage is not available.");
        string frontierId =
            capturedFrontierId ?? throw new InvalidOperationException("No commander frontier id is available.");
        await store.SaveRavenColonialApiKeyAsync(
            frontierId,
            capturedCommander,
            capturedOdyssey,
            normalized,
            CancellationToken.None
        );
        if (version == profileVersion)
        {
            ApplySavedRavenApiKey(normalized, validatedCommander);
        }
    }

    private void ApplySavedRavenApiKey(string? normalized, string? validatedCommander)
    {
        storedRavenApiKey = normalized;
        RavenApiKey = normalized ?? string.Empty;
        RavenCredentialStatus = normalized is null
            ? "The Raven API key was removed from this commander profile."
            : $"The Raven API key was validated for {validatedCommander} and saved.";
        OnPropertyChanged(nameof(HasStoredRavenApiKey));
        if (normalized is null && FleetCarrierCargoSyncEnabled)
        {
            FleetCarrierCargoSyncEnabled = false;
        }

        if (ShipCargoPublishingEnabled)
        {
            ShipCargoPublishingStatus = GetShipCargoReadyStatus();
        }

        UpdateProjectEditorContext();
        UpdateSystemEditorContext();
    }

    /// <summary>Checks key ownership against a captured commander and rejects results from a superseded profile.</summary>
    private async Task<bool> TryValidateRavenApiKeyAsync(string normalized, Action<string?> setValidatedCommander)
    {
        if (CommanderName is null)
        {
            RavenCredentialStatus = "Load the active commander before validating a Raven API key.";
            return false;
        }

        int version = profileVersion;
        string commander = CommanderName;
        RavenCredentialStatus = "Validating the Raven API key without saving it...";
        string? validatedCommander = await client.GetCommanderByApiKeyAsync(normalized, CancellationToken.None);
        if (version != profileVersion)
        {
            return false;
        }
        setValidatedCommander(validatedCommander);
        if (validatedCommander is null)
        {
            RavenCredentialStatus = "Raven rejected this API key. The saved key was not changed.";
            return false;
        }

        if (!string.Equals(validatedCommander, commander, StringComparison.OrdinalIgnoreCase))
        {
            RavenCredentialStatus =
                $"This key belongs to {validatedCommander}, not " + $"{CommanderName}. The saved key was not changed.";
            return false;
        }

        return true;
    }

    /// <summary>Registers a carrier and serializes its absolute cargo baseline before replaying queued transactions.</summary>
    public async Task PublishCurrentFleetCarrierAsync()
    {
        if (
            !CanPublishCurrentFleetCarrier()
            || constructionState.CurrentDock is not { } dock
            || storedRavenApiKey is null
        )
        {
            FleetCarrierSyncStatus = GetFleetCarrierPublishBlockReason();
            return;
        }

        BeginCargoBaselinePending(dock.MarketId);
        bool published = false;
        IsFleetCarrierSyncBusy = true;
        FleetCarrierSyncStatus = $"Publishing {dock.StationName} to Raven Colonial...";
        try
        {
            ColonizationFleetCarrier registered = await client.PublishFleetCarrierAsync(
                new ColonizationFleetCarrierRegistration
                {
                    MarketId = dock.MarketId,
                    Name = dock.StationName,
                    DisplayName = fleetCarrierIdentityTracker.ResolveDisplayName(dock.StationName),
                },
                storedRavenApiKey,
                CancellationToken.None
            );
            published = true;
            registered = registered with
            {
                Cargo = registered.Cargo ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            };
            ReplaceLocalFleetCarrier(registered);

            MarketSnapshot? market = GetFreshFleetCarrierMarket(dock);
            if (market is null)
            {
                FleetCarrierSyncStatus =
                    $"Published and linked {GetCarrierName(registered)}. "
                    + "Open its commodity market to synchronize cargo.";
                return;
            }

            IReadOnlyDictionary<string, int> replacements =
                ColonizationFleetCarrierCargoSynchronizer.CreateMarketReplacement(market, registered);
            if (replacements.Count == 0)
            {
                ReconcilePendingCargo(market);
                lastSyncedMarket = (market.MarketId, market.Timestamp);
                FleetCarrierSyncStatus =
                    $"Published and linked {GetCarrierName(registered)}; " + "its cargo is already current.";
                return;
            }

            CommodityOverlay.ApplyPendingFleetCarrierCargo(replacements.Keys);
            IReadOnlyDictionary<string, int> updatedCargo = await client.ReplaceFleetCarrierCargoAsync(
                dock.MarketId,
                replacements,
                storedRavenApiKey,
                CancellationToken.None
            );
            ReplaceLocalFleetCarrier(
                registered with
                {
                    Cargo = updatedCargo.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase
                    ),
                }
            );
            ReconcilePendingCargo(market);
            lastSyncedMarket = (market.MarketId, market.Timestamp);
            FleetCarrierSyncStatus =
                $"Published and linked {GetCarrierName(registered)} and "
                + $"updated {replacements.Count:N0} cargo entries.";
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            FleetCarrierSyncStatus = published
                ? "The Fleet Carrier was linked, but its current cargo was not updated: " + exception.Message
                : "The Fleet Carrier was not published: " + exception.Message;
        }
        finally
        {
            CommodityOverlay.ApplyPendingFleetCarrierCargo(null);
            await CompleteCargoBaselineAsync(dock.MarketId, CancellationToken.None);
            IsFleetCarrierSyncBusy = false;
        }
    }

    /// <summary>
    /// Seeds RavenColonial with a full CAPI cargo manifest once per linked carrier per
    /// session (EDMC parity). Later Frontier refreshes are ignored; journal deltas keep
    /// linked + workspace totals current between CAPI updates.
    /// </summary>
    public async Task SeedLinkedCarrierCargoFromCapiAsync(FrontierAccountSnapshot? snapshot)
    {
        if (snapshot is null || !FleetCarrierCargoSyncEnabled || storedRavenApiKey is null || fleetCarriers.Count == 0)
        {
            return;
        }

        int generation = capiCargoSeedGeneration;
        await TrySeedCarrierFromCapiAsync(
            snapshot.Carrier,
            snapshot.CarrierFetchedAt ?? snapshot.FetchedAt,
            snapshot.IsDocked,
            generation
        );
        if (generation != capiCargoSeedGeneration)
        {
            return;
        }

        await TrySeedCarrierFromCapiAsync(
            snapshot.SquadronCarrier,
            snapshot.SquadronCarrierFetchedAt ?? snapshot.FetchedAt,
            snapshot.IsDocked,
            generation
        );
    }

    public async Task SyncFleetCarrierCargoAsync(bool force = true)
    {
        if (
            !TryBeginFleetCarrierCargoSync(
                force,
                out MarketSnapshot? market,
                out string? apiKey,
                out (long MarketId, DateTimeOffset Timestamp) identity,
                out ColonizationFleetCarrier? localCarrier
            )
        )
        {
            return;
        }

        IsFleetCarrierSyncBusy = true;
        FleetCarrierSyncStatus = $"Checking {GetCarrierName(localCarrier)} market cargo...";
        try
        {
            await ApplyFleetCarrierMarketCargoSyncAsync(market, apiKey, identity);
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            FleetCarrierSyncStatus = "Fleet Carrier cargo was not updated: " + exception.Message;
        }
        finally
        {
            CommodityOverlay.ApplyPendingFleetCarrierCargo(null);
            IsFleetCarrierSyncBusy = false;
        }
    }

    private void ClearCapiCargoSeedSession()
    {
        capiCargoSeededMarketIds.Clear();
        capiCargoSeedGeneration++;
    }

    /// <summary>Installs a reliable Frontier carrier manifest through the same baseline queue as market synchronization.</summary>
    private async Task TrySeedCarrierFromCapiAsync(
        FrontierCarrierSnapshot? carrier,
        DateTimeOffset? fetchedAt,
        bool isDocked,
        int generation
    )
    {
        if (carrier is null || storedRavenApiKey is null || generation != capiCargoSeedGeneration)
        {
            return;
        }

        long? marketId = ColonizationFleetCarrierCapiCargoSeeder.ResolveLinkedMarketId(carrier, fleetCarriers);
        if (marketId is not { } linkedMarketId)
        {
            return;
        }

        ColonizationFleetCarrier? localCarrier = fleetCarriers.FirstOrDefault(c => c.MarketId == linkedMarketId);
        (bool accepted, string reason) = ColonizationFleetCarrierCapiCargoSeeder.ShouldAcceptSnapshot(
            linkedMarketId,
            isDocked || constructionState.CurrentDock is not null,
            capiCargoSeededMarketIds,
            fetchedAt,
            localCarrier?.Cargo
        );
        if (!accepted)
        {
            return;
        }

        IReadOnlyDictionary<string, int> totals = ColonizationFleetCarrierCapiCargoSeeder.CreateCargoTotals(carrier);
        if (
            localCarrier is not null
            && !ColonizationFleetCarrierCapiCargoSeeder.ManifestsDiffer(localCarrier.Cargo, totals)
        )
        {
            capiCargoSeededMarketIds.Add(linkedMarketId);
            return;
        }

        BeginCargoBaselinePending(linkedMarketId);
        try
        {
            CommodityOverlay.ApplyPendingFleetCarrierCargo(totals.Keys);
            IReadOnlyDictionary<string, int> updatedCargo = await client.ReplaceFleetCarrierCargoAsync(
                linkedMarketId,
                totals,
                storedRavenApiKey,
                CancellationToken.None
            );
            if (generation != capiCargoSeedGeneration)
            {
                return;
            }

            capiCargoSeededMarketIds.Add(linkedMarketId);
            ColonizationFleetCarrier? current = fleetCarriers.FirstOrDefault(c => c.MarketId == linkedMarketId);
            if (current is not null)
            {
                ReplaceLocalFleetCarrier(
                    current with
                    {
                        Cargo = updatedCargo.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.OrdinalIgnoreCase
                        ),
                    }
                );
            }

            FleetCarrierSyncStatus = $"Seeded linked carrier {linkedMarketId} cargo from Frontier CAPI ({reason}).";
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            FleetCarrierSyncStatus = "Frontier CAPI cargo seed was not applied: " + exception.Message;
        }
        finally
        {
            CommodityOverlay.ApplyPendingFleetCarrierCargo(null);
            await CompleteCargoBaselineAsync(linkedMarketId, CancellationToken.None);
        }
    }

    private bool TryBeginFleetCarrierCargoSync(
        bool force,
        out MarketSnapshot market,
        out string apiKey,
        out (long MarketId, DateTimeOffset Timestamp) identity,
        out ColonizationFleetCarrier localCarrier
    )
    {
        market = null!;
        apiKey = null!;
        identity = default;
        localCarrier = null!;
        if (!CanSyncFleetCarrierCargo() || currentMarket is null || storedRavenApiKey is null)
        {
            if (force)
            {
                FleetCarrierSyncStatus = GetFleetCarrierSyncBlockReason();
            }

            return false;
        }

        identity = (currentMarket.MarketId, currentMarket.Timestamp);
        if (!force && lastSyncedMarket == identity)
        {
            return false;
        }

        ColonizationFleetCarrier? carrier = fleetCarriers.FirstOrDefault(candidate =>
            candidate.MarketId == currentMarket.MarketId
        );
        if (carrier is null)
        {
            if (force)
            {
                FleetCarrierSyncStatus = GetFleetCarrierSyncBlockReason();
            }

            return false;
        }

        market = currentMarket;
        apiKey = storedRavenApiKey;
        localCarrier = carrier;
        return true;
    }

    /// <summary>Installs an authoritative market baseline before applying transactions recorded while synchronization was pending.</summary>
    private async Task ApplyFleetCarrierMarketCargoSyncAsync(
        MarketSnapshot market,
        string apiKey,
        (long MarketId, DateTimeOffset Timestamp) identity
    )
    {
        BeginCargoBaselinePending(market.MarketId);
        try
        {
            ColonizationFleetCarrier? serverCarrier = await client.GetFleetCarrierAsync(
                market.MarketId,
                CancellationToken.None
            );
            if (serverCarrier is null)
            {
                FleetCarrierSyncStatus = "Raven Colonial does not have this Fleet Carrier.";
                return;
            }

            // Re-resolve after await: the local list may have changed while waiting.
            ColonizationFleetCarrier? localCarrier = fleetCarriers.FirstOrDefault(carrier =>
                carrier.MarketId == market.MarketId
            );

            IReadOnlyDictionary<string, int> replacements =
                ColonizationFleetCarrierCargoSynchronizer.CreateMarketReplacement(market, serverCarrier);
            if (replacements.Count == 0)
            {
                if (localCarrier is not null)
                {
                    ReplaceLocalFleetCarrier(serverCarrier);
                }

                ReconcilePendingCargo(market);
                lastSyncedMarket = identity;
                FleetCarrierSyncStatus = $"{GetCarrierName(serverCarrier)} cargo is already current.";
                return;
            }

            CommodityOverlay.ApplyPendingFleetCarrierCargo(replacements.Keys);
            FleetCarrierSyncStatus =
                $"Updating {replacements.Count:N0} cargo entries for " + GetCarrierName(serverCarrier) + "...";
            IReadOnlyDictionary<string, int> updatedCargo = await client.ReplaceFleetCarrierCargoAsync(
                market.MarketId,
                replacements,
                apiKey,
                CancellationToken.None
            );
            localCarrier = fleetCarriers.FirstOrDefault(carrier => carrier.MarketId == market.MarketId);
            if (localCarrier is not null)
            {
                ReplaceLocalFleetCarrier(
                    serverCarrier with
                    {
                        Cargo = updatedCargo.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.OrdinalIgnoreCase
                        ),
                    }
                );
            }

            ReconcilePendingCargo(market);
            lastSyncedMarket = identity;
            FleetCarrierSyncStatus =
                $"Updated {replacements.Count:N0} cargo entries for " + GetCarrierName(serverCarrier) + ".";
        }
        finally
        {
            CommodityOverlay.ApplyPendingFleetCarrierCargo(null);
            await CompleteCargoBaselineAsync(market.MarketId, CancellationToken.None);
        }
    }

    /// <summary>Loads the linked carrier baseline for the event-time dock before relative cargo synchronization.</summary>
    private async Task<string?> EnsureLinkedFleetCarrierDockBaselineAsync(
        ColonizationDockingSnapshot? dock,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !FleetCarrierCargoSyncEnabled
            || storedRavenApiKey is null
            || dock is null
            || !string.Equals(dock.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase)
            || !fleetCarriers.Any(carrier => carrier.MarketId == dock.MarketId)
        )
        {
            return null;
        }

        if (cargoBaselineReady.Contains(dock.MarketId) || IsCargoBaselinePending(dock.MarketId))
        {
            return null;
        }

        ColonizationFleetCarrier? localCarrier = fleetCarriers.FirstOrDefault(carrier =>
            carrier.MarketId == dock.MarketId
        );
        if (localCarrier is null)
        {
            return null;
        }

        if (!ColonizationFleetCarrierPendingCargo.NeedsServerBaseline(localCarrier.Cargo))
        {
            cargoBaselineReady.Add(dock.MarketId);
            return null;
        }

        BeginCargoBaselinePending(dock.MarketId);
        bool stored = false;
        try
        {
            ColonizationFleetCarrier? serverCarrier = await client.GetFleetCarrierAsync(
                dock.MarketId,
                cancellationToken
            );
            if (serverCarrier is null)
            {
                return null;
            }

            ReplaceLocalFleetCarrier(
                serverCarrier with
                {
                    Cargo = (
                        serverCarrier.Cargo ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    ).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                }
            );
            stored = true;
            return $"Loaded Raven Colonial cargo baseline for {GetCarrierName(serverCarrier)}.";
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            return "Fleet Carrier dock baseline was not loaded: " + exception.Message;
        }
        finally
        {
            await CompleteCargoBaselineAsync(dock.MarketId, cancellationToken: cancellationToken);
            if (!stored)
            {
                cargoBaselineReady.Remove(dock.MarketId);
            }
        }
    }

    private void BeginCargoBaselinePending(long marketId)
    {
        cargoBaselinePendingDepth[marketId] = cargoBaselinePendingDepth.GetValueOrDefault(marketId) + 1;
        cargoBaselineReady.Remove(marketId);
    }

    private bool IsCargoBaselinePending(long marketId)
    {
        return cargoBaselinePendingDepth.GetValueOrDefault(marketId) > 0;
    }

    /// <summary>Queues deltas with their latest journal time, keeping aggregate time unknown if any included event is undated.</summary>
    private bool TryQueuePendingCargoDelta(
        long marketId,
        IReadOnlyDictionary<string, int> delta,
        DateTimeOffset? recordedAt
    )
    {
        if (!IsCargoBaselinePending(marketId) || delta.Count == 0)
        {
            return false;
        }

        if (
            !pendingCargoDeltas.TryGetValue(
                marketId,
                out (Dictionary<string, int> Delta, DateTimeOffset? RecordedAt) pending
            )
        )
        {
            pending = (new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), recordedAt);
        }
        DateTimeOffset? latest = null;
        if (pending.RecordedAt is { } previous && recordedAt is { } current)
        {
            latest = previous > current ? previous : current;
        }
        pendingCargoDeltas[marketId] = (pending.Delta, latest);
        ColonizationFleetCarrierPendingCargo.MergeDelta(pending.Delta, delta);
        return true;
    }

    /// <summary>Completes the outermost baseline and preserves failed queued transactions for later reconciliation.</summary>
    private async Task CompleteCargoBaselineAsync(long marketId, CancellationToken cancellationToken = default)
    {
        int current = cargoBaselinePendingDepth.GetValueOrDefault(marketId);
        if (current <= 0)
        {
            return;
        }

        int remaining = current - 1;
        if (remaining > 0)
        {
            cargoBaselinePendingDepth[marketId] = remaining;
            return;
        }

        cargoBaselinePendingDepth.Remove(marketId);
        cargoBaselineReady.Add(marketId);
        if (
            !pendingCargoDeltas.Remove(
                marketId,
                out (Dictionary<string, int> Delta, DateTimeOffset? RecordedAt) pending
            )
            || pending.Delta.Count == 0
            || storedRavenApiKey is null
        )
        {
            return;
        }

        try
        {
            await ApplyFleetCarrierCargoAdjustmentAsync(
                marketId,
                pending.Delta,
                "queued dock baseline",
                true,
                null,
                pending.RecordedAt,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            FleetCarrierSyncStatus =
                "Queued Fleet Carrier updates are retained for reconciliation: " + exception.Message;
        }
    }

    /// <summary>Retires covered deltas only when their known journal event time is no later than the authoritative market snapshot.</summary>
    private void ReconcilePendingCargo(MarketSnapshot market)
    {
        var covered = market
            .Items.Where(item => item.Producer || !item.Consumer)
            .Select(item => item.Commodity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (
            ColonizationPendingCargoAdjustment pending in failedCargoAdjustments
                .Where(item =>
                    item.Owner == GetWriteOwner()
                    && item.MarketId == market.MarketId
                    && item.RecordedAt is { } recordedAt
                    && recordedAt <= market.Timestamp
                )
                .ToArray()
        )
        {
            foreach (string commodity in pending.Delta.Keys.Where(covered.Contains).ToArray())
            {
                pending.Delta.Remove(commodity);
            }
            if (pending.Delta.Count == 0)
            {
                failedCargoAdjustments.Remove(pending);
            }
        }
        SavePendingCargoAdjustments();
    }

    /// <summary>Invalidates dock readiness without discarding transactions owned by an in-flight baseline.</summary>
    private void ClearCargoBaseline(long marketId)
    {
        cargoBaselineReady.Remove(marketId);
    }

    private void ClearAllCargoBaselines()
    {
        cargoBaselinePendingDepth.Clear();
        cargoBaselineReady.Clear();
        pendingCargoDeltas.Clear();
    }

    /// <summary>Returns the dock captured for this event, including a captured undocked state.</summary>
    private ColonizationDockingSnapshot? GetJournalDock(JournalEventEnvelope journalEvent) =>
        journalDocks.TryGetValue(journalEvent, out ColonizationDockingSnapshot? dock)
            ? dock
            : constructionState.CurrentDock;

    /// <summary>Resolves a known dock for suppression after a failed market adjustment.</summary>
    private ColonizationDockingSnapshot? GetJournalDockForMarket(long marketId) =>
        journalDocks.Values.FirstOrDefault(dock => dock?.MarketId == marketId) ?? constructionState.CurrentDock;

    /// <summary>Identifies pending writes by commander profile so they cannot migrate to another commander.</summary>
    private string GetWriteOwner() => string.Join("|", CommanderName, profileFrontierId, profileIsOdyssey);

    /// <summary>Runs one requested workspace refresh after the current busy operation releases ownership.</summary>
    private async Task RefreshPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!refreshPending)
        {
            return;
        }
        refreshPending = false;
        await RefreshAsync(cancellationToken);
    }

    /// <summary>Retries retained work on idle polls while protecting ownership and preventing overlapping recovery passes.</summary>
    public async Task<string?> RetryPendingWritesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || CommanderName is null || retryingWrites || utcNow() < nextWriteRetry)
        {
            return null;
        }
        retryingWrites = true;
        NotifyContributionRecovery();
        try
        {
            string owner = GetWriteOwner();
            int version = profileVersion;
            nextWriteRetry = utcNow().AddSeconds(5);
            string? cargoMessage = await RetryCargoAdjustmentsAsync(owner, version, cancellationToken);
            if (version != profileVersion)
            {
                return null;
            }
            var messages = new List<string>();
            if (cargoMessage is not null)
            {
                messages.Add(cargoMessage);
            }
            foreach (
                ColonizationPendingContribution pending in pendingContributions
                    .Where(item => item.Owner == owner)
                    .ToArray()
            )
            {
                if (contributionsInFlight.Contains(pending.EventId))
                {
                    continue;
                }
                string? message = await RetryContributionAsync(pending, cancellationToken);
                if (version != profileVersion)
                {
                    return null;
                }
                if (message is not null)
                {
                    messages.Add(message);
                }
            }
            return messages.Count > 0 ? string.Join(Environment.NewLine, messages.Distinct()) : null;
        }
        finally
        {
            retryingWrites = false;
            NotifyContributionRecovery();
        }
    }

    /// <summary>Reconciles failed cargo writes in order, holding dependent writes while allowing other carriers to recover.</summary>
    private async Task<string?> RetryCargoAdjustmentsAsync(
        string owner,
        int version,
        CancellationToken cancellationToken
    )
    {
        var blocked = new HashSet<long>();
        var messages = new List<string>();
        foreach (
            ColonizationPendingCargoAdjustment pending in failedCargoAdjustments
                .Where(item => item.Owner == owner)
                .ToArray()
        )
        {
            if (
                storedRavenApiKey is null
                || blocked.Contains(pending.MarketId)
                || IsCargoBaselinePending(pending.MarketId)
                || cargoWritesInFlight.Contains((owner, pending.MarketId))
            )
            {
                continue;
            }
            string? message = await ReconcileCargoAdjustmentAsync(pending, version, cancellationToken);
            if (version != profileVersion)
            {
                return null;
            }
            if (message is not null)
            {
                blocked.Add(pending.MarketId);
                messages.Add(message);
            }
        }
        return messages.Count > 0 ? string.Join(Environment.NewLine, messages.Distinct()) : null;
    }

    /// <summary>Checks a relative write against its prior baseline, recognizing applied results and refusing conflicting remote state.</summary>
    private async Task<string?> ReconcileCargoAdjustmentAsync(
        ColonizationPendingCargoAdjustment pending,
        int version,
        CancellationToken cancellationToken
    )
    {
        try
        {
            ColonizationFleetCarrier? remote = await client.GetFleetCarrierAsync(pending.MarketId, cancellationToken);
            if (
                version != profileVersion
                || !failedCargoAdjustments.Contains(pending)
                || IsCargoBaselinePending(pending.MarketId)
            )
            {
                return null;
            }
            if (remote is null || (pending.Attempted && pending.Before is null))
            {
                return "Fleet Carrier updates remain pending; refresh its market to reconcile an uncertain write.";
            }
            Dictionary<string, int> before = pending.Before ?? remote.Cargo;
            bool applied =
                pending.Attempted
                && pending.Delta.All(pair =>
                    remote.Cargo.GetValueOrDefault(pair.Key)
                    == Math.Max(0, before.GetValueOrDefault(pair.Key) + pair.Value)
                );
            string? blockedReason = GetCargoReplayBlockReason(pending, remote, applied);
            if (blockedReason is not null)
            {
                return blockedReason;
            }
            ColonizationPendingCargoAdjustment attempted = pending with
            {
                Attempted = true,
                OutcomeUnknown = true,
                Before = pending.Attempted
                    ? before
                    : new Dictionary<string, int>(remote.Cargo, StringComparer.OrdinalIgnoreCase),
            };
            failedCargoAdjustments[failedCargoAdjustments.IndexOf(pending)] = attempted;
            SavePendingCargoAdjustments();
            IReadOnlyDictionary<string, int> updated = applied
                ? remote.Cargo
                : await client.AdjustFleetCarrierCargoAsync(
                    pending.MarketId,
                    pending.Delta,
                    storedRavenApiKey!,
                    cancellationToken
                );
            failedCargoAdjustments.Remove(attempted);
            SavePendingCargoAdjustments();
            if (version == profileVersion)
            {
                ReplaceLocalFleetCarrier(
                    remote with
                    {
                        Cargo = updated.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.OrdinalIgnoreCase
                        ),
                    }
                );
            }
            return null;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            int index = failedCargoAdjustments.FindIndex(item =>
                item.Owner == pending.Owner
                && item.MarketId == pending.MarketId
                && ReferenceEquals(item.Delta, pending.Delta)
            );
            if (index >= 0)
            {
                failedCargoAdjustments[index] = failedCargoAdjustments[index] with
                {
                    OutcomeUnknown = !IsDefiniteRejection(exception),
                };
                SavePendingCargoAdjustments();
            }
            return "Fleet Carrier updates remain pending: " + exception.Message;
        }
    }

    /// <summary>Rejects concurrent or uncertain relative-write replay until a fresh market supplies an authoritative count.</summary>
    private static string? GetCargoReplayBlockReason(
        ColonizationPendingCargoAdjustment pending,
        ColonizationFleetCarrier remote,
        bool applied
    )
    {
        Dictionary<string, int> before = pending.Before ?? remote.Cargo;
        if (
            pending.Attempted
            && !applied
            && !pending.Delta.All(pair =>
                remote.Cargo.GetValueOrDefault(pair.Key) == before.GetValueOrDefault(pair.Key)
            )
        )
        {
            return "Fleet Carrier updates remain pending because Raven cargo changed concurrently. Refresh its market to reconcile.";
        }
        return pending.Attempted && pending.OutcomeUnknown && !applied
            ? "Fleet Carrier updates remain pending; refresh its market to reconcile an uncertain write."
            : null;
    }

    /// <summary>Retries a definitely rejected delivery and preserves ambiguous outcomes for explicit user reconciliation.</summary>
    private async Task<string?> RetryContributionAsync(
        ColonizationPendingContribution pending,
        CancellationToken cancellationToken,
        bool verifiedNotRecorded = false
    )
    {
        if (pending.OutcomeUnknown && !verifiedNotRecorded)
        {
            return "A construction contribution has an uncertain server outcome and is retained. Verify its credit on Raven before resubmitting.";
        }
        ColonizationPendingContribution sending = pending with { OutcomeUnknown = true };
        pendingContributions[pendingContributions.IndexOf(pending)] = sending;
        SavePendingContributions();
        try
        {
            await client.ContributeToProjectAsync(pending.BuildId, pending.Commander, pending.Cargo, cancellationToken);
            pendingContributions.Remove(sending);
            SavePendingContributions();
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            pendingContributions[pendingContributions.IndexOf(sending)] = sending with
            {
                OutcomeUnknown = !IsDefiniteRejection(exception),
            };
            SavePendingContributions();
            return "Construction contributions remain pending: " + exception.Message;
        }
    }

    /// <summary>Lists retained deliveries for the current profile so uncertain server outcomes can be checked on Raven.</summary>
    public string PendingContributionSummary =>
        string.Join(
            Environment.NewLine,
            pendingContributions
                .Where(item => item.Owner == GetWriteOwner())
                .Select(item =>
                    $"{item.Commander}: {item.Cargo.Values.Sum(value => (long)value):N0} units for {item.BuildId}: {string.Join(", ", item.Cargo.Select(pair => $"{pair.Key} {pair.Value:N0}"))} ({(item.OutcomeUnknown ? "unconfirmed" : "waiting to retry")})."
                )
        );

    /// <summary>Indicates that the current profile has deliveries requiring an explicit reconciliation decision.</summary>
    public bool HasUncertainContributions =>
        pendingContributions.Any(item => item.Owner == GetWriteOwner() && item.OutcomeUnknown);

    /// <summary>Offers individual uncertain deliveries for verification; checkbox choices are never persisted.</summary>
    public IReadOnlyList<ColonizationPendingContributionRowViewModel> UnconfirmedContributions =>
        unconfirmedContributions;

    /// <summary>Prevents recovery decisions from changing while a delivery upload or retry is active.</summary>
    public bool CanSelectUnconfirmedContributions => !retryingWrites && contributionsInFlight.Count == 0;

    /// <summary>Retries checked deliveries after the commander verifies that Raven did not record them.</summary>
    public ICommand RetryUnconfirmedContributionsCommand { get; }

    /// <summary>Removes checked deliveries from local recovery after their Raven credit is confirmed.</summary>
    public ICommand DismissConfirmedContributionsCommand { get; }

    /// <summary>Retries only checked deliveries verified absent on Raven, retaining every unselected delivery unchanged.</summary>
    public async Task RetryUnconfirmedContributionsAsync()
    {
        if (!IsEnabled || !CanReconcileSelectedContributions())
        {
            return;
        }
        ColonizationPendingContribution[] selected = GetSelectedUnconfirmedContributions();
        int version = profileVersion;
        retryingWrites = true;
        nextWriteRetry = utcNow().AddSeconds(5);
        foreach (ColonizationPendingContributionRowViewModel row in unconfirmedContributions)
        {
            row.IsSelected = false;
        }
        NotifyContributionRecovery();
        try
        {
            var messages = new List<string>();
            foreach (ColonizationPendingContribution pending in selected)
            {
                if (version != profileVersion || !IsEnabled)
                {
                    return;
                }
                string? message = await RetryContributionAsync(
                    pending,
                    CancellationToken.None,
                    verifiedNotRecorded: true
                );
                if (message is not null)
                {
                    messages.Add(message);
                }
            }
            if (version == profileVersion)
            {
                StatusMessage =
                    messages.Count > 0
                        ? string.Join(Environment.NewLine, messages.Distinct())
                        : "Retried the selected construction deliveries.";
            }
        }
        finally
        {
            retryingWrites = false;
            NotifyContributionRecovery();
        }
    }

    /// <summary>Clears only checked deliveries verified credited on Raven without sending any server request.</summary>
    public void DismissConfirmedContributions()
    {
        if (!CanReconcileSelectedContributions())
        {
            return;
        }
        foreach (ColonizationPendingContribution pending in GetSelectedUnconfirmedContributions())
        {
            pendingContributions.Remove(pending);
        }
        SavePendingContributions();
    }

    /// <summary>Requires an explicit checkbox choice and excludes concurrent delivery or retry activity.</summary>
    private bool CanReconcileSelectedContributions() =>
        CanSelectUnconfirmedContributions && unconfirmedContributions.Any(row => row.IsSelected);

    /// <summary>Captures selected journal identities from only the active commander's uncertain deliveries.</summary>
    private ColonizationPendingContribution[] GetSelectedUnconfirmedContributions()
    {
        var selectedIds = unconfirmedContributions.Where(row => row.IsSelected).Select(row => row.EventId).ToHashSet();
        string owner = GetWriteOwner();
        return pendingContributions
            .Where(item => item.Owner == owner && item.OutcomeUnknown && selectedIds.Contains(item.EventId))
            .ToArray();
    }

    /// <summary>Preserves ordered relative writes across restart without persisting credentials.</summary>
    private void SavePendingCargoAdjustments()
    {
        try
        {
            settingsStore.SavePendingCargoAdjustments(failedCargoAdjustments);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FleetCarrierSyncStatus =
                "Cargo updates are retained in memory, but their recovery file could not be saved: "
                + exception.Message;
        }
    }

    /// <summary>Persists delivery recovery state without credentials and updates the recovery controls.</summary>
    private void SavePendingContributions()
    {
        try
        {
            settingsStore.SavePendingContributions(pendingContributions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage =
                "Construction deliveries are retained in memory, but their recovery file could not be saved: "
                + exception.Message;
        }
        NotifyContributionRecovery();
    }

    /// <summary>Preserves current checkbox choices while refreshing deliveries, clearing them when profile ownership changes.</summary>
    private void NotifyContributionRecovery()
    {
        string owner = GetWriteOwner();
        IReadOnlyList<ColonizationPendingContributionRowViewModel> previous =
            contributionRecoveryOwner == owner ? unconfirmedContributions : [];
        contributionRecoveryOwner = owner;
        unconfirmedContributions = pendingContributions
            .Where(item => item.Owner == owner && item.OutcomeUnknown)
            .Select(item =>
                previous.FirstOrDefault(row => row.EventId == item.EventId)
                ?? new ColonizationPendingContributionRowViewModel(item, RaiseContributionRecoveryCommandStates)
            )
            .ToArray();
        OnPropertyChanged(nameof(UnconfirmedContributions));
        OnPropertyChanged(nameof(CanSelectUnconfirmedContributions));
        OnPropertyChanged(nameof(PendingContributionSummary));
        OnPropertyChanged(nameof(HasUncertainContributions));
        RaiseContributionRecoveryCommandStates();
    }

    /// <summary>Updates both recovery buttons when selection or upload availability changes.</summary>
    private void RaiseContributionRecoveryCommandStates()
    {
        (RetryUnconfirmedContributionsCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (DismissConfirmedContributionsCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>Only explicit client-error responses are safe for automatic contribution replay; server and transport failures may follow a committed write.</summary>
    private static bool IsDefiniteRejection(Exception exception) =>
        exception is RavenColonialServiceException { StatusCode: { } status }
        && (int)status is >= 400 and < 500
        && status != System.Net.HttpStatusCode.RequestTimeout;

    /// <summary>Loads the active commander workspace and services refreshes requested while an obsolete load was busy.</summary>
    public Task RefreshAsync()
    {
        return RefreshAsync(CancellationToken.None);
    }

    /// <summary>Loads the active commander workspace and services refreshes requested while an obsolete load was busy.</summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled || CommanderName is null)
        {
            return;
        }
        if (IsBusy)
        {
            refreshPending = true;
            return;
        }
        int version = profileVersion;
        string commander = CommanderName;
        IsBusy = true;

        StatusMessage = "Fetching active projects from Raven Colonial...";
        try
        {
            if (Projects.Count == 0)
            {
                await RestoreLegacyProfileAsync(cancellationToken: cancellationToken);
            }
            if (version != profileVersion)
            {
                return;
            }
            ColonizationCommanderProjects result = await client.GetCommanderProjectsAsync(commander, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (
                version != profileVersion
                || !string.Equals(CommanderName, commander, StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            hiddenProjectIds = result.HiddenProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            primaryProjectId = result.PrimaryProjectId;
            fleetCarriers = result.FleetCarriers;
            localUntrackedProject = null;
            Projects = result
                .Projects.OrderBy(project => project.SystemName)
                .ThenBy(project => project.BuildName)
                .Select(CreateRow)
                .ToArray();
            HasUnsavedProjectVisibility = false;
            UpdateProjectSummary();
            StatusMessage = Projects.Count switch
            {
                0 => "No active Raven Colonial projects were found for this commander.",
                1 => "Loaded 1 active Raven Colonial project.",
                _ => $"Loaded {Projects.Count:N0} active Raven Colonial projects.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            if (version == profileVersion)
            {
                StatusMessage = "Project refresh failed without changing your selection: " + exception.Message;
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(cancellationToken: cancellationToken);
        }
    }

    /// <summary>Restores legacy preferences only while the initiating commander profile remains active.</summary>
    private async Task RestoreLegacyProfileAsync(CancellationToken cancellationToken = default)
    {
        if (legacyProfileStore is null || profileFrontierId is null || CommanderName is null)
        {
            return;
        }

        int version = profileVersion;
        LegacyColonizationProfileLoadResult result = await legacyProfileStore.LoadAsync(
            profileFrontierId,
            cancellationToken
        );
        if (version != profileVersion)
        {
            return;
        }
        if (result.Error is not null)
        {
            StatusMessage = "The imported colonization cache could not be read: " + result.Error;
            return;
        }

        if (result.Snapshot is not { } snapshot)
        {
            return;
        }

        hiddenProjectIds = snapshot.HiddenProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        primaryProjectId = snapshot.PrimaryProjectId;
        fleetCarriers = snapshot.FleetCarriers;
        Projects = snapshot
            .Projects.OrderBy(project => project.SystemName)
            .ThenBy(project => project.BuildName)
            .Select(CreateRow)
            .ToArray();
        HasUnsavedProjectVisibility = false;
        UpdateProjectSummary();
        string warning =
            result.Warnings.Count == 0 ? string.Empty : $" Ignored {result.Warnings.Count:N0} invalid cached item(s).";
        StatusMessage =
            $"Restored {Projects.Count:N0} imported colonization "
            + $"project(s) from {Path.GetFileName(result.Path)}.{warning}";
    }

    /// <summary>Saves visibility for a captured commander and prevents a stale response replacing another workspace.</summary>
    public async Task SaveProjectVisibilityAsync()
    {
        if (!IsEnabled || IsBusy || CommanderName is null || !HasUnsavedProjectVisibility)
        {
            return;
        }

        int version = profileVersion;
        IsBusy = true;
        StatusMessage = "Saving project visibility to Raven Colonial...";
        try
        {
            IReadOnlyList<string> saved = await client.SaveHiddenProjectIdsAsync(
                CommanderName,
                hiddenProjectIds,
                CancellationToken.None
            );
            if (version != profileVersion)
            {
                return;
            }
            hiddenProjectIds = saved.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (ColonizationProjectRowViewModel row in Projects)
            {
                row.UpdateShown(!hiddenProjectIds.Contains(row.Project.BuildId));
            }

            HasUnsavedProjectVisibility = false;
            UpdateProjectSummary();
            StatusMessage = "Project visibility saved to Raven Colonial.";
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            if (version != profileVersion)
            {
                return;
            }
            StatusMessage = "Project visibility was not saved: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(CancellationToken.None);
        }
    }

    private ColonizationProjectRowViewModel CreateRow(ColonizationProject project)
    {
        IReadOnlyList<ColonizationBuildCost> matchingBuilds = buildCatalog.FindByLayout(project.BuildType);
        ColonizationBuildCost? build =
            (matchingBuilds.Count > 0 ? matchingBuilds[0] : null) ?? buildCatalog.FindByBuildType(project.BuildType);
        string fleetCarrierType = project.IsFleetCarrierLoading ? "Fleet Carrier loading" : project.BuildType;
        string type = build is null ? fleetCarrierType : $"{build.DisplayName} ({project.BuildType})";
        return new ColonizationProjectRowViewModel(
            project,
            type,
            string.Equals(project.BuildId, primaryProjectId, StringComparison.OrdinalIgnoreCase),
            !hiddenProjectIds.Contains(project.BuildId),
            OnProjectShownChanged,
            TogglePrimaryProjectAsync
        );
    }

    /// <summary>Changes the captured commander's primary project without applying results to a replacement profile.</summary>
    public async Task TogglePrimaryProjectAsync(ColonizationProjectRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsEnabled || IsBusy || CommanderName is null)
        {
            return;
        }

        string? nextPrimaryId = row.IsPrimary ? null : row.Project.BuildId;
        int version = profileVersion;
        IsBusy = true;
        StatusMessage = nextPrimaryId is null
            ? "Clearing the primary Raven Colonial project..."
            : $"Setting {row.BuildName} as the primary Raven Colonial project...";
        try
        {
            await client.SetPrimaryProjectAsync(CommanderName, nextPrimaryId, CancellationToken.None);
            if (version != profileVersion)
            {
                return;
            }
            primaryProjectId = nextPrimaryId;
            Projects = Projects
                .Select(project => project.Project)
                .OrderBy(project => project.SystemName)
                .ThenBy(project => project.BuildName)
                .Select(CreateRow)
                .ToArray();
            StatusMessage = nextPrimaryId is null
                ? "The primary Raven Colonial project was cleared."
                : $"{row.BuildName} is now the primary Raven Colonial project.";
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            if (version != profileVersion)
            {
                return;
            }
            StatusMessage = "The primary project was not changed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(CancellationToken.None);
        }
    }

    /// <summary>Installs a successfully created project before optional cleanup and ignores obsolete callback results.</summary>
    private async Task OnProjectCreatedAsync(ColonizationProject project)
    {
        InvalidateProjectLocationCache();
        lastDepotPatchPayloadSignature = null;
        int version = profileVersion;
        UpsertProject(project);
        project = await ClearPhantomCommoditiesAsync(project, CancellationToken.None);
        if (version != profileVersion)
        {
            return;
        }
        Projects = Projects
            .Where(row => !string.Equals(row.Project.BuildId, project.BuildId, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Project)
            .Append(project)
            .OrderBy(candidate => candidate.SystemName)
            .ThenBy(candidate => candidate.BuildName)
            .Select(CreateRow)
            .ToArray();
        UpdateProjectSummary();
    }

    private void SetCurrentBody(int? bodyId, string? bodyName, string? commanderName)
    {
        if (
            currentBodyId == bodyId
            && string.Equals(currentBodyName, bodyName, StringComparison.Ordinal)
            && string.Equals(currentBodyCommanderName, commanderName, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        currentBodyId = bodyId;
        currentBodyName = bodyName;
        currentBodyCommanderName = bodyId is null ? null : commanderName;
        UpdateProjectEditorContext();
    }

    private void RememberJournalBody(JournalEventEnvelope journalEvent, string? commanderName)
    {
        if (ColonizationBodyJournal.ClearsCurrentBody(journalEvent.EventName))
        {
            SetCurrentBody(null, null, null);
            return;
        }

        if (!ColonizationBodyJournal.ReportsCurrentBody(journalEvent.EventName))
        {
            return;
        }

        int? bodyId = ColonizationBodyJournal.ReadBodyId(journalEvent.Payload);
        if (bodyId is not >= 0)
        {
            return;
        }

        SetCurrentBody(bodyId, ColonizationBodyJournal.ReadBodyName(journalEvent.Payload), commanderName);
    }

    private void UpdateProjectEditorContext()
    {
        ColonizationConstructionSnapshot snapshot = constructionState.CreateSnapshot();
        ProjectEditor.UpdateContext(
            new ColonizationProjectEditorContext(
                IsEnabled,
                CommanderName,
                currentSystemName,
                currentStarPosition,
                snapshot.CurrentDock,
                snapshot.CurrentDepot,
                storedRavenApiKey,
                currentBodyId,
                currentBodyName
            )
        );
    }

    private void UpdateSystemEditorContext()
    {
        SystemEditor.UpdateContext(
            new ColonizationSystemEditorContext(
                IsEnabled,
                CommanderName,
                currentSystemName,
                currentSystemAddress,
                storedRavenApiKey
            )
        );
    }

    private void OnProjectShownChanged(ColonizationProjectRowViewModel row, bool isShown)
    {
        if (isShown)
        {
            hiddenProjectIds.Remove(row.Project.BuildId);
        }
        else
        {
            hiddenProjectIds.Add(row.Project.BuildId);
        }

        HasUnsavedProjectVisibility = true;
        UpdateProjectSummary();
    }

    private void UpdateProjectSummary()
    {
        ColonizationProjectTotals totals = ColonizationProjectCalculator.CalculateTotals(
            Projects.Select(row => row.Project),
            hiddenProjectIds,
            constructionState.ShipCargoCapacity
        );
        string trips = totals.TripsInCurrentShip is long tripCount
            ? $" | {tripCount:N0} trips in current ship"
            : string.Empty;
        ProjectSummary = $"Cargo required: {totals.RemainingCargo:N0}" + trips;
        UpdateCommodityPlan();
    }

    private void UpdateConstructionDisplay()
    {
        ColonizationConstructionSnapshot snapshot = constructionState.CreateSnapshot();
        if (snapshot.CurrentDock is null)
        {
            ConstructionTitle = "No construction depot active";
            ConstructionStatus = "Dock at a construction site and open Construction Services.";
            ConstructionResources = [];
            return;
        }

        ConstructionTitle = snapshot.CurrentDock.StationName;
        if (snapshot.CurrentDepot is null)
        {
            ConstructionStatus = snapshot.CurrentDock.IsConstructionSite
                ? "Open Construction Services to load current requirements."
                : "The current station is not a colonization construction site.";
            ConstructionResources = [];
            return;
        }

        ColonizationConstructionDepotSnapshot depot = snapshot.CurrentDepot;
        ConstructionStatus = depot.IsComplete
            ? "Construction complete."
            : (depot.IsFailed) switch
            {
                true => "Construction failed.",
                false => $"{depot.ReportedProgress:P1} complete | " + $"{depot.TotalRemaining:N0} cargo remaining",
            };
        ConstructionResources = depot
            .Resources.OrderByDescending(resource => resource.RemainingAmount)
            .ThenBy(resource => resource.LocalizedName)
            .Select(resource => new ColonizationResourceRowViewModel(
                resource.LocalizedName,
                resource.RemainingAmount,
                resource.ProvidedAmount,
                resource.RequiredAmount,
                resource.Payment
            ))
            .ToArray();
    }

    private void ClearProjects()
    {
        Projects = [];
        fleetCarriers = [];
        localUntrackedProject = null;
        hiddenProjectIds.Clear();
        primaryProjectId = null;
        HasUnsavedProjectVisibility = false;
        UpdateProjectSummary();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
    }

    private void UpdateCommodityPlan()
    {
        OnPropertyChanged(nameof(LinkedFleetCarriers));
        OnPropertyChanged(nameof(DetectedSquadronCarrierMarketId));
        ColonizationConstructionSnapshot construction = constructionState.CreateSnapshot();
        ColonizationDockingSnapshot? dock = construction.CurrentDock;
        bool hasMarketSinceDocking =
            currentMarket is not null
            && dock?.Timestamp is not null
            && dock.MarketId == currentMarket.MarketId
            && currentMarket.Timestamp > dock.Timestamp;
        CommodityOverlay.Apply(
            ColonizationCommodityPlanner.Create(
                new ColonizationCommodityPlanRequest
                {
                    Projects = Projects.Select(row => row.Project),
                    HiddenBuildIds = hiddenProjectIds,
                    PrimaryBuildId = primaryProjectId,
                    CommanderName = CommanderName,
                    FleetCarriers = fleetCarriers,
                    ShipCargo = shipCargo,
                    Construction = construction,
                    Market = currentMarket,
                }
            ),
            latestStatus,
            hasMarketSinceDocking,
            construction.IsSquadronBankOpen
        );
    }

    private string GetShipCargoReadyStatus()
    {
        if (SharedCargoSuppressed)
        {
            return "Ship cargo is paused while multiple Elite windows are running because Cargo.json cannot be attributed safely.";
        }

        if (!ShipCargoPublishingEnabled)
        {
            return "Automatic ship cargo publishing is off.";
        }

        if (!IsEnabled)
        {
            return "Enable Raven Colonial before publishing ship cargo.";
        }

        return HasStoredRavenApiKey
            ? "Ship cargo will publish after Cargo.json changes."
            : "Save a Raven API key before ship cargo can publish.";
    }

    private bool CanSaveRavenApiKey()
    {
        string? normalized = string.IsNullOrWhiteSpace(RavenApiKey) ? null : RavenApiKey.Trim();
        return commanderProfileStore is not null
            && profileFrontierId is not null
            && !IsFleetCarrierSyncBusy
            && !string.Equals(normalized, storedRavenApiKey, StringComparison.Ordinal);
    }

    private bool CanPublishCurrentFleetCarrier()
    {
        return IsEnabled
            && HasStoredRavenApiKey
            && !IsFleetCarrierSyncBusy
            && constructionState.CurrentDock is { MarketId: > 0, StationType: not null } dock
            && string.Equals(dock.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase);
    }

    private string GetFleetCarrierPublishBlockReason()
    {
        if (!IsEnabled)
        {
            return "Enable Raven Colonial before publishing a Fleet Carrier.";
        }

        if (!HasStoredRavenApiKey)
        {
            return "Save a Raven API key before publishing a Fleet Carrier.";
        }

        return "Dock at the Fleet Carrier you want to publish and link.";
    }

    private MarketSnapshot? GetFreshFleetCarrierMarket(ColonizationDockingSnapshot dock)
    {
        return
            currentMarket is not null
            && dock.Timestamp is not null
            && currentMarket.MarketId == dock.MarketId
            && string.Equals(currentMarket.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase)
            && currentMarket.Timestamp > dock.Timestamp
            ? currentMarket
            : null;
    }

    private bool CanSyncFleetCarrierCargo()
    {
        if (
            !IsEnabled
            || !FleetCarrierCargoSyncEnabled
            || !HasStoredRavenApiKey
            || IsFleetCarrierSyncBusy
            || currentMarket is null
            || !string.Equals(currentMarket.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase)
            || !fleetCarriers.Any(carrier => carrier.MarketId == currentMarket.MarketId)
        )
        {
            return false;
        }

        ColonizationDockingSnapshot? dock = constructionState.CurrentDock;
        return dock?.Timestamp is not null
            && dock.MarketId == currentMarket.MarketId
            && currentMarket.Timestamp > dock.Timestamp;
    }

    private string GetFleetCarrierSyncBlockReason()
    {
        if (!IsEnabled)
        {
            return "Enable Raven Colonial before syncing Fleet Carrier cargo.";
        }

        if (!FleetCarrierCargoSyncEnabled)
        {
            return FleetCarrierSyncOffMessage;
        }

        if (!HasStoredRavenApiKey)
        {
            return "Save a Raven API key before syncing Fleet Carrier cargo.";
        }

        if (currentMarket is null)
        {
            return "Open a Fleet Carrier commodity market in Elite first.";
        }

        if (!string.Equals(currentMarket.StationType, FleetCarrierStationType, StringComparison.OrdinalIgnoreCase))
        {
            return "The current market is not a Fleet Carrier market.";
        }

        if (!fleetCarriers.Any(carrier => carrier.MarketId == currentMarket.MarketId))
        {
            return "The current Fleet Carrier is not linked to this commander in Raven Colonial.";
        }

        return "Dock at the Fleet Carrier and reopen its commodity market before syncing.";
    }

    private string? GetShipCargoPublishingBlockReason()
    {
        if (!IsEnabled)
        {
            return "Enable Raven Colonial before publishing ship cargo.";
        }

        if (storedRavenApiKey is null)
        {
            return "Save a Raven API key before ship cargo can publish.";
        }

        if (CommanderName is null)
        {
            return "Load a commander profile before publishing ship cargo.";
        }

        if (!Projects.Any(project => project.IsShown))
        {
            return "Ship cargo was not published because no visible colonization projects are active.";
        }

        if (string.IsNullOrWhiteSpace(currentShipType))
        {
            return "Ship cargo is waiting for a Loadout journal event.";
        }

        return null;
    }

    private void ApplyShipIdentity(JournalEventEnvelope journalEvent)
    {
        JsonElement root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "LoadGame":
            case "Loadout":
                currentShipType = GetJournalString(root, "Ship") ?? currentShipType;
                currentShipName =
                    GetJournalString(root, "ShipName") ?? GetJournalString(root, "ShipIdent") ?? currentShipName;
                break;

            case "ShipyardSwap":
                currentShipType = GetJournalString(root, "ShipType") ?? currentShipType;
                break;
        }
    }

    private static string? GetJournalString(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private static long? GetJournalInt64(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long result)
            ? result
            : null;
    }

    private static bool? GetJournalBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? CombineMessages(params string?[] messages)
    {
        string?[] present = messages.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }

    private static int? GetJournalInt32(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private void ReplaceLocalFleetCarrier(ColonizationFleetCarrier updatedCarrier)
    {
        fleetCarriers = fleetCarriers
            .Where(carrier => carrier.MarketId != updatedCarrier.MarketId)
            .Append(updatedCarrier)
            .ToArray();
        UpdateCommodityPlan();
    }

    private static string GetCarrierName(ColonizationFleetCarrier carrier)
    {
        return string.IsNullOrWhiteSpace(carrier.DisplayName) ? carrier.Name : carrier.DisplayName;
    }

    private void SaveOverlayPreferences(
        ColonizationOverlayPreferences updatedPreferences,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (updatedPreferences == overlayPreferences)
        {
            return;
        }

        try
        {
            settingsStore.SaveOverlayPreferences(updatedPreferences);
            overlayPreferences = updatedPreferences;
            CommodityOverlay.ApplyPreferences(updatedPreferences);
            OnPropertyChanged(propertyName);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "The construction overlay preference could not be saved: " + exception.Message;
        }
    }

    /// <summary>Refreshes workspace and recovery action availability after preferences or commander context change.</summary>
    private void RaiseCommandStates()
    {
        refreshCommand.RaiseCanExecuteChanged();
        saveProjectsCommand.RaiseCanExecuteChanged();
        saveRavenApiKeyCommand.RaiseCanExecuteChanged();
        publishFleetCarrierCommand.RaiseCanExecuteChanged();
        syncFleetCarrierCargoCommand.RaiseCanExecuteChanged();
        RaiseContributionRecoveryCommandStates();
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

        public bool CanExecute(object? parameter) => canExecute();

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

public sealed class ColonizationProjectRowViewModel : INotifyPropertyChanged
{
    private readonly Action<ColonizationProjectRowViewModel, bool> changed;
    private bool isShown;

    public ColonizationProjectRowViewModel(
        ColonizationProject project,
        string typeDescription,
        bool isPrimary,
        bool isShown,
        Action<ColonizationProjectRowViewModel, bool> changed,
        Func<ColonizationProjectRowViewModel, Task> togglePrimary
    )
    {
        Project = project;
        TypeDescription = typeDescription;
        IsPrimary = isPrimary;
        this.isShown = isShown;
        this.changed = changed;
        TogglePrimaryCommand = new RowAsyncCommand(() => togglePrimary(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ColonizationProject Project { get; }

    public string BuildName => Project.BuildName;

    public string SystemName => Project.SystemName;

    public string SystemAddressText => SystemAddressFormatter.Format(Project.SystemAddress);

    public string TypeDescription { get; }

    public bool IsPrimary { get; }

    public string PrimaryLabel => IsPrimary ? "PRIMARY" : string.Empty;

    public string PrimaryActionLabel => IsPrimary ? "Clear primary" : "Make primary";

    public ICommand TogglePrimaryCommand { get; }

    public string ProgressText =>
        Project.IsFleetCarrierLoading
            ? $"? of {Project.MaximumRequired:N0}"
            : Project.Progress switch
            {
                double progress => (progress * 100).ToString("0", CultureInfo.InvariantCulture)
                    + "% of "
                    + Project.MaximumRequired.ToString("N0", CultureInfo.CurrentCulture),
                null => "Progress unavailable",
            };

    public bool IsShown
    {
        get => isShown;
        set
        {
            if (value == isShown)
            {
                return;
            }

            isShown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
            changed(this, value);
        }
    }

    internal void UpdateShown(bool value)
    {
        if (value == isShown)
        {
            return;
        }

        isShown = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
    }

    private sealed class RowAsyncCommand(Func<Task> execute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !isExecuting;

        public async void Execute(object? parameter)
        {
            if (isExecuting)
            {
                return;
            }

            isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await execute();
            }
            finally
            {
                isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

public sealed record ColonizationResourceRowViewModel(
    string Name,
    int Remaining,
    int Provided,
    int Required,
    int Payment
)
{
    public string RemainingText => $"{Remaining:N0} remaining";

    public string ProgressText => $"{Provided:N0} / {Required:N0}";

    public string PaymentText => $"{Payment:N0} CR/t";
}
