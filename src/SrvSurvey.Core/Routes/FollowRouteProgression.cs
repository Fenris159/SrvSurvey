namespace SrvSurvey.Core.Routes;

/// <summary>Advances route passes without changing the source itinerary or its per-system data.</summary>
public static class FollowRouteProgression
{
    public static FollowRouteDocument SetProgress(FollowRouteDocument route, int lastReachedIndex)
    {
        ArgumentNullException.ThrowIfNull(route);
        int count = route.Hops.Count;
        int normalized = count == 0 ? -1 : Math.Clamp(lastReachedIndex, -1, count - 1);
        FollowRouteDocument updated = route with
        {
            LastReachedIndex = normalized,
            IsActive = route.IsActive && normalized < count - 1,
        };
        if (
            !route.IsActive
            || count < 2
            || normalized < count - 1
            || route.RepeatMode == FollowRouteRepeatMode.None
            || (route.RepeatMode == FollowRouteRepeatMode.FromStart && !CanRepeatFromStart(route.Kind, route.Hops))
        )
        {
            return updated;
        }

        if (route.RepeatMode == FollowRouteRepeatMode.Reverse)
        {
            return updated with { IsActive = true, IsReversed = !route.IsReversed, LastReachedIndex = 0 };
        }

        FollowRouteHop endpoint = route.GetNavigationHop(normalized);
        FollowRouteHop first = route.Hops[0];
        bool alreadyAtStart = IsSameSystem(endpoint, first);
        return updated with { IsActive = true, IsReversed = false, LastReachedIndex = alreadyAtStart ? 0 : -1 };
    }

    /// <summary>Carrier routes can restart only when the itinerary already returns to its first system.</summary>
    public static bool CanRepeatFromStart(FollowRouteKind kind, IReadOnlyList<FollowRouteHop> hops)
    {
        ArgumentNullException.ThrowIfNull(hops);
        return kind != FollowRouteKind.FleetCarrier || (hops.Count > 1 && IsSameSystem(hops[0], hops[^1]));
    }

    private static bool IsSameSystem(FollowRouteHop first, FollowRouteHop second) =>
        first.SystemAddress is { } address && second.SystemAddress is { } secondAddress
            ? address == secondAddress
            : string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
}
