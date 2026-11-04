using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed partial class JournalDirectoryMonitorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedCommanderFollowsVerifiedPartsWithoutResettingOrReplayingItsSession(bool rotate)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string first = Path.Combine(temporaryDirectory, "Journal.2026-11-03T050000.01.log");
        string next = Path.Combine(temporaryDirectory, "Journal.2026-11-03T050000.02.log");
        await File.WriteAllTextAsync(
            first,
            """
            {"timestamp":"2026-11-03T05:00:00Z","event":"Fileheader","part":1,"Odyssey":true}
            {"timestamp":"2026-11-03T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"2026-11-03T05:00:02Z","event":"Location","StarSystem":"Sol","Body":"Earth","BodyType":"Planet"}
            """ + "\n"
        );
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F123");
        var state = new JournalSessionState();
        if (rotate)
        {
            foreach (JournalEventEnvelope entry in (await monitor.PollAsync()).JournalEvents)
            {
                state.Apply(entry);
            }
            await File.AppendAllTextAsync(first, "{\"timestamp\":\"2026-11-03T05:00:59Z\",\"event\":\"Screenshot\"}\n");
        }
        await File.WriteAllTextAsync(
            next,
            """
            {"timestamp":"2026-11-03T05:01:00Z","event":"Fileheader","part":2,"Odyssey":true}
            {"timestamp":"2026-11-03T05:01:01Z","event":"FuelScoop","Total":10}
            """ + "\n"
        );
        File.SetLastWriteTimeUtc(first, new DateTime(2026, 11, 3, 5, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(next, new DateTime(2026, 11, 3, 5, 1, 0, DateTimeKind.Utc));
        JournalMonitorUpdate update = await monitor.PollAsync();
        Assert.Equal(next, update.JournalPath);
        Assert.False(update.IsAwaitingCommanderIdentity);
        foreach (JournalEventEnvelope entry in update.JournalEvents)
        {
            state.Apply(entry);
        }
        Assert.Equal("F123", state.FrontierId);
        Assert.Equal("Sol", state.SystemName);
        Assert.Equal("Earth", state.BodyName);
        Assert.False(state.IsAtMainMenu);
        Assert.Equal(
            rotate ? ["Screenshot", "FuelScoop"] : ["Fileheader", "Commander", "Location", "FuelScoop"],
            update.JournalEvents.Select(entry => entry.EventName)
        );
        Assert.False((await monitor.PollAsync()).HasChanges);
    }

    [Theory]
    [InlineData("03", 3)]
    [InlineData("02", 3)]
    public async Task MissingOrMismatchedContinuationPartsNeverBorrowCommanderIdentity(string suffix, int headerPart)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string first = Path.Combine(temporaryDirectory, "Journal.2026-11-03T050000.01.log");
        await File.WriteAllTextAsync(
            first,
            """
            {"timestamp":"2026-11-03T05:00:00Z","event":"Fileheader","part":1}
            {"timestamp":"2026-11-03T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            """ + "\n"
        );
        string next = Path.Combine(temporaryDirectory, $"Journal.2026-11-03T050000.{suffix}.log");
        await File.WriteAllTextAsync(
            next,
            $$"""
            {"timestamp":"2026-11-03T05:01:00Z","event":"Fileheader","part":{{headerPart}}}
            """ + "\n"
        );
        File.SetLastWriteTimeUtc(first, new DateTime(2026, 11, 3, 5, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(next, new DateTime(2026, 11, 3, 5, 1, 0, DateTimeKind.Utc));
        JournalMonitorUpdate update = await new JournalDirectoryMonitor(temporaryDirectory, "F123").PollAsync();
        Assert.Equal(first, update.JournalPath);
        Assert.True(update.IsAwaitingCommanderIdentity);
    }
}
