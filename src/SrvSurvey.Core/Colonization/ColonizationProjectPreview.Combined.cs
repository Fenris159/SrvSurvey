namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationProjectPreview
{
    /// <summary>Combines distinct builds, summing demand and deliveries while counting each physical carrier once.</summary>
    public static ColonizationProjectPreview CreateCombined(
        IEnumerable<ColonizationProjectPreviewData> data,
        DateTimeOffset fetchedAt
    )
    {
        ArgumentNullException.ThrowIfNull(data);
        ColonizationProjectPreview[] builds = data.DistinctBy(
                item => item.Project.BuildId,
                StringComparer.OrdinalIgnoreCase
            )
            .Select(item => new ColonizationProjectPreview(item, fetchedAt))
            .ToArray();
        if (builds.Length == 0)
        {
            throw new ArgumentException("A combined report requires at least one build.", nameof(data));
        }
        return new ColonizationProjectPreview(fetchedAt, builds);
    }

    /// <summary>Uses the last available whole cargo snapshot for shared carriers instead of adding duplicate stock responses.</summary>
    private ColonizationProjectPreview(DateTimeOffset fetchedAt, IReadOnlyList<ColonizationProjectPreview> builds)
    {
        IsCombined = true;
        FetchedAt = fetchedAt;
        Projects = builds.Select(build => build.Project).ToArray();
        Project = Projects[0];
        MaximumRequired = builds.Sum(build => build.MaximumRequired);
        Delivered = builds.Sum(build => build.Delivered);
        Statistics = CombineStatistics(builds);
        Carriers = builds
            .SelectMany(build => build.Carriers)
            .GroupBy(carrier => carrier.MarketId)
            .Select(group => group.LastOrDefault(carrier => carrier.Cargo is not null) ?? group.Last())
            .ToArray();
        var needs = builds
            .SelectMany(build => build.Rows)
            .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Need), StringComparer.OrdinalIgnoreCase);
        Rows = CreateRows(needs, Carriers);
        Remaining = Rows.Sum(row => row.Need);
        CarrierDeficit = CalculateDeficit(Rows, Carriers);
    }

    /// <summary>Aggregates complete histories by commander and UTC bucket; missing history never produces misleading partial totals.</summary>
    private static ColonizationProjectStatistics? CombineStatistics(IReadOnlyList<ColonizationProjectPreview> builds)
    {
        if (builds.Any(build => build.Statistics is null))
        {
            return null;
        }
        ColonizationProjectStatistics[] statistics = builds.Select(build => build.Statistics!).ToArray();
        return new ColonizationProjectStatistics
        {
            TotalCargo = statistics.Sum(item => item.TotalCargo),
            TotalDeliveries = statistics.Sum(item => item.TotalDeliveries),
            Start = statistics.Min(item => item.Start),
            End = statistics.Max(item => item.End),
            Cmdrs = SumContributions(statistics.SelectMany(item => item.Cmdrs)),
            Stats = statistics
                .SelectMany(item => item.Stats)
                .GroupBy(bucket => bucket.Time.ToUniversalTime())
                .OrderBy(group => group.Key)
                .Select(group => new ColonizationDeliveryBucket
                {
                    Time = group.Key,
                    Cmdrs = SumContributions(group.SelectMany(bucket => bucket.Cmdrs)),
                })
                .ToList(),
        };
    }

    /// <summary>Merges a commander's contributions across builds without case-sensitive duplicates.</summary>
    private static Dictionary<string, long> SumContributions(IEnumerable<KeyValuePair<string, long>> contributions) =>
        contributions
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value), StringComparer.OrdinalIgnoreCase);
}
