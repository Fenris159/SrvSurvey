using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed partial class JournalDirectoryMonitorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AFailedLaterPartDeliversItsConsumedPrefixOnceAndResumesAfterRepair(
        bool bootstrap,
        bool disappears
    )
    {
        Directory.CreateDirectory(temporaryDirectory);
        string first = Path.Combine(temporaryDirectory, "Journal.2026-11-04T050000.01.log");
        string next = Path.Combine(temporaryDirectory, "Journal.2026-11-04T050000.02.log");
        await File.WriteAllTextAsync(
            first,
            """
            {"timestamp":"2026-11-04T05:00:00Z","event":"Fileheader","part":1}
            {"timestamp":"2026-11-04T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            """ + "\n"
        );
        bool fail = true;
        int nextReads = 0;
        FileStream Open(string path)
        {
            if (path == next && ++nextReads > 1 && fail)
            {
                if (disappears)
                {
                    File.Delete(next);
                    throw new FileNotFoundException("Temporarily missing journal part", next);
                }
                throw new IOException("Temporarily locked journal part");
            }
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        var monitor = new JournalDirectoryMonitor([temporaryDirectory], "F123", _ => default, Open);
        if (!bootstrap)
        {
            await monitor.PollAsync();
        }
        await File.AppendAllTextAsync(
            first,
            """
            {"timestamp":"2026-11-04T05:00:58Z","event":"Screenshot"}
            {"timestamp":"2026-11-04T05:00:59Z","event":"SendText","Message":".mining survey"}
            """ + "\n"
        );
        string continuation =
            """
                {"timestamp":"2026-11-04T05:01:00Z","event":"Fileheader","part":2}
                {"timestamp":"2026-11-04T05:01:01Z","event":"FuelScoop","Total":10}
                {"timestamp":"2026-11-04T05:01:02Z","event":"SendText","Message":".mining survey"}
                """ + "\n";
        await File.WriteAllTextAsync(next, continuation);
        File.SetLastWriteTimeUtc(first, new DateTime(2026, 11, 4, 5, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(next, new DateTime(2026, 11, 4, 5, 1, 0, DateTimeKind.Utc));
        JournalMonitorUpdate prefix = await monitor.PollAsync();
        Assert.Equal(bootstrap, prefix.IsBootstrapRead);
        Assert.Contains(
            disappears ? "Temporarily missing" : "Temporarily locked",
            Assert.Single(prefix.Errors),
            StringComparison.Ordinal
        );
        Assert.Equal(
            bootstrap ? ["Fileheader", "Commander", "Screenshot", "SendText"] : ["Screenshot", "SendText"],
            prefix.JournalEvents.Select(entry => entry.EventName)
        );
        if (disappears)
        {
            JournalMonitorUpdate stillMissing = await monitor.PollAsync();
            Assert.Empty(stillMissing.JournalEvents);
            Assert.Equal(bootstrap, stillMissing.IsBootstrapRead);
            Assert.Single(stillMissing.Errors);
            Assert.Equal(next, monitor.CurrentJournalPath);
            await File.WriteAllTextAsync(next, continuation);
            File.SetLastWriteTimeUtc(next, new DateTime(2026, 11, 4, 5, 1, 0, DateTimeKind.Utc));
        }
        fail = false;
        JournalMonitorUpdate repaired = await monitor.PollAsync();
        Assert.Equal(["FuelScoop", "SendText"], repaired.JournalEvents.Select(entry => entry.EventName));
        Assert.Equal(bootstrap, repaired.IsBootstrapRead);
        Assert.Empty(repaired.Errors);
        Assert.Empty((await monitor.PollAsync()).JournalEvents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReadFailureBeforeAnyBootstrapEventsKeepsTheRecoveredHistoryAsBootstrap(bool identityRead)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "Journal.2026-11-04T050000.01.log");
        await File.WriteAllTextAsync(
            path,
            """
            {"timestamp":"2026-11-04T05:00:00Z","event":"Fileheader","part":1}
            {"timestamp":"2026-11-04T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"2026-11-04T05:00:02Z","event":"SendText","Message":".mining survey"}
            """ + "\n"
        );
        int reads = 0;
        var monitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F123",
            _ => default,
            file =>
            {
                if (++reads == (identityRead ? 1 : 2))
                {
                    throw new IOException("Initial read unavailable");
                }
                return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
        );
        JournalMonitorUpdate failed = await monitor.PollAsync();
        Assert.Empty(failed.JournalEvents);
        Assert.Single(failed.Errors);
        JournalMonitorUpdate recovered = await monitor.PollAsync();
        Assert.True(recovered.IsBootstrapRead);
        Assert.Equal(["Fileheader", "Commander", "SendText"], recovered.JournalEvents.Select(entry => entry.EventName));
        Assert.False((await monitor.PollAsync()).IsBootstrapRead);
    }
}
