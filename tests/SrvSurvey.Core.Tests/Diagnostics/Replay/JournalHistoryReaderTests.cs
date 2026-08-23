using SrvSurvey.Core.Diagnostics.Replay;

namespace SrvSurvey.Core.Tests.Diagnostics.Replay;

public sealed class JournalHistoryReaderTests
{
    [Fact]
    public async Task EmptyCurrentJournalDoesNotHideEarlierHistory()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log"),
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}\n");
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T190000.01.log"),
            string.Empty);

        var history = await new JournalHistoryReader().LoadAsync(
            temp.Path,
            CancellationToken.None);

        Assert.Single(history.Events);
        Assert.Equal(2, history.FileCount);
    }

    [Fact]
    public async Task InProgressFinalLineDoesNotHideCompleteEvents()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log"),
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}\n"
                + "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":");

        var history = await new JournalHistoryReader().LoadAsync(
            temp.Path,
            CancellationToken.None);

        Assert.Single(history.Events);
        Assert.Equal("Commander", history.Events[0].EventName);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-history-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
