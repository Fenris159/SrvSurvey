using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationLocalCarrierCargoTests
{
    [Fact]
    public void DockedCapiRefreshDoesNotOverwriteLiveJournalTotals()
    {
        var local = new ColonizationLocalCarrierCargo();
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 100, 0)), At(10), isDocked: true);
        local.Observe(Event("MarketBuy", 20, "\"MarketID\":42,\"Type\":\"steel\",\"Count\":10"), Dock, true);
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 100, 0)), At(30), isDocked: true);
        Assert.Equal(90, local.GetQuantity(RavenCarrier, "steel"));
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 80, 0)), At(40));
        Assert.Equal(80, local.GetQuantity(RavenCarrier, "steel"));
    }

    private static readonly ColonizationFleetCarrier RavenCarrier = new() { MarketId = 42, Name = "ABC-123" };
    private static readonly ColonizationDockingSnapshot Dock = new(
        42,
        1,
        "Sol",
        "ABC-123",
        null,
        [],
        StationType: "FleetCarrier"
    );

    [Fact]
    public void CapiProvidesCompleteTotalsIncludingZeroAndMapsAliasesWithoutUsingRavenAmounts()
    {
        var local = new ColonizationLocalCarrierCargo();
        Assert.Null(local.GetQuantity(RavenCarrier, "steel"));
        Assert.Null(local.GetTotal(RavenCarrier, ["steel"]));
        local.Observe(
            Carrier(
                42,
                new FrontierInventorySnapshot("Commodity", "$Steel_Name;", 12, 0),
                new FrontierInventorySnapshot("Commodity", "steel", 3, 0),
                new FrontierInventorySnapshot("Commodity", "Micro Controllers", 7, 0)
            ),
            At(10)
        );
        Assert.Equal(15, local.GetQuantity(RavenCarrier, "steel"));
        Assert.Equal(7, local.GetQuantity(RavenCarrier, "microcontrollers"));
        Assert.Equal(0, local.GetQuantity(RavenCarrier, "water"));
        Assert.Equal(
            22,
            local.GetTotal(RavenCarrier, ["steel", "Steel", "microcontrollers", "micro controllers", "water"])
        );
        Assert.Equal(15, local.GetQuantity(RavenCarrier with { Cargo = new() { ["steel"] = 999 } }, "steel"));
        Assert.Null(local.GetQuantity(RavenCarrier with { MarketId = 43 }, "steel"));
        local.Clear();
        Assert.Null(local.GetQuantity(RavenCarrier, "steel"));
    }

    [Fact]
    public void ReplaysNewerJournalTransactionsOverLateCapiBaselinesAndDeduplicatesSourceRecords()
    {
        var local = new ColonizationLocalCarrierCargo();
        JournalEventEnvelope buy = Event("MarketBuy", 20, "\"MarketID\":42,\"Type\":\"steel\",\"Count\":10");
        local.Observe(buy, Dock, true);
        local.Observe(buy, Dock, true);
        local.Observe(Event("MarketSell", 21, "\"MarketID\":42,\"Type\":\"steel\",\"Count\":4"), Dock, true);
        local.Observe(
            Event(
                "CargoTransfer",
                22,
                "\"Transfers\":[{\"Type\":\"steel\",\"Count\":30,\"Direction\":\"tocarrier\"},{\"Type\":\"steel\",\"Count\":9,\"Direction\":\"toship\"}]"
            ),
            Dock,
            true
        );
        Assert.Null(local.GetQuantity(RavenCarrier, "steel"));
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 100, 0)), At(10));
        Assert.Equal(115, local.GetQuantity(RavenCarrier, "steel"));
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 1, 0)), At(5));
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 1, 0)), At(10));
        Assert.Equal(115, local.GetQuantity(RavenCarrier, "steel"));
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 200, 0)), At(18));
        Assert.Equal(215, local.GetQuantity(RavenCarrier, "steel"));
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "steel", 300, 0)), At(25));
        local.Observe(buy, Dock, true);
        Assert.Equal(300, local.GetQuantity(RavenCarrier, "steel"));
        JournalEventEnvelope sell = Event("MarketSell", 26, "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5") with
        {
            SourceRecordIdentity = "journal:1",
        };
        local.Observe(sell, Dock, true);
        local.Observe(sell with { SourceRecordIdentity = "journal:2" }, Dock, true);
        local.Observe(sell, Dock, true);
        Assert.Equal(310, local.GetQuantity(RavenCarrier, "steel"));
    }

    [Fact]
    public void ResolvesCallsignUntilMarketIdentityIsKnownAndTracksSquadronTransfersOnce()
    {
        var local = new ColonizationLocalCarrierCargo();
        local.Observe(Carrier(null, new FrontierInventorySnapshot("Commodity", "water", 20, 0)), At(10));
        Assert.Equal(20, local.GetQuantity(RavenCarrier, "water"));
        local.Observe(
            Event("CargoTransfer", 20, "\"Transfers\":[{\"Type\":\"water\",\"Count\":6,\"Direction\":\"toship\"}]"),
            Dock with
            {
                StationServices = ["squadronBank"],
            },
            true
        );
        Assert.Equal(14, local.GetQuantity(RavenCarrier, "water"));
        Assert.Null(local.GetQuantity(RavenCarrier with { MarketId = 43 }, "water"));
        local.Observe(Carrier(42, new FrontierInventorySnapshot("Commodity", "water", 30, 0)), At(15));
        Assert.Equal(24, local.GetQuantity(RavenCarrier, "water"));
    }

    [Fact]
    public void IgnoresUnusableEventsAndDoesNotClaimNegativeOrUnknownQuantitiesAreZero()
    {
        var local = new ColonizationLocalCarrierCargo();
        local.Observe(null, At(10));
        local.Observe(Carrier(42) with { Callsign = "" }, At(10));
        local.Observe(Carrier(42), At(10));
        JournalEventEnvelope buy = Event("MarketBuy", 20, "\"MarketID\":42,\"Type\":\"steel\",\"Count\":10");
        local.Observe(buy, null, true);
        local.Observe(buy with { Timestamp = null }, Dock, true);
        local.Observe(Event("Docked", 20, ""), Dock, true);
        local.Observe(buy, Dock with { StationType = "Orbis" }, true);
        local.Observe(buy, Dock with { MarketId = 43 }, true);
        local.Observe(Event("MarketBuy", 20, "\"MarketID\":42,\"Type\":\"steel\",\"Count\":0"), Dock, true);
        local.Observe(Event("MarketBuy", 20, "\"MarketID\":42,\"Count\":\"wrong type\""), Dock, true);
        Assert.Equal(0, local.GetQuantity(RavenCarrier, "steel"));
        local.Observe(buy, Dock, true);
        Assert.Null(local.GetQuantity(RavenCarrier, "steel"));
        Assert.Null(local.GetTotal(RavenCarrier, ["steel"]));
        local.Observe(Event("MarketSell", 21, "\"MarketID\":42,\"Type\":\"steel\",\"Count\":15"), Dock, true);
        Assert.Equal(5, local.GetQuantity(RavenCarrier, "steel"));
    }

    internal static DateTimeOffset At(int seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    private static JournalEventEnvelope Event(string name, int seconds, string fields)
    {
        string json = "{\"event\":\"" + name + "\"" + (fields.Length > 0 ? "," + fields : "") + "}";
        using var document = JsonDocument.Parse(json);
        return new(name, At(seconds), json, document.RootElement.Clone());
    }

    internal static FrontierCarrierSnapshot Carrier(long? marketId, params FrontierInventorySnapshot[] cargo) =>
        new(
            "ABC-123",
            "Supply",
            "Sol",
            "NormalOperation",
            "All",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            cargo,
            [],
            [],
            [],
            [],
            Market: new(marketId, "ABC-123", "FleetCarrier", [], [], [], [], [], [], At(10))
        );
}
