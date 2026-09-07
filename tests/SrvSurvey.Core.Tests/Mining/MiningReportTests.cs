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
    [Fact]
    public void ChartsUseTimedObservationsAndCompareMaterialsAcrossSessions()
    {
        var start = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        var first = new MiningSession { Started = start, Ended = start.AddHours(1), System = "Sol" };
        first.Prospects.Add(new(start.AddMinutes(2), [new("<Platinum>", 25)], "Monazite", "High"));
        first.Collections.Add(new(start.AddMinutes(10), "platinum", 2, false));
        first.Collections.Add(new(start.AddMinutes(20), "platinum", 3, false));
        var second = new MiningSession { Started = start.AddDays(1), Ended = start.AddDays(1).AddHours(1), System = "Achenar" };
        second.Collections.Add(new(second.Started.AddMinutes(5), "platinum", 7, false));
        var html = MiningReport.Html([first, second]);
        Assert.Contains("Yield over time", html); Assert.Contains("Cumulative refining over time", html);
        Assert.Contains("2 min · 25 %", html); Assert.Contains("20 min · 5 t", html);
        Assert.Contains("&lt;Platinum&gt;", html);
        Assert.Contains("Materials across sessions", html);
        Assert.Contains("5 t · 5 t/h", html); Assert.Contains("7 t · 7 t/h", html);
        Assert.DoesNotContain("Monazite — yield", html);
    }

    [Fact]
    public void ImportedSummariesDoNotInventChartObservations()
    {
        var session = new MiningSession { Imported = new(45, 10, 1, 0, new()) };
        var html = MiningReport.Html([session]);
        Assert.Contains("No timed observations were recorded", html);
        Assert.DoesNotContain("<svg", html);
    }
}
