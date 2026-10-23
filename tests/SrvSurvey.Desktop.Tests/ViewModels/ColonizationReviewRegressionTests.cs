using System.Globalization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData("{\"Colonization\":[]}")]
    [InlineData("{\"Colonization\":{\"HiddenPlannedProjects\":[]}}")]
    [InlineData("{\"Colonization\":{\"HiddenPlannedProjects\":42}}")]
    [InlineData("{\"Colonization\":{\"HiddenPlannedProjects\":{\"TEST CMDR\":{}}}}")]
    public void KiloReviewMalformedHiddenPlansAreOptional(string json)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui.json");
        File.WriteAllText(path, json);
        Assert.Empty(new ColonizationSettingsStore(path).LoadHiddenPlannedProjects("Test Cmdr"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KiloReviewAcknowledgedLiveFocusSurvivesLocalWriteFailure(bool fromPlan)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        client.Workspace = client.Workspace with
        {
            Projects = [.. client.Workspace.Projects, Project("second", "Second", 200) with { SystemName = "Sol" }],
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        if (fromPlan)
        {
            await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.IsPlanned));
        }
        string path = Path.Combine(directory, "ui.json");
        File.Delete(path);
        Directory.CreateDirectory(path);
        await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.BuildId == "second"));
        Assert.Equal("second", client.PrimaryProjectRequests[^1]);
        Assert.Equal("second", Assert.Single(model.Projects, row => row.IsFocused).Project.BuildId);
        Assert.Equal(200, model.CommodityOverlay.Plan.TotalRemaining);
        Assert.Contains("could not be saved", model.StatusMessage);
    }

    [Fact]
    public async Task KiloReviewRemovingTheSelectedPlanRetiresMarketSearch()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.MarketSearchFactory = () => new ColonizationShoppingViewModelTests.SearchStub([]);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        model.Projects.Single(row => row.Project.IsPlanned).IsShoppingSelected = true;
        model.SelectedWorkspaceTabIndex = 1;
        ColonizationShoppingViewModel search = model.MarketSearchWorkspace!;
        Assert.NotNull(search);
        model.SystemEditor.SelectedSite = model.SystemEditor.Sites.Single();
        model.SystemEditor.RemoveSiteCommand.Execute(null);
        Assert.False(model.CanSearchMarkets);
        Assert.Null(model.MarketSearchWorkspace);
        Assert.False(search.CanSearch);
        Assert.Empty(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")).LoadShopping("Test Cmdr")!.ProjectIds
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KiloReviewEditorEnrichmentPreservesShoppingAllocations(bool statusUpdate)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        ColonizationSystemRecord home = client.PlanningSystems!["Sol"];
        client.PlanningSystems["Sol"] = home with
        {
            Sites =
            [
                .. home.Sites,
                new()
                {
                    Id = "complete",
                    Name = "Existing Port",
                    BuildType = "asteroid",
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ],
        };
        using ColonizationViewModel model = Create(client);
        model.MarketSearchFactory = () =>
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
            ]);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        model.Projects.Single(row => row.Project.IsPlanned).IsShoppingSelected = true;
        model.SelectedWorkspaceTabIndex = 1;
        ColonizationShoppingViewModel search = model.MarketSearchWorkspace!;
        await search.SearchAsync();
        search.Results[0].AvailableSupplies[0].ToggleWishListCommand.Execute(null);
        long quantity = Assert.Single(search.CaptureState().WishList).Quantity;
        Assert.Equal(2, model.SystemEditor.Sites.Count);
        Assert.True(model.SystemEditor.CanEdit);
        if (statusUpdate)
        {
            model.SystemEditor.UpdateStatus(
                new()
                {
                    Destination = new()
                    {
                        System = 42,
                        Body = 2,
                        Name = "Existing Port",
                    },
                }
            );
        }
        else
        {
            model.SystemEditor.ApplyJournalEvents([
                Event(
                    "Docked",
                    "\"SystemAddress\":42,\"StarSystem\":\"Sol\",\"StationName\":\"Existing Port\",\"MarketID\":500,\"StationType\":\"StationAsteroid\",\"BodyID\":2"
                ),
            ]);
        }
        Assert.Equal(quantity, Assert.Single(model.MarketSearchWorkspace!.CaptureState().WishList).Quantity);
        Assert.Equal(
            quantity,
            Assert
                .Single(
                    new ColonizationSettingsStore(Path.Combine(directory, "ui.json"))
                        .LoadShopping("Test Cmdr")!
                        .Search!.WishList
                )
                .Quantity
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KiloReviewBufferedReadsDoNotUndoLocalPlanChanges(bool remove)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        client.Workspace = client.Workspace with
        {
            Projects = [.. client.Workspace.Projects, Project("remote", "Remote", 10) with { SystemName = "Achenar" }],
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        var entered = new TaskCompletionSource();
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        client.LoadPlanningSystem = (name, token) =>
        {
            if (name == "Sol")
            {
                return Task.FromResult(client.PlanningSystems!["Sol"]);
            }
            entered.TrySetResult();
            return response.Task.WaitAsync(token);
        };
        Task refresh = model.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (remove)
        {
            model.SystemEditor.SelectedSite = model.SystemEditor.Sites.Single();
            model.SystemEditor.RemoveSiteCommand.Execute(null);
        }
        else
        {
            model.SystemEditor.Sites[0].Name = "Newest local plan";
        }
        response.SetResult(new() { Name = "Achenar", SystemAddress = 43 });
        await refresh;
        if (remove)
        {
            Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned);
        }
        else
        {
            Assert.Equal(
                "Newest local plan (Plan)",
                Assert.Single(model.Projects, row => row.Project.IsPlanned).BuildName
            );
        }
    }

    [Fact]
    public async Task KiloReviewCancelledPlanningRefreshKeepsUnsavedShowSelection()
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects.Single(row => !row.Project.IsPlanned).IsShown = false;
        var entered = new TaskCompletionSource();
        client.LoadPlanningSystem = (_, token) =>
        {
            entered.TrySetResult();
            return new TaskCompletionSource<ColonizationSystemRecord>().Task.WaitAsync(token);
        };
        model.IsAutoRefreshEnabled = true;
        (await clock.NextTickAsync()).SetResult();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.IsAutoRefreshEnabled = false;
        await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.Projects.Single(row => !row.Project.IsPlanned).IsShown);
        Assert.True(model.HasUnsavedProjectVisibility);
        await model.SaveProjectVisibilityAsync();
        Assert.Contains("active", client.LastSavedHiddenIds);
    }

    private static StubRavenColonialClient ReviewPlanningClient()
    {
        StubRavenColonialClient client = FocusClient();
        client.LoadPlanningSystem = (identifier, _) =>
            Task.FromResult(
                client.PlanningSystems!.GetValueOrDefault(identifier)
                    ?? client.PlanningSystems!.Values.Single(system =>
                        system.SystemAddress.ToString(CultureInfo.InvariantCulture) == identifier
                    )
            );
        return client;
    }

    [Fact]
    public async Task KiloReviewEditorPreservesUnspecifiedLegacyStatus()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        ColonizationSystemRecord home = client.PlanningSystems!["Sol"];
        client.PlanningSystems["Sol"] = home with
        {
            Sites =
            [
                new()
                {
                    Id = "legacy",
                    Name = "Existing Port",
                    BuildType = "asteroid",
                    MarketId = 500,
                },
            ],
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        Assert.False(Assert.Single(model.SystemEditor.Sites).ToSite().HasExplicitStatus);
        Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned);
    }
}
