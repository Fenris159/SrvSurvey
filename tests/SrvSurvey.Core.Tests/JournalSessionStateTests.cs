using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests;

public sealed class JournalSessionStateTests
{
    [Fact]
    public void ApplyBuildsStateIncrementallyAndObservesUnknownEvents()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"Commander","Name":"Drew","FID":"F123"}""")));
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:01Z","event":"Location","StarSystem":"Sol","SystemAddress":"10477373803","StarPos":[0,0,0],"Body":"Earth","BodyType":"Planet"}""")));
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:01Z","event":"Loadout","Ship":"mandalay"}""")));
        Assert.False(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:02Z","event":"FutureEvent","Value":42}""")));
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:03Z","event":"Shutdown"}""")));

        var snapshot = state.CreateSnapshot("Journal.fixture.log");
        Assert.Equal("Drew", snapshot.CommanderName);
        Assert.Equal("F123", snapshot.FrontierId);
        Assert.Equal("Sol", snapshot.SystemName);
        Assert.Equal(10477373803, snapshot.SystemAddress);
        Assert.Equal(new GalacticCoordinate(0, 0, 0), snapshot.StarPosition);
        Assert.Equal("Earth", snapshot.BodyName);
        Assert.True(snapshot.IsShutdown);
        Assert.Equal("mandalay", state.ShipType);
        Assert.Equal(5, snapshot.ValidLineCount);
        Assert.Equal(4, snapshot.RecognizedEventCount);
        Assert.Equal(1, state.UnhandledEventCount);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-24T10:00:03Z"),
            snapshot.LastEventTimestamp);
    }

    [Fact]
    public void VehicleLaunchAndDockEventsPreserveVrCalibrationMode()
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"LaunchSRV","SRVType":"combat_multicrew_srv_01"}""")));
        Assert.Equal("combat_multicrew_srv_01", state.ActiveSrvType);
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:01Z","event":"LaunchFighter"}""")));
        Assert.True(state.IsFighterLaunched);

        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:02Z","event":"DockFighter"}""")));
        Assert.False(state.IsFighterLaunched);
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:03Z","event":"DockSRV"}""")));
        Assert.Null(state.ActiveSrvType);
    }

    [Theory]
    [InlineData("flightsuit_class1", OdysseySuitType.Flight)]
    [InlineData("explorationsuit_class5", OdysseySuitType.Artemis)]
    [InlineData("utilitysuit_class3", OdysseySuitType.Maverick)]
    [InlineData("tacticalsuit_class5", OdysseySuitType.Dominator)]
    [InlineData("future_suit", OdysseySuitType.Unknown)]
    public void SuitLoadoutsTrackLegacySuitCategories(
        string suitName,
        OdysseySuitType expected)
    {
        var state = new JournalSessionState();

        Assert.True(state.Apply(Parse(
            $$"""{"event":"SuitLoadout","SuitName":"{{suitName}}"}""")));
        Assert.Equal(expected, state.CurrentSuit);

        Assert.True(state.Apply(Parse(
            """{"event":"SwitchSuitLoadout","SuitName":"tacticalsuit_class2"}""")));
        Assert.Equal(OdysseySuitType.Dominator, state.CurrentSuit);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(json, out var journalEvent, out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
