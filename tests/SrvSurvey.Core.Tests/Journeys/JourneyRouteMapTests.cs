using System.Globalization;
using System.Xml.Linq;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyRouteMapTests
{
    [Fact]
    public void GalacticMapUsesSolAndSagittariusAsReferencesAndLinksEveryVisit()
    {
        JourneyDocument journey = WithPositions([
            new(0, 0, 0),
            new(10_000, 500, 0),
            new(0, -500, 10_000),
            new(25.21875, -20.90625, 25899.96875),
            new(10_000, -400, 0),
        ]);
        XElement[] maps = Maps(JourneyReport.Html(journey));
        Assert.Equal(2, maps.Length);
        XElement galactic = maps.Single(map => map.Attribute("data-map")?.Value == "galactic");
        XElement sol = Reference(galactic, "Sol");
        XElement center = Reference(galactic, "Sagittarius A*");
        Assert.Equal("0", sol.Attribute("data-x")?.Value);
        Assert.Equal("0", sol.Attribute("data-z")?.Value);
        Assert.Equal("25.21875", center.Attribute("data-x")?.Value);
        Assert.Equal("25899.96875", center.Attribute("data-z")?.Value);
        foreach (XElement map in maps)
        {
            XElement[] links = map.Descendants("a").ToArray();
            Assert.Equal(journey.VisitedSystems.Count, links.Length);
            for (int index = 0; index < links.Length; index++)
            {
                Assert.Equal($"#visit-{index + 1}", links[index].Attribute("href")?.Value);
                Assert.Contains(
                    journey.VisitedSystems[index].StarSystem.Name,
                    links[index].Element("title")!.Value,
                    StringComparison.Ordinal
                );
            }
        }
        XElement[] points = Stops(galactic);
        Assert.Equal(
            Coordinate(points[1], "cx") - Coordinate(points[0], "cx"),
            Coordinate(points[0], "cy") - Coordinate(points[2], "cy"),
            2
        );
        Assert.Equal(Coordinate(points[1], "cx"), Coordinate(points[4], "cx"));
        Assert.Equal(Coordinate(points[1], "cy"), Coordinate(points[4], "cy"));
        Assert.Equal(Coordinate(center.Element("circle")!, "cx"), Coordinate(points[3], "cx"));
        Assert.Equal(Coordinate(center.Element("circle")!, "cy"), Coordinate(points[3], "cy"));
    }

    [Fact]
    public void SingleAndRepeatedDestinationsRemainPlottableAndKeepTheirOwnLinks()
    {
        foreach (
            GalacticCoordinate[] positions in new[]
            {
                new GalacticCoordinate[] { new(0, 0, 0) },
                new GalacticCoordinate[] { new(15, 1, 8), new(15, 1000, 8) },
                new GalacticCoordinate[] { new(70_000, 0, -30_000), new(-70_000, 0, 70_000) },
            }
        )
        {
            foreach (XElement map in Maps(JourneyReport.Html(WithPositions(positions))))
            {
                Assert.Equal(positions.Length, Stops(map).Length);
                foreach (XElement point in Stops(map))
                {
                    Assert.InRange(Coordinate(point, "cx"), 0, 960);
                    Assert.InRange(Coordinate(point, "cy"), 0, 620);
                }
                Assert.DoesNotContain("NaN", map.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain("Infinity", map.ToString(), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DetailViewOmitsDistantReferencesWithoutLosingTheCoordinateLegend()
    {
        string html = JourneyReport.Html(WithPositions([new(50_000, 100, -20_000), new(50_010, 900, -20_000)]));
        XElement[] maps = Maps(html);
        Assert.Equal(2, maps[0].Descendants("g").Count(group => group.Attribute("data-reference") is not null));
        Assert.DoesNotContain(maps[1].Descendants("g"), group => group.Attribute("data-reference") is not null);
        Assert.Contains("Galactic context", html, StringComparison.Ordinal);
        Assert.Contains("Route detail", html, StringComparison.Ordinal);
        Assert.Contains("25.21875", html, StringComparison.Ordinal);
        Assert.Contains("25899.96875", html, StringComparison.Ordinal);
    }

    [Fact]
    public void MapTitlesEncodeSystemNamesAndCoordinatesUseInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            JourneyDocument journey = WithPositions([new(12.5, 0, 23.75)]);
            JourneySystemVisit visit = journey.VisitedSystems[0];
            journey = journey with
            {
                VisitedSystems = [visit with { StarSystem = visit.StarSystem with { Name = "A & B <script>" } }],
            };
            string html = JourneyReport.Html(journey);
            Assert.Contains("A &amp; B &lt;script&gt;", html, StringComparison.Ordinal);
            foreach (XElement map in Maps(html))
            {
                XElement link = Assert.Single(map.Descendants("a"));
                Assert.Contains("A & B <script>", link.Element("title")!.Value, StringComparison.Ordinal);
                Assert.True(double.IsFinite(Coordinate(Assert.Single(Stops(map)), "cx")));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CanRenderAnOfflineGalacticRouteExample()
    {
        JourneyDocument journey = WithPositions([
            new(0, 0, 0),
            new(6_400, 150, 11_000),
            new(25.21875, -20.90625, 25899.96875),
            new(-5_000, -200, 35_000),
            new(-16_000, 300, 16_000),
            new(0, 0, 0),
        ]) with
        {
            Name = "The galactic crossing",
            Description = "A route across the galactic plane, returning to Sol.",
        };
        string html = JourneyReport.Html(journey);
        Assert.Equal(6, Stops(Maps(html)[0]).Length);
        string? path = Environment.GetEnvironmentVariable("SRVSURVEY_JOURNEY_MAP_OUTPUT");
        if (path is not null)
        {
            File.WriteAllText(path, html);
        }
    }

    private static JourneyDocument WithPositions(GalacticCoordinate[] positions)
    {
        JourneyDocument sample = JourneyReportTests.SampleJourney();
        JourneySystemVisit first = sample.VisitedSystems[0];
        return sample with
        {
            VisitedSystems = positions
                .Select(
                    (position, index) =>
                        first with
                        {
                            StarSystem = new(index == 0 ? "Sol" : $"Waypoint {index}", index + 1, position),
                            Arrived = first.Arrived.AddMinutes(index),
                            Departed = first.Arrived.AddMinutes(index + 1),
                        }
                )
                .ToArray(),
        };
    }

    private static XElement[] Maps(string html) =>
        html.Split("<svg", StringSplitOptions.None)
            .Skip(1)
            .Select(part => XElement.Parse("<svg" + part[..(part.IndexOf("</svg>", StringComparison.Ordinal) + 6)]))
            .ToArray();

    private static XElement Reference(XElement map, string name) =>
        map.Descendants("g").Single(group => group.Attribute("data-reference")?.Value == name);

    private static XElement[] Stops(XElement map) =>
        map.Descendants("circle")
            .Where(circle => circle.Attribute("class")?.Value.Split(' ').Contains("route-stop") == true)
            .ToArray();

    private static double Coordinate(XElement point, string attribute) =>
        double.Parse(point.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);
}
