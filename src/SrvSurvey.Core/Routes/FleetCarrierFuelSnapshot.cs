using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

public enum FollowRouteCarrierKind
{
    Personal,
    Squadron,
}

/// <summary>Latest attributable carrier observations, kept separate from route forecasts.</summary>
public sealed record FleetCarrierFuelSnapshot(
    FollowRouteCarrierKind Kind,
    string Callsign,
    long? CarrierId,
    string SystemName,
    double? TankTonnes,
    double? StoredTritiumTonnes,
    double? UsedCapacityTonnes,
    DateTimeOffset ObservedAt,
    GalacticCoordinate? Position = null,
    bool IsEstimated = false
)
{
    public double Capacity => Kind == FollowRouteCarrierKind.Squadron ? 60000 : 25000;

    public bool IsComplete =>
        TankTonnes is >= 0 and <= 1000
        && StoredTritiumTonnes is >= 0
        && UsedCapacityTonnes is >= 0
        && UsedCapacityTonnes <= Capacity
        && StoredTritiumTonnes <= UsedCapacityTonnes;
}
