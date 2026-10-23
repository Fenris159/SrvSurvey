using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task DisposingTheWorkspaceAlsoStopsItsShoppingModel()
    {
        using ColonizationViewModel model = Create(ShoppingClient(100));
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.True(shopping.CanSearch);
        model.Dispose();
        Assert.False(shopping.CanSearch);
    }

    [Theory]
    [InlineData(false, 40)]
    [InlineData(true, 100)]
    public async Task LegacyCarrierPreferenceIsConsistentBeforeAndAfterReopeningSearch(bool hasPreference, int expected)
    {
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        store.SaveEnabled(true);
        store.SaveShopping(
            "Test Cmdr",
            new ColonizationShoppingSession(
                ["one"],
                new()
                {
                    ReferenceSystem = "Sol",
                    CompletedSystem = "Sol",
                    CompletedRadius = 100,
                    CompletedAt = DateTimeOffset.UtcNow,
                    UseCarrierDifference = false,
                    HasCarrierDifferencePreference = hasPreference,
                    Quotes = [ColonizationShoppingViewModelTests.Quote(1, "steel", 500)],
                    WishList = [new("1", "steel", 100)],
                }
            )
        );
        StubRavenColonialClient client = ShoppingClient(100);
        client.Workspace = client.Workspace with
        {
            FleetCarriers =
            [
                new()
                {
                    MarketId = 42,
                    Name = "ABC-123",
                    Cargo = new() { ["steel"] = 60 },
                },
            ],
        };
        using ColonizationViewModel model = Create(client);
        await model.SetCommanderAsync("Test Cmdr");
        string before = Assert.Single(model.WishList.Stations).Children[0].Label;
        Assert.Equal($"Steel: {expected}", before);
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal(expected, Assert.Single(shopping.CaptureState().WishList).Quantity);
        Assert.Equal(before, Assert.Single(model.WishList.Stations).Children[0].Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CarrierDifferenceReconcilesSimultaneousProjectAndManifestUpdates(bool closeSearch)
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "AAA-111",
            Cargo = new() { ["steel"] = 100 },
        };
        StubRavenColonialClient client = ShoppingClient(300);
        client.Workspace = client.Workspace with { FleetCarriers = [carrier] };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 500),
            ])
        );
        shopping.ReferenceSystem = "Sol";
        model.SelectedPlanningCarrier = model.PlanningCarrierOptions.Single(choice => choice.MarketId == 42);
        shopping.SelectedCarrier = shopping.CarrierOptions.Single(choice => choice.MarketId == 42);
        shopping.UseCarrierDifference = true;
        await shopping.SearchAsync();
        shopping.Results[0].ToggleWishListCommand.Execute(null);
        Assert.Equal("Steel: 200", Assert.Single(model.WishList.Stations).Children[0].Label);
        if (closeSearch)
        {
            shopping.Dispose();
        }
        client.Workspace = ShoppingWorkspace(250) with
        {
            FleetCarriers = [carrier with { Cargo = new() { ["steel"] = 50 } }],
        };
        await model.RefreshAsync();
        Assert.Equal("Steel: 200", Assert.Single(model.WishList.Stations).Children[0].Label);
        client.Workspace = client.Workspace with
        {
            FleetCarriers = [carrier with { Cargo = new() { ["steel"] = 150 } }],
        };
        await model.RefreshAsync();
        Assert.Equal("Steel: 100", Assert.Single(model.WishList.Stations).Children[0].Label);
        using ColonizationViewModel restarted = Create(client);
        await restarted.SetCommanderAsync("Test Cmdr");
        using ColonizationShoppingViewModel reopened = restarted.CreateShopping(
            [restarted.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.True(reopened.UseCarrierDifference);
        Assert.Equal(42, reopened.SelectedCarrier.MarketId);
        Assert.True(reopened.IsFullyPlanned);
        Assert.Equal(100, Assert.Single(reopened.CaptureState().WishList).Quantity);
        reopened.Dispose();
        client.Workspace = client.Workspace with { FleetCarriers = [] };
        await restarted.RefreshAsync();
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal(0, store.LoadShopping("Test Cmdr")!.Search!.SelectedCarrierMarketId);
        Assert.Equal("Steel: 100", Assert.Single(restarted.WishList.Stations).Children[0].Label);
    }

    [Fact]
    public async Task ShoppingSurvivesApplicationRestartWithSelectedProjectsAndCommanderIsolation()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [Project("one", "Port", remaining: 100), Project("two", "Planned", remaining: 200)],
                [],
                null,
                []
            ),
        };
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            model.Projects[0].IsShoppingSelected = true;
            model.Projects[1].IsShoppingSelected = true;
            using ColonizationShoppingViewModel shopping = model.CreateShopping(
                model.Projects.Select(row => row.Project).ToArray(),
                new ColonizationShoppingViewModelTests.SearchStub([
                    ColonizationShoppingViewModelTests.Quote(1, "steel", 500),
                ])
            );
            shopping.ReferenceSystem = "Sol";
            await shopping.SearchAsync();
            shopping.Results[0].ToggleWishListCommand.Execute(null);
            Assert.True(model.WishList.HasItems);
            shopping.MaximumArrivalText = "250";
            Assert.Equal("Reopen Search", model.SearchMarketsButtonText);
        }
        using ColonizationViewModel restarted = Create(client);
        await restarted.SetCommanderAsync("test cmdr");
        Assert.All(restarted.Projects, row => Assert.True(row.IsShoppingSelected));
        Assert.Equal("Reopen Search", restarted.SearchMarketsButtonText);
        Assert.Single(restarted.WishList.Stations);
        var offline = new ColonizationShoppingViewModelTests.SearchStub([]);
        using (
            ColonizationShoppingViewModel reopened = restarted.CreateShopping(
                restarted.Projects.Reverse().Select(row => row.Project).ToArray(),
                offline
            )
        )
        {
            Assert.True(Assert.Single(reopened.Results).IsWishListed);
            Assert.True(reopened.IsFullyPlanned);
            Assert.Equal("250", reopened.MaximumArrivalText);
            Assert.Equal(0, offline.Calls);
        }
        await restarted.SetCommanderAsync("Other Cmdr");
        Assert.False(restarted.WishList.HasItems);
        Assert.All(restarted.Projects, row => Assert.False(row.IsShoppingSelected));
        Assert.Equal("Search Markets", restarted.SearchMarketsButtonText);
        await restarted.SetCommanderAsync("Test Cmdr");
        Assert.Equal("Reopen Search", restarted.SearchMarketsButtonText);
    }

    [Fact]
    public async Task ChangedSelectionRetainsCriteriaAndRejectsObsoleteSessionWrites()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [Project("one", "Port", remaining: 100), Project("two", "Planned", remaining: 200)],
                [],
                null,
                []
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        using ColonizationShoppingViewModel first = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal("Reopen Search", model.SearchMarketsButtonText);
        first.RadiusText = "25";
        model.Projects[1].IsShoppingSelected = true;
        first.RadiusText = "500";
        first.SaveWorkspace();
        first.Dispose();
        Assert.Equal("Reopen Search", model.SearchMarketsButtonText);
        using ColonizationViewModel restarted = Create(client);
        await restarted.SetCommanderAsync("Test Cmdr");
        Assert.Equal("Reopen Search", restarted.SearchMarketsButtonText);
        Assert.All(restarted.Projects, row => Assert.True(row.IsShoppingSelected));
        using ColonizationShoppingViewModel second = restarted.CreateShopping(
            restarted.Projects.Select(row => row.Project).ToArray(),
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Empty(second.Results);
        Assert.Equal("25", second.RadiusText);
        client.Workspace = new ColonizationCommanderProjects([Project("one", "Port", remaining: 100)], [], null, []);
        await restarted.RefreshAsync();
        Assert.Equal("Reopen Search", restarted.SearchMarketsButtonText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WishListRemovalsStayRemovedAcrossSearchCloseAndApplicationRestart(bool closeSearchFirst)
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects([Project("one", "Port", remaining: 100)], [], null, []),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 50),
                ColonizationShoppingViewModelTests.Quote(2, "steel", 50),
            ])
        );
        shopping.ReferenceSystem = "Sol";
        await shopping.SearchAsync();
        shopping.Results.Single(row => row.Station == "Port 1").ToggleWishListCommand.Execute(null);
        shopping.Results.Single(row => row.Station == "Port 2").ToggleWishListCommand.Execute(null);
        if (closeSearchFirst)
        {
            shopping.Dispose();
        }
        model.WishList.Stations[0].Children[0].RemoveCommand.Execute(null);
        Assert.Equal("Port 2", Assert.Single(model.WishList.Stations).Label);
        if (!closeSearchFirst)
        {
            Assert.False(shopping.Results[0].IsWishListed);
            Assert.False(shopping.Results[0].AvailableSupplies[0].IsWishListed);
        }
        model.WishList.Stations[0].RemoveCommand.Execute(null);
        Assert.False(model.WishList.HasItems);
        shopping.Dispose();
        using ColonizationViewModel restarted = Create(client);
        await restarted.SetCommanderAsync("Test Cmdr");
        Assert.False(restarted.WishList.HasItems);
        using ColonizationShoppingViewModel reopened = restarted.CreateShopping(
            [restarted.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal(2, reopened.Results.Count);
        Assert.All(reopened.Results, row => Assert.False(row.IsWishListed));
        Assert.Empty(reopened.CaptureState().WishList);
    }

    [Fact]
    public async Task LegacyWishListsMigrateBeforeClosedWindowRemovalsWithoutGrowingOtherPurchases()
    {
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        store.SaveEnabled(true);
        store.SaveShopping(
            "Test Cmdr",
            new ColonizationShoppingSession(
                ["one"],
                new ColonizationShoppingState
                {
                    ReferenceSystem = "Sol",
                    Quotes =
                    [
                        ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
                        ColonizationShoppingViewModelTests.Quote(2, "steel", 500),
                    ],
                    WishList = [new("1", "steel"), new("2", "steel")],
                }
            )
        );
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects([Project("one", "Port", remaining: 300)], [], null, []),
        };
        using ColonizationViewModel model = Create(client);
        await model.SetCommanderAsync("Test Cmdr");
        Assert.Equal("Steel: 200", model.WishList.Stations[1].Children[0].Label);
        model.WishList.Stations[0].RemoveCommand.Execute(null);
        Assert.Equal("Steel: 200", Assert.Single(model.WishList.Stations).Children[0].Label);
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal("Steel: 100", shopping.NeedsText);
        Assert.Equal(200, Assert.Single(shopping.CaptureState().WishList).Quantity);
    }

    [Theory]
    [InlineData(false, 150)]
    [InlineData(true, 150)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public async Task ProjectRefreshPermanentlyCapsPurchasesWhetherSearchIsOpenOrClosed(bool closeSearch, int remaining)
    {
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
                ColonizationShoppingViewModelTests.Quote(2, "steel", 500),
            ])
        );
        shopping.ReferenceSystem = "Sol";
        await shopping.SearchAsync();
        shopping.Results.Single(row => row.Station == "Port 1").ToggleWishListCommand.Execute(null);
        shopping.Results.Single(row => row.Station == "Port 2").ToggleWishListCommand.Execute(null);
        if (closeSearch)
        {
            shopping.Dispose();
        }
        client.Workspace = ShoppingWorkspace(remaining);
        await model.RefreshAsync();
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal(remaining, store.LoadShopping("Test Cmdr")!.Search!.WishList.Sum(item => item.Quantity));
        client.Workspace = ShoppingWorkspace(300);
        await model.RefreshAsync();
        Assert.Equal(remaining, store.LoadShopping("Test Cmdr")!.Search!.WishList.Sum(item => item.Quantity));
        using ColonizationViewModel restarted = Create(client);
        await restarted.SetCommanderAsync("Test Cmdr");
        Assert.Equal(remaining > 0, restarted.WishList.HasItems);
        using ColonizationShoppingViewModel reopened = restarted.CreateShopping(
            [restarted.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal(300 - remaining, Assert.Single(reopened.Needs).Remaining);
        Assert.Equal(remaining, reopened.CaptureState().WishList.Sum(item => item.Quantity));
    }

    [Fact]
    public async Task EmptyAuthoritativeProjectSnapshotInvalidatesTheSavedSelection()
    {
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal("Reopen Search", model.SearchMarketsButtonText);
        client.Workspace = new ColonizationCommanderProjects([], [], null, []);
        await model.RefreshAsync();
        Assert.Equal("Search Markets", model.SearchMarketsButtonText);
        Assert.False(model.CanSearchMarkets);
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Empty(store.LoadShopping("Test Cmdr")!.ProjectIds);
        Assert.Null(store.LoadShopping("Test Cmdr")!.Search);
        shopping.Dispose();
        Assert.Null(store.LoadShopping("Test Cmdr")!.Search);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleLegacyCacheCannotShrinkPurchasesOrRemoveNewerSelectedProjects(bool omitSelectedBuild)
    {
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        store.SaveEnabled(true);
        store.SaveShopping(
            "Test Cmdr",
            new ColonizationShoppingSession(
                ["one"],
                new ColonizationShoppingState
                {
                    ReferenceSystem = "Sol",
                    Quotes = [ColonizationShoppingViewModelTests.Quote(1, "steel", 500)],
                    WishList = [new("1", "steel", 300)],
                }
            )
        );
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "F123-colony.json"),
            JsonSerializer.Serialize(
                new
                {
                    cmdr = "Test Cmdr",
                    projects = new[] { Project(omitSelectedBuild ? "older" : "one", "Cached Port", 100) },
                }
            )
        );
        StubRavenColonialClient client = ShoppingClient(300);
        var response = new TaskCompletionSource<ColonizationCommanderProjects>();
        var started = new TaskCompletionSource();
        client.LoadWorkspace = _ =>
        {
            started.SetResult();
            return response.Task;
        };
        using var model = new ColonizationViewModel(
            store,
            client,
            legacyProfileStore: new LegacyColonizationProfileStore(directory)
        );
        model.SetCommanderProfile("F123", true, apiKey: null);
        Task load = model.SetCommanderAsync("Test Cmdr");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(100, Assert.Single(model.Projects).Project.Commodities["steel"]);
        Assert.False(model.CanSearchMarkets);
        model.OpenShopping();
        Assert.Null(model.MarketSearchWorkspace);
        Assert.Throws<InvalidOperationException>(() =>
            model.CreateShopping([model.Projects[0].Project], new ColonizationShoppingViewModelTests.SearchStub([]))
        );
        Assert.Equal(300, Assert.Single(store.LoadShopping("Test Cmdr")!.Search!.WishList).Quantity);
        Assert.Equal("one", Assert.Single(store.LoadShopping("Test Cmdr")!.ProjectIds));
        response.SetResult(client.Workspace);
        await load;
        Assert.True(Assert.Single(model.Projects).IsShoppingSelected);
        Assert.True(model.CanSearchMarkets);
        using ColonizationShoppingViewModel reopened = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal(300, Assert.Single(reopened.CaptureState().WishList).Quantity);
        Assert.Equal("Steel: 300", Assert.Single(Assert.Single(model.WishList.Stations).Children).Label);
        Assert.Equal(300, Assert.Single(store.LoadShopping("Test Cmdr")!.Search!.WishList).Quantity);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LateProjectResponsesCannotChangeSavedPurchasesAfterShutdown(
        bool disposeWorkspace,
        bool failRequest
    )
    {
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 500),
            ])
        );
        shopping.ReferenceSystem = "Sol";
        await shopping.SearchAsync();
        shopping.Results[0].ToggleWishListCommand.Execute(null);
        shopping.Dispose();
        var response = new TaskCompletionSource<ColonizationCommanderProjects>();
        client.LoadWorkspace = _ => response.Task;
        Task refresh = model.RefreshAsync();
        if (disposeWorkspace)
        {
            model.Dispose();
        }
        else
        {
            model.IsEnabled = false;
        }
        string status = model.StatusMessage;
        if (failRequest)
        {
            response.SetException(new HttpRequestException("offline"));
        }
        else
        {
            response.SetResult(ShoppingWorkspace(100));
        }
        await refresh;
        Assert.Equal(status, model.StatusMessage);
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal(300, Assert.Single(store.LoadShopping("Test Cmdr")!.Search!.WishList).Quantity);
        if (disposeWorkspace)
        {
            Assert.Equal(300, Assert.Single(model.Projects).Project.Commodities["steel"]);
        }
        else
        {
            Assert.Empty(model.Projects);
        }
    }

    [Fact]
    public void ShoppingStoreRejectsInvalidDataAndPreservesOtherCommandersAndUiSettings()
    {
        string path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);
        store.SaveEnabled(true);
        Assert.Null(store.LoadShopping("Test"));
        var session = new ColonizationShoppingSession(["one"], new ColonizationShoppingState());
        store.SaveShopping("Test", session);
        store.SaveShopping("Other", new ColonizationShoppingSession(["two"], null));
        Assert.True(store.LoadEnabled());
        Assert.Equal("one", Assert.Single(store.LoadShopping("TEST")!.ProjectIds));
        Assert.Equal("two", Assert.Single(store.LoadShopping("other")!.ProjectIds));
        string shoppingPath = path + ".shopping.json";
        foreach (
            string invalid in new[]
            {
                "broken",
                "[]",
                "{\"Version\":2}",
                "{\"Version\":\"bad\"}",
                "{\"Version\":1,\"TEST\":{\"ProjectIds\":null}}",
                "{\"Version\":1,\"TEST\":{\"ProjectIds\":[null]}}",
            }
        )
        {
            File.WriteAllText(shoppingPath, invalid);
            Assert.Null(store.LoadShopping("Test"));
        }
        JsonObject validState = JsonSerializer.SerializeToNode(session.Search)!.AsObject();
        foreach (
            string property in new[]
            {
                "ReferenceSystem",
                "RadiusText",
                "MaximumAgeText",
                "MaximumArrivalText",
                "MinimumPad",
                "Sort",
                "Warnings",
                "WarningEntries",
                "CompletedSystem",
                "ExcludedCommodities",
                "ExpandedMarkets",
                "SearchedCommodities",
                "Quotes",
                "WishList",
            }
        )
        {
            JsonObject invalidState = validState.DeepClone().AsObject();
            invalidState[property] = null;
            WriteShopping(invalidState);
            Assert.Null(store.LoadShopping("Test"));
        }
        foreach (string? warning in new string?[] { null, "", " " })
        {
            JsonObject invalidState = validState.DeepClone().AsObject();
            invalidState["WarningEntries"] = new JsonArray(JsonValue.Create(warning));
            WriteShopping(invalidState);
            Assert.Null(store.LoadShopping("Test"));
        }
        validState["Quotes"] = new JsonArray((JsonNode?)null);
        WriteShopping(validState);
        Assert.Null(store.LoadShopping("Test"));

        void WriteShopping(JsonObject state)
        {
            File.WriteAllText(
                shoppingPath,
                new JsonObject
                {
                    ["Version"] = 1,
                    ["TEST"] = new JsonObject { ["ProjectIds"] = new JsonArray("one"), ["Search"] = state },
                }.ToJsonString()
            );
        }
    }
}
