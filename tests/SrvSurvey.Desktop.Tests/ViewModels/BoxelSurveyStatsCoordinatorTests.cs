using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.JournalEventEnvelopeTestParser;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BoxelSurveyStatsCoordinatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelSurveyStatsCoordinatorTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CommanderSwitchIsolatesWritesAndReloadsBodies()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        using var coordinator = new BoxelSurveyStatsCoordinator(
            store,
            flushDelay: TimeSpan.FromHours(1));
        await coordinator.SwitchCommanderAsync("F-A");
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""),
        ]);
        await coordinator.IngestSnapshotAsync(
            Snapshot(
                "Praea Euq IL-P c5-0",
                2001,
                Enumerable.Range(1, 5)
                    .Select(id => Planet(id, "Icy body", 100 * id, 200 * id))
                    .ToArray()));
        await coordinator.FlushAsync();

        var commanderA = Path.Combine(
            temporaryDirectory,
            BoxelSurveyStatsStore.StoreDirectoryName,
            "F-A");
        var filesA = Directory.GetFiles(commanderA, "*.json");
        Assert.Contains(filesA, path => Path.GetFileName(path) == "index.json");

        await coordinator.SwitchCommanderAsync("F-B");
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T12:20:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""),
        ]);
        await coordinator.IngestSnapshotAsync(
            Snapshot("Praea Euq IL-P c5-0", 2001, [Planet(1, "Rocky body", 10, 20)]));
        await coordinator.FlushAsync();

        var reloadedA = await store.LoadBoxelAsync("F-A", "Praea Euq IL-P c5-");
        Assert.NotNull(reloadedA);
        Assert.Equal(5, Assert.Single(reloadedA.Systems).Bodies.Count);
        var indexB = Assert.Single(await store.ListIndexAsync("F-B"));
        Assert.Equal("Praea Euq IL-P c5-", indexB.Prefix);
        Assert.Equal(1, indexB.VisitedSystemCount);

        await coordinator.SwitchCommanderAsync("F-A");
        var restored = await coordinator.GetAsync("Praea Euq IL-P c5-");
        Assert.NotNull(restored);
        Assert.Equal(5, restored.CountsOf(BoxelPlanetClass.Icy).Count);
        await coordinator.IngestSnapshotAsync(
            Snapshot("Praea Euq IL-P c5-0", 2001, []));
        restored = await coordinator.GetAsync("Praea Euq IL-P c5-");
        Assert.Equal(5, restored!.CountsOf(BoxelPlanetClass.Icy).Count);
        Assert.Equal(1500, restored.CurrentValue);
    }

    [Fact]
    public async Task BootstrapAppliesOnlyFileheaderAndLoadGame()
    {
        using var coordinator = new BoxelSurveyStatsCoordinator(
            new BoxelSurveyStatsStore(temporaryDirectory));
        await coordinator.SwitchCommanderAsync("F123");
        await coordinator.ApplyBootstrapContextAsync(
        [
            Parse("""{"event":"Fileheader","Odyssey":false}"""),
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""),
        ]);

        Assert.Equal(0, coordinator.State.BoxelCount);
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""),
            Parse("""{"event":"NavBeaconScan","SystemAddress":2001}"""),
        ]);
        Assert.True(coordinator.State.TryGet("Praea Euq IL-P c5-", out var snapshot));
        Assert.Equal(1, snapshot.Visited);
        Assert.Equal(1, snapshot.NavBeaconCount);
        Assert.Equal(0, snapshot.FssCompleteCount);
    }

    [Fact]
    public async Task SearchViewModelReceivesTheSameCoordinator()
    {
        using var coordinator = new BoxelSurveyStatsCoordinator(
            new BoxelSurveyStatsStore(temporaryDirectory));
        var viewModel = new BoxelSearchViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new NullResolver(),
            surveyStats: coordinator);
        Assert.Same(coordinator, viewModel.SurveyStats);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static SystemScanSnapshot Snapshot(
        string name,
        long address,
        IReadOnlyList<SystemScanBodySnapshot> bodies)
    {
        return new SystemScanSnapshot(
            name,
            address,
            null,
            0,
            0,
            false,
            false,
            bodies.Count,
            bodies.Count,
            0,
            bodies.Sum(body => (long)body.CurrentScanValue),
            0,
            0,
            null,
            null,
            bodies);
    }

    private static SystemScanBodySnapshot Planet(
        int bodyId,
        string planetClass,
        int currentValue,
        int mappedValue)
    {
        return new SystemScanBodySnapshot(
            bodyId,
            $"Body {bodyId}",
            $"{bodyId}",
            SystemBodyKind.Planet,
            null,
            planetClass,
            false,
            false,
            true,
            false,
            false,
            false,
            null,
            false,
            false,
            null,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            "None",
            null,
            0,
            0,
            0,
            0,
            currentValue,
            mappedValue,
            currentValue,
            0,
            new Dictionary<string, double>(),
            new Dictionary<string, double>(),
            [],
            [],
            [],
            []);
    }

    private sealed class NullResolver : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>([]);
        }
    }
}
