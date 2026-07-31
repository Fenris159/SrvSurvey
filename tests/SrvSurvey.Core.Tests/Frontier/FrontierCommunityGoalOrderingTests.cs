using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Core.Tests.Frontier;

public sealed class FrontierCommunityGoalOrderingTests
{
    [Fact]
    public void ActiveGoalsComeFirstAndCompletedGoalsAreNewestFirst()
    {
        var goals = new[]
        {
            Goal("Older completion", true, "2026-03-01T00:00:00Z"),
            Goal("Active later", false, "2026-08-08T00:00:00Z"),
            Goal("Newest completion", true, "2026-07-01T00:00:00Z"),
            Goal("Active sooner", false, "2026-08-01T00:00:00Z"),
        };

        var ordered = FrontierCommunityGoalOrdering.Order(goals);

        Assert.Equal(
            ["Active sooner", "Active later", "Newest completion", "Older completion"],
            ordered.Select(goal => goal.Title));
    }

    private static FrontierCommunityGoalSnapshot Goal(
        string title,
        bool complete,
        string lastUpdated) => new(
        null,
        title,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        DateTimeOffset.Parse(lastUpdated).AddDays(1),
        complete,
        0,
        null,
        0,
        0,
        string.Empty,
        null,
        0,
        null,
        false,
        [new FrontierDataPointSnapshot("inara.lastUpdate", lastUpdated)]);
}
