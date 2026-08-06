using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Navigation;

public sealed record JumpTarget(
    string Name,
    long SystemAddress,
    string? StarClass = null);

public enum JumpInfoRouteSource
{
    Direct,
    NavRoute,
    FollowedRoute,
}

public sealed record JumpInfoRouteLeg(
    string FromSystem,
    string ToSystem,
    double DistanceLy,
    bool IsScoopable,
    bool RequiresBoost);

public sealed record JumpInfoRoutePlan(
    JumpTarget Target,
    JumpInfoRouteSource Source,
    int TargetLegIndex,
    IReadOnlyList<JumpInfoRouteLeg> Legs,
    GalacticCoordinate? TargetPosition)
{
    public int JumpNumber => TargetLegIndex >= 0 ? TargetLegIndex + 1 : 0;

    public double TotalDistanceLy => Legs.Sum(leg => leg.DistanceLy);
}

public static class JumpInfoRoutePlanner
{
    private const string ScoopableStarClasses = "KGBFOAM";

    public static JumpInfoRoutePlan? Create(
        JumpTarget? fsdTarget,
        EliteStatus? status,
        string? currentSystemName,
        long? currentSystemAddress,
        GalacticCoordinate? currentPosition,
        NavRouteSnapshot? navRoute,
        FollowRouteDocument? followedRoute,
        double? maximumJumpRange = null)
    {
        var target = SelectTarget(fsdTarget, status);
        if (target is null)
        {
            return null;
        }

        var navPoints = CreateNavRoutePoints(navRoute);
        var routePoints = navPoints.Count >= 3
            ? navPoints
            : CreateFollowedRoutePoints(followedRoute);
        JumpInfoRouteSource source;
        if (navPoints.Count >= 3)
        {
            source = JumpInfoRouteSource.NavRoute;
        }
        else if (routePoints.Count > 0)
        {
            source = JumpInfoRouteSource.FollowedRoute;
        }
        else
        {
            source = JumpInfoRouteSource.Direct;
        }

        var targetPoint = FindTarget(routePoints, target);
        if (targetPoint is not null && string.IsNullOrWhiteSpace(target.StarClass))
        {
            target = target with { StarClass = targetPoint.StarClass };
        }

        if (targetPoint is null)
        {
            routePoints = CreateDirectRoute(
                currentSystemName,
                currentSystemAddress,
                currentPosition,
                target);
            source = JumpInfoRouteSource.Direct;
        }

        var legs = CreateLegs(routePoints, maximumJumpRange);
        var targetLegIndex = legs.FindIndex(leg => MatchesTarget(
            leg.ToSystem,
            routePoints.FirstOrDefault(point => string.Equals(
                point.Name,
                leg.ToSystem,
                StringComparison.OrdinalIgnoreCase))?.SystemAddress,
            target));

        return new JumpInfoRoutePlan(
            target,
            source,
            targetLegIndex,
            legs,
            targetPoint?.Position);
    }

    public static JumpTarget? SelectTarget(
        JumpTarget? fsdTarget,
        EliteStatus? status)
    {
        if (fsdTarget is { Name.Length: > 0 })
        {
            return fsdTarget;
        }

        var destination = status?.Destination;
        return destination is { Body: 0, System: > 0, Name.Length: > 0 }
            ? new JumpTarget(destination.Name, destination.System)
            : null;
    }

    private static List<RoutePoint> CreateNavRoutePoints(
        NavRouteSnapshot? navRoute)
    {
        return navRoute?.Route
            .Select(entry => new RoutePoint(
                entry.StarSystem,
                entry.SystemAddress,
                entry.Position,
                entry.StarClass,
                false))
            .ToList() ?? [];
    }

    private static List<RoutePoint> CreateFollowedRoutePoints(
        FollowRouteDocument? followedRoute)
    {
        if (followedRoute is not { IsActive: true, Hops.Count: > 0 })
        {
            return [];
        }

        return followedRoute.Hops
            .Select(hop => new RoutePoint(
                hop.Name,
                hop.SystemAddress,
                hop.Position,
                hop.Neutron ? "N" : null,
                hop.Neutron))
            .ToList();
    }

    private static List<RoutePoint> CreateDirectRoute(
        string? currentSystemName,
        long? currentSystemAddress,
        GalacticCoordinate? currentPosition,
        JumpTarget target)
    {
        if (string.IsNullOrWhiteSpace(currentSystemName))
        {
            return [];
        }

        return
        [
            new RoutePoint(
                currentSystemName,
                currentSystemAddress,
                currentPosition,
                null,
                false),
            new RoutePoint(
                target.Name,
                target.SystemAddress,
                null,
                target.StarClass,
                false),
        ];
    }

    private static RoutePoint? FindTarget(
        IReadOnlyList<RoutePoint> route,
        JumpTarget target)
    {
        return route.FirstOrDefault(point => MatchesTarget(
            point.Name,
            point.SystemAddress,
            target));
    }

    private static List<JumpInfoRouteLeg> CreateLegs(
        IReadOnlyList<RoutePoint> route,
        double? maximumJumpRange)
    {
        var legs = new List<JumpInfoRouteLeg>(Math.Max(0, route.Count - 1));
        for (var index = 1; index < route.Count; index++)
        {
            var from = route[index - 1];
            var to = route[index];
            if (from.Position is not { } fromPosition
                || to.Position is not { } toPosition)
            {
                continue;
            }

            var distance = fromPosition.DistanceTo(toPosition);
            var starClass = to.StarClass?.Trim();
            var scoopable = starClass is { Length: > 0 }
                && ScoopableStarClasses.Contains(
                    char.ToUpperInvariant(starClass[0]));
            var requiresBoost = to.Neutron
                || maximumJumpRange is > 0
                    && distance > maximumJumpRange.Value;
            legs.Add(new JumpInfoRouteLeg(
                from.Name,
                to.Name,
                distance,
                scoopable,
                requiresBoost));
        }

        return legs;
    }

    private static bool MatchesTarget(
        string name,
        long? systemAddress,
        JumpTarget target)
    {
        return target.SystemAddress > 0 && systemAddress == target.SystemAddress
            || string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RoutePoint(
        string Name,
        long? SystemAddress,
        GalacticCoordinate? Position,
        string? StarClass,
        bool Neutron);
}
