namespace SrvSurvey.Core.Mining;

public sealed record SurfaceMiningHuntReference(
    string SearchGroup,
    string Material,
    string StartWith,
    string AlsoPossibleOn,
    string Geology,
    string SpecialClue,
    int AverageGalacticPrice,
    int PeakSellPrice);

public sealed record SurfaceMiningCommodity(
    string Category,
    string Name,
    bool HighMetalContent,
    bool MetalRich,
    bool Rocky,
    bool RockyIce,
    bool Icy,
    int AverageSellPrice,
    int MaximumSellPrice,
    string ColorHex);

public static class SurfaceMiningCommodityCatalog
{
    public const string SourceUrl =
        "https://forums.frontier.co.uk/threads/rhino-surface-hotspot-list.649504/";

    public static IReadOnlyList<SurfaceMiningHuntReference> HuntReferences { get; } =
    [
        new("Volcanic gemstones", "Diamond", "Metal-rich", "High-metal-content, Rocky, or Rocky ice", "Silicate magma or Iron magma", "—", 134_784, 720_648),
        new("Volcanic gemstones", "Sapphire", "Metal-rich", "High-metal-content or Rocky", "Iron magma", "—", 128_050, 648_352),
        new("Volcanic gemstones", "Ruby", "Metal-rich", "High-metal-content or Rocky", "Iron magma", "—", 110_381, 589_240),
        new("Metal-bearing ground", "Iridium", "Metal-rich", "High-metal-content", "None required", "—", 208_463, 1_038_104),
        new("Metal-bearing ground", "Rhodplumsite", "Metal-rich", "High-metal-content", "None required", "Do not expect alongside Platinum", 187_921, 826_249),
        new("Metal-bearing ground", "Helium", "High-metal-content", "Metal-rich, Rocky, or Rocky ice", "CO₂ geysers, Ammonia geysers, Methane geysers, Nitrogen geysers, Helium geysers, or Silicate-vapour geysers", "—", 102_861, 591_360),
        new("Metal-bearing ground", "Platinum", "Metal-rich", "High-metal-content", "None required", "—", 70_998, 333_030),
        new("Metal-bearing ground", "Osmium", "High-metal-content", "Metal-rich", "Iron magma", "—", 56_471, 273_000),
        new("Metal-bearing ground", "Gold", "Metal-rich or High-metal-content", "Rocky", "None required", "—", 48_005, 282_678),
        new("Metal-bearing ground", "Silver", "High-metal-content", "Metal-rich or Rocky", "None required", "—", 37_743, 219_318),
        new("Metal-bearing ground", "Samarium", "Metal-rich or High-metal-content", "Rocky", "None required", "—", 28_362, 154_266),
        new("Metal-bearing ground", "Tantalum", "High-metal-content", "Metal-rich", "None required", "—", 14_360, 15_582),
        new("Metal-bearing ground", "Thorium", "High-metal-content", "Metal-rich or Rocky", "None required", "—", 12_297, 12_917),
        new("Metal-bearing ground", "Uranium", "High-metal-content", "Metal-rich", "None required", "—", 7_599, 8_587),
        new("Metal-bearing ground", "Titanium", "High-metal-content", "Metal-rich", "None required", "—", 4_800, 5_865),
        new("Metal-bearing ground", "Haematite", "High-metal-content", "Metal-rich, Rocky, or Rocky ice", "None required", "—", 2_800, 10_044),
        new("Metal-bearing ground", "Lithium", "High-metal-content", "Metal-rich", "None required", "—", 2_099, 2_892),
        new("Metal-bearing ground", "Copper", "High-metal-content", "Metal-rich, Rocky, or Rocky ice", "None required", "—", 774, 1_931),
        new("Rocky / volcanic ground", "Monazite", "Rocky", "—", "Silicate magma or Iron magma", "—", 268_661, 865_908),
        new("Rocky / volcanic ground", "Alexandrite", "Rocky", "—", "Silicate magma or Iron magma", "—", 229_207, 714_088),
        new("Rocky / volcanic ground", "Periclase Dunite", "Rocky", "—", "Iron magma", "White-dwarf primary (D–DX)", 204_168, 1_038_104),
        new("Rocky / volcanic ground", "Serendibite", "Rocky", "—", "Silicate magma or Iron magma", "—", 188_438, 570_948),
        new("Rocky / volcanic ground", "Bastnasite", "Rocky", "—", "Silicate magma or Iron magma", "—", 78_583, 531_208),
        new("Rocky / volcanic ground", "Quartz Pyroxenite", "Rocky", "Rocky ice", "Iron magma", "—", 46_469, 312_072),
        new("Rocky / volcanic ground", "Jadeite", "Rocky", "—", "Silicate-vapour geysers", "—", 42_770, 179_421),
        new("Rocky / volcanic ground", "Olivine", "Rocky or Rocky ice", "—", "Silicate magma or Iron magma", "—", 31_417, 209_936),
        new("Rocky ground", "Grandidierite", "Rocky", "—", "None required", "Trace iron in body composition", 213_547, 571_800),
        new("Rocky ground", "Thortveitite", "Rocky", "—", "None required", "Trace yttrium in body composition", 203_892, 1_038_104),
        new("Rocky ground", "Palladium", "Rocky", "Metal-rich, High-metal-content, or Rocky ice", "None required", "—", 52_167, 302_136),
        new("Rocky ground", "Magnesite", "Rocky", "—", "None required", "—", 38_198, 255_880),
        new("Rocky ground", "Uraninite", "Rocky", "—", "None required", "—", 3_006, 17_166),
        new("Icy ground", "Low Temperature Diamonds", "Icy", "Rocky or Rocky ice", "None required", "—", 130_184, 384_562),
        new("Icy ground", "Helium-3", "Icy", "—", "None required", "White-dwarf primary (D–DX)", 96_223, 553_040),
        new("Icy ground", "Tritium", "Icy", "—", "None required", "—", 53_311, 61_894),
        new("Icy ground", "Deuterium", "Icy", "Rocky or Rocky ice", "None required", "—", 40_762, 273_368),
        new("Icy ground", "Methanol Monohydrate Crystals", "Rocky ice", "Icy", "None required", "—", 2_525, 3_794),
        new("Icy ground", "Water", "Icy", "Rocky ice", "None required", "—", 496, 2_964),
    ];

    public static bool TryResolve(string value, out SurfaceMiningCommodity commodity)
    {
        var normalized = value.Trim();
        if (Aliases.TryGetValue(normalized, out var canonical))
        {
            normalized = canonical;
        }

        commodity = All.FirstOrDefault(candidate => string.Equals(
            candidate.Name,
            normalized,
            StringComparison.OrdinalIgnoreCase))!;
        return commodity is not null;
    }

    private static SurfaceMiningCommodity CreateCommodity(
        SurfaceMiningHuntReference reference)
    {
        var bodyTypes = reference.StartWith + " " + reference.AlsoPossibleOn;
        return new SurfaceMiningCommodity(
            Categories[reference.Material],
            reference.Material,
            bodyTypes.Contains("High-metal-content", StringComparison.OrdinalIgnoreCase),
            bodyTypes.Contains("Metal-rich", StringComparison.OrdinalIgnoreCase),
            ContainsBodyType(bodyTypes, "Rocky"),
            bodyTypes.Contains("Rocky ice", StringComparison.OrdinalIgnoreCase),
            ContainsBodyType(bodyTypes, "Icy"),
            reference.AverageGalacticPrice,
            reference.PeakSellPrice,
            Colors[reference.Material]);
    }

    private static bool ContainsBodyType(string value, string bodyType)
    {
        var searchable = bodyType.Equals("Rocky", StringComparison.OrdinalIgnoreCase)
            ? value.Replace("Rocky ice", string.Empty, StringComparison.OrdinalIgnoreCase)
            : value;
        return searchable.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals(bodyType, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Low Temp Diamonds"] = "Low Temperature Diamonds",
            ["Methanol Crystals"] = "Methanol Monohydrate Crystals",
        };

    private static readonly IReadOnlyDictionary<string, string> Categories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Helium"] = "Chemicals",
            ["Helium-3"] = "Chemicals",
            ["Tritium"] = "Chemicals",
            ["Water"] = "Chemicals",
            ["Iridium"] = "Metals",
            ["Platinum"] = "Metals",
            ["Palladium"] = "Metals",
            ["Gold"] = "Metals",
            ["Osmium"] = "Metals",
            ["Silver"] = "Metals",
            ["Samarium"] = "Metals",
            ["Tantalum"] = "Metals",
            ["Thorium"] = "Metals",
            ["Uranium"] = "Metals",
            ["Titanium"] = "Metals",
            ["Lithium"] = "Metals",
            ["Copper"] = "Metals",
            ["Diamond"] = "Minerals",
            ["Sapphire"] = "Minerals",
            ["Ruby"] = "Minerals",
            ["Rhodplumsite"] = "Minerals",
            ["Monazite"] = "Minerals",
            ["Alexandrite"] = "Minerals",
            ["Periclase Dunite"] = "Minerals",
            ["Serendibite"] = "Minerals",
            ["Bastnasite"] = "Minerals",
            ["Quartz Pyroxenite"] = "Minerals",
            ["Jadeite"] = "Minerals",
            ["Olivine"] = "Minerals",
            ["Grandidierite"] = "Minerals",
            ["Thortveitite"] = "Minerals",
            ["Low Temperature Diamonds"] = "Minerals",
            ["Deuterium"] = "Minerals",
            ["Magnesite"] = "Minerals",
            ["Uraninite"] = "Minerals",
            ["Haematite"] = "Minerals",
            ["Methanol Monohydrate Crystals"] = "Minerals",
        };

    private static readonly IReadOnlyDictionary<string, string> Colors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Helium"] = "#8EEBFF",
            ["Helium-3"] = "#64D8FF",
            ["Tritium"] = "#00B8D4",
            ["Water"] = "#2196F3",
            ["Iridium"] = "#D7E3EA",
            ["Platinum"] = "#E5E4E2",
            ["Palladium"] = "#C8B7D8",
            ["Gold"] = "#FFD700",
            ["Osmium"] = "#6F8FAF",
            ["Silver"] = "#C0C0C0",
            ["Samarium"] = "#C89B6D",
            ["Tantalum"] = "#8B6F8E",
            ["Thorium"] = "#7FA86B",
            ["Uranium"] = "#9ACD32",
            ["Titanium"] = "#8C9AA5",
            ["Lithium"] = "#D8B4F8",
            ["Copper"] = "#B87333",
            ["Thortveitite"] = "#7C4DFF",
            ["Periclase Dunite"] = "#8BC34A",
            ["Monazite"] = "#2E8B57",
            ["Rhodplumsite"] = "#D81B60",
            ["Diamond"] = "#E8FFFF",
            ["Alexandrite"] = "#00A86B",
            ["Sapphire"] = "#1565C0",
            ["Ruby"] = "#D32F2F",
            ["Grandidierite"] = "#00A7A7",
            ["Serendibite"] = "#5E35B1",
            ["Bastnasite"] = "#F57C00",
            ["Low Temperature Diamonds"] = "#80DEEA",
            ["Quartz Pyroxenite"] = "#CE93D8",
            ["Deuterium"] = "#42A5F5",
            ["Magnesite"] = "#F5F5DC",
            ["Olivine"] = "#9BAF3F",
            ["Jadeite"] = "#00C853",
            ["Uraninite"] = "#607D8B",
            ["Haematite"] = "#A44A3F",
            ["Methanol Monohydrate Crystals"] = "#B2EBF2",
        };

    public static IReadOnlyList<SurfaceMiningCommodity> All { get; } =
        HuntReferences.Select(CreateCommodity)
            .OrderBy(commodity => commodity.Category switch
            {
                "Chemicals" => 0,
                "Metals" => 1,
                _ => 2,
            })
            .ThenByDescending(commodity => commodity.MaximumSellPrice)
            .ThenBy(commodity => commodity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
