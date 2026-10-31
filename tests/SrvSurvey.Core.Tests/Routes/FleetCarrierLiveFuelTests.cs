using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class FleetCarrierLiveFuelTests
{
    private static readonly DateTimeOffset Origin = DateTimeOffset.Parse(
        "2026-10-15T12:00:00Z",
        CultureInfo.InvariantCulture
    );

    [Fact]
    public void UnknownCarrierStatsCannotCreatePersonalOrSquadronIdentity()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(1, "CarrierStats", "\"CarrierID\":2,\"Callsign\":\"OTH-002\",\"FuelLevel\":900"),
            Event(2, "CarrierLocation", "\"CarrierID\":2,\"StarSystem\":\"B\""),
        ]);
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Personal, []));
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Squadron, []));
        journal.Observe([
            Event(3, "CarrierStats", "\"CarrierID\":2,\"CarrierType\":\"Unknown\",\"Callsign\":\"OTH-002\""),
        ]);
        Assert.Null(journal.GetKnownCarrierKind(2));
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Personal, []));
        journal.Observe([Event(4, "CarrierJumpRequest", "\"CarrierID\":2,\"CarrierType\":\"SquadronCarrier\"")]);
        Assert.Equal(900, journal.Read(null, FollowRouteCarrierKind.Squadron, [])!.TankTonnes);
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Personal, []));
    }

    [Fact]
    public void TypedSquadronRequestsKeepLaterUntypedStatsOutOfThePersonalCarrier()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierStats",
                "\"CarrierID\":1,\"CarrierType\":\"FleetCarrier\",\"Callsign\":\"OWN-001\",\"FuelLevel\":100"
            ),
            Event(2, "CarrierJumpRequest", "\"CarrierID\":2,\"CarrierType\":\"SquadronCarrier\",\"SystemName\":\"A\""),
            Event(3, "CarrierStats", "\"CarrierID\":2,\"Callsign\":\"SQU-002\",\"FuelLevel\":700"),
        ]);
        FleetCarrierFuelSnapshot personal = Assert.IsType<FleetCarrierFuelSnapshot>(
            journal.Read(null, FollowRouteCarrierKind.Personal, [])
        );
        FleetCarrierFuelSnapshot squadron = Assert.IsType<FleetCarrierFuelSnapshot>(
            journal.Read(null, FollowRouteCarrierKind.Squadron, [])
        );
        Assert.Equal(1, personal.CarrierId);
        Assert.Equal(100, personal.TankTonnes);
        Assert.Equal(2, squadron.CarrierId);
        Assert.Equal(700, squadron.TankTonnes);
    }

    [Fact]
    public void ClearingCommanderObservationsAlsoClearsCarrierTypeHints()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([Event(1, "CarrierJumpRequest", "\"CarrierID\":2,\"CarrierType\":\"SquadronCarrier\"")]);
        Assert.Equal(FollowRouteCarrierKind.Squadron, journal.GetKnownCarrierKind(2));
        journal.Reset();
        Assert.Null(journal.GetKnownCarrierKind(2));
        journal.Observe([Event(2, "CarrierStats", "\"CarrierID\":2,\"Callsign\":\"OWN-002\",\"FuelLevel\":900")]);
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Personal, []));
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Squadron, []));
        journal.Observe([
            Event(
                3,
                "CarrierStats",
                "\"CarrierID\":2,\"CarrierType\":\"FleetCarrier\",\"Callsign\":\"OWN-002\",\"FuelLevel\":900"
            ),
        ]);
        Assert.Equal(900, journal.Read(null, FollowRouteCarrierKind.Personal, [])!.TankTonnes);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"2\"")]
    [InlineData("{}")]
    [InlineData("true")]
    public void MalformedCarrierIdentityHintsDoNotInterruptObservationReplay(string id)
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierStats",
                $"\"CarrierID\":{id},\"CarrierType\":\"SquadronCarrier\",\"Callsign\":\"BAD-000\""
            ),
        ]);
        Assert.Null(journal.GetKnownCarrierKind(2));
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Personal, []));
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Squadron, []));
    }

    [Theory]
    [InlineData(false, 1, 1000)]
    [InlineData(false, 2, 995)]
    [InlineData(false, 303, 1000)]
    [InlineData(true, 2, 1000)]
    public void SameSystemCarrierLocationConsumesFuelOnlyForAConfirmedJump(bool cancelled, int arrivalSecond, int fuel)
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierJumpRequest",
                $"\"CarrierID\":1,\"SystemName\":\"A\",\"DepartureTime\":\"{Origin.AddSeconds(2):O}\""
            ),
        ]);
        if (cancelled)
        {
            journal.Observe([Event(1, "CarrierJumpCancelled", "\"CarrierID\":1")]);
        }
        journal.Observe([
            Event(arrivalSecond, "CarrierLocation", "\"CarrierID\":1,\"StarSystem\":\"A\""),
            Event(
                arrivalSecond < 2 ? arrivalSecond : arrivalSecond + 1,
                "CarrierLocation",
                "\"CarrierID\":1,\"StarSystem\":\"A\""
            ),
        ]);
        FleetCarrierFuelSnapshot snapshot = journal.Read(
            Account(Carrier(1, "OWN-001", 1000, 500, 1000)),
            FollowRouteCarrierKind.Personal,
            Hops()
        )!;
        Assert.Equal(fuel, snapshot.TankTonnes);
        Assert.Equal(fuel != 1000, snapshot.IsEstimated);
        Assert.Equal(new GalacticCoordinate(0, 0, 0), snapshot.Position);
    }

    [Fact]
    public void ANewerFuelBaselineDoesNotLeaveAHistoricalSameSystemJumpPending()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierJumpRequest",
                $"\"CarrierID\":1,\"SystemName\":\"A\",\"DepartureTime\":\"{Origin.AddSeconds(2):O}\""
            ),
            Event(3, "CarrierLocation", "\"CarrierID\":1,\"StarSystem\":\"A\""),
            Event(7, "CarrierLocation", "\"CarrierID\":1,\"StarSystem\":\"A\""),
        ]);
        FrontierAccountSnapshot baseline = Account(Carrier(1, "OWN-001", 995, 500, 1000)) with
        {
            CarrierFetchedAt = Origin.AddSeconds(6),
        };
        FleetCarrierFuelSnapshot snapshot = journal.Read(baseline, FollowRouteCarrierKind.Personal, Hops())!;
        Assert.Equal(995, snapshot.TankTonnes);
        Assert.False(snapshot.IsEstimated);
    }

    [Theory]
    [InlineData(1, "B", false, 2, true)]
    [InlineData(2, "B", false, 2, false)]
    [InlineData(1, "Other", false, 2, false)]
    [InlineData(1, "B", true, 2, false)]
    [InlineData(1, "B", false, 1, false)]
    [InlineData(1, "B", false, 303, false)]
    public void IdlessArrivalRequiresAMatchingUncancelledCarrierRequest(
        int requestedCarrier,
        string destination,
        bool cancelled,
        int arrivalSecond,
        bool expected
    )
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierJumpRequest",
                $"\"CarrierID\":{requestedCarrier},\"SystemName\":\"B\",\"DepartureTime\":\"{Origin.AddSeconds(2):O}\""
            ),
        ]);
        if (cancelled)
        {
            journal.Observe([Event(1, "CarrierJumpCancelled", "\"CarrierID\":1")]);
        }
        journal.Observe([
            Event(arrivalSecond, "CarrierJump", $"\"StarSystem\":\"{destination}\",\"StarPos\":[100,0,0]"),
        ]);
        FleetCarrierFuelSnapshot snapshot = journal.Read(
            Account(Carrier(1, "OWN-001", 1000, 500, 1000)),
            FollowRouteCarrierKind.Personal,
            Hops()
        )!;
        Assert.Equal(expected ? "B" : "A", snapshot.SystemName);
        Assert.Equal(expected ? 981 : 1000, snapshot.TankTonnes);
    }

    [Fact]
    public void CapiPreservesInvariantTritiumAndSeparatesSquadronFuelWithoutAddingSaleStockTwice()
    {
        FrontierCarrierSnapshot personal = Carrier(1, "OWN-001", 800, 500, 1200);
        Assert.Equal(500, personal.FuelData!.StoredTritiumTonnes);
        Assert.Equal(800, personal.FuelData.TankTonnes);
        Assert.Equal(1200, personal.FuelData.UsedCapacityTonnes);
        Assert.Equal(1, personal.FuelData.CarrierId);
        FrontierCarrierSnapshot squadron = FrontierCapiSnapshotParser
            .ParseSquadronEndpoint("{\"squadronCarrier\":" + Payload(2, "SQU-002", 900, 1000, 40000) + "}", Origin)
            .Carrier!;
        Assert.Equal(FollowRouteCarrierKind.Squadron, squadron.FuelData!.Kind);
        Assert.Equal(60000, squadron.FuelData.Capacity);
        Assert.True(squadron.FuelData.IsComplete);
    }

    [Theory]
    [InlineData("{\"Name\":{\"callsign\":\"OWN-001\"}}", "OWN-001")]
    [InlineData("{\"name\":{\"Callsign\":\"OWN-001\"}}", "OWN-001")]
    [InlineData("{\"name\":{\"callsign\":12345}}", "12345")]
    [InlineData("{\"name\":{\"callsign\":\" OWN-001 \"}}", "OWN-001")]
    [InlineData("{\"name\":{\"callsign\":{\"name\":\"OWN-001\"}}}", "OWN-001")]
    public void CapiFuelSnapshotsPreserveCallsignFormsAcceptedByTheCarrierParser(string payload, string expected)
    {
        FrontierCarrierSnapshot personal = FrontierCapiSnapshotParser.ParseCarrier(payload, Origin);
        Assert.Equal(expected, personal.Callsign);
        Assert.Equal(expected, personal.FuelData!.Callsign);
        FrontierCarrierSnapshot squadron = FrontierCapiSnapshotParser
            .ParseSquadronEndpoint("{\"squadronCarrier\":" + payload + "}", Origin)
            .Carrier!;
        Assert.Equal(expected, squadron.Callsign);
        Assert.Equal(expected, squadron.FuelData!.Callsign);
        Assert.Equal(FollowRouteCarrierKind.Squadron, squadron.FuelData.Kind);
    }

    [Theory]
    [InlineData("335", false)]
    [InlineData("\"335\"", false)]
    [InlineData("\" 335 \"", false)]
    [InlineData("335", true)]
    [InlineData("\"335\"", true)]
    public void CapiNumericStringDepotMatchesTheCarrierWorkspaceReserve(string fuel, bool squadron)
    {
        string payload =
            "{\"name\":{\"callsign\":\"OWN-001\"},\"fuel\":"
            + fuel
            + ",\"capacity\":{\"cargoNotForSale\":5251,\"crew\":1050,\"freeSpace\":18699},\"cargo\":[]}";
        FrontierCarrierSnapshot carrier = squadron
            ? FrontierCapiSnapshotParser.ParseSquadronEndpoint("{\"squadronCarrier\":" + payload + "}", Origin).Carrier!
            : FrontierCapiSnapshotParser.ParseCarrier(payload, Origin);

        Assert.Equal(335, carrier.Tritium);
        Assert.Equal(335, carrier.FuelData!.TankTonnes);
        Assert.True(carrier.FuelData.IsComplete);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"unknown\"")]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("1e9999")]
    public void CapiInvalidDepotValuesRemainUnknown(string fuel)
    {
        FrontierCarrierSnapshot carrier = FrontierCapiSnapshotParser.ParseCarrier(
            "{\"name\":{\"callsign\":\"OWN-001\"},\"fuel\":" + fuel + "}",
            Origin
        );

        Assert.Null(carrier.FuelData!.TankTonnes);
        Assert.False(carrier.FuelData.IsComplete);
    }

    [Fact]
    public void CapiNumericStringTritiumCargoIsAvailableToTheRouteFuelPlan()
    {
        FrontierCarrierSnapshot carrier = FrontierCapiSnapshotParser.ParseCarrier(
            """
            {"name":{"callsign":"OWN-001"},"fuel":"335","capacity":{"cargoNotForSale":500},"cargo":[{"commodity":"tritium","qty":"500"}]}
            """,
            Origin
        );

        Assert.Equal(500, carrier.FuelData!.StoredTritiumTonnes);
        Assert.True(carrier.FuelData.IsComplete);
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData(",\"fuel\":0", ",\"cargo\":[]", true)]
    [InlineData(",\"fuel\":1000", ",\"cargo\":[{\"commodity\":\"tritium\"}]", false)]
    [InlineData(",\"fuel\":1001", ",\"cargo\":[]", false)]
    public void MissingFieldsRemainUnknownAndReportedEmptyInventoriesRemainZero(
        string fuel,
        string cargo,
        bool complete
    )
    {
        FrontierCarrierSnapshot carrier = FrontierCapiSnapshotParser.ParseCarrier(
            "{\"name\":{\"callsign\":\"OWN-001\"},\"capacity\":{\"cargoNotForSale\":0,\"freeSpace\":25000}"
                + fuel
                + cargo
                + "}",
            Origin
        );
        Assert.Equal(complete, carrier.FuelData!.IsComplete);
        if (complete)
        {
            Assert.Equal(0, carrier.FuelData.TankTonnes);
            Assert.Equal(0, carrier.FuelData.StoredTritiumTonnes);
        }
    }

    [Fact]
    public void JournalReconstructionUsesOnlySelectedCarrierAndStopsAtTheRequestedPassBoundary()
    {
        var journal = new FleetCarrierFuelJournal();
        FrontierAccountSnapshot account = Account(
            Carrier(1, "OWN-001", 1000, 500, 1000),
            Carrier(2, "SQU-002", 700, 800, 2000)
        );
        JournalEventEnvelope first = Event(
            1,
            "CarrierJump",
            "\"MarketID\":1,\"StarSystem\":\"B\",\"StarPos\":[100,0,0]"
        );
        JournalEventEnvelope second = Event(
            2,
            "CarrierJump",
            "\"MarketID\":1,\"StarSystem\":\"C\",\"StarPos\":[250,0,0]"
        );
        journal.Observe([first, second, Event(3, "CarrierDepositFuel", "\"CarrierID\":2,\"Total\":999")]);
        journal.Observe([first, second]);
        FleetCarrierFuelSnapshot atFirst = journal.Read(account, FollowRouteCarrierKind.Personal, Hops(), first)!;
        Assert.Equal(981, atFirst.TankTonnes);
        Assert.Equal("B", atFirst.SystemName);
        Assert.True(atFirst.IsEstimated);
        FleetCarrierFuelSnapshot atEnd = journal.Read(account, FollowRouteCarrierKind.Personal, Hops())!;
        Assert.Equal(956, atEnd.TankTonnes);
        Assert.Equal(500, atEnd.StoredTritiumTonnes);
        Assert.Equal(new GalacticCoordinate(250, 0, 0), atEnd.Position);
        FleetCarrierFuelSnapshot squadron = journal.Read(account, FollowRouteCarrierKind.Squadron, Hops())!;
        Assert.Equal(999, squadron.TankTonnes);
        Assert.Equal(800, squadron.StoredTritiumTonnes);
        journal.Reset();
        Assert.Equal(1000, journal.Read(account, FollowRouteCarrierKind.Personal, Hops())!.TankTonnes);
    }

    [Fact]
    public void StatsDepositsAndAttributedTradesUpdateIndependentAmountsWithoutTreatingDonationsAsHoldTransfers()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierStats",
                "\"CarrierID\":1,\"Callsign\":\"OWN-001\",\"FuelLevel\":880,\"SpaceUsage\":{\"TotalCapacity\":25000,\"FreeSpace\":24010}"
            ),
            Event(2, "CarrierDepositFuel", "\"CarrierID\":1,\"Amount\":50,\"Total\":930"),
            Event(3, "Docked", "\"StationType\":\"FleetCarrier\",\"MarketID\":1"),
            Event(
                4,
                "CargoTransfer",
                "\"Transfers\":[{\"Type\":\"tritium\",\"Count\":100,\"Direction\":\"toship\"},{\"Type\":\"gold\",\"Count\":20,\"Direction\":\"tocarrier\"}]"
            ),
            Event(5, "MarketSell", "\"Type\":\"gold\",\"Count\":30"),
            Event(6, "MarketBuy", "\"Type\":\"tritium\",\"Count\":10"),
            Event(7, "Undocked", ""),
            Event(8, "CargoTransfer", "\"Transfers\":[{\"Type\":\"tritium\",\"Count\":200,\"Direction\":\"toship\"}]"),
            Event(9, "CarrierStats", "\"CarrierID\":99,\"Callsign\":\"NOT-OWN\",\"FuelLevel\":1"),
        ]);
        FleetCarrierFuelSnapshot current = journal.Read(
            Account(Carrier(1, "OWN-001", 1000, 500, 1000)),
            FollowRouteCarrierKind.Personal,
            Hops()
        )!;
        Assert.Equal(930, current.TankTonnes);
        Assert.Equal(390, current.StoredTritiumTonnes);
        Assert.Equal(930, current.UsedCapacityTonnes);
        Assert.False(current.IsEstimated);
    }

    [Fact]
    public void NewerCapiReplacesOlderJournalAndUnknownJumpOriginsInvalidateFuel()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierStats",
                "\"CarrierID\":1,\"CarrierType\":\"FleetCarrier\",\"Callsign\":\"OWN-001\",\"FuelLevel\":300"
            ),
            Event(2, "CarrierLocation", "\"CarrierID\":1,\"StarSystem\":\"Unknown\""),
        ]);
        FrontierCarrierSnapshot carrier = Carrier(1, "OWN-001", 1000, 500, 1000);
        Assert.Null(journal.Read(Account(carrier), FollowRouteCarrierKind.Personal, Hops())!.TankTonnes);
        carrier = carrier with
        {
            FuelData = carrier.FuelData! with { ObservedAt = Origin.AddSeconds(5), TankTonnes = 900 },
        };
        Assert.Equal(900, journal.Read(Account(carrier), FollowRouteCarrierKind.Personal, Hops())!.TankTonnes);
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Personal, Hops())!.StoredTritiumTonnes);
        Assert.Null(journal.Read(Account(carrier), FollowRouteCarrierKind.Squadron, Hops()));
    }

    [Fact]
    public void JournalOnlyCarriersStaySeparateAndDoNotInferTritiumFromMixedCargo()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(
                1,
                "CarrierStats",
                "\"CarrierID\":1,\"CarrierType\":\"FleetCarrier\",\"Callsign\":\"OWN-001\",\"FuelLevel\":600,\"SpaceUsage\":{\"TotalCapacity\":25000,\"FreeSpace\":24000,\"Cargo\":500}"
            ),
            Event(
                2,
                "CarrierStats",
                "\"CarrierID\":2,\"CarrierType\":\"SquadronCarrier\",\"Callsign\":\"SQU-002\",\"FuelLevel\":800,\"SpaceUsage\":{\"TotalCapacity\":60000,\"FreeSpace\":50000,\"Cargo\":0}"
            ),
            Event(3, "CarrierLocation", "\"CarrierID\":2,\"StarSystem\":\"A\""),
            Event(4, "CarrierStats", "\"CarrierID\":\"bad\",\"Callsign\":\"BAD\""),
        ]);
        FleetCarrierFuelSnapshot personal = journal.Read(null, FollowRouteCarrierKind.Personal, Hops())!;
        Assert.Equal(600, personal.TankTonnes);
        Assert.Equal(1000, personal.UsedCapacityTonnes);
        Assert.Null(personal.StoredTritiumTonnes);
        Assert.False(personal.IsComplete);
        FleetCarrierFuelSnapshot squadron = journal.Read(null, FollowRouteCarrierKind.Squadron, Hops())!;
        Assert.Equal("SQU-002", squadron.Callsign);
        Assert.Equal(0, squadron.StoredTritiumTonnes);
        Assert.Equal(800, squadron.TankTonnes);
        Assert.True(squadron.IsComplete);
        journal.Observe([Event(5, "CarrierDepositFuel", "\"CarrierID\":2,\"Total\":850")]);
        Assert.Equal(850, journal.Read(null, FollowRouteCarrierKind.Squadron, Hops())!.TankTonnes);
        journal.Reset();
        Assert.Null(journal.Read(null, FollowRouteCarrierKind.Personal, Hops()));
    }

    [Fact]
    public void InvalidTransfersAndEmptyCapacityDoNotInventFuel()
    {
        var journal = new FleetCarrierFuelJournal();
        journal.Observe([
            Event(1, "Docked", "\"StationType\":\"FleetCarrier\",\"MarketID\":1"),
            Event(2, "CargoTransfer", "\"Transfers\":[{\"Type\":\"tritium\",\"Count\":200,\"Direction\":\"unknown\"}]"),
            Event(3, "CargoTransfer", "\"Transfers\":{}"),
        ]);
        Assert.Equal(
            500,
            journal
                .Read(Account(Carrier(1, "OWN-001", 1000, 500, 1000)), FollowRouteCarrierKind.Personal, Hops())!
                .StoredTritiumTonnes
        );
        FrontierCarrierSnapshot unknown = FrontierCapiSnapshotParser.ParseCarrier(
            "{\"name\":{\"callsign\":\"OWN-001\"},\"fuel\":1000,\"capacity\":{},\"cargo\":[]}",
            Origin
        );
        Assert.Null(unknown.FuelData!.UsedCapacityTonnes);
        Assert.False(unknown.FuelData.IsComplete);
    }

    [Fact]
    public void ShortfallWarnsWhenFuelMustBeBoughtAtIntermediateStopsBecauseTheHoldIsFull()
    {
        var fuel = new FleetCarrierFuelSnapshot(
            FollowRouteCarrierKind.Personal,
            "OWN-001",
            1,
            "A",
            0,
            0,
            25000,
            Origin
        );
        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(Hops(), fuel, true);
        Assert.True(plan.RequiresFuelStops);
        Assert.True(plan.AdditionalFuelTonnes > 0);
        Assert.Equal(plan.AdditionalFuelTonnes, plan.FuelRequiredTonnes);
    }

    [Theory]
    [InlineData(FollowRouteCarrierKind.Personal, 15000, 1000, 107)]
    [InlineData(FollowRouteCarrierKind.Squadron, 40000, 1000, 145)]
    public void JumpCostsMatchSpanshIncludingSquadronMass(
        FollowRouteCarrierKind kind,
        double used,
        double tank,
        double expected
    )
    {
        Assert.Equal(expected, FleetCarrierFuelPlanner.JumpFuel(kind, 499.960198611162, used, tank));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveForecastPreservesSourceAndDeclinesMassAsStoredTritiumIsTransferred(bool low)
    {
        FollowRouteHop[] original = Hops();
        var fuel = new FleetCarrierFuelSnapshot(
            FollowRouteCarrierKind.Personal,
            "OWN-001",
            1,
            "A",
            20,
            500,
            1000,
            Origin
        );
        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(original, fuel, low);
        Assert.True(plan.HasRequiredData);
        Assert.Null(plan.UnfundedHopIndex);
        Assert.Equal(0, plan.AdditionalFuelTonnes);
        Assert.Equal(0, plan.Hops[0].Carrier!.FuelUsedTonnes);
        double cost = plan.Hops.Skip(1).Sum(hop => hop.Carrier!.FuelUsedTonnes!.Value);
        Assert.Equal(cost, plan.FuelRequiredTonnes);
        Assert.Equal(
            520 - cost,
            plan.Hops[^1].Carrier!.FuelRemainingTonnes + plan.Hops[^1].Carrier!.TritiumInMarketTonnes
        );
        Assert.All(original, hop => Assert.Null(hop.Carrier));
        FleetCarrierFuelPlan reverse = FleetCarrierFuelPlanner.Calculate(original.Reverse().ToArray(), fuel, low);
        Assert.Equal(150, reverse.Hops[1].Carrier!.DistanceLy);
        Assert.Equal(100, reverse.Hops[2].Carrier!.DistanceLy);
    }

    [Fact]
    public void FuelShortfallIncludesTheMassOfExtraFuelAndRefreshCanBeginAtAnIntermediateWaypoint()
    {
        FollowRouteHop[] hops = Hops();
        var fuel = new FleetCarrierFuelSnapshot(FollowRouteCarrierKind.Personal, "OWN-001", 1, "A", 5, 0, 1000, Origin);
        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(hops, fuel, true);
        double additional = Assert.IsType<double>(plan.AdditionalFuelTonnes);
        Assert.True(additional > 0);
        Assert.NotNull(plan.UnfundedHopIndex);
        Assert.Null(
            FleetCarrierFuelPlanner
                .Calculate(
                    hops,
                    fuel with
                    {
                        StoredTritiumTonnes = additional,
                        UsedCapacityTonnes = 1000 + additional,
                    },
                    true
                )
                .UnfundedHopIndex
        );
        Assert.NotNull(
            FleetCarrierFuelPlanner
                .Calculate(
                    hops,
                    fuel with
                    {
                        StoredTritiumTonnes = additional - 1,
                        UsedCapacityTonnes = 999 + additional,
                    },
                    true
                )
                .UnfundedHopIndex
        );
        FleetCarrierFuelPlan resumed = FleetCarrierFuelPlanner.Calculate(hops, fuel with { TankTonnes = 100 }, true, 1);
        Assert.Null(resumed.Hops[0].Carrier);
        Assert.Equal(0, resumed.Hops[1].Carrier!.FuelUsedTonnes);
        Assert.Equal(150, resumed.Hops[2].Carrier!.DistanceLy);
        Assert.Null(resumed.UnfundedHopIndex);
        Assert.True(resumed.FuelRequiredTonnes < plan.FuelRequiredTonnes);
    }

    [Fact]
    public void InvalidLiveInputsOrUnreachableWaypointsDoNotFabricateForecasts()
    {
        var fuel = new FleetCarrierFuelSnapshot(
            FollowRouteCarrierKind.Personal,
            "OWN-001",
            1,
            "A",
            100,
            null,
            1000,
            Origin
        );
        Assert.False(FleetCarrierFuelPlanner.Calculate(Hops(), fuel, true).HasRequiredData);
        Assert.False(FleetCarrierFuelPlanner.Calculate([], fuel, true).HasRequiredData);
        Assert.False(
            FleetCarrierFuelPlanner.Calculate(Hops(), fuel with { StoredTritiumTonnes = 0 }, true, -1).HasRequiredData
        );
        FollowRouteHop[] hops = [Hops()[0], Hops()[1] with { Position = new(1000, 0, 0) }];
        Assert.False(
            FleetCarrierFuelPlanner.Calculate(hops, fuel with { StoredTritiumTonnes = 0 }, true).HasRequiredData
        );
        Assert.False(
            FleetCarrierFuelPlanner
                .Calculate([hops[0], hops[1] with { Position = null }], fuel with { StoredTritiumTonnes = 0 }, true)
                .HasRequiredData
        );
    }

    private static FollowRouteHop[] Hops() =>
        [
            new("A", 1, new(0, 0, 0), null, false, false),
            new("B", 2, new(100, 0, 0), null, false, false),
            new("C", 3, new(250, 0, 0), null, false, false),
        ];

    private static FrontierAccountSnapshot Account(
        FrontierCarrierSnapshot personal,
        FrontierCarrierSnapshot? squadron = null
    ) => new("CMDR", 0, 0, false, true, "A", "", null, [], [], [], personal, Origin, SquadronCarrier: squadron);

    private static FrontierCarrierSnapshot Carrier(long id, string callsign, int tank, int stored, int used) =>
        FrontierCapiSnapshotParser.ParseCarrier(Payload(id, callsign, tank, stored, used), Origin);

    private static string Payload(long id, string callsign, int tank, int stored, int used) =>
        JsonSerializer.Serialize(
            new
            {
                id,
                name = new { callsign },
                currentStarSystem = "A",
                fuel = tank,
                capacity = new { cargoNotForSale = used, freeSpace = 1000 },
                cargo = new[]
                {
                    new
                    {
                        commodity = "tritium",
                        locName = "Тритий",
                        qty = stored,
                    },
                },
                orders = new
                {
                    commodities = new
                    {
                        sales = new[]
                        {
                            new
                            {
                                name = "Tritium",
                                stock = stored,
                                price = 100,
                            },
                        },
                    },
                },
            }
        );

    private static JournalEventEnvelope Event(int second, string name, string fields)
    {
        JsonObject json = JsonNode.Parse("{" + fields + "}")!.AsObject();
        json["event"] = name;
        json["timestamp"] = Origin.AddSeconds(second).ToString("O", CultureInfo.InvariantCulture);
        Assert.True(
            JournalEventEnvelope.TryParse(json.ToJsonString(), out JournalEventEnvelope? entry, out string? error),
            error
        );
        return entry!;
    }
}
