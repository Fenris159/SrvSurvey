using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public async Task LocalPlanningChangesPreserveOtherSystemsDuringFullRefresh(
        bool duringWorkspace,
        bool remove,
        int jumps
    )
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        ColonizationSystemRecord remote = FilterPlanningSystem("Remote Colony", 47);
        client.PlanningSystems![remote.Name] = remote;
        client.DiscoveredPlanningSystems = [remote];
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        await model.SystemEditor.LoadAsync();
        remote = remote with { Sites = [remote.Sites[0] with { Name = "Updated Remote Plan" }] };
        var workspace = new TaskCompletionSource<ColonizationCommanderProjects>();
        var remoteEntered = new TaskCompletionSource();
        var remoteResponse = new TaskCompletionSource<ColonizationSystemRecord>();
        client.LoadWorkspace = _ => duringWorkspace ? workspace.Task : Task.FromResult(client.Workspace);
        client.LoadPlanningSystem = (name, _) =>
        {
            if (name == "Remote Colony")
            {
                remoteEntered.TrySetResult();
                return remoteResponse.Task;
            }
            return Task.FromResult(client.PlanningSystems[name]);
        };
        Task refresh = model.RefreshAsync();
        if (!duringWorkspace)
        {
            await remoteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        if (remove)
        {
            model.SystemEditor.SelectedSite = model.SystemEditor.Sites[0];
            model.SystemEditor.RemoveSiteCommand.Execute(null);
        }
        else
        {
            model.SystemEditor.Sites[0].Name = "Newest Local Plan";
        }
        if (jumps > 0)
        {
            model.UpdateSystemContext("Achenar", null, 43);
        }
        if (jumps > 1)
        {
            model.UpdateSystemContext("Sol", null, 42);
        }
        workspace.TrySetResult(client.Workspace);
        await remoteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        remoteResponse.SetResult(remote);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        await model.CurrentSystemPlanningTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            "Updated Remote Plan (Plan)",
            Assert.Single(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 47).BuildName
        );
        if (remove)
        {
            Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 42);
        }
        else
        {
            Assert.Equal(
                "Newest Local Plan (Plan)",
                Assert.Single(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 42).BuildName
            );
        }
    }

    [Fact]
    public async Task EditorLoadDuringDiscoveryKeepsRemotePlanningUpdates()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        ColonizationSystemRecord remote = FilterPlanningSystem("Remote Colony", 47);
        client.PlanningSystems![remote.Name] = remote;
        client.DiscoveredPlanningSystems = [remote];
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        remote = remote with { Sites = [remote.Sites[0] with { Name = "Updated Remote Plan" }] };
        client.PlanningSystems[remote.Name] = remote;
        var discoveryEntered = new TaskCompletionSource();
        var discovery = new TaskCompletionSource<IReadOnlyList<ColonizationSystemRecord>>();
        client.LoadPlanningDiscovery = (_, _) =>
        {
            discoveryEntered.TrySetResult();
            return discovery.Task;
        };
        Task refresh = model.RefreshAsync();
        await discoveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await model.SystemEditor.LoadAsync();
        discovery.SetResult([remote]);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            "Updated Remote Plan (Plan)",
            Assert.Single(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 47).BuildName
        );
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("commander")]
    [InlineData("refresh")]
    public async Task PlanningDraftsSurviveTravelAndRetireOnAuthoritativeLoadOrCommanderChange(string reset)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "Local Draft";
        model.UpdateSystemContext("Achenar", null, 43);
        await model.CurrentSystemPlanningTask;
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        Assert.Contains(model.Projects, row => row.BuildName == "Local Draft (Plan)");
        if (reset == "commander")
        {
            await model.SetCommanderAsync("Other Cmdr");
        }
        else
        {
            if (reset == "reload")
            {
                await model.SystemEditor.LoadAsync();
            }
            await model.RefreshAsync();
        }
        Assert.Contains(model.Projects, row => row.BuildName == "Test Project (Plan)");
        Assert.DoesNotContain(model.Projects, row => row.BuildName == "Local Draft (Plan)");
    }
}
