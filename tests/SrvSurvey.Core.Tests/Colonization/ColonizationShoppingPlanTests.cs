using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationShoppingPlanTests
{
    [Fact]
    public void PreservedSelectionsKeepFixedQuantitiesOutsideTheCurrentNeedsAndStock()
    {
        var plan = new ColonizationShoppingPlan(
            [new("water", "Water", 30)],
            [Quote(1, "steel", 5), Quote(2, "water", 100)],
            [new("1", "steel", 50), new("missing", "steel", 20)],
            preserveSelections: true
        );
        Assert.Equal(50, Assert.Single(plan.Snapshot()).Quantity);
        Assert.Equal(1, Assert.Single(plan.SnapshotQuotes()).MarketId);
        Assert.Equal(0, plan.Remaining("steel"));
        plan.Select([new("2", "water")]);
        Assert.Equal(30, plan.Snapshot()[1].Quantity);
        plan.Remove([new("1", "steel")]);
        Assert.Equal(0, plan.Remaining("steel"));
        plan.Remove(plan.Snapshot());
        Assert.Equal(30, plan.Remaining("water"));
        Assert.Empty(plan.SnapshotQuotes());
    }

    [Fact]
    public void RemovingPreservedSurplusCannotOverflowOrIncreaseNeedsPastTheirCurrentAmount()
    {
        var plan = new ColonizationShoppingPlan(
            [new("steel", "Steel", 1)],
            [Quote(1, "steel", 1), Quote(2, "steel", 1)],
            [new("1", "steel", long.MaxValue), new("2", "steel", long.MaxValue)],
            preserveSelections: true
        );
        plan.Remove([new("1", "steel")]);
        Assert.Equal(0, plan.Remaining("steel"));
        plan.Select([new("1", "steel")]);
        Assert.Equal("2", Assert.Single(plan.Snapshot()).MarketKey);
        plan.Remove(plan.Snapshot());
        Assert.Equal(1, plan.Remaining("steel"));
        Assert.Empty(plan.Snapshot());
    }

    [Theory]
    [InlineData(10, 10, 200)]
    [InlineData(30, 0, 190)]
    [InlineData(220, 0, 0)]
    [InlineData(1000, 0, 0)]
    [InlineData(0, 20, 200)]
    [InlineData(-1, 20, 200)]
    public void PurchasesConsumeSmallestAllocationsFirstAndNeverAffectOtherGoods(
        long purchased,
        long smallRemaining,
        long largeRemaining
    )
    {
        ColonizationWishListItem[] original =
        [
            new("large", "steel", 200),
            new("small", "steel", 20),
            new("small", "water", 50),
        ];

        IReadOnlyList<ColonizationWishListItem> updated = ColonizationShoppingPlan.ConsumePurchase(
            original,
            "$Steel_Name;",
            purchased
        );

        Assert.Equal(
            smallRemaining,
            updated.FirstOrDefault(item => item.MarketKey == "small" && item.Commodity == "steel")?.Quantity ?? 0
        );
        Assert.Equal(largeRemaining, updated.FirstOrDefault(item => item.MarketKey == "large")?.Quantity ?? 0);
        Assert.Equal(50, updated.Single(item => item.Commodity == "water").Quantity);
        Assert.Equal(20, original[1].Quantity);
        Assert.All(updated, item => Assert.True(item.Quantity > 0));
    }

    [Fact]
    public void PurchaseTiesUseSavedOrderAndUnlistedGoodsLeaveAllocationsUnchanged()
    {
        ColonizationWishListItem[] original = [new("first", "steel", 20), new("second", "steel", 20)];

        IReadOnlyList<ColonizationWishListItem> updated = ColonizationShoppingPlan.ConsumePurchase(
            original,
            "Steel",
            25
        );

        Assert.Equal(new("second", "steel", 15), Assert.Single(updated));
        Assert.Equal(original, ColonizationShoppingPlan.ConsumePurchase(original, "water", 500));
        Assert.Empty(
            ColonizationShoppingPlan.ConsumePurchase([new("large", "steel", long.MaxValue)], "steel", long.MaxValue)
        );
        Assert.Throws<ArgumentNullException>(() => ColonizationShoppingPlan.ConsumePurchase(null!, "steel", 1));
    }

    [Fact]
    public void AllocatesInClickOrderAndCapsTheLastPurchaseToTheRemainingNeed()
    {
        var plan = new ColonizationShoppingPlan(
            [new("steel", "Steel", 300)],
            [Quote(1, "steel", 100), Quote(2, "steel", 500), Quote(3, "steel", 200)]
        );
        plan.Select([new("1", "steel")]);
        Assert.Equal(100, Assert.Single(plan.Snapshot()).Quantity);
        Assert.Equal(200, plan.Remaining("steel"));
        Assert.DoesNotContain(plan.GetAvailableQuotes(), quote => quote.MarketId == 1);
        plan.Select([new("2", "steel")]);
        Assert.Equal(200, plan.Snapshot()[1].Quantity);
        Assert.Empty(plan.GetRemainingNeeds());
        plan.Select([new("3", "steel")]);
        Assert.Equal(2, plan.Snapshot().Count);
        Assert.Equal(300, plan.Snapshot().Sum(item => item.Quantity));
    }

    [Fact]
    public void RemovingAChoiceRestoresItsTonnesWithoutGrowingOtherChoicesAndAllowsExplicitTopUp()
    {
        var plan = new ColonizationShoppingPlan(
            [new("steel", "Steel", 300)],
            [Quote(1, "steel", 100), Quote(2, "steel", 500)]
        );
        plan.Select([new("1", "steel"), new("2", "steel")]);
        plan.Remove([new("1", "steel")]);
        Assert.Equal(100, plan.Remaining("steel"));
        Assert.Equal(200, Assert.Single(plan.Snapshot()).Quantity);
        Assert.Equal(300, plan.GetAvailableQuotes().Single(quote => quote.MarketId == 2).Stock);
        plan.Select([new("2", "steel")]);
        Assert.Equal(300, Assert.Single(plan.Snapshot()).Quantity);
        Assert.Equal(0, plan.Remaining("steel"));
        plan.Remove([new("2", "steel", 1)]);
        Assert.Equal(300, plan.Remaining("steel"));
        Assert.Empty(plan.Snapshot());
        Assert.Equal(300, Assert.Single(plan.GetRemainingNeeds()).Required);
    }

    [Fact]
    public void MigratesOlderSelectionsAndKeepsExplicitQuantitiesFixedOnRestore()
    {
        ColonizationShoppingNeed[] needs = [new("steel", "Steel", 300)];
        ColonizationMarketQuote[] quotes = [Quote(1, "steel", 100), Quote(2, "steel", 500)];
        var legacy = new ColonizationShoppingPlan(needs, quotes, [new("1", "steel"), new("2", "steel")]);
        Assert.Equal(100, legacy.Snapshot()[0].Quantity);
        Assert.Equal(200, legacy.Snapshot()[1].Quantity);
        var restored = new ColonizationShoppingPlan(needs, quotes, [legacy.Snapshot()[1]]);
        Assert.Equal(200, Assert.Single(restored.Snapshot()).Quantity);
        Assert.Equal(100, restored.Remaining("steel"));
    }

    [Fact]
    public void RejectsInvalidDuplicateAndUnavailableSelectionsAndNeverAddsDuplicateProviderStock()
    {
        var plan = new ColonizationShoppingPlan(
            [new("steel", "Steel", 100)],
            [Quote(1, "steel", 100), Quote(1, "steel", 50) with { UpdatedAt = DateTimeOffset.MaxValue }],
            [
                null!,
                new("missing", "steel"),
                new("1", "water"),
                new("1", "steel", -1),
                new("1", "steel", 1000),
                new("1", "steel", 1),
            ]
        );
        Assert.Equal(50, Assert.Single(plan.Snapshot()).Quantity);
        Assert.Equal(50, plan.Remaining("steel"));
        Assert.Empty(plan.GetAvailableQuotes());
        plan.Select([new("1", "steel")]);
        Assert.Equal(50, Assert.Single(plan.Snapshot()).Quantity);
        plan.Remove([new("missing", "steel")]);
        Assert.Equal(50, plan.Remaining("steel"));
    }

    [Fact]
    public void AllocatesMultipleGoodsIndependentlyWithoutOverflowingLargeRequirements()
    {
        var plan = new ColonizationShoppingPlan(
            [new("steel", "Steel", long.MaxValue), new("water", "Water", 50)],
            [Quote(1, "steel", long.MaxValue - 3), Quote(1, "water", 80), Quote(2, "steel", 10)]
        );
        plan.Select([new("1", "steel"), new("1", "water"), new("2", "steel")]);
        Assert.Equal(3, plan.Snapshot()[2].Quantity);
        Assert.Equal(50, plan.Snapshot()[1].Quantity);
        Assert.Empty(plan.GetRemainingNeeds());
        Assert.Equal(0, plan.Remaining("unknown"));
        plan.Remove([new("1", "steel"), new("1", "water")]);
        Assert.Equal(long.MaxValue - 3, plan.Remaining("steel"));
        Assert.Equal(50, plan.Remaining("water"));
    }

    private static ColonizationMarketQuote Quote(long market, string commodity, long stock) =>
        new(
            "Sol",
            "Port " + market,
            market,
            "Coriolis",
            3,
            market * 10,
            100,
            commodity,
            stock,
            500,
            DateTimeOffset.MinValue,
            "Ardent"
        );
}
