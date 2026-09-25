using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class JournalPowerplayPledgeTests
{
    [Fact]
    public void LatestJournalPledgeWinsOverAnOlderLeave()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string older = Path.Combine(directory, "Journal.2026-01-01T000000.01.log");
            string newer = Path.Combine(directory, "Journal.2026-09-20T000000.01.log");
            File.WriteAllLines(
                older,
                [
                    """{"event":"Commander","Name":"Fenris","FID":"F123"}""",
                    """{"timestamp":"2026-01-01T00:00:00Z","event":"PowerplayLeave","Power":"Edmund Mahon"}""",
                ]
            );
            File.WriteAllLines(
                newer,
                [
                    """{"event":"LoadGame","Commander":"Fenris","FID":"F123"}""",
                    """{"timestamp":"2026-09-20T03:45:55Z","event":"Powerplay","Power":"A. Lavigny-Duval"}""",
                ]
            );
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal("A. Lavigny-Duval", JournalPowerplayPledge.ReadLatest([directory], "F123"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void OtherCommandersPledgesDoNotOverrideTheActiveCommander()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string journal = Path.Combine(directory, "Journal.2026-09-20T000000.01.log");
            File.WriteAllLines(
                journal,
                [
                    """{"event":"Commander","Name":"Fenris","FID":"F123"}""",
                    """{"event":"Powerplay","Power":"A. Lavigny-Duval"}""",
                    """{"event":"Commander","Name":"Other","FID":"F456"}""",
                    """{"event":"PowerplayLeave"}""",
                    """{"event":"Powerplay","Power":"Felicia Winters"}""",
                ]
            );
            using var writer = new FileStream(
                journal,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete
            );
            Assert.Equal("A. Lavigny-Duval", JournalPowerplayPledge.ReadLatest([directory], "F123"));
            Assert.Equal("Felicia Winters", JournalPowerplayPledge.ReadLatest([directory], "F456"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
