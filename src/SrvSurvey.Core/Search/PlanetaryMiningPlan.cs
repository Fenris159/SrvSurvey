using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

public sealed record PlanetaryBodyCriteria(
    IReadOnlyList<string> BodySubtypes,
    IReadOnlyList<string> LandmarkSubtypes,
    bool RequiresWhiteDwarfHost = false,
    IReadOnlyList<string>? VolcanismTypes = null
);

/// <summary>
/// Turns a Surface Hunt material into the Spansh body filters for landable planetary mining.
/// </summary>
public static class PlanetaryMiningPlan
{
    public const string MiningType = "Planetary Mining";
    public static IReadOnlyList<string> MetallicMagmaTypes { get; } =
    ["Metallic Magma", "Minor Metallic Magma", "Major Metallic Magma"];
    public static IReadOnlyList<string> RockyMagmaTypes { get; } =
    ["Rocky Magma", "Minor Rocky Magma", "Major Rocky Magma"];
    public static IReadOnlyList<string> SilicateVapourTypes { get; } =
    ["Silicate Vapour Geysers", "Minor Silicate Vapour Geysers", "Major Silicate Vapour Geysers"];

    /// <summary>
    /// Surface Hunt materials that EDPM does not sell from rings.
    /// Shared names stay, including Monazite. Low Temperature Diamonds is a ring
    /// hotspot and is not the same commodity as Diamond.
    /// Names are compacted so Ardent's "periclasedunite" matches "Periclase Dunite".
    /// </summary>
    private static readonly HashSet<string> SurfaceExclusiveMaterials = CompactNames(
        "Bastnasite",
        "Deuterium",
        "Diamond",
        "Haematite",
        "Helium",
        "Helium-3",
        "Iridium",
        "Magnesite",
        "Olivine",
        "Periclase Dunite",
        "Quartz Pyroxenite",
        "Ruby",
        "Sapphire",
        "Thortveitite"
    );

    /// <summary>Minerals and metals EDPM stores for ring stations. Other station goods stay off the list.</summary>
    public static IReadOnlyList<string> EdpmCommodityNames { get; } =
    [
        "Alexandrite",
        "Aluminium",
        "Bauxite",
        "Benitoite",
        "Bertrandite",
        "Beryllium",
        "Bismuth",
        "Bromellite",
        "Cobalt",
        "Coltan",
        "Copper",
        "Cryolite",
        "Gallite",
        "Gallium",
        "Gold",
        "Goslarite",
        "Grandidierite",
        "Hafnium 178",
        "Indite",
        "Indium",
        "Jadeite",
        "Lanthanum",
        "Lepidolite",
        "Lithium",
        "Lithium Hydroxide",
        "Low Temperature Diamonds",
        "Methane Clathrate",
        "Methanol Monohydrate Crystals",
        "Moissanite",
        "Monazite",
        "Musgravite",
        "Osmium",
        "Painite",
        "Palladium",
        "Platinum",
        "Praseodymium",
        "Pyrophyllite",
        "Rhodplumsite",
        "Rutile",
        "Samarium",
        "Serendibite",
        "Silver",
        "Taaffeite",
        "Tantalum",
        "Thallium",
        "Thorium",
        "Titanium",
        "Uraninite",
        "Uranium",
        "Void Opal",
    ];

    private static readonly HashSet<string> EdpmCommodities = CompactNames(EdpmCommodityNames);

    public static bool IsSurfaceExclusive(string material) => SurfaceExclusiveMaterials.Contains(Compact(material));

    public static bool IsEdpmCommodity(string material) => EdpmCommodities.Contains(Compact(material));

    private static HashSet<string> CompactNames(params string[] names) => CompactNames((IEnumerable<string>)names);

    private static HashSet<string> CompactNames(IEnumerable<string> names)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            keys.Add(Compact(name));
        }

        return keys;
    }

    private static string Compact(string value) => MiningCommodityName.Key(value);

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
                    MiningCommodityName.Same(reference.Material, material)
                )
            )
            .OfType<SurfaceMiningHuntReference>()
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        string[] subtypes = matches.SelectMany(BodySubtypes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IReadOnlyList<string>?[] geologyTypes = matches
            .Select(reference => VolcanismTypesFor(reference.Geology))
            .ToArray();
        IReadOnlyList<string> volcanismTypes = geologyTypes.All(types => types is { Count: > 0 })
            ? geologyTypes
                .OfType<IReadOnlyList<string>>()
                .SelectMany(types => types)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        bool requiresWhiteDwarfHost = matches.All(reference =>
            reference.SpecialClue.Contains("White-dwarf", StringComparison.OrdinalIgnoreCase)
        );
        return new PlanetaryBodyCriteria(subtypes, [], requiresWhiteDwarfHost, volcanismTypes);
    }

    public static bool Matches(PlanetaryBodyCriteria criteria, MiningPlanetaryBody body)
    {
        if (!criteria.BodySubtypes.Contains(body.Subtype, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (criteria.VolcanismTypes is { Count: > 0 })
        {
            return criteria.VolcanismTypes.Contains(body.VolcanismType, StringComparer.OrdinalIgnoreCase);
        }

        return true;
    }

    public static bool IsWhiteDwarf(MiningBodyParent parent) =>
        parent.Type.Equals("Star", StringComparison.OrdinalIgnoreCase)
        && parent.Subtype.Contains("White Dwarf", StringComparison.OrdinalIgnoreCase);

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

    private static IReadOnlyList<string>? VolcanismTypesFor(string geology)
    {
        if (geology.Equals("Iron magma", StringComparison.OrdinalIgnoreCase))
        {
            return MetallicMagmaTypes;
        }

        if (geology.Equals("Silicate magma or Iron magma", StringComparison.OrdinalIgnoreCase))
        {
            return MetallicMagmaTypes.Concat(RockyMagmaTypes).ToArray();
        }

        if (geology.Equals("Silicate-vapour geysers", StringComparison.OrdinalIgnoreCase))
        {
            return SilicateVapourTypes;
        }

        // Spansh has no Helium geyser type, so Helium cannot use an exhaustive volcanism filter.
        return null;
    }
}
