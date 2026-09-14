using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class CargoInventoryDiffTests
{
    [Fact]
    public void CopyFromInventoryClearsAndCopiesCounts()
    {
        Dictionary<string, int> dest = CargoInventoryDiff.CreateCountMap();
        dest["old"] = 9;
        CargoItem[] inventory = new[] { new CargoItem("iron", "Iron", 3, 0), new CargoItem("nickel", "Nickel", 7, 0) };

        CargoInventoryDiff.CopyFromInventory(dest, inventory);

        Assert.Equal(2, dest.Count);
        Assert.Equal(3, dest["iron"]);
        Assert.Equal(7, dest["nickel"]);
        Assert.False(dest.ContainsKey("old"));
    }

    [Fact]
    public void CopyFromInventoryHandlesNullInventory()
    {
        Dictionary<string, int> dest = CargoInventoryDiff.CreateCountMap();
        dest["iron"] = 1;
        CargoInventoryDiff.CopyFromInventory(dest, null);
        Assert.Empty(dest);
    }

    [Fact]
    public void ComputeReturnsPositiveDeltaWhenShipGainsCargo()
    {
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 2;
        CargoItem[] after = new[] { new CargoItem("iron", "Iron", 5, 0) };

        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(3, diff["iron"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ComputeReturnsFullCountWhenBeforeIsEmptyAndCommodityIsNew()
    {
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        CargoItem[] after = new[] { new CargoItem("cobalt", "Cobalt", 12, 0) };

        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(12, diff["cobalt"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ComputeMergesMixedCaseCommodityNames()
    {
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        before["Iron"] = 2;
        CargoItem[] after = new[] { new CargoItem("iron", "Iron", 5, 0) };

        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(3, diff["iron"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ComputeCasefoldsCaseSensitiveCallerDictionaries()
    {
        // Documented case-insensitive matching must not depend on the caller's comparer.
        var before = new Dictionary<string, int> { ["Iron"] = 2 };
        var after = new Dictionary<string, int> { ["iron"] = 5 };

        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(3, diff["iron"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ComputeReturnsNegativeDeltaWhenShipLosesCargo()
    {
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        before["steel"] = 100;
        CargoItem[] after = new[] { new CargoItem("steel", "Steel", 25, 0) };

        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(-75, diff["steel"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ComputeIncludesRemovedCommoditiesAsNegative()
    {
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 4;
        before["nickel"] = 2;
        CargoItem[] after = new[] { new CargoItem("iron", "Iron", 4, 0) };

        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(-2, diff["nickel"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ComputeReturnsEmptyWhenUnchanged()
    {
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 4;
        CargoItem[] after = new[] { new CargoItem("iron", "Iron", 4, 0) };

        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, after);

        Assert.Empty(diff);
    }

    [Fact]
    public void ComputeHandlesNullAfterInventory()
    {
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 4;
        Dictionary<string, int> diff = CargoInventoryDiff.Compute(before, (IReadOnlyList<CargoItem>?)null);
        Assert.Equal(-4, diff["iron"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ToCountMapMapsNamesAndCounts()
    {
        CargoItem[] inventory = new[] { new CargoItem("iron", "Iron", 1, 0), new CargoItem("nickel", "Nickel", 2, 0) };

        Dictionary<string, int> map = CargoInventoryDiff.ToCountMap(inventory);

        Assert.Equal(2, map.Count);
        Assert.Equal(1, map["iron"]);
        Assert.Equal(2, map["nickel"]);
    }

    [Fact]
    public void ToCountMapHandlesNullOrEmpty()
    {
        Assert.Empty(CargoInventoryDiff.ToCountMap(null));
        Assert.Empty(CargoInventoryDiff.ToCountMap([]));
    }

    [Fact]
    public void InvertForFleetCarrierMatchesSquadronFcSupplyDelta()
    {
        // Ship transferred 10 steel to carrier: ship before 50 → after 40
        Dictionary<string, int> before = CargoInventoryDiff.CreateCountMap();
        before["steel"] = 50;
        CargoItem[] after = new[] { new CargoItem("steel", "Steel", 40, 0) };

        Dictionary<string, int> shipDiff = CargoInventoryDiff.Compute(before, after);
        Dictionary<string, int> fcDiff = CargoInventoryDiff.InvertForFleetCarrier(shipDiff);

        Assert.Equal(-10, shipDiff["steel"]);
        Assert.Equal(10, fcDiff["steel"]);
    }
}
