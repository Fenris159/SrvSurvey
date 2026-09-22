using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

public sealed record PlanetaryBodyCriteria(IReadOnlyList<string> BodySubtypes, IReadOnlyList<string> LandmarkSubtypes);

/// <summary>
/// Turns a Surface Hunt material into the Spansh body filters for landable planetary mining.
/// </summary>
public static class PlanetaryMiningPlan
{
    public const string MiningType = "Planetary Mining";

    public static IReadOnlyList<string> SpanshPowers { get; } =
    [
        "A. Lavigny-Duval",
        "Aisling Duval",
        "Archon Delaine",
        "Denton Patreus",
        "Edmund Mahon",
        "Felicia Winters",
        "Jerome Archer",
        "Li Yong-Rui",
        "Nakato Kaine",
        "Pranav Antal",
        "Yuri Grom",
        "Zemina Torval",
    ];

    public static IReadOnlyList<string> OtherPowers(string power)
    {
        string spansh = power.Equals("Arissa Lavigny-Duval", StringComparison.OrdinalIgnoreCase)
            ? "A. Lavigny-Duval"
            : power;
        return SpanshPowers.Where(candidate => !candidate.Equals(spansh, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static IReadOnlyList<string> Materials { get; } =
        SurfaceMiningCommodityCatalog
            .HuntReferences.Select(reference => reference.Material)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static PlanetaryBodyCriteria? For(IEnumerable<string> materials)
    {
        SurfaceMiningHuntReference[] matches = materials
            .Select(material =>
                SurfaceMiningCommodityCatalog.HuntReferences.FirstOrDefault(reference =>
                    reference.Material.Equals(material, StringComparison.OrdinalIgnoreCase)
                )
            )
            .OfType<SurfaceMiningHuntReference>()
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        string[] subtypes = matches.SelectMany(BodySubtypes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        bool requireGeology = matches.All(reference => !IsOpenGround(reference.Geology));
        string[] landmarks = requireGeology
            ? matches
                .SelectMany(reference => LandmarkSubtypes(reference.Geology))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        return new PlanetaryBodyCriteria(subtypes, landmarks);
    }

    private static IEnumerable<string> BodySubtypes(SurfaceMiningHuntReference reference)
    {
        if (!SurfaceMiningCommodityCatalog.TryResolve(reference.Material, out SurfaceMiningCommodity? commodity))
        {
            yield break;
        }

        if (commodity.MetalRich)
        {
            yield return "Metal-rich body";
        }

        if (commodity.HighMetalContent)
        {
            yield return "High metal content world";
        }

        if (commodity.Rocky)
        {
            yield return "Rocky body";
        }

        if (commodity.RockyIce)
        {
            yield return "Rocky Ice world";
        }

        if (commodity.Icy)
        {
            yield return "Icy body";
        }
    }

    private static bool IsOpenGround(string geology) =>
        geology.Equals("None required", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> LandmarkSubtypes(string geology)
    {
        if (geology.Contains("Iron magma", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Iron Magma Lava Spout";
        }

        if (geology.Contains("Silicate magma", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Silicate Magma Lava Spout";
        }

        if (geology.Contains("CO", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Carbon Dioxide Ice Geyser";
        }

        if (geology.Contains("Ammonia", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Ammonia Ice Geyser";
        }

        if (geology.Contains("Methane", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Methane Ice Geyser";
        }

        if (geology.Contains("Nitrogen", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Nitrogen Ice Geyser";
        }

        if (
            geology.Contains("Silicate-vapour", StringComparison.OrdinalIgnoreCase)
            || geology.Contains("Silicate vapour", StringComparison.OrdinalIgnoreCase)
        )
        {
            yield return "Silicate Vapour Gas Vent";
        }
    }
}
