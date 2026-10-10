using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationProjectCsvExporterTests
{
    private static readonly string[] FieldHeader = ["Field", "Value"];
    private static readonly string[] CommanderDeliveryHeader = ["Commander", "Cargo"];
    private static readonly string[] ExpectedCommanderDelivery = ["Cmdr", "30"];
    private static readonly string[] HourlyDeliveryHeader = ["Time UTC", "Commander", "Cargo"];
    private static readonly string[] ExpectedHourlyDelivery = ["1970-01-01T00:00:00.0000000+00:00", "Cmdr", "30"];

    /// <summary>Verifies UTF-8 BOM compatibility and replacement of an existing longer export, including non-ASCII names.</summary>
    [Fact]
    public async Task WritesExcelUtf8AndTruncatesExistingFile()
    {
        using var stream = new MemoryStream();
        await stream.WriteAsync(new byte[1024]);
        await ColonizationProjectCsvExporter.WriteUtf8Async(stream, "\"München\",\"日本語\"\r\n");
        byte[] bytes = stream.ToArray();
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes[..3]);
        Assert.Equal("\"München\",\"日本語\"\r\n", System.Text.Encoding.UTF8.GetString(bytes[3..]));
        Assert.True(stream.CanRead);
    }

    /// <summary>Checks complete data round trips through compact tables whose columns belong to that section, including multiline notes.</summary>
    [Fact]
    public void ExportsCompletePreviewWithInvariantNumbersAndHistory()
    {
        ColonizationProjectPreviewData data = ColonizationProjectPreviewTests.Example();
        data = data with
        {
            Project = data.Project with
            {
                Notes = "Line one, quoted \"text\"\nLine two",
                DiscordLink = "https://example.invalid",
                Commanders = new() { ["Cmdr"] = ["steel"] },
            },
            Statistics = new()
            {
                TotalCargo = 30,
                TotalDeliveries = 2,
                Start = DateTimeOffset.UnixEpoch,
                End = DateTimeOffset.UnixEpoch,
                Cmdrs = new() { ["Cmdr"] = 30 },
                Stats =
                [
                    new()
                    {
                        Time = DateTimeOffset.UnixEpoch,
                        Cmdrs = new() { ["Cmdr"] = 30 },
                    },
                ],
            },
        };
        var preview = new ColonizationProjectPreview(data, DateTimeOffset.UnixEpoch);
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string csv = ColonizationProjectCsvExporter.Write(preview, 128);
            Assert.Contains("\"Remaining cargo\",\"9922\"", csv);
            Assert.Contains("\"Fleet carrier deficit\",\"8261\"", csv);
            Assert.Contains("Cmdr (steel)", csv);
            Assert.Contains("Line one, quoted \"\"text\"\"\nLine two", csv);
            Assert.Contains("\"Security\",\"'+10\"", csv);
            Assert.Contains("1970-01-01T00:00:00.0000000+00:00", csv);
            Assert.EndsWith("\r\n", csv);
            Dictionary<string, List<string[]>> tables = ParseTables(csv);
            Assert.Equal(6, tables.Count);
            Assert.Equal(FieldHeader, tables["Project details"][0]);
            Assert.All(tables["Project details"], row => Assert.Equal(2, row.Length));
            Assert.Contains(tables["Project details"], row => row.SequenceEqual(new[] { "Notes", data.Project.Notes }));
            Assert.Contains(
                tables["Project details"],
                row => row.SequenceEqual(new[] { "Current ship capacity", "128" })
            );
            Assert.Contains(tables["Project details"], row => row.SequenceEqual(new[] { "Current ship trips", "78" }));
            List<string[]> cargo = tables["Cargo requirements (tonnes)"];
            string[] cargoHeader =
            [
                "Category",
                "Commodity",
                "Need",
                "FC Diff",
                .. preview.Carriers.Select(carrier => carrier.Label),
            ];
            Assert.Equal(cargoHeader, cargo[0]);
            Assert.Equal(preview.Rows.Count + 1, cargo.Count);
            Assert.All(cargo, row => Assert.Equal(6, row.Length));
            Assert.Contains(
                cargo,
                row => row.SequenceEqual(new[] { "Metals", "Aluminium", "2414", "-2414", "0", "0" })
            );
            Assert.All(tables["Fleet carrier cargo (tonnes)"], row => Assert.Equal(2, row.Length));
            Assert.All(tables["System effects"], row => Assert.Equal(2, row.Length));
            Assert.Equal(CommanderDeliveryHeader, tables["Commander deliveries (tonnes)"][0]);
            Assert.Equal(ExpectedCommanderDelivery, tables["Commander deliveries (tonnes)"][1]);
            Assert.Equal(HourlyDeliveryHeader, tables["Hourly deliveries (tonnes)"][0]);
            Assert.Equal(ExpectedHourlyDelivery, tables["Hourly deliveries (tonnes)"][1]);
            Assert.Equal(
                "Raven-build-test-build-19700101-000000.csv",
                ColonizationProjectCsvExporter.SuggestedFileName(preview)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Protects formula-like public names and notes while keeping signed numeric deficit cells usable in Excel.</summary>
    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData("+cmd")]
    [InlineData("-cmd")]
    [InlineData("@SUM(1)")]
    [InlineData(" \t=SUM(1)")]
    public void EscapesUntrustedSpreadsheetText(string text)
    {
        ColonizationProjectPreviewData data = ColonizationProjectPreviewTests.Example();
        data = data with
        {
            Project = data.Project with { BuildId = "../id", BuildName = text, Notes = text },
            Statistics = null,
        };
        var preview = new ColonizationProjectPreview(data, DateTimeOffset.UnixEpoch);
        string csv = ColonizationProjectCsvExporter.Write(preview);
        Assert.Contains($"\"'{text}\"", csv);
        Assert.Contains("\"-2414\"", csv);
        Assert.DoesNotContain("Hourly deliveries", csv);
        Assert.StartsWith("Raven-build-___id-", ColonizationProjectCsvExporter.SuggestedFileName(preview));
    }

    /// <summary>Preserves empty cells for unknown stock, statistics, progress, and unrecognized build effects.</summary>
    [Fact]
    public void ExportsUnknownDataWithoutInventingZeroes()
    {
        ColonizationProjectPreviewData data = ColonizationProjectPreviewTests.Example();
        data = data with
        {
            CarrierCargo = null,
            Statistics = null,
            Project = data.Project with { BuildType = "unknown", MaximumRequired = 0 },
        };
        string csv = ColonizationProjectCsvExporter.Write(new(data, DateTimeOffset.UnixEpoch));
        Assert.Contains("\"Fleet carrier deficit\",\"\"", csv);
        Assert.Contains("\"Progress (%)\",\"\"", csv);
        Assert.DoesNotContain("System effects", csv);
        Dictionary<string, List<string[]>> tables = ParseTables(csv);
        Assert.All(tables["Cargo requirements (tonnes)"].Skip(1), row => Assert.All(row.Skip(3), Assert.Empty));
        Assert.All(tables["Fleet carrier cargo (tonnes)"].Skip(1), row => Assert.Empty(row[1]));
    }

    /// <summary>Omits table headings and unused columns when a build has no cargo, carrier links, effects, or delivery records.</summary>
    [Fact]
    public void OmitsEmptyTablesButKeepsSummaryAndKnownZeroes()
    {
        var project = new ColonizationProject { BuildId = "empty", BuildType = "unknown" };
        var preview = new ColonizationProjectPreview(new(project, [], new()), DateTimeOffset.UnixEpoch);
        Dictionary<string, List<string[]>> tables = ParseTables(ColonizationProjectCsvExporter.Write(preview));
        Assert.Single(tables);
        List<string[]> details = tables["Project details"];
        Assert.All(details, row => Assert.Equal(2, row.Length));
        Assert.Contains(details, row => row.SequenceEqual(new[] { "Remaining cargo", "0" }));
        Assert.Contains(details, row => row.SequenceEqual(new[] { "Tracked deliveries", "0" }));
        Assert.DoesNotContain(details, row => row[0].StartsWith("Current ship", StringComparison.Ordinal));
    }

    /// <summary>Parses section titles and CSV records with a framework reader, preserving escaped quotes, blank values, and multiline text.</summary>
    private static Dictionary<string, List<string[]>> ParseTables(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv))
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");
        var tables = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        List<string[]>? table = null;
        while (!parser.EndOfData)
        {
            string[] cells = Assert.IsType<string[]>(parser.ReadFields());
            if (cells.Length == 1)
            {
                table = [];
                tables.Add(cells[0], table);
            }
            else
            {
                Assert.NotNull(table);
                table.Add(cells);
            }
        }
        return tables;
    }
}
