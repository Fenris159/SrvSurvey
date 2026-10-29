using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningLegacyIdentityMigrationTests
{
    /// <summary>Bootstrap migrates physical identities without suppressing later identical live records or recounting rereads.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyHashesOnlySuppressBootstrapAndRememberItsSourceRecords(bool reopen)
    {
        string directory = Path.Combine(Path.GetTempPath(), "mining-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string raw = """{"timestamp":"2026-10-28T12:00:03Z","event":"MiningRefined","Type":"platinum"}""";
            var at = DateTimeOffset.Parse("2026-10-28T12:00:03Z", CultureInfo.InvariantCulture);
            var session = new MiningSession { Started = at.AddSeconds(-1) };
            session.Collections.Add(new(at, "platinum", 1, false));
            var data = new MiningCommanderData
            {
                Current = session,
                ProcessedEvents = [Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))],
            };
            var state = new MiningWorkspaceState(data);
            string path = Path.Combine(directory, "Journal.2026-10-28T120000.01.log");
            await File.WriteAllTextAsync(
                path,
                """{"timestamp":"2026-10-28T12:00:00Z","event":"LoadGame","Commander":"Test","FID":"F1","Ship":"Python","Odyssey":true}"""
                    + "\n"
                    + raw
                    + "\n"
            );
            var monitor = new JournalDirectoryMonitor(directory, "F1");
            JournalMonitorUpdate bootstrap = await monitor.PollAsync();
            Assert.True(bootstrap.IsBootstrapRead);
            foreach (JournalEventEnvelope entry in bootstrap.JournalEvents)
            {
                state.Apply(entry, true, "Sol", "Ring", "Python");
            }
            Assert.Equal(1, state.Session.Current!.RefinedTons);
            if (reopen)
            {
                state = new MiningWorkspaceState(MiningStore.Parse(MiningStore.Export(state.Data)));
            }
            // A live replay of the same physical record remains deduplicated after migration.
            foreach (JournalEventEnvelope entry in bootstrap.JournalEvents)
            {
                state.Apply(entry, false, "Sol", "Ring", "Python");
            }
            Assert.Equal(1, state.Session.Current!.RefinedTons);
            await File.AppendAllTextAsync(path, raw + "\n");
            JournalMonitorUpdate live = await monitor.PollAsync();
            Assert.False(live.IsBootstrapRead);
            JournalEventEnvelope next = Assert.Single(live.JournalEvents);
            Assert.NotEqual(
                bootstrap.JournalEvents.Single(e => e.EventName == "MiningRefined").SourceRecordIdentity,
                next.SourceRecordIdentity
            );
            state.Apply(next, false, "Sol", "Ring", "Python");
            Assert.Equal(2, state.Session.Current.RefinedTons);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
