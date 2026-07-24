using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationFleetCarrierCargoSynchronizerTests
{
    [Fact]
    public void ReplacesChangedSaleStockAndClearsExhaustedStock()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new Dictionary<string, int>
            {
                ["steel"] = 75,
                ["water"] = 10,
                ["gold"] = 5,
            },
        };
        var market = Market(
            42,
            [
                Item("$Steel_Name;", stock: 80, producer: true),
                Item("$Water_Name;", stock: 0),
                Item("$Gold_Name;", stock: 0, consumer: true),
                Item("$Titanium_Name;", stock: 20, producer: true),
            ]);

        var replacement =
            ColonizationFleetCarrierCargoSynchronizer
                .CreateMarketReplacement(market, carrier);

        Assert.Equal(3, replacement.Count);
        Assert.Equal(80, replacement["steel"]);
        Assert.Equal(0, replacement["water"]);
        Assert.Equal(20, replacement["titanium"]);
        Assert.False(replacement.ContainsKey("gold"));
    }

    [Fact]
    public void OmitsUnchangedCargoAndRejectsWrongCarrier()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new Dictionary<string, int> { ["steel"] = 75 },
        };

        var unchanged =
            ColonizationFleetCarrierCargoSynchronizer
                .CreateMarketReplacement(
                    Market(
                        42,
                        [Item("$Steel_Name;", 75, producer: true)]),
                    carrier);
        Assert.Empty(unchanged);

        Assert.Throws<ArgumentException>(() =>
            ColonizationFleetCarrierCargoSynchronizer
                .CreateMarketReplacement(
                    Market(99, []),
                    carrier));
    }

    private static MarketSnapshot Market(
        long marketId,
        IReadOnlyList<MarketItem> items)
    {
        return new MarketSnapshot(
            DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            "Market",
            marketId,
            "Test Carrier",
            "FleetCarrier",
            "all",
            "Test System",
            items);
    }

    private static MarketItem Item(
        string name,
        int stock,
        bool producer = false,
        bool consumer = false)
    {
        return new MarketItem(
            1,
            name,
            null,
            string.Empty,
            null,
            0,
            0,
            0,
            0,
            0,
            stock,
            0,
            producer,
            consumer,
            false);
    }
}
