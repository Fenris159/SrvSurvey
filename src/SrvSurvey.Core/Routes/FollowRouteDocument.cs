using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

public sealed record FollowRouteDocument(
    string FrontierId,
    string FilePath,
    bool IsActive,
    bool AutoCopy,
    int LastReachedIndex,
    IReadOnlyList<FollowRouteHop> Hops,
    string? Name = null,
    string? Notes = null,
    bool IsFavorite = false,
    FollowRouteKind Kind = FollowRouteKind.Standard,
    SpanshRouteKind? SourceSpanshKind = null)
{
    public bool IsStarted => LastReachedIndex >= 0;

    public bool IsComplete => Hops.Count > 0
        && LastReachedIndex >= Hops.Count - 1;

    public FollowRouteHop? NextHop
    {
        get
        {
            var nextIndex = LastReachedIndex + 1;
            return IsActive
                && nextIndex >= 0
                && nextIndex < Hops.Count
                    ? Hops[nextIndex]
                    : null;
        }
    }

    public bool UseNextHop => AutoCopy && NextHop is not null;
}

public sealed record FollowRouteHop(
    string Name,
    long? SystemAddress,
    GalacticCoordinate? Position,
    string? Notes,
    bool Refuel,
    bool Neutron,
    IReadOnlyList<FollowRouteBioTarget>? Bio = null,
    FollowRouteCarrierHop? Carrier = null)
{
    public IReadOnlyList<FollowRouteBioTarget> BioTargets =>
        Bio ?? Array.Empty<FollowRouteBioTarget>();

    public double? DistanceTo(FollowRouteHop other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Position is { } position && other.Position is { } otherPosition
            ? position.DistanceTo(otherPosition)
            : null;
    }
}

public sealed record FollowRouteCarrierHop(
    double? DistanceLy,
    double? RemainingLy,
    double? FuelRemainingTonnes,
    double? TritiumInMarketTonnes,
    double? FuelUsedTonnes,
    bool HasIcyRing,
    bool IsSystemPristine,
    bool MustRestock,
    double? RestockAmountTonnes);

public sealed record FollowRouteBioTarget(
    string BodyName,
    long? BodyId,
    IReadOnlyList<string> Species,
    bool IsCompleted = false,
    string? Subtype = null,
    double? DistanceToArrivalLs = null,
    long? EstimatedScanValue = null,
    long? EstimatedMappingValue = null,
    long? EstimatedBiologyValue = null,
    bool IsTerraformable = false,
    bool IsBiological = false);

public sealed record FollowRouteLoadResult(
    string Path,
    bool Exists,
    FollowRouteDocument? Route,
    string? Error)
{
    public bool IsSuccess => Route is not null;
}

public enum FollowRouteKind
{
    Standard,
    FleetCarrier,
}

public sealed record FollowRouteCatalogEntry(
    string Name,
    string FileName,
    string FilePath,
    bool IsLegacy,
    DateTimeOffset LastModified,
    DateTimeOffset CreatedAt = default,
    string? Notes = null,
    bool IsFavorite = false);

public sealed record FollowRouteRenameResult(
    string PreviousPath,
    FollowRouteDocument Route,
    FollowRouteCatalogEntry CatalogEntry);

public sealed record FollowRouteArrivalResult(
    FollowRouteDocument Route,
    bool Changed,
    int? ReachedIndex)
{
    public bool Completed => Changed && Route.IsComplete;
}
