using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Core.Tests.Diagnostics;

public sealed class JournalHistoryAnalyzerTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journal-history-tests-{Guid.NewGuid():N}");
    private readonly DateTimeOffset now = new(
        2026,
        7,
        25,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task CalculatesLegacyStatisticsForSelectedCommander()
    {
        WriteJournal(
            "Journal.2025-02-20T120000.01.log",
            """
            {"timestamp":"2025-02-20T12:00:00Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2025-02-20T12:01:00Z","event":"FSDJump","JumpDist":12.5}
            {"timestamp":"2025-02-20T12:02:00Z","event":"MarketBuy","Count":4}
            {"timestamp":"2025-02-20T12:03:00Z","event":"MarketSell","Count":2}
            {"timestamp":"2025-02-20T12:04:00Z","event":"CargoTransfer","Transfers":[{"Count":3},{"Count":-1}]}
            {"timestamp":"2025-02-20T12:05:00Z","event":"Shutdown"}
            """);
        WriteJournal(
            "Journal.2026-07-20T120000.01.log",
            """
            {"timestamp":"2026-07-20T12:00:00Z","event":"LoadGame","Commander":"Drew","FID":"F123"}
            {"timestamp":"2026-07-20T12:01:00Z","event":"FSDJump","JumpDist":7.25}
            {"timestamp":"2026-07-20T12:02:00Z","event":"ApproachBody"}
            {"timestamp":"2026-07-20T12:03:00Z","event":"ScanOrganic","ScanType":"Analyse"}
            {"timestamp":"2026-07-20T12:04:00Z","event":"MarketBuy","Count":10}
            {"timestamp":"2026-07-20T12:05:00Z","event":"MarketSell","Count":8}
            {"timestamp":"2026-07-20T12:06:00Z","event":"CargoTransfer","Transfers":[{"Count":5}]}
            {"timestamp":"2026-07-20T12:07:00Z","event":"CollectCargo"}
            {"timestamp":"2026-07-20T12:08:00Z","event":"ColonisationContribution","Contributions":[{"Amount":25},{"Amount":75}]}
            {"timestamp":"2026-07-20T12:09:00Z","event":"Docked"}
            {"timestamp":"2026-07-20T12:10:00Z","event":"Touchdown"}
            {"timestamp":"2026-07-20T12:11:00Z","event":"Died"}
            {"timestamp":"2026-07-20T12:12:00Z","event":"Shutdown"}
            """);
        WriteJournal(
            "Journal.2026-07-21T120000.01.log",
            """
            {"timestamp":"2026-07-21T12:00:00Z","event":"Commander","Name":"Other","FID":"F999"}
            {"timestamp":"2026-07-21T12:01:00Z","event":"FSDJump","JumpDist":999}
            {"timestamp":"2026-07-21T12:02:00Z","event":"Shutdown"}
            """);
        var analyzer = new JournalHistoryAnalyzer(
            temporaryDirectory,
            () => now);

        var result = await analyzer.AnalyzeAsync(
            "F123",
            JournalHistoryAnalyzer.EliteReleaseDate);

        Assert.Equal(3, result.CandidateFileCount);
        Assert.Equal(2, result.ProcessedFileCount);
        Assert.Equal(1, result.SkippedCommanderFileCount);
        Assert.Equal(2, result.Statistics.JumpCount);
        Assert.Equal(19.75, result.Statistics.JumpDistanceLy);
        Assert.Equal(1, result.Statistics.BodyApproachCount);
        Assert.Equal(1, result.Statistics.OrganismAnalysisCount);
        Assert.Equal(14, result.Statistics.CargoBought);
        Assert.Equal(10, result.Statistics.CargoSold);
        Assert.Equal(7, result.Statistics.CargoTransferred);
        Assert.Equal(1, result.Statistics.CargoCollected);
        Assert.Equal(100, result.Statistics.CargoContributed);
        Assert.Equal(1, result.Statistics.DockedCount);
        Assert.Equal(1, result.Statistics.TouchdownCount);
        Assert.Equal(1, result.Statistics.DeathCount);
        Assert.Equal(4, result.Trailblazers.Before.Bought);
        Assert.Equal(2, result.Trailblazers.Before.Sold);
        Assert.Equal(2, result.Trailblazers.Before.Transferred);
        Assert.Equal(10, result.Trailblazers.After.Bought);
        Assert.Equal(8, result.Trailblazers.After.Sold);
        Assert.Equal(5, result.Trailblazers.After.Transferred);
    }

    [Fact]
    public async Task SkipsRecentIncompleteJournalButProcessesOlderIncompleteFile()
    {
        WriteJournal(
            "Journal.2026-07-24T120000.01.log",
            """
            {"event":"Commander","FID":"F123"}
            {"event":"FSDJump","JumpDist":50}
            """);
        WriteJournal(
            "Journal.260720120000.01.log",
            """
            {"event":"Commander","FID":"F123"}
            {"event":"FSDJump","JumpDist":25}
            """);
        var analyzer = new JournalHistoryAnalyzer(
            temporaryDirectory,
            () => now);

        var result = await analyzer.AnalyzeAsync(
            "F123",
            JournalHistoryAnalyzer.EliteReleaseDate);

        Assert.Equal(1, result.SkippedRecentActiveFileCount);
        Assert.Equal(1, result.ProcessedFileCount);
        Assert.Equal(1, result.Statistics.JumpCount);
        Assert.Equal(25, result.Statistics.JumpDistanceLy);
    }

    [Fact]
    public async Task DateFilterAndMalformedLinesAreDeterministic()
    {
        WriteJournal(
            "Journal.2026-06-01T120000.01.log",
            """
            {"event":"Commander","FID":"F123"}
            {"event":"FSDJump","JumpDist":100}
            {"event":"Shutdown"}
            """);
        WriteJournal(
            "Journal.2026-07-20T120000.01.log",
            """
            {"event":"Commander","FID":"F123"}
            {not-json
            {"event":"FSDJump","JumpDist":5}
            {"event":"Shutdown"}
            """);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "Journal.invalid.01.log"),
            "{\"event\":\"Shutdown\"}\n");
        var progress = new List<JournalHistoryAnalysisProgress>();
        var analyzer = new JournalHistoryAnalyzer(
            temporaryDirectory,
            () => now);

        var result = await analyzer.AnalyzeAsync(
            "F123",
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new Progress<JournalHistoryAnalysisProgress>(progress.Add));

        Assert.Equal(1, result.CandidateFileCount);
        Assert.Equal(1, result.ProcessedFileCount);
        Assert.Equal(1, result.MalformedLineCount);
        Assert.Equal(1, result.Statistics.JumpCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("invalid"));
        Assert.Single(progress);
        Assert.Equal(1, progress[0].ProcessedFileCount);
    }

    [Theory]
    [InlineData("Journal.2026-07-25T123456.01.log", 2026)]
    [InlineData("Journal.260725123456.01.log", 2026)]
    [InlineData("Journal.invalid.01.log", 0)]
    public void ParsesBothJournalFileNameGenerations(
        string fileName,
        int expectedYear)
    {
        var parsed = JournalHistoryAnalyzer.TryGetJournalTimestamp(
            fileName,
            out var timestamp);

        Assert.Equal(expectedYear != 0, parsed);
        if (parsed)
        {
            Assert.Equal(expectedYear, timestamp.Year);
            Assert.Equal(TimeSpan.Zero, timestamp.Offset);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private void WriteJournal(string fileName, string content)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, fileName);
        File.WriteAllText(path, content.ReplaceLineEndings("\n") + "\n");
        File.SetLastWriteTimeUtc(path, now.UtcDateTime.AddDays(-3));
    }
}
