using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class RingMiningCatalogTests
{
    private static readonly string[] MixedMinerals = ["Platinum", "Osmium"];

    [Theory]
    [InlineData("Osmium", false)]
    [InlineData("Platinum", true)]
    [InlineData("", true)]
    public void ACoreInAnotherCommodityDoesNotSatisfyTheSelectedCommodity(string selected, bool expected)
    {
        string[] selection = selected.Length == 0 ? [] : [selected];
        Assert.Equal(
            expected,
            RingMiningCatalog.SupportsAny("Metallic", MixedMinerals, RingMiningMethod.Core, selection)
        );
    }

    [Theory]
    [InlineData("eRingClass_MetalRich", "Platinum", RingMiningMethod.Core, true)]
    [InlineData("Metal-rich", "Platinum", RingMiningMethod.Laser, false)]
    [InlineData("Icy", "$opal_name;", RingMiningMethod.Core, true)]
    [InlineData("Unknown", "Platinum", RingMiningMethod.Laser, false)]
    public void CompatibilityAcceptsJournalNamesWithoutInventingUnknownCompositions(
        string ring,
        string mineral,
        RingMiningMethod method,
        bool expected
    ) => Assert.Equal(expected, RingMiningCatalog.Supports(ring, mineral, method));
}
