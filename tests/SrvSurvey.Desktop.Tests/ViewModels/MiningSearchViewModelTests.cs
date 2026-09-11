using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningSearchViewModelTests
{
    [Fact]
    public void EachSearchTableRetainsIndependentSortState()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );

        model.Destination = 0;
        model.SortCommand.Execute("System");
        Assert.Equal("↑", model.SortIndicators["System"]);
        model.Destination = 1;
        Assert.Equal(string.Empty, model.SortIndicators["System"]);
        model.SortCommand.Execute("Price");
        Assert.Equal("↑", model.SortIndicators["Price"]);
        model.Destination = 0;
        Assert.Equal("↑", model.SortIndicators["System"]);
    }

    [Fact]
    public async Task PlainBookmarkDoesNotEraseKnownOverlapOrResAnnotations()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var bookmarks = new BookmarksViewModel(directory);
            bookmarks.AddMiningLocation(new GalacticBookmark { System = "Review Test", Body = "Review Test A Ring" });
            var ring = new MiningRing
            {
                System = "Review Test",
                Body = "Review Test A Ring",
                Position = new GalacticCoordinate(0, 0, 0),
                Hotspots = new() { ["Platinum"] = 2 },
                Overlaps = "Platinum x2",
                ResourceExtractionSites = "High",
            };
            using var model = new MiningSearchViewModel(
                new MiningSearchClient(),
                bookmarks,
                _ => { },
                () => [ring],
                new Resolver()
            )
            {
                Source = "Local",
                Reference = ring.System,
                Radius = 1,
                OnlyOverlaps = true,
                OnlyRes = true,
            };
            await model.SearchRingsAsync();
            Assert.Contains(
                model.Rings,
                r => r.System == ring.System && r.Overlaps == ring.Overlaps && r.ResourceExtractionSites == "High"
            );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void SearchPreferencesRoundTripAndResetForAnotherCommander()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );
        model.Reference = "Sol";
        model.Radius = 240;
        model.OnlyRes = true;
        model.TraderType = "Encoded";
        var restored = MiningStore.Parse(
            MiningStore.Export(
                new MiningCommanderData { Settings = new MiningPreferences { SearchOptions = model.SaveOptions() } }
            )
        );
        model.LoadOptions(new());
        Assert.Equal("", model.Reference);
        Assert.Equal(100, model.Radius);
        Assert.False(model.OnlyRes);
        model.LoadOptions(restored.Settings.SearchOptions);
        Assert.Equal("Sol", model.Reference);
        Assert.Equal(240, model.Radius);
        Assert.True(model.OnlyRes);
        Assert.Equal("Encoded", model.TraderType);
    }

    [Fact]
    public async Task OversizedTraderResponseShowsInlineFailureAndAllowsRetry()
    {
        using var http = new HttpClient(new OversizedTraderHandler());
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
        };
        var error = await Record.ExceptionAsync(() => model.SearchTradersAsync());
        Assert.Null(error);
        Assert.False(model.IsBusy);
        Assert.Contains("Search unavailable", model.Status);
        Assert.Empty(model.Markets);
        await model.SearchTradersAsync();
        Assert.Contains("material traders", model.Status);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task SelectedSystemAndRingCarryLocationIntoSellingWorkflow()
    {
        using var handler = new WorkflowHandler();
        using var http = new HttpClient(handler);
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Sol",
            Mineral = "Platinum",
            Source = "Spansh",
            Destination = 3,
            SelectedSystem = new("Wille", 2, "", "", "", "", "", "", "", 0),
        };
        await model.FindSelectedSystemRingsAsync();
        Assert.Equal(0, model.Destination);
        Assert.True(model.SystemOnly);
        Assert.Equal("Wille", model.Reference);
        model.SelectedRing = Assert.Single(model.Rings);
        await model.FindSellingStationsAsync();
        Assert.Equal(1, model.Destination);
        Assert.False(model.Buying);
        Assert.Equal("Sell mined cargo", model.TradeMode);
        Assert.False(model.SystemOnly);
        Assert.Equal(2, model.Markets.Count);
        await model.SearchTradersAsync();
        Assert.Equal(2, model.Markets.Count); // Trader results cannot replace commodity prices.
        Assert.Single(model.Traders);
    }

    [Fact]
    public async Task PowerplayObjectivesExcludeUnknownOwnershipAndRequirePledge()
    {
        using var handler = new WorkflowHandler();
        using var http = new HttpClient(handler);
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
            Objective = "Reinforce",
        };
        await model.SearchSystemsAsync();
        Assert.Contains("Choose your pledged Power", model.Status);
        model.PledgedPower = "Aisling Duval";
        await model.SearchSystemsAsync();
        Assert.Equal("Own", Assert.Single(model.Systems).System);
        model.Objective = "Undermine";
        await model.SearchSystemsAsync();
        Assert.Equal("Other", Assert.Single(model.Systems).System);
        model.Objective = "Acquire";
        await model.SearchSystemsAsync();
        Assert.Equal("Open", Assert.Single(model.Systems).System);
    }

    [Fact]
    public async Task AcquisitionKeepsSellingDestinationWhenMiningRingIsInAnotherSystem()
    {
        using var http = new HttpClient(new WorkflowHandler());
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Objective = "Acquire",
            SelectedSystem = new("Acquisition target", 1, "", "", "", "", "", "", "Unoccupied", 0),
        };
        model.UseSelectedSystem();
        model.SystemOnly = false;
        model.SelectedRing = new MiningRing { System = "Mining origin", Body = "Mining origin A Ring" };
        await model.FindSellingStationsAsync();
        Assert.Equal("Acquisition target", model.Reference);
        Assert.True(model.SystemOnly);
        Assert.Contains("Mining: Mining origin", model.PlanningContext);
        Assert.Contains("Acquire destination: Acquisition target", model.PlanningContext);
        model.ClearPlan();
        Assert.Empty(model.PlanningContext);
    }

    [Fact]
    public async Task ExpansionUsesLocalObservationsAndIsOnlyAnAcquisitionCandidate()
    {
        var cache = new MiningCommunityCache();
        var now = DateTimeOffset.UtcNow;
        cache.Apply(
            $$$"""{"$schemaRef":"https://eddn.edcd.io/schemas/journal/1","message":{"timestamp":"{{{now:O}}}","event":"Location","StarSystem":"Wille","StarPos":[0,0,0],"ControllingPower":"Aisling Duval","PowerplayState":"Expansion"}}""",
            now
        );
        using var http = new HttpClient(new WorkflowHandler());
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver(),
            cache
        )
        {
            Reference = "Wille",
            PowerState = "Expansion",
            PledgedPower = "Aisling Duval",
            Objective = "Reinforce",
        };
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems);
        model.Objective = "Undermine";
        model.PledgedPower = "Jerome Archer";
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems);
        model.Objective = "Acquire";
        await model.SearchSystemsAsync();
        Assert.Equal("Wille", Assert.Single(model.Systems).System);
        Assert.Contains("Local Powerplay observations", model.Status);
        model.Security = "High";
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems); // The journal cannot certify security.
    }

    private sealed class WorkflowHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var payload = request.RequestUri!.AbsolutePath switch
            {
                "/api/bodies/search" =>
                    """{"results":[{"system_name":"Wille","rings":[{"name":"Wille A Ring","type":"Metallic","signals":[{"name":"Platinum","count":2}]}]}]}""",
                "/api/systems/search" =>
                    """{"results":[{"name":"Own","controlling_power":"Aisling Duval","power_state":"Fortified"},{"name":"Other","controlling_power":"Jerome Archer","power_state":"Exploited"},{"name":"Unknown"},{"name":"Open","power_state":"Unoccupied"}]}""",
                "/api/stations/search" => """{"results":[{"system_name":"Wille","name":"Trader"}]}""",
                _ =>
                    $$"""[{"systemName":"Wille","stationName":"Market","sellPrice":200000,"demand":1000,"updatedAt":"{{DateTimeOffset.UtcNow:O}}"},{"systemName":"Elsewhere","stationName":"Other market","sellPrice":250000,"demand":1000,"updatedAt":"{{DateTimeOffset.UtcNow:O}}"}]""",
            };
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) }
            );
        }
    }

    [Fact]
    public async Task CommanderResetDuringDeferredCancellationCannotStartReplacementSearch()
    {
        using var handler = new DeferredCancellationHandler();
        using var http = new HttpClient(handler);
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
        };
        var first = model.SearchTradersAsync();
        var replacement = model.SearchTradersAsync();
        try
        {
            await handler.CancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.LoadOptions(new() { Reference = "Sol" });
            handler.Release.Set();
            await Task.WhenAll(first, replacement);
            Assert.Equal(1, handler.Calls);
            Assert.Empty(model.Traders);
            Assert.False(model.IsBusy);
        }
        finally
        {
            handler.Release.Set();
        }
    }

    private sealed class DeferredCancellationHandler : HttpMessageHandler
    {
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource CancellationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            if (Calls > 1)
            {
                return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") };
            }

            using var registration = cancellationToken.Register(() =>
            {
                CancellationEntered.TrySetResult();
                Release.Wait(TimeSpan.FromSeconds(10));
            });
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Canceled request must not complete.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release.Set();
                Release.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class OversizedTraderHandler : HttpMessageHandler
    {
        private int calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var content =
                ++calls == 1
                    ? (HttpContent)new ByteArrayContent(new byte[8 * 1024 * 1024 + 1])
                    : new StringContent("{\"results\":[]}");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
