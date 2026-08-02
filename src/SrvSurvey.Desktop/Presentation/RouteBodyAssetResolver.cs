namespace SrvSurvey.Desktop.Presentation;

public enum RouteBodyVisualKind
{
    Unknown,
    BlackHole,
    NeutronStar,
    WhiteDwarf,
    Star,
    GasGiant,
    WaterGiant,
    WaterWorld,
    EarthLikeWorld,
    AmmoniaWorld,
    HighMetalContentWorld,
    MetalRichBody,
    RockyBody,
    RockyIceBody,
    IcyBody,
    AsteroidCluster,
    Barycentre,
}

public sealed record RouteBodyVisual(
    RouteBodyVisualKind Kind,
    string AssetPath,
    string AccessibleName);

/// <summary>
/// Maps the stable body subtype supplied by Spansh to shared body artwork.
/// </summary>
public static class RouteBodyAssetResolver
{
    private const string AssetRoot =
        "avares://SrvSurvey.Desktop/Assets/Bodies/";

    public static IReadOnlyList<RouteBodyVisual> SupportedVisuals { get; } =
        Enum.GetValues<RouteBodyVisualKind>()
            .Select(CreateVisual)
            .ToArray();

    public static RouteBodyVisual Resolve(string? subtype)
    {
        var normalized = Normalize(subtype);
        var kind = ResolveKind(normalized);
        return CreateVisual(kind);
    }

    private static RouteBodyVisual CreateVisual(RouteBodyVisualKind kind)
    {
        return new RouteBodyVisual(
            kind,
            AssetRoot + GetFileName(kind),
            GetAccessibleName(kind));
    }

    private static RouteBodyVisualKind ResolveKind(string normalized)
    {
        if (normalized.Contains("black hole", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.BlackHole;
        }

        if (normalized.Contains("neutron", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.NeutronStar;
        }

        if (normalized.Contains("white dwarf", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.WhiteDwarf;
        }

        if (normalized.Contains("barycentre", StringComparison.Ordinal)
            || normalized.Contains("barycenter", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.Barycentre;
        }

        if (normalized.Contains("asteroid", StringComparison.Ordinal)
            || normalized.Contains("belt cluster", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.AsteroidCluster;
        }

        if (normalized.Contains("earth like", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.EarthLikeWorld;
        }

        if (normalized.Contains("ammonia world", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.AmmoniaWorld;
        }

        if (normalized.Contains("water giant", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.WaterGiant;
        }

        if (normalized.Contains("water world", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.WaterWorld;
        }

        if (normalized.Contains("high metal content", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.HighMetalContentWorld;
        }

        if (normalized.Contains("metal rich", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.MetalRichBody;
        }

        if (normalized.Contains("rocky ice", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.RockyIceBody;
        }

        if (normalized.Contains("icy", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.IcyBody;
        }

        if (normalized.Contains("rocky", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.RockyBody;
        }

        if (normalized.Contains("gas giant", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.GasGiant;
        }

        if (normalized.EndsWith("star", StringComparison.Ordinal)
            || normalized.Contains(" star ", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.Star;
        }

        return RouteBodyVisualKind.Unknown;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            value
                .Trim()
                .ToLowerInvariant()
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetFileName(RouteBodyVisualKind kind) => kind switch
    {
        RouteBodyVisualKind.BlackHole => "black-hole.png",
        RouteBodyVisualKind.NeutronStar => "neutron-star.png",
        RouteBodyVisualKind.WhiteDwarf => "white-dwarf.png",
        RouteBodyVisualKind.Star => "star.png",
        RouteBodyVisualKind.GasGiant => "gas-giant.png",
        RouteBodyVisualKind.WaterGiant => "water-giant.png",
        RouteBodyVisualKind.WaterWorld => "water-world.png",
        RouteBodyVisualKind.EarthLikeWorld => "earth-like-world.png",
        RouteBodyVisualKind.AmmoniaWorld => "ammonia-world.png",
        RouteBodyVisualKind.HighMetalContentWorld => "high-metal-content.png",
        RouteBodyVisualKind.MetalRichBody => "metal-rich.png",
        RouteBodyVisualKind.RockyBody => "rocky-body.png",
        RouteBodyVisualKind.RockyIceBody => "rocky-ice-body.png",
        RouteBodyVisualKind.IcyBody => "icy-body.png",
        RouteBodyVisualKind.AsteroidCluster => "asteroid-cluster.png",
        RouteBodyVisualKind.Barycentre => "barycentre.png",
        _ => "unknown.png",
    };

    private static string GetAccessibleName(RouteBodyVisualKind kind) => kind switch
    {
        RouteBodyVisualKind.BlackHole => "Black hole",
        RouteBodyVisualKind.NeutronStar => "Neutron star",
        RouteBodyVisualKind.WhiteDwarf => "White dwarf",
        RouteBodyVisualKind.Star => "Star",
        RouteBodyVisualKind.GasGiant => "Gas giant",
        RouteBodyVisualKind.WaterGiant => "Water giant",
        RouteBodyVisualKind.WaterWorld => "Water world",
        RouteBodyVisualKind.EarthLikeWorld => "Earth-like world",
        RouteBodyVisualKind.AmmoniaWorld => "Ammonia world",
        RouteBodyVisualKind.HighMetalContentWorld => "High metal content world",
        RouteBodyVisualKind.MetalRichBody => "Metal-rich body",
        RouteBodyVisualKind.RockyBody => "Rocky body",
        RouteBodyVisualKind.RockyIceBody => "Rocky ice body",
        RouteBodyVisualKind.IcyBody => "Icy body",
        RouteBodyVisualKind.AsteroidCluster => "Asteroid cluster",
        RouteBodyVisualKind.Barycentre => "Barycentre",
        _ => "Unknown body type",
    };
}
