using System.Globalization;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationProjectCsvExporterTests
{
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

    /// <summary>Checks that cargo, project metadata, effects, carrier totals, and hourly deliveries share one rectangular CSV schema.</summary>
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
            Assert.Contains("\"Hourly deliveries\"", csv);
            Assert.Contains("\"Commander deliveries\"", csv);
            Assert.Contains("\"Effect\",\"Security\",\"'+10\"", csv);
            Assert.Contains("1970-01-01T00:00:00.0000000+00:00", csv);
            Assert.EndsWith("\r\n", csv);
            string noMultiline = csv.Replace("\nLine two", "Line two", StringComparison.Ordinal);
            Assert.All(
                noMultiline.Split("\r\n", StringSplitOptions.RemoveEmptyEntries),
                line => Assert.Equal(12, ParseCells(line))
            );
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
        Assert.DoesNotContain("\"Effect\"", csv);
    }

    /// <summary>Counts quoted CSV cells, distinguishing an escaped quote from a field boundary.</summary>
    private static int ParseCells(string line)
    {
        int cells = 1;
        bool quoted = false;
        int index = 0;
        while (index < line.Length)
        {
            if (line[index] == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (line[index] == ',' && !quoted)
            {
                cells++;
            }
            index++;
        }
        return cells;
    }
}
