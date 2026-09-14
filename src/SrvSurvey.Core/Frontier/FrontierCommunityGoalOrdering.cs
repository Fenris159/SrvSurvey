using System.Globalization;

namespace SrvSurvey.Core.Frontier;

public static class FrontierCommunityGoalOrdering
{
    public static IReadOnlyList<FrontierCommunityGoalSnapshot> Order(IEnumerable<FrontierCommunityGoalSnapshot> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        FrontierCommunityGoalSnapshot[] materialized = goals.ToArray();
        IOrderedEnumerable<FrontierCommunityGoalSnapshot> active = materialized
            .Where(goal => !goal.IsComplete)
            .OrderBy(goal => goal.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(goal => goal.Title, StringComparer.CurrentCultureIgnoreCase);
        IOrderedEnumerable<FrontierCommunityGoalSnapshot> completed = materialized
            .Where(goal => goal.IsComplete)
            .OrderByDescending(CompletionTimestamp)
            .ThenByDescending(goal => goal.ExpiresAt ?? DateTimeOffset.MinValue)
            .ThenBy(goal => goal.Title, StringComparer.CurrentCultureIgnoreCase);
        return active.Concat(completed).ToArray();
    }

    private static DateTimeOffset CompletionTimestamp(FrontierCommunityGoalSnapshot goal)
    {
        foreach (string? path in new[] { "inara.lastUpdate", "journal.communityGoalTimestamp" })
        {
            string? value = goal
                .DataPoints?.FirstOrDefault(point =>
                    string.Equals(point.Path, path, StringComparison.OrdinalIgnoreCase)
                )
                ?.Value;
            if (
                DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset timestamp
                )
            )
            {
                return timestamp;
            }
        }

        return goal.ExpiresAt ?? DateTimeOffset.MinValue;
    }
}
