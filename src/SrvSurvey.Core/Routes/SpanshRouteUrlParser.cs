namespace SrvSurvey.Core.Routes;

public static class SpanshRouteUrlParser
{
    public static bool TryParse(
        string? text,
        out SpanshRouteReference? route)
    {
        route = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (Guid.TryParse(candidate, out var directId))
        {
            route = new SpanshRouteReference(
                directId,
                SpanshRouteKind.Generic);
            return true;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !IsSpanshHost(uri.Host))
        {
            return false;
        }

        var parts = uri.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resultsIndex = Array.FindLastIndex(
            parts,
            part => string.Equals(
                part,
                "results",
                StringComparison.OrdinalIgnoreCase));
        if (resultsIndex < 0
            || resultsIndex + 1 >= parts.Length
            || !Guid.TryParse(parts[resultsIndex + 1], out var routeId))
        {
            return false;
        }

        var routeParts = parts.Take(resultsIndex).ToArray();
        var kind = Classify(routeParts);
        route = new SpanshRouteReference(routeId, kind);
        return true;
    }

    private static SpanshRouteKind Classify(IReadOnlyList<string> parts)
    {
        if (Contains(parts, "exact-plotter"))
        {
            return SpanshRouteKind.Galaxy;
        }

        if (Contains(parts, "fleet-carrier"))
        {
            return SpanshRouteKind.FleetCarrier;
        }

        if (Contains(parts, "colonisation"))
        {
            return SpanshRouteKind.Colonisation;
        }

        if (Contains(parts, "exobiology"))
        {
            return SpanshRouteKind.Exobiology;
        }

        if (Contains(parts, "trade"))
        {
            return SpanshRouteKind.Trade;
        }

        if (Contains(parts, "tourist") || Contains(parts, "tourist-search"))
        {
            return SpanshRouteKind.Tourist;
        }

        if (Contains(parts, "plotter"))
        {
            return SpanshRouteKind.Neutron;
        }

        if (Contains(parts, "riches")
            || Contains(parts, "ammonia")
            || Contains(parts, "earth")
            || Contains(parts, "rocky-metal"))
        {
            return SpanshRouteKind.Riches;
        }

        return SpanshRouteKind.Generic;
    }

    private static bool Contains(
        IEnumerable<string> parts,
        string expected)
    {
        return parts.Any(part => string.Equals(
            part,
            expected,
            StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSpanshHost(string host)
    {
        return string.Equals(host, "spansh.co.uk", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".spansh.co.uk", StringComparison.OrdinalIgnoreCase);
    }
}

public enum SpanshRouteKind
{
    Generic,
    Riches,
    Exobiology,
    Tourist,
    Neutron,
    Galaxy,
    FleetCarrier,
    Colonisation,
    Trade,
}

public sealed record SpanshRouteReference(
    Guid JobId,
    SpanshRouteKind Kind);
