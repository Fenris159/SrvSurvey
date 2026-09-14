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
        MarketSnapshot market = Market(
            42,
            [
                Item("$Steel_Name;", stock: 80, producer: true),
                Item("$Water_Name;", stock: 0),
                Item("$Gold_Name;", stock: 0, consumer: true),
                Item("$Titanium_Name;", stock: 20, producer: true),
            ]
        );

        IReadOnlyDictionary<string, int> replacement =
            ColonizationFleetCarrierCargoSynchronizer.CreateMarketReplacement(market, carrier);

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

        IReadOnlyDictionary<string, int> unchanged = ColonizationFleetCarrierCargoSynchronizer.CreateMarketReplacement(
            Market(42, [Item("$Steel_Name;", 75, producer: true)]),
            carrier
        );
        Assert.Empty(unchanged);

        Assert.Throws<ArgumentException>(() =>
            ColonizationFleetCarrierCargoSynchronizer.CreateMarketReplacement(Market(99, []), carrier)
        );
    }

    [Fact]
    public void CreatesLegacyMarketAndMainShipTransferAdjustments()
    {
        ColonizationDockingSnapshot dock = Dock();

        IReadOnlyDictionary<string, int> bought = ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
            Event("MarketBuy", "\"MarketID\":42,\"Type\":\"$Steel_Name;\",\"Count\":5"),
            dock,
            isInMainShip: true
        );
        IReadOnlyDictionary<string, int> sold = ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
            Event("MarketSell", "\"MarketID\":42,\"Type\":\"Water\",\"Count\":2"),
            dock,
            isInMainShip: true
        );
        IReadOnlyDictionary<string, int> transferred =
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                Event(
                    "CargoTransfer",
                    """
                    "Transfers":[
                      {"Type":"$Steel_Name;","Count":4,"Direction":"tocarrier"},
                      {"Type":"Water","Count":3,"Direction":"toship"}]
                    """
                ),
                dock,
                isInMainShip: true
            );

        Assert.Equal(-5, bought["steel"]);
        Assert.Equal(2, sold["water"]);
        Assert.Equal(4, transferred["steel"]);
        Assert.Equal(-3, transferred["water"]);
    }

    [Fact]
    public void RefusesSrvAndMalformedTransfersAndSkipsAllSquadronTransfers()
    {
        JournalEventEnvelope transfer = Event(
            "CargoTransfer",
            """
            "Transfers":[{"Type":"Steel","Count":4,"Direction":"tocarrier"}]
            """
        );
        JournalEventEnvelope mixedTransfer = Event(
            "CargoTransfer",
            """
            "Transfers":[
              {"Type":"Steel","Count":4,"Direction":"tocarrier"},
              {"Type":"Water","Count":3,"Direction":"toship"}]
            """
        );

        Assert.Empty(
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(transfer, Dock(), isInMainShip: false)
        );
        // Squadron carriers prefer ship cargo GetDiff when available.
        Assert.Empty(
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                transfer,
                Dock("squadronBank"),
                isInMainShip: true
            )
        );
        Assert.Empty(
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                mixedTransfer,
                Dock("squadronBank"),
                isInMainShip: true,
                preferShipCargoDiffForSquadron: true
            )
        );
        // When ship-cargo diff is unavailable (shared cargo suppressed), journal fallback.
        IReadOnlyDictionary<string, int> squadronFallback =
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                mixedTransfer,
                Dock("squadronBank"),
                isInMainShip: true,
                preferShipCargoDiffForSquadron: false
            );
        Assert.Equal(4, squadronFallback["steel"]);
        Assert.Equal(-3, squadronFallback["water"]);
        Assert.Throws<InvalidDataException>(() =>
            ColonizationFleetCarrierCargoSynchronizer.CreateJournalAdjustment(
                Event("CargoTransfer", "\"Transfers\":[{\"Type\":\"Steel\",\"Direction\":\"tocarrier\"}]"),
                Dock(),
                isInMainShip: true
            )
        );
    }

    [Fact]
    public void DetectsSquadronFleetCarriersAndInvertsShipDiffForCarrierSupply()
    {
        Assert.True(ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(Dock("squadronBank")));
        Assert.False(ColonizationFleetCarrierCargoSynchronizer.IsSquadronFleetCarrier(Dock()));

        var shipDiff = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["steel"] = -10, ["water"] = 3 };
        IReadOnlyDictionary<string, int> fcDiff =
            ColonizationFleetCarrierCargoSynchronizer.CreateSquadronCargoDiffAdjustment(shipDiff);

        Assert.Equal(10, fcDiff["steel"]);
        Assert.Equal(-3, fcDiff["water"]);
    }

    private static MarketSnapshot Market(long marketId, IReadOnlyList<MarketItem> items)
    {
        return new MarketSnapshot(
            DateTimeOffset.Parse("2026-07-24T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            "Market",
            marketId,
            "Test Carrier",
            "FleetCarrier",
            "all",
            "Test System",
            items
        );
    }

    private static MarketItem Item(string name, int stock, bool producer = false, bool consumer = false)
    {
        return new MarketItem(1, name, null, string.Empty, null, 0, 0, 0, 0, 0, stock, 0, producer, consumer, false);
    }

    private static ColonizationDockingSnapshot Dock(params string[] stationServices)
    {
        return new ColonizationDockingSnapshot(
            42,
            20,
            "Test",
            "Supply carrier",
            null,
            stationServices,
            DateTimeOffset.Parse("2026-07-24T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            "FleetCarrier"
        );
    }

    private static JournalEventEnvelope Event(string eventName, string properties)
    {
        string json = $$"""
            {"timestamp":"2026-07-24T12:00:00Z","event":"{{eventName}}",{{properties}}}
            """;
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? result, out string? error), error);
        return result!;
    }
}
