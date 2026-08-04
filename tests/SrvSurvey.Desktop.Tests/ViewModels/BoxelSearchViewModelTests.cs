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

        viewModel.UpdateCurrentSystem(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3));
        var rows = viewModel.Systems;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        viewModel.UpdateCurrentSystem(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3));

        Assert.Same(rows, viewModel.Systems);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task ActivateResolvesImportedHandAuthoredSystemName()
    {
        var published = Path.Combine(temporaryDirectory, "pub");
        Directory.CreateDirectory(published);
        await File.WriteAllTextAsync(
            Path.Combine(
                published,
                KnownSystemAddressCatalog.LegacyFileName),
            "known_systems = {\n  \"sol\": 10477373803,\n}\n"
                + "known_missing = [\n]\n");
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var viewModel = new BoxelSearchViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            knownSystems: KnownSystemAddressCatalog.Load(temporaryDirectory));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Sol";
        viewModel.LowMassCode = "c";

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsActive);
        Assert.DoesNotContain("valid generated", viewModel.StatusMessage);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.Equal("Sol", saved.Data?.BoxelSearch.TopBoxel?.Name);
        Assert.Equal(
            10477373803,
            saved.Data?.BoxelSearch.TopBoxel?.SystemAddress);
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
            Flags = StatusFlags.InMainShip,
            GuiFocus = GuiFocus.NoFocus,
        }, nextMusicTrack: "GalaxyMap");

        Assert.Equal("Praea Euq IL-P c5-0", viewModel.NextSystem);
        Assert.Equal(["Praea Euq IL-P c5-0"], copied);
        Assert.True(viewModel.Systems[1].IsComplete);
        Assert.True(viewModel.ShouldShowGalaxyMapOverlay);
    }

    [Fact]
    public async Task RoutePrioritySuppressesBoxelCopyForTheSameMapEntry()
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
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3));

        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap },
            allowAutoCopy: false);
        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });

        Assert.Empty(copied);

        await viewModel.UpdateStatusAsync(new EliteStatus());
        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });

        Assert.Single(copied);
    }

    [Fact]
    public async Task GalaxyMapOverlayValidatesFinalRouteDestination()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
            ]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        await viewModel.UpdateRouteAsync(new NavRouteSnapshot(
            DateTimeOffset.Parse("2026-07-25T01:00:00Z"),
            "NavRoute",
        [
            new NavRouteEntry("Praea Euq IL-P c5-0", 0, null, "K"),
            new NavRouteEntry("Praea Euq IL-P c5-1", 0, null, "K"),
        ]));

        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap },
            allowAutoCopy: false);

        Assert.True(viewModel.ShouldShowGalaxyMapOverlay);
        Assert.True(viewModel.IsDestinationValid);
        Assert.Contains("destination is valid", viewModel.DestinationStatus);

        await viewModel.UpdateRouteAsync(new NavRouteSnapshot(
            DateTimeOffset.Parse("2026-07-25T01:01:00Z"),
            "NavRoute",
        [
            new NavRouteEntry("Praea Euq IL-P c5-0", 0, null, "K"),
            new NavRouteEntry("Synuefe XE-Y c17-0", 0, null, "K"),
        ]));

        Assert.False(viewModel.IsDestinationValid);
        Assert.Contains("outside", viewModel.DestinationStatus);

        await viewModel.UpdateStatusAsync(new EliteStatus());
        Assert.False(viewModel.ShouldShowGalaxyMapOverlay);
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

    [Fact]
    public async Task FullAreaAuditRefreshesEveryChildAndPersistsProgress()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        var observations = new[] { top }
            .Concat(top.Children)
            .Select((boxel, index) => new BoxelSystemObservation(
                boxel.WithSystemNumber(0) with { SystemAddress = 100 + index },
                new GalacticCoordinate(index, 0, 0),
                null,
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                true))
            .ToArray();
        var viewModel = CreateViewModel(
            profileStore,
            new StubResolver(observations));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = top.Name;
        viewModel.LowMassCode = "c";
        viewModel.StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        viewModel.SkipKnownToSpansh = true;
        await viewModel.ActivateAsync();

        await viewModel.AuditAllAsync();

        Assert.False(viewModel.IsAuditing);
        Assert.Equal(9, viewModel.AuditProcessed);
        Assert.Equal(9, viewModel.AuditTotal);
        Assert.Equal("9 of 9 boxels complete", viewModel.BoxelProgress);
        Assert.Contains("Audited all 9 boxels", viewModel.AuditProgress);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.Equal(9, saved.Data?.BoxelSearch.CompletedPrefixes.Count);
    }

    [Fact]
    public async Task LargeAuditRequiresExplicitConfirmation()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0")
            .Parent
            .Parent
            .Parent;
        var viewModel = CreateViewModel(profileStore, new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = top.Name;
        viewModel.LowMassCode = "a";
        await viewModel.ActivateAsync();

        Assert.True(viewModel.ShowLargeAuditConfirmation);
        Assert.False(viewModel.AuditAllCommand.CanExecute(null));

        viewModel.ConfirmLargeAudit = true;

        Assert.True(viewModel.AuditAllCommand.CanExecute(null));
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
