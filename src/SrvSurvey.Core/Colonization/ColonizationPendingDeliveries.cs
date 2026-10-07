namespace SrvSurvey.Core.Colonization;

/// <summary>A retained delivery belongs to its originating commander profile and records whether replay needs user verification.</summary>
public sealed record ColonizationPendingContribution(
    string Owner,
    string BuildId,
    string Commander,
    Dictionary<string, int> Cargo,
    string EventId,
    bool OutcomeUnknown
);

/// <summary>An ordered carrier delta retains its baseline and journal event time; null time prevents retirement by a market timestamp.</summary>
public sealed record ColonizationPendingCargoAdjustment(
    string Owner,
    long MarketId,
    Dictionary<string, int> Delta,
    DateTimeOffset? RecordedAt,
    Dictionary<string, int>? Before,
    bool Attempted = true,
    bool OutcomeUnknown = true
);

/// <summary>A construction market whose Raven site row was already repaired, so later docks skip the lookup.</summary>
public sealed record ColonizationBuildSiteRepairVisit(long MarketId, string StationKey);

/// <summary>Persists delivery recovery state so retained writes and repair guards survive restart.</summary>
public interface IColonizationDeliveryRecoveryStore
{
    /// <summary>Loads retained deliveries in their saved order.</summary>
    IReadOnlyList<ColonizationPendingContribution> LoadPendingContributions();

    /// <summary>Replaces the retained deliveries.</summary>
    void SavePendingContributions(IReadOnlyList<ColonizationPendingContribution> contributions);

    /// <summary>Loads retained carrier cargo writes in their saved order.</summary>
    IReadOnlyList<ColonizationPendingCargoAdjustment> LoadPendingCargoAdjustments();

    /// <summary>Replaces the retained carrier cargo writes.</summary>
    void SavePendingCargoAdjustments(IReadOnlyList<ColonizationPendingCargoAdjustment> adjustments);

    /// <summary>Loads repaired construction markets, oldest first.</summary>
    IReadOnlyList<ColonizationBuildSiteRepairVisit> LoadBuildSiteRepairVisits();

    /// <summary>Replaces the repaired construction markets.</summary>
    void SaveBuildSiteRepairVisits(IEnumerable<ColonizationBuildSiteRepairVisit> visits);
}
