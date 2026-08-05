using System.Globalization;
using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Desktop.Platform.Inara;

public static class InaraCommunityGoalEnricher
{
    private static readonly TimeSpan ExpiryTolerance = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<FrontierCommunityGoalSnapshot> Enrich(
        IReadOnlyList<FrontierCommunityGoalSnapshot>? frontierGoals,
        InaraCommunityGoalsResult inara)
    {
        ArgumentNullException.ThrowIfNull(inara);
        var source = (frontierGoals ?? [])
            .Where(goal => !IsPriorInaraOnlyGoal(goal))
            .ToArray();
        var matched = new HashSet<int>();
        var result = new List<FrontierCommunityGoalSnapshot>(
            source.Length + inara.Goals.Count);
        foreach (var frontier in source)
        {
            var matchIndex = FindMatch(frontier, source, inara.Goals, matched);
            if (matchIndex is null)
            {
                result.Add(frontier);
                continue;
            }

            matched.Add(matchIndex.Value);
            result.Add(Merge(frontier, inara.Goals[matchIndex.Value], inara.FetchedAt));
        }

        for (var index = 0; index < inara.Goals.Count; index++)
        {
            if (!matched.Contains(index))
            {
                result.Add(Create(inara.Goals[index], inara.FetchedAt));
            }
        }

        return FrontierCommunityGoalOrdering.Order(result);
    }

    private static int? FindMatch(
        FrontierCommunityGoalSnapshot frontier,
        IReadOnlyList<FrontierCommunityGoalSnapshot> frontierGoals,
        IReadOnlyList<InaraCommunityGoalSnapshot> inaraGoals,
        HashSet<int> alreadyMatched)
    {
        var title = Normalize(frontier.Title);
        var candidates = inaraGoals
            .Select((goal, index) => new
            {
                Goal = goal,
                Index = index,
                Score = MatchScore(frontier, goal),
            })
            .Where(candidate => !alreadyMatched.Contains(candidate.Index)
                && Normalize(candidate.Goal.Title) == title
                && candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        if (candidates[0].Score > 0
            && (candidates.Length == 1
                || candidates[0].Score > candidates[1].Score))
        {
            return candidates[0].Index;
        }

        var frontierTitleCount = frontierGoals.Count(goal =>
            Normalize(goal.Title) == title);
        return candidates.Length == 1 && frontierTitleCount == 1
            ? candidates[0].Index
            : null;
    }

    private static int MatchScore(
        FrontierCommunityGoalSnapshot frontier,
        InaraCommunityGoalSnapshot inara)
    {
        var score = 0;
        if (!Compatible(frontier.System, inara.System, ref score, 4)
            || !Compatible(frontier.Market, inara.Station, ref score, 4))
        {
            return -1;
        }

        if (frontier.ExpiresAt is { } frontierExpiry
            && inara.ExpiresAt is { } inaraExpiry)
        {
            if ((frontierExpiry - inaraExpiry).Duration() > ExpiryTolerance)
            {
                return -1;
            }

            score += 3;
        }

        return score;
    }

    private static bool Compatible(
        string first,
        string second,
        ref int score,
        int exactScore)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return true;
        }

        if (Normalize(first) != Normalize(second))
        {
            return false;
        }

        score += exactScore;
        return true;
    }

    private static FrontierCommunityGoalSnapshot Merge(
        FrontierCommunityGoalSnapshot frontier,
        InaraCommunityGoalSnapshot inara,
        DateTimeOffset fetchedAt)
    {
        var data = AddInaraData(frontier.DataPoints, inara, fetchedAt);
        return frontier with
        {
            Description = FirstNonEmpty(frontier.Description, inara.Description),
            Objective = FirstNonEmpty(frontier.Objective, inara.Objective),
            Reward = FirstNonEmpty(frontier.Reward, inara.Reward),
            System = FirstNonEmpty(frontier.System, inara.System),
            Market = FirstNonEmpty(frontier.Market, inara.Station),
            ExpiresAt = frontier.ExpiresAt ?? inara.ExpiresAt,
            IsComplete = frontier.IsComplete || inara.IsComplete,
            CurrentTotal = frontier.CurrentTotal != 0
                ? frontier.CurrentTotal
                : inara.ContributionsTotal ?? 0,
            Contributors = frontier.HasContributorData
                ? frontier.Contributors
                : inara.Contributors ?? 0,
            TierReached = FirstNonEmpty(
                frontier.TierReached,
                FormatTier(inara.TierReached, inara.TierMaximum)),
            HasContributorData = frontier.HasContributorData
                || inara.Contributors is not null,
            DataPoints = data,
        };
    }

    private static FrontierCommunityGoalSnapshot Create(
        InaraCommunityGoalSnapshot inara,
        DateTimeOffset fetchedAt)
    {
        return new FrontierCommunityGoalSnapshot(
            null,
            inara.Title,
            inara.Description,
            inara.Objective,
            inara.Reward,
            inara.System,
            inara.Station,
            inara.ExpiresAt,
            inara.IsComplete,
            inara.ContributionsTotal ?? 0,
            null,
            0,
            inara.Contributors ?? 0,
            FormatTier(inara.TierReached, inara.TierMaximum),
            null,
            0,
            null,
            false,
            AddInaraData([], inara, fetchedAt)
                .Append(new FrontierDataPointSnapshot(
                    "inara.sourceOnly",
                    "true"))
                .ToArray(),
            HasContributorData: inara.Contributors is not null);
    }

    private static FrontierDataPointSnapshot[] AddInaraData(
        IReadOnlyList<FrontierDataPointSnapshot>? existing,
        InaraCommunityGoalSnapshot inara,
        DateTimeOffset fetchedAt)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in existing ?? [])
        {
            values[point.Path] = point.Value;
        }

        values["inara.fetchedAt"] = fetchedAt.ToString(
            "O",
            CultureInfo.InvariantCulture);
        Add("inara.lastUpdate", inara.LastUpdatedAt?.ToString(
            "O",
            CultureInfo.InvariantCulture));
        Add("inara.url", inara.InaraUrl);
        Add("inara.tierReached", inara.TierReached?.ToString(
            CultureInfo.InvariantCulture));
        Add("inara.tierMaximum", inara.TierMaximum?.ToString(
            CultureInfo.InvariantCulture));
        Add("inara.contributors", inara.Contributors?.ToString(
            CultureInfo.InvariantCulture));
        Add("inara.contributionsTotal", inara.ContributionsTotal?.ToString(
            CultureInfo.InvariantCulture));
        return values
            .Select(pair => new FrontierDataPointSnapshot(pair.Key, pair.Value))
            .ToArray();

        void Add(string path, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[path] = value;
            }
        }
    }

    private static bool IsPriorInaraOnlyGoal(
        FrontierCommunityGoalSnapshot goal) =>
        goal.DataPoints?.Any(point =>
            string.Equals(
                point.Path,
                "inara.sourceOnly",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                point.Value,
                "true",
                StringComparison.OrdinalIgnoreCase)) == true;

    private static string FormatTier(int? reached, int? maximum)
    {
        return reached is null
            ? string.Empty
            : (maximum is > 0) switch
            {
                true => $"Tier {reached:N0} / {maximum:N0}",
                false => $"Tier {reached:N0}"
            };
    }

    private static string FirstNonEmpty(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second.Trim() : first.Trim();

    private static string Normalize(string value) =>
        string.Concat(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant));
}
