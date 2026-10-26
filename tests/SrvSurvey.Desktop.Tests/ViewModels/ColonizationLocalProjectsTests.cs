using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    /// <summary>Cancelling a retained-project read cannot replace an unsaved Show selection with server state.</summary>
    [Fact]
    public async Task CancelledRetainedProjectRefreshPreservesUnsavedSelectionForSave()
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = LocalScopeClient();
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Visitor", null, 42);
        model.ShowUnaffiliatedProjectsInCurrentSystem = true;
        await model.CurrentSystemPlanningTask;
        await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.BuildId == "visitor-live"));
        await model.CurrentSystemPlanningTask;
        model.Projects.Single(row => row.Project.BuildId == "own").IsShown = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.LoadProject = (_, token) =>
        {
            entered.TrySetResult();
            return new TaskCompletionSource<ColonizationProject?>().Task.WaitAsync(token);
        };
        model.IsAutoRefreshEnabled = true;
        (await clock.NextTickAsync()).SetResult();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.IsAutoRefreshEnabled = false;
        await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.Projects.Single(row => row.Project.BuildId == "own").IsShown);
        Assert.True(model.HasUnsavedProjectVisibility);
        client.LoadProject = null;
        await model.SaveProjectVisibilityAsync();
        Assert.Contains("own", client.LastSavedHiddenIds);
        Assert.False(model.Projects.Single(row => row.Project.BuildId == "own").IsShown);
    }

    private static StubRavenColonialClient LocalScopeClient()
    {
        ColonizationSystemRecord home = FilterPlanningSystem("Home", 1);
        ColonizationSystemRecord visitor = FilterPlanningSystem("Visitor", 42) with { Architect = "Someone Else" };
        ColonizationSystemRecord next = FilterPlanningSystem("Next", 43) with { Architect = "Someone Else" };
        ColonizationProject live = Project(
            "visitor-live",
            "Visitor Build",
            100,
            10,
            42,
            architectName: "Someone Else"
        ) with
        {
            SystemName = "Visitor",
        };
        visitor = visitor with
        {
            Sites =
            [
                .. visitor.Sites,
                new()
                {
                    Id = "live",
                    Name = live.BuildName,
                    BuildId = live.BuildId,
                    MarketId = 10,
                    Status = ColonizationSystemSiteStatus.Build,
                },
            ],
        };
        return new StubRavenColonialClient
        {
            Workspace = new([Project("own", "Own Build", 100) with { SystemName = "Home" }], [], null, []),
            DiscoveredPlanningSystems = [home],
            PlanningSystems = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Home"] = home,
                ["Visitor"] = visitor,
                ["Next"] = next,
            },
            SiteProjectResponse = live,
        };
    }

    private async Task<ColonizationViewModel> LocalScopeModel(StubRavenColonialClient client)
    {
        ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        return model;
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"AffiliatedBuildIds\":null}")]
    [InlineData("{\"AffiliatedSystemNames\":null}")]
    [InlineData("{\"RetainedProjects\":null}")]
    [InlineData("{\"RetainedProjects\":[null]}")]
    [InlineData("{\"RetainedProjects\":[{\"Project\":null}]}")]
    [InlineData("{\"RetainedProjects\":[{\"Project\":{}}]}")]
    public async Task MalformedLocalProjectCacheCannotCrashCommanderRestore(string value)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui.json");
        await File.WriteAllTextAsync(path + ".local-projects.json", "{\"TEST CMDR\":" + value + "}");
        var store = new ColonizationSettingsStore(path);
        Assert.Null(store.LoadLocalProjects("Test Cmdr"));
        store.SaveLocalProjects("Other Cmdr", new ColonizationProjectScopeState { BrowseEnabled = true });
        Assert.True(store.LoadLocalProjects("Other Cmdr")!.BrowseEnabled);
    }

    [Fact]
    public async Task ReenablingRavenRestoresBrowsingInTheUnchangedCurrentSystem()
    {
        using ColonizationViewModel model = await LocalScopeModel(LocalScopeClient());
        model.UpdateSystemContext("Visitor", null, 42);
        model.ShowUnaffiliatedProjectsInCurrentSystem = true;
        await model.CurrentSystemPlanningTask;
        Assert.Contains(model.Projects, project => project.SystemName == "Visitor");
        model.IsEnabled = false;
        model.IsEnabled = true;
        await model.RefreshAsync();
        await model.CurrentSystemPlanningTask;
        Assert.True(model.ShowUnaffiliatedProjectsInCurrentSystem);
        Assert.Contains(model.Projects, project => project.SystemName == "Visitor");
    }

    [Fact]
    public async Task FailedLocalRetentionSaveDoesNotDeleteTheLegacyPlansAndCanRetry()
    {
        StubRavenColonialClient client = LocalScopeClient();
        string path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);
        store.SavePlannedSystems("Test Cmdr", [client.PlanningSystems!["Visitor"]]);
        Directory.CreateDirectory(path + ".local-projects.json");
        using ColonizationViewModel model = await LocalScopeModel(client);
        Assert.Contains(store.LoadPlannedSystems("Test Cmdr"), system => system.Name == "Visitor");
        Assert.Contains("could not be saved", model.StatusMessage);
        Directory.Delete(path + ".local-projects.json");
        await model.RefreshAsync();
        Assert.DoesNotContain(store.LoadPlannedSystems("Test Cmdr"), system => system.Name == "Visitor");
        model.IsEnabled = false;
        model.ShowUnaffiliatedProjectsInCurrentSystem = true;
        Assert.Empty(model.Projects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFocusedProjectRetentionRemainsVisibleAndRefreshCanRetry(bool planned)
    {
        StubRavenColonialClient client = LocalScopeClient();
        using ColonizationViewModel model = await LocalScopeModel(client);
        model.UpdateSystemContext("Visitor", null, 42);
        model.ShowUnaffiliatedProjectsInCurrentSystem = true;
        await model.CurrentSystemPlanningTask;
        ColonizationProjectRowViewModel row = model.Projects.Single(row =>
            row.SystemName == "Visitor" && row.Project.IsPlanned == planned
        );
        string id = row.Project.BuildId;
        string cache = Path.Combine(directory, "ui.json.local-projects.json");
        File.Delete(cache);
        Directory.CreateDirectory(cache);
        await model.ToggleFocusProjectAsync(row);
        Assert.Contains(model.Projects, project => project.Project.BuildId == id && project.IsFocused);
        Assert.Contains("Local project tracking could not be saved", model.StatusMessage);
        model.UpdateSystemContext("Next", null, 43);
        await model.CurrentSystemPlanningTask;
        Assert.Contains(model.Projects, project => project.Project.BuildId == id && project.IsFocused);
        Directory.Delete(cache);
        await model.RefreshAsync();
        using ColonizationViewModel restarted = await LocalScopeModel(client);
        Assert.Contains(restarted.Projects, project => project.Project.BuildId == id && project.IsFocused);
    }

    [Fact]
    public async Task VisitingDoesNotLoadUnrelatedPlansUntilBrowsingIsEnabledAndTravelExpiresOnlyUnselectedData()
    {
        StubRavenColonialClient client = LocalScopeClient();
        var reads = new List<string>();
        client.LoadPlanningSystem = (name, _) =>
        {
            reads.Add(name);
            return Task.FromResult(client.PlanningSystems![name]);
        };
        using ColonizationViewModel model = await LocalScopeModel(client);
        Assert.False(model.ShowUnaffiliatedProjectsInCurrentSystem);
        reads.Clear();
        model.UpdateSystemContext("Visitor", null, 42);
        await model.CurrentSystemPlanningTask;
        Assert.Empty(reads);
        Assert.DoesNotContain(model.Projects, row => row.SystemName == "Visitor");
        model.ShowUnaffiliatedProjectsInCurrentSystem = true;
        await model.CurrentSystemPlanningTask;
        Assert.Equal(["Visitor"], reads);
        Assert.Equal("Visitor", model.ProjectSystemFilter!.SystemName);
        Assert.Equal(ColonizationProjectBuildFilter.All, model.ProjectBuildFilter!.Value);
        Assert.Equal(2, model.VisibleProjects.Count);
        Assert.All(model.VisibleProjects, row => Assert.False(row.IsShown));
        ColonizationProjectRowViewModel live = model.Projects.Single(row => row.Project.BuildId == "visitor-live");
        live.IsShown = true;
        await model.ToggleFocusProjectAsync(
            model.Projects.Single(row => row.Project.IsPlanned && row.SystemName == "Visitor")
        );
        Assert.Empty(client.PrimaryProjectRequests);
        Assert.Empty(client.LinkRequests);
        model.UpdateSystemContext("Next", null, 43);
        await model.CurrentSystemPlanningTask;
        Assert.Contains(model.Projects, row => row.Project.BuildId == "visitor-live" && row.IsShown);
        Assert.Contains(model.Projects, row => row.SystemName == "Visitor" && row.IsFocused);
        Assert.Equal("Next", model.ProjectSystemFilter.SystemName);
        Assert.Single(model.VisibleProjects);
        model.ShowUnaffiliatedProjectsInCurrentSystem = false;
        Assert.DoesNotContain(model.Projects, row => row.SystemName == "Next");
        Assert.Contains(model.Projects, row => row.SystemName == "Visitor");
        using ColonizationViewModel restarted = await LocalScopeModel(client);
        Assert.False(restarted.ShowUnaffiliatedProjectsInCurrentSystem);
        Assert.Contains(restarted.Projects, row => row.Project.BuildId == "visitor-live" && row.IsShown);
        Assert.Contains(restarted.Projects, row => row.SystemName == "Visitor" && row.IsFocused);
        await restarted.ToggleFocusProjectAsync(restarted.Projects.Single(row => row.IsFocused));
        Assert.DoesNotContain(restarted.Projects, row => row.Project.IsPlanned && row.SystemName == "Visitor");
        restarted.Projects.Single(row => row.Project.BuildId == "visitor-live").IsShown = false;
        Assert.DoesNotContain(restarted.Projects, row => row.SystemName == "Visitor");
    }

    [Fact]
    public async Task SuccessfulRefreshCleansLegacyVisitedPlansButPreservesFocusAndApiAffiliatedSharedPlans()
    {
        StubRavenColonialClient client = LocalScopeClient();
        ColonizationSystemRecord shared = FilterPlanningSystem("Shared", 44) with { Architect = "Another Architect" };
        ColonizationSystemRecord focused = FilterPlanningSystem("Focused", 45) with { Architect = "Another Architect" };
        client.DiscoveredPlanningSystems = [.. client.DiscoveredPlanningSystems, shared];
        client.PlanningSystems!["Shared"] = shared;
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        store.SavePlannedSystems("Test Cmdr", [client.PlanningSystems["Visitor"], shared, focused]);
        store.SaveLocalProjectFocus("Test Cmdr", "plan:45:plan");
        using ColonizationViewModel model = await LocalScopeModel(client);
        Assert.DoesNotContain(model.Projects, row => row.SystemName == "Visitor");
        Assert.Contains(model.Projects, row => row.SystemName == "Shared" && row.Project.IsPlanned);
        Assert.Contains(model.Projects, row => row.SystemName == "Focused" && row.IsFocused);
        Assert.DoesNotContain(store.LoadPlannedSystems("Test Cmdr"), system => system.Name is "Visitor" or "Focused");
        Assert.Equal(
            "plan:45:plan",
            Assert.Single(store.LoadLocalProjects("Test Cmdr")!.RetainedProjects).Project.BuildId
        );
        await model.RefreshAsync();
        Assert.DoesNotContain(model.Projects, row => row.SystemName == "Visitor");
    }

    [Fact]
    public async Task FailedDiscoveryPreservesOldCacheAndTheNextSuccessfulRefreshCleansIt()
    {
        StubRavenColonialClient client = LocalScopeClient();
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        store.SavePlannedSystems("Test Cmdr", [client.PlanningSystems!["Visitor"]]);
        client.PlanningDiscoveryFailure = new HttpRequestException("offline discovery");
        using ColonizationViewModel model = await LocalScopeModel(client);
        Assert.Contains(store.LoadPlannedSystems("Test Cmdr"), system => system.Name == "Visitor");
        Assert.Contains("offline discovery", model.StatusMessage);
        client.PlanningDiscoveryFailure = null;
        await model.RefreshAsync();
        Assert.DoesNotContain(model.Projects, row => row.SystemName == "Visitor");
        Assert.DoesNotContain(store.LoadPlannedSystems("Test Cmdr"), system => system.Name == "Visitor");
    }

    [Theory]
    [InlineData("travel")]
    [InlineData("disable")]
    [InlineData("commander")]
    public async Task LateLocalSystemResponsesCannotPopulateAnotherSystemOrCommander(string transition)
    {
        StubRavenColonialClient client = LocalScopeClient();
        using ColonizationViewModel model = await LocalScopeModel(client);
        model.UpdateSystemContext("Visitor", null, 42);
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        client.LoadPlanningSystem = (name, _) =>
            name == "Visitor" ? response.Task : Task.FromResult(client.PlanningSystems![name]);
        model.ShowUnaffiliatedProjectsInCurrentSystem = true;
        Task pending = model.CurrentSystemPlanningTask;
        if (transition == "travel")
        {
            model.UpdateSystemContext("Next", null, 43);
        }
        else if (transition == "disable")
        {
            model.ShowUnaffiliatedProjectsInCurrentSystem = false;
        }
        else
        {
            await model.SetCommanderAsync("Other Cmdr");
        }
        response.SetResult(client.PlanningSystems!["Visitor"]);
        await pending;
        await model.CurrentSystemPlanningTask;
        Assert.DoesNotContain(model.Projects, row => row.SystemName == "Visitor");
    }

    [Fact]
    public async Task DockedUnaffiliatedBuildIsTemporaryUnlessFocusedAndStillReportsDeliveriesAndCompletion()
    {
        StubRavenColonialClient client = LocalScopeClient();
        using ColonizationViewModel model = await LocalScopeModel(client);
        JournalEventEnvelope dock = Event(
            "Docked",
            """
            "MarketID":10,"SystemAddress":42,"StarSystem":"Visitor","StationName":"Orbital Construction Site: Visitor","StationServices":["colonisationcontribution"]
            """
        );
        model.ApplyJournalEvents([dock]);
        await model.SynchronizeLiveProjectsAsync([dock], true);
        ColonizationProjectRowViewModel row = model.Projects.Single(row => row.Project.BuildId == "visitor-live");
        Assert.False(model.ShowUnaffiliatedProjectsInCurrentSystem);
        Assert.False(row.IsShown);
        Assert.Contains(model.CommodityOverlay.Plan.Rows, commodity => commodity.Commodity == "steel");
        await model.ToggleFocusProjectAsync(row);
        Assert.Empty(client.PrimaryProjectRequests);
        JournalEventEnvelope delivery = Event(
            "ColonisationContribution",
            """
            "MarketID":10,"Contributions":[{"Name":"steel","Amount":100}]
            """
        );
        model.ApplyJournalEvents([delivery]);
        await model.SynchronizeLiveProjectsAsync([delivery], true);
        Assert.Equal("visitor-live", Assert.Single(client.Contributions).BuildId);
        Assert.Contains(client.ProjectUpdates, update => update.Commodities?.GetValueOrDefault("steel") == 0);
        JournalEventEnvelope complete = Event(
            "ColonisationConstructionDepot",
            """
            "MarketID":10,"ConstructionProgress":1,"ConstructionComplete":true,"Resources":[{"Name":"steel","RequiredAmount":100,"ProvidedAmount":100}]
            """
        );
        model.ApplyJournalEvents([complete]);
        await model.SynchronizeLiveProjectsAsync([complete], true);
        Assert.Equal(1, client.MarkCompleteCount);
        Assert.True(model.Projects.Single(project => project.Project.BuildId == "visitor-live").Project.IsComplete);
        model.ApplyJournalEvents([
            Event(
                "Undocked",
                """
                "MarketID":10
                """
            ),
        ]);
        model.UpdateSystemContext("Next", null, 43);
        Assert.Contains(model.Projects, project => project.Project.BuildId == "visitor-live" && project.IsFocused);
        Assert.Empty(client.LinkRequests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task UnfocusedDockedPreviewExpiresAndDisabledAccessCannotRepopulateProjects(
        bool disableAccess,
        bool failedAffiliations
    )
    {
        StubRavenColonialClient client = LocalScopeClient();
        if (failedAffiliations)
        {
            client.Failure = new HttpRequestException("offline commander affiliation");
        }
        using ColonizationViewModel model = await LocalScopeModel(client);
        JournalEventEnvelope dock = Event(
            "Docked",
            """
            "MarketID":10,"SystemAddress":42,"StarSystem":"Visitor","StationName":"Orbital Construction Site: Visitor","StationServices":["colonisationcontribution"]
            """
        );
        model.ApplyJournalEvents([dock]);
        await model.SynchronizeLiveProjectsAsync([dock], true);
        Assert.Contains(model.Projects, row => row.Project.BuildId == "visitor-live");
        if (disableAccess)
        {
            model.IsEnabled = false;
        }
        model.ApplyJournalEvents([
            Event(
                "Undocked",
                """
                "MarketID":10
                """
            ),
        ]);
        Assert.DoesNotContain(model.Projects, row => row.Project.BuildId == "visitor-live");
        if (disableAccess)
        {
            Assert.Empty(model.Projects);
            model.UpdateSystemContext("Next", null, 43);
            Assert.Empty(model.Projects);
        }
    }
}
