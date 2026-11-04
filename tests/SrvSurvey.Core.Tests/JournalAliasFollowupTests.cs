using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed partial class JournalDirectoryMonitorTests
{
    [Fact]
    public async Task AChangedConfiguredAliasIsReportedWithoutReplayingThePhysicalJournal()
    {
        string real = Path.Combine(temporaryDirectory, "physical");
        string alias = Path.Combine(temporaryDirectory, "preferred-profile");
        Directory.CreateDirectory(real);
        string journal = Path.Combine(real, "Journal.2026-11-03T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-11-03T12:00:00Z\",\"event\":\"Commander\",\"FID\":\"F123\",\"Name\":\"Test\"}\n"
        );
        var monitor = new JournalDirectoryMonitor([alias, real], "F123");
        Assert.Equal(real, (await monitor.PollAsync()).ConfiguredJournalDirectory);
        Assert.False((await monitor.PollAsync()).HasChanges);
        Directory.CreateSymbolicLink(alias, real);
        JournalMonitorUpdate changed = await monitor.PollAsync();
        Assert.Equal(journal, changed.JournalPath);
        Assert.Equal(alias, changed.ConfiguredJournalDirectory);
        Assert.Empty(changed.JournalEvents);
        Assert.True(changed.HasChanges);
        Assert.False((await monitor.PollAsync()).HasChanges);
    }
}
