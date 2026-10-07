namespace SrvSurvey.Core.Colonization;

/// <summary>
/// One outcome of delivery recovery. Presentation owns the wording, so the notice carries only the values it needs.
/// </summary>
/// <param name="Kind">Identifies the outcome.</param>
/// <param name="Name">The project, carrier, station, journal event, or commander the outcome refers to.</param>
/// <param name="Detail">A failure reason, Raven response, or secondary name such as the current system.</param>
/// <param name="Count">The number of affected entries or units, or the carrier market ID for CAPI seeding.</param>
public sealed record ColonizationDeliveryNotice(
    ColonizationDeliveryNoticeKind Kind,
    string? Name = null,
    string? Detail = null,
    long Count = 0
);

/// <summary>Outcomes reported by <see cref="ColonizationDeliveryRecovery"/>.</summary>
public enum ColonizationDeliveryNoticeKind
{
    /// <summary>A live journal event failed remotely; <c>Name</c> is the event and <c>Detail</c> the reason.</summary>
    LiveEventSkipped,

    /// <summary>The aggregate squadron cargo difference failed remotely.</summary>
    SquadronCargoDiffSkipped,

    /// <summary>A beacon deployment could not register the architect without a saved API key.</summary>
    ArchitectNeedsApiKey,

    /// <summary>A beacon deployment could not register the architect without a known system.</summary>
    ArchitectNeedsSystem,

    /// <summary>The commander (<c>Name</c>) became architect of the system (<c>Detail</c>).</summary>
    ArchitectRegistered,

    /// <summary>A squadron cargo difference waits for an in-flight carrier baseline.</summary>
    SquadronCargoQueued,

    /// <summary>Journal cargo updates wait for an in-flight carrier baseline.</summary>
    CarrierCargoQueued,

    /// <summary>A carrier cargo update waits behind an earlier unconfirmed write.</summary>
    CarrierCargoQueuedBehindUnconfirmed,

    /// <summary>Linked carrier cargo was adjusted from the journal event in <c>Name</c>.</summary>
    CarrierCargoAdjusted,

    /// <summary>A docked construction project was linked to the architect.</summary>
    ProjectLinked,

    /// <summary>A docked construction project was loaded without linking the commander.</summary>
    UntrackedProjectLoaded,

    /// <summary>Faction or body metadata was repaired on the docked project.</summary>
    ProjectMetadataUpdated,

    /// <summary>The Raven site's market ID was repaired.</summary>
    BuildSiteMarketRepaired,

    /// <summary>The Raven site's name was repaired.</summary>
    BuildSiteNameRepaired,

    /// <summary>No Raven project matched the recorded contribution.</summary>
    ContributionProjectUnknown,

    /// <summary>The contribution was already retained for reconciliation.</summary>
    ContributionAlreadyRetained,

    /// <summary>Raven acknowledged the contribution of <c>Count</c> units.</summary>
    ContributionPublished,

    /// <summary>No Raven project matched the current construction depot.</summary>
    DepotProjectUnknown,

    /// <summary>The project was marked complete on Raven.</summary>
    ProjectMarkedComplete,

    /// <summary>The depot reported requirements above the supported total.</summary>
    RequirementsAboveSupportedTotal,

    /// <summary>Remaining requirements were republished after a contribution.</summary>
    RemainingUpdatedAfterContribution,

    /// <summary>Construction requirements were published from the depot.</summary>
    RequirementsUpdated,

    /// <summary>The docked linked carrier's Raven cargo baseline was loaded.</summary>
    CarrierBaselineLoaded,

    /// <summary>The docked linked carrier's Raven cargo baseline could not be loaded.</summary>
    CarrierBaselineNotLoaded,

    /// <summary>A retained carrier write cannot be safely replayed until a fresh market arrives.</summary>
    CarrierCargoPendingUncertain,

    /// <summary>A retained carrier write is blocked because Raven cargo changed concurrently.</summary>
    CarrierCargoPendingConcurrentChange,

    /// <summary>A retained carrier write failed again.</summary>
    CarrierCargoPendingFailed,

    /// <summary>A retained delivery has an uncertain outcome and needs user verification.</summary>
    ContributionOutcomeUncertain,

    /// <summary>A retained delivery failed again.</summary>
    ContributionPendingFailed,

    /// <summary>Retained carrier writes could not be persisted.</summary>
    CarrierCargoRecoveryNotSaved,

    /// <summary>Retained deliveries could not be persisted.</summary>
    ContributionRecoveryNotSaved,

    /// <summary>The docked carrier (<c>Name</c>) is being published.</summary>
    CarrierPublishing,

    /// <summary>The carrier was published; its market must be opened to synchronize cargo.</summary>
    CarrierPublishedAwaitingMarket,

    /// <summary>The carrier was published and its cargo was already current.</summary>
    CarrierPublishedCargoCurrent,

    /// <summary>The carrier was published and <c>Count</c> cargo entries were replaced.</summary>
    CarrierPublishedCargoUpdated,

    /// <summary>The carrier was linked but its cargo replacement failed.</summary>
    CarrierLinkedCargoNotUpdated,

    /// <summary>The carrier could not be published.</summary>
    CarrierNotPublished,

    /// <summary>The linked carrier with market ID <c>Count</c> was seeded from Frontier CAPI.</summary>
    CapiCargoSeeded,

    /// <summary>The Frontier CAPI cargo seed failed.</summary>
    CapiCargoSeedNotApplied,

    /// <summary>The carrier's market cargo is being compared with Raven.</summary>
    CarrierCargoChecking,

    /// <summary>The carrier's market cargo synchronization failed.</summary>
    CarrierCargoNotUpdated,

    /// <summary>Raven has no record of the carrier.</summary>
    CarrierNotOnRaven,

    /// <summary>The carrier's Raven cargo already matches its market.</summary>
    CarrierCargoCurrent,

    /// <summary><c>Count</c> carrier cargo entries are being replaced.</summary>
    CarrierCargoUpdating,

    /// <summary><c>Count</c> carrier cargo entries were replaced.</summary>
    CarrierCargoUpdated,

    /// <summary>Updates queued behind a carrier baseline failed and remain retained.</summary>
    QueuedCarrierCargoRetained,
}

/// <summary>A docking repair failure that stays visible only while its system remains active.</summary>
/// <param name="SystemAddress">The system whose construction site could not be repaired.</param>
/// <param name="EventName">The journal event that triggered the repair.</param>
/// <param name="SystemLabel">The system name, or its address when the name is unknown.</param>
/// <param name="Detail">The failure reason, or null when the matched Raven site has no persisted ID to patch.</param>
public sealed record ColonizationBuildSiteRepairWarning(
    long SystemAddress,
    string EventName,
    string SystemLabel,
    string? Detail
);

/// <summary>Receives delivery recovery changes that presentation must reflect immediately.</summary>
public interface IColonizationDeliveryObserver
{
    /// <summary>The project list changed because synchronization loaded, linked, or updated a project.</summary>
    void ProjectsChanged();

    /// <summary>A linked carrier's cargo or registration changed.</summary>
    void FleetCarriersChanged();

    /// <summary>Carrier cargo writes for these commodities are in flight, or none when null.</summary>
    void PendingFleetCarrierCargoChanged(IEnumerable<string>? commodities);

    /// <summary>Carrier cargo synchronization progressed or finished.</summary>
    void FleetCarrierStatusChanged(ColonizationDeliveryNotice notice);

    /// <summary>A carrier publish or market synchronization started or finished.</summary>
    void FleetCarrierSyncBusyChanged(bool busy);

    /// <summary>A general recovery outcome that is not tied to a journal batch.</summary>
    void StatusChanged(ColonizationDeliveryNotice notice);

    /// <summary>Retained deliveries or their reconciliation availability changed.</summary>
    void PendingContributionsChanged();

    /// <summary>The docking repair warning appeared, changed, or cleared.</summary>
    void BuildSiteRepairWarningChanged();

    /// <summary>The commander's current body changed while applying journal events.</summary>
    void CurrentBodyChanged();

    /// <summary>Docking permission at a construction site or known project station should refresh projects.</summary>
    void DockingRefreshRequested();
}
