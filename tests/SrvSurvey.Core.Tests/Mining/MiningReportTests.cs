using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningReportTests
{
    [Fact]
    public void ReportExplainsProspectingStatisticsAndPreservesTheirValuesAndCsvFields()
    {
        var start = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var session = new MiningSession
        {
            Started = start,
            Ended = start.AddHours(1),
            Prospects =
            [
                new(start, [new("Platinum", 10), new("Monazite", 20)], "", "High"),
                new(start.AddMinutes(1), [new("Platinum", 30)], "", "High"),
                new(start.AddMinutes(2), [new("Osmium", 15)], "Monazite", "Low"),
            ],
            Collections = [new(start.AddMinutes(3), "Platinum", 2, false)],
        };

        string html = MiningReport.Html([session]);
        Assert.Contains("Prospected asteroids: 3", html);
        Assert.Contains("Core finds: 1", html);
        Assert.Contains("0.67 t/prospect", html);
        Assert.Contains("Average in matching rocks %", html);
        Assert.Contains("<td>Platinum</td><td>2</td><td>2</td><td>20.0</td><td>30.0</td>", html);
        Assert.Contains("<td>Monazite</td><td>2</td><td>2</td><td>20.0</td><td>20.0</td>", html);
        Assert.Contains("Rates use active session time.", html);
        Assert.Contains("Core finds: 1", MiningReport.Text([session]));
        string csv = MiningReport.Csv([session]);
        Assert.Contains(",Asteroids,CoreHits,", csv);
        string[] cells = csv.Split('\n')[1].Split(',');
        Assert.Equal("\"3\"", cells[7]);
        Assert.Equal("\"1\"", cells[8]);
        session.AsteroidAdjustment = 1;
        Assert.Contains("Prospected asteroids: 4", MiningReport.Html([session]));
        Assert.Contains("Prospected asteroids: 4", MiningReport.Text([session]));
    }

    [Fact]
    public void ExportIncludesRealTotalsAndEscapesCommanderSuppliedText()
    {
        var session = new MiningSession
        {
            Started = DateTimeOffset.Parse(
                "2026-09-06T12:00:00Z",
                global::System.Globalization.CultureInfo.InvariantCulture
            ),
            Ended = DateTimeOffset.Parse(
                "2026-09-06T13:00:00Z",
                global::System.Globalization.CultureInfo.InvariantCulture
            ),
            System = "=1+1",
            Notes = "<script>alert(1)</script>",
        };
        session.Collections.Add(new MiningCollectionEntry(session.Started, "platinum", 2, false));
        string html = MiningReport.Html([session]);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("platinum", html);
        Assert.Contains("2.0 t/h", html);
        Assert.Contains("'=1+1", MiningReport.Csv([session]));
    }

    [Fact]
    public void ChartsUseTimedObservationsAndCompareMaterialsAcrossSessions()
    {
        var start = DateTimeOffset.Parse(
            "2026-09-06T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        var first = new MiningSession
        {
            Started = start,
            Ended = start.AddHours(1),
            System = "Sol",
        };
        first.Prospects.Add(new(start.AddMinutes(2), [new("<Platinum>", 25)], "Monazite", "High"));
        first.Collections.Add(new(start.AddMinutes(10), "platinum", 2, false));
        first.Collections.Add(new(start.AddMinutes(20), "platinum", 3, false));
        var second = new MiningSession
        {
            Started = start.AddDays(1),
            Ended = start.AddDays(1).AddHours(1),
            System = "Achenar",
        };
        second.Collections.Add(new(second.Started.AddMinutes(5), "platinum", 7, false));
        string html = MiningReport.Html([first, second]);
        Assert.Contains("Yield over time", html);
        Assert.Contains("Cumulative refining over time", html);
        Assert.Contains("2 min · 25 %", html);
        Assert.Contains("20 min · 5 t", html);
        Assert.Contains("&lt;Platinum&gt;", html);
        Assert.Contains("Materials across sessions", html);
        Assert.Contains("5 t · 5 t/h", html);
        Assert.Contains("7 t · 7 t/h", html);
        Assert.DoesNotContain("Monazite — yield", html);
    }

    [Fact]
    public void ImportedSummariesDoNotInventChartObservations()
    {
        var session = new MiningSession { Imported = new(45, 10, 1, 0, []) };
        string html = MiningReport.Html([session]);
        Assert.Contains("No timed observations were recorded", html);
        Assert.DoesNotContain("<svg", html);
    }
}
