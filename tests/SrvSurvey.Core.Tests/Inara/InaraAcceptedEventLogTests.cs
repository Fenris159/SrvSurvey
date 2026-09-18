using System.Globalization;
using SrvSurvey.Core.Inara;

namespace SrvSurvey.Core.Tests.Inara;

public sealed class InaraAcceptedEventLogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-inara-accepted-{Guid.NewGuid():N}"
    );

    [Fact]
    public void PrunesEntriesOlderThanTwelveHours()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-28T12:00:00Z", CultureInfo.InvariantCulture));
        var log = new InaraAcceptedEventLog(Path.Combine(temporaryDirectory, "inara-accepted.txt"), time);

        log.Record("addCommanderTravelFSDJump", "2026-07-28T12:00:00Z", "Sirius");
        time.Advance(InaraAcceptedEventLog.Retention + TimeSpan.FromSeconds(1));
        log.Record("addCommanderTravelFSDJump", "2026-07-28T23:00:01Z", "Sol");

        string line = Assert.Single(log.ReadLines());
        Assert.Contains("Sol", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Sirius", line, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordedLinesNeverContainApiKeys()
    {
        const string apiKey = "secret-personal-key";
        var log = new InaraAcceptedEventLog(Path.Combine(temporaryDirectory, "inara-accepted.txt"));
        log.Record("addCommanderTravelFSDJump", "2026-07-28T12:00:00Z", "Sirius");

        Assert.All(log.ReadLines(), line => Assert.DoesNotContain(apiKey, line, StringComparison.Ordinal));
        Assert.All(log.ReadLines(), line => Assert.DoesNotContain("APIkey", line, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecordCreatesMissingDirectoryAndKeepsUnparseableLines()
    {
        string path = Path.Combine(temporaryDirectory, "nested", "inara-accepted.txt");
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-28T12:00:00Z", CultureInfo.InvariantCulture));
        var log = new InaraAcceptedEventLog(path, time);

        log.Record("addCommanderTravelFSDJump", "2026-07-28T12:00:00Z", "Sirius");
        File.AppendAllText(path, "not-a-timestamp leftover" + Environment.NewLine);
        time.Advance(InaraAcceptedEventLog.Retention + TimeSpan.FromSeconds(1));
        log.Record("addCommanderTravelFSDJump", "2026-07-28T23:00:01Z", "Sol");

        string[] lines = log.ReadLines().ToArray();
        Assert.Contains(lines, line => line.Contains("Sol", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("not-a-timestamp leftover", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("Sirius", StringComparison.Ordinal));
    }

    [Fact]
    public void FileFailureDoesNotThrow()
    {
        string occupied = Path.Combine(temporaryDirectory, "occupied");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(occupied, "not-a-directory");
        var log = new InaraAcceptedEventLog(Path.Combine(occupied, "inara-accepted.txt"));

        log.Record("addCommanderTravelFSDJump", "2026-07-28T12:00:00Z", "Sirius");

        Assert.Empty(log.ReadLines());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
        }
    }
}
