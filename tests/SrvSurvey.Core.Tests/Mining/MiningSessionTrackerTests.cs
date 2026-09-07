using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningSessionTrackerTests
{
    [Fact]
    public void SessionCountsRefiningRatherThanCargoTransfersAndExcludesPausedTime()
    {
        var tracker = new MiningSessionTracker();
        var start = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        tracker.Start(start, "Sol", "Earth A Ring", "Python");
        tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""));
        tracker.Apply(Parse("""{"event":"MiningRefined","timestamp":"2026-09-06T12:02:00Z","Type":"platinum"}"""));
        tracker.Apply(Parse("""{"event":"CargoTransfer","timestamp":"2026-09-06T12:03:00Z","Transfers":[{"Type":"platinum","Count":20}]}"""));
        tracker.Pause(start.AddMinutes(10));
        tracker.Resume(start.AddMinutes(20));
        var session = tracker.Stop(start.AddMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(20), session.ActiveDuration);
        Assert.Equal(1, session.RefinedTons);
        Assert.Equal(3, session.TonsPerHour);
        Assert.Single(session.Prospects);
        Assert.Equal(35, session.Prospects[0].Materials[0].Percentage);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var entry, out _));
        return entry!;
    }
}
