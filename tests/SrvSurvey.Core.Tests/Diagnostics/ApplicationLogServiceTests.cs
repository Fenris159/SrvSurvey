using System.Globalization;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Core.Tests.Diagnostics;

public sealed class ApplicationLogServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-logs-{Guid.NewGuid():N}");
    private readonly TimeProvider timeProvider = new FixedTimeProvider(
        DateTimeOffset.Parse("2026-07-25T13:14:15-05:00", global::System.Globalization.CultureInfo.InvariantCulture)
    );

    [Fact]
    public void AppendUpdatesMemoryFileAndObservers()
    {
        var service = new ApplicationLogService(temporaryDirectory, timeProvider);
        int changeCount = 0;
        service.Changed += (_, _) => changeCount++;

        string line = service.Append("Journal loaded");

        Assert.Equal("13:14:15: Journal loaded", line);
        Assert.Equal([line], service.Entries);
        Assert.Equal(line, service.Text);
        Assert.Equal(1, changeCount);
        Assert.Null(service.LastWriteError);
        Assert.Equal(line + Environment.NewLine, File.ReadAllText(service.CurrentLogPath));
    }

    [Fact]
    public void SessionFileUsesLegacyNameAndAvoidsCollisions()
    {
        var first = new ApplicationLogService(temporaryDirectory, timeProvider);
        var second = new ApplicationLogService(temporaryDirectory, timeProvider);

        Assert.EndsWith("srvs-20260725_131415.txt", first.CurrentLogPath);
        Assert.EndsWith("srvs-20260725_131415_1.txt", second.CurrentLogPath);
        Assert.True(File.Exists(first.CurrentLogPath));
        Assert.True(File.Exists(second.CurrentLogPath));
    }

    [Fact]
    public void ClearRetainsOnlyResetEntryAndAppendsItToFile()
    {
        var service = new ApplicationLogService(temporaryDirectory, timeProvider);
        service.Append("Before reset");

        service.Clear();

        string entry = Assert.Single(service.Entries);
        Assert.Equal("13:14:15: Logs reset", entry);
        string[] persisted = File.ReadAllLines(service.CurrentLogPath);
        Assert.Equal(2, persisted.Length);
        Assert.Equal("13:14:15: Before reset", persisted[0]);
        Assert.Equal(entry, persisted[1]);
    }

    [Fact]
    public void NewSessionRetainsNewestTenLogFiles()
    {
        string logDirectory = Path.Combine(temporaryDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        for (int index = 0; index < 11; index++)
        {
            string path = Path.Combine(logDirectory, $"old-{index:00}.txt");
            File.WriteAllText(path, index.ToString(CultureInfo.InvariantCulture));
            File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddMinutes(index));
        }

        var service = new ApplicationLogService(temporaryDirectory, timeProvider);
        string[] retained = Directory.GetFiles(logDirectory, "*.txt");

        Assert.Equal(10, retained.Length);
        Assert.DoesNotContain(Path.Combine(logDirectory, "old-00.txt"), retained);
        Assert.DoesNotContain(Path.Combine(logDirectory, "old-01.txt"), retained);
        Assert.Contains(service.CurrentLogPath, retained);
    }

    [Fact]
    public void FileFailureDoesNotLoseInMemoryEntries()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string dataPath = Path.Combine(temporaryDirectory, "not-a-directory");
        File.WriteAllText(dataPath, "occupied");
        var service = new ApplicationLogService(dataPath, timeProvider);

        string line = service.Append("Still visible");

        Assert.Equal([line], service.Entries);
        Assert.False(string.IsNullOrWhiteSpace(service.LastWriteError));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "Test");

        public override DateTimeOffset GetUtcNow() => value.ToUniversalTime();
    }
}
