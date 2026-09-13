using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MineMapCsvExporterTests
{
    [Fact]
    public void WriteCreatesOneSelfContainedRowPerMarker()
    {
        DateTimeOffset created = DateTimeOffset.Parse("2026-09-12T12:30:00-05:00", CultureInfo.InvariantCulture);
        var center = new SurfaceCoordinate(14.2609, -79.3292);
        const double planetRadiusMeters = 855_573.1875;
        var survey = new MineMapSurvey
        {
            Id = Guid.Parse("2c60e981-e491-4a32-8547-b7157817f6e1"),
            FrontierId = "F123",
            CommanderName = "Fenris",
            SystemName = "LTT 4428",
            SystemAddress = 123456789,
            SystemPosition = new GalacticCoordinate(12.5, -3.25, 99),
            BodyId = 5,
            BodyName = "LTT 4428 D 5 a",
            BodyType = "Rocky body",
            ArrivalDistanceLs = 19797,
            LocationSignal = 20,
            LocationRadiusMeters = 6380,
            PlanetRadiusMeters = planetRadiusMeters,
            Center = center,
            CreatedAt = created,
            UpdatedAt = created.AddHours(1),
            Notes = "North shelf, clear approach\nBring limpets",
            Markers =
            [
                new MineMapMarker
                {
                    Id = Guid.Parse("57f1b8bd-ea3d-49d7-9144-22d81c1da345"),
                    Material = "Grandiderite",
                    MineralAmount = MineMapRating.High,
                    Density = MineMapRating.Medium,
                    RigCount = 3,
                    Location = MineMapService.GetDestination(center, 90, 1200, planetRadiusMeters),
                    CreatedAt = created.AddMinutes(2),
                    SplatBoundary =
                    [
                        MineMapService.GetDestination(center, 85, 1000, planetRadiusMeters),
                        MineMapService.GetDestination(center, 95, 1000, planetRadiusMeters),
                        MineMapService.GetDestination(center, 90, 1400, planetRadiusMeters),
                    ],
                    SuggestedRigLocations = [MineMapService.GetDestination(center, 90, 1200, planetRadiusMeters)],
                },
                new MineMapMarker
                {
                    Material = "Haematite",
                    MineralAmount = MineMapRating.Low,
                    Density = MineMapRating.High,
                    Location = MineMapService.GetDestination(center, 180, 500, planetRadiusMeters),
                    CreatedAt = created.AddMinutes(5),
                },
            ],
        };

        List<string[]> rows = Parse(MineMapCsvExporter.Write(survey));

        Assert.Equal(3, rows.Count);
        string[] headers = rows[0];
        Assert.All(rows.Skip(1), row => Assert.Equal(headers.Length, row.Length));
        Assert.Equal("LTT 4428", Value(rows[1], headers, "System"));
        Assert.Equal("LTT 4428 D 5 a", Value(rows[1], headers, "Body"));
        Assert.Equal("20", Value(rows[1], headers, "LocationSignalNumber"));
        Assert.Equal("Mining Location Signal 20", Value(rows[1], headers, "LocationName"));
        Assert.Equal(survey.Notes, Value(rows[1], headers, "SurveyNotes"));
        Assert.Equal("Grandiderite", Value(rows[1], headers, "Commodity"));
        Assert.Equal("High", Value(rows[1], headers, "MineralAmount"));
        Assert.Equal("Medium", Value(rows[1], headers, "Density"));
        Assert.Equal("3", Value(rows[1], headers, "RigCount"));
        Assert.Equal("1.2", Value(rows[1], headers, "MarkerDistanceFromCenterKm"));
        Assert.StartsWith("[[", Value(rows[1], headers, "SplatBoundaryCoordinates"), StringComparison.Ordinal);
        Assert.Equal("Haematite", Value(rows[2], headers, "Commodity"));
        Assert.Equal("", Value(rows[2], headers, "RigCount"));
    }

    [Fact]
    public void WritePreservesSurveyDetailsWhenThereAreNoMarkersAndNeutralizesSpreadsheetFormulas()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var survey = new MineMapSurvey
        {
            CommanderName = "@commander",
            SystemName = "System",
            SystemAddress = 1,
            SystemPosition = new GalacticCoordinate(0, 0, 0),
            BodyName = "System A 1",
            BodyType = "Rocky body",
            LocationSignal = 4,
            LocationRadiusMeters = 1000,
            PlanetRadiusMeters = 500_000,
            Center = new SurfaceCoordinate(0, 0),
            CreatedAt = now,
            UpdatedAt = now,
            Notes = "=HYPERLINK(\"https://example.invalid\")",
        };

        List<string[]> rows = Parse(MineMapCsvExporter.Write(survey));

        Assert.Equal(2, rows.Count);
        Assert.Equal("'@commander", Value(rows[1], rows[0], "Commander"));
        Assert.StartsWith("'=HYPERLINK", Value(rows[1], rows[0], "SurveyNotes"), StringComparison.Ordinal);
        Assert.Equal("", Value(rows[1], rows[0], "MarkerId"));
        Assert.Equal("System-A-1-signal-4-surface-mining.csv", MineMapCsvExporter.CreateSuggestedFileName(survey));
    }

    private static string Value(string[] row, string[] headers, string header) => row[Array.IndexOf(headers, header)];

    private static List<string[]> Parse(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv))
        {
            HasFieldsEnclosedInQuotes = true,
            TextFieldType = FieldType.Delimited,
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields()!);
        }

        return rows;
    }
}
