using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class JournalDirectoryChangeSignalTests
{
    [Theory]
    [InlineData("Status.json", true)]
    [InlineData("cargo.JSON", true)]
    [InlineData("NavRoute.json", true)]
    [InlineData("ShipLocker.json", true)]
    [InlineData("Market.json", true)]
    [InlineData("Journal.123.log", true)]
    [InlineData("other.json", false)]
    [InlineData("Journal.123.tmp", false)]
    public void WatchesOnlyRelevantFiles(string name, bool expected) =>
        Assert.Equal(expected, JournalDirectoryChangeSignal.IsRelevant(name));

    [Fact]
    public async Task FileWrittenDuringProjectionWakesNextPollWithoutWaitingForFallback()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var signal = new JournalDirectoryChangeSignal([directory]);
            await File.WriteAllTextAsync(Path.Combine(directory, "Status.json"), "{}");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await signal.WaitAsync(TimeSpan.FromSeconds(30), deadline.Token);
            Assert.False(deadline.IsCancellationRequested);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MissingFolderRetainsFallbackAndCancellation()
    {
        using var signal = new JournalDirectoryChangeSignal([
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
        ]);
        await signal.WaitAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            signal.WaitAsync(TimeSpan.FromSeconds(30), cancelled.Token)
        );
    }
}
