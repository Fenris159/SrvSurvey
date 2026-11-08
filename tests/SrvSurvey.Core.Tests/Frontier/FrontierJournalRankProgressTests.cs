using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Frontier;

public sealed class FrontierJournalRankProgressTests
{
    [Theory]
    [InlineData("Combat", "combat")]
    [InlineData("Trade", "trade")]
    [InlineData("Explore", "explore")]
    [InlineData("Soldier", "soldier")]
    [InlineData("Exobiologist", "exobiologist")]
    [InlineData("CQC", "cqc")]
    [InlineData("Federation", "federation")]
    [InlineData("Empire", "empire")]
    public void PercentagesRetainPrecisionAndNeverUseTheRankOrdinal(string journalKey, string capiKey)
    {
        var tracker = new FrontierJournalRankProgress();
        var rank = new FrontierRankSnapshot(capiKey, journalKey, 2, "Current rank");
        Assert.Null(tracker.Project(rank, DateTimeOffset.UnixEpoch).ProgressPercent);
        tracker.Apply(Parse($$"""{"timestamp":"1970-01-01T00:01:00Z","event":"Rank","{{journalKey}}":2}"""));
        Assert.Null(tracker.Project(rank, DateTimeOffset.UnixEpoch).ProgressPercent);
        tracker.Apply(Parse($$"""{"timestamp":"1970-01-01T00:02:00Z","event":"Progress","{{journalKey}}":37.625}"""));

        Assert.Equal(37.625, tracker.Project(rank, DateTimeOffset.UnixEpoch).ProgressPercent);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(145, 100)]
    public void PercentageBarsStayWithinBoundsIncludingBankedNavyProgress(int reported, int expected)
    {
        var tracker = new FrontierJournalRankProgress();
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:01:00Z","event":"Rank","Federation":12}"""));
        tracker.Apply(Parse($$"""{"timestamp":"1970-01-01T00:02:00Z","event":"Progress","Federation":{{reported}}}"""));

        Assert.Equal(
            expected,
            tracker
                .Project(new("federation", "Federation", 12, "Rear Admiral"), DateTimeOffset.UnixEpoch)
                .ProgressPercent
        );
    }

    [Fact]
    public void PromotionsDiscardOldPercentagesAndStaleEventsCannotUndoThem()
    {
        var tracker = new FrontierJournalRankProgress();
        var rank = new FrontierRankSnapshot("combat", "Combat", 6, "Dangerous");
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:01:00Z","event":"Rank","Combat":6}"""));
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:02:00Z","event":"Progress","Combat":99}"""));
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:03:00Z","event":"Promotion","Combat":7}"""));
        FrontierRankSnapshot promoted = tracker.Project(rank, DateTimeOffset.UnixEpoch);
        Assert.Equal("Deadly", promoted.Name);
        Assert.Equal(7, promoted.Level);
        Assert.Null(promoted.ProgressPercent);
        Assert.False(tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:02:30Z","event":"Progress","Combat":99}""")));
        Assert.Null(tracker.Project(rank, DateTimeOffset.UnixEpoch).ProgressPercent);
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:04:00Z","event":"Progress","Combat":0.25}"""));
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:05:00Z","event":"Rank","Combat":7}"""));
        Assert.Equal(0.25, tracker.Project(rank, DateTimeOffset.UnixEpoch).ProgressPercent);
        Assert.Equal(rank, tracker.Project(rank, DateTimeOffset.UnixEpoch.AddHours(1)));
    }

    [Theory]
    [InlineData("Progress", "37.5", "1970-01-01T00:01:00Z")]
    [InlineData("Rank", "-1", "1970-01-01T00:01:00Z")]
    [InlineData("Rank", "2.5", "1970-01-01T00:01:00Z")]
    [InlineData("Rank", "\"invalid\"", "1970-01-01T00:01:00Z")]
    [InlineData("Progress", "1e999", "1970-01-01T00:01:00Z")]
    [InlineData("Rank", "2", "invalid")]
    [InlineData("LoadGame", "2", "1970-01-01T00:01:00Z")]
    public void MissingLevelsAndInvalidObservationsCannotInventProgress(
        string eventName,
        string value,
        string timestamp
    )
    {
        var tracker = new FrontierJournalRankProgress();
        tracker.Apply(
            Parse($$"""{"timestamp":"{{timestamp}}","event":"{{eventName}}","Combat":{{value}},"Unrecognized":50}""")
        );
        var rank = new FrontierRankSnapshot("combat", "Combat", 6, "Dangerous");

        Assert.Equal(rank, tracker.Project(rank, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("combat", 13)]
    [InlineData("trade", 13)]
    [InlineData("explore", 13)]
    [InlineData("soldier", 13)]
    [InlineData("exobiologist", 13)]
    [InlineData("cqc", 8)]
    [InlineData("federation", 14)]
    [InlineData("empire", 14)]
    public void HighestRanksHaveNoNextRank(string key, int highest)
    {
        Assert.True(new FrontierRankSnapshot(key, "", highest, "").IsHighestRank);
        Assert.False(new FrontierRankSnapshot(key, "", highest - 1, "").IsHighestRank);
        Assert.False(new FrontierRankSnapshot("power", "Powerplay", 161, "").IsHighestRank);
    }

    [Fact]
    public void NewProgressCannotMakeAnOlderLevelOverrideANewerCapiRank()
    {
        var tracker = new FrontierJournalRankProgress();
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:01:00Z","event":"Rank","Combat":6}"""));
        tracker.Apply(Parse("""{"timestamp":"1970-01-01T00:03:00Z","event":"Progress","Combat":50}"""));
        var current = new FrontierRankSnapshot("combat", "Combat", 7, "Deadly");

        Assert.Equal(current, tracker.Project(current, DateTimeOffset.UnixEpoch.AddMinutes(2)));
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return Assert.IsType<JournalEventEnvelope>(entry);
    }
}
