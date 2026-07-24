using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyStatisticsTests
{
    [Fact]
    public void CalculatesDistanceDistinctSystemsAndLegacyCounters()
    {
        var journey = CreateJourney(
        [
            Visit(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                JourneyCounts.Empty with
                {
                    BodyScans = 2,
                    BodyCount = 2,
                    Screenshots = 1,
                    Notes = 1,
                },
                new Dictionary<string, int> { ["Earth"] = 2 },
                new HashSet<long> { 10, 11 },
                new Dictionary<string, int> { ["Biology"] = 1 }),
            Visit(
                "Alpha",
                2,
                new GalacticCoordinate(3, 4, 0),
                JourneyCounts.Empty with
                {
                    BodyScans = 1,
                    BodyCount = 3,
                    Organisms = 2,
                    ExobiologyRewards = 1_000,
                }),
            Visit(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                JourneyCounts.Empty),
        ]);

        var result = JourneyStatistics.Calculate(journey);

        Assert.Equal(3, result.JumpCount);
        Assert.Equal(10, result.TotalDistance);
        Assert.Equal(2, result.UniqueSystemCount);
        Assert.Equal(1, result.FssCompletedSystemCount);
        Assert.Equal(3, result.Counts.BodyScans);
        Assert.Equal(1, result.Counts.Screenshots);
        Assert.Equal(1, result.LandedBodyCount);
        Assert.Equal(2, result.TotalLandingCount);
        Assert.Equal(2, result.CodexScanCount);
        Assert.Equal(1, result.SubCategoryCounts["Biology"]);
    }

    [Fact]
    public void EmptyJourneyReturnsZeroStatistics()
    {
        var result = JourneyStatistics.Calculate(CreateJourney([]));

        Assert.Equal(0, result.JumpCount);
        Assert.Equal(0, result.TotalDistance);
        Assert.Equal(JourneyCounts.Empty, result.Counts);
    }

    private static JourneyDocument CreateJourney(
        IReadOnlyList<JourneySystemVisit> visits)
    {
        return new JourneyDocument(
            "test",
            "test.json",
            "F123",
            "Drew",
            "Test",
            string.Empty,
            "Journal.test.log",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            null,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            visits);
    }

    private static JourneySystemVisit Visit(
        string name,
        long address,
        GalacticCoordinate position,
        JourneyCounts counts,
        IReadOnlyDictionary<string, int>? landedOn = null,
        IReadOnlySet<long>? codexScanned = null,
        IReadOnlyDictionary<string, int>? subCategories = null)
    {
        return new JourneySystemVisit(
            new JourneySystemReference(name, address, position),
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            null,
            counts,
            landedOn,
            codexScanned,
            null,
            null,
            subCategories,
            null,
            null);
    }
}
