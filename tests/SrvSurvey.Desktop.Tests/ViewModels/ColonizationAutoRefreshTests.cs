using System.Threading.Channels;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task AutoRefreshPollsEveryMinuteUpdatesShoppingAndStopsAfterOneHour()
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsAutoRefreshEnabled = true;
        Assert.False(model.IsAutoRefreshEnabled);
        model.IsEnabled = true;
        model.IsAutoRefreshEnabled = true;
        Assert.False(model.CanAutoRefresh);
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        model.UpdateSystemContext("Sol", null, 1);
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
            ])
        );
        await shopping.SearchAsync();
        shopping.Results[0].ToggleWishListCommand.Execute(null);
        Assert.Contains("Steel: 200", shopping.NeedsText);
        model.IsAutoRefreshEnabled = true;
        model.IsAutoRefreshEnabled = true;
        Assert.Equal(2, client.LoadCount);
        Assert.Equal(TimeSpan.FromHours(1), clock.Lifetime);
        TaskCompletionSource tick = await clock.NextTickAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), clock.Interval);
        client.Workspace = ShoppingWorkspace(150);
        tick.SetResult();
        tick = await clock.NextTickAsync();
        Assert.Equal(3, client.LoadCount);
        Assert.Equal(150, model.Projects[0].Project.Commodities["steel"]);
        Assert.Contains("Steel: 50", shopping.NeedsText);
        Assert.Equal("Steel: 100", Assert.Single(Assert.Single(model.WishList.Stations).Children).Label);
        client.Workspace = ShoppingWorkspace(40);
        tick.SetResult();
        await clock.NextTickAsync();
        Assert.True(shopping.IsFullyPlanned);
        Assert.Equal("Steel: 40", Assert.Single(Assert.Single(model.WishList.Stations).Children).Label);
        clock.Expire();
        await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsAutoRefreshEnabled);
        Assert.Equal(4, client.LoadCount);
    }

    [Fact]
    public async Task AutoRefreshSkipsBusyRequestsAndCancelsSlowRequestsAtExpiry()
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        var response = new TaskCompletionSource<ColonizationCommanderProjects>();
        client.LoadWorkspace = _ => response.Task;
        Task manual = model.RefreshAsync();
        model.IsAutoRefreshEnabled = true;
        (await clock.NextTickAsync()).SetResult();
        TaskCompletionSource tick = await clock.NextTickAsync();
        Assert.Equal(2, client.LoadCount);
        response.SetResult(client.Workspace);
        await manual;
        client.LoadWorkspace = _ => new TaskCompletionSource<ColonizationCommanderProjects>().Task;
        tick.SetResult();
        await WaitForAsync(() => client.LoadCount == 3);
        Assert.True(model.IsBusy);
        clock.Expire();
        await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsBusy);
        Assert.False(model.IsAutoRefreshEnabled);
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("consent")]
    [InlineData("commander")]
    [InlineData("dispose")]
    public async Task AutoRefreshStopsOnSessionChangesAndDoesNotPersist(string stop)
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.IsAutoRefreshEnabled = true;
        await clock.NextTickAsync();
        switch (stop)
        {
            case "toggle":
                model.IsAutoRefreshEnabled = false;
                model.IsAutoRefreshEnabled = false;
                break;
            case "consent":
                model.IsEnabled = false;
                break;
            case "commander":
                await model.SetCommanderAsync("Other Cmdr");
                break;
            case "dispose":
                model.Dispose();
                model.IsAutoRefreshEnabled = true;
                Assert.False(model.CanAutoRefresh);
                break;
        }
        await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsAutoRefreshEnabled);
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync(model.CommanderName);
        Assert.False(reopened.IsAutoRefreshEnabled);
    }

    [AvaloniaFact]
    public async Task CommanderSwitchDuringAutomaticLoadStillFetchesTheNewCommander()
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        var started = new TaskCompletionSource();
        client.LoadWorkspace = commander =>
        {
            if (commander == "Other Cmdr")
            {
                return Task.FromResult(ShoppingWorkspace(50));
            }
            started.TrySetResult();
            return new TaskCompletionSource<ColonizationCommanderProjects>().Task;
        };
        model.IsAutoRefreshEnabled = true;
        (await clock.NextTickAsync()).SetResult();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await model.SetCommanderAsync("Other Cmdr");
        await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsAutoRefreshEnabled);
        Assert.False(model.IsBusy);
        Assert.Equal("Other Cmdr", model.CommanderName);
        Assert.Equal(3, client.LoadCount);
        Assert.Equal(50, Assert.Single(model.Projects).Project.Commodities["steel"]);
    }

    [Fact]
    public async Task AutoRefreshCanRestartAndSurvivesTransientApiFailures()
    {
        var clock = new AutoRefreshClock();
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.IsAutoRefreshEnabled = true;
        await clock.NextTickAsync();
        Task previous = model.AutoRefreshTask;
        model.IsAutoRefreshEnabled = false;
        model.IsAutoRefreshEnabled = true;
        await previous.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsAutoRefreshEnabled);
        client.Failure = new HttpRequestException("offline");
        (await clock.NextTickAsync()).SetResult();
        TaskCompletionSource tick = await clock.NextTickAsync();
        Assert.Contains("offline", model.StatusMessage);
        Assert.True(model.IsAutoRefreshEnabled);
        client.Failure = null;
        tick.SetResult();
        await clock.NextTickAsync();
        Assert.Equal(3, client.LoadCount);
        Assert.Contains("Loaded", model.StatusMessage);
        model.IsAutoRefreshEnabled = false;
        await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MarketRefreshFetchesUpdatedNeedsBeforeQueryingBothMarketProviders()
    {
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        model.UpdateSystemContext("Sol", null, 1);
        var markets = new ColonizationShoppingViewModelTests.SearchStub([]);
        using ColonizationShoppingViewModel shopping = model.CreateShopping([model.Projects[0].Project], markets);
        client.Workspace = ShoppingWorkspace(100);
        await shopping.SearchAsync();
        Assert.Equal(2, client.LoadCount);
        Assert.Equal(100, Assert.Single(markets.LastQuery!.Needs).Required);
        client.Workspace = ShoppingWorkspace(75) with
        {
            Projects =
            [
                Project("one", "Planned Port", 75) with
                {
                    Commodities = new() { ["steel"] = 75, ["water"] = 20 },
                },
            ],
        };
        await shopping.SearchAsync();
        Assert.Equal(3, client.LoadCount);
        Assert.Equal(2, markets.Calls);
        Assert.Equal(75, markets.LastQuery.Needs.Single(need => need.Commodity == "steel").Required);
        Assert.Equal(20, markets.LastQuery.Needs.Single(need => need.Commodity == "water").Required);
        Assert.DoesNotContain("newly required", shopping.Warnings);
        client.Failure = new HttpRequestException("offline");
        await shopping.SearchAsync();
        Assert.Equal(2, markets.Calls);
        Assert.Contains("Refresh failed", shopping.Status);
        client.Failure = null;
        client.Workspace = ShoppingWorkspace(0);
        await shopping.SearchAsync();
        Assert.Empty(shopping.Needs);
        Assert.Empty(shopping.Results);
        Assert.Equal(2, markets.Calls);
        Assert.Contains("no remaining", shopping.Status);
        Assert.True(shopping.CanSearch);
        client.Workspace = ShoppingWorkspace(10);
        await shopping.SearchAsync();
        Assert.Equal(3, markets.Calls);
        Assert.Equal(10, Assert.Single(markets.LastQuery.Needs).Required);
    }

    [Fact]
    public async Task MarketRefreshSharesInFlightProjectLoadAndRejectsObsoleteSelections()
    {
        StubRavenColonialClient client = ShoppingClient(300);
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        model.UpdateSystemContext("Sol", null, 1);
        var markets = new ColonizationShoppingViewModelTests.SearchStub([]);
        using ColonizationShoppingViewModel shopping = model.CreateShopping([model.Projects[0].Project], markets);
        var response = new TaskCompletionSource<ColonizationCommanderProjects>();
        client.LoadWorkspace = _ => response.Task;
        Task projectRefresh = model.RefreshAsync();
        Task marketRefresh = shopping.SearchAsync();
        Assert.Equal(2, client.LoadCount);
        Assert.Equal(0, markets.Calls);
        response.SetResult(ShoppingWorkspace(120));
        await Task.WhenAll(projectRefresh, marketRefresh);
        Assert.Equal(120, Assert.Single(markets.LastQuery!.Needs).Required);
        response = new TaskCompletionSource<ColonizationCommanderProjects>();
        marketRefresh = shopping.SearchAsync();
        model.Projects[0].IsShoppingSelected = false;
        response.SetResult(ShoppingWorkspace(50));
        await marketRefresh;
        Assert.Equal(1, markets.Calls);
        Assert.Contains("Refresh failed", shopping.Status);
        model.IsEnabled = false;
        await shopping.SearchAsync();
        Assert.Equal(3, client.LoadCount);
        Assert.Equal(1, markets.Calls);
    }

    [Fact]
    public async Task WorkspaceIncludesCommanderLinkedPlannedAndBuildSites()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [
                    Project("plan", "Planned Port", 300, marketId: 0),
                    Project("build", "Building Port", 150, marketId: 1234),
                ],
                [],
                null,
                []
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        Assert.Equal(2, model.Projects.Count);
        Assert.Contains(model.Projects, row => row.Project.MarketId == 0);
        Assert.Contains(model.Projects, row => row.Project.MarketId == 1234);
        foreach (ColonizationProjectRowViewModel row in model.Projects)
        {
            row.IsShoppingSelected = true;
        }
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            model.Projects.Select(row => row.Project).ToArray(),
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal(450, Assert.Single(shopping.Needs).Need.Required);
    }

    private static StubRavenColonialClient ShoppingClient(int remaining) =>
        new() { Workspace = ShoppingWorkspace(remaining) };

    private static ColonizationCommanderProjects ShoppingWorkspace(int remaining) =>
        new([Project("one", "Planned Port", remaining)], [], null, []);

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class AutoRefreshClock
    {
        private readonly Channel<TaskCompletionSource> ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        private TaskCompletionSource expiry = new();
        public TimeSpan Interval { get; private set; }
        public TimeSpan Lifetime { get; private set; }

        public Task DelayAsync(TimeSpan duration, CancellationToken token)
        {
            var pending = new TaskCompletionSource();
            if (duration == TimeSpan.FromHours(1))
            {
                Lifetime = duration;
                expiry = pending;
            }
            else
            {
                Interval = duration;
                ticks.Writer.TryWrite(pending);
            }
            return pending.Task.WaitAsync(token);
        }

        public Task<TaskCompletionSource> NextTickAsync() =>
            ticks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public void Expire() => expiry.SetResult();
    }
}
