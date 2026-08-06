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

    private static readonly (string Token, RouteBodyVisualKind Kind)[]
        ExactTokenKinds =
        [
            ("black hole", RouteBodyVisualKind.BlackHole),
            ("neutron", RouteBodyVisualKind.NeutronStar),
            ("white dwarf", RouteBodyVisualKind.WhiteDwarf),
            ("earth like", RouteBodyVisualKind.EarthLikeWorld),
            ("ammonia world", RouteBodyVisualKind.AmmoniaWorld),
            ("water giant", RouteBodyVisualKind.WaterGiant),
            ("water world", RouteBodyVisualKind.WaterWorld),
            ("high metal content", RouteBodyVisualKind.HighMetalContentWorld),
            ("metal rich", RouteBodyVisualKind.MetalRichBody),
            ("rocky ice", RouteBodyVisualKind.RockyIceBody),
            ("icy", RouteBodyVisualKind.IcyBody),
            ("rocky", RouteBodyVisualKind.RockyBody),
            ("gas giant", RouteBodyVisualKind.GasGiant),
        ];

    private static RouteBodyVisualKind ResolveKind(string normalized)
    {
        if (ContainsAny(normalized, "barycentre", "barycenter"))
        {
            return RouteBodyVisualKind.Barycentre;
        }

        if (ContainsAny(normalized, "asteroid", "belt cluster"))
        {
            return RouteBodyVisualKind.AsteroidCluster;
        }

        foreach (var (token, kind) in ExactTokenKinds)
        {
            if (normalized.Contains(token, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        if (normalized.EndsWith("star", StringComparison.Ordinal)
            || normalized.Contains(" star ", StringComparison.Ordinal))
        {
            return RouteBodyVisualKind.Star;
        }

        return RouteBodyVisualKind.Unknown;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.Ordinal));

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
