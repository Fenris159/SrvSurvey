using System.Globalization;

namespace SrvSurvey.Core.Frontier;

public static class FrontierCommunityGoalOrdering
{
    public static IReadOnlyList<FrontierCommunityGoalSnapshot> Order(
        IEnumerable<FrontierCommunityGoalSnapshot> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        var materialized = goals.ToArray();
        var active = materialized
            .Where(goal => !goal.IsComplete)
            .OrderBy(goal => goal.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(
                goal => goal.Title,
                StringComparer.CurrentCultureIgnoreCase);
        var completed = materialized
            .Where(goal => goal.IsComplete)
            .OrderByDescending(CompletionTimestamp)
            .ThenByDescending(goal => goal.ExpiresAt ?? DateTimeOffset.MinValue)
            .ThenBy(
                goal => goal.Title,
                StringComparer.CurrentCultureIgnoreCase);
        return active.Concat(completed).ToArray();
    }

    private static DateTimeOffset CompletionTimestamp(
        FrontierCommunityGoalSnapshot goal)
    {
        foreach (var path in new[]
        {
            "inara.lastUpdate",
            "journal.communityGoalTimestamp",
        })
        {
            var value = goal.DataPoints?.FirstOrDefault(point => string.Equals(
                point.Path,
                path,
                StringComparison.OrdinalIgnoreCase))?.Value;
            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var timestamp))
            {
                return timestamp;
            }
        }

        return goal.ExpiresAt ?? DateTimeOffset.MinValue;
    }
}
