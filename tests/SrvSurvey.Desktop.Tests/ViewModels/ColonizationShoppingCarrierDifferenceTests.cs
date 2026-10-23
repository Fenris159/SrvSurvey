using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationShoppingCarrierDifferenceTests
{
    private static ColonizationFleetCarrier[] Carriers() =>
        [
            new()
            {
                MarketId = 42,
                Name = "AAA-111",
                DisplayName = "Personal carrier",
                Owner = "Test Cmdr",
                Cargo = new() { ["steel"] = 60, ["water"] = 10 },
            },
            new()
            {
                MarketId = 43,
                Name = "BBB-222",
                DisplayName = "Team carrier",
                Owner = "Teammate",
                Cargo = new() { ["steel"] = 30, ["water"] = 100 },
            },
        ];

    [Fact]
    public void CarrierDifferenceDefaultsOnAndPreservesAnExplicitOffPreference()
    {
        using var model = new ColonizationShoppingViewModel(ColonizationShoppingViewModelTests.Projects(), () => "Sol");
        Assert.True(model.UseCarrierDifference);
        using var legacy = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            restoredState: new() { UseCarrierDifference = false }
        );
        Assert.True(legacy.UseCarrierDifference);
        model.UseCarrierDifference = false;
        using var restored = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            restoredState: model.CaptureState()
        );
        Assert.False(restored.UseCarrierDifference);
        restored.Dispose();
        Assert.False(restored.SelectPlanningCarrier(42));
    }

    [Fact]
    public void RestoredLegacyLimitWarningsCollapseToOneSummary()
    {
        string warning =
            "Spansh reached the 100-station limit for Steel; narrow the radius for more complete results. ";
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            restoredState: new() { Warnings = warning + warning.Replace("Steel", "Water", StringComparison.Ordinal) }
        );
        Assert.Equal("Previous Spansh results were truncated. Refresh to load more markets.", model.Warnings);
    }

    [Fact]
    public async Task AllOrIndividualLinkedCarrierChangesNeedsWithoutRepeatingTheSearch()
    {
        var search = new ColonizationShoppingViewModelTests.SearchStub([
            ColonizationShoppingViewModelTests.Quote(1, "steel", 200),
            ColonizationShoppingViewModelTests.Quote(1, "water", 200),
        ]);
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            search
        );
        model.UpdateCarriers(Carriers());
        Assert.Equal(3, model.CarrierOptions.Count);
        Assert.Equal("ALL", model.SelectedCarrier.Label);
        Assert.Contains(model.CarrierOptions, carrier => carrier.Label == "Team carrier (BBB-222)");
        model.UseCarrierDifference = true;
        Assert.Equal("Steel: 10", model.NeedsText);
        await model.SearchAsync();
        Assert.Equal(2, search.LastQuery!.Needs.Count);
        Assert.Equal(10, Assert.Single(model.Results).Supplies[0].Need.Required);
        model.SelectedCarrier = model.CarrierOptions.Single(carrier => carrier.MarketId == 42);
        Assert.Equal("Steel: 40, Water: 40", model.NeedsText);
        model.SelectedCarrier = model.CarrierOptions.Single(carrier => carrier.MarketId == 43);
        Assert.Equal("Steel: 70", model.NeedsText);
        model.UseCarrierDifference = false;
        Assert.Equal("Steel: 100, Water: 50", model.NeedsText);
        Assert.Equal(1, search.Calls);
        Assert.Contains("Location Stock", model.Results[0].Supplies[0].Detail);
        Assert.DoesNotContain(
            ColonizationShoppingViewModelTests
                .Quote(1, "steel", 1)
                .UpdatedAt.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture),
            model.Results[0].Supplies[0].Detail
        );
    }

    [Fact]
    public async Task PreferencesRestoreAndFreshCargoCapsPurchasesWithoutGrowingThemAgain()
    {
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 200),
            ])
        );
        model.UseCarrierDifference = false;
        model.UpdateCarriers(Carriers());
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        model.SelectedCarrier = model.CarrierOptions.Single(carrier => carrier.MarketId == 42);
        model.UseCarrierDifference = true;
        Assert.Equal(40, Assert.Single(model.CaptureState().WishList).Quantity);
        using var restored = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            restoredState: model.CaptureState()
        );
        restored.UpdateCarriers(Carriers());
        Assert.True(restored.UseCarrierDifference);
        Assert.Equal(42, restored.SelectedCarrier.MarketId);
        Assert.Equal(40, Assert.Single(restored.CaptureState().WishList).Quantity);
        restored.UpdateProjects(
            ColonizationShoppingViewModelTests.Projects(),
            [Carriers()[0] with { Cargo = new() { ["steel"] = 80 } }, Carriers()[1]]
        );
        Assert.Equal(20, Assert.Single(restored.CaptureState().WishList).Quantity);
        restored.UseCarrierDifference = false;
        Assert.Equal(20, Assert.Single(restored.CaptureState().WishList).Quantity);
        Assert.Contains("Steel: 80", restored.NeedsText);
        restored.UpdateCarriers([]);
        Assert.Equal("ALL", restored.SelectedCarrier.Label);
        restored.Dispose();
        restored.UpdateCarriers(Carriers());
        Assert.Single(restored.CarrierOptions);
    }
}
