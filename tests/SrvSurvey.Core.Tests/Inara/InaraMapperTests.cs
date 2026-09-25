using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Inara;

namespace SrvSurvey.Core.Tests.Inara;

public sealed class InaraMapperTests
{
    private static readonly InaraContext Context = new(
        "Test Commander",
        "F123456",
        "Sol",
        "Galileo",
        "Earth",
        "CobraMkIII",
        42,
        "Surveyor",
        "SRV-42",
        false
    );

    [Fact]
    public void PayloadUsesOnlyTheCommandersPersonalKey()
    {
        var credentials = new InaraCredentials("Test Commander", "F123456", "personal-key");
        InaraEvent[] events = new[] { new InaraEvent("getCommanderProfile", "2026-07-28T12:00:00Z", new JObject()) };

        JObject payload = InaraPayloadBuilder.Build("2.0.95.0", credentials, events);
        JObject header = Assert.IsType<JObject>(payload["header"]);

        Assert.Equal("SrvSurvey", header.Value<string>("appName"));
        Assert.Equal("personal-key", header.Value<string>("APIkey"));
        Assert.Equal("Test Commander", header.Value<string>("commanderName"));
        Assert.Equal("F123456", header.Value<string>("commanderFrontierID"));
        Assert.True(header.Value<bool>("isBeingDeveloped"));
        Assert.Null(header["applicationKey"]);
        Assert.Null(header["applicationAccessToken"]);
    }

    [Fact]
    public void DiagnosticFormattingDoesNotExposeThePersonalApiKey()
    {
        const string apiKey = "secret-personal-key";
        var credentials = new InaraCredentials("Test Commander", "F123456", apiKey);
        var options = new InaraPublicationOptions(apiKey, "Test Commander", "F123456", "4.1.0.100", IsOdyssey: true);
        var queued = new InaraQueuedEvent(
            apiKey,
            new InaraEvent("getCommanderProfile", "2026-07-28T12:00:00Z", new JObject())
        );

        Assert.DoesNotContain(apiKey, credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, queued.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("key", true, false, false, true)]
    [InlineData(null, true, false, false, false)]
    [InlineData("key", false, false, false, false)]
    [InlineData("key", true, true, false, false)]
    [InlineData("key", true, false, true, false)]
    public void UploadPolicyRequiresAKeyAndSafeSessionConditions(
        string? apiKey,
        bool isLive,
        bool isBeta,
        bool inMulticrew,
        bool expected
    )
    {
        Assert.Equal(expected, InaraPublisher.CanUpload(apiKey, isLive, isBeta, inMulticrew));
    }

    [Theory]
    [InlineData("4.0.0.1900", false, true)]
    [InlineData("4.1.0.100", false, true)]
    [InlineData("3.8.0.0", false, false)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    public void LiveGalaxyDetectionIncludesHorizonsFour(string? gameVersion, bool isOdyssey, bool expected)
    {
        Assert.Equal(expected, InaraPublisher.IsLiveVersion(gameVersion, isOdyssey));
    }

    [Fact]
    public void FsdJumpMapsToInaraTravelEvent()
    {
        var mapper = new InaraEventMapper();
        IReadOnlyList<InaraEvent> mapped = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Alpha Centauri",
                  "StarPos": [3.03125, -0.09375, 3.15625],
                  "JumpDist": 4.37
                }
                """
            ),
            Context,
            true
        );

        InaraEvent jump = Assert.Single(mapped, item => item.Name == "addCommanderTravelFSDJump");
        Assert.Equal("Alpha Centauri", jump.Data.Value<string>("starsystemName"));
        Assert.Equal(4.37, jump.Data.Value<double>("jumpDistance"));
        Assert.Equal(42, jump.Data.Value<long>("shipGameID"));
    }

    [Fact]
    public void PowerplayJoinUsesTheFullPowerNameForInara()
    {
        var mapper = new InaraEventMapper();
        IReadOnlyList<InaraEvent> mapped = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-09-23T12:00:00Z",
                  "event": "PowerplayJoin",
                  "Power": "A. Lavigny-Duval"
                }
                """
            ),
            Context,
            true
        );

        InaraEvent rank = Assert.Single(mapped, item => item.Name == "setCommanderRankPower");
        Assert.Equal("Arissa Lavigny-Duval", rank.Data.Value<string>("powerName"));
    }

    [Fact]
    public void ReputationEventsMapAllNumericEntries()
    {
        var mapper = new InaraEventMapper();

        IReadOnlyList<InaraEvent> major = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "Reputation",
                  "Empire": 25.5,
                  "Federation": 91
                }
                """
            ),
            Context,
            true
        );
        InaraEvent majorEvent = Assert.Single(major, item => item.Name == "setCommanderReputationMajorFaction");
        Assert.Equal(2, Assert.IsType<JArray>(majorEvent.Data).Count);

        IReadOnlyList<InaraEvent> minor = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:01:00Z",
                  "event": "Location",
                  "StarSystem": "Sol",
                  "Factions": [
                    { "Name": "Faction One", "MyReputation": 75 },
                    { "Name": "Faction Two", "MyReputation": -12.5 }
                  ]
                }
                """
            ),
            Context,
            true
        );
        InaraEvent minorEvent = Assert.Single(minor, item => item.Name == "setCommanderReputationMinorFaction");
        Assert.Equal(2, Assert.IsType<JArray>(minorEvent.Data).Count);
    }

    [Fact]
    public void UnknownTaxiStateDoesNotClaimTheCommandersShip()
    {
        var mapper = new InaraEventMapper();
        IReadOnlyList<InaraEvent> mapped = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Alpha Centauri",
                  "StarPos": [3.03125, -0.09375, 3.15625],
                  "JumpDist": 4.37
                }
                """
            ),
            Context with
            {
                IsTaxi = null,
            },
            true
        );

        InaraEvent jump = Assert.Single(mapped, item => item.Name == "addCommanderTravelFSDJump");
        Assert.Null(jump.Data["isTaxiShuttle"]);
        Assert.Null(jump.Data["shipGameID"]);
        Assert.Null(jump.Data["shipType"]);
    }

    [Fact]
    public void CargoSnapshotsReplaceOlderQueuedSnapshots()
    {
        var mapper = new InaraEventMapper();
        var credentials = new InaraCredentials("Test Commander", "F123456", "personal-key");
        var queue = new InaraEventQueue();

        IReadOnlyList<InaraEvent> initial = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "Cargo",
                  "Vessel": "Ship",
                  "Inventory": [{ "Name": "tea", "Count": 2 }]
                }
                """
            ),
            Context,
            true
        );
        queue.Enqueue(credentials.ApiKey, initial.Where(item => item.ReplaceKey == "inventory:cargo"));

        IReadOnlyList<InaraEvent> changed = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:01:00Z",
                  "event": "CargoTransfer",
                  "Transfers": [
                    { "Type": "tea", "Count": 3, "Direction": "toship" }
                  ]
                }
                """
            ),
            Context,
            true
        );
        queue.Enqueue(credentials.ApiKey, changed.Where(item => item.ReplaceKey == "inventory:cargo"));

        InaraQueuedEvent queued = Assert.Single(queue.TakeAll());
        JArray cargo = Assert.IsType<JArray>(queued.Event.Data);
        Assert.Equal(5, Assert.Single(cargo.OfType<JObject>()).Value<int>("itemCount"));
    }

    [Fact]
    public void EventQueueBoundsBacklogAndRejectsAReplacedKey()
    {
        var first = new InaraCredentials("First", "F1", "key-1");
        var second = new InaraCredentials("Second", "F2", "key-2");
        var queue = new InaraEventQueue();

        int dropped = queue.Enqueue(
            first.ApiKey,
            Enumerable
                .Range(0, 5)
                .Select(index => new InaraEvent($"first-{index}", "2026-07-28T12:00:00Z", new JObject())),
            maximumCount: 4
        );
        Assert.Equal(1, dropped);
        queue.Enqueue(
            second.ApiKey,
            [new InaraEvent("second", "2026-07-28T12:00:00Z", new JObject())],
            maximumCount: 5
        );

        List<InaraQueuedEvent> batch = queue.TakeBatch(second.ApiKey, 2, out int discarded);

        Assert.Equal(4, discarded);
        Assert.Equal("second", Assert.Single(batch).Event.Name);
        Assert.All(batch, item => Assert.Equal(second.ApiKey, item.ApiKey));
        Assert.Equal(0, queue.Count);
    }

    [Theory]
    [InlineData("MissionCompleted", "setCommanderMissionCompleted")]
    [InlineData("MissionFailed", "setCommanderMissionFailed")]
    [InlineData("MissionAbandoned", "setCommanderMissionAbandoned")]
    public void MissionTerminalTransitionDoesNotReplaceQueuedAcceptance(
        string terminalJournalEvent,
        string terminalInaraEvent
    )
    {
        var mapper = new InaraEventMapper();
        var credentials = new InaraCredentials("Test Commander", "F123456", "personal-key");
        var queue = new InaraEventQueue();
        IReadOnlyList<InaraEvent> accepted = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "MissionAccepted",
                  "MissionID": 42,
                  "Name": "Mission_Delivery",
                  "Faction": "Pilots Federation",
                  "DestinationSystem": "Sirius"
                }
                """
            ),
            Context,
            true
        );
        queue.Enqueue(credentials.ApiKey, accepted);
        IReadOnlyList<InaraEvent> terminal = mapper.Process(
            JObject.Parse(
                $$"""
                {
                  "timestamp": "2026-07-28T12:01:00Z",
                  "event": "{{terminalJournalEvent}}",
                  "MissionID": 42
                }
                """
            ),
            Context,
            true
        );
        queue.Enqueue(credentials.ApiKey, terminal);

        InaraQueuedEvent[] missionEvents = queue
            .TakeAll()
            .Where(item => item.Event.Name.Contains("Mission", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["addCommanderMission", terminalInaraEvent], missionEvents.Select(item => item.Event.Name));
        Assert.NotEqual(missionEvents[0].Event.ReplaceKey, missionEvents[1].Event.ReplaceKey);
        Assert.Equal("Mission_Delivery", missionEvents[0].Event.Data.Value<string>("missionName"));
    }

    [Fact]
    public void MulticrewSuppressesUploadsUntilCrewIsLeft()
    {
        var mapper = new InaraEventMapper();
        mapper.Process(
            JObject.Parse(
                """
                { "timestamp": "2026-07-28T12:00:00Z", "event": "JoinACrew" }
                """
            ),
            Context,
            true
        );

        IReadOnlyList<InaraEvent> suppressed = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:01:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Sirius",
                  "StarPos": [6.25, -1.25, -5.75],
                  "JumpDist": 8.6
                }
                """
            ),
            Context,
            true
        );
        Assert.True(mapper.InMulticrew);
        Assert.Empty(suppressed);

        mapper.Process(
            JObject.Parse(
                """
                { "timestamp": "2026-07-28T12:02:00Z", "event": "QuitACrew" }
                """
            ),
            Context,
            true
        );
        IReadOnlyList<InaraEvent> resumed = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:03:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Sirius",
                  "StarPos": [6.25, -1.25, -5.75],
                  "JumpDist": 8.6
                }
                """
            ),
            Context,
            true
        );

        Assert.False(mapper.InMulticrew);
        Assert.Contains(resumed, item => item.Name == "addCommanderTravelFSDJump");
    }

    [Fact]
    public void MulticrewRequiresFreshInventorySnapshotsAfterReturning()
    {
        var mapper = new InaraEventMapper();
        IReadOnlyList<InaraEvent> ownCargo = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "Cargo",
                  "Vessel": "Ship",
                  "Inventory": [{ "Name": "tea", "Count": 2 }]
                }
                """
            ),
            Context,
            true
        );
        Assert.Contains(ownCargo, item => item.Name == "setCommanderInventoryCargo");

        mapper.Process(
            JObject.Parse(
                """
                { "timestamp": "2026-07-28T12:01:00Z", "event": "JoinACrew" }
                """
            ),
            Context,
            true
        );
        IReadOnlyList<InaraEvent> crewCargo = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:02:00Z",
                  "event": "Cargo",
                  "Vessel": "Ship",
                  "Inventory": [{ "Name": "gold", "Count": 50 }],
                  "Multicrew": true
                }
                """
            ),
            Context,
            true
        );
        Assert.Empty(crewCargo);

        IReadOnlyList<InaraEvent> leaving = mapper.Process(
            JObject.Parse(
                """
                { "timestamp": "2026-07-28T12:03:00Z", "event": "QuitACrew" }
                """
            ),
            Context,
            true
        );
        Assert.DoesNotContain(leaving, item => item.Name == "setCommanderInventoryCargo");

        IReadOnlyList<InaraEvent> resumed = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:04:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Sirius",
                  "StarPos": [6.25, -1.25, -5.75],
                  "JumpDist": 8.6
                }
                """
            ),
            Context,
            true
        );
        Assert.DoesNotContain(resumed, item => item.Name == "setCommanderInventoryCargo");

        IReadOnlyList<InaraEvent> refreshed = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:05:00Z",
                  "event": "Cargo",
                  "Vessel": "Ship",
                  "Inventory": [{ "Name": "tea", "Count": 3 }]
                }
                """
            ),
            Context,
            true
        );
        InaraEvent snapshot = Assert.Single(refreshed, item => item.Name == "setCommanderInventoryCargo");
        JObject item = Assert.Single(Assert.IsType<JArray>(snapshot.Data).OfType<JObject>());
        Assert.Equal("tea", item.Value<string>("itemName"));
        Assert.Equal(3, item.Value<int>("itemCount"));
    }

    [Fact]
    public void CreditTransactionsUseTheDocumentedHourlyCadence()
    {
        var mapper = new InaraEventMapper();
        IReadOnlyList<InaraEvent> startup = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "LoadGame",
                  "Credits": 1000,
                  "Loan": 0
                }
                """
            ),
            Context,
            true
        );
        Assert.Single(startup, item => item.Name == "setCommanderCredits");

        IReadOnlyList<InaraEvent> purchase = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:10:00Z",
                  "event": "MarketBuy",
                  "Type": "tea",
                  "Count": 1,
                  "TotalCost": 100
                }
                """
            ),
            Context,
            true
        );
        Assert.DoesNotContain(purchase, item => item.Name == "setCommanderCredits");

        IReadOnlyList<InaraEvent> hourly = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T13:00:00Z",
                  "event": "Music",
                  "MusicTrack": "Exploration"
                }
                """
            ),
            Context,
            true
        );
        InaraEvent report = Assert.Single(hourly, item => item.Name == "setCommanderCredits");
        Assert.Equal(900, report.Data.Value<long>("commanderCredits"));
    }

    [Fact]
    public void ShutdownFlushesAChangedBalanceBeforeTheHourlyWindow()
    {
        var mapper = new InaraEventMapper();
        mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "LoadGame",
                  "Credits": 1000,
                  "Loan": 0
                }
                """
            ),
            Context,
            true
        );
        mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:05:00Z",
                  "event": "MarketSell",
                  "Type": "tea",
                  "Count": 1,
                  "TotalSale": 250
                }
                """
            ),
            Context,
            true
        );

        IReadOnlyList<InaraEvent> shutdown = mapper.Process(
            JObject.Parse(
                """
                { "timestamp": "2026-07-28T12:06:00Z", "event": "Shutdown" }
                """
            ),
            Context,
            true
        );
        InaraEvent report = Assert.Single(shutdown, item => item.Name == "setCommanderCredits");
        Assert.Equal(1250, report.Data.Value<long>("commanderCredits"));
    }

    [Fact]
    public void MiningRefinedUpdatesCargoWithoutPublishingInventorySnapshots()
    {
        var mapper = new InaraEventMapper();
        mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "Cargo",
                  "Vessel": "Ship",
                  "Inventory": [{ "Name": "tea", "Count": 2 }]
                }
                """
            ),
            Context,
            true
        );

        IReadOnlyList<InaraEvent> refined = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:01:00Z",
                  "event": "MiningRefined",
                  "Type": "platinum"
                }
                """
            ),
            Context,
            true
        );
        Assert.DoesNotContain(refined, item => item.Name == "setCommanderInventoryCargo");

        IReadOnlyList<InaraEvent> collected = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:02:00Z",
                  "event": "CollectCargo",
                  "Type": "tea",
                  "Count": 1
                }
                """
            ),
            Context,
            true
        );
        InaraEvent snapshot = Assert.Single(collected, item => item.Name == "setCommanderInventoryCargo");
        JArray items = Assert.IsType<JArray>(snapshot.Data);
        Assert.Contains(items.OfType<JObject>(), item => item.Value<string>("itemName") == "platinum");
        Assert.Contains(
            items.OfType<JObject>(),
            item => item.Value<string>("itemName") == "tea" && item.Value<int>("itemCount") == 3
        );
    }

    [Fact]
    public void MiningRefinedSessionStartDoesNotPublishInventorySnapshots()
    {
        var mapper = new InaraEventMapper();
        mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "Cargo",
                  "Vessel": "Ship",
                  "Inventory": [{ "Name": "tea", "Count": 2 }]
                }
                """
            ),
            Context,
            false
        );

        IReadOnlyList<InaraEvent> refined = mapper.Process(
            JObject.Parse(
                """
                {
                  "timestamp": "2026-07-28T12:01:00Z",
                  "event": "MiningRefined",
                  "Type": "platinum"
                }
                """
            ),
            Context,
            true
        );

        Assert.Contains(refined, item => item.Name == "getCommanderProfile");
        Assert.DoesNotContain(refined, item => item.Name == "setCommanderInventoryCargo");
        Assert.DoesNotContain(refined, item => item.Name == "setCommanderInventoryMaterials");
    }
}
