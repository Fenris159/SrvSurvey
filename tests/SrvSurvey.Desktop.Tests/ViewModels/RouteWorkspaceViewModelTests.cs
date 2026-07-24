using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RouteWorkspaceViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-view-model-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingRouteLoadsAsAnEmptyCommanderWorkspace()
    {
        var viewModel = CreateViewModel();

        var initialized = await viewModel.UpdateContextAsync(
            "F123",
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0));

        Assert.True(initialized);
        Assert.True(viewModel.HasProfile);
        Assert.False(viewModel.HasRoute);
        Assert.Equal("No route loaded", viewModel.NextHopName);
        Assert.Equal("F123.json", viewModel.RouteFileName);
        Assert.False(viewModel.IsDirty);
        Assert.Contains("No followed route", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LegacyRouteDisplaysSegmentsAndSavesProgressAndPreferences()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync(
            "F123",
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0));

        Assert.Equal(3, viewModel.RouteCount);
        Assert.Equal("Second", viewModel.NextHopName);
        Assert.Equal("0.00 ly", viewModel.Hops[0].Distance);
        Assert.Equal("5.00 ly", viewModel.Hops[1].Distance);
        Assert.Equal("12.00 ly", viewModel.Hops[2].Distance);
        Assert.Equal("CURRENT", viewModel.Hops[0].State);
        Assert.Equal("NEXT", viewModel.Hops[1].State);

        viewModel.AutoCopy = false;
        viewModel.SetProgressThrough(2, true);

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.IsComplete);
        Assert.False(viewModel.IsActive);
        await viewModel.SaveAsync();

        Assert.False(viewModel.IsDirty);
        var saved = await new FollowRouteStore(temporaryDirectory)
            .LoadAsync("F123");
        Assert.Equal(2, saved.Route!.LastReachedIndex);
        Assert.False(saved.Route.IsActive);
        Assert.False(saved.Route.AutoCopy);
    }

    [Fact]
    public async Task NameImportKeepsUnknownSystemsAndChecksCurrentFirstHop()
    {
        var resolver = new StubResolver(new Dictionary<string, StarSystemReference>
        {
            ["Sol"] = new(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0)),
        });
        var viewModel = CreateViewModel(resolver: resolver);
        await viewModel.UpdateContextAsync(
            "F123",
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0));

        await viewModel.ImportNamesAsync([" Sol ", "Unknown"]);

        Assert.True(viewModel.IsDirty);
        Assert.Equal(2, viewModel.RouteCount);
        Assert.Equal(1, viewModel.ReachedCount);
        Assert.True(viewModel.Hops[0].IsReached);
        Assert.Equal("Unknown", viewModel.NextHopName);
        Assert.Null(viewModel.Hops[1].Hop.SystemAddress);
        Assert.Contains("1 resolved", viewModel.StatusMessage);
        Assert.Contains("1 kept by name", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SpanshImportRejectsInvalidClipboardAndLoadsRouteFlags()
    {
        var imported = new[]
        {
            new FollowRouteHop(
                "Jackson's Lighthouse",
                7,
                new GalacticCoordinate(1, 2, 3),
                null,
                true,
                true),
        };
        var spanshClient = new StubSpanshClient(imported);
        var viewModel = CreateViewModel(spanshClient: spanshClient);
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ImportSpanshUrlAsync("not a URL");

        Assert.Equal(0, spanshClient.CallCount);
        Assert.Contains("valid Spansh route", viewModel.StatusMessage);

        await viewModel.ImportSpanshUrlAsync(
            "https://spansh.co.uk/exact-plotter/results/74FA2952-2048-11F1-8302-B948FF6DF5C1");

        Assert.Equal(1, spanshClient.CallCount);
        var hop = Assert.Single(viewModel.Hops);
        Assert.Contains("Refuel", hop.Notes);
        Assert.Contains("Neutron", hop.Notes);
    }

    [Fact]
    public async Task LiveFsdJumpAdvancesExpectedHopAndPersistsIt()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """
                {"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Second","SystemAddress":2,"StarPos":[3,4,0]}
                """),
        ]);

        Assert.Equal(2, viewModel.ReachedCount);
        Assert.Equal("Third", viewModel.NextHopName);
        Assert.Contains("hop #2", viewModel.StatusMessage);
        var saved = await new FollowRouteStore(temporaryDirectory)
            .LoadAsync("F123");
        Assert.Equal(1, saved.Route!.LastReachedIndex);
    }

    [Fact]
    public async Task OutOfOrderArrivalDoesNotChangeRouteButFinalArrivalCompletes()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: -1);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Elsewhere", 99, null);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """
                {"event":"FSDJump","StarSystem":"Second","SystemAddress":2}
                """),
        ]);
        Assert.Equal(0, viewModel.ReachedCount);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """
                {"event":"FSDJump","StarSystem":"Third","SystemAddress":3}
                """),
        ]);

        Assert.True(viewModel.IsComplete);
        Assert.False(viewModel.IsActive);
        Assert.Contains("Route complete", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CopyNextHopUsesDesktopClipboardBoundary()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);
        string? copied = null;
        viewModel.SetClipboardWriter(text =>
        {
            copied = text;
            return Task.CompletedTask;
        });

        await viewModel.CopyNextHopAsync();

        Assert.Equal("Second", copied);
        Assert.Contains("Copied Second", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MalformedRouteIsReportedWithoutCreatingAnEditableDraft()
    {
        var directory = Path.Combine(temporaryDirectory, "routes");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "F123.json"),
            "{\"hops\":");
        var viewModel = CreateViewModel();

        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        Assert.False(viewModel.HasRoute);
        Assert.False(viewModel.IsDirty);
        Assert.Contains("Could not read", viewModel.StatusMessage);
    }

    private RouteWorkspaceViewModel CreateViewModel(
        IStarSystemResolver? resolver = null,
        ISpanshRouteClient? spanshClient = null)
    {
        var store = new FollowRouteStore(temporaryDirectory);
        return new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(
                resolver
                    ?? new StubResolver(
                        new Dictionary<string, StarSystemReference>())),
            spanshClient ?? new StubSpanshClient([]));
    }

    private async Task SaveRouteAsync(bool isActive, int lastReachedIndex)
    {
        var store = new FollowRouteStore(temporaryDirectory);
        await store.SaveAsync(new FollowRouteDocument(
            "F123",
            store.GetPath("F123"),
            isActive,
            true,
            lastReachedIndex,
            [
                Hop("Sol", 1, new GalacticCoordinate(0, 0, 0)),
                Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
                Hop("Third", 3, new GalacticCoordinate(3, 4, 12)),
            ]));
    }

    private static FollowRouteHop Hop(
        string name,
        long address,
        GalacticCoordinate position)
    {
        return new FollowRouteHop(
            name,
            address,
            position,
            null,
            false,
            false);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubResolver(
        IReadOnlyDictionary<string, StarSystemReference> systems)
        : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<StarSystemReference> result = systems.TryGetValue(
                query,
                out var system)
                    ? [system]
                    : [];
            return Task.FromResult(result);
        }
    }

    private sealed class StubSpanshClient(
        IReadOnlyList<FollowRouteHop> hops) : ISpanshRouteClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(hops);
        }
    }
}
