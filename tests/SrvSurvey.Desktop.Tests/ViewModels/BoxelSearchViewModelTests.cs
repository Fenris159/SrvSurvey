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
    public async Task EmptyProfileLeavesAutoCopyUnselected()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));

        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);

        Assert.False(viewModel.AutoCopy);
    }

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
        Assert.Equal("Praea Euq IL-P c5-1", viewModel.NextSystem);
        Assert.Equal(3, viewModel.Systems.Count);
        Assert.True(viewModel.Systems[0].IsComplete);
        Assert.Equal("3", viewModel.ExpectedSystemCount);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.True(saved.Data?.BoxelSearch.Active);
        Assert.Equal('c', saved.Data?.BoxelSearch.LowMassCode);
        Assert.True(saved.Data?.BoxelSearch.SkipAlreadyVisited);

        viewModel.UpdateCurrentSystem(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3),
            100);
        Assert.Equal("id64 100", viewModel.CurrentSystemAddressText);
        var rows = viewModel.Systems;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        viewModel.UpdateCurrentSystem(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3),
            100);

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
    public async Task SuggestedSystemSelectionUsesProviderId64ForActivation()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var suggestionClient = new StubSuggestionClient(
        [
            new SystemNameSuggestion("Sol", 10477373803, "EDSM"),
            new SystemNameSuggestion("Solati", 1458376315610, "EDSM"),
        ]);
        var viewModel = new BoxelSearchViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            systemNameSuggestionClient: suggestionClient,
            systemSuggestionDelay: TimeSpan.Zero);
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);

        viewModel.TopBoxelText = "Sol";
        await WaitUntilAsync(() => !viewModel.IsSearchingSystemSuggestions);

        Assert.Equal(2, viewModel.SystemNameSuggestions.Count);
        Assert.Equal(0, viewModel.SelectedSystemSuggestionIndex);
        Assert.Equal("2 system suggestions from EDSM.", viewModel.SystemSuggestionStatus);
        viewModel.MoveSystemSuggestionSelection(1);
        Assert.Equal(1, viewModel.SelectedSystemSuggestionIndex);
        viewModel.MoveSystemSuggestionSelection(-1);
        Assert.True(viewModel.SelectCurrentSystemSuggestion());
        Assert.Equal("Sol", viewModel.TopBoxelText);
        Assert.False(viewModel.HasSystemNameSuggestions);

        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsActive);
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
        viewModel.AutoCopy = true;
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
        viewModel.AutoCopy = true;
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
        viewModel.AutoCopy = true;
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
    public async Task HierarchyNavigationShowsBreadcrumbsNamedNeighborsAndChildren()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "b";
        await viewModel.ActivateAsync();

        var root = Assert.Single(viewModel.BreadcrumbBoxels);
        var rootChildren = viewModel.ChildBoxels.ToArray();
        Assert.Same(root, viewModel.CurrentHierarchyBoxel);
        Assert.Null(viewModel.ParentBoxel);
        Assert.Null(viewModel.PreviousSiblingBoxel);
        Assert.Null(viewModel.NextSiblingBoxel);
        Assert.Equal("Search root", viewModel.SiblingPosition);
        Assert.Equal(8, rootChildren.Length);
        Assert.All(rootChildren, child =>
        {
            Assert.Equal("Not searched", child.ProgressLabel);
            Assert.Equal("NOT STARTED", child.StatusLabel);
        });

        await rootChildren[2].NavigateAsync();

        Assert.Equal(2, viewModel.BreadcrumbBoxels.Count);
        Assert.Same(root, viewModel.BreadcrumbBoxels[0]);
        Assert.Same(rootChildren[2], viewModel.CurrentHierarchyBoxel);
        Assert.Same(root, viewModel.ParentBoxel);
        Assert.Equal("3 of 8 at this level", viewModel.SiblingPosition);
        Assert.Equal(rootChildren[1].Label, viewModel.PreviousSiblingBoxel?.Label);
        Assert.Equal(rootChildren[3].Label, viewModel.NextSiblingBoxel?.Label);
        Assert.Equal(8, viewModel.ChildBoxels.Count);

        await Assert.IsType<BoxelNavigationOptionViewModel>(
            viewModel.NextSiblingBoxel).NavigateAsync();

        Assert.Equal(rootChildren[3].Label, viewModel.CurrentHierarchyBoxel?.Label);
        Assert.Equal("4 of 8 at this level", viewModel.SiblingPosition);

        viewModel.SetProfileError("Profile unavailable.");
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "b";
        await viewModel.ActivateAsync();

        Assert.NotSame(root, viewModel.CurrentHierarchyBoxel);
    }

    [Fact]
    public async Task HierarchyRowsKeepTheirIdentityDuringProgressUpdates()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        var breadcrumb = viewModel.BreadcrumbBoxels;
        var children = viewModel.ChildBoxels;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq RS-U d2-0","SystemAddress":100,"StarPos":[1,2,3]}"""),
        ]);

        Assert.Same(breadcrumb, viewModel.BreadcrumbBoxels);
        Assert.Same(children, viewModel.ChildBoxels);
        Assert.All(children, child => Assert.Contains(child, viewModel.ChildBoxels));
        Assert.DoesNotContain(nameof(viewModel.BreadcrumbBoxels), notifications);
        Assert.DoesNotContain(nameof(viewModel.ChildBoxels), notifications);
        Assert.Equal("1 of 1 systems complete", viewModel.CurrentHierarchyBoxel?.ProgressLabel);
        Assert.Equal("COMPLETE", viewModel.CurrentHierarchyBoxel?.StatusLabel);
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

    [Fact]
    public async Task NamedProgressSaveLinksAutomaticUpdatesAndReopensDialogIfRemoved()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var savedStore = new SavedBoxelSearchStore(temporaryDirectory);
        var viewModel = new BoxelSearchViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
            ]),
            savedSearchStore: savedStore);
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        Assert.Equal(
            SaveBoxelProgressResult.RequiresDetails,
            await viewModel.SaveProgressAsync());
        Assert.Equal(
            SaveBoxelProgressResult.Saved,
            await viewModel.SaveProgressAsync("Return later", "Test notes"));
        var entry = Assert.Single(await savedStore.ListAsync("F123"));

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":101,"StarPos":[1,2,3]}"""),
        ]);

        entry = Assert.Single(await savedStore.ListAsync("F123"));
        Assert.Equal(1, entry.CompletedSystems);
        Assert.Equal("Test notes", entry.Notes);
        Assert.Equal(
            SaveBoxelProgressResult.Saved,
            await viewModel.SaveProgressAsync());
        Assert.Single(await savedStore.ListAsync("F123"));

        File.Delete(entry.FilePath);

        Assert.Equal(
            SaveBoxelProgressResult.RequiresDetails,
            await viewModel.SaveProgressAsync());
        var active = await profileStore.LoadAsync("F123", true);
        Assert.Null(active.Data?.BoxelSearch.SavedSearchFileName);
    }

    [Fact]
    public async Task RestartRestoresIndividualCompletedSystems()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var resolver = new StubResolver(
        [
            Observation("Praea Euq IL-P c5-0", 100),
            Observation("Praea Euq IL-P c5-1", 101),
        ]);
        var first = CreateViewModel(profileStore, resolver);
        await first.LoadProfileAsync("F123", "Drew", true, BoxelSearchSnapshot.Empty);
        first.TopBoxelText = "Praea Euq IL-P c5-0";
        first.LowMassCode = "c";
        await first.ActivateAsync();
        await first.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":101,"StarPos":[1,2,3]}"""),
        ]);
        var saved = await profileStore.LoadAsync("F123", true);
        var suggestions = new CountingSuggestionClient();
        var restarted = CreateViewModel(profileStore, resolver, suggestions);

        await restarted.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            Assert.IsType<BoxelSearchSnapshot>(saved.Data?.BoxelSearch));

        Assert.Equal("Praea Euq IL-P c5-0", restarted.TopBoxelText);
        Assert.False(restarted.HasSystemNameSuggestions);
        Assert.Equal(string.Empty, restarted.SystemSuggestionStatus);
        Assert.Equal(0, suggestions.CallCount);
        Assert.False(restarted.Systems[0].IsComplete);
        Assert.True(restarted.Systems[1].IsComplete);
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
        IBoxelSystemResolver resolver,
        ISystemNameSuggestionClient? suggestionClient = null)
    {
        return new BoxelSearchViewModel(
            store,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            resolver,
            systemNameSuggestionClient: suggestionClient,
            systemSuggestionDelay: TimeSpan.Zero);
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

    private sealed class CountingSuggestionClient : ISystemNameSuggestionClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<SystemNameSuggestion>>([]);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the asynchronous suggestion request.");
    }

    private sealed class StubSuggestionClient(
        IReadOnlyList<SystemNameSuggestion> suggestions)
        : ISystemNameSuggestionClient
    {
        public Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(suggestions);
        }
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
