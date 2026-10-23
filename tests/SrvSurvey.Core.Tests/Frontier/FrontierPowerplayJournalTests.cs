using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Frontier;

public sealed class FrontierPowerplayJournalTests
{
    [Fact]
    public void DailyJournalReadsModernRankAndAbsoluteMeritsForOnlyTheSelectedAccount()
    {
        FrontierPowerplaySnapshot? result = FrontierPowerplayJournal.Parse(
            """
            invalid JSON
            []
            {"event":"Fileheader"}
            {"timestamp":"2026-10-23T00:00:00Z","event":"Powerplay","Power":"Wrong","Rank":1}
            {"event":"Commander","FID":"F123"}
            {"timestamp":"2026-10-23T02:29:55Z","event":"Powerplay","Power":"A. Lavigny-Duval","Rank":160,"Merits":1260198}
            {"timestamp":"2026-10-23T02:30:00Z","event":"PowerplayMerits","Power":"A. Lavigny-Duval","MeritsGained":20,"TotalMerits":1260218}
            {"event":"Fileheader"}
            {"event":"LoadGame","FID":"F456"}
            {"timestamp":"2026-10-23T03:00:00Z","event":"Powerplay","Power":"Wrong","Rank":2}
            """,
            "f123"
        );

        Assert.NotNull(result);
        Assert.Equal("A. Lavigny-Duval", result.Power);
        Assert.Equal(160, result.Rank);
        Assert.Equal(1260218, result.Merits);
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-23T02:30:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            result.UpdatedAt
        );
    }

    [Theory]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Power":"A. Lavigny-Duval","Rank":161}""",
        "A. Lavigny-Duval",
        161,
        1260198L
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayLeave","Power":"A. Lavigny-Duval"}""",
        "",
        null,
        null
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayDefect","FromPower":"A. Lavigny-Duval","ToPower":"Aisling Duval"}""",
        "Aisling Duval",
        null,
        null
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayJoin","Power":"Aisling Duval"}""",
        "Aisling Duval",
        null,
        null
    )]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"Powerplay","Power":""}""", "", null, null)]
    public void AppliesPledgeTransitionsWithoutCarryingAnotherPowersRank(
        string json,
        string power,
        int? rank,
        long? merits
    )
    {
        FrontierPowerplaySnapshot? result = FrontierPowerplayJournal.Apply(Previous(), Entry(json));
        Assert.NotNull(result);
        Assert.Equal(power, result.Power);
        Assert.Equal(rank, result.Rank);
        Assert.Equal(merits, result.Merits);
    }

    [Theory]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayJoin","Power":"A. Lavigny-Duval"}""",
        null,
        null
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayJoin","Power":"A. Lavigny-Duval","Rank":2}""",
        2,
        null
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayDefect","ToPower":"A. Lavigny-Duval","Merits":10}""",
        null,
        10L
    )]
    public void NewMembershipCannotInheritValuesFromAnEarlierPledgeToTheSamePower(string json, int? rank, long? merits)
    {
        FrontierPowerplaySnapshot result = Assert.IsType<FrontierPowerplaySnapshot>(
            FrontierPowerplayJournal.Apply(Previous(), Entry(json))
        );

        Assert.Equal("A. Lavigny-Duval", result.Power);
        Assert.Equal(rank, result.Rank);
        Assert.Equal(merits, result.Merits);
    }

    [Theory]
    [InlineData("""{"event":"Powerplay","Power":"Wrong","Rank":1}""")]
    [InlineData("""{"timestamp":"2026-10-22T00:00:00Z","event":"PowerplayLeave"}""")]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"Rank","Power":"Wrong"}""")]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Rank":161}""")]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Power":"A. Lavigny-Duval","Rank":-1}"""
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Power":"A. Lavigny-Duval","Rank":2147483648}"""
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Power":"A. Lavigny-Duval","Rank":"161"}"""
    )]
    [InlineData(
        """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayMerits","Power":"A. Lavigny-Duval","MeritsGained":100}"""
    )]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayMerits","Power":42,"TotalMerits":100}""")]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Power":"   ","Rank":161}""")]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayMerits","Power":"","TotalMerits":10}""")]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayJoin","Power":"  "}""")]
    [InlineData("""{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayDefect","ToPower":""}""")]
    public void InvalidOrOlderEventsCannotEraseKnownStatus(string json)
    {
        FrontierPowerplaySnapshot previous = Previous();
        Assert.Same(previous, FrontierPowerplayJournal.Apply(previous, Entry(json)));
    }

    [Fact]
    public void DailyJournalCanSeedPartialUpdatesFromCachedStatus()
    {
        const string journal = """
            {"event":"LoadGame","FID":"F123"}
            {"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayMerits","Power":"A. Lavigny-Duval","TotalMerits":1260218}
            """;
        FrontierPowerplaySnapshot? result = FrontierPowerplayJournal.Parse(journal, "F123", Previous());
        Assert.NotNull(result);
        Assert.Equal(160, result.Rank);
        Assert.Equal(1260218, result.Merits);
        Assert.Null(FrontierPowerplayJournal.Parse(journal, "F456", Previous()));
        Assert.Null(FrontierPowerplayJournal.Parse("", "F123"));
    }

    private static FrontierPowerplaySnapshot Previous() =>
        new(
            "A. Lavigny-Duval",
            160,
            1260198,
            DateTimeOffset.Parse("2026-10-23T02:29:55Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );

    private static JournalEventEnvelope Entry(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }
}
