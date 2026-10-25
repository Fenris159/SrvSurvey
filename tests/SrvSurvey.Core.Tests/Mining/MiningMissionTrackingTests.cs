using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningMissionTrackingTests
{
    [Theory]
    [InlineData("Mission_Mining")]
    [InlineData("Mission_MiningRush")]
    [InlineData("Mission_Mining_Wing")]
    public void MiningVariantsDefaultToTrackedAndUseAcceptanceLocation(string name)
    {
        var tracker = new MiningMissionTracker();
        JournalEventEnvelope entry = Event(
            $$"""{"event":"MissionAccepted","MissionID":42,"Name":"{{name}}","Commodity":"$platinum_name;","Count":10,"Reward":500000}"""
        );
        Assert.True(tracker.Apply(entry, "Sol", "Abraham Lincoln"));
        MiningMission mission = Assert.Single(tracker.Missions);
        Assert.True(mission.IsTracked);
        Assert.Equal("Platinum", mission.Commodity);
        Assert.Equal("Sol", mission.DestinationSystem);
        Assert.Equal("Abraham Lincoln", mission.DestinationStation);
        Assert.Equal("Sol / Abraham Lincoln", mission.Destination);
        Assert.Equal(500000, mission.Reward);
        mission.IsTracked = false;
        Assert.False(tracker.Apply(entry));
        Assert.False(mission.IsTracked);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"event":"MissionAccepted","MissionID":"42"}""")]
    [InlineData(
        """{"event":"MissionAccepted","MissionID":42,"Name":"Mission_Delivery","Commodity":"gold","Count":10}"""
    )]
    [InlineData("""{"event":"MissionAccepted","MissionID":42,"Name":"Mission_Mining","Commodity":"gold","Count":0}""")]
    [InlineData("""{"event":"MissionAccepted","MissionID":42,"Name":"Mission_Mining","Count":10}""")]
    [InlineData("""{"event":"CargoDepot","MissionID":42,"UpdateType":"Deliver"}""")]
    public void RejectsUnrelatedOrIncompleteMissionData(string json)
    {
        // Envelopes need an event, but malformed mission fields must not disrupt the projection.
        var tracker = new MiningMissionTracker();
        if (json == "{}")
        {
            json = """{"event":"LoadGame"}""";
        }
        Assert.False(tracker.Apply(Event(json)));
        Assert.Empty(tracker.Missions);
    }

    [Theory]
    [InlineData("MissionCompleted", "Completed", 10)]
    [InlineData("MissionAbandoned", "Abandoned", 0)]
    [InlineData("MissionFailed", "Failed", 0)]
    public void LifecycleAndCanonicalCargoKeepDuplicateAllocationsSeparate(
        string eventName,
        string status,
        int delivered
    )
    {
        var tracker = new MiningMissionTracker();
        tracker.Apply(
            Event(
                """{"event":"MissionAccepted","MissionID":1,"Name":"Mission_Mining","Commodity":"$lowtemperaturediamond_name;","Count":10,"DestinationSystem":"Achenar","DestinationStation":"Dawes Hub"}"""
            ),
            "Sol",
            "Galileo"
        );
        tracker.Apply(
            Event(
                """{"event":"MissionAccepted","MissionID":2,"Name":"Mission_Mining","Commodity":"Low Temperature Diamonds","Count":10}"""
            )
        );
        tracker.Missions[0].IsTracked = false;
        tracker.UpdateCargo([new("lowtemperaturediamond", null, 12, 0)]);
        Assert.Equal(10, tracker.Missions[0].OnBoard);
        Assert.Equal(2, tracker.Missions[1].OnBoard);
        Assert.Equal("Achenar", tracker.Missions[0].DestinationSystem);
        Assert.Equal("Dawes Hub", tracker.Missions[0].DestinationStation);
        Assert.False(
            tracker.Apply(Event("""{"event":"CargoDepot","MissionID":1,"UpdateType":"Collect","ItemsDelivered":4}"""))
        );
        Assert.False(tracker.Apply(Event("""{"event":"Unknown","MissionID":1}""")));
        Assert.True(tracker.Apply(Event($$"""{"event":"{{eventName}}","MissionID":1}""")));
        tracker.UpdateCargo([new("lowtemperaturediamond", null, 12, 0)]);
        Assert.Equal(status, tracker.Missions[0].Status);
        Assert.Equal(delivered, tracker.Missions[0].Delivered);
        Assert.Equal(0, tracker.Missions[0].OnBoard);
        Assert.Equal(10, tracker.Missions[1].OnBoard);
    }

    [Fact]
    public void TrackingAndDestinationsSurviveExportAndImportWithoutOverwritingLocalChoices()
    {
        MiningCommanderData legacy = MiningStore.Parse(
            """{"SchemaVersion":1,"Missions":[{"Id":1,"Commodity":"Platinum","Required":10,"Destination":"Sol / Galileo"}]}"""
        );
        Assert.True(legacy.Missions[0].IsTracked);
        legacy.Missions[0].IsTracked = false;
        var state = new MiningWorkspaceState(MiningStore.Parse(MiningStore.Export(legacy)));
        state.Import(
            new MiningCommanderData
            {
                Missions = [new() { Id = 1, IsTracked = true }, new() { Id = 2, Commodity = "Gold" }],
            }
        );
        Assert.False(state.Missions.Missions[0].IsTracked);
        Assert.True(state.Missions.Missions[1].IsTracked);
        Assert.Equal("Sol / Galileo", state.Missions.Missions[0].Destination);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            MiningStore.Parse("""{"SchemaVersion":1,"Missions":[{"DestinationSystem":null}]}""")
        );
    }

    [Fact]
    public void ExpiredMissionsCannotConsumeCargo()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var tracker = new MiningMissionTracker();
        tracker.Missions.Add(
            new()
            {
                Commodity = "Gold",
                Required = 10,
                Expiry = now.AddSeconds(-1).ToString("O"),
            }
        );
        tracker.Missions.Add(
            new()
            {
                Commodity = "Gold",
                Required = 10,
                Expiry = now.AddDays(1).ToString("O"),
            }
        );
        tracker.UpdateCargo([new("gold", null, 5, 0)], now);
        Assert.Equal(0, tracker.Missions[0].OnBoard);
        Assert.Equal(5, tracker.Missions[1].OnBoard);
    }

    [Theory]
    [InlineData("Destination")]
    [InlineData("DestinationSystem")]
    [InlineData("DestinationStation")]
    [InlineData("Expiry")]
    [InlineData("Status")]
    public void CorruptMissionStringsCannotEnterTheWorkspace(string property)
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            MiningStore.Parse($$"""{"SchemaVersion":1,"Missions":[{"{{property}}":null}]}""")
        );
    }

    [Fact]
    public void ARemoteDestinationDoesNotInheritTheAcceptanceStation()
    {
        var tracker = new MiningMissionTracker();
        Assert.True(
            tracker.Apply(
                Event(
                    """{"event":"MissionAccepted","MissionID":1,"Name":"Mission_MiningRush","Commodity":"gold","Count":10,"DestinationSystem":"Achenar"}"""
                ),
                "Sol",
                "Galileo"
            )
        );
        MiningMission mission = Assert.Single(tracker.Missions);
        Assert.Equal("Achenar", mission.DestinationSystem);
        Assert.Empty(mission.DestinationStation);
        Assert.Equal("Achenar", mission.Destination);
        Assert.True(
            tracker.Apply(Event("""{"event":"CargoDepot","MissionID":1,"UpdateType":"Deliver","ItemsDelivered":99}"""))
        );
        Assert.Equal(10, mission.Delivered);
        Assert.Equal(0, mission.Remaining);
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }
}
