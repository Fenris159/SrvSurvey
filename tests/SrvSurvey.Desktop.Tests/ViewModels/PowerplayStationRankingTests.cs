using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class PowerplayStationRankingTests
{
    [Fact]
    public void MedianWinsOutsideTheStationCountTieBand()
    {
        var smallHigh = PowerplayStationRanking.FromScores([100]);
        var smallLow = PowerplayStationRanking.FromScores([91]);
        var manyHigh = PowerplayStationRanking.FromScores(Enumerable.Repeat(100L, 20).ToArray());
        var manyLow = PowerplayStationRanking.FromScores(Enumerable.Repeat(96L, 20).ToArray());

        Assert.True(PowerplayStationRanking.CompareDescending(smallHigh, smallLow) < 0);
        Assert.True(PowerplayStationRanking.CompareDescending(manyHigh, manyLow) < 0);
    }

    [Fact]
    public void P90AndNearbyStationCountBreakMedianTies()
    {
        var steady = PowerplayStationRanking.FromScores([100, 100, 100, 110]);
        var peak = PowerplayStationRanking.FromScores([100, 100, 100, 140]);
        var sparse = PowerplayStationRanking.FromScores([90, 90, 100, 100]);
        var plentiful = PowerplayStationRanking.FromScores([96, 96, 100, 100]);

        Assert.True(PowerplayStationRanking.CompareDescending(peak, steady) < 0);
        Assert.Equal(100, sparse.P90);
        Assert.Equal(100, plentiful.P90);
        Assert.True(PowerplayStationRanking.CompareDescending(plentiful, sparse) < 0);
    }

    [Fact]
    public void MedianDifferenceInsideTheFivePercentBandDefersToP90()
    {
        var highMedian = PowerplayStationRanking.FromScores([100, 100, 100, 100, 100]);
        var highPeak = PowerplayStationRanking.FromScores([96, 96, 96, 96, 160]);

        Assert.True(PowerplayStationRanking.CompareDescending(highPeak, highMedian) < 0);
    }
}
