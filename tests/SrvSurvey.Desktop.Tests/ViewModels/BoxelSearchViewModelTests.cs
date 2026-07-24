using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BoxelSearchViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-boxel-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ActivateMergesSourcesAndPersistsLegacySearchState()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var systemDirectory = Path.Combine(
            temporaryDirectory,
            "systems",
            "F123");
        Directory.CreateDirectory(systemDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Praea Euq IL-P c5-0_100.json"),
            """
            {
              "name": "Praea Euq IL-P c5-0",
              "address": 100,
              "starPos": [1, 2, 3],
              "lastVisited": "2026-06-01T00:00:00Z"
            }
            """);
        var resolver = new StubResolver(
        [
            Observation("Praea Euq IL-P c5-0", 100),
            Observation("Praea Euq IL-P c5-1", 101),
            Observation("Praea Euq IL-P c5-2", 102),
        ]);
        var viewModel = CreateViewModel(profileStore, resolver);
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        viewModel.SkipAlreadyVisited = true;

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsActive);
        Assert.Equal("Praea Euq IL-P c5-2", viewModel.NextSystem);
        Assert.Equal(3, viewModel.Systems.Count);
        Assert.True(viewModel.Systems[0].IsComplete);
        Assert.Equal("3", viewModel.ExpectedSystemCount);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.True(saved.Data?.BoxelSearch.Active);
        Assert.Equal('c', saved.Data?.BoxelSearch.LowMassCode);
        Assert.True(saved.Data?.BoxelSearch.SkipAlreadyVisited);
    }

    [Fact]
    public async Task JournalCompletionAndGalaxyMapAutoCopyUseTheNextSystem()
    {
        var copied = new List<string>();
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var viewModel = new BoxelSearchViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
            ]),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            });
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        viewModel.UpdateCurrentSystem(
            "Praea Euq IL-P c5-1",
            new GalacticCoordinate(1, 2, 3));
        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":101,"StarPos":[1,2,3]}"""),
        ]);

        await viewModel.UpdateStatusAsync(new EliteStatus
        {
            GuiFocus = GuiFocus.GalaxyMap,
        });

        Assert.Equal("Praea Euq IL-P c5-0", viewModel.NextSystem);
        Assert.Equal(["Praea Euq IL-P c5-0"], copied);
        Assert.True(viewModel.Systems[1].IsComplete);
    }

    [Fact]
    public async Task EmptyMarkerUsesLegacyStoreAndAdvancesToTheNextChild()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var copied = new List<string>();
        var viewModel = new BoxelSearchViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            });
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        await viewModel.ToggleCurrentEmptyAsync();

        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        Assert.True(await new EmptyBoxelStore(temporaryDirectory).IsEmptyAsync(top));
        Assert.NotEqual(top.Prefix, viewModel.CurrentBoxelName);
        Assert.Single(copied);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.NotEqual(top.Prefix, saved.Data?.BoxelSearch.Current?.Prefix);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private BoxelSearchViewModel CreateViewModel(
        CommanderProfileStore store,
        IBoxelSystemResolver resolver)
    {
        return new BoxelSearchViewModel(
            store,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            resolver);
    }

    private static BoxelSystemObservation Observation(string name, long address)
    {
        return new BoxelSystemObservation(
            BoxelAddress.Parse(name) with { SystemAddress = address },
            new GalacticCoordinate(address, 0, 0),
            null,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            true);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private sealed class StubResolver(
        IReadOnlyList<BoxelSystemObservation> systems) : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>(
                systems.Where(system => string.Equals(
                        system.Boxel.Prefix,
                        boxel.Prefix,
                        StringComparison.Ordinal))
                    .ToArray());
        }
    }
}
