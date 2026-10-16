using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class FleetCarrierRouteFuelTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Origin = DateTimeOffset.Parse(
        "2026-10-15T12:00:00Z",
        CultureInfo.InvariantCulture
    );
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-live-carrier-{Guid.NewGuid():N}");
    private readonly List<RouteWorkspaceViewModel> models = [];

    [Fact]
    public async Task NumericStringCapiReservePopulatesTheDepotSummaryAndSearchFuel()
    {
        FrontierAccountSnapshot account = Account() with
        {
            Carrier = FrontierCapiSnapshotParser.ParseCarrier(
                """
                {"id":1,"name":{"callsign":"OWN-001"},"currentStarSystem":"A","fuel":"335","capacity":{"cargoNotForSale":5251,"crew":1050,"freeSpace":18699},"cargo":[]}
                """,
                Origin
            ),
        };
        Assert.Equal(335, account.Carrier.Tritium);
        RouteWorkspaceViewModel model = await Create(() => account);
        model.PrepareCarrierSearch();

        Assert.Contains("Depot: 335 t", model.CarrierFuelSummary);
        Assert.Equal("335", Field(model, "fuel_loaded").Value);
        Assert.Equal("0", Field(model, "tritium_stored").Value);
        Assert.Equal("6301", Field(model, "capacity_used").Value);
        Assert.False(Field(model, "calculate_starting_fuel").IsChecked);
    }

    [Fact]
    public async Task CarrierSelectorPrefillsSearchFromItsOwnLatestObservationsAndPersistsTheChoice()
    {
        FrontierAccountSnapshot account = Account();
        FrontierAccountSnapshot? activeAccount = account;
        var planner = new Results();
        RouteWorkspaceViewModel model = await Create(() => activeAccount, planner);
        model.PrepareCarrierSearch();
        Assert.Equal("700", Field(model, "fuel_loaded").Value);
        Assert.Equal("500", Field(model, "tritium_stored").Value);
        Assert.Equal("1000", Field(model, "capacity_used").Value);
        Assert.Equal("A", Field(model, "source").Value);
        Assert.False(Field(model, "calculate_starting_fuel").IsChecked);
        Assert.Contains("OWN-001", model.CarrierFuelSummary);
        model.SelectedCarrier = model.CarrierOptions[1];
        Assert.Equal("Squadron Carrier", Field(model, "carrier_type").Value);
        Assert.False(Field(model, "carrier_type").IsVisible);
        Assert.Equal("900", Field(model, "fuel_loaded").Value);
        Assert.Equal("4000", Field(model, "tritium_stored").Value);
        Assert.Equal(60000m, Field(model, "capacity_used").Maximum);
        await model.ApplyJournalEventsAsync([
            Event(
                1,
                "CarrierStats",
                "\"CarrierID\":2,\"Callsign\":\"SQU-002\",\"FuelLevel\":800,\"SpaceUsage\":{\"TotalCapacity\":60000,\"FreeSpace\":48000}"
            ),
            Event(2, "CarrierDepositFuel", "\"CarrierID\":1,\"Total\":1000"),
        ]);
        model.PrepareCarrierSearch();
        Assert.Equal("800", Field(model, "fuel_loaded").Value);
        Assert.Equal("12000", Field(model, "capacity_used").Value);
        Field(model, "destinations").Value = "C";
        Assert.True(await model.SpanshSearch!.SearchAsync());
        Assert.Equal("15000", planner.Request!.Parameters.Single(pair => pair.Key == "mass").Value);
        Assert.Equal("800", planner.Request.Parameters.Single(pair => pair.Key == "fuel_loaded").Value);
        Assert.Equal("4000", planner.Request.Parameters.Single(pair => pair.Key == "tritium_stored").Value);
        Assert.Equal(FollowRouteCarrierKind.Squadron, model.CreateSnapshot()!.CarrierKind);
        model.SaveAsName = "Squadron loop";
        await model.ConfirmSaveAsAsync();
        await model.FlushWorkspaceAsync();
        RouteWorkspaceViewModel reopened = await Create(() => activeAccount);
        Assert.Equal(FollowRouteCarrierKind.Squadron, reopened.SelectedCarrierKind);
        Assert.Equal(
            FollowRouteCarrierKind.Squadron,
            (await new FollowRouteStore(directory, FollowRouteKind.FleetCarrier).LoadAsync("F1")).Route!.CarrierKind
        );
        activeAccount = null;
        await reopened.UpdateContextAsync("F2", "Elsewhere", 99, null);
        Assert.Equal(FollowRouteCarrierKind.Personal, reopened.SelectedCarrierKind);
        Assert.DoesNotContain("SQU-002", reopened.CarrierFuelSummary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatCapturesFuelOnceAtTheBoundaryWarnsOfShortfallAndRefreshResumesAfterResupply(bool reverse)
    {
        int reads = 0;
        int refreshes = 0;
        FrontierAccountSnapshot account = Account(tank: 1000, stock: 0);
        var results = new Results { Closed = !reverse };
        RouteWorkspaceViewModel model = await Create(
            () =>
            {
                reads++;
                return account;
            },
            results,
            () =>
            {
                refreshes++;
                return Task.CompletedTask;
            }
        );
        await Import(model);
        model.RepeatAndReverse = reverse;
        model.RepeatFromStart = !reverse;
        int beforePass = reads;
        await model.ApplyJournalEventsAsync([
            Event(1, "CarrierJump", "\"MarketID\":1,\"StarSystem\":\"B\",\"StarPos\":[100,0,0]"),
        ]);
        Assert.Equal(beforePass, reads);
        Assert.Equal(2, model.ReachedCount);
        await model.ApplyJournalEventsAsync([
            Event(2, "CarrierStats", "\"CarrierID\":1,\"Callsign\":\"OWN-001\",\"FuelLevel\":30"),
        ]);
        Assert.Equal(beforePass, reads);
        await model.ApplyJournalEventsAsync([
            Event(3, "CarrierJump", "\"MarketID\":1,\"StarSystem\":\"C\",\"StarPos\":[250,0,0]"),
        ]);
        if (!reverse)
        {
            await model.ApplyJournalEventsAsync([
                Event(4, "CarrierStats", "\"CarrierID\":1,\"Callsign\":\"OWN-001\",\"FuelLevel\":40"),
            ]);
            await model.ApplyJournalEventsAsync([
                Event(5, "CarrierJump", "\"MarketID\":1,\"StarSystem\":\"A\",\"StarPos\":[0,0,0]"),
            ]);
        }
        Assert.Equal(beforePass + 1, reads);
        Assert.True(model.IsActive);
        Assert.True(model.IsFuelWarningVisible);
        double shortage = Assert.IsType<double>(model.AdditionalTritiumNeeded);
        Assert.True(shortage > 0);
        Assert.Contains(shortage.ToString("N0", CultureInfo.CurrentCulture), model.FuelWarningMessage);
        Assert.Contains("Mine or buy", model.FuelWarningMessage);
        Assert.Contains("remaining route", model.FuelWarningMessage);
        string[] order = model.Hops.Select(hop => hop.Name).ToArray();
        int reached = model.ReachedCount;
        model.DismissDialogs();
        await model.ApplyJournalEventsAsync([Event(10, "CarrierDepositFuel", "\"CarrierID\":1,\"Total\":500")]);
        Assert.Equal(beforePass + 1, reads);
        Assert.Equal(shortage, model.AdditionalTritiumNeeded);
        await model.RefreshAsync();
        Assert.Equal(1, refreshes);
        Assert.Equal(beforePass + 2, reads);
        Assert.False(model.IsFuelWarningVisible);
        Assert.Equal(0, model.AdditionalTritiumNeeded);
        Assert.Equal(order, model.Hops.Select(hop => hop.Name));
        Assert.Equal(reached, model.ReachedCount);
        Assert.Equal("B", model.NextHopName);
        Assert.Contains("current waypoint", model.StatusMessage);
        Assert.All(model.CreateSnapshot()!.Hops, hop => Assert.Null(hop.Carrier));
    }

    [Fact]
    public async Task RefreshUsesTheCurrentWaypointAndNewCapiCargoWithoutRestartingThePass()
    {
        FrontierAccountSnapshot account = Account(tank: 700, stock: 500);
        RouteWorkspaceViewModel model = await Create(() => account);
        await Import(model);
        await model.ApplyJournalEventsAsync([
            Event(1, "CarrierJump", "\"MarketID\":1,\"StarSystem\":\"B\",\"StarPos\":[100,0,0]"),
        ]);
        account = account with
        {
            Carrier = account.Carrier! with
            {
                FuelData = account.Carrier.FuelData! with
                {
                    SystemName = "B",
                    TankTonnes = 300,
                    StoredTritiumTonnes = 2500,
                    UsedCapacityTonnes = 3000,
                    ObservedAt = Origin.AddSeconds(5),
                },
            },
        };
        await model.RefreshAsync();
        Assert.Equal(2, model.ReachedCount);
        Assert.Equal("C", model.NextHopName);
        Assert.Equal($"{150:N2} ly from B", model.NextHopDistance);
        Assert.Null(model.Hops[0].Hop.Carrier);
        Assert.Equal(0, model.Hops[1].Hop.Carrier!.FuelUsedTonnes);
        Assert.Equal(150, model.Hops[2].Hop.Carrier!.DistanceLy);
        Assert.Equal(
            2500,
            model.Hops[1].Hop.Carrier!.TritiumInMarketTonnes + model.Hops[1].Hop.Carrier!.RestockAmountTonnes
        );
        Assert.Equal("Keep notes", model.CreateSnapshot()!.Hops[0].Notes);
    }

    [Fact]
    public async Task UnavailableCarrierClearsAutofilledFuelAndOtherCarrierArrivalsCannotAdvanceTheRoute()
    {
        FrontierAccountSnapshot account = Account();
        RouteWorkspaceViewModel model = await Create(() => account);
        model.PrepareCarrierSearch();
        await Import(model);
        await model.ApplyJournalEventsAsync([
            Event(1, "CarrierJump", "\"MarketID\":2,\"StarSystem\":\"B\",\"StarPos\":[100,0,0]"),
        ]);
        Assert.Equal(1, model.ReachedCount);
        account = account with { SquadronCarrier = null };
        model.SelectedCarrier = model.CarrierOptions[1];
        Assert.Empty(Field(model, "capacity_used").Value);
        Assert.Empty(Field(model, "fuel_loaded").Value);
        Assert.Empty(Field(model, "tritium_stored").Value);
        Assert.Contains("Refresh", model.CarrierFuelSummary);
        await model.ApplyJournalEventsAsync([
            Event(2, "CarrierJump", "\"MarketID\":1,\"StarSystem\":\"B\",\"StarPos\":[100,0,0]"),
        ]);
        Assert.Equal(1, model.ReachedCount);
        Assert.False(model.IsFuelWarningVisible);
    }

    [Fact]
    public async Task RefreshFailuresAreReportedAndCommanderSwitchDiscardsAnOldPendingRefresh()
    {
        FrontierAccountSnapshot? account = Account();
        bool fail = true;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RouteWorkspaceViewModel model = await Create(
            () => account,
            refresh: async () =>
            {
                if (fail)
                {
                    throw new IOException("Fuel refresh failed");
                }
                await gate.Task;
            }
        );
        await Import(model);
        await model.RefreshAsync();
        Assert.Contains("Fuel refresh failed", model.StatusMessage);
        fail = false;
        Task refresh = model.RefreshAsync();
        account = null;
        await model.UpdateContextAsync("F2", "Elsewhere", 99, null);
        gate.SetResult();
        await refresh;
        Assert.False(model.HasRoute);
        Assert.False(model.IsFuelWarningVisible);
        Assert.DoesNotContain("OWN-001", model.CarrierFuelSummary);
    }

    [Fact]
    public async Task JournalOnlyDataPrefillsKnownFieldsAndIdentifiesTheSquadronWithoutCapi()
    {
        RouteWorkspaceViewModel model = await Create(() => null);
        await Import(model);
        model.SelectedCarrier = model.CarrierOptions[1];
        await model.ApplyJournalEventsAsync([
            Event(
                1,
                "CarrierStats",
                "\"CarrierID\":2,\"CarrierType\":\"SquadronCarrier\",\"Callsign\":\"SQU-002\",\"FuelLevel\":800,\"SpaceUsage\":{\"TotalCapacity\":60000,\"FreeSpace\":50000,\"Cargo\":500}"
            ),
            Event(2, "CarrierJump", "\"MarketID\":1,\"StarSystem\":\"A\",\"StarPos\":[0,0,0]"),
            Event(3, "CarrierJump", "\"MarketID\":2,\"StarSystem\":\"A\",\"StarPos\":[0,0,0]"),
            Event(4, "CarrierDepositFuel", "\"CarrierID\":2,\"Total\":850"),
        ]);
        Assert.Equal(1, model.ReachedCount);
        model.PrepareCarrierSearch();
        Assert.Equal("850", Field(model, "fuel_loaded").Value);
        Assert.Equal("10000", Field(model, "capacity_used").Value);
        Assert.Empty(Field(model, "tritium_stored").Value);
        Assert.Equal("A", Field(model, "source").Value);
        await model.RefreshAsync();
        Assert.Contains("require known fuel", model.CarrierFuelPlanMessage);
        Assert.False(model.IsFuelWarningVisible);
    }

    [Fact]
    public async Task LoadingAndResettingAnExistingRouteCaptureCurrentFuelInsteadOfReusingAnEarlierPass()
    {
        FrontierAccountSnapshot account = Account(tank: 1000, stock: 0);
        RouteWorkspaceViewModel model = await Create(() => account);
        await Import(model);
        model.SaveAsName = "Fuel test";
        await model.ConfirmSaveAsAsync();
        FollowRouteDocument saved = model.CreateSnapshot()!;
        await model.ApplyJournalEventsAsync([
            Event(1, "CarrierJump", "\"MarketID\":1,\"StarSystem\":\"B\",\"StarPos\":[100,0,0]"),
            Event(2, "CarrierDepositFuel", "\"CarrierID\":1,\"Total\":5"),
        ]);
        Assert.False(model.IsFuelWarningVisible);
        await model.LoadSavedRouteAsync(Path.GetFileName(saved.FilePath), false);
        Assert.Equal(2, model.ReachedCount);
        Assert.True(model.IsFuelWarningVisible);
        double remainingShortfall = model.AdditionalTritiumNeeded!.Value;
        await model.ResetAsync();
        Assert.Equal(0, model.ReachedCount);
        Assert.True(model.AdditionalTritiumNeeded > remainingShortfall);
    }

    private async Task<RouteWorkspaceViewModel> Create(
        Func<FrontierAccountSnapshot?> source,
        Results? results = null,
        Func<Task>? refresh = null
    )
    {
        results ??= new Results();
        var model = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(directory, FollowRouteKind.FleetCarrier)),
            new RouteNameImporter(new Resolver()),
            results,
            new RouteWorkspaceOptions(
                FollowRouteKind.FleetCarrier,
                PlannerClient: results,
                CarrierSnapshotSource: source,
                RefreshCarrierSnapshot: refresh
            )
        );
        models.Add(model);
        await model.UpdateContextAsync("F1", "Elsewhere", 99, null);
        return model;
    }

    private static Task Import(RouteWorkspaceViewModel model) =>
        model.ImportSpanshUrlAsync("https://spansh.co.uk/fleet-carrier/results/74FA2952-2048-11F1-8302-B948FF6DF5C1");

    private static SpanshPlannerFieldViewModel Field(RouteWorkspaceViewModel model, string key) =>
        model.SpanshSearch!.Fields.Single(field => field.Definition.Key == key);

    private static FrontierAccountSnapshot Account(int tank = 700, int stock = 500)
    {
        FrontierCarrierSnapshot personal = FrontierCapiSnapshotParser.ParseCarrier(
            JsonSerializer.Serialize(
                new
                {
                    id = 1,
                    name = new { callsign = "OWN-001" },
                    currentStarSystem = "A",
                    fuel = tank,
                    capacity = new { cargoNotForSale = 1000, freeSpace = 24000 },
                    cargo = new[] { new { commodity = "tritium", qty = stock } },
                }
            ),
            Origin
        );
        FrontierCarrierSnapshot squadron = personal with
        {
            Callsign = "SQU-002",
            FuelData = personal.FuelData! with
            {
                Kind = FollowRouteCarrierKind.Squadron,
                Callsign = "SQU-002",
                CarrierId = 2,
                TankTonnes = 900,
                StoredTritiumTonnes = 4000,
                UsedCapacityTonnes = 10000,
            },
        };
        return new(
            "CMDR",
            0,
            0,
            false,
            true,
            "Elsewhere",
            "",
            null,
            [],
            [],
            [],
            personal,
            Origin,
            SquadronCarrier: squadron
        );
    }

    private static JournalEventEnvelope Event(int second, string name, string fields)
    {
        JsonObject json = JsonNode.Parse("{" + fields + "}")!.AsObject();
        json["event"] = name;
        json["timestamp"] = Origin.AddSeconds(second).ToString("O", CultureInfo.InvariantCulture);
        Assert.True(
            JournalEventEnvelope.TryParse(json.ToJsonString(), out JournalEventEnvelope? result, out string? error),
            error
        );
        return result!;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (RouteWorkspaceViewModel model in models)
        {
            await model.FlushWorkspaceAsync();
            model.SpanshSearch!.Dispose();
        }
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class Results : ISpanshRouteClient, ISpanshPlannerClient
    {
        public bool Closed { get; init; }
        public SpanshPlannerRequest? Request { get; private set; }

        public Task<SpanshRouteReference> SubmitAsync(
            SpanshPlannerRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return Task.FromResult(
                new SpanshRouteReference(Guid.Parse("74FA2952-2048-11F1-8302-B948FF6DF5C1"), request.Planner.Kind)
            );
        }

        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        )
        {
            FollowRouteHop first = new("A", 1, new GalacticCoordinate(0, 0, 0), "Keep notes", false, false);
            FollowRouteHop[] hops =
            [
                first,
                new("B", 2, new(100, 0, 0), null, false, false),
                new("C", 3, new(250, 0, 0), null, false, false),
            ];
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>(Closed ? [.. hops, first] : hops);
        }
    }
}
