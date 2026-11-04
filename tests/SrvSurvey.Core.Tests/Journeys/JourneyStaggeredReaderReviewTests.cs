using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StaggeredReadersKeepEachDistinctSameSecondEventExactlyOnce(
        bool historyCatchUp,
        bool sameSecondPrefix
    )
    {
        const string fileName = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            fileName,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z")
                .Replace("Drew", "Pilot 🚀", StringComparison.Ordinal)
                .ReplaceLineEndings("\r\n") + "\r\n"
        );
        string path = Path.Combine(JournalDirectory, fileName);
        var slowMonitor = new JournalDirectoryMonitor(JournalDirectory, "F123");
        var fastMonitor = new JournalDirectoryMonitor(JournalDirectory, "F123");
        await slowMonitor.PollAsync();
        await fastMonitor.PollAsync();
        JourneyService slow = CreateService();
        JourneyJournalSystemSearchResult start = await slow.FindLatestStartAsync("F123", true, 42);
        JourneyDocument original = (
            await slow.BeginAsync(new("F123", "Pilot", true, "Shared expedition", "", start.Entry!))
        ).Journey!;
        JourneyService fast = CreateService();
        if (!historyCatchUp)
        {
            await fast.InitializeActiveAsync("F123", true);
        }
        const string shot = "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Screenshot\"}\n";
        await File.AppendAllTextAsync(
            path,
            "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}\n"
        );
        if (sameSecondPrefix)
        {
            await File.AppendAllTextAsync(path, shot);
        }
        JournalMonitorUpdate slowPrefix = await slowMonitor.PollAsync();
        await File.AppendAllTextAsync(path, shot);
        if (historyCatchUp)
        {
            await fast.InitializeActiveAsync("F123", true);
        }
        else
        {
            await fast.ApplyLiveAsync((await fastMonitor.PollAsync()).JournalEvents);
        }
        Assert.Equal(sameSecondPrefix ? 2 : 1, fast.ActiveJourney?.CurrentSystem?.Counts.Screenshots);
        await slow.ApplyLiveAsync(slowPrefix.JournalEvents);
        Assert.Equal(sameSecondPrefix ? 2 : 1, slow.ActiveJourney?.CurrentSystem?.Counts.Screenshots);
        // Identical payloads at different physical positions are distinct captures.
        await File.AppendAllTextAsync(path, shot);
        await slow.ApplyLiveAsync((await slowMonitor.PollAsync()).JournalEvents);
        Assert.Equal(sameSecondPrefix ? 3 : 2, slow.ActiveJourney?.CurrentSystem?.Counts.Screenshots);
        await fast.ApplyLiveAsync((await fastMonitor.PollAsync()).JournalEvents);
        JourneyDocument stored = (await new JourneyStore(DataDirectory).LoadAsync("F123", original.FileName)).Journey!;
        Assert.Equal(sameSecondPrefix ? 3 : 2, stored.CurrentSystem!.Counts.Screenshots);
        Assert.Equal(sameSecondPrefix ? 3 : 2, fast.ActiveJourney?.CurrentSystem?.Counts.Screenshots);
    }
}
