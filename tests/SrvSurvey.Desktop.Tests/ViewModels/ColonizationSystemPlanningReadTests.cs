using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task JumpPlanningReadsOnlyCurrentSystemAndKeepsOtherPlans()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        int discoveries = client.PlanningDiscoveryCommanders.Count;
        var requests = new List<string>();
        ColonizationSystemRecord next = client.PlanningSystems!["Sol"] with
        {
            Name = "Achenar",
            SystemAddress = 43,
            Sites = [client.PlanningSystems["Sol"].Sites[0] with { Id = "remote", Name = "Remote Plan" }],
        };
        client.LoadPlanningSystem = (name, _) =>
        {
            requests.Add(name);
            return Task.FromResult(name == "Achenar" ? next : client.PlanningSystems[name]);
        };
        model.UpdateSystemContext("Achenar", null, 43);
        await model.CurrentSystemPlanningTask;
        Assert.Equal(["Achenar"], requests);
        Assert.Equal(discoveries, client.PlanningDiscoveryCommanders.Count);
        Assert.Contains(model.Projects, row => row.BuildName == "Remote Plan (Plan)");
        Assert.Contains(model.Projects, row => row.BuildName == "Test Project (Plan)");
        model.UpdateSystemContext("Achenar", null, 43);
        Assert.Single(requests);
    }

    [Theory]
    [InlineData("jump", false)]
    [InlineData("consent", false)]
    [InlineData("commander", false)]
    [InlineData("dispose", false)]
    [InlineData("refresh", false)]
    [InlineData("jump", true)]
    [InlineData("consent", true)]
    [InlineData("commander", true)]
    [InlineData("dispose", true)]
    [InlineData("refresh", true)]
    public async Task JumpPlanningCancelsSupersededReads(string change, bool honorsCancellation)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        ColonizationSystemRecord home = client.PlanningSystems!["Sol"];
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        CancellationToken captured = default;
        client.LoadPlanningSystem = (_, token) =>
        {
            captured = token;
            return honorsCancellation ? response.Task.WaitAsync(token) : response.Task;
        };
        try
        {
            model.UpdateSystemContext("Sol", null, 42);
            Task pending = model.CurrentSystemPlanningTask;
            Assert.True(captured.CanBeCanceled);
            client.LoadPlanningSystem = (name, _) => Task.FromResult(client.PlanningSystems[name]);
            switch (change)
            {
                case "jump":
                    model.UpdateSystemContext("Achenar", null, 43);
                    break;
                case "consent":
                    model.IsEnabled = false;
                    model.IsEnabled = true;
                    break;
                case "commander":
                    await model.SetCommanderAsync("Other Cmdr");
                    break;
                case "dispose":
                    model.Dispose();
                    break;
                case "refresh":
                    await model.RefreshAsync();
                    break;
            }
            Assert.True(captured.IsCancellationRequested);
            response.TrySetResult(home with { Sites = [home.Sites[0] with { Name = "Stale Plan" }] });
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(model.Projects, row => row.BuildName == "Stale Plan (Plan)");
        }
        finally
        {
            response.TrySetResult(home);
        }
    }

    [Fact]
    public async Task JumpPlanningKeepsNewerLocalEdits()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        await model.SystemEditor.LoadAsync();
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        client.LoadPlanningSystem = (_, _) => response.Task;
        model.UpdateSystemContext("Sol", new(1, 2, 3), 42);
        Task pending = model.CurrentSystemPlanningTask;
        model.SystemEditor.Sites[0].Name = "Newer Local Plan";
        response.SetResult(client.PlanningSystems!["Sol"]);
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(model.Projects, row => row.BuildName == "Newer Local Plan (Plan)");
        Assert.DoesNotContain(model.Projects, row => row.BuildName == "Test Project (Plan)");
    }

    [Fact]
    public async Task JumpPlanningFailureKeepsCachedNeeds()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        client.LoadPlanningSystem = (_, _) =>
            Task.FromException<ColonizationSystemRecord>(new HttpRequestException("Offline"));
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        Assert.Contains(model.Projects, row => row.BuildName == "Test Project (Plan)");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task JumpsDuringFullRefreshKeepDiscoveredAndUpdatedRemotePlans(bool duringSystemRead, bool cached)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        ColonizationSystemRecord remote = FilterPlanningSystem("Remote Colony", 47);
        client.PlanningSystems![remote.Name] = remote;
        client.DiscoveredPlanningSystems = cached ? [remote] : [];
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        if (cached)
        {
            model.UpdateSystemContext("Sol", null, 42);
            await model.CurrentSystemPlanningTask;
            await model.SystemEditor.LoadAsync();
        }
        int discoveries = client.PlanningDiscoveryCommanders.Count;
        remote = remote with { Sites = [remote.Sites[0] with { Name = "Updated Remote Plan" }] };
        client.PlanningSystems[remote.Name] = remote;
        client.DiscoveredPlanningSystems = [remote];
        var discovery = new TaskCompletionSource<IReadOnlyList<ColonizationSystemRecord>>();
        var system = new TaskCompletionSource<ColonizationSystemRecord>();
        var requests = new List<string>();
        if (!duringSystemRead)
        {
            client.PlanningDiscoveryTask = discovery.Task;
        }
        client.LoadPlanningSystem = (name, _) =>
        {
            requests.Add(name);
            return duringSystemRead && name == "Sol" ? system.Task : Task.FromResult(client.PlanningSystems[name]);
        };
        Task refresh = model.RefreshAsync();
        Assert.True(model.IsBusy);
        model.UpdateSystemContext("Achenar", null, 43);
        model.UpdateSystemContext("Sol", null, 42);
        model.UpdateSystemContext("Achenar", null, 43);
        discovery.TrySetResult([remote]);
        system.TrySetResult(client.PlanningSystems["Sol"]);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        await model.CurrentSystemPlanningTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            "Updated Remote Plan (Plan)",
            Assert.Single(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 47).BuildName
        );
        Assert.Equal(discoveries + 1, client.PlanningDiscoveryCommanders.Count);
        Assert.Single(requests, name => name == "Sol");
        Assert.Single(requests, name => name == "Remote Colony");
        Assert.False(model.IsBusy);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public async Task QueuedJumpSurvivesAutoRefreshCancellationDuringDiscovery(
        bool expire,
        bool honorsCancellation,
        bool withdrawConsent
    )
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        ColonizationSystemRecord destination = FilterPlanningSystem("Achenar", 43);
        client.PlanningSystems!["Achenar"] = destination;
        var workspaceEntered = new TaskCompletionSource();
        var workspace = new TaskCompletionSource<ColonizationCommanderProjects>();
        var discoveryEntered = new TaskCompletionSource();
        var discovery = new TaskCompletionSource<IReadOnlyList<ColonizationSystemRecord>>();
        client.LoadWorkspace = _ =>
        {
            workspaceEntered.TrySetResult();
            return workspace.Task;
        };
        client.LoadPlanningDiscovery = (_, token) =>
        {
            discoveryEntered.TrySetResult();
            return honorsCancellation ? discovery.Task.WaitAsync(token) : discovery.Task;
        };
        client.LoadPlanningSystem = (name, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(client.PlanningSystems[name]);
        };
        try
        {
            model.IsAutoRefreshEnabled = true;
            (await clock.NextTickAsync()).SetResult();
            await workspaceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.UpdateSystemContext("Achenar", null, 43);
            workspace.SetResult(client.Workspace);
            await discoveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (withdrawConsent)
            {
                model.IsEnabled = false;
                model.IsEnabled = true;
            }
            else if (expire)
            {
                clock.Expire();
                await WaitForAsync(() => !model.IsAutoRefreshEnabled);
            }
            else
            {
                model.IsAutoRefreshEnabled = false;
            }
            discovery.TrySetResult([]);
            await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
            await model.CurrentSystemPlanningTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (withdrawConsent)
            {
                Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 43);
            }
            else
            {
                Assert.Contains(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 43);
            }
            Assert.False(model.IsBusy);
            Assert.False(model.IsAutoRefreshEnabled);
        }
        finally
        {
            workspace.TrySetResult(client.Workspace);
            discovery.TrySetResult([]);
        }
    }
}
