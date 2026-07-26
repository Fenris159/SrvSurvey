using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class CargoInventoryStateTests
{
    [Fact]
    public void JournalDeltasMaintainAUnifiedShipInventory()
    {
        var state = new CargoInventoryState();
        state.Reset(Snapshot(
            "Ship",
            new CargoItem("gold", "Gold", 2, 1),
            new CargoItem("water", "Water", 3, 0)));

        Assert.True(state.Apply(Event(
            "CollectCargo",
            "\"Type\":\"silver\",\"Type_Localised\":\"Silver\"")));
        Assert.True(state.Apply(Event(
            "EjectCargo",
            "\"Type\":\"gold\",\"Count\":1")));
        Assert.True(state.Apply(Event(
            "MarketBuy",
            "\"Type\":\"water\",\"Count\":2")));
        Assert.True(state.Apply(Event(
            "MarketSell",
            "\"Type\":\"water\",\"Count\":1")));
        Assert.True(state.Apply(Event(
            "ColonisationContribution",
            "\"Contributions\":[{\"Name\":\"$Water_name;\",\"Amount\":2}]")));
        Assert.True(state.Apply(Event(
            "CargoTransfer",
            "\"Transfers\":["
                + "{\"Type\":\"silver\",\"Count\":1,\"Direction\":\"tocarrier\"},"
                + "{\"Type\":\"gold\",\"Count\":2,\"Direction\":\"toship\"}]")));

        var snapshot = Assert.IsType<CargoSnapshot>(state.CreateSnapshot());
        Assert.Equal(3, snapshot.GetCount("gold"));
        Assert.Equal(2, snapshot.GetCount("water"));
        Assert.Equal(0, snapshot.GetCount("silver"));
        Assert.Equal(5, snapshot.Count);
        Assert.Equal(1, Assert.Single(
            snapshot.Inventory,
            item => item.Name == "gold").Stolen);
    }

    [Fact]
    public void SrvTransfersUseTheActiveVehicleDirection()
    {
        var state = new CargoInventoryState();
        state.Reset(Snapshot(
            "SRV",
            new CargoItem("ancientrelic", null, 2, 0)));

        Assert.True(state.Apply(
            Event(
                "CargoTransfer",
                "\"Transfers\":["
                    + "{\"Type\":\"ancientrelic\",\"Count\":1,\"Direction\":\"toship\"},"
                    + "{\"Type\":\"ancientorb\",\"Count\":3,\"Direction\":\"tosrv\"}]"),
            isInSrv: true));

        var snapshot = Assert.IsType<CargoSnapshot>(state.CreateSnapshot());
        Assert.Equal(1, snapshot.GetCount("ancientrelic"));
        Assert.Equal(3, snapshot.GetCount("ancientorb"));
    }

    [Fact]
    public void AuthoritativeSnapshotReplacesEarlierJournalProjection()
    {
        var state = new CargoInventoryState();
        state.Apply(Event(
            "CollectCargo",
            "\"Type\":\"gold\",\"Type_Localised\":\"Gold\""));

        Assert.True(state.Reset(Snapshot(
            "Ship",
            new CargoItem("silver", "Silver", 4, 0))));

        var snapshot = Assert.IsType<CargoSnapshot>(state.CreateSnapshot());
        Assert.Equal(0, snapshot.GetCount("gold"));
        Assert.Equal(4, snapshot.GetCount("silver"));
    }

    [Fact]
    public void CargoEventMergesDuplicatesAndClampsHostileCounts()
    {
        var state = new CargoInventoryState();

        Assert.True(state.Apply(Event(
            "Cargo",
            "\"Vessel\":\"Ship\",\"Inventory\":["
                + $"{{\"Name\":\"gold\",\"Count\":{int.MaxValue},\"Stolen\":1}},"
                + "{\"Name\":\"GOLD\",\"Count\":2,\"Stolen\":2},"
                + "{\"Name\":\"silver\",\"Count\":-1}]")));
        Assert.False(state.Apply(Event(
            "MarketBuy",
            "\"Type\":\"gold\",\"Count\":1")));

        var snapshot = Assert.IsType<CargoSnapshot>(state.CreateSnapshot());
        var gold = Assert.Single(snapshot.Inventory);
        Assert.Equal(int.MaxValue, gold.Count);
        Assert.Equal(3, gold.Stolen);
        Assert.Equal(int.MaxValue, snapshot.Count);
    }

    [Fact]
    public void ExplicitClearRemovesTheProjection()
    {
        var state = new CargoInventoryState();
        state.Reset(Snapshot(
            "Ship",
            new CargoItem("gold", "Gold", 1, 0)));

        Assert.True(state.Reset(null));
        Assert.Null(state.CreateSnapshot());
        Assert.False(state.Reset(null));
    }

    private static CargoSnapshot Snapshot(
        string vessel,
        params CargoItem[] items)
    {
        return new CargoSnapshot(
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            "Cargo",
            vessel,
            items.Sum(item => item.Count),
            items);
    }

    private static JournalEventEnvelope Event(
        string eventName,
        string properties)
    {
        var json = "{\"timestamp\":\"2026-07-25T12:05:00Z\","
            + $"\"event\":\"{eventName}\",{properties}}}";
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(result);
    }
}
