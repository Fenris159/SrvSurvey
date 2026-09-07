using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningReportTests
{
    [Fact]
    public void ExportIncludesRealTotalsAndEscapesCommanderSuppliedText()
    {
        var session = new MiningSession { Started = DateTimeOffset.Parse("2026-09-06T12:00:00Z"), Ended = DateTimeOffset.Parse("2026-09-06T13:00:00Z"), System = "=1+1", Notes = "<script>alert(1)</script>" };
        session.Collections.Add(new MiningCollection(session.Started, "platinum", 2, false));
        var html = MiningReport.Html([session]);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("platinum", html);
        Assert.Contains("2.0 t/h", html);
        Assert.Contains("'=1+1", MiningReport.Csv([session]));
    }
}
