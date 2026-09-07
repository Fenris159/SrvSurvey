using SrvSurvey.Core.Mining;
namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningReportImportTests
{
    [Fact]
    public void ImportsReferenceCsvWithoutInventingJournalEvents()
    {
        var csv = "timestamp_utc,system,body,elapsed,total_tons,asteroids_prospected,comment\n2026-09-06T12:00:00Z,Sol,Earth A Ring,01:00:00,12.5,4,\"line one\nline two\"";
        var session = Assert.Single(MiningReportImport.ReadCsv(csv));
        Assert.Equal(12.5, session.RefinedTons);
        Assert.Equal(12.5, session.TonsPerHour);
        Assert.Equal(4, session.Asteroids);
        Assert.Empty(session.Prospects);
        Assert.Contains("line two", session.Notes);
    }
}
