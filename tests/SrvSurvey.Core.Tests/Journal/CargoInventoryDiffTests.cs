using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class CargoInventoryDiffTests
{
    [Fact]
    public void CopyFromInventory_clears_and_copies_counts()
    {
        var dest = CargoInventoryDiff.CreateCountMap();
        dest["old"] = 9;
        var inventory = new[]
        {
            new CargoItem("iron", "Iron", 3, 0),
            new CargoItem("nickel", "Nickel", 7, 0),
        };

        CargoInventoryDiff.CopyFromInventory(dest, inventory);

        Assert.Equal(2, dest.Count);
        Assert.Equal(3, dest["iron"]);
        Assert.Equal(7, dest["nickel"]);
        Assert.False(dest.ContainsKey("old"));
    }

    [Fact]
    public void CopyFromInventory_handles_null_inventory()
    {
        var dest = CargoInventoryDiff.CreateCountMap();
        dest["iron"] = 1;
        CargoInventoryDiff.CopyFromInventory(dest, null);
        Assert.Empty(dest);
    }

    [Fact]
    public void Compute_returns_positive_delta_when_ship_gains_cargo()
    {
        var before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 2;
        var after = new[] { new CargoItem("iron", "Iron", 5, 0) };

        var diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(3, diff["iron"]);
        Assert.Single(diff);
    }

    [Fact]
    public void Compute_returns_full_count_when_before_is_empty_and_commodity_is_new()
    {
        var before = CargoInventoryDiff.CreateCountMap();
        var after = new[] { new CargoItem("cobalt", "Cobalt", 12, 0) };

        var diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(12, diff["cobalt"]);
        Assert.Single(diff);
    }

    [Fact]
    public void Compute_merges_mixed_case_commodity_names()
    {
        var before = CargoInventoryDiff.CreateCountMap();
        before["Iron"] = 2;
        var after = new[] { new CargoItem("iron", "Iron", 5, 0) };

        var diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(3, diff["iron"]);
        Assert.Single(diff);
    }

    [Fact]
    public void Compute_returns_negative_delta_when_ship_loses_cargo()
    {
        var before = CargoInventoryDiff.CreateCountMap();
        before["steel"] = 100;
        var after = new[] { new CargoItem("steel", "Steel", 25, 0) };

        var diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(-75, diff["steel"]);
        Assert.Single(diff);
    }

    [Fact]
    public void Compute_includes_removed_commodities_as_negative()
    {
        var before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 4;
        before["nickel"] = 2;
        var after = new[] { new CargoItem("iron", "Iron", 4, 0) };

        var diff = CargoInventoryDiff.Compute(before, after);

        Assert.Equal(-2, diff["nickel"]);
        Assert.Single(diff);
    }

    [Fact]
    public void Compute_returns_empty_when_unchanged()
    {
        var before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 4;
        var after = new[] { new CargoItem("iron", "Iron", 4, 0) };

        var diff = CargoInventoryDiff.Compute(before, after);

        Assert.Empty(diff);
    }

    [Fact]
    public void Compute_handles_null_after_inventory()
    {
        var before = CargoInventoryDiff.CreateCountMap();
        before["iron"] = 4;
        var diff = CargoInventoryDiff.Compute(before, (IReadOnlyList<CargoItem>?)null);
        Assert.Equal(-4, diff["iron"]);
        Assert.Single(diff);
    }

    [Fact]
    public void ToCountMap_maps_names_and_counts()
    {
        var inventory = new[]
        {
            new CargoItem("iron", "Iron", 1, 0),
            new CargoItem("nickel", "Nickel", 2, 0),
        };

        var map = CargoInventoryDiff.ToCountMap(inventory);

        Assert.Equal(2, map.Count);
        Assert.Equal(1, map["iron"]);
        Assert.Equal(2, map["nickel"]);
    }

    [Fact]
    public void ToCountMap_handles_null_or_empty()
    {
        Assert.Empty(CargoInventoryDiff.ToCountMap(null));
        Assert.Empty(CargoInventoryDiff.ToCountMap([]));
    }

    [Fact]
    public void InvertForFleetCarrier_matches_squadron_fc_supply_delta()
    {
        // Ship transferred 10 steel to carrier: ship before 50 → after 40
        var before = CargoInventoryDiff.CreateCountMap();
        before["steel"] = 50;
        var after = new[] { new CargoItem("steel", "Steel", 40, 0) };

        var shipDiff = CargoInventoryDiff.Compute(before, after);
        var fcDiff = CargoInventoryDiff.InvertForFleetCarrier(shipDiff);

        Assert.Equal(-10, shipDiff["steel"]);
        Assert.Equal(10, fcDiff["steel"]);
    }
}
