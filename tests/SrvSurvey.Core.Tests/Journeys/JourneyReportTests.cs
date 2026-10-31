using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyReportTests
{
    [Fact]
    public void ScreenshotGalleriesUseRelativePreviewsAndOriginalsForEveryVisitToTheSystem()
    {
        string html = JourneyReport.Html(
            SampleJourney(),
            systemImages: new Dictionary<long, IReadOnlyList<JourneyReportImage>>
            {
                [42] = [new("Earth & sunrise <1>.png", "images/42/0001.png", "images/42/0001-preview.png")],
            }
        );
        Assert.Equal(2, html.Split("class=\"screenshot-tile\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("href=\"images/42/0001.png\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"images/42/0001-preview.png\"", html, StringComparison.Ordinal);
        Assert.Contains("Earth &amp; sunrise &lt;1&gt;.png", html, StringComparison.Ordinal);
        Assert.Contains("<dialog id=\"image-viewer\"", html, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' file:", html, StringComparison.Ordinal);
        Assert.Contains("Share this whole folder", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "class=\"screenshot-tile\"",
            JourneyReport.Html(SampleJourney()),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("/private/photo.png")]
    [InlineData("images/../../photo.png")]
    [InlineData("images/%2e%2e/photo.png")]
    [InlineData("https://example.org/photo.png")]
    [InlineData("images/a.png?secret")]
    [InlineData("images/a.svg")]
    [InlineData("images\\photo.png")]
    [InlineData("")]
    public void ScreenshotLinksCannotEscapeThePortableImageFolder(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new JourneyReportImage("Screenshot", path, "images/preview.png"));
        Assert.ThrowsAny<ArgumentException>(() => new JourneyReportImage("Screenshot", "images/original.png", path));
    }

    [Fact]
    public void PortableReportIncludesEveryStatisticVisitAndRecordedDetail()
    {
        JourneyDocument journey = SampleJourney();
        string html = JourneyReport.Html(
            journey,
            new Dictionary<long, string> { [42] = "A quiet landing\nReturn at sunrise." }
        );
        foreach (
            string text in new[]
            {
                "Across the black",
                "Commander Drew",
                "Expedition record",
                "Galactic X/Z projection",
                "System history",
                "Bodies catalogued",
                "Detailed surface scans",
                "Journal touchdowns",
                "Screenshot records",
                "Note records",
                "Scanned body IDs",
                "Scanned Codex entries",
                "New Codex entries",
                "Landings by body",
                "Surface signals",
                "FSS signals",
                "Discovery categories",
                "Biology base value",
                "3,000,000,000",
                "System notes",
                "Return at sunrise.",
                "System address",
                "Galactic coordinates",
                "Journey end (UTC)",
                "Time at this visit",
                "FSS survey",
                "Complete",
            }
        )
        {
            Assert.Contains(text, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.Contains("max-height:78vh;overflow:auto", html, StringComparison.Ordinal);
        Assert.Contains("id=\"system-search\"", html, StringComparison.Ordinal);
        string history = html[html.IndexOf("<ol class=\"history\"", StringComparison.Ordinal)..];
        Assert.True(
            history.IndexOf("Sol", StringComparison.Ordinal) < history.IndexOf("Alpha", StringComparison.Ordinal)
        );
        Assert.True(
            history.LastIndexOf("Sol", StringComparison.Ordinal) > history.IndexOf("Alpha", StringComparison.Ordinal)
        );
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_JOURNEY_REPORT_OUTPUT");
        if (output is not null)
        {
            File.WriteAllText(output, html);
        }
    }

    [Fact]
    public void ReportEncodesUserTextAndKeepsLocalIdentityAndPathsPrivate()
    {
        JourneyDocument journey = SampleJourney() with
        {
            Name = "<script>alert('title')</script>",
            Description = "<img src=x onerror=alert(1)>",
            CommanderName = "Drew & Crew",
            FrontierId = "PRIVATE-FID",
            FilePath = "/private/profile/path.json",
        };
        string html = JourneyReport.Html(
            journey,
            new Dictionary<long, string> { [42] = "</p><script>alert('note')</script>" }
        );
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Drew &amp; Crew", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-FID", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/profile", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAndSingleVisitReportsRemainReadable()
    {
        JourneyDocument journey = SampleJourney();
        string empty = JourneyReport.Html(journey with { VisitedSystems = [] });
        Assert.Contains("No system visits were recorded", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", empty, StringComparison.Ordinal);
        string single = JourneyReport.Html(journey with { VisitedSystems = [journey.VisitedSystems[0]] });
        Assert.Contains("Starting system", single, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", single, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", single, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => JourneyReport.Html(journey with { EndTime = null }));
        Assert.Throws<ArgumentNullException>(() => JourneyReport.Html(null!));
    }

    [Fact]
    public void LargeRouteConnectsEveryDestinationAndKeepsAllVisits()
    {
        JourneyDocument journey = SampleJourney();
        JourneySystemVisit first = journey.VisitedSystems[0];
        JourneySystemVisit[] visits = Enumerable
            .Range(0, 2100)
            .Select(index =>
                first with
                {
                    Arrived = first.Arrived.AddSeconds(index),
                    StarSystem = new($"System {index}", index + 1, new(index, index % 7, index % 200)),
                    LandedOn = null,
                    CodexScanned = null,
                    CodexNew = null,
                    BodiesScanned = null,
                    SubCategories = null,
                    SurfaceSignals = null,
                    FssSignals = null,
                }
            )
            .ToArray();
        string html = JourneyReport.Html(journey with { VisitedSystems = visits });
        Assert.Contains("id=\"visit-2100\"", html, StringComparison.Ordinal);
        Assert.Contains("System 2099", html, StringComparison.Ordinal);
        Assert.Contains("2,100 visits", html, StringComparison.Ordinal);
        string points = html.Split("class=\"route-path\" points=\"", StringSplitOptions.None)[1].Split('"')[0];
        Assert.Equal(2100, points.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    internal static JourneyDocument SampleJourney()
    {
        DateTimeOffset at = new(2026, 10, 29, 12, 0, 15, TimeSpan.Zero);
        var counts = new JourneyCounts(8, 3, 2, 4, 2, 8, 2, 1, 1_000_000_000, 2_000_000, 1);
        JourneySystemVisit Visit(string name, long address, int hour) =>
            new(
                new(name, address, new(address == 42 ? 0 : 15, address == 42 ? 0 : 3, address == 42 ? 0 : 8)),
                at.AddHours(hour),
                hour == 2 ? null : at.AddHours(hour + 1),
                counts,
                new Dictionary<string, int> { [name + " 1"] = 2 },
                new HashSet<long> { 123, 456 },
                new HashSet<int> { 1, 2 },
                new HashSet<string> { "Bacterium & life" },
                new Dictionary<string, int> { ["Biology"] = 2 },
                new Dictionary<string, int> { ["Biological"] = 4 },
                new Dictionary<string, int> { ["Fleet carrier"] = 1 }
            );
        return new(
            "sample",
            "sample.json",
            "F123",
            "Drew",
            "Across the black",
            "A field record from the edge of inhabited space.",
            "Journal.test.log",
            at,
            at.AddHours(3),
            at.AddHours(3),
            [Visit("Sol", 42, 0), Visit("Alpha", 43, 1), Visit("Sol", 42, 2)]
        );
    }
}
