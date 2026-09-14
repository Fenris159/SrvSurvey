namespace SrvSurvey.Core.Journeys;

public static class JourneyStatistics
{
    public static JourneyQuickStatistics Calculate(JourneyDocument journey)
    {
        ArgumentNullException.ThrowIfNull(journey);
        JourneyCounts counts = JourneyCounts.Empty;
        var uniqueSystems = new HashSet<string>(StringComparer.Ordinal);
        var categoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        double totalDistance = 0d;
        int landedBodies = 0;
        int totalLandings = 0;
        int codexScans = 0;
        int fssCompleted = 0;
        JourneySystemReference? previous = null;

        foreach (JourneySystemVisit visit in journey.VisitedSystems)
        {
            uniqueSystems.Add(visit.StarSystem.Name);
            counts += visit.Counts;
            if (previous is not null)
            {
                totalDistance += visit.StarSystem.DistanceTo(previous);
            }

            previous = visit.StarSystem;
            landedBodies += visit.LandedOn?.Count ?? 0;
            totalLandings += visit.LandedOn?.Values.Sum() ?? 0;
            codexScans += visit.CodexScanned?.Count ?? 0;
            if (visit.HasCompletedFss)
            {
                fssCompleted++;
            }

            if (visit.SubCategories is null)
            {
                continue;
            }

            foreach ((string? name, int count) in visit.SubCategories)
            {
                categoryCounts[name] = categoryCounts.GetValueOrDefault(name) + count;
            }
        }

        return new JourneyQuickStatistics(
            journey.VisitedSystems.Count,
            totalDistance,
            uniqueSystems.Count,
            fssCompleted,
            counts,
            landedBodies,
            totalLandings,
            codexScans,
            categoryCounts
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
        );
    }
}

public sealed record JourneyQuickStatistics(
    int JumpCount,
    double TotalDistance,
    int UniqueSystemCount,
    int FssCompletedSystemCount,
    JourneyCounts Counts,
    int LandedBodyCount,
    int TotalLandingCount,
    int CodexScanCount,
    IReadOnlyDictionary<string, int> SubCategoryCounts
);
