using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningSessionTrackerTests
{
    [Fact]
    public void SessionCountsRefiningRatherThanCargoTransfersAndExcludesPausedTime()
    {
        var tracker = new MiningSessionTracker();
        var start = DateTimeOffset.Parse(
            "2026-09-06T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        tracker.Start(start, "Sol", "Earth A Ring", "Python");
        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""
            )
        );
        tracker.Apply(Parse("""{"event":"MiningRefined","timestamp":"2026-09-06T12:02:00Z","Type":"platinum"}"""));
        tracker.Apply(
            Parse(
                """{"event":"CargoTransfer","timestamp":"2026-09-06T12:03:00Z","Transfers":[{"Type":"platinum","Count":20}]}"""
            )
        );
        tracker.Pause(start.AddMinutes(10));
        tracker.Resume(start.AddMinutes(20));
        MiningSession session = tracker.Stop(start.AddMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(20), session.ActiveDuration);
        Assert.Equal(1, session.RefinedTons);
        Assert.Equal(3, session.TonsPerHour);
        Assert.Single(session.Prospects);
        Assert.Equal(35, session.Prospects[0].Materials[0].Percentage);
    }

    [Fact]
    public void CoreOnlyMineralCountsOnceAndIsAlwaysAQualityHit()
    {
        var session = new MiningSession
        {
            Prospects =
            [
                new MiningProspect(DateTimeOffset.UtcNow, [new MiningMaterial("Osmium", 5)], "Monazite", "High"),
            ],
        };
        MiningMaterialSummary material = Assert.Single(
            session.Summarize(new Dictionary<string, double> { ["Monazite"] = 90 }),
            m => m.Name == "Monazite"
        );
        Assert.Equal(1, material.Finds);
        Assert.Equal(1, material.QualityHits);
        Assert.Equal(0, material.Average);
    }

    [Fact]
    public void DepletionUpdatesTheLatestProspectWithoutCountingAnotherAsteroid()
    {
        var tracker = new MiningSessionTracker();
        var start = DateTimeOffset.Parse(
            "2026-09-06T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        tracker.Start(start, "Sol", "Earth A Ring", "Python");

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":100}"""
            )
        );
        Assert.True(
            tracker.Apply(
                Parse(
                    """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":42.5}"""
                )
            )
        );

        MiningProspect prospect = Assert.Single(tracker.Current!.Prospects);
        Assert.Equal(start.AddMinutes(1), prospect.Time);
        Assert.Equal(42.5, prospect.Remaining);
        Assert.Equal(prospect, tracker.Current.ActiveProspect);
        Assert.Equal(1, tracker.Current.Asteroids);
    }

    [Fact]
    public void DepletionAndTravelReleaseTheCurrentProspectWithoutRemovingSessionHistory()
    {
        var tracker = new MiningSessionTracker();
        var start = DateTimeOffset.Parse(
            "2026-09-06T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        tracker.Start(start, "Sol", "Earth A Ring", "Python");

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""
            )
        );
        Assert.NotNull(tracker.Current!.ActiveProspect);
        Assert.True(tracker.Apply(Parse("""{"event":"SupercruiseEntry","timestamp":"2026-09-06T12:02:00Z"}""")));
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Single(tracker.Current.Prospects);

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:03:00Z","Materials":[{"Name":"Painite","Proportion":28}],"Remaining":100}"""
            )
        );
        Assert.True(
            tracker.Apply(Parse("""{"event":"FSDJump","timestamp":"2026-09-06T12:04:00Z","StarSystem":"Achenar"}"""))
        );
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Equal(2, tracker.Current.Prospects.Count);

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:05:00Z","Materials":[{"Name":"Osmium","Proportion":20}],"Remaining":100}"""
            )
        );
        Assert.True(
            tracker.Apply(Parse("""{"event":"StartJump","timestamp":"2026-09-06T12:06:00Z","JumpType":"Hyperspace"}"""))
        );
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Equal(3, tracker.Current.Prospects.Count);

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:07:00Z","Materials":[{"Name":"Rhodplumsite","Proportion":18}],"Remaining":100}"""
            )
        );
        Assert.True(
            tracker.Apply(
                Parse(
                    """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:08:00Z","Materials":[{"Name":"Rhodplumsite","Proportion":18}],"Remaining":0}"""
                )
            )
        );
        Assert.Null(tracker.Current.ActiveProspect);
        Assert.Equal(4, tracker.Current.Prospects.Count);
        Assert.Equal(0, tracker.Current.Prospects[^1].Remaining);
    }

    [Fact]
    public void MultipleProspectsPersistUntilTheirMatchingAsteroidIsDepleted()
    {
        var tracker = new MiningSessionTracker();
        var start = DateTimeOffset.Parse(
            "2026-09-06T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        tracker.Start(start, "Sol", "Earth A Ring", "Python");

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":100}"""
            )
        );
        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","Materials":[{"Name":"Osmium","Proportion":28}],"Content":"Medium","Remaining":100}"""
            )
        );

        Assert.Equal(2, tracker.Current!.ActiveProspects.Count);
        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:03:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":0}"""
            )
        );

        MiningProspect active = Assert.Single(tracker.Current.ActiveProspects);
        Assert.Equal("Osmium", Assert.Single(active.Materials).Name);
        Assert.Equal("Osmium", Assert.Single(tracker.Current.ActiveProspect!.Materials).Name);
        Assert.Equal(2, tracker.Current.Prospects.Count);
        Assert.Equal(0, tracker.Current.Prospects[0].Remaining);
    }

    [Fact]
    public void DepletionUpdatesTheMatchingHistoryEntryWhenActiveProspectsShareATimestamp()
    {
        var time = DateTimeOffset.Parse(
            "2026-09-06T12:01:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        static MiningProspect Prospect(DateTimeOffset timestamp) =>
            new(timestamp, [new MiningMaterial("Platinum", 35)], "", "High");
        var session = new MiningSession
        {
            Started = time.AddMinutes(-1),
            Prospects = [Prospect(time), Prospect(time)],
            ActiveProspects = [Prospect(time), Prospect(time)],
            ActiveProspect = Prospect(time),
        };
        var tracker = new MiningSessionTracker();
        tracker.Restore(session);

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":0}"""
            )
        );

        Assert.Equal(100, tracker.Current!.Prospects[0].Remaining);
        Assert.Equal(0, tracker.Current.Prospects[1].Remaining);
        Assert.Single(tracker.Current.ActiveProspects);
    }

    [Fact]
    public void DepletionOfRestoredOrphanUpdatesOnlyTheActiveProspect()
    {
        var time = DateTimeOffset.Parse(
            "2026-09-06T12:01:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        static MiningProspect Prospect(DateTimeOffset timestamp, double remaining = 100, string content = "High") =>
            new(timestamp, [new MiningMaterial("Platinum", 35)], "", content) { Remaining = remaining };
        MiningProspect orphan = Prospect(time);
        var session = new MiningSession
        {
            Started = time.AddMinutes(-1),
            Prospects = [Prospect(time.AddSeconds(-1)), Prospect(time, 90), Prospect(time, content: "Low")],
            ActiveProspects = [orphan],
            ActiveProspect = orphan,
        };
        var tracker = new MiningSessionTracker();
        tracker.Restore(session);

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":50}"""
            )
        );

        Assert.Equal([100, 90, 100], tracker.Current!.Prospects.Select(prospect => prospect.Remaining));
        Assert.Equal(50, Assert.Single(tracker.Current.ActiveProspects).Remaining);
    }

    [Fact]
    public void UnmatchedDepletedReportIsRecordedWithoutBecomingActive()
    {
        var tracker = new MiningSessionTracker();
        tracker.Start(
            DateTimeOffset.Parse("2026-09-06T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            "Sol",
            "Earth A Ring",
            "Python"
        );

        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":0}"""
            )
        );

        Assert.Equal(0, Assert.Single(tracker.Current!.Prospects).Remaining);
        Assert.Empty(tracker.Current.ActiveProspects);
        Assert.Null(tracker.Current.ActiveProspect);
    }

    [Fact]
    public void TravelClearsRestoredActiveCollectionWithoutLegacyPointer()
    {
        var time = DateTimeOffset.Parse(
            "2026-09-06T12:01:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var active = new MiningProspect(time, [new MiningMaterial("Platinum", 35)], "", "High");
        var tracker = new MiningSessionTracker();
        tracker.Restore(
            new MiningSession
            {
                Started = time.AddMinutes(-1),
                Prospects = [active],
                ActiveProspects = [active],
            }
        );

        Assert.True(tracker.Apply(Parse("""{"event":"SupercruiseEntry","timestamp":"2026-09-06T12:02:00Z"}""")));
        Assert.Empty(tracker.Current!.ActiveProspects);
    }

    [Fact]
    public void RestoreMigratesLegacyCurrentProspectIntoTheActiveCollection()
    {
        MiningProspect prospect = new(
            DateTimeOffset.Parse("2026-09-06T12:01:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            [new MiningMaterial("Platinum", 35)],
            "",
            "High"
        );
        var session = new MiningSession
        {
            Started = prospect.Time.AddMinutes(-1),
            Prospects = [prospect],
            ActiveProspect = prospect,
        };
        var tracker = new MiningSessionTracker();

        tracker.Restore(session);

        Assert.Equal(prospect, Assert.Single(tracker.Current!.ActiveProspects));
    }

    [Theory]
    [InlineData("Monazite", "High", "Platinum", 35.0)]
    [InlineData("", "Low", "Platinum", 35.0)]
    [InlineData("", "High", "Osmium", 35.0)]
    [InlineData("", "High", "Platinum", 34.0)]
    public void AmbiguousDepletionReportDoesNotRemoveAStillActiveProspect(
        string core,
        string content,
        string material,
        double proportion
    )
    {
        var tracker = new MiningSessionTracker();
        tracker.Start(
            DateTimeOffset.Parse("2026-09-06T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            "Sol",
            "Earth A Ring",
            "Python"
        );
        tracker.Apply(
            Parse(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Content":"High","Remaining":100}"""
            )
        );
        string update =
            $$"""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","MotherlodeMaterial":"{{core}}","Materials":[{"Name":"{{material}}","Proportion":{{proportion.ToString(global::System.Globalization.CultureInfo.InvariantCulture)}}}],"Content":"{{content}}","Remaining":50}""";

        tracker.Apply(Parse(update));

        Assert.Equal(2, tracker.Current!.ActiveProspects.Count);
        Assert.Equal(2, tracker.Current.Prospects.Count);
    }

    [Fact]
    public void RestoreAndTravelIgnoreEndedOrEmptySessions()
    {
        var tracker = new MiningSessionTracker();
        tracker.Restore(
            new MiningSession { Started = DateTimeOffset.UtcNow.AddMinutes(-1), Ended = DateTimeOffset.UtcNow }
        );
        Assert.Null(tracker.Current);

        tracker.Start(DateTimeOffset.UtcNow, "Sol", "Earth A Ring", "Python");
        Assert.False(tracker.Apply(Parse("""{"event":"SupercruiseEntry"}""")));
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }
}
