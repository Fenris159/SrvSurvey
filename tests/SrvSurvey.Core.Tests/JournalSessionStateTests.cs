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
        Assert.Equal(4, snapshot.ValidLineCount);
        Assert.Equal(3, snapshot.RecognizedEventCount);
        Assert.Equal(1, state.UnhandledEventCount);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-24T10:00:03Z"),
            snapshot.LastEventTimestamp);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(json, out var journalEvent, out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
