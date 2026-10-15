using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RouteWorkspaceViewModelTests : IAsyncLifetime
{
    private readonly List<RouteWorkspaceViewModel> workspaces = [];
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-view-model-tests-{Guid.NewGuid():N}"
    );

    /// <summary>
    /// Checks that missing route loads as an empty commander workspace.
    /// </summary>
    [Fact]
    public async Task MissingRouteLoadsAsAnEmptyCommanderWorkspace()
    {
        RouteWorkspaceViewModel viewModel = CreateViewModel();

        bool initialized = await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));

        Assert.True(initialized);
        Assert.True(viewModel.HasProfile);
        Assert.False(viewModel.HasRoute);
        Assert.False(viewModel.HasCarrierEstimatedTimeRemaining);
        Assert.Equal("No route loaded", viewModel.NextHopName);
        Assert.Equal("Not saved", viewModel.RouteFileName);
        Assert.False(viewModel.HasSavedRoute);
        Assert.False(viewModel.IsDirty);
        Assert.Contains("No followed route", viewModel.StatusMessage);
    }

    /// <summary>Imported drafts survive workspace reloads and application sessions without becoming saved routes.</summary>
    [Theory]
    [InlineData(FollowRouteKind.Standard, false)]
    [InlineData(FollowRouteKind.FleetCarrier, false)]
    [InlineData(FollowRouteKind.Standard, true)]
    [InlineData(FollowRouteKind.FleetCarrier, true)]
    public async Task UnsavedImportedRouteSurvivesReopeningAndRestarting(FollowRouteKind kind, bool search)
    {
        FollowRouteHop[] hops =
        [
            Hop("Sol", 1, new GalacticCoordinate(0, 0, 0)),
            Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
            Hop("Third", 3, new GalacticCoordinate(3, 4, 12)),
        ];
        RouteWorkspaceViewModel model = CreateViewModel(
            spanshClient: new StubSpanshClient(hops),
            routeKind: kind,
            plannerClient: search ? new StubPlannerClient() : null
        );
        using SpanshSearchViewModel? searchModel = model.SpanshSearch;
        await model.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
        string planner = kind == FollowRouteKind.FleetCarrier ? "fleet-carrier" : "plotter";
        if (search)
        {
            SpanshPlannerDefinition definition =
                kind == FollowRouteKind.FleetCarrier
                    ? SpanshPlannerCatalog.CarrierPlanners[0]
                    : SpanshPlannerCatalog.ShipPlanners[0];
            Assert.True(
                await model.SearchSpanshAsync(new StubPlannerClient(), new(definition, []), CancellationToken.None)
            );
        }
        else
        {
            await model.ImportSpanshUrlAsync(
                $"https://spansh.co.uk/{planner}/results/74FA2952-2048-11F1-8302-B948FF6DF5C1"
            );
        }

        for (int opening = 0; opening < 2; opening++)
        {
            await model.RefreshAsync();
            Assert.Equal(hops, model.CreateSnapshot()!.Hops);
            Assert.True(model.IsActive);
            Assert.Equal("Second", model.NextHopName);
            Assert.False(model.HasSavedRoute);
            Assert.Empty(model.SavedRoutes);
        }

        RouteWorkspaceViewModel restarted = CreateViewModel(routeKind: kind);
        await restarted.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
        Assert.Equal(hops, restarted.CreateSnapshot()!.Hops);
        Assert.True(restarted.IsActive);
        Assert.Equal("Second", restarted.NextHopName);
        Assert.False(restarted.HasSavedRoute);
        Assert.True(await restarted.SetAutoCopyAsync(true));
        await restarted.RefreshAsync();
        Assert.True(restarted.AutoCopy);
        Assert.Empty(restarted.SavedRoutes);
    }

    /// <summary>Unsaved routes retain manual edits, notes, and journal arrivals across commander switches and restarts.</summary>
    [Theory]
    [InlineData(FollowRouteKind.Standard, "FSDJump")]
    [InlineData(FollowRouteKind.FleetCarrier, "CarrierJump")]
    public async Task UnsavedRouteKeepsProgressAndPreferencesAndNewClearsRecovery(FollowRouteKind kind, string arrival)
    {
        RouteWorkspaceViewModel model = CreateViewModel(routeKind: kind);
        await model.UpdateContextAsync("F123", "Elsewhere", 99, null);
        await model.ImportNamesAsync(["Sol", "Second", "Third"]);
        model.SetProgressThrough(0, true);
        model.AutoCopy = false;
        model.IsActive = false;
        model.NotesDraft = "Check the fuel before departure.";
        await model.SaveNotesAsync();
        await model.UpdateContextAsync("F456", "Elsewhere", 99, null);
        Assert.False(model.HasRoute);
        await model.UpdateContextAsync("F123", "Elsewhere", 99, null);
        Assert.Equal(1, model.ReachedCount);
        Assert.False(model.AutoCopy);
        Assert.False(model.IsActive);
        Assert.Equal("Check the fuel before departure.", model.CreateSnapshot()!.Notes);

        model.IsActive = true;
        await model.ApplyJournalEventsAsync([
            Parse($$"""{"event":"{{arrival}}","StarSystem":"Second","SystemAddress":2}"""),
        ]);
        await model.FlushWorkspaceAsync();
        RouteWorkspaceViewModel restarted = CreateViewModel(routeKind: kind);
        await restarted.UpdateContextAsync("F123", "Second", 2, null);
        Assert.Equal(2, restarted.ReachedCount);
        Assert.True(restarted.IsActive);
        Assert.Equal("Third", restarted.NextHopName);
        Assert.False(restarted.HasSavedRoute);
        if (kind == FollowRouteKind.FleetCarrier)
        {
            Assert.True(restarted.ShouldShowFleetCarrierRouteOverlay);
        }
        await restarted.ResetAsync();
        await restarted.RefreshAsync();
        Assert.Equal(0, restarted.ReachedCount);
        await restarted.ConfirmNewAsync();
        await restarted.RefreshAsync();
        Assert.False(restarted.HasRoute);
        Assert.Empty(restarted.SavedRoutes);

        await restarted.ImportNamesAsync(["Sol", "Achenar"]);
        Assert.True(await restarted.DeactivateCurrentRouteAsync());
        await restarted.RefreshAsync();
        Assert.False(restarted.HasRoute);
    }

    /// <summary>Replacing a working route updates recovery while storage errors keep the active in-memory itinerary.</summary>
    [Fact]
    public async Task ReplacementAndStorageFailuresDoNotSilentlyLoseWorkingRoutes()
    {
        RouteWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Elsewhere", 99, null);
        await model.ImportNamesAsync(["Sol", "Second"]);
        await model.ImportNamesAsync(["Achenar", "Third"]);
        await model.RefreshAsync();
        Assert.Equal(["Achenar", "Third"], model.Hops.Select(hop => hop.Name));

        string workspace = Path.Combine(temporaryDirectory, "Routes", "F123", ".workspace.json");
        File.Delete(workspace);
        Directory.CreateDirectory(workspace);
        await model.ImportNamesAsync(["Muang", "Shinrarta Dezhra"]);
        Assert.Equal(["Muang", "Shinrarta Dezhra"], model.Hops.Select(hop => hop.Name));
        Assert.Contains("could not be retained", model.StatusMessage);
    }

    /// <summary>A faulted background save must not block the next edit, flush, or commander reload.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterEditsRecoverAfterAnUnexpectedWorkspaceSaveFailure(bool jsonFailure)
    {
        RouteWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Elsewhere", 99, null);
        await model.ImportNamesAsync(["Sol", "Second", "Third"]);
        Exception failure = jsonFailure
            ? new System.Text.Json.JsonException("Test serialization failure")
            : new NotSupportedException("Test serialization failure");
        System.Reflection.FieldInfo? pendingSave = typeof(RouteWorkspaceViewModel).GetField(
            "workspaceSave",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.NotNull(pendingSave);
        pendingSave.SetValue(model, Task.FromException(failure));

        model.SetProgressThrough(0, true);
        await model.FlushWorkspaceAsync();

        Assert.Contains("Test serialization failure", model.StatusMessage);
        await model.RefreshAsync();
        Assert.Equal(1, model.ReachedCount);
        Assert.Equal("Second", model.NextHopName);
        Assert.True(model.HasRoute);
    }

    /// <summary>Verifies poi annotations preserve imported route and progress.</summary>
    [Fact]
    public async Task PoiAnnotationsPreserveImportedRouteAndProgress()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        RouteWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
        SrvSurvey.Core.Routes.FollowRouteDocument before = model.CreateSnapshot()!;
        var poi = new SrvSurvey.Core.Exploration.ExplorationPoi(
            1,
            "GEC",
            "Scenic stop",
            "Second",
            null,
            null,
            "Sights and Scenery",
            "",
            "",
            "",
            7,
            4,
            12,
            false
        );
        model.ApplyPoiCatalog(new SrvSurvey.Core.Exploration.ExplorationPoiCatalog([poi]));
        FollowRouteDocument after = model.CreateSnapshot()!;
        Assert.Equal(before.Hops, after.Hops);
        Assert.Equal(before.LastReachedIndex, after.LastReachedIndex);
        Assert.Equal(before.IsActive, after.IsActive);
        Assert.Equal(before.AutoCopy, after.AutoCopy);
        Assert.Equal(before.Notes, after.Notes);
        Assert.False(model.IsDirty);
        Assert.True(model.Hops[1].HasPois);
        Assert.Equal("Scenic stop", model.Hops[1].PoiText);
        Assert.False(model.Hops[0].HasPois);
        model.SetProgressThrough(1, true);
        Assert.True(model.Hops[1].HasPois);
        model.ApplyPoiCatalog(SrvSurvey.Core.Exploration.ExplorationPoiCatalog.Empty);
        Assert.False(model.Hops[1].HasPois);
    }

    [Fact]
    public async Task LegacyRouteDisplaysSegmentsAndSavesProgressAndPreferences()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));

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
        FollowRouteLoadResult saved = await new FollowRouteStore(temporaryDirectory).LoadAsync("F123");
        Assert.Equal(2, saved.Route!.LastReachedIndex);
        Assert.False(saved.Route.IsActive);
        Assert.False(saved.Route.AutoCopy);
    }

    [Fact]
    public async Task NameImportKeepsUnknownSystemsAndChecksCurrentFirstHop()
    {
        var resolver = new StubResolver(
            new Dictionary<string, StarSystemReference> { ["Sol"] = new("Sol", 1, new GalacticCoordinate(0, 0, 0)) }
        );
        RouteWorkspaceViewModel viewModel = CreateViewModel(resolver: resolver);
        await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));

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
        FollowRouteHop[] imported = new[]
        {
            new FollowRouteHop("Jackson's Lighthouse", 7, new GalacticCoordinate(1, 2, 3), null, true, true),
        };
        var spanshClient = new StubSpanshClient(imported);
        RouteWorkspaceViewModel viewModel = CreateViewModel(spanshClient: spanshClient);
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ImportSpanshUrlAsync("not a URL");

        Assert.Equal(0, spanshClient.CallCount);
        Assert.Contains("valid Spansh route", viewModel.StatusMessage);

        await viewModel.ImportSpanshUrlAsync(
            "https://spansh.co.uk/exact-plotter/results/74FA2952-2048-11F1-8302-B948FF6DF5C1"
        );

        Assert.Equal(1, spanshClient.CallCount);
        RouteHopItemViewModel hop = Assert.Single(viewModel.Hops);
        Assert.Contains("Refuel", hop.Notes);
        Assert.Contains("Neutron", hop.Notes);
    }

    [Fact]
    public async Task FleetCarrierWorkspaceAcceptsOnlyFleetCarrierSpanshRoutes()
    {
        var spanshClient = new StubSpanshClient([
            new FollowRouteHop(
                "Colonia",
                7,
                null,
                "Refuel 500 t Tritium",
                false,
                false,
                Carrier: new FollowRouteCarrierHop(
                    DistanceLy: 499.76,
                    RemainingLy: 21502.09,
                    FuelRemainingTonnes: 1000,
                    TritiumInMarketTonnes: 2799,
                    FuelUsedTonnes: 93,
                    HasIcyRing: true,
                    IsSystemPristine: true,
                    MustRestock: true,
                    RestockAmountTonnes: 3892
                )
            ),
        ]);
        RouteWorkspaceViewModel viewModel = CreateViewModel(
            spanshClient: spanshClient,
            routeKind: FollowRouteKind.FleetCarrier
        );
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ImportSpanshUrlAsync(
            "https://spansh.co.uk/exact-plotter/results/74FA2952-2048-11F1-8302-B948FF6DF5C1"
        );

        Assert.Equal(0, spanshClient.CallCount);
        Assert.Contains("Fleet Carrier Router", viewModel.StatusMessage);

        await viewModel.ImportSpanshUrlAsync(
            "https://spansh.co.uk/fleet-carrier/results/74FA2952-2048-11F1-8302-B948FF6DF5C1"
        );

        Assert.Equal(1, spanshClient.CallCount);
        RouteHopItemViewModel hop = Assert.Single(viewModel.Hops);
        Assert.Equal("Colonia", hop.Hop.Name);
        Assert.True(hop.IsFleetCarrierHop);
        Assert.False(hop.IsStandardHop);
        Assert.Equal($"{499.76:N2}", hop.CarrierDistance);
        Assert.Equal($"{21502.09:N2}", hop.CarrierRemaining);
        Assert.Equal(0, hop.JumpsRemaining);
        Assert.Equal($"{1000:N0}", hop.CarrierFuelRemaining);
        Assert.Equal($"{2799:N0}", hop.CarrierTritiumInMarket);
        Assert.Equal($"{93:N0}", hop.CarrierFuelUsed);
        Assert.Equal("PRISTINE", hop.CarrierIcyRing);
        Assert.Equal("YES", hop.CarrierRestock);
        Assert.Equal($"{3892:N0}", hop.CarrierRestockAmount);
    }

    [Fact]
    public async Task FleetCarrierWorkspaceAdvancesOnlyOnCarrierJump()
    {
        var store = new FollowRouteStore(temporaryDirectory, FollowRouteKind.FleetCarrier);
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
            "Carrier Test"
        );
        RouteWorkspaceViewModel viewModel = CreateViewModel(routeKind: FollowRouteKind.FleetCarrier);
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ApplyJournalEventsAsync([
            Parse(
                """
                {"event":"FSDJump","StarSystem":"Second","SystemAddress":2}
                """
            ),
        ]);

        Assert.Equal(1, viewModel.ReachedCount);

        await viewModel.ApplyJournalEventsAsync([
            Parse(
                """
                {"event":"CarrierJump","StarSystem":"Second","SystemAddress":2}
                """
            ),
        ]);

        Assert.Equal(2, viewModel.ReachedCount);
        Assert.True(viewModel.IsComplete);
        FollowRouteLoadResult saved = await store.LoadAsync("F123");
        Assert.Equal(1, saved.Route!.LastReachedIndex);
    }

    [Fact]
    public async Task FleetCarrierCountdownUsesJournalLifecycleAndResetsForCommander()
    {
        var now = DateTimeOffset.Parse(
            "2026-08-01T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        RouteWorkspaceViewModel viewModel = CreateViewModel(routeKind: FollowRouteKind.FleetCarrier, utcNow: () => now);
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        viewModel.ApplyFleetCarrierJumpEvents([
            Parse(
                """
                {"timestamp":"2026-08-01T12:00:00Z","event":"CarrierJumpRequest","CarrierID":123,"SystemName":"Colonia","DepartureTime":"2026-08-01T12:15:00Z"}
                """
            ),
        ]);

        Assert.True(viewModel.HasCarrierJumpCountdown);
        Assert.Equal("DEPARTURE TO COLONIA", viewModel.CarrierJumpCountdownTitle);
        Assert.Equal("15:00", viewModel.CarrierJumpCountdownValue);
        Assert.Equal("JUMP INITIATION IN", viewModel.CarrierJumpPhaseLabel);

        now = now.AddMinutes(7);
        viewModel.RefreshCarrierJumpCountdown();
        Assert.Equal("8:00", viewModel.CarrierJumpCountdownValue);
        Assert.Equal("PAD LOCKDOWN IN", viewModel.CarrierJumpPhaseLabel);

        await viewModel.UpdateContextAsync("F456", "Sol", 1, null);
        Assert.False(viewModel.HasCarrierJumpCountdown);
        Assert.Equal("No jump scheduled", viewModel.CarrierJumpCountdownValue);
    }

    [Fact]
    public async Task LiveFsdJumpAdvancesExpectedHopAndPersistsIt()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.ApplyJournalEventsAsync([
            Parse(
                """
                {"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Second","SystemAddress":2,"StarPos":[3,4,0]}
                """
            ),
        ]);

        Assert.Equal(2, viewModel.ReachedCount);
        Assert.Equal("Third", viewModel.NextHopName);
        Assert.Contains("hop #2", viewModel.StatusMessage);
        FollowRouteLoadResult saved = await new FollowRouteStore(temporaryDirectory).LoadAsync("F123");
        Assert.Equal(1, saved.Route!.LastReachedIndex);
    }

    [Fact]
    public async Task OutOfOrderArrivalDoesNotChangeRouteButFinalArrivalCompletes()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: -1);
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Elsewhere", 99, null);

        await viewModel.ApplyJournalEventsAsync([
            Parse(
                """
                {"event":"FSDJump","StarSystem":"Second","SystemAddress":2}
                """
            ),
        ]);
        Assert.Equal(0, viewModel.ReachedCount);

        await viewModel.ApplyJournalEventsAsync([
            Parse(
                """
                {"event":"FSDJump","StarSystem":"Third","SystemAddress":3}
                """
            ),
        ]);

        Assert.True(viewModel.IsComplete);
        Assert.False(viewModel.IsActive);
        Assert.Contains("Route complete", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CopyNextHopUsesDesktopClipboardBoundary()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        RouteWorkspaceViewModel viewModel = CreateViewModel();
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
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        viewModel.SetClipboardWriter(text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });
        await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));

        Assert.True(viewModel.ShouldAutoCopyNextHop);

        await viewModel.UpdateStatusAsync(
            new EliteStatus
            {
                GuiFocus = GuiFocus.GalaxyMap,
                Destination = new StatusDestination { System = 2, Name = "Second" },
            }
        );
        await viewModel.UpdateStatusAsync(
            new EliteStatus
            {
                GuiFocus = GuiFocus.GalaxyMap,
                Destination = new StatusDestination { System = 2, Name = "Second" },
            }
        );

        Assert.Equal(["Second"], copied);
        Assert.True(viewModel.ShouldShowGalaxyMapOverlay);
        Assert.Equal("5.00 ly from Sol", viewModel.NextHopDistance);
        Assert.Equal("SELECTED IN GALAXY MAP", viewModel.NextHopDestinationStatus);
        Assert.Equal("NEXT SYSTEM COPIED", viewModel.NextHopClipboardStatus);

        await viewModel.UpdateStatusAsync(new EliteStatus());
        Assert.False(viewModel.ShouldShowGalaxyMapOverlay);
        await viewModel.UpdateStatusAsync(new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });

        Assert.Equal(["Second", "Second"], copied);
    }

    [Fact]
    public async Task GalaxyMapMusicFallbackTriggersRouteOverlayAndAutoCopy()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var copied = new List<string>();
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        viewModel.SetClipboardWriter(text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });
        await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));

        await viewModel.UpdateStatusAsync(
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                Destination = new StatusDestination { System = 2, Name = "Second" },
            },
            "GalaxyMap"
        );

        Assert.True(viewModel.ShouldShowGalaxyMapOverlay);
        Assert.Equal(["Second"], copied);
        Assert.Equal("SELECTED IN GALAXY MAP", viewModel.NextHopDestinationStatus);
    }

    [Fact]
    public async Task PausedOrManualRouteDoesNotAutoCopyInGalaxyMap()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        var copied = new List<string>();
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        viewModel.SetClipboardWriter(text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);
        viewModel.AutoCopy = false;

        await viewModel.UpdateStatusAsync(new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });

        Assert.False(viewModel.ShouldAutoCopyNextHop);
        Assert.Empty(copied);
    }

    [Fact]
    public async Task GalaxyMapClipboardFailureIsReportedWithoutEscaping()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        viewModel.SetClipboardWriter(_ => throw new InvalidOperationException("clipboard locked"));
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        await viewModel.UpdateStatusAsync(new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });

        Assert.Contains("clipboard locked", viewModel.StatusMessage);
        Assert.Equal("AUTO-COPY READY", viewModel.NextHopClipboardStatus);
    }

    [Fact]
    public async Task MalformedRouteIsReportedWithoutCreatingAnEditableDraft()
    {
        string directory = Path.Combine(temporaryDirectory, "routes");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "F123.json"), "{\"hops\":");
        RouteWorkspaceViewModel viewModel = CreateViewModel();

        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        Assert.False(viewModel.HasRoute);
        Assert.False(viewModel.IsDirty);
        Assert.Contains("Could not read", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveAsCreatesNamedRouteAndUndoRestoresItsDefinition()
    {
        RouteWorkspaceViewModel viewModel = CreateViewModel();
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
        Assert.True(File.Exists(Path.Combine(temporaryDirectory, "Routes", "F123", "Bubble Tour.json")));

        await viewModel.ImportNamesAsync(["Beagle Point"]);
        Assert.True(viewModel.HasDefinitionChanges);
        Assert.False(viewModel.CanSaveChanges);
        await viewModel.RefreshAsync();
        Assert.Equal("Beagle Point", Assert.Single(viewModel.Hops).Name);
        await viewModel.DiscardAsync();

        Assert.Equal(["Sol", "Achenar"], viewModel.Hops.Select(hop => hop.Name));
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task SaveAsErrorVisibilityTracksValidationAndNameChanges()
    {
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);
        await viewModel.ImportNamesAsync(["Sol", "Achenar"]);
        viewModel.SaveAsCommand.Execute(null);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName);

        viewModel.SaveAsName = " ";
        await viewModel.ConfirmSaveAsAsync();

        Assert.True(viewModel.HasSaveAsError);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SaveAsError));
        Assert.Contains(nameof(RouteWorkspaceViewModel.HasSaveAsError), notifications);

        notifications.Clear();
        viewModel.SaveAsName = "Bubble Tour";

        Assert.False(viewModel.HasSaveAsError);
        Assert.Equal(string.Empty, viewModel.SaveAsError);
        Assert.Contains(nameof(RouteWorkspaceViewModel.HasSaveAsError), notifications);
    }

    [Fact]
    public async Task NotesSaveImmediatelyButResetWaitsForSaveChanges()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        viewModel.NotesCommand.Execute(null);
        viewModel.NotesDraft = "Refuel before the neutron section.";
        await viewModel.SaveNotesAsync();
        await viewModel.ResetAsync();

        var store = new FollowRouteStore(temporaryDirectory);
        FollowRouteLoadResult beforeProgressSave = await store.LoadAsync("F123");
        Assert.Equal("Refuel before the neutron section.", beforeProgressSave.Route!.Notes);
        Assert.Equal(0, beforeProgressSave.Route.LastReachedIndex);
        Assert.True(viewModel.CanSaveChanges);

        await viewModel.SaveAsync();
        FollowRouteLoadResult afterProgressSave = await store.LoadAsync("F123");
        Assert.Equal(-1, afterProgressSave.Route!.LastReachedIndex);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task NewKeepsSavedRouteWhileDeleteMovesItOutOfLibrary()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        FollowRouteDocument saved = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops = [Hop("Sol", 1, new GalacticCoordinate(0, 0, 0))],
            },
            "Keep Until Deleted"
        );
        RouteWorkspaceViewModel viewModel = CreateViewModel();
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
        Assert.Single(Directory.EnumerateFiles(Path.Combine(temporaryDirectory, "Routes", "F123", ".trash"), "*.json"));
    }

    [Fact]
    public async Task ClosingWorkspaceDismissesPendingConfirmationState()
    {
        await SaveRouteAsync(isActive: true, lastReachedIndex: 0);
        RouteWorkspaceViewModel viewModel = CreateViewModel();
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
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
        IReadOnlyList<RouteHopItemViewModel> rows = viewModel.Hops;
        RouteHopItemViewModel firstRow = rows[0];
        RouteHopItemViewModel secondRow = rows[1];

        bool reinitialized = await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
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
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, null);

        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName);
        var dialogs = new (ICommand Command, Func<bool> IsVisible, string VisibilityProperty)[]
        {
            (
                viewModel.NewCommand,
                () => viewModel.IsNewConfirmationVisible,
                nameof(RouteWorkspaceViewModel.IsNewConfirmationVisible)
            ),
            (viewModel.SaveAsCommand, () => viewModel.IsSaveAsVisible, nameof(RouteWorkspaceViewModel.IsSaveAsVisible)),
            (viewModel.NotesCommand, () => viewModel.IsNotesVisible, nameof(RouteWorkspaceViewModel.IsNotesVisible)),
            (
                viewModel.DeleteCommand,
                () => viewModel.IsDeleteConfirmationVisible,
                nameof(RouteWorkspaceViewModel.IsDeleteConfirmationVisible)
            ),
        };
        foreach ((ICommand Command, Func<bool> IsVisible, string VisibilityProperty) dialog in dialogs)
        {
            notifications.Clear();
            dialog.Command.Execute(null);
            Assert.True(dialog.IsVisible());
            Assert.True(viewModel.IsDialogVisible);
            Assert.Contains(dialog.VisibilityProperty, notifications);
            Assert.Contains(nameof(RouteWorkspaceViewModel.IsDialogVisible), notifications);

            notifications.Clear();
            viewModel.DismissDialogs();

            Assert.False(viewModel.IsDialogVisible);
            Assert.False(dialog.IsVisible());
            Assert.Contains(dialog.VisibilityProperty, notifications);
            Assert.Contains(nameof(RouteWorkspaceViewModel.IsDialogVisible), notifications);
        }

        Assert.True(viewModel.HasSavedRoute);
        Assert.Equal(3, viewModel.RouteCount);
    }

    [Fact]
    public async Task BioCompletionPersistsAndKeepsSharedRowsStable()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        await store.SaveAsync(
            new FollowRouteDocument(
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
                                IsBiological: true
                            ),
                        ]
                    ),
                    Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
                ]
            )
        );
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
        IReadOnlyList<RouteHopItemViewModel> rows = viewModel.Hops;
        RouteHopItemViewModel hop = Assert.Single(rows, candidate => candidate.IsCurrent);
        RouteBioTargetItemViewModel target = Assert.Single(hop.BioTargets);
        var notifications = new List<string?>();
        target.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName);

        await viewModel.SetBioTargetCompletedAsync(target, true);

        Assert.Same(rows, viewModel.Hops);
        Assert.Same(hop, viewModel.CurrentBioHop);
        Assert.Same(target, Assert.Single(viewModel.CurrentBioTargets));
        Assert.True(target.IsCompleted);
        Assert.Equal("Rocky body", target.Subtype);
        Assert.EndsWith("/Assets/Bodies/rocky-body.png", target.BodyIconAssetPath, StringComparison.Ordinal);
        Assert.Equal("1,500 LS", target.DistanceToArrival);
        Assert.Equal("500 CR", target.EstimatedScanValue);
        Assert.Equal("2,221 CR", target.EstimatedMappingValue);
        Assert.Equal("27,428,800 CR", target.EstimatedBiologyValue);
        Assert.True(target.IsTerraformable);
        Assert.True(target.HasDetails);
        Assert.True(viewModel.ShouldShowRouteBioOverlay);
        Assert.Contains(nameof(RouteBioTargetItemViewModel.IsCompleted), notifications);
        FollowRouteLoadResult reloaded = await store.LoadAsync("F123");
        Assert.True(reloaded.Route!.Hops[0].BioTargets[0].IsCompleted);
        RouteWorkspaceViewModel restarted = CreateViewModel();
        await restarted.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
        Assert.True(Assert.Single(restarted.CurrentBioTargets).IsCompleted);
        Assert.False(restarted.HasDefinitionChanges);
        Assert.False(restarted.IsDirty);

        notifications.Clear();
        await viewModel.SetBioTargetCompletedAsync(target, true);

        Assert.Empty(notifications);
        Assert.Same(target, Assert.Single(viewModel.CurrentBioTargets));
    }

    [Fact]
    public async Task BodyArrivalCompletesTheMatchingRouteTarget()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        await store.SaveAsync(
            new FollowRouteDocument(
                "F123",
                store.GetPath("F123"),
                true,
                true,
                0,
                [
                    new FollowRouteHop(
                        "Synuefe NL-N C23-4",
                        101,
                        new GalacticCoordinate(0, 0, 0),
                        null,
                        false,
                        false,
                        [new FollowRouteBioTarget("A 4", 10, ["Bacterium Acies"], Subtype: "Rocky body")]
                    ),
                    Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
                ]
            )
        );
        RouteWorkspaceViewModel viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync("F123", "Synuefe NL-N C23-4", 101, new GalacticCoordinate(0, 0, 0));
        RouteBioTargetItemViewModel target = Assert.Single(viewModel.CurrentBioTargets);

        await viewModel.ApplyJournalEventsAsync([
            Parse(
                """
                {"event":"ApproachBody","StarSystem":"Synuefe NL-N C23-4","SystemAddress":101,"Body":"Synuefe NL-N C23-4 A 4","BodyID":10}
                """
            ),
        ]);

        Assert.Same(target, Assert.Single(viewModel.CurrentBioTargets));
        Assert.True(target.IsCompleted);
        FollowRouteLoadResult reloaded = await store.LoadAsync("F123");
        Assert.True(reloaded.Route!.Hops[0].BioTargets[0].IsCompleted);
    }

    [Fact]
    public void BodyArtworkChangesOnlyWhenTheBodySubtypeChanges()
    {
        var source = new FollowRouteBioTarget("A 1", 10, [], Subtype: "Rocky body");
        var target = new RouteBioTargetItemViewModel(0, 0, source);
        IReadOnlyList<RouteBioDetailSegmentViewModel> originalSegments = target.CompactDetailSegments;
        IReadOnlyList<RouteBioDetailSegmentViewModel> originalInlineSegments = target.InlineSegments;
        var notifications = new List<string?>();
        target.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName);

        target.Update(source);

        Assert.Empty(notifications);
        Assert.Same(originalSegments, target.CompactDetailSegments);
        Assert.Same(originalInlineSegments, target.InlineSegments);
        Assert.Equal("Rocky body", Assert.Single(originalSegments).Text);
        Assert.Equal(["A 1", "Rocky body"], originalInlineSegments.Select(segment => segment.Text));
        Assert.True(originalInlineSegments[0].IsBodyName);
        Assert.True(originalInlineSegments[1].IsDetail);
        Assert.EndsWith("/Assets/Bodies/rocky-body.png", target.BodyIconAssetPath, StringComparison.Ordinal);

        target.Update(source with { Subtype = "Water world" });

        Assert.EndsWith("/Assets/Bodies/water-world.png", target.BodyIconAssetPath, StringComparison.Ordinal);
        Assert.Contains(nameof(RouteBioTargetItemViewModel.BodyIconAssetPath), notifications);
        Assert.Contains(nameof(RouteBioTargetItemViewModel.BodyIconAccessibleName), notifications);
        Assert.NotSame(originalSegments, target.CompactDetailSegments);
        Assert.NotSame(originalInlineSegments, target.InlineSegments);
        Assert.Contains(nameof(RouteBioTargetItemViewModel.CompactDetailSegments), notifications);
        Assert.Contains(nameof(RouteBioTargetItemViewModel.InlineSegments), notifications);

        notifications.Clear();
        target.Update(source with { Subtype = "WATER-WORLD" });

        Assert.DoesNotContain(nameof(RouteBioTargetItemViewModel.BodyIconAssetPath), notifications);
        Assert.DoesNotContain(nameof(RouteBioTargetItemViewModel.BodyIconAccessibleName), notifications);
    }

    private RouteWorkspaceViewModel CreateViewModel(
        IStarSystemResolver? resolver = null,
        ISpanshRouteClient? spanshClient = null,
        FollowRouteKind routeKind = FollowRouteKind.Standard,
        Func<DateTimeOffset>? utcNow = null,
        ISpanshPlannerClient? plannerClient = null
    )
    {
        var store = new FollowRouteStore(temporaryDirectory, routeKind);
        var model = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(resolver ?? new StubResolver(new Dictionary<string, StarSystemReference>())),
            spanshClient ?? new StubSpanshClient([]),
            routeKind,
            utcNow,
            plannerClient: plannerClient
        );
        workspaces.Add(model);
        return model;
    }

    private async Task SaveRouteAsync(bool isActive, int lastReachedIndex)
    {
        var store = new FollowRouteStore(temporaryDirectory);
        await store.SaveAsync(
            new FollowRouteDocument(
                "F123",
                store.GetPath("F123"),
                isActive,
                true,
                lastReachedIndex,
                [
                    Hop("Sol", 1, new GalacticCoordinate(0, 0, 0)),
                    Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
                    Hop("Third", 3, new GalacticCoordinate(3, 4, 12)),
                ]
            )
        );
    }

    private static FollowRouteHop Hop(string name, long address, GalacticCoordinate position)
    {
        return new FollowRouteHop(name, address, position, null, false, false);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? journalEvent, out string? error),
            error
        );
        return journalEvent!;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (RouteWorkspaceViewModel workspace in workspaces)
        {
            await workspace.FlushWorkspaceAsync();
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubResolver(IReadOnlyDictionary<string, StarSystemReference> systems) : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            IReadOnlyList<StarSystemReference> result = systems.TryGetValue(query, out StarSystemReference? system)
                ? [system]
                : [];
            return Task.FromResult(result);
        }
    }

    private sealed class StubSpanshClient(IReadOnlyList<FollowRouteHop> hops) : ISpanshRouteClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            return Task.FromResult(hops);
        }
    }

    private sealed class StubPlannerClient : ISpanshPlannerClient
    {
        /// <summary>Returns a deterministic planner job for testing the real search-to-route handoff.</summary>
        public Task<SpanshRouteReference> SubmitAsync(
            SpanshPlannerRequest request,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new SpanshRouteReference(Guid.Parse("74FA2952-2048-11F1-8302-B948FF6DF5C1"), request.Planner.Kind)
            );
    }
}
