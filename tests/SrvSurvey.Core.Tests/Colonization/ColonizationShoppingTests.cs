using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationShoppingTests
{
    [Fact]
    public void CarrierCoverageWeightsRemainingTonnageAndCapsSurplusPerCommodity()
    {
        ColonizationProject project = Project("one", new() { ["steel"] = 100, ["water"] = 300 });
        ColonizationFleetCarrier[] carriers =
        [
            new()
            {
                MarketId = 42,
                Cargo = new() { ["$Steel_name;"] = 1000, ["steel"] = 1000 },
            },
            new()
            {
                MarketId = 43,
                Cargo = new() { ["water"] = 100 },
            },
        ];
        Assert.Equal(50, ColonizationShopping.CarrierCoveragePercent(project, carriers));
        Assert.Equal(25, ColonizationShopping.CarrierCoveragePercent(project, carriers, 42));
        Assert.Equal(25, ColonizationShopping.CarrierCoveragePercent(project, carriers, 43));
        Assert.Equal(0, ColonizationShopping.CarrierCoveragePercent(project, carriers, 99));
        Assert.Equal(0, ColonizationShopping.CarrierCoveragePercent(project, []));
        Assert.Equal(100, ColonizationShopping.CarrierCoveragePercent(project with { IsComplete = true }, []));
        Assert.Equal(100, ColonizationShopping.CarrierCoveragePercent(Project("empty", new() { ["steel"] = 0 }), []));
        Assert.Equal(0, ColonizationShopping.CarrierCoveragePercent(Project("unknown", []), carriers));
    }

    [Fact]
    public void CarrierDifferenceDeduplicatesCarriersAndAliasesAndClampsStock()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new()
            {
                ["$Steel_name;"] = 20,
                ["steel"] = 20,
                ["water"] = -10,
            },
        };
        ColonizationShoppingNeed[] needs = [new("steel", "Steel", 100), new("water", "Water", 50)];
        IReadOnlyList<ColonizationShoppingNeed> result = ColonizationShopping.SubtractCarrierCargo(
            needs,
            [
                carrier,
                carrier,
                new()
                {
                    MarketId = 0,
                    Cargo = new() { ["steel"] = 1000 },
                },
                new()
                {
                    MarketId = 43,
                    Cargo = new() { ["steel"] = 10, ["water"] = 100 },
                },
            ]
        );
        Assert.Equal(70, Assert.Single(result).Required);
        Assert.Equal(80, ColonizationShopping.SubtractCarrierCargo(needs, [carrier], 42)[0].Required);
        Assert.Equal(needs, ColonizationShopping.SubtractCarrierCargo(needs, [carrier], 99));
    }

    [Fact]
    public void AggregatesUnfinishedSelectedBuildsOnceAndNormalizesProviderAliases()
    {
        ColonizationProject first = Project(
            "one",
            new()
            {
                ["$Steel_name;"] = int.MaxValue,
                ["muonimager"] = 20,
                ["water"] = -1,
            }
        );
        ColonizationProject second = Project(
            "two",
            new()
            {
                ["STEEL"] = int.MaxValue,
                ["mutomimager"] = 30,
                ["H.E. Suits"] = 5,
            }
        );
        IReadOnlyList<ColonizationShoppingNeed> needs = ColonizationShopping.Aggregate([
            first,
            first,
            second,
            Project("done", new() { ["water"] = 5 }) with
            {
                IsComplete = true,
            },
            Project("empty", new() { [""] = 2, ["tea"] = 0 }),
        ]);
        Assert.Equal(3, needs.Count);
        Assert.Equal(2L * int.MaxValue, needs.Single(need => need.Commodity == "steel").Required);
        Assert.Equal(50, needs.Single(need => need.Commodity == "mutomimager").Required);
        Assert.Equal("H.E. Suits", needs[0].DisplayName);
    }

    [Theory]
    [InlineData("microbialfurnaces", "heliostaticfurnaces", "Microbial Furnaces")]
    [InlineData("landenrichmentsystems", "terrainenrichmentsystems", "Land Enrichment Systems")]
    [InlineData("combatstabilizers", "combatstabilisers", "Combat Stabilisers")]
    [InlineData("Agri-Medicines", "agriculturalmedicines", "Agri-Medicines")]
    [InlineData("Liquid oxygen", "liquidoxygen", "Liquid oxygen")]
    [InlineData("unknown", "unknown", "unknown")]
    public void UsesVerifiedProviderNames(string name, string key, string displayName)
    {
        ColonizationShoppingNeed need = Assert.Single(
            ColonizationShopping.Aggregate([Project("one", new() { [name] = 1 })])
        );
        Assert.Equal(key, need.Commodity);
        Assert.Equal(displayName, need.DisplayName);
    }

    [Fact]
    public void MergesObservationsByMarketWithoutAddingStockAndUsesLatestTimestamp()
    {
        ColonizationMarketQuote older = Quote(1, "steel", 100, "Spansh");
        ColonizationMarketQuote newer = older with
        {
            Stock = 50,
            Source = "Ardent",
            UpdatedAt = older.UpdatedAt.AddMinutes(1),
        };
        IReadOnlyList<ColonizationMarketQuote> merged = ColonizationShopping.Merge([
            older,
            newer,
            older with
            {
                MarketId = 2,
            },
            older with
            {
                Commodity = "water",
            },
            older with
            {
                Stock = 0,
            },
        ]);
        Assert.Equal(3, merged.Count);
        Assert.Equal(50, merged.Single(quote => quote.MarketId == 1 && quote.Commodity == "steel").Stock);
        Assert.Equal(
            "Ardent",
            Assert.Single(ColonizationShopping.Merge([older, older with { Source = "Ardent" }])).Source
        );
        Assert.Single(
            ColonizationShopping.Merge([older with { MarketId = 0 }, older with { MarketId = 0, Station = "PORT" }])
        );
        Assert.Equal(
            2,
            ColonizationShopping
                .Merge([older with { MarketId = 0 }, older with { MarketId = 0, System = "Other" }])
                .Count
        );
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(501, 7)]
    [InlineData(1, 0)]
    [InlineData(500, 31)]
    public void RejectsUnsupportedQueryBounds(int radius, int age) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ColonizationMarketQuery("Sol", radius, [], age).Validate()
        );

    [Fact]
    public void ValidatesRequiredQueryData()
    {
        Assert.Throws<ArgumentException>(() => new ColonizationMarketQuery(" ", 1, []).Validate());
        Assert.Throws<ArgumentNullException>(() => new ColonizationMarketQuery("Sol", 1, null!).Validate());
        Assert.Throws<ArgumentNullException>(() => ColonizationShopping.Aggregate(null!));
    }

    internal static ColonizationProject Project(string id, Dictionary<string, int> commodities) =>
        new()
        {
            BuildId = id,
            BuildName = id,
            Commodities = commodities,
        };

    internal static ColonizationMarketQuote Quote(long id, string commodity, long stock, string source = "Ardent") =>
        new("Sol", "Port", id, "Coriolis Starport", 3, 12, 100, commodity, stock, 500, DateTimeOffset.UtcNow, source);
}
