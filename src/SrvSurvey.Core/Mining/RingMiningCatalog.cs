namespace SrvSurvey.Core.Mining;

public enum RingMiningMethod
{
    Laser,
    Core,
}

public sealed record RingMiningCommodity(string Name, int AverageSellPrice);

public sealed record RingMiningComposition(
    string RingType,
    IReadOnlyList<RingMiningCommodity> Laser,
    IReadOnlyList<RingMiningCommodity> Core
);

/// <summary>Shared ring composition reference for extraction filters and the mining guide.</summary>
public static class RingMiningCatalog
{
    public static IReadOnlyList<RingMiningComposition> All { get; } =
    [
        Ring(
            "Icy",
            [
                Item("Low Temperature Diamonds", 131607),
                Item("Bromellite", 33414),
                Item("Hydrogen Peroxide", 3119),
                Item("Liquid Oxygen", 1639),
                Item("Lithium Hydroxide", 5655),
                Item("Methane Clathrate", 1597),
                Item("Methanol Monohydrate Crystals", 2522),
                Item("Tritium", 53425),
                Item("Water", 496),
            ],
            [
                Item("Alexandrite", 227771),
                Item("Grandidierite", 211582),
                Item("Low Temperature Diamonds", 131607),
                Item("Void Opals", 150964),
                Item("Bromellite", 33414),
            ]
        ),
        Ring(
            "Metallic",
            [
                Item("Osmium", 55698),
                Item("Painite", 57890),
                Item("Platinum", 70136),
                Item("Bertrandite", 18476),
                Item("Gold", 47900),
                Item("Indite", 11268),
                Item("Palladium", 52064),
                Item("Praseodymium", 8636),
                Item("Samarium", 28658),
                Item("Silver", 37628),
            ],
            [
                Item("Monazite", 273262),
                Item("Rhodplumsite", 186345),
                Item("Serendibite", 186953),
                Item("Painite", 57890),
                Item("Platinum", 70136),
            ]
        ),
        Ring(
            "MetalRich",
            [
                Item("Osmium", 55698),
                Item("Bertrandite", 18476),
                Item("Coltan", 6144),
                Item("Gallite", 12235),
                Item("Gold", 47900),
                Item("Indite", 11268),
                Item("Lepidolite", 1796),
                Item("Praseodymium", 8636),
                Item("Samarium", 28658),
                Item("Silver", 37628),
                Item("Uraninite", 3004),
            ],
            [
                Item("Alexandrite", 227771),
                Item("Benitoite", 164647),
                Item("Monazite", 273262),
                Item("Rhodplumsite", 186345),
                Item("Serendibite", 186953),
                Item("Painite", 57890),
                Item("Platinum", 70136),
            ]
        ),
        Ring(
            "Rocky",
            [
                Item("Bertrandite", 18476),
                Item("Bauxite", 2092),
                Item("Coltan", 6144),
                Item("Gallite", 12235),
                Item("Indite", 11268),
                Item("Lepidolite", 1796),
                Item("Rutile", 3084),
            ],
            [
                Item("Alexandrite", 227771),
                Item("Benitoite", 164647),
                Item("Monazite", 273262),
                Item("Musgravite", 220251),
                Item("Serendibite", 186953),
            ]
        ),
    ];

    public static RingMiningComposition? Find(string ringType)
    {
        string key = NormalizeRing(ringType);
        return All.FirstOrDefault(ring => NormalizeRing(ring.RingType) == key);
    }

    public static bool Supports(string ringType, string mineral, RingMiningMethod method)
    {
        RingMiningComposition? ring = Find(ringType);
        if (ring is null)
        {
            return false;
        }
        IReadOnlyList<RingMiningCommodity> minerals = method == RingMiningMethod.Core ? ring.Core : ring.Laser;
        return minerals.Any(item => MiningCommodityName.Same(item.Name, mineral));
    }

    public static bool SupportsAny(
        string ringType,
        IEnumerable<string> observedMinerals,
        RingMiningMethod method,
        IReadOnlyList<string> selectedMinerals
    ) =>
        observedMinerals.Any(name =>
            (selectedMinerals.Count == 0 || selectedMinerals.Any(selected => MiningCommodityName.Same(name, selected)))
            && Supports(ringType, name, method)
        );

    private static string NormalizeRing(string value) =>
        new(
            value
                .Replace("eRingClass_", "", StringComparison.OrdinalIgnoreCase)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray()
        );

    private static RingMiningComposition Ring(
        string ringType,
        IReadOnlyList<RingMiningCommodity> laser,
        IReadOnlyList<RingMiningCommodity> core
    ) => new(ringType, laser, core);

    private static RingMiningCommodity Item(string name, int averageSellPrice) => new(name, averageSellPrice);
}
