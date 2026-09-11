using System.Diagnostics.CodeAnalysis;

namespace SrvSurvey.Core.Mining;

public sealed record SurfaceMiningHuntReference(
    string SearchGroup,
    string Material,
    string StartWith,
    string AlsoPossibleOn,
    string Geology,
    string SpecialClue,
    int AverageGalacticPrice,
    int PeakSellPrice
);

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
    string ColorHex
);

public static class SurfaceMiningCommodityCatalog
{
    private const string MetalRichBodyType = "Metal-rich";
    private const string HighMetalContentBodyType = "High-metal-content";
    private const string RockyBodyType = "Rocky";
    private const string RockyIceBodyType = "Rocky ice";
    private const string NoGeologyRequired = "None required";
    private const string SilicateOrIronMagma = "Silicate magma or Iron magma";
    private const string IronMagma = "Iron magma";
    private const string MetalBearingGround = "Metal-bearing ground";
    private const string RockyVolcanicGround = "Rocky / volcanic ground";
    private const string RockyGround = "Rocky ground";
    private const string IcyGround = "Icy ground";
    private const string LowTemperatureDiamonds = "Low Temperature Diamonds";
    private const string MethanolMonohydrateCrystals = "Methanol Monohydrate Crystals";
    private const string ChemicalsCategory = "Chemicals";
    private const string MetalsCategory = "Metals";
    private const string MineralsCategory = "Minerals";

    public static IReadOnlyList<SurfaceMiningHuntReference> HuntReferences { get; } =
    [
        new(
            "Volcanic gemstones",
            "Diamond",
            MetalRichBodyType,
            "High-metal-content, Rocky, or Rocky ice",
            SilicateOrIronMagma,
            "—",
            134_784,
            720_648
        ),
        new(
            "Volcanic gemstones",
            "Sapphire",
            MetalRichBodyType,
            "High-metal-content or Rocky",
            IronMagma,
            "—",
            128_050,
            648_352
        ),
        new(
            "Volcanic gemstones",
            "Ruby",
            MetalRichBodyType,
            "High-metal-content or Rocky",
            IronMagma,
            "—",
            110_381,
            589_240
        ),
        new(
            MetalBearingGround,
            "Iridium",
            MetalRichBodyType,
            HighMetalContentBodyType,
            NoGeologyRequired,
            "—",
            208_463,
            1_038_104
        ),
        new(
            MetalBearingGround,
            "Rhodplumsite",
            MetalRichBodyType,
            HighMetalContentBodyType,
            NoGeologyRequired,
            "Do not expect alongside Platinum",
            187_921,
            826_249
        ),
        new(
            MetalBearingGround,
            "Helium",
            HighMetalContentBodyType,
            "Metal-rich, Rocky, or Rocky ice",
            "CO₂ geysers, Ammonia geysers, Methane geysers, Nitrogen geysers, Helium geysers, or Silicate-vapour geysers",
            "—",
            102_861,
            591_360
        ),
        new(
            MetalBearingGround,
            "Platinum",
            MetalRichBodyType,
            HighMetalContentBodyType,
            NoGeologyRequired,
            "—",
            70_998,
            333_030
        ),
        new(MetalBearingGround, "Osmium", HighMetalContentBodyType, MetalRichBodyType, IronMagma, "—", 56_471, 273_000),
        new(
            MetalBearingGround,
            "Gold",
            "Metal-rich or High-metal-content",
            RockyBodyType,
            NoGeologyRequired,
            "—",
            48_005,
            282_678
        ),
        new(
            MetalBearingGround,
            "Silver",
            HighMetalContentBodyType,
            "Metal-rich or Rocky",
            NoGeologyRequired,
            "—",
            37_743,
            219_318
        ),
        new(
            MetalBearingGround,
            "Samarium",
            "Metal-rich or High-metal-content",
            RockyBodyType,
            NoGeologyRequired,
            "—",
            28_362,
            154_266
        ),
        new(
            MetalBearingGround,
            "Tantalum",
            HighMetalContentBodyType,
            MetalRichBodyType,
            NoGeologyRequired,
            "—",
            14_360,
            15_582
        ),
        new(
            MetalBearingGround,
            "Thorium",
            HighMetalContentBodyType,
            "Metal-rich or Rocky",
            NoGeologyRequired,
            "—",
            12_297,
            12_917
        ),
        new(
            MetalBearingGround,
            "Uranium",
            HighMetalContentBodyType,
            MetalRichBodyType,
            NoGeologyRequired,
            "—",
            7_599,
            8_587
        ),
        new(
            MetalBearingGround,
            "Titanium",
            HighMetalContentBodyType,
            MetalRichBodyType,
            NoGeologyRequired,
            "—",
            4_800,
            5_865
        ),
        new(
            MetalBearingGround,
            "Haematite",
            HighMetalContentBodyType,
            "Metal-rich, Rocky, or Rocky ice",
            NoGeologyRequired,
            "—",
            2_800,
            10_044
        ),
        new(
            MetalBearingGround,
            "Lithium",
            HighMetalContentBodyType,
            MetalRichBodyType,
            NoGeologyRequired,
            "—",
            2_099,
            2_892
        ),
        new(
            MetalBearingGround,
            "Copper",
            HighMetalContentBodyType,
            "Metal-rich, Rocky, or Rocky ice",
            NoGeologyRequired,
            "—",
            774,
            1_931
        ),
        new(RockyVolcanicGround, "Monazite", RockyBodyType, "—", SilicateOrIronMagma, "—", 268_661, 865_908),
        new(RockyVolcanicGround, "Alexandrite", RockyBodyType, "—", SilicateOrIronMagma, "—", 229_207, 714_088),
        new(
            RockyVolcanicGround,
            "Periclase Dunite",
            RockyBodyType,
            "—",
            IronMagma,
            "White-dwarf primary (D–DX)",
            204_168,
            1_038_104
        ),
        new(RockyVolcanicGround, "Serendibite", RockyBodyType, "—", SilicateOrIronMagma, "—", 188_438, 570_948),
        new(RockyVolcanicGround, "Bastnasite", RockyBodyType, "—", SilicateOrIronMagma, "—", 78_583, 531_208),
        new(RockyVolcanicGround, "Quartz Pyroxenite", RockyBodyType, RockyIceBodyType, IronMagma, "—", 46_469, 312_072),
        new(RockyVolcanicGround, "Jadeite", RockyBodyType, "—", "Silicate-vapour geysers", "—", 42_770, 179_421),
        new(RockyVolcanicGround, "Olivine", "Rocky or Rocky ice", "—", SilicateOrIronMagma, "—", 31_417, 209_936),
        new(
            RockyGround,
            "Grandidierite",
            RockyBodyType,
            "—",
            NoGeologyRequired,
            "Trace iron in body composition",
            213_547,
            571_800
        ),
        new(
            RockyGround,
            "Thortveitite",
            RockyBodyType,
            "—",
            NoGeologyRequired,
            "Trace yttrium in body composition",
            203_892,
            1_038_104
        ),
        new(
            RockyGround,
            "Palladium",
            RockyBodyType,
            "Metal-rich, High-metal-content, or Rocky ice",
            NoGeologyRequired,
            "—",
            52_167,
            302_136
        ),
        new(RockyGround, "Magnesite", RockyBodyType, "—", NoGeologyRequired, "—", 38_198, 255_880),
        new(RockyGround, "Uraninite", RockyBodyType, "—", NoGeologyRequired, "—", 3_006, 17_166),
        new(IcyGround, LowTemperatureDiamonds, "Icy", "Rocky or Rocky ice", NoGeologyRequired, "—", 130_184, 384_562),
        new(IcyGround, "Helium-3", "Icy", "—", NoGeologyRequired, "White-dwarf primary (D–DX)", 96_223, 553_040),
        new(IcyGround, "Tritium", "Icy", "—", NoGeologyRequired, "—", 53_311, 61_894),
        new(IcyGround, "Deuterium", "Icy", "Rocky or Rocky ice", NoGeologyRequired, "—", 40_762, 273_368),
        new(IcyGround, MethanolMonohydrateCrystals, RockyIceBodyType, "Icy", NoGeologyRequired, "—", 2_525, 3_794),
        new(IcyGround, "Water", "Icy", RockyIceBodyType, NoGeologyRequired, "—", 496, 2_964),
    ];

    public static bool TryResolve(string value, [MaybeNullWhen(false)] out SurfaceMiningCommodity commodity)
    {
        var normalized = value.Trim();
        if (Aliases.TryGetValue(normalized, out var canonical))
        {
            normalized = canonical;
        }

        commodity = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase)
        )!;
        return commodity is not null;
    }

    private static SurfaceMiningCommodity CreateCommodity(SurfaceMiningHuntReference reference)
    {
        var bodyTypes = reference.StartWith + " " + reference.AlsoPossibleOn;
        return new SurfaceMiningCommodity(
            Categories[reference.Material],
            reference.Material,
            bodyTypes.Contains(HighMetalContentBodyType, StringComparison.OrdinalIgnoreCase),
            bodyTypes.Contains(MetalRichBodyType, StringComparison.OrdinalIgnoreCase),
            ContainsBodyType(bodyTypes, RockyBodyType),
            bodyTypes.Contains(RockyIceBodyType, StringComparison.OrdinalIgnoreCase),
            ContainsBodyType(bodyTypes, "Icy"),
            reference.AverageGalacticPrice,
            reference.PeakSellPrice,
            Colors[reference.Material]
        );
    }

    private static bool ContainsBodyType(string value, string bodyType)
    {
        var searchable = bodyType.Equals(RockyBodyType, StringComparison.OrdinalIgnoreCase)
            ? value.Replace(RockyIceBodyType, string.Empty, StringComparison.OrdinalIgnoreCase)
            : value;
        return searchable
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals(bodyType, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Low Temp Diamonds"] = LowTemperatureDiamonds,
        ["Methanol Crystals"] = MethanolMonohydrateCrystals,
    };

    private static readonly Dictionary<string, string> Categories = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Helium"] = ChemicalsCategory,
        ["Helium-3"] = ChemicalsCategory,
        ["Tritium"] = ChemicalsCategory,
        ["Water"] = ChemicalsCategory,
        ["Iridium"] = MetalsCategory,
        ["Platinum"] = MetalsCategory,
        ["Palladium"] = MetalsCategory,
        ["Gold"] = MetalsCategory,
        ["Osmium"] = MetalsCategory,
        ["Silver"] = MetalsCategory,
        ["Samarium"] = MetalsCategory,
        ["Tantalum"] = MetalsCategory,
        ["Thorium"] = MetalsCategory,
        ["Uranium"] = MetalsCategory,
        ["Titanium"] = MetalsCategory,
        ["Lithium"] = MetalsCategory,
        ["Copper"] = MetalsCategory,
        ["Diamond"] = MineralsCategory,
        ["Sapphire"] = MineralsCategory,
        ["Ruby"] = MineralsCategory,
        ["Rhodplumsite"] = MineralsCategory,
        ["Monazite"] = MineralsCategory,
        ["Alexandrite"] = MineralsCategory,
        ["Periclase Dunite"] = MineralsCategory,
        ["Serendibite"] = MineralsCategory,
        ["Bastnasite"] = MineralsCategory,
        ["Quartz Pyroxenite"] = MineralsCategory,
        ["Jadeite"] = MineralsCategory,
        ["Olivine"] = MineralsCategory,
        ["Grandidierite"] = MineralsCategory,
        ["Thortveitite"] = MineralsCategory,
        [LowTemperatureDiamonds] = MineralsCategory,
        ["Deuterium"] = MineralsCategory,
        ["Magnesite"] = MineralsCategory,
        ["Uraninite"] = MineralsCategory,
        ["Haematite"] = MineralsCategory,
        [MethanolMonohydrateCrystals] = MineralsCategory,
    };

    private static readonly Dictionary<string, string> Colors = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
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
        [LowTemperatureDiamonds] = "#80DEEA",
        ["Quartz Pyroxenite"] = "#CE93D8",
        ["Deuterium"] = "#42A5F5",
        ["Magnesite"] = "#F5F5DC",
        ["Olivine"] = "#9BAF3F",
        ["Jadeite"] = "#00C853",
        ["Uraninite"] = "#607D8B",
        ["Haematite"] = "#A44A3F",
        [MethanolMonohydrateCrystals] = "#B2EBF2",
    };

    public static IReadOnlyList<SurfaceMiningCommodity> All { get; } =
        HuntReferences
            .Select(CreateCommodity)
            .OrderBy(commodity =>
                commodity.Category switch
                {
                    ChemicalsCategory => 0,
                    MetalsCategory => 1,
                    _ => 2,
                }
            )
            .ThenByDescending(commodity => commodity.MaximumSellPrice)
            .ThenBy(commodity => commodity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
