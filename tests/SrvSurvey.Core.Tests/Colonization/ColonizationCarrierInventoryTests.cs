using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationCarrierInventoryTests
{
    [Theory]
    [InlineData("$LiquidOxygen_Name;", "liquidoxygen")]
    [InlineData("Liquid oxygen", "liquidoxygen")]
    [InlineData("H.E. Suits", "hazardousenvironmentsuits")]
    [InlineData("Microbial Furnaces", "heliostaticfurnaces")]
    [InlineData("Guardian Relic", "ancientrelic")]
    [InlineData("Titan Drive Component", "thargoidtitandrivecomponent")]
    [InlineData("metalloys", "metaalloys")]
    [InlineData("  unknown good  ", "unknown good")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void SourcesResolveTheSameCommodityIdentity(string? name, string expected) =>
        Assert.Equal(expected, ColonizationCarrierInventory.CommodityKey(name));

    [Fact]
    public void ObservationsKeepTheirCarrierSourceAndOriginalKeysWithoutCombiningTheirAmounts()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new() { ["liquid oxygen"] = 80, ["liquidoxygen"] = 60 },
        };
        var raven = ColonizationCarrierInventory.FromRaven(carrier);
        var local = ColonizationCarrierInventory.FromLocal(
            carrier,
            new Dictionary<string, long> { ["$LiquidOxygen_Name;"] = 75 }
        );
        Assert.Equal(42, raven.MarketId);
        Assert.Equal(raven.MarketId, local.MarketId);
        Assert.Equal(ColonizationInventorySource.Raven, raven.Source);
        Assert.Equal(ColonizationInventorySource.Local, local.Source);
        Assert.True(raven.HasAliases);
        ColonizationInventoryStock ravenStock = Assert.Single(raven.Stock);
        ColonizationInventoryStock localStock = Assert.Single(local.Stock);
        Assert.Equal(ravenStock.Commodity, localStock.Commodity);
        Assert.Equal(80, ravenStock.Quantity);
        Assert.Equal(75, localStock.Quantity);
        Assert.Equal(2, ravenStock.SourceKeys.Count);
        Assert.Contains("liquid oxygen", ravenStock.SourceKeys);
        Assert.Contains("liquidoxygen", ravenStock.SourceKeys);
        Assert.Equal("$LiquidOxygen_Name;", Assert.Single(localStock.SourceKeys));
        Assert.Throws<InvalidOperationException>(() => local.CreateRavenReplacement(carrier.Cargo));
        Assert.Equal(43, ColonizationCarrierInventory.FromRaven(carrier with { MarketId = 43 }).MarketId);
    }

    [Fact]
    public void ReplacementTargetsAllOldAliasesAndPreservesUnrecognizedGoodsWithoutMutatingTheObservation()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new()
            {
                ["liquid oxygen"] = 80,
                ["liquidoxygen"] = 60,
                ["LIQUIDOXYGEN"] = 60,
                ["steel"] = 40,
                ["unknown good"] = 10,
            },
        };
        var inventory = ColonizationCarrierInventory.FromRaven(carrier);
        Dictionary<string, int> replacement = inventory.CreateRavenReplacement(
            new Dictionary<string, int> { ["Liquid oxygen"] = 75, ["unknown good"] = 10 }
        );
        Assert.Equal(75, replacement["liquidoxygen"]);
        Assert.Equal(0, replacement["liquid oxygen"]);
        Assert.Equal(0, replacement["LIQUIDOXYGEN"]);
        Assert.Equal(0, replacement["steel"]);
        Assert.Equal(10, replacement["unknown good"]);
        Assert.Equal(80, carrier.Cargo["liquid oxygen"]);
        Assert.Equal(60, carrier.Cargo["liquidoxygen"]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            inventory.CreateRavenReplacement(new Dictionary<string, int> { ["liquidoxygen"] = -1 })
        );
        Dictionary<string, int> update = inventory.CreateRavenUpdate(
            new Dictionary<string, int> { ["liquidoxygen"] = 75 }
        );
        Assert.Equal(75, update["liquidoxygen"]);
        Assert.Equal(0, update["liquid oxygen"]);
        Assert.False(update.ContainsKey("steel"));
        Assert.False(update.ContainsKey("unknown good"));
    }

    [Fact]
    public void SnapshotAliasesAreNotTransactionsAndMissingOrInvalidStockIsNotInvented()
    {
        Dictionary<string, int> stock = ColonizationCarrierInventory.NormalizeStock(
            new Dictionary<string, int>
            {
                ["liquid oxygen"] = 80,
                ["liquidoxygen"] = 60,
                ["steel"] = -1,
                [" "] = 5,
            }
        );
        Assert.Equal(2, stock.Count);
        Assert.Equal(80, stock["liquidoxygen"]);
        Assert.Equal(0, stock["steel"]);
        Assert.False(stock.ContainsKey("water"));
    }

    [Fact]
    public void RelativeWritesSumTransactionsAndUpdateEveryTouchedSourceKeyInOnePatch()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new()
            {
                ["liquid oxygen"] = 100,
                ["LIQUIDOXYGEN"] = 80,
                ["liquidoxygen"] = 60,
                ["steel"] = 20,
            },
        };
        var inventory = ColonizationCarrierInventory.FromRaven(carrier);
        Dictionary<string, int> patch = inventory.CreateRavenAdjustment(
            new Dictionary<string, int> { ["Liquid oxygen"] = 15, ["liquidoxygen"] = -5 }
        );
        Assert.Equal(3, patch.Count);
        Assert.Equal(10, patch["liquid oxygen"]);
        Assert.Equal(10, patch["LIQUIDOXYGEN"]);
        Assert.Equal(10, patch["liquidoxygen"]);
        Assert.False(patch.ContainsKey("steel"));
        Assert.Empty(
            inventory.CreateRavenAdjustment(
                new Dictionary<string, int>
                {
                    ["Liquid oxygen"] = 5,
                    ["liquidoxygen"] = -5,
                    [" "] = 3,
                }
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            ColonizationCarrierInventory
                .FromLocal(carrier, [])
                .CreateRavenAdjustment(new Dictionary<string, int> { ["steel"] = 1 })
        );
        Assert.Throws<InvalidDataException>(() =>
            ColonizationCarrierInventory
                .FromRaven(carrier)
                .CreateRavenAdjustment(
                    new Dictionary<string, int> { ["liquidoxygen"] = int.MaxValue, ["Liquid oxygen"] = 1 }
                )
        );
    }
}
