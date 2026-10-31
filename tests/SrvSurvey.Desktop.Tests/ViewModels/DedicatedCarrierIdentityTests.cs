using System.Globalization;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class DedicatedCarrierIdentityTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Origin = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-carrier-identity-{Guid.NewGuid():N}"
    );
    private readonly List<RouteWorkspaceViewModel> workspaces = [];
    private DateTimeOffset now = Origin;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameDestinationWithoutIdentityCannotAdvanceEitherDedicatedCarrier(bool saved)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync(saved);
        JournalEventEnvelope[] requests = [Request(1, "FleetCarrier"), Request(2, "SquadronCarrier")];
        fc.ApplyFleetCarrierJumpEvents(requests);
        sc.ApplyFleetCarrierJumpEvents(requests);
        Assert.Equal("B", fc.NextHopName);
        Assert.Equal("B", sc.NextHopName);
        JournalEventEnvelope arrival = Event("CarrierJump", "\"StarSystem\":\"B\",\"StationType\":\"FleetCarrier\"");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);

        Assert.Equal(1, fc.ReachedCount);
        Assert.Equal(1, sc.ReachedCount);
        Assert.Equal(FleetCarrierJumpCountdownKind.Scheduled, fc.CarrierJumpKind);
        Assert.Equal(FleetCarrierJumpCountdownKind.Scheduled, sc.CarrierJumpKind);
    }

    [Theory]
    [InlineData("\"MarketID\":1", false)]
    [InlineData("\"CarrierID\":2", true)]
    [InlineData("\"CarrierType\":\"FleetCarrier\"", false)]
    [InlineData("\"CarrierType\":\"SquadronCarrier\"", true)]
    [InlineData("\"Callsign\":\"own-001\"", false)]
    [InlineData("\"Callsign\":\"SQU-002\"", true)]
    [InlineData("\"StationName\":\"OWN-001\"", false)]
    [InlineData("\"StationName\":\"squ-002\"", true)]
    public async Task PositiveIdentityAdvancesOnlyItsOwnRouteAndCountdown(string identity, bool squadron)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync();
        JournalEventEnvelope arrival = Event("CarrierJump", identity + ",\"StarSystem\":\"B\"");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);

        RouteWorkspaceViewModel own = squadron ? sc : fc;
        RouteWorkspaceViewModel other = squadron ? fc : sc;
        Assert.Equal(2, own.ReachedCount);
        Assert.Equal(1, other.ReachedCount);
        Assert.Equal(FleetCarrierJumpCountdownKind.PostJumpCooldown, own.CarrierJumpKind);
        Assert.Equal(FleetCarrierJumpCountdownKind.None, other.CarrierJumpKind);
    }

    [Theory]
    [InlineData("\"MarketID\":1,\"StationName\":\"OTH-999\"")]
    [InlineData("\"MarketID\":2,\"Callsign\":\"OWN-001\"")]
    [InlineData("\"MarketID\":1,\"CarrierType\":\"SquadronCarrier\"")]
    [InlineData("\"CarrierType\":\"FleetCarrier\",\"StationName\":\"SQU-002\"")]
    [InlineData("\"CarrierType\":\"SquadronCarrier\",\"Callsign\":\"OWN-001\"")]
    [InlineData("\"MarketID\":999")]
    [InlineData("\"CarrierType\":\"UnknownCarrier\"")]
    [InlineData("\"StationName\":\"OTH-999\"")]
    public async Task UnrelatedOrContradictoryIdentityCannotChangeEitherCarrier(string identity)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync();
        JournalEventEnvelope arrival = Event("CarrierJump", identity + ",\"StarSystem\":\"B\"");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);

        Assert.Equal(1, fc.ReachedCount);
        Assert.Equal(1, sc.ReachedCount);
        Assert.Equal(FleetCarrierJumpCountdownKind.None, fc.CarrierJumpKind);
        Assert.Equal(FleetCarrierJumpCountdownKind.None, sc.CarrierJumpKind);
    }

    [Theory]
    [InlineData("FleetCarrier", false)]
    [InlineData("SquadronCarrier", true)]
    public async Task ExplicitKnownTypeCanIdentifyAnArrivalWithoutCapiOrPriorStats(string type, bool squadron)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync(knownIdentities: false);
        JournalEventEnvelope arrival = Event("CarrierJump", $"\"CarrierType\":\"{type}\",\"StarSystem\":\"B\"");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);

        Assert.Equal(squadron ? 1 : 2, fc.ReachedCount);
        Assert.Equal(squadron ? 2 : 1, sc.ReachedCount);
    }

    [Theory]
    [InlineData("\"MarketID\":1")]
    [InlineData("\"Callsign\":\"OWN-001\"")]
    [InlineData("\"StationName\":\"OWN-001\"")]
    public async Task ATypeCannotClaimTheOtherKnownCarrierWhenItsOwnIdentityIsUnknown(string identity)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync(knownIdentities: false);
        JournalEventEnvelope personal = Event(
            "CarrierStats",
            "\"CarrierID\":1,\"CarrierType\":\"FleetCarrier\",\"Callsign\":\"OWN-001\""
        );
        fc.ApplyFleetCarrierJumpEvents([personal]);
        sc.ApplyFleetCarrierJumpEvents([personal]);
        JournalEventEnvelope arrival = Event(
            "CarrierJump",
            identity + ",\"CarrierType\":\"SquadronCarrier\",\"StarSystem\":\"B\""
        );

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);

        Assert.Equal(1, fc.ReachedCount);
        Assert.Equal(1, sc.ReachedCount);
        Assert.Equal(FleetCarrierJumpCountdownKind.None, fc.CarrierJumpKind);
        Assert.Equal(FleetCarrierJumpCountdownKind.None, sc.CarrierJumpKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"CarrierType\":\"UnknownCarrier\"")]
    public async Task AnUnidentifiedOrUnsupportedRequestCannotTeachCarrierOwnership(string identity)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync(knownIdentities: false);
        JournalEventEnvelope request = Event(
            "CarrierJumpRequest",
            $"\"CarrierID\":999,\"SystemName\":\"B\",\"DepartureTime\":\"{Origin.AddMinutes(15):O}\"" + identity
        );
        fc.ApplyFleetCarrierJumpEvents([request]);
        sc.ApplyFleetCarrierJumpEvents([request]);
        JournalEventEnvelope arrival = Event("CarrierJump", "\"MarketID\":999,\"StarSystem\":\"B\"");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);

        Assert.Equal(1, fc.ReachedCount);
        Assert.Equal(1, sc.ReachedCount);
        Assert.Equal(FleetCarrierJumpCountdownKind.None, fc.CarrierJumpKind);
        Assert.Equal(FleetCarrierJumpCountdownKind.None, sc.CarrierJumpKind);
    }

    [Theory]
    [InlineData("scheduled")]
    [InlineData("canceled")]
    [InlineData("stale")]
    public async Task ARequestWithoutArrivalIdentityCannotAuthorizeProgressOrCountdownChanges(string scenario)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync();
        JournalEventEnvelope request = Request(1, "FleetCarrier");
        fc.ApplyFleetCarrierJumpEvents([request]);
        sc.ApplyFleetCarrierJumpEvents([request]);
        if (scenario == "canceled")
        {
            now = Origin.AddSeconds(1);
            JournalEventEnvelope cancellation = Event("CarrierJumpCancelled", "\"CarrierID\":1");
            fc.ApplyFleetCarrierJumpEvents([cancellation]);
            sc.ApplyFleetCarrierJumpEvents([cancellation]);
        }
        else if (scenario == "stale")
        {
            now = Origin.AddHours(1);
            fc.RefreshCarrierJumpCountdown();
            sc.RefreshCarrierJumpCountdown();
        }
        FleetCarrierJumpCountdownKind personalKind = fc.CarrierJumpKind;
        FleetCarrierJumpCountdownKind squadronKind = sc.CarrierJumpKind;
        JournalEventEnvelope arrival = Event("CarrierJump", "\"StarSystem\":\"B\"");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);

        Assert.Equal(1, fc.ReachedCount);
        Assert.Equal(1, sc.ReachedCount);
        Assert.Equal(personalKind, fc.CarrierJumpKind);
        Assert.Equal(squadronKind, sc.CarrierJumpKind);
    }

    [Theory]
    [InlineData("CarrierJump")]
    [InlineData("CarrierLocation")]
    public async Task AnAmbiguousArrivalCannotMoveOrDebitEitherCarrierFuelSnapshot(string eventName)
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync();
        PrepareFuelOriginsAndRequests(fc, sc);
        now = Origin.AddMinutes(16);
        JournalEventEnvelope arrival = Event(eventName, "\"StarSystem\":\"B\",\"StarPos\":[100,0,0]");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);
        fc.PrepareCarrierSearch();
        sc.PrepareCarrierSearch();

        Assert.Equal("A", Field(fc, "source"));
        Assert.Equal("A", Field(sc, "source"));
        Assert.Equal("700", Field(fc, "fuel_loaded"));
        Assert.Equal("900", Field(sc, "fuel_loaded"));
        Assert.Equal(1, fc.ReachedCount);
        Assert.Equal(1, sc.ReachedCount);
    }

    [Theory]
    [InlineData("CarrierJump", "\"MarketID\":1", false, true)]
    [InlineData("CarrierJump", "\"CarrierType\":\"SquadronCarrier\"", true, true)]
    [InlineData("CarrierJump", "\"Callsign\":\"OWN-001\"", false, true)]
    [InlineData("CarrierJump", "\"StationName\":\"SQU-002\"", true, true)]
    [InlineData("CarrierLocation", "\"CarrierID\":1", false, true)]
    [InlineData("CarrierLocation", "\"CarrierType\":\"SquadronCarrier\"", true, true)]
    [InlineData("CarrierLocation", "\"Callsign\":\"OWN-001\"", false, true)]
    [InlineData("CarrierLocation", "\"StationName\":\"SQU-002\"", true, true)]
    [InlineData("CarrierJump", "\"CarrierType\":\"SquadronCarrier\"", true, false)]
    [InlineData("CarrierJump", "\"Callsign\":\"OWN-001\"", false, false)]
    [InlineData("CarrierLocation", "\"CarrierType\":\"SquadronCarrier\"", true, false)]
    [InlineData("CarrierLocation", "\"StationName\":\"SQU-002\"", true, false)]
    public async Task PositiveMovementIdentityChangesOnlyItsOwnLocationAndFuel(
        string eventName,
        string identity,
        bool squadron,
        bool scheduled
    )
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync();
        PrepareFuelOriginsAndRequests(fc, sc, scheduled);
        now = Origin.AddMinutes(16);
        JournalEventEnvelope arrival = Event(eventName, identity + ",\"StarSystem\":\"B\",\"StarPos\":[100,0,0]");

        await fc.ApplyJournalEventsAsync([arrival]);
        await sc.ApplyJournalEventsAsync([arrival]);
        fc.PrepareCarrierSearch();
        sc.PrepareCarrierSearch();

        RouteWorkspaceViewModel own = squadron ? sc : fc;
        RouteWorkspaceViewModel other = squadron ? fc : sc;
        Assert.Equal("B", Field(own, "source"));
        Assert.NotEqual(squadron ? "900" : "700", Field(own, "fuel_loaded"));
        Assert.Equal("A", Field(other, "source"));
        Assert.Equal(squadron ? "700" : "900", Field(other, "fuel_loaded"));
    }

    private void PrepareFuelOriginsAndRequests(
        RouteWorkspaceViewModel fc,
        RouteWorkspaceViewModel sc,
        bool scheduled = true
    )
    {
        now = Origin.AddSeconds(1);
        JournalEventEnvelope[] locations =
        [
            Event("CarrierLocation", "\"CarrierID\":1,\"StarSystem\":\"A\",\"StarPos\":[0,0,0]"),
            Event("CarrierLocation", "\"CarrierID\":2,\"StarSystem\":\"A\",\"StarPos\":[0,0,0]"),
        ];
        fc.ApplyFleetCarrierJumpEvents(locations);
        sc.ApplyFleetCarrierJumpEvents(locations);
        fc.PrepareCarrierSearch();
        sc.PrepareCarrierSearch();
        Assert.Equal("A", Field(fc, "source"));
        Assert.Equal("A", Field(sc, "source"));
        Assert.Equal("700", Field(fc, "fuel_loaded"));
        Assert.Equal("900", Field(sc, "fuel_loaded"));
        if (scheduled)
        {
            JournalEventEnvelope[] requests = [Request(1, "FleetCarrier"), Request(2, "SquadronCarrier")];
            fc.ApplyFleetCarrierJumpEvents(requests);
            sc.ApplyFleetCarrierJumpEvents(requests);
        }
    }

    private static string Field(RouteWorkspaceViewModel workspace, string key) =>
        workspace.SpanshSearch!.Fields.Single(field => field.Definition.Key == key).Value;

    [Fact]
    public async Task FirstBatchMovementUsesTheReplayedIdentityBeforeTheWorkspaceHasAFuelSnapshot()
    {
        (RouteWorkspaceViewModel fc, RouteWorkspaceViewModel sc) = await CreatePairAsync(knownIdentities: false);
        JournalEventEnvelope[] stats = KnownCarrierStats();
        now = Origin.AddSeconds(1);
        JournalEventEnvelope[] locations =
        [
            Event("CarrierLocation", "\"CarrierID\":1,\"StarSystem\":\"A\",\"StarPos\":[0,0,0]"),
            Event("CarrierLocation", "\"CarrierID\":2,\"StarSystem\":\"A\",\"StarPos\":[0,0,0]"),
        ];
        now = Origin.AddMinutes(16);
        JournalEventEnvelope arrival = Event(
            "CarrierJump",
            "\"Callsign\":\"OWN-001\",\"StarSystem\":\"B\",\"StarPos\":[100,0,0]"
        );
        JournalEventEnvelope[] batch = [.. stats, .. locations, arrival];

        await fc.ApplyJournalEventsAsync(batch);
        await sc.ApplyJournalEventsAsync(batch);
        fc.PrepareCarrierSearch();
        sc.PrepareCarrierSearch();

        Assert.Equal("B", Field(fc, "source"));
        Assert.NotEqual("700", Field(fc, "fuel_loaded"));
        Assert.Equal("A", Field(sc, "source"));
        Assert.Equal("900", Field(sc, "fuel_loaded"));
        Assert.Equal(2, fc.ReachedCount);
        Assert.Equal(1, sc.ReachedCount);
    }

    private async Task<(RouteWorkspaceViewModel Personal, RouteWorkspaceViewModel Squadron)> CreatePairAsync(
        bool saved = false,
        bool knownIdentities = true
    )
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        RouteWorkspaceViewModel fc = await CreateAsync(library, FollowRouteCarrierKind.Personal, saved);
        RouteWorkspaceViewModel sc = await CreateAsync(library, FollowRouteCarrierKind.Squadron, saved);
        if (knownIdentities)
        {
            JournalEventEnvelope[] stats = KnownCarrierStats();
            fc.ApplyFleetCarrierJumpEvents(stats);
            sc.ApplyFleetCarrierJumpEvents(stats);
        }
        return (fc, sc);
    }

    private JournalEventEnvelope[] KnownCarrierStats() =>
        [
            Event(
                "CarrierStats",
                "\"CarrierID\":1,\"CarrierType\":\"FleetCarrier\",\"Callsign\":\"OWN-001\",\"FuelLevel\":700,\"SpaceUsage\":{\"TotalCapacity\":25000,\"FreeSpace\":24000}"
            ),
            Event(
                "CarrierStats",
                "\"CarrierID\":2,\"CarrierType\":\"SquadronCarrier\",\"Callsign\":\"SQU-002\",\"FuelLevel\":900,\"SpaceUsage\":{\"TotalCapacity\":60000,\"FreeSpace\":50000}"
            ),
        ];

    private async Task<RouteWorkspaceViewModel> CreateAsync(
        FollowRouteStore library,
        FollowRouteCarrierKind kind,
        bool saved
    )
    {
        var routes = new Routes();
        var model = new RouteWorkspaceViewModel(
            new FollowRouteService(library.ForCarrier(kind)),
            new RouteNameImporter(new Resolver()),
            routes,
            new RouteWorkspaceOptions(
                FollowRouteKind.FleetCarrier,
                UtcNow: () => now,
                PlannerClient: routes,
                DedicatedCarrier: kind
            )
        );
        workspaces.Add(model);
        await model.UpdateContextAsync("F1", "A", 1, null);
        await model.ImportNamesAsync(["A", "B", "C"]);
        model.SetProgressThrough(0, true);
        await model.FlushWorkspaceAsync();
        if (saved)
        {
            model.SaveAsName = kind.ToString();
            await model.ConfirmSaveAsAsync();
        }
        return model;
    }

    private JournalEventEnvelope Request(long carrierId, string type) =>
        Event(
            "CarrierJumpRequest",
            $"\"CarrierID\":{carrierId},\"CarrierType\":\"{type}\",\"SystemName\":\"B\",\"DepartureTime\":\"{Origin.AddMinutes(15):O}\""
        );

    private JournalEventEnvelope Event(string name, string fields)
    {
        JsonObject json = JsonNode.Parse("{" + fields + "}")!.AsObject();
        json["event"] = name;
        json["timestamp"] = now.ToString("O", CultureInfo.InvariantCulture);
        Assert.True(
            JournalEventEnvelope.TryParse(json.ToJsonString(), out JournalEventEnvelope? entry, out string? error),
            error
        );
        return entry!;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (RouteWorkspaceViewModel model in workspaces)
        {
            await model.FlushWorkspaceAsync();
            model.SpanshSearch?.Dispose();
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

    private sealed class Routes : ISpanshRouteClient, ISpanshPlannerClient
    {
        public Task<SpanshRouteReference> SubmitAsync(
            SpanshPlannerRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new SpanshRouteReference(Guid.Empty, request.Planner.Kind));

        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
    }
}
