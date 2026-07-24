using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

public sealed record FollowRouteDocument(
    string FrontierId,
    string FilePath,
    bool IsActive,
    bool AutoCopy,
    int LastReachedIndex,
    IReadOnlyList<FollowRouteHop> Hops)
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
    bool Neutron)
{
    public double? DistanceTo(FollowRouteHop other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Position is { } position && other.Position is { } otherPosition
            ? position.DistanceTo(otherPosition)
            : null;
    }
}

public sealed record FollowRouteLoadResult(
    string Path,
    bool Exists,
    FollowRouteDocument? Route,
    string? Error)
{
    public bool IsSuccess => Route is not null;
}

public sealed record FollowRouteArrivalResult(
    FollowRouteDocument Route,
    bool Changed,
    int? ReachedIndex)
{
    public bool Completed => Changed && Route.IsComplete;
}
