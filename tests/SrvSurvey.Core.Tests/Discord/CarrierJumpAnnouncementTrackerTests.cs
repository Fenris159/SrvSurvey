using System.Globalization;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Discord;

public sealed class CarrierJumpAnnouncementTrackerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse(
        "2026-10-28T12:00:00Z",
        CultureInfo.InvariantCulture
    );

    [Fact]
    public void SimultaneousPersonalAndSquadronJumpsKeepTheirOwnItineraries()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        Assert.Empty(
            tracker.Apply(
                "F1",
                [
                    Event("CarrierStats", 0, "\"CarrierID\":1,\"Name\":\"Explorer\",\"Callsign\":\"ABC-123\""),
                    Event(
                        "CarrierStats",
                        0,
                        "\"CarrierID\":2,\"CarrierType\":\"SquadronCarrier\",\"Name\":\"Squad\",\"Callsign\":\"SQU-123\""
                    ),
                    Event("CarrierLocation", 1, "\"CarrierID\":1,\"StarSystem\":\"Sol\""),
                    Event("CarrierLocation", 1, "\"CarrierID\":2,\"StarSystem\":\"Achenar\""),
                ]
            )
        );
        IReadOnlyList<CarrierJumpAnnouncement> scheduled = tracker.Apply(
            "F1",
            [Request(1, 2), Request(2, 3, "SquadronCarrier")]
        );
        Assert.Equal(2, scheduled.Count);
        Assert.Equal("Sol", scheduled[0].Origin);
        Assert.Equal("Achenar", scheduled[1].Origin);
        Assert.Equal(FollowRouteCarrierKind.Personal, scheduled[0].CarrierKind);
        Assert.Equal(FollowRouteCarrierKind.Squadron, scheduled[1].CarrierKind);
        Assert.Equal("Explorer", scheduled[0].Name);
        Assert.Equal("SQU-123", scheduled[1].Callsign);
        Assert.Equal("Lave 1", scheduled[1].Body);
        Assert.Equal(Start.AddSeconds(100), scheduled[0].Departure);

        CarrierJumpAnnouncement cancelled = Assert.Single(
            tracker.Apply("F1", [Event("CarrierJumpCancelled", 5, "\"CarrierID\":1")])
        );
        Assert.Equal(CarrierAnnouncementKind.Cancelled, cancelled.Event);
        Assert.Equal("Lave", cancelled.Destination);
        Assert.Equal("Sol", cancelled.Origin);
        Assert.Empty(tracker.Apply("F1", [Event("CarrierLocation", 101, "\"CarrierID\":1,\"StarSystem\":\"Lave\"")]));
        CarrierJumpAnnouncement arrived = Assert.Single(
            tracker.Apply("F1", [Event("CarrierJump", 101, "\"MarketID\":2,\"StarSystem\":\"Lave\",\"BodyID\":1")])
        );
        Assert.Equal(CarrierAnnouncementKind.Arrived, arrived.Event);
        Assert.Equal(FollowRouteCarrierKind.Squadron, arrived.CarrierKind);
        Assert.Equal("Achenar", arrived.Origin);
        Assert.Empty(tracker.Apply("F1", [Event("CarrierLocation", 102, "\"CarrierID\":2,\"StarSystem\":\"Lave\"")]));
    }

    [Theory]
    [InlineData(50, "\"CarrierID\":1,\"StarSystem\":\"Lave\"")]
    [InlineData(101, "\"CarrierID\":99,\"StarSystem\":\"Lave\"")]
    [InlineData(101, "\"CarrierID\":1,\"StarSystem\":\"Sol\"")]
    [InlineData(101, "\"CarrierID\":1,\"StarSystem\":\"Lave\",\"SystemAddress\":999")]
    [InlineData(101, "\"CarrierID\":1,\"StarSystem\":\"Lave\",\"BodyID\":0")]
    public void PassengerJumpsEarlyLocationsAndWrongDestinationsDoNotConfirmArrival(int second, string data)
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [Request(1, 1)]);
        Assert.Empty(tracker.Apply("F1", [Event("CarrierLocation", second, data)]));
        CarrierJumpAnnouncement arrived = Assert.Single(
            tracker.Apply(
                "F1",
                [
                    Event(
                        "CarrierLocation",
                        110,
                        "\"CarrierID\":1,\"StarSystem\":\"Lave\",\"SystemAddress\":42,\"BodyID\":1"
                    ),
                ]
            )
        );
        Assert.Equal(CarrierAnnouncementKind.Arrived, arrived.Event);
    }

    [Fact]
    public void StartupLocationsDuplicatesAndOlderEventsNeverCreateExtraAnnouncements()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        JournalEventEnvelope request = Request(1, 10);
        Assert.Empty(tracker.Apply("F1", [Event("CarrierLocation", 1, "\"CarrierID\":1,\"StarSystem\":\"Sol\"")]));
        Assert.Single(tracker.Apply("F1", [request]));
        Assert.Empty(tracker.Apply("F1", [request, Request(1, 9)]));
        Assert.Empty(tracker.Apply("F2", [Event("CarrierJumpCancelled", 11, "\"CarrierID\":1")]));
        Assert.Single(tracker.Apply("F1", [Event("CarrierJumpCancelled", 11, "\"CarrierID\":1")]));
        Assert.Empty(tracker.Apply("F1", [Event("CarrierJumpCancelled", 12, "\"CarrierID\":1")]));
        Assert.Empty(tracker.Apply(" ", [request]));
        Assert.Throws<ArgumentNullException>(() => tracker.Apply("F1", null!));
    }

    [Theory]
    [InlineData("CarrierJumpRequest", "\"CarrierID\":0,\"SystemName\":\"Lave\"")]
    [InlineData("CarrierJumpRequest", "\"CarrierID\":-1,\"SystemName\":\"Lave\"")]
    [InlineData("CarrierJumpRequest", "\"CarrierID\":true,\"SystemName\":\"Lave\"")]
    [InlineData("CarrierJumpRequest", "\"CarrierID\":\"bad\",\"SystemName\":\"Lave\"")]
    [InlineData("CarrierJumpRequest", "\"CarrierID\":1,\"SystemName\":false")]
    [InlineData("CarrierJumpRequest", "\"CarrierID\":1,\"CarrierType\":\"Unknown\",\"SystemName\":\"Lave\"")]
    [InlineData("FSDJump", "\"SystemName\":\"Lave\"")]
    [InlineData("CarrierJump", "\"StarSystem\":\"Lave\"")]
    public void UnusableEventsAreIgnored(string name, string data)
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        Assert.Empty(tracker.Apply("F1", [Event(name, 1, data)]));
        Assert.Empty(tracker.Apply("F1", [Event(name, 2, data) with { Timestamp = null }]));
    }

    [Fact]
    public void AccountIdentitySeedsOriginWithoutDependingOnTheSelectedCarrierTab()
    {
        FrontierCarrierSnapshot personal = FrontierCapiSnapshotParser.ParseCarrier(
            """{"id":1,"name":{"callsign":"ABC-123","vanityName":"Explorer"},"currentStarSystem":"Sol"}""",
            Start
        );
        FrontierCarrierSnapshot squadron = FrontierCapiSnapshotParser.ParseCarrier(
            """{"id":2,"name":{"callsign":"SQU-123"},"currentStarSystem":"Achenar"}""",
            Start
        );
        var account = new FrontierAccountSnapshot(
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
            Start,
            SquadronCarrier: squadron
        );
        var tracker = new CarrierJumpAnnouncementTracker();
        Assert.Empty(tracker.Apply("F1", [], account));
        Assert.Empty(tracker.Apply("F1", [Event("CarrierNameChanged", 1, "\"CarrierID\":2,\"Name\":\"Renamed\"")]));
        CarrierJumpAnnouncement scheduled = Assert.Single(
            tracker.Apply(
                "F1",
                [Event("CarrierJumpRequest", 2, "\"CarrierID\":\"2\",\"SystemName\":\"Lave\",\"DepartureTime\":false")],
                account
            )
        );
        Assert.Equal(FollowRouteCarrierKind.Squadron, scheduled.CarrierKind);
        Assert.Equal("Achenar", scheduled.Origin);
        Assert.Equal("Renamed", scheduled.Name);
        Assert.Null(scheduled.Departure);
        Assert.Empty(tracker.Apply("F1", [Event("CarrierLocation", 3, "\"CarrierID\":2,\"StarSystem\":false")]));
        Assert.Single(tracker.Apply("F1", [Event("CarrierLocation", 4, "\"CarrierID\":2,\"StarSystem\":\"Lave\"")]));
    }

    [Fact]
    public void DuplicateMemoryIsBoundedAndCurrentOriginIsUpdatedAfterArrival()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [Event("CarrierBuy", 0, "\"CarrierID\":1,\"Name\":\"Explorer\"")]);
        tracker.Apply("F1", [Request(1, 1)]);
        tracker.Apply("F1", [Event("CarrierLocation", 110, "\"CarrierID\":1,\"StarSystem\":\"Lave\"")]);
        foreach (int index in Enumerable.Range(111, 515))
        {
            Assert.Empty(
                tracker.Apply("F1", [Event("CarrierLocation", index, "\"CarrierID\":1,\"StarSystem\":\"Lave\"")])
            );
        }
        Assert.Equal("Lave", Assert.Single(tracker.Apply("F1", [Request(1, 630)])).Origin);
    }

    [Fact]
    public void ExplicitCompletedJumpConfirmsArrivalEvenWhenBodyMetadataDescribesTheSystemStar()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [Request(1, 1)]);
        CarrierJumpAnnouncement arrived = Assert.Single(
            tracker.Apply("F1", [Event("CarrierJump", 110, "\"MarketID\":1,\"StarSystem\":\"Lave\",\"BodyID\":0")])
        );
        Assert.Equal(CarrierAnnouncementKind.Arrived, arrived.Event);
        Assert.Equal("Lave 1", arrived.Body);
    }

    [Fact]
    public void LaterAccountDetailsEnrichJournalDiscoveryAndPendingAnnouncements()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [Event("CarrierLocation", 0, "\"CarrierID\":1,\"StarSystem\":false")]);
        tracker.Apply("F1", [Request(1, 1)]);
        FrontierAccountSnapshot account = Account(squadron: true);
        Assert.Empty(tracker.Apply("F1", [], account));
        CarrierJumpAnnouncement cancelled = Assert.Single(
            tracker.Apply("F1", [Event("CarrierJumpCancelled", 2, "\"CarrierID\":1")], account)
        );
        Assert.Equal("Explorer", cancelled.Name);
        Assert.Equal("ABC-123", cancelled.Callsign);
        Assert.Equal(FollowRouteCarrierKind.Squadron, cancelled.CarrierKind);
        Assert.Equal("Sol", cancelled.Origin);
    }

    [Fact]
    public void CachedAccountDetailsCannotRevertANewerJournalPositionOrName()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [], Account());
        tracker.Apply("F1", [Event("CarrierNameChanged", 1, "\"CarrierID\":1,\"Name\":\"Renamed\"")]);
        tracker.Apply("F1", [Event("CarrierLocation", 2, "\"CarrierID\":1,\"StarSystem\":\"Achenar\"")]);
        CarrierJumpAnnouncement scheduled = Assert.Single(tracker.Apply("F1", [Request(1, 3)], Account()));
        Assert.Equal("Achenar", scheduled.Origin);
        Assert.Equal("Renamed", scheduled.Name);
    }

    [Fact]
    public void AccountAlreadyAtDestinationDoesNotInventAPendingDepartureSystem()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [Request(1, 1)]);
        tracker.Apply("F1", [], Account(system: "Lave"));
        CarrierJumpAnnouncement arrived = Assert.Single(
            tracker.Apply("F1", [Event("CarrierJump", 110, "\"MarketID\":1,\"StarSystem\":\"Lave\"")])
        );
        Assert.Empty(arrived.Origin);
        Assert.Equal("Explorer", arrived.Name);
    }

    [Fact]
    public void PurchaseLocationSuppliesTheFirstJumpOrigin()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [Event("CarrierBuy", 0, "\"CarrierID\":1,\"Location\":\"Sol\"")]);
        Assert.Equal("Sol", Assert.Single(tracker.Apply("F1", [Request(1, 1)])).Origin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownOwnedCarrierCanCompleteWithoutTheOriginalJournalRequest(bool squadron)
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [], Account(squadron));
        JournalEventEnvelope completed = Event(
            "CarrierJump",
            110,
            "\"MarketID\":1,\"StarSystem\":\"Lave\",\"Body\":\"Lave 1\""
        );
        CarrierJumpAnnouncement arrived = Assert.Single(tracker.Apply("F1", [completed]));
        Assert.Equal(CarrierAnnouncementKind.Arrived, arrived.Event);
        Assert.Equal(squadron ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal, arrived.CarrierKind);
        Assert.Equal("Sol", arrived.Origin);
        Assert.Equal("Lave", arrived.Destination);
        Assert.Equal("Lave 1", arrived.Body);
        Assert.Null(arrived.Departure);
        Assert.Empty(tracker.Apply("F1", [completed]));
        Assert.Empty(tracker.Apply("F1", [Event("CarrierJump", 111, "\"MarketID\":1,\"StarSystem\":\"Lave\"")]));
        Assert.Empty(tracker.Apply("F1", [Event("CarrierJump", 112, "\"MarketID\":999,\"StarSystem\":\"Sol\"")]));
        Assert.Single(tracker.Apply("F1", [Event("CarrierJump", 300, "\"MarketID\":1,\"StarSystem\":\"Lave\"")]));
    }

    [Fact]
    public void ALocationAndExplicitJumpConfirmTheSameArrivalOnlyOnce()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [Request(1, 1)]);
        Assert.Single(tracker.Apply("F1", [Event("CarrierLocation", 110, "\"CarrierID\":1,\"StarSystem\":\"Lave\"")]));
        Assert.Empty(tracker.Apply("F1", [Event("CarrierJump", 111, "\"MarketID\":1,\"StarSystem\":\"Lave\"")]));
        Assert.Single(tracker.Apply("F1", [Event("CarrierJump", 112, "\"MarketID\":1,\"StarSystem\":\"Sol\"")]));
    }

    [Fact]
    public void CommanderBoundariesNeverRelabelPrecedingCarrierRecords()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        IReadOnlyList<CarrierJumpAnnouncement> announcements = tracker.Apply(
            "F2",
            [
                Request(1, 1),
                Event("Fileheader", 2, "\"Odyssey\":true"),
                Request(2, 3),
                Event("Commander", 4, "\"Name\":\"Second\",\"FID\":\"F2\""),
                Request(3, 5),
                Event("LoadGame", 6, "\"Commander\":\"Second\""),
                Request(4, 7),
                Event("Commander", 8, "\"Name\":\"First\",\"FID\":\"F1\""),
                Request(5, 9),
                Event("LoadGame", 10, "\"Commander\":\"Second\""),
                Request(6, 11),
            ],
            initialIdentity: new("F1", "First")
        );
        Assert.Equal([3L, 4L], announcements.Select(item => item.CarrierId));
        Assert.Empty(tracker.Apply("F2", [Event("CarrierJumpCancelled", 12, "\"CarrierID\":1")]));
    }

    [Fact]
    public void NameOnlyCommanderSelectionStillHonorsBoundariesAndUnchangedIdentity()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        IReadOnlyList<CarrierJumpAnnouncement> announcements = tracker.Apply(
            "First",
            [
                Request(1, 1),
                Event("LoadGame", 2, "\"Ship\":\"CobraMkIII\""),
                Request(2, 3),
                Event("Commander", 4, "\"Name\":\"Other\""),
                Request(3, 5),
            ],
            initialIdentity: new(null, "First")
        );
        Assert.Equal([1L, 2L], announcements.Select(item => item.CarrierId));
    }

    [Theory]
    [InlineData("F1", true)]
    [InlineData("F2", false)]
    public void AnIdentityWithoutANameRetainsOnlyTheSameConfirmedCommander(string frontierId, bool expected)
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        IReadOnlyList<CarrierJumpAnnouncement> announcements = tracker.Apply(
            "F1",
            [
                Event("Commander", 1, $"\"FID\":\"{frontierId}\""),
                Event("LoadGame", 2, "\"Commander\":\"First\""),
                Request(1, 3),
            ],
            initialIdentity: new("F1", "First")
        );
        Assert.Equal(expected ? 1 : 0, announcements.Count);
    }

    [Fact]
    public void AnOlderJournalRecordCannotReclassifyANewerKnownSquadronCarrier()
    {
        var tracker = new CarrierJumpAnnouncementTracker();
        tracker.Apply("F1", [], Account(squadron: true));
        tracker.Apply("F1", [Event("CarrierLocation", 10, "\"CarrierID\":1,\"StarSystem\":\"Achenar\"")]);
        tracker.Apply(
            "F1",
            [Event("CarrierStats", 5, "\"CarrierID\":1,\"CarrierType\":\"FleetCarrier\",\"Name\":\"Old name\"")]
        );
        CarrierJumpAnnouncement scheduled = Assert.Single(
            tracker.Apply("F1", [Event("CarrierJumpRequest", 11, "\"CarrierID\":1,\"SystemName\":\"Lave\"")])
        );
        Assert.Equal(FollowRouteCarrierKind.Squadron, scheduled.CarrierKind);
        Assert.Equal("Explorer", scheduled.Name);
        Assert.Equal("Achenar", scheduled.Origin);
    }

    private static FrontierAccountSnapshot Account(bool squadron = false, string system = "Sol")
    {
        FrontierCarrierSnapshot carrier = FrontierCapiSnapshotParser.ParseCarrier(
            $$"""{"id":1,"name":{"callsign":"ABC-123","vanityName":"Explorer"},"currentStarSystem":"{{system}}"}""",
            Start
        );
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
            squadron ? null : carrier,
            Start,
            SquadronCarrier: squadron ? carrier : null
        );
    }

    private static JournalEventEnvelope Request(long id, int second, string type = "FleetCarrier") =>
        Event(
            "CarrierJumpRequest",
            second,
            $"\"CarrierID\":{id},\"CarrierType\":\"{type}\",\"SystemName\":\"Lave\",\"SystemAddress\":42,\"Body\":\"Lave 1\",\"BodyID\":1,\"DepartureTime\":\"{Start.AddSeconds(100):O}\""
        );

    private static JournalEventEnvelope Event(string name, int second, string fields)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(
                $$"""{"timestamp":"{{Start.AddSeconds(second):O}}","event":"{{name}}",{{fields}}} """,
                out JournalEventEnvelope? entry,
                out string? error
            ),
            error
        );
        return entry!;
    }
}
