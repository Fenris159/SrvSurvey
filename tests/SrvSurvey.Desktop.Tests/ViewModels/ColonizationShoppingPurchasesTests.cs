using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task LockedShoppingListSurvivesNewProjectsAndRestartsAndStillConsumesPurchases()
    {
        StubRavenColonialClient client = SavePurchaseList();
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            model.OpenShopping();
            model.MarketSearchWorkspace!.IsWishListLocked = true;
            model.Projects[0].IsShoppingSelected = false;
            Assert.Null(model.MarketSearchWorkspace);
            Assert.Equal(2, model.WishList.Stations.Count);
            client.Workspace = new(
                [Project("two", "Water project", 120) with { Commodities = new() { ["water"] = 120 } }],
                [],
                null,
                []
            );
            await model.RefreshAsync();
            model.MarketSearchFactory = () =>
                new ColonizationShoppingViewModelTests.SearchStub([
                    ColonizationShoppingViewModelTests.Quote(3, "water", 200),
                ]);
            model.Projects[0].IsShoppingSelected = true;
            await model.MarketSearchWorkspace!.SearchAsync();
            Assert.True(model.MarketSearchWorkspace.IsWishListLocked);
            Assert.Equal(220, model.MarketSearchWorkspace.CaptureState().WishList.Sum(item => item.Quantity));
            Assert.All(model.WishList.Stations, station => Assert.StartsWith("Steel:", station.Children[0].Label));
        }
        using ColonizationViewModel restored = Create(client);
        restored.IsEnabled = true;
        await restored.SetCommanderAsync("Test Cmdr");
        restored.OpenShopping();
        Assert.True(restored.MarketSearchWorkspace!.IsWishListLocked);
        Assert.Equal(2, restored.WishList.Stations.Count);
        await restored.SynchronizeLiveProjectsAsync([Event("MarketBuy", "\"Type\":\"steel\",\"Count\":30")], true);
        Assert.Equal("Steel: 190", Assert.Single(restored.WishList.Stations).Children[0].Label);
        restored.MarketSearchWorkspace.ClearWishListCommand.Execute(null);
        Assert.False(restored.WishList.HasItems);
        Assert.True(restored.MarketSearchWorkspace.IsWishListLocked);
        ColonizationShoppingState saved = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"))
            .LoadShopping("Test Cmdr")!
            .Search!;
        Assert.Empty(saved.WishList);
        Assert.True(saved.IsWishListLocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShoppingPurchasesUpdateClosedAndOpenSearchesAndSurviveRestartWithoutDoubleCounting(
        bool openSearch
    )
    {
        StubRavenColonialClient client = SavePurchaseList();
        JournalEventEnvelope purchase = Event(
            "MarketBuy",
            "\"MarketID\":9999,\"Type\":\"$Steel_Name;\",\"Count\":30"
        ) with
        {
            SourceRecordIdentity = "purchase.log:100",
        };
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            model.ApplyJournalEvents([Event("Loadout", "\"CargoCapacity\":100")]);
            Assert.Equal(3, model.WishList.TotalTripsInCurrentShip);
            if (openSearch)
            {
                model.OpenShopping();
            }
            await model.SynchronizeLiveProjectsAsync([purchase], allowPublishing: false);
            Assert.Equal(2, model.WishList.Stations.Count);
            await model.SynchronizeLiveProjectsAsync([purchase], allowPublishing: true);
            await model.SynchronizeLiveProjectsAsync([purchase], allowPublishing: true);

            Assert.Equal("Steel: 190", Assert.Single(model.WishList.Stations).Children[0].Label);
            Assert.Equal(2, model.WishList.TotalTripsInCurrentShip);
            Assert.Equal(220, Assert.Single(model.Projects).Project.RemainingRequired);
            if (openSearch)
            {
                Assert.Equal(190, Assert.Single(model.MarketSearchWorkspace!.CaptureState().WishList).Quantity);
                model.MarketSearchWorkspace.SaveWorkspace();
            }
            model.ApplyJournalEvents([Event("Loadout", "\"CargoCapacity\":400")]);
            Assert.Equal(1, model.WishList.TotalTripsInCurrentShip);
        }
        using ColonizationViewModel restarted = Create(client);
        restarted.IsEnabled = true;
        await restarted.SetCommanderAsync("Test Cmdr");
        await restarted.SynchronizeLiveProjectsAsync([purchase], allowPublishing: true);
        Assert.Equal("Steel: 190", Assert.Single(restarted.WishList.Stations).Children[0].Label);
        await restarted.SetCommanderAsync("Other Cmdr");
        await restarted.SynchronizeLiveProjectsAsync([purchase], allowPublishing: true);
        Assert.False(restarted.WishList.HasItems);
        await restarted.SetCommanderAsync("Test Cmdr");
        restarted.OpenShopping();
        ColonizationShoppingState saved = restarted.MarketSearchWorkspace!.CaptureState();
        Assert.Equal(190, Assert.Single(saved.WishList).Quantity);
        Assert.Single(saved.AppliedPurchaseIds);
        await restarted.SynchronizeLiveProjectsAsync(
            [Event("MarketBuy", "\"MarketID\":10000,\"Type\":\"Steel\",\"Count\":220")],
            allowPublishing: true
        );
        Assert.False(restarted.WishList.HasItems);
        Assert.Empty(restarted.MarketSearchWorkspace.CaptureState().WishList);
    }

    [Fact]
    public async Task SeparateIdenticalPurchasesAtDifferentJournalOffsetsBothCountAndRemovalsUpdateTrips()
    {
        StubRavenColonialClient client = SavePurchaseList();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.ApplyJournalEvents([Event("Loadout", "\"CargoCapacity\":100")]);
        JournalEventEnvelope first = Event("MarketBuy", "\"Type\":\"steel\",\"Count\":10") with
        {
            SourceRecordIdentity = "journal:10",
        };
        await model.SynchronizeLiveProjectsAsync([first, first with { SourceRecordIdentity = "journal:20" }], true);
        Assert.Equal("Steel: 200", Assert.Single(model.WishList.Stations).Children[0].Label);
        model.WishList.Stations[0].RemoveCommand.Execute(null);
        Assert.False(model.WishList.HasItems);
        Assert.Equal(0, model.WishList.TotalTripsInCurrentShip);
    }

    [Fact]
    public async Task InvalidTransactionsAndDisabledProfilesDoNotConsumeSavedPurchases()
    {
        StubRavenColonialClient client = SavePurchaseList();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        await model.SynchronizeLiveProjectsAsync(
            [
                Event("MarketSell", "\"Type\":\"steel\",\"Count\":10"),
                Event("MarketBuy", "\"Count\":10"),
                Event("MarketBuy", "\"Type\":\"steel\""),
                Event("MarketBuy", "\"Type\":\"steel\",\"Count\":\"10\""),
                Event("MarketBuy", "\"Type\":\"steel\",\"Count\":1.5"),
                Event("MarketBuy", "\"Type\":\"steel\",\"Count\":-1"),
                Event("MarketBuy", "\"Type\":\"steel\",\"Count\":0"),
            ],
            true
        );
        Assert.Equal(2, model.WishList.Stations.Count);
        model.IsEnabled = false;
        await model.SynchronizeLiveProjectsAsync([Event("MarketBuy", "\"Type\":\"steel\",\"Count\":220")], true);
        ColonizationShoppingState saved = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"))
            .LoadShopping("Test Cmdr")!
            .Search!;
        Assert.Equal(220, saved.WishList.Sum(item => item.Quantity));
        Assert.Empty(saved.AppliedPurchaseIds);
    }

    [Fact]
    public async Task PurchaseReplayProtectionIsBoundedAndSurvivesOrdinaryWorkspaceSaves()
    {
        using ColonizationViewModel model = Create(SavePurchaseList());
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.OpenShopping();
        JournalEventEnvelope[] purchases = Enumerable
            .Range(0, 520)
            .Select(index =>
                Event("MarketBuy", "\"Type\":\"water\",\"Count\":1") with
                {
                    SourceRecordIdentity = $"journal:{index}",
                }
            )
            .ToArray();
        await model.SynchronizeLiveProjectsAsync(purchases, true);
        model.MarketSearchWorkspace!.ShowShortSupply = false;
        model.MarketSearchWorkspace.SaveWorkspace();

        ColonizationShoppingState saved = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"))
            .LoadShopping("Test Cmdr")!
            .Search!;
        Assert.Equal(512, saved.AppliedPurchaseIds.Count);
        Assert.Equal(220, saved.WishList.Sum(item => item.Quantity));
    }

    private StubRavenColonialClient SavePurchaseList()
    {
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        store.SaveShopping(
            "Test Cmdr",
            new(
                ["one"],
                new ColonizationShoppingState
                {
                    ReferenceSystem = "Sol",
                    Quotes =
                    [
                        ColonizationShoppingViewModelTests.Quote(1, "steel", 20),
                        ColonizationShoppingViewModelTests.Quote(2, "steel", 200),
                    ],
                    WishList = [new("2", "steel", 200), new("1", "steel", 20)],
                }
            )
        );
        return new() { Workspace = new([Project("one", "Port", 220)], [], null, []) };
    }
}
