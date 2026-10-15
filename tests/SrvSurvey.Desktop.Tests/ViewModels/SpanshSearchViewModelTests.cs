using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SpanshSearchViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-Spansh-" + Guid.NewGuid().ToString("N")
    );
    private readonly List<SpanshSearchViewModel> searches = [];

    [Fact]
    public async Task SearchLoadsAutomaticallyAsTheSameActiveUnsavedItineraryAsClipboardImport()
    {
        var client = new Planner();
        RouteWorkspaceViewModel route = await CreateRoute(client);
        SpanshSearchViewModel search = route.SpanshSearch!;
        Assert.Equal("Spansh Search", search.Title);
        Assert.Equal(6, search.Planners.Count);
        Assert.Equal("Sol", Field(search, "from").Value);
        Field(search, "to").Value = "Achenar";
        Field(search, "range").Number = 50.5m;
        Assert.True(await search.SearchAsync());
        Assert.Equal(2, route.RouteCount);
        Assert.True(route.IsActive);
        Assert.Equal("Achenar", route.NextHopName);
        Assert.False(route.HasSavedRoute);
        Assert.Equal(SpanshRouteKind.Neutron, route.CreateSnapshot()!.SourceSpanshKind);
        Assert.Equal("50.5", client.Request!.Parameters.Single(pair => pair.Key == "range").Value);
        Assert.Contains("Save As", search.Status);
        Assert.False(search.IsBusy);
        Assert.Empty(await new FollowRouteStore(directory).ListAsync("F1"));
    }

    [Fact]
    public async Task PlannerSwitchesPreserveInputsAndAutofillTheLiveCurrentShip()
    {
        var ship = new CurrentShipExportViewModel();
        RouteWorkspaceViewModel route = await CreateRoute(new Planner(), ship: ship);
        SpanshSearchViewModel search = route.SpanshSearch!;
        SpanshPlannerDefinition neutron = search.SelectedPlanner;
        Field(search, "to").Value = "Achenar";
        search.SelectedPlanner = search.Planners.Single(planner => planner.Kind == SpanshRouteKind.Galaxy);
        Assert.True(search.IsGalaxy);
        Assert.False(search.CanSearch);
        Assert.True(Field(search, "ship_build").IsReadOnly);
        JournalSessionState session = Session();
        ship.Update(session, new EliteStatus { Flags = StatusFlags.InMainShip }, false);
        Assert.True(search.CanSearch);
        Assert.Equal(ship.SlefJson, Field(search, "ship_build").Value);
        Assert.Contains("Surveyor", search.ShipDescription);
        Assert.Contains("ready", search.ShipStatus);
        search.UseCurrentShip = false;
        Assert.False(Field(search, "ship_build").IsReadOnly);
        Field(search, "ship_build").Value = "[]";
        ship.Update(session, new EliteStatus { Flags = StatusFlags.InSrv }, false);
        Assert.Equal("[]", Field(search, "ship_build").Value);
        Assert.True(search.CanSearch);
        search.UseCurrentShip = true;
        Assert.False(search.CanSearch);
        Assert.Empty(Field(search, "ship_build").Value);
        Assert.Contains("Loadout", search.ShipStatus);
        search.SelectedPlanner = neutron;
        Assert.Equal("Achenar", Field(search, "to").Value);
        search.SelectedPlanner = SpanshPlannerCatalog.CarrierPlanners[0];
        Assert.Same(neutron, search.SelectedPlanner);
    }

    [Fact]
    public async Task CarrierWorkspaceOffersOnlyCarrierPlannerAndConditionalFuelControls()
    {
        var client = new Planner();
        RouteWorkspaceViewModel route = await CreateRoute(client, FollowRouteKind.FleetCarrier);
        SpanshSearchViewModel search = route.SpanshSearch!;
        Assert.Single(search.Planners);
        Assert.Equal("Spansh Search · Fleet carrier", search.Title);
        Assert.False(search.IsGalaxy);
        Assert.False(Field(search, "refuel_destinations").IsVisible);
        Assert.True(Field(search, "destinations").SystemList!.ShowRefuel);
        Assert.False(Field(search, "fuel_loaded").IsVisible);
        Field(search, "calculate_starting_fuel").IsChecked = false;
        Assert.False(Field(search, "destinations").SystemList!.ShowRefuel);
        Assert.False(Field(search, "refuel_destinations").IsVisible);
        Assert.True(Field(search, "fuel_loaded").IsVisible);
        Field(search, "destinations").Value = "Achenar";
        Field(search, "carrier_type").SelectedChoice = Field(search, "carrier_type").Choices[1];
        Assert.Equal(60000m, Field(search, "capacity_used").Maximum);
        Assert.Equal("Squadron Carrier", Field(search, "carrier_type").SelectedChoice!.Name);
        Assert.True(await search.SearchAsync());
        Assert.Equal(SpanshRouteKind.FleetCarrier, route.CreateSnapshot()!.SourceSpanshKind);
        Assert.Equal("15000", client.Request!.Parameters.Single(pair => pair.Key == "mass").Value);
    }

    [Fact]
    public async Task InvalidInputNeverSubmitsAndManualBuildJsonErrorsAreVisible()
    {
        var client = new Planner();
        RouteWorkspaceViewModel route = await CreateRoute(client);
        SpanshSearchViewModel search = route.SpanshSearch!;
        Assert.False(await search.SearchAsync());
        Assert.Null(client.Request);
        Assert.Contains("required", search.Status);
        search.SelectedPlanner = search.Planners.Single(planner => planner.Kind == SpanshRouteKind.Galaxy);
        search.UseCurrentShip = false;
        Field(search, "destination").Value = "Achenar";
        Field(search, "ship_build").Value = "bad json";
        Assert.False(await search.SearchAsync());
        Assert.Null(client.Request);
        Assert.False(search.IsBusy);
        await route.UpdateContextAsync(null, null, null, null);
        Assert.False(await search.SearchAsync());
    }

    [Theory]
    [InlineData("error")]
    [InlineData("empty")]
    [InlineData("cancel")]
    [InlineData("commander")]
    public async Task FailureCancellationAndCommanderChangesCannotReplaceAnExistingItinerary(string scenario)
    {
        var client = new Planner();
        var results = new Results();
        RouteWorkspaceViewModel route = await CreateRoute(client, results: results);
        await route.ImportSpanshUrlAsync("https://spansh.co.uk/plotter/results/74FA2952-2048-11F1-8302-B948FF6DF5C1");
        SpanshSearchViewModel search = route.SpanshSearch!;
        Field(search, "to").Value = "Achenar";
        Field(search, "range").Value = "50";
        client.Error = scenario == "error";
        results.Empty = scenario == "empty";
        if (scenario is "cancel" or "commander")
        {
            client.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        Task<bool> task = search.SearchAsync();
        if (client.Gate is not null)
        {
            Assert.True(search.IsBusy);
            Assert.False(search.CanSearch);
            search.SelectedPlanner = search.Planners[1];
            Assert.Equal(SpanshRouteKind.Neutron, search.SelectedPlanner.Kind);
            if (scenario == "commander")
            {
                await route.UpdateContextAsync("F2", "Other", 300, null);
            }
            else
            {
                search.Cancel();
            }
            client.Gate.SetResult();
        }
        Assert.False(await task);
        Assert.False(route.IsBusy);
        Assert.False(search.IsBusy);
        if (scenario == "commander")
        {
            Assert.False(route.HasRoute);
        }
        else
        {
            Assert.Equal(["Sol", "Achenar"], route.Hops.Select(hop => hop.Name));
        }
        Assert.True(search.Status.Length > 0);
    }

    [Fact]
    public async Task CrossWorkspacePlannerRequestsAreRejected()
    {
        var client = new Planner();
        RouteWorkspaceViewModel route = await CreateRoute(client);
        Assert.False(
            await route.SearchSpanshAsync(
                client,
                new(SpanshPlannerCatalog.CarrierPlanners[0], []),
                CancellationToken.None
            )
        );
        Assert.Null(client.Request);
    }

    [Fact]
    public void FieldControlsUseCorrectTypesAndNumericFormatting()
    {
        var field = new SpanshPlannerFieldViewModel(
            new("range", "Range", SpanshPlannerFieldType.Number, Required: true, Minimum: 10, Maximum: 100)
        );
        Assert.True(field.IsNumber);
        Assert.False(field.IsText);
        Assert.Equal("Range", field.Label);
        Assert.True(field.IsRequired);
        Assert.Null(field.Number);
        field.Number = 12.5m;
        Assert.Equal("12.5", field.Value);
        Assert.Equal(12.5m, field.Number);
        field.Number = null;
        Assert.Empty(field.Value);
        Assert.Equal(10m, field.Minimum);
        Assert.Equal(100m, field.Maximum);
        Assert.False(field.HasHelp);
        Assert.Empty(field.Choices);
        Assert.Null(field.SelectedChoice);
        field.SelectedChoice = null;
        var systems = new SpanshPlannerFieldViewModel(
            new("via", "Via", SpanshPlannerFieldType.Systems, Help: "One per line")
        );
        Assert.True(systems.IsSystems);
        Assert.False(systems.IsText);
        Assert.False(systems.IsMultiline);
        Assert.True(systems.HasHelp);
        Assert.Equal("One per line", systems.Help);
        Assert.Equal(decimal.MaxValue, systems.Maximum);
        Assert.Equal(0m, systems.Minimum);
        var system = new SpanshPlannerFieldViewModel(new("source", "Source", SpanshPlannerFieldType.System));
        Assert.True(system.IsSystem);
        var flag = new SpanshPlannerFieldViewModel(new("flag", "Flag", SpanshPlannerFieldType.Boolean, "1"));
        Assert.True(flag.IsBoolean);
        Assert.True(flag.IsChecked);
        flag.IsChecked = false;
        Assert.Equal("0", flag.Value);
        flag.IsChecked = true;
        Assert.Equal("1", flag.Value);
        var choice = new SpanshPlannerFieldViewModel(new("choice", "Choice", SpanshPlannerFieldType.Choice));
        Assert.True(choice.IsChoice);
    }

    [Fact]
    public async Task DestinationEditorWritesCarrierRefuelWaypointsAndNeverDropsPendingInput()
    {
        var client = new Planner();
        RouteWorkspaceViewModel route = await CreateRoute(client, FollowRouteKind.FleetCarrier);
        SpanshSearchViewModel search = route.SpanshSearch!;
        SpanshSystemListViewModel list = Field(search, "destinations").SystemList!;
        list.Draft = "Achenar";
        Assert.False(await search.SearchAsync());
        Assert.Null(client.Request);
        Assert.Contains("Add or save", search.Status);
        list.CommitCommand.Execute(null);
        list.Rows[0].Refuel = true;
        Assert.Equal("Achenar", Field(search, "refuel_destinations").Value);
        list.Rows[0].EditCommand.Execute(null);
        Assert.False(await search.SearchAsync());
        list.Draft = "Vega";
        list.CommitCommand.Execute(null);
        Assert.Equal("Vega", Field(search, "refuel_destinations").Value);
        Field(search, "refuel_destinations").Value = "";
        Assert.False(list.Rows[0].Refuel);
        list.Rows[0].Refuel = true;
        list.Draft = "Colonia";
        list.CommitCommand.Execute(null);
        Assert.True(await search.SearchAsync());
        Assert.Equal(
            ["Vega", "Colonia"],
            client.Request!.Parameters.Where(pair => pair.Key == "destinations").Select(pair => pair.Value)
        );
        Assert.Equal("Vega", client.Request.Parameters.Single(pair => pair.Key == "refuel_destinations").Value);
        list.Rows[0].RemoveCommand.Execute(null);
        Assert.Empty(Field(search, "refuel_destinations").Value);
    }

    public void Dispose()
    {
        foreach (SpanshSearchViewModel search in searches)
        {
            search.Dispose();
        }
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private async Task<RouteWorkspaceViewModel> CreateRoute(
        Planner client,
        FollowRouteKind kind = FollowRouteKind.Standard,
        CurrentShipExportViewModel? ship = null,
        Results? results = null
    )
    {
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(directory, kind)),
            new RouteNameImporter(new Resolver()),
            results ?? new Results(),
            kind,
            plannerClient: client,
            currentShip: ship
        );
        searches.Add(route.SpanshSearch!);
        await route.UpdateContextAsync("F1", "Sol", 100, new(0, 0, 0));
        return route;
    }

    private static SpanshPlannerFieldViewModel Field(SpanshSearchViewModel search, string key) =>
        search.Fields.Single(field => field.Definition.Key == key);

    private static JournalSessionState Session()
    {
        var session = new JournalSessionState();
        session.Apply(Parse("""{"event":"LoadGame","FID":"F1","Ship":"sidewinder","ShipID":1}"""));
        session.Apply(
            Parse(
                """{"event":"Loadout","Ship":"sidewinder","ShipID":1,"ShipName":"Surveyor","UnladenMass":25,"FuelCapacity":{"Main":4,"Reserve":0.3},"Modules":[{"Slot":"FrameShiftDrive","Item":"Int_Hyperdrive_Size2_Class5"}]}"""
            )
        );
        return session;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }

    private sealed class Planner : ISpanshPlannerClient
    {
        public SpanshPlannerRequest? Request { get; private set; }
        public TaskCompletionSource? Gate { get; set; }
        public bool Error { get; set; }

        public async Task<SpanshRouteReference> SubmitAsync(
            SpanshPlannerRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            if (Gate is not null)
            {
                await Gate.Task;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Error)
            {
                throw new HttpRequestException("Planner unavailable");
            }
            return new(Guid.Parse("74FA2952-2048-11F1-8302-B948FF6DF5C1"), request.Planner.Kind);
        }
    }

    private sealed class Results : ISpanshRouteClient
    {
        public bool Empty { get; set; }

        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<IReadOnlyList<FollowRouteHop>>(
                Empty
                    ? []
                    :
                    [
                        new("Sol", 100, new(0, 0, 0), null, false, false),
                        new("Achenar", 200, new(1, 2, 3), null, false, false),
                    ]
            );
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
