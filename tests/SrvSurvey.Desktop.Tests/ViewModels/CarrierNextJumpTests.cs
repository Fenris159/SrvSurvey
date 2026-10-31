using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class CarrierNextJumpTests
{
    private static readonly int[] DestinationPosition = [500, 0, 0];
    private static readonly int[] OriginPosition = [0, 0, 0];

    [Fact]
    public async Task SameSystemRepositioningConsumesFuelOnceAndLaterStatsReplaceTheEstimate()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        await fixture.RequestAsync(destination: "Origin");
        Assert.Equal(0, fixture.Route.CarrierNextJumpDistance);
        Assert.Equal(5, fixture.Route.CarrierNextJumpFuel);
        fixture.Now = fixture.Now.AddMinutes(15).AddSeconds(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Origin",
                }
            ),
        ]);
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.Equal("COOLDOWN", overlay.HopProgress);
        Assert.Equal("255 t", overlay.FuelLeft);
        Assert.Equal("Fuel left (est.)", overlay.FuelLeftLabel);
        fixture.Now = fixture.Now.AddSeconds(10);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Origin",
                }
            ),
        ]);
        Assert.Equal(255, fixture.Route.CarrierDepotFuel);
        fixture.Now = fixture.Now.AddMinutes(6);
        fixture.Route.RefreshCarrierJumpCountdown();
        await fixture.RequestAsync();
        Assert.Equal(255, fixture.Route.CarrierDepotFuel);
        Assert.Equal("174 t", overlay.FuelLeft);
        await fixture.SeedFuelAsync(500);
        Assert.Equal(500, fixture.Route.CarrierDepotFuel);
        Assert.False(fixture.Route.CarrierFuelIsEstimated);
    }

    [Fact]
    public async Task IdlessCarrierArrivalUpdatesFuelAndOriginForTheNextJump()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        await fixture.RequestAsync();
        fixture.Now = fixture.Now.AddMinutes(15).AddSeconds(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierJump",
                    StarSystem = "Destination",
                    StarPos = DestinationPosition,
                }
            ),
        ]);
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.Equal("179 t", overlay.FuelLeft);
        Assert.Equal("Fuel left (est.)", overlay.FuelLeftLabel);
        fixture.Now = fixture.Now.AddMinutes(6);
        fixture.Route.RefreshCarrierJumpCountdown();
        await fixture.RequestAsync(destination: "Destination 2");
        Assert.Equal(500, fixture.Route.CarrierNextJumpDistance);
        Assert.Equal("99 t", overlay.FuelLeft);
    }

    [Fact]
    public async Task UntrackedCarrierMovementInvalidatesThePreviousArrivalFuelEstimate()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        await fixture.RequestAsync();
        fixture.Now = fixture.Now.AddMinutes(15).AddSeconds(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Destination",
                }
            ),
        ]);
        Assert.Equal(179, fixture.Route.CarrierDepotFuel);
        fixture.Now = fixture.Now.AddMinutes(6);
        fixture.Route.RefreshCarrierJumpCountdown();
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Untracked",
                }
            ),
        ]);
        fixture.Now = fixture.Now.AddMinutes(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Destination",
                }
            ),
        ]);
        await fixture.RequestAsync(destination: "Destination 2");
        Assert.Null(fixture.Route.CarrierDepotFuel);
        Assert.Null(fixture.Route.CarrierNextJumpFuel);
    }

    [Fact]
    public async Task EstimatedDepotFuelCarriesAcrossSuccessiveJumpsWithoutCoordinates()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        await fixture.RequestAsync();
        fixture.Now = fixture.Now.AddMinutes(15).AddSeconds(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Destination",
                }
            ),
        ]);
        fixture.Now = fixture.Now.AddMinutes(6);
        fixture.Route.RefreshCarrierJumpCountdown();
        await fixture.RequestAsync(destination: "Destination 2");
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.Equal("99 t", overlay.FuelLeft);
        fixture.Now = fixture.Now.AddMinutes(15).AddSeconds(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Destination 2",
                }
            ),
        ]);
        Assert.Equal("99 t", overlay.FuelLeft);
        Assert.Equal("Fuel left (est.)", overlay.FuelLeftLabel);
    }

    [Fact]
    public async Task LaterFuelObservationsPopulateAnAlreadyScheduledJump()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.False(overlay.HasJumpFuel);
        await fixture.SeedFuelAsync(260);
        await fixture.Route.CarrierDestinationLookup;
        Assert.Equal("81 t", overlay.JumpFuel);
        Assert.Equal("179 t", overlay.FuelLeft);
    }

    [Theory]
    [InlineData("Planned destination", true)]
    [InlineData("Destination", false)]
    public async Task ActualJumpCanOverrideAnOffRouteDestinationWithoutChangingTheRoute(string planned, bool passive)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        await fixture.Route.ImportNamesAsync(["Origin", planned]);
        Assert.True(fixture.Route.IsActive);
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.Equal(passive, overlay.IsPassive);
        Assert.Equal("Destination", overlay.SystemName);
        Assert.Equal(planned, fixture.Route.NextHopName);
        fixture.Route.ShowCarrierNextJumpInformation = false;
        Assert.True(overlay.ShouldShow);
        Assert.False(overlay.IsPassive);
        Assert.Equal(planned, overlay.SystemName);
        Assert.Equal(2, fixture.Route.RouteCount);
    }

    [Fact]
    public async Task ArrivalWithoutCoordinatesRetainsThePreviouslyCalculatedFuelEstimate()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        fixture.Now = fixture.Now.AddMinutes(15).AddSeconds(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Destination",
                }
            ),
        ]);
        Assert.Equal("COOLDOWN", overlay.HopProgress);
        Assert.Equal("179 t", overlay.FuelLeft);
        Assert.Equal("Fuel left (est.)", overlay.FuelLeftLabel);
        Assert.False(overlay.HasRestockWarning);
    }

    [Fact]
    public async Task OnlineOriginAndDestinationResolutionCanSupplyMissingCoordinates()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierStats",
                    CarrierID = 1,
                    CarrierType = "FleetCarrier",
                    Callsign = "OWN-001",
                    FuelLevel = 260,
                    SpaceUsage = new
                    {
                        TotalCapacity = 25000,
                        FreeSpace = 20000,
                        Cargo = 0,
                    },
                }
            ),
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Origin",
                }
            ),
            fixture.Entry(
                new
                {
                    @event = "CarrierJumpRequest",
                    CarrierID = 1,
                    SystemName = "Destination",
                    DepartureTime = fixture.Now.AddMinutes(15),
                }
            ),
        ]);
        await fixture.Route.CarrierDestinationLookup;
        Assert.Equal(500, fixture.Route.CarrierNextJumpDistance);
        Assert.Equal(81, fixture.Route.CarrierNextJumpFuel);
    }

    [Theory]
    [InlineData(260, true)]
    [InlineData(280, true)]
    [InlineData(281, false)]
    [InlineData(500, false)]
    public async Task PassiveJumpUsesJournalFuelAndPublicDestinationWithoutAPlannedRoute(int fuel, bool low)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(fuel);
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.False(fixture.Route.HasRoute);
        Assert.True(overlay.ShouldShow);
        Assert.True(overlay.IsPassive);
        Assert.Equal("Destination", overlay.SystemName);
        Assert.Equal("SCHEDULED", overlay.HopProgress);
        Assert.Equal("FLEET CARRIER NEXT JUMP", overlay.Header);
        Assert.Contains("500", overlay.JumpSummary);
        Assert.Equal("81 t", overlay.JumpFuel);
        Assert.Equal($"{fuel - 81:N0} t", overlay.FuelLeft);
        Assert.True(overlay.HasDepotFuel);
        Assert.Equal($"{fuel:N0} t", overlay.DepotFuel);
        Assert.Equal("0 t", overlay.TritiumInMarket);
        Assert.Equal(low, overlay.HasRestockWarning);
        Assert.Equal("LOW TRITIUM", overlay.WarningLabel);
        Assert.True(overlay.HasIcyRing);
        Assert.True(overlay.HasIcyBody);
        Assert.Equal("PRISTINE ICY RING", overlay.IcyRingLabel);
        Assert.False(overlay.HasRouteDetails);
        Assert.Equal("After jump (est.)", overlay.FuelLeftLabel);
    }

    [Fact]
    public async Task UnknownFuelIsOmittedWhileTheCountdownAndDestinationRemainVisible()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.True(overlay.ShouldShow);
        Assert.True(overlay.HasCountdown);
        Assert.False(overlay.HasFuelLeft);
        Assert.False(overlay.HasMarketTritium);
        Assert.False(overlay.HasJumpFuel);
        Assert.False(overlay.HasTritiumDetails);
        Assert.False(overlay.HasRestockWarning);
        Assert.False(overlay.HasJumpSummary);
    }

    [Fact]
    public async Task JumpPhasesWaitForArrivalThenFinishCooldownWithoutAnotherRoute()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        fixture.Now = fixture.Now.AddMinutes(7);
        fixture.Route.RefreshCarrierJumpCountdown();
        Assert.Equal("PAD LOCKDOWN IN", overlay.CountdownPhase);
        fixture.Now = fixture.Now.AddMinutes(5);
        fixture.Route.RefreshCarrierJumpCountdown();
        Assert.Equal("LANDING PADS LOCKED", overlay.CountdownPhase);
        fixture.Now = fixture.Now.AddMinutes(3);
        fixture.Route.RefreshCarrierJumpCountdown();
        Assert.Equal("IN TRANSIT", overlay.HopProgress);
        Assert.Equal("AWAITING ARRIVAL", overlay.CountdownPhase);
        fixture.Now = fixture.Now.AddSeconds(20);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierLocation",
                    CarrierID = 1,
                    StarSystem = "Destination",
                    StarPos = DestinationPosition,
                }
            ),
        ]);
        Assert.Equal("COOLDOWN", overlay.HopProgress);
        Assert.Equal("CARRIER ARRIVED", overlay.CountdownPhase);
        Assert.True(overlay.ShouldShow);
        Assert.False(overlay.HasRestockWarning);
        Assert.False(overlay.HasJumpFuel);
        fixture.Now = fixture.Now.AddMinutes(6);
        fixture.Route.RefreshCarrierJumpCountdown();
        Assert.False(overlay.ShouldShow);
    }

    [Fact]
    public async Task CancellationRetainsTheDestinationAndTheTogglePersistsWithoutDisablingTheCountdown()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        Assert.True(fixture.Route.ShowCarrierNextJumpInformation);
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        fixture.Route.ShowCarrierNextJumpInformation = false;
        Assert.False(overlay.ShouldShow);
        Assert.True(overlay.HasCountdown);
        Assert.False(new CarrierNextJumpSettingsStore(fixture.SettingsPath).Load());
        fixture.Route.ShowCarrierNextJumpInformation = true;
        await fixture.Route.CarrierDestinationLookup;
        Assert.True(overlay.ShouldShow);
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(new { @event = "CarrierJumpCancelled", CarrierID = 1 }),
        ]);
        Assert.Equal("CANCELLED", overlay.HopProgress);
        Assert.Equal("Destination", overlay.SystemName);
        fixture.Now = fixture.Now.AddMinutes(2);
        fixture.Route.RefreshCarrierJumpCountdown();
        Assert.False(overlay.ShouldShow);
    }

    [Fact]
    public async Task OtherCarrierEventsAndLateLookupsCannotOverwriteTheSelectedCommander()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedFuelAsync(260);
        fixture.Client.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RequestAsync(wait: false);
        Task lookup = fixture.Route.CarrierDestinationLookup;
        await fixture.Route.ApplyJournalEventsAsync([
            fixture.Entry(
                new
                {
                    @event = "CarrierJumpRequest",
                    CarrierID = 2,
                    SystemName = "Other carrier",
                    DepartureTime = fixture.Now.AddMinutes(15),
                }
            ),
        ]);
        Assert.Equal("Destination", fixture.Route.CarrierJumpDestination);
        await fixture.Route.UpdateContextAsync("Other commander", null, null, null);
        fixture.Client.Delay.SetResult(new(Fixture.Summary("Destination"), []));
        await lookup;
        Assert.Null(fixture.Route.CarrierDestinationSummary);
        Assert.False(fixture.Route.ShouldShowCarrierNextJumpOverlay);
    }

    [Fact]
    public async Task LookupFailureLeavesOptionalFieldsHiddenAndDoesNotStopJournalTracking()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Client.Fail = true;
        await fixture.RequestAsync();
        using FleetCarrierRouteOverlayViewModel overlay = fixture.Overlay();
        Assert.True(overlay.ShouldShow);
        Assert.True(overlay.HasCountdown);
        Assert.False(overlay.HasIcyBody);
        Assert.False(overlay.HasIcyRing);
        Assert.False(overlay.HasTritiumDetails);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "carrier-next-jump-" + Guid.NewGuid().ToString("N")
        );
        public DateTimeOffset Now { get; set; } =
            DateTimeOffset.Parse("2026-10-25T12:00:00Z", CultureInfo.InvariantCulture);
        public Client Client { get; } = new();
        public RouteWorkspaceViewModel Route { get; }
        public string SettingsPath => Path.Combine(directory, "ui.json");

        private Fixture()
        {
            Route = new(
                new FollowRouteService(new FollowRouteStore(directory, FollowRouteKind.FleetCarrier)),
                new RouteNameImporter(new Resolver()),
                new Routes(),
                new RouteWorkspaceOptions(
                    FollowRouteKind.FleetCarrier,
                    UtcNow: () => Now,
                    CarrierNextJump: new(Client, new(SettingsPath), new Resolver())
                )
            );
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Route.UpdateContextAsync("F1", "Elsewhere", 1, new(999, 999, 999));
            return fixture;
        }

        public Task SeedFuelAsync(int fuel) =>
            Route.ApplyJournalEventsAsync([
                Entry(
                    new
                    {
                        @event = "CarrierStats",
                        CarrierID = 1,
                        CarrierType = "FleetCarrier",
                        Callsign = "OWN-001",
                        FuelLevel = fuel,
                        SpaceUsage = new
                        {
                            TotalCapacity = 25000,
                            FreeSpace = 20000,
                            Cargo = 0,
                        },
                    }
                ),
                Entry(
                    new
                    {
                        @event = "CarrierLocation",
                        CarrierID = 1,
                        StarSystem = "Origin",
                        StarPos = OriginPosition,
                    }
                ),
            ]);

        public async Task RequestAsync(bool wait = true, string destination = "Destination")
        {
            await Route.ApplyJournalEventsAsync([
                Entry(
                    new
                    {
                        @event = "CarrierJumpRequest",
                        CarrierID = 1,
                        SystemName = destination,
                        SystemAddress = 42,
                        DepartureTime = Now.AddMinutes(15),
                    }
                ),
            ]);
            if (wait)
            {
                await Route.CarrierDestinationLookup;
            }
        }

        public FleetCarrierRouteOverlayViewModel Overlay() =>
            new(Route, OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));

        public JournalEventEnvelope Entry(object entry)
        {
            string json = JsonSerializer.Serialize(entry);
            System.Text.Json.Nodes.JsonNode node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            node["timestamp"] = Now.ToString("O");
            Assert.True(JournalEventEnvelope.TryParse(node.ToJsonString(), out JournalEventEnvelope? parsed, out _));
            return parsed!;
        }

        public static SystemSummary Summary(string name) =>
            new(
                name,
                42,
                new(
                    name switch
                    {
                        "Origin" => 0,
                        "Destination 2" => 1000,
                        _ => 500,
                    },
                    0,
                    0
                ),
                null,
                true,
                0,
                0,
                null,
                null,
                null,
                null,
                new(0, 0, 0, 0, 0, 0, 0),
                []
            )
            {
                MiningResources = new(true, true, true),
            };

        public async ValueTask DisposeAsync()
        {
            Route.CancelCarrierDestinationLookup();
            await Route.FlushWorkspaceAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class Client : ISystemSummaryClient
    {
        public bool Fail { get; set; }
        public TaskCompletionSource<SystemSummaryLoadResult>? Delay { get; set; }

        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default
        ) =>
            Fail
                ? Task.FromException<SystemSummaryLoadResult>(new HttpRequestException("Unavailable"))
                : Delay?.Task ?? Task.FromResult(new SystemSummaryLoadResult(Fixture.Summary(systemName), []));
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<IReadOnlyList<StarSystemReference>>([
                new(query, 42, new(query == "Destination" ? 500 : 0, 0, 0)),
            ]);
    }

    private sealed class Routes : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
    }
}
