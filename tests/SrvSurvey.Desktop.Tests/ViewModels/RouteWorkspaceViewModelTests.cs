using System.Windows.Input;
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
        Assert.Equal("Not saved", viewModel.RouteFileName);
        Assert.False(viewModel.HasSavedRoute);
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
    public async Task FleetCarrierWorkspaceAcceptsOnlyFleetCarrierSpanshRoutes()
    {
        var spanshClient = new StubSpanshClient(
        [
            new FollowRouteHop(
                "Colonia",
                7,
                null,
                "Refuel 500 t Tritium",
                false,
                false),
        ]);
        var viewModel = CreateViewModel(
            spanshClient: spanshClient,
            routeKind: FollowRouteKind.FleetCarrier);
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ImportSpanshUrlAsync(
            "https://spansh.co.uk/exact-plotter/results/74FA2952-2048-11F1-8302-B948FF6DF5C1");

        Assert.Equal(0, spanshClient.CallCount);
        Assert.Contains("Fleet Carrier Router", viewModel.StatusMessage);

        await viewModel.ImportSpanshUrlAsync(
            "https://spansh.co.uk/fleet-carrier/results/74FA2952-2048-11F1-8302-B948FF6DF5C1");

        Assert.Equal(1, spanshClient.CallCount);
        Assert.Equal("Colonia", Assert.Single(viewModel.Hops).Hop.Name);
    }

    [Fact]
    public async Task FleetCarrierWorkspaceAdvancesOnlyOnCarrierJump()
    {
        var store = new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                IsActive = true,
                AutoCopy = true,
                LastReachedIndex = 0,
                Hops =
                [
                    Hop("Sol", 1, new GalacticCoordinate(0, 0, 0)),
                    Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
                ],
            },
            "Carrier Test");
        var viewModel = CreateViewModel(
            routeKind: FollowRouteKind.FleetCarrier);
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """
                {"event":"FSDJump","StarSystem":"Second","SystemAddress":2}
                """),
        ]);

        Assert.Equal(1, viewModel.ReachedCount);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """
                {"event":"CarrierJump","StarSystem":"Second","SystemAddress":2}
                """),
        ]);

        Assert.Equal(2, viewModel.ReachedCount);
        Assert.True(viewModel.IsComplete);
        var saved = await store.LoadAsync("F123");
        Assert.Equal(1, saved.Route!.LastReachedIndex);
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
    public async Task GalaxyMapEntryAutoCopiesRouteBeforeOtherNavigationTools()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var copied = new List<string>();
        var viewModel = CreateViewModel();
        viewModel.SetClipboardWriter(text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });
        await viewModel.UpdateContextAsync(
            "F123",
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0));

        Assert.True(viewModel.ShouldAutoCopyNextHop);

        await viewModel.UpdateStatusAsync(new EliteStatus
        {
            GuiFocus = GuiFocus.GalaxyMap,
            Destination = new StatusDestination
            {
                System = 2,
                Name = "Second",
            },
        });
        await viewModel.UpdateStatusAsync(new EliteStatus
        {
            GuiFocus = GuiFocus.GalaxyMap,
            Destination = new StatusDestination
            {
                System = 2,
                Name = "Second",
            },
        });

        Assert.Equal(["Second"], copied);
        Assert.True(viewModel.ShouldShowGalaxyMapOverlay);
        Assert.Equal("5.00 ly from Sol", viewModel.NextHopDistance);
        Assert.Equal("SELECTED IN GALAXY MAP", viewModel.NextHopDestinationStatus);
        Assert.Equal("NEXT SYSTEM COPIED", viewModel.NextHopClipboardStatus);

        await viewModel.UpdateStatusAsync(new EliteStatus());
        Assert.False(viewModel.ShouldShowGalaxyMapOverlay);
        await viewModel.UpdateStatusAsync(new EliteStatus
        {
            GuiFocus = GuiFocus.GalaxyMap,
        });

        Assert.Equal(["Second", "Second"], copied);
    }

    [Fact]
    public async Task PausedOrManualRouteDoesNotAutoCopyInGalaxyMap()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var copied = new List<string>();
        var viewModel = CreateViewModel();
        viewModel.SetClipboardWriter(text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);
        viewModel.AutoCopy = false;

        await viewModel.UpdateStatusAsync(new EliteStatus
        {
            GuiFocus = GuiFocus.GalaxyMap,
        });

        Assert.False(viewModel.ShouldAutoCopyNextHop);
        Assert.Empty(copied);
    }

    [Fact]
    public async Task GalaxyMapClipboardFailureIsReportedWithoutEscaping()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        viewModel.SetClipboardWriter(_ =>
            throw new Exception("clipboard locked"));
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.UpdateStatusAsync(new EliteStatus
        {
            GuiFocus = GuiFocus.GalaxyMap,
        });

        Assert.Contains("clipboard locked", viewModel.StatusMessage);
        Assert.Equal("AUTO-COPY READY", viewModel.NextHopClipboardStatus);
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

    [Fact]
    public async Task SaveAsCreatesNamedRouteAndUndoRestoresItsDefinition()
    {
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);
        await viewModel.ImportNamesAsync(["Sol", "Achenar"]);

        viewModel.SaveAsCommand.Execute(null);
        Assert.True(viewModel.IsSaveAsVisible);
        viewModel.SaveAsName = "Bubble Tour";
        await viewModel.ConfirmSaveAsAsync();

        Assert.True(viewModel.HasSavedRoute);
        Assert.Equal("Bubble Tour", viewModel.RouteName);
        Assert.Equal("Bubble Tour.json", viewModel.RouteFileName);
        Assert.Single(viewModel.SavedRoutes);
        Assert.True(File.Exists(Path.Combine(
            temporaryDirectory,
            "Routes",
            "F123",
            "Bubble Tour.json")));

        await viewModel.ImportNamesAsync(["Beagle Point"]);
        Assert.True(viewModel.HasDefinitionChanges);
        Assert.False(viewModel.CanSaveChanges);
        await viewModel.DiscardAsync();

        Assert.Equal(["Sol", "Achenar"], viewModel.Hops.Select(hop => hop.Name));
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task NotesSaveImmediatelyButResetWaitsForSaveChanges()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        viewModel.NotesCommand.Execute(null);
        viewModel.NotesDraft = "Refuel before the neutron section.";
        await viewModel.SaveNotesAsync();
        await viewModel.ResetAsync();

        var store = new FollowRouteStore(temporaryDirectory);
        var beforeProgressSave = await store.LoadAsync("F123");
        Assert.Equal("Refuel before the neutron section.", beforeProgressSave.Route!.Notes);
        Assert.Equal(0, beforeProgressSave.Route.LastReachedIndex);
        Assert.True(viewModel.CanSaveChanges);

        await viewModel.SaveAsync();
        var afterProgressSave = await store.LoadAsync("F123");
        Assert.Equal(-1, afterProgressSave.Route!.LastReachedIndex);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task NewKeepsSavedRouteWhileDeleteMovesItOutOfLibrary()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var saved = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops = [Hop("Sol", 1, new GalacticCoordinate(0, 0, 0))],
            },
            "Keep Until Deleted");
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        viewModel.NewCommand.Execute(null);
        Assert.True(viewModel.IsNewConfirmationVisible);
        await viewModel.ConfirmNewAsync();

        Assert.False(viewModel.IsDialogVisible);
        Assert.False(viewModel.HasSavedRoute);
        Assert.True(File.Exists(saved.FilePath));
        Assert.Single(viewModel.SavedRoutes);

        viewModel.SelectedSavedRoute = Assert.Single(viewModel.SavedRoutes);
        await viewModel.LoadSelectedRouteAsync();
        viewModel.DeleteCommand.Execute(null);
        Assert.True(viewModel.IsDeleteConfirmationVisible);
        await viewModel.ConfirmDeleteAsync();

        Assert.False(viewModel.IsDialogVisible);
        Assert.False(viewModel.HasSavedRoute);
        Assert.Empty(viewModel.SavedRoutes);
        Assert.False(File.Exists(saved.FilePath));
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(temporaryDirectory, "Routes", "F123", ".trash"),
            "*.json"));
    }

    [Fact]
    public async Task ClosingWorkspaceDismissesPendingConfirmationState()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        viewModel.DeleteCommand.Execute(null);
        Assert.True(viewModel.IsDeleteConfirmationVisible);

        viewModel.DismissDialogs();

        Assert.False(viewModel.IsDialogVisible);
        Assert.False(viewModel.IsDeleteConfirmationVisible);
    }

    [Fact]
    public async Task RouteRowsStayStableAcrossContextAndProgressUpdates()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync(
            "F123",
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0));
        var rows = viewModel.Hops;
        var firstRow = rows[0];
        var secondRow = rows[1];

        var reinitialized = await viewModel.UpdateContextAsync(
            "F123",
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0));
        viewModel.SetProgressThrough(1, reached: true);

        Assert.False(reinitialized);
        Assert.Same(rows, viewModel.Hops);
        Assert.Same(firstRow, viewModel.Hops[0]);
        Assert.Same(secondRow, viewModel.Hops[1]);
        Assert.True(secondRow.IsReached);
        Assert.Equal("VISITED", secondRow.State);
    }

    [Fact]
    public async Task EveryWorkspaceDialogCanBeDismissedWithoutChangingRoute()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);
        var dialogs = new (
            ICommand Command,
            Func<bool> IsVisible,
            string VisibilityProperty)[]
        {
            (
                viewModel.NewCommand,
                () => viewModel.IsNewConfirmationVisible,
                nameof(RouteWorkspaceViewModel.IsNewConfirmationVisible)),
            (
                viewModel.SaveAsCommand,
                () => viewModel.IsSaveAsVisible,
                nameof(RouteWorkspaceViewModel.IsSaveAsVisible)),
            (
                viewModel.NotesCommand,
                () => viewModel.IsNotesVisible,
                nameof(RouteWorkspaceViewModel.IsNotesVisible)),
            (
                viewModel.DeleteCommand,
                () => viewModel.IsDeleteConfirmationVisible,
                nameof(RouteWorkspaceViewModel.IsDeleteConfirmationVisible)),
        };
        foreach (var dialog in dialogs)
        {
            notifications.Clear();
            dialog.Command.Execute(null);
            Assert.True(dialog.IsVisible());
            Assert.True(viewModel.IsDialogVisible);
            Assert.Contains(dialog.VisibilityProperty, notifications);
            Assert.Contains(
                nameof(RouteWorkspaceViewModel.IsDialogVisible),
                notifications);

            notifications.Clear();
            viewModel.DismissDialogs();

            Assert.False(viewModel.IsDialogVisible);
            Assert.False(dialog.IsVisible());
            Assert.Contains(dialog.VisibilityProperty, notifications);
            Assert.Contains(
                nameof(RouteWorkspaceViewModel.IsDialogVisible),
                notifications);
        }

        Assert.True(viewModel.HasSavedRoute);
        Assert.Equal(3, viewModel.RouteCount);
    }

    [Fact]
    public async Task BioCompletionPersistsAndKeepsSharedRowsStable()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        await store.SaveAsync(new FollowRouteDocument(
            "F123",
            store.GetPath("F123"),
            true,
            true,
            0,
            [
                new FollowRouteHop(
                    "Sol",
                    1,
                    new GalacticCoordinate(0, 0, 0),
                    null,
                    false,
                    false,
                    [
                        new FollowRouteBioTarget(
                            "A 1",
                            10,
                            ["Bacterium Acies", "Stratum Tectonicas"],
                            Subtype: "Rocky body",
                            DistanceToArrivalLs: 1500,
                            EstimatedScanValue: 500,
                            EstimatedMappingValue: 2221,
                            EstimatedBiologyValue: 27428800,
                            IsTerraformable: true,
                            IsBiological: true),
                    ]),
                Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
            ]));
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync(
            "F123",
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0));
        var rows = viewModel.Hops;
        var hop = Assert.Single(rows, candidate => candidate.IsCurrent);
        var target = Assert.Single(hop.BioTargets);
        var notifications = new List<string?>();
        target.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        await viewModel.SetBioTargetCompletedAsync(target, true);

        Assert.Same(rows, viewModel.Hops);
        Assert.Same(hop, viewModel.CurrentBioHop);
        Assert.Same(target, Assert.Single(viewModel.CurrentBioTargets));
        Assert.True(target.IsCompleted);
        Assert.Equal("Rocky body", target.Subtype);
        Assert.EndsWith(
            "/Assets/Bodies/rocky-body.png",
            target.BodyIconAssetPath,
            StringComparison.Ordinal);
        Assert.Equal("1,500 LS", target.DistanceToArrival);
        Assert.Equal("500 CR", target.EstimatedScanValue);
        Assert.Equal("2,221 CR", target.EstimatedMappingValue);
        Assert.Equal("27,428,800 CR", target.EstimatedBiologyValue);
        Assert.True(target.IsTerraformable);
        Assert.True(target.HasDetails);
        Assert.True(viewModel.ShouldShowRouteBioOverlay);
        Assert.Contains(nameof(RouteBioTargetItemViewModel.IsCompleted), notifications);
        var reloaded = await store.LoadAsync("F123");
        Assert.True(reloaded.Route!.Hops[0].BioTargets[0].IsCompleted);

        notifications.Clear();
        await viewModel.SetBioTargetCompletedAsync(target, true);

        Assert.Empty(notifications);
        Assert.Same(target, Assert.Single(viewModel.CurrentBioTargets));
    }

    [Fact]
    public void BodyArtworkChangesOnlyWhenTheBodySubtypeChanges()
    {
        var source = new FollowRouteBioTarget(
            "A 1",
            10,
            [],
            Subtype: "Rocky body");
        var target = new RouteBioTargetItemViewModel(0, 0, source);
        var notifications = new List<string?>();
        target.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        target.Update(source);

        Assert.Empty(notifications);
        Assert.EndsWith(
            "/Assets/Bodies/rocky-body.png",
            target.BodyIconAssetPath,
            StringComparison.Ordinal);

        target.Update(source with { Subtype = "Water world" });

        Assert.EndsWith(
            "/Assets/Bodies/water-world.png",
            target.BodyIconAssetPath,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(RouteBioTargetItemViewModel.BodyIconAssetPath),
            notifications);
        Assert.Contains(
            nameof(RouteBioTargetItemViewModel.BodyIconAccessibleName),
            notifications);

        notifications.Clear();
        target.Update(source with { Subtype = "WATER-WORLD" });

        Assert.DoesNotContain(
            nameof(RouteBioTargetItemViewModel.BodyIconAssetPath),
            notifications);
        Assert.DoesNotContain(
            nameof(RouteBioTargetItemViewModel.BodyIconAccessibleName),
            notifications);
    }

    private RouteWorkspaceViewModel CreateViewModel(
        IStarSystemResolver? resolver = null,
        ISpanshRouteClient? spanshClient = null,
        FollowRouteKind routeKind = FollowRouteKind.Standard)
    {
        var store = new FollowRouteStore(temporaryDirectory, routeKind);
        return new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(
                resolver
                    ?? new StubResolver(
                        new Dictionary<string, StarSystemReference>())),
            spanshClient ?? new StubSpanshClient([]),
            routeKind);
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
