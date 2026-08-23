using SrvSurvey.Core.Diagnostics.Replay;

namespace SrvSurvey.Core.Tests.Diagnostics.Replay;

public sealed class JournalReplayPlayerTests
{
    [Fact]
    public async Task StepAppendsCompleteEventsInSourceOrder()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "Journal.01.log");
        var lines = new[]
        {
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}",
            "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Replay Cmdr\",\"FID\":\"F123456\"}",
        };
        await File.WriteAllLinesAsync(sourcePath, lines);
        var session = await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        var player = new JournalReplayPlayer(session);

        Assert.True(await player.StepAsync(CancellationToken.None));
        Assert.True(await player.StepAsync(CancellationToken.None));
        Assert.False(await player.StepAsync(CancellationToken.None));

        Assert.Equal(2, player.Position);
        Assert.Equal(lines, await File.ReadAllLinesAsync(
            session.PlaybackJournalPath));
    }

    [Fact]
    public async Task PlayUsesVirtualTimeAndReadsSpeedForEachDelay()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllLinesAsync(
            sourcePath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:04Z\",\"event\":\"Location\"}",
                "{\"timestamp\":\"2026-08-21T18:00:08Z\",\"event\":\"Shutdown\"}",
            ]);
        var session = await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        var delay = new RecordingDelay();
        var player = new JournalReplayPlayer(session, delay);
        var speeds = new Queue<double>([1, 2, 4]);

        await player.PlayAsync(
            () => speeds.Dequeue(),
            CancellationToken.None);

        Assert.Equal(
            [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)],
            delay.Delays);
        Assert.True(player.IsComplete);
    }

    private sealed class RecordingDelay : IReplayDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-replay-player-{Guid.NewGuid():N}");
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
