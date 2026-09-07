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

    [Fact]
    public void CoreOnlyMineralCountsOnceAndIsAlwaysAQualityHit()
    {
        var session = new MiningSession { Prospects = [new MiningProspect(DateTimeOffset.UtcNow, [new MiningMaterial("Osmium", 5)], "Monazite", "High")] };
        var material = Assert.Single(session.Summarize(new Dictionary<string, double> { ["Monazite"] = 90 }), m => m.Name == "Monazite");
        Assert.Equal(1, material.Finds); Assert.Equal(1, material.QualityHits);
        Assert.Equal(0, material.Average);
    }

    [Fact]
    public void DepletionUpdatesTheLatestProspectWithoutCountingAnotherAsteroid()
    {
        var tracker = new MiningSessionTracker();
        var start = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        tracker.Start(start, "Sol", "Earth A Ring", "Python");

        tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":100}"""));
        Assert.True(tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":42.5}""")));

        var prospect = Assert.Single(tracker.Current!.Prospects);
        Assert.Equal(start.AddMinutes(1), prospect.Time);
        Assert.Equal(42.5, prospect.Remaining);
        Assert.Equal(prospect, tracker.Current.ActiveProspect);
        Assert.Equal(1, tracker.Current.Asteroids);
    }

    [Fact]
    public void DepletionAndTravelReleaseTheCurrentProspectWithoutRemovingSessionHistory()
    {
        var tracker = new MiningSessionTracker();
        var start = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        tracker.Start(start, "Sol", "Earth A Ring", "Python");

        tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""));
        Assert.NotNull(tracker.Current!.ActiveProspect);
        Assert.True(tracker.Apply(Parse("""{"event":"SupercruiseEntry","timestamp":"2026-09-06T12:02:00Z"}""")));
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Single(tracker.Current.Prospects);

        tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:03:00Z","Materials":[{"Name":"Painite","Proportion":28}],"Remaining":100}"""));
        Assert.True(tracker.Apply(Parse("""{"event":"FSDJump","timestamp":"2026-09-06T12:04:00Z","StarSystem":"Achenar"}""")));
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Equal(2, tracker.Current.Prospects.Count);

        tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:05:00Z","Materials":[{"Name":"Osmium","Proportion":20}],"Remaining":100}"""));
        Assert.True(tracker.Apply(Parse("""{"event":"StartJump","timestamp":"2026-09-06T12:06:00Z","JumpType":"Hyperspace"}""")));
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Equal(3, tracker.Current.Prospects.Count);

        tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:07:00Z","Materials":[{"Name":"Rhodplumsite","Proportion":18}],"Remaining":100}"""));
        Assert.True(tracker.Apply(Parse("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:08:00Z","Materials":[{"Name":"Rhodplumsite","Proportion":18}],"Remaining":0}""")));
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Equal(4, tracker.Current.Prospects.Count);
        Assert.Equal(0, tracker.Current.Prospects[^1].Remaining);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var entry, out _));
        return entry!;
    }
}
