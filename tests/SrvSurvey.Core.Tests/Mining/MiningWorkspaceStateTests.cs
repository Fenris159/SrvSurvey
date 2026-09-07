using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningWorkspaceStateTests
{
    [Fact]
    public void BootstrapDoesNotAutoStartButLiveProspectorDoesAndReplayedEventsAreNotCountedTwice()
    {
        var state = new MiningWorkspaceState(new MiningCommanderData());
        var entry = Parse("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:01:00Z","Type":"Prospector"}""");
        state.Apply(entry, true, "Sol", "Ring", "Python");
        Assert.Null(state.Session.Current);
        var live = Parse("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:02:00Z","Type":"Prospector"}""");
        state.Apply(live, false, "Sol", "Ring", "Python");
        state.Apply(live, false, "Sol", "Ring", "Python");
        Assert.Equal(1, state.Session.Current?.ProspectorLimpets);
        var restored = new MiningWorkspaceState(state.Data);
        restored.Apply(live, true, "Sol", "Ring", "Python");
        Assert.Equal(1, restored.Session.Current?.ProspectorLimpets);
    }
    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var result, out _));
        return result!;
    }
}
