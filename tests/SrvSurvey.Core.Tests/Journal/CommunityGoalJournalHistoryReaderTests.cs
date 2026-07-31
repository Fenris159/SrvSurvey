using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class CommunityGoalJournalHistoryReaderTests
{
    [Fact]
    public async Task ReturnsLatestProgressForRequestedCommanderOnly()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Journal.2026-07-01T000000.01.log"),
                """
                {"timestamp":"2026-07-01T00:00:00Z","event":"Commander","FID":"F111","Name":"First"}
                {"timestamp":"2026-07-01T00:01:00Z","event":"CommunityGoal","CurrentGoals":[{"CGID":850,"Title":"Vista Genomics Exobiology Initiative","Expiry":"2026-07-09T10:00:00Z","IsComplete":false,"PlayerContribution":1,"PlayerPercentileBand":100,"Bonus":1000}]}
                {"timestamp":"2026-07-01T00:02:00Z","event":"CommunityGoal","CurrentGoals":[{"CGID":850,"Title":"Vista Genomics Exobiology Initiative","Expiry":"2026-07-09T10:00:00Z","IsComplete":true,"PlayerContribution":2,"PlayerPercentileBand":100,"Bonus":45000000}]}
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Journal.2026-07-02T000000.01.log"),
                """
                {"timestamp":"2026-07-02T00:00:00Z","event":"Commander","FID":"F222","Name":"Second"}
                {"timestamp":"2026-07-02T00:01:00Z","event":"CommunityGoal","CurrentGoals":[{"CGID":850,"Title":"Vista Genomics Exobiology Initiative","Expiry":"2026-07-09T10:00:00Z","IsComplete":true,"PlayerContribution":999,"PlayerPercentileBand":10,"Bonus":999999999}]}
                """);
            var reader = new CommunityGoalJournalHistoryReader(root);

            var result = await reader.ReadAsync("111");

            var goal = Assert.Single(result.Goals);
            Assert.Equal(850, goal.Id);
            Assert.True(goal.IsComplete);
            Assert.Equal(2, goal.PlayerContribution);
            Assert.Equal(100, goal.PlayerPercentile);
            Assert.Equal(45_000_000, goal.Bonus);
            Assert.True(goal.HasPlayerContributionData);
            Assert.Contains(goal.DataPoints!, point =>
                point.Path == "journal.communityGoalTimestamp"
                && point.Value.StartsWith(
                    "2026-07-01T00:02:00",
                    StringComparison.Ordinal));
            Assert.Empty(result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsMalformedRelevantEntriesWithoutLosingValidHistory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Journal.2026-07-01T000000.01.log"),
                """
                {"timestamp":"2026-07-01T00:00:00Z","event":"Commander","FID":"F111"}
                {"timestamp":"2026-07-01T00:01:00Z","event":"CommunityGoal"
                {"timestamp":"2026-07-01T00:02:00Z","event":"CommunityGoal","CurrentGoals":[{"CGID":850,"Title":"Valid Goal","PlayerContribution":3}]}
                """);
            var reader = new CommunityGoalJournalHistoryReader(root);

            var result = await reader.ReadAsync("F111");

            Assert.Single(result.Goals);
            Assert.Contains("1 malformed entry", result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-community-goal-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
