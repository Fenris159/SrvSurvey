using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

/// <summary>
/// Synchronizes colonization deliveries with Raven Colonial from journal events and recovers writes whose
/// outcome was lost: contributions are never credited twice, carrier cargo deltas reconcile by journal time
/// against market baselines, and retained work retries in order for its originating commander profile only.
/// </summary>
public sealed partial class ColonizationDeliveryRecovery
{
    private const string FleetCarrierStationType = "FleetCarrier";

    private readonly IRavenColonialClient client;
    private readonly IColonizationDeliveryRecoveryStore store;
    private readonly IColonizationDeliveryObserver observer;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly ColonizationConstructionState constructionState = new();
    private readonly ColonizationFleetCarrierIdentityTracker fleetCarrierIdentityTracker = new();
    private readonly Dictionary<JournalEventEnvelope, ColonizationDockingSnapshot?> journalDocks = new(
        ReferenceEqualityComparer.Instance
    );
    private ColonizationProject[] projects = [];
    private IReadOnlyList<ColonizationFleetCarrier> fleetCarriers = [];
    private ColonizationProject? localUntrackedProject;
    private MarketSnapshot? currentMarket;
    private bool inMainShip;
    private string? commanderName;
    private string? frontierId;
    private bool isOdyssey = true;
    private string? apiKey;
    private int profileVersion;
    private bool isEnabled;
    private string? currentSystemName;
    private long? currentSystemAddress;
    private int? currentBodyId;
    private string? currentBodyName;
    private string? currentBodyCommanderName;
    private long? detectedSquadronCarrierMarketId;
    private string? detectedSquadronCommander;

    /// <summary>Restores retained deliveries, carrier writes, and repair guards from the recovery store.</summary>
    public ColonizationDeliveryRecovery(
        IRavenColonialClient client,
        IColonizationDeliveryRecoveryStore store,
        IColonizationDeliveryObserver observer,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<DateTimeOffset>? utcNow = null
    )
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
        this.delayAsync = delayAsync ?? Task.Delay;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        pendingContributions.AddRange(store.LoadPendingContributions());
        failedCargoAdjustments.AddRange(store.LoadPendingCargoAdjustments());
        foreach (ColonizationBuildSiteRepairVisit visit in store.LoadBuildSiteRepairVisits())
        {
            buildSiteRepairVisits.Enqueue(visit);
            buildSiteRepairVisitSet.Add(visit);
        }
    }

    /// <summary>Allows Raven reads and writes; disabling drops the docking repair warning.</summary>
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            isEnabled = value;
            if (!value)
            {
                InvalidateBuildSiteRepairContext();
            }
        }
    }

    /// <summary>Allows linked carrier cargo to follow journal transactions and market snapshots.</summary>
    public bool FleetCarrierCargoSyncEnabled { get; set; }

    /// <summary>The active commander, or null when no commander profile is loaded.</summary>
    public string? CommanderName => commanderName;

    /// <summary>The Frontier identity of the active commander profile.</summary>
    public string? FrontierId => frontierId;

    /// <summary>Whether the active commander profile is an Odyssey profile.</summary>
    public bool IsOdyssey => isOdyssey;

    /// <summary>The saved Raven API key, or null when the commander has none.</summary>
    public string? ApiKey => apiKey;

    /// <summary>Changes whenever commander or profile identity changes so stale asynchronous results can be ignored.</summary>
    public int ProfileVersion => profileVersion;

    /// <summary>Identifies which retained writes belong to the active commander profile.</summary>
    public string RecoveryOwner => string.Join("|", commanderName, frontierId, isOdyssey);

    /// <summary>The commander's projects ordered by system then build name.</summary>
    public IReadOnlyList<ColonizationProject> Projects => projects;

    /// <summary>The commander's linked carriers; the instance changes only when a carrier changes.</summary>
    public IReadOnlyList<ColonizationFleetCarrier> FleetCarriers => fleetCarriers;

    /// <summary>The most recent Market.json snapshot.</summary>
    public MarketSnapshot? CurrentMarket => currentMarket;

    /// <summary>The station the commander is docked at, if any.</summary>
    public ColonizationDockingSnapshot? CurrentDock => constructionState.CurrentDock;

    /// <summary>The current ship's cargo capacity reported by the journal.</summary>
    public int ShipCargoCapacity => constructionState.ShipCargoCapacity;

    /// <summary>The squadron carrier the journal commander last docked at.</summary>
    public long? DetectedSquadronCarrierMarketId => detectedSquadronCarrierMarketId;

    /// <summary>The active system's name.</summary>
    public string? CurrentSystemName => currentSystemName;

    /// <summary>The active system's address.</summary>
    public long? CurrentSystemAddress => currentSystemAddress;

    /// <summary>The body the commander is on or orbiting, if reported by the journal.</summary>
    public int? CurrentBodyId => currentBodyId;

    /// <summary>The name of <see cref="CurrentBodyId"/>.</summary>
    public string? CurrentBodyName => currentBodyName;

    /// <summary>Captures the live dock, depot, and ship state.</summary>
    public ColonizationConstructionSnapshot CreateConstructionSnapshot() => constructionState.CreateSnapshot();

    /// <summary>Switches commander, discarding commander-scoped session state and the repair warning.</summary>
    public void SetCommander(string? normalizedCommanderName)
    {
        profileVersion++;
        InvalidateBuildSiteRepairContext();
        if (!string.Equals(detectedSquadronCommander, normalizedCommanderName, StringComparison.OrdinalIgnoreCase))
        {
            detectedSquadronCarrierMarketId = null;
        }
        commanderName = normalizedCommanderName;
        if (!string.Equals(currentBodyCommanderName, normalizedCommanderName, StringComparison.OrdinalIgnoreCase))
        {
            currentBodyId = null;
            currentBodyName = null;
            currentBodyCommanderName = null;
        }
        ClearCapiCargoSeedSession();
        ClearAllCargoBaselines();
        ClearWorkspace();
    }

    /// <summary>Updates credentials and invalidates requests owned by a superseded Frontier profile.</summary>
    public void SetProfile(string? profileFrontierId, bool profileIsOdyssey, string? profileApiKey)
    {
        if (frontierId != profileFrontierId?.Trim() || isOdyssey != profileIsOdyssey || apiKey != profileApiKey?.Trim())
        {
            profileVersion++;
        }
        frontierId = string.IsNullOrWhiteSpace(profileFrontierId) ? null : profileFrontierId.Trim();
        isOdyssey = profileIsOdyssey;
        apiKey = string.IsNullOrWhiteSpace(profileApiKey) ? null : profileApiKey.Trim();
        lastSyncedMarket = null;
    }

    /// <summary>Applies a newly saved key to the current profile without invalidating in-flight work.</summary>
    public void UpdateApiKey(string? normalizedApiKey)
    {
        apiKey = normalizedApiKey;
    }

    /// <summary>
    /// Updates the active system, expiring the repair warning and body when the system changes. A position-only
    /// change still stores the latest spelling. Returns false when nothing changed.
    /// </summary>
    public bool UpdateSystemContext(string? systemName, long? systemAddress, bool positionChanged)
    {
        string? nextSystemName = string.IsNullOrWhiteSpace(systemName) ? null : systemName.Trim();
        long? nextSystemAddress = systemAddress is > 0 ? systemAddress : null;
        if (
            string.Equals(currentSystemName, nextSystemName, StringComparison.OrdinalIgnoreCase)
            && currentSystemAddress == nextSystemAddress
            && !positionChanged
        )
        {
            return false;
        }

        if (
            !string.Equals(currentSystemName, nextSystemName, StringComparison.OrdinalIgnoreCase)
            || (currentSystemAddress is > 0 && nextSystemAddress is > 0 && currentSystemAddress != nextSystemAddress)
        )
        {
            InvalidateBuildSiteRepairContext();
            currentBodyId = null;
            currentBodyName = null;
            currentBodyCommanderName = null;
        }

        currentSystemName = nextSystemName;
        currentSystemAddress = nextSystemAddress;
        return true;
    }

    /// <summary>Records whether the commander is in the main ship, which decides how transfers affect carriers.</summary>
    public void UpdateStatus(EliteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        inMainShip = status.InMainShip;
    }

    /// <summary>Records the latest Market.json snapshot used for carrier cargo synchronization.</summary>
    public void UpdateMarket(MarketSnapshot market)
    {
        currentMarket = market ?? throw new ArgumentNullException(nameof(market));
    }

    /// <summary>Installs a freshly loaded commander workspace without notifying the observer.</summary>
    public void ReplaceWorkspace(
        IEnumerable<ColonizationProject> loadedProjects,
        IReadOnlyList<ColonizationFleetCarrier> loadedFleetCarriers
    )
    {
        ArgumentNullException.ThrowIfNull(loadedProjects);
        fleetCarriers = loadedFleetCarriers ?? throw new ArgumentNullException(nameof(loadedFleetCarriers));
        localUntrackedProject = null;
        projects = Sort(loadedProjects);
    }

    /// <summary>Drops the commander workspace without notifying the observer.</summary>
    public void ClearWorkspace()
    {
        projects = [];
        fleetCarriers = [];
        localUntrackedProject = null;
    }

    /// <summary>Installs a successfully created project before optional cleanup and ignores obsolete cleanup results.</summary>
    public async Task InstallCreatedProjectAsync(ColonizationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        InvalidateProjectLocationCache();
        lastDepotPatchPayloadSignature = null;
        int version = profileVersion;
        UpsertProject(project);
        project = await ClearPhantomCommoditiesAsync(project, CancellationToken.None);
        if (version != profileVersion)
        {
            return;
        }
        projects = Sort(
            projects
                .Where(candidate =>
                    !string.Equals(candidate.BuildId, project.BuildId, StringComparison.OrdinalIgnoreCase)
                )
                .Append(project)
        );
        observer.ProjectsChanged();
    }

    /// <summary>
    /// Applies journal context, capturing each event's dock, and expires repair warnings when docking moves
    /// to another known system. Returns whether the construction state changed.
    /// </summary>
    public bool ApplyJournalEvents(IReadOnlyList<JournalEventEnvelope> journalEvents, string? journalCommanderName)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        string? owner = journalCommanderName ?? commanderName;
        if (!string.Equals(detectedSquadronCommander, owner, StringComparison.OrdinalIgnoreCase))
        {
            detectedSquadronCarrierMarketId = null;
            detectedSquadronCommander = owner;
        }
        ColonizationDockingSnapshot? dockBefore = constructionState.CurrentDock;
        long before = constructionState.Version;
        journalDocks.Clear();
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            ApplyJournalEvent(journalEvent, owner);
        }

        ExpireBuildSiteRepairWarningAfterSystemChange(dockBefore);
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

        return constructionState.Version != before;
    }

    /// <summary>Synchronizes journal events using their event-time dock and retries retained writes during idle polls.</summary>
    public async Task<IReadOnlyList<ColonizationDeliveryNotice>> SynchronizeLiveEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool allowPublishing,
        CargoInventoryState? cargoInventory = null,
        bool cargoActivity = false,
        bool preferShipCargoDiffForSquadron = true,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!allowPublishing || !isEnabled || commanderName is null)
        {
            ClearSquadronCargoSyncState(cargoInventory);
            return [];
        }

        // When ship cargo is not current (or suppressed), squadron carriers use
        // journal transfer adjustments. Otherwise use the full GetDiff path.
        bool preferSquadronCargoDiff =
            cargoInventory is not null
            && preferShipCargoDiffForSquadron
            && journalEvents
                .Where(item => item.EventName is "MarketBuy" or "MarketSell" or "CargoTransfer")
                .All(item => GetJournalDock(item)?.MarketId == constructionState.CurrentDock?.MarketId);
        var notices = new List<ColonizationDeliveryNotice>();
        notices.AddRange(await RetryPendingWritesAsync(cancellationToken: cancellationToken));
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            notices.AddRange(
                await TrySynchronizeLiveJournalEventAsync(
                    journalEvent,
                    preferSquadronCargoDiff,
                    cargoInventory,
                    cancellationToken: cancellationToken
                )
            );
        }

        if (cargoInventory is { } squadronCargoInventory && preferSquadronCargoDiff)
        {
            AddNotice(
                notices,
                await TrySynchronizeSquadronCargoDiffAsync(
                    squadronCargoInventory,
                    cargoActivity,
                    cancellationToken: cancellationToken
                )
            );
        }

        if (cargoInventory is not null && !preferSquadronCargoDiff)
        {
            cargoInventory.GetDiff();
            skipNextCargoEvent = false;
        }

        return notices;
    }

    /// <summary>Tracks one event's dock, squadron carrier, carrier identity, and current body.</summary>
    private void ApplyJournalEvent(JournalEventEnvelope journalEvent, string? owner)
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
        RememberJournalBody(journalEvent, owner);
    }

    /// <summary>Routes one live event and converts expected remote failures into recoverable notices.</summary>
    private async Task<IReadOnlyList<ColonizationDeliveryNotice>> TrySynchronizeLiveJournalEventAsync(
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
                "Docked" => await SynchronizeDockedAsync(journalEvent, cancellationToken),
                "Location" when GetJournalBoolean(journalEvent.Payload, "Docked") == true => Notices(
                    await SynchronizeBuildSiteRepairAsync(journalEvent, cancellationToken: cancellationToken)
                ),
                "ColonisationContribution" => await SynchronizeContributionAsync(
                    journalEvent,
                    cancellationToken: cancellationToken
                ),
                "ColonisationConstructionDepot" => Notices(
                    await SynchronizeDepotAsync(journalEvent, cancellationToken: cancellationToken)
                ),
                "ColonisationBeaconDeployed" => Notices(
                    await SynchronizeBeaconDeploymentAsync(cancellationToken: cancellationToken)
                ),
                "DockingGranted" => RequestDockingRefresh(journalEvent),
                "MarketBuy" or "MarketSell" or "CargoTransfer" => Notices(
                    await SynchronizeFleetCarrierCargoAdjustmentAsync(
                        journalEvent,
                        preferShipCargoDiffForSquadron,
                        cargoInventory,
                        cancellationToken: cancellationToken
                    )
                ),
                _ => [],
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
            return [new(ColonizationDeliveryNoticeKind.LiveEventSkipped, journalEvent.EventName, exception.Message)];
        }
    }

    /// <summary>Loads the docked project, repairs its site, and loads a linked carrier baseline in that order.</summary>
    private async Task<IReadOnlyList<ColonizationDeliveryNotice>> SynchronizeDockedAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<ColonizationDeliveryNotice> project = await SynchronizeDockedProjectAsync(
            journalEvent,
            cancellationToken: cancellationToken
        );
        ColonizationDeliveryNotice? repair = await SynchronizeBuildSiteRepairAsync(
            journalEvent,
            cancellationToken: cancellationToken
        );
        ColonizationDeliveryNotice? baseline = await EnsureLinkedFleetCarrierDockBaselineAsync(
            GetJournalDock(journalEvent),
            cancellationToken: cancellationToken
        );
        return [.. project, .. Notices(repair, baseline)];
    }

    /// <summary>Asks presentation to refresh projects after docking permission at a relevant station.</summary>
    private ColonizationDeliveryNotice[] RequestDockingRefresh(JournalEventEnvelope journalEvent)
    {
        if (
            projects.Length == 0
            && !ColonizationDockingSnapshot.IsConstructionSiteName(
                GetJournalString(journalEvent.Payload, "StationName")
            )
        )
        {
            return [];
        }

        observer.DockingRefreshRequested();
        return [];
    }

    /// <summary>Registers the commander as architect after beacon deployment while honoring cancellation.</summary>
    private async Task<ColonizationDeliveryNotice?> SynchronizeBeaconDeploymentAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (apiKey is null)
        {
            return new(ColonizationDeliveryNoticeKind.ArchitectNeedsApiKey);
        }

        if (string.IsNullOrWhiteSpace(currentSystemName))
        {
            return new(ColonizationDeliveryNoticeKind.ArchitectNeedsSystem);
        }

        await client.UpdateSystemSitesAsync(
            currentSystemName,
            new ColonizationSystemSiteUpdate { Architect = commanderName },
            apiKey,
            cancellationToken
        );
        return new(ColonizationDeliveryNoticeKind.ArchitectRegistered, commanderName, currentSystemName);
    }

    private void SetCurrentBody(int? bodyId, string? bodyName, string? bodyCommanderName)
    {
        if (
            currentBodyId == bodyId
            && string.Equals(currentBodyName, bodyName, StringComparison.Ordinal)
            && string.Equals(currentBodyCommanderName, bodyCommanderName, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        currentBodyId = bodyId;
        currentBodyName = bodyName;
        currentBodyCommanderName = bodyId is null ? null : bodyCommanderName;
        observer.CurrentBodyChanged();
    }

    private void RememberJournalBody(JournalEventEnvelope journalEvent, string? owner)
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

        SetCurrentBody(bodyId, ColonizationBodyJournal.ReadBodyName(journalEvent.Payload), owner);
    }

    /// <summary>Returns the dock captured for this event, including a captured undocked state.</summary>
    private ColonizationDockingSnapshot? GetJournalDock(JournalEventEnvelope journalEvent) =>
        journalDocks.TryGetValue(journalEvent, out ColonizationDockingSnapshot? dock)
            ? dock
            : constructionState.CurrentDock;

    /// <summary>Resolves a known dock for suppression after a failed market adjustment.</summary>
    private ColonizationDockingSnapshot? GetJournalDockForMarket(long marketId) =>
        journalDocks.Values.FirstOrDefault(dock => dock?.MarketId == marketId) ?? constructionState.CurrentDock;

    /// <summary>Only explicit client-error responses are safe for automatic replay; server and transport failures may follow a committed write.</summary>
    private static bool IsDefiniteRejection(Exception exception) =>
        exception is RavenColonialServiceException { StatusCode: { } status }
        && (int)status is >= 400 and < 500
        && status != System.Net.HttpStatusCode.RequestTimeout;

    private static ColonizationProject[] Sort(IEnumerable<ColonizationProject> source) =>
        source.OrderBy(project => project.SystemName).ThenBy(project => project.BuildName).ToArray();

    private static ColonizationDeliveryNotice[] Notices(params ColonizationDeliveryNotice?[] notices) =>
        notices.OfType<ColonizationDeliveryNotice>().ToArray();

    private static void AddNotice(List<ColonizationDeliveryNotice> notices, ColonizationDeliveryNotice? notice)
    {
        if (notice is not null)
        {
            notices.Add(notice);
        }
    }

    private static Dictionary<string, int> CopyCargo(IEnumerable<KeyValuePair<string, int>> cargo) =>
        cargo.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

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

    private static int? GetJournalInt32(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
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

    private static string GetCarrierName(ColonizationFleetCarrier carrier)
    {
        return string.IsNullOrWhiteSpace(carrier.DisplayName) ? carrier.Name : carrier.DisplayName;
    }
}
