namespace SrvSurvey.Desktop.ViewModels;

internal readonly record struct PowerplayStationRanking(double Median, double P90, int NearP90Count, int StationCount)
{
    public static PowerplayStationRanking FromScores(IReadOnlyList<long> stationScores)
    {
        if (stationScores.Count == 0)
        {
            return default;
        }

        long[] sorted = stationScores.Order().ToArray();
        double median = Percentile(sorted, 0.5);
        double p90 = Percentile(sorted, 0.9);
        int nearP90Count = sorted.Count(score => score >= 0.95 * p90);
        return new PowerplayStationRanking(median, p90, nearP90Count, sorted.Length);
    }

    public static int CompareDescending(PowerplayStationRanking left, PowerplayStationRanking right)
    {
        double medianBand = 0.05;
        if (left.StationCount < 5 || right.StationCount < 5)
        {
            medianBand = 0.08;
        }
        else if (left.StationCount >= 20 && right.StationCount >= 20)
        {
            medianBand = 0.03;
        }
        if (RelativeDifference(left.Median, right.Median) >= medianBand)
        {
            return right.Median.CompareTo(left.Median);
        }

        if (RelativeDifference(left.P90, right.P90) >= 0.05)
        {
            return right.P90.CompareTo(left.P90);
        }

        return right.NearP90Count.CompareTo(left.NearP90Count);
    }

    private static double RelativeDifference(double left, double right) =>
        Math.Abs(left - right) / Math.Max(Math.Max(left, right), 1e-9);

    private static double Percentile(long[] sorted, double proportion)
    {
        double position = (sorted.Length - 1) * proportion;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
