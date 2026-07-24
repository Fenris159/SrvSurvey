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
        Guid routeId = default;
        if (!parts.Any(part => Guid.TryParse(part, out routeId)))
        {
            return false;
        }

        var kind = parts.Any(part => string.Equals(
            part,
            "tourist",
            StringComparison.OrdinalIgnoreCase))
                ? SpanshRouteKind.Tourist
                : parts.Any(part => string.Equals(
                    part,
                    "exact-plotter",
                    StringComparison.OrdinalIgnoreCase))
                    ? SpanshRouteKind.Galaxy
                    : parts.Any(part => string.Equals(
                        part,
                        "plotter",
                        StringComparison.OrdinalIgnoreCase))
                        ? SpanshRouteKind.Neutron
                        : SpanshRouteKind.Generic;
        route = new SpanshRouteReference(routeId, kind);
        return true;
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
    Tourist,
    Neutron,
    Galaxy,
}

public sealed record SpanshRouteReference(
    Guid JobId,
    SpanshRouteKind Kind);
