using System.IO.Compression;
using SrvSurvey.Core.Diagnostics.Replay;

namespace SrvSurvey.Core.Tests.Diagnostics.Replay;

public sealed class JournalReplayExporterTests
{
    [Fact]
    public async Task ExportPrependsCommanderAndSessionBootstrapForSelectedRange()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Fileheader\",\"gameversion\":\"4.2\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Replay Cmdr\",\"FID\":\"F123456\",\"Odyssey\":true}",
                "{\"timestamp\":\"2026-08-21T18:00:03Z\",\"event\":\"Location\",\"StarSystem\":\"Bootstrap\",\"SystemAddress\":1}",
                "{\"timestamp\":\"2026-08-21T18:10:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Selected\",\"SystemAddress\":2}",
                "{\"timestamp\":\"2026-08-21T18:20:00Z\",\"event\":\"Shutdown\"}",
            ]);
        var destination = Path.Combine(temp.Path, "incident.srvreplay");

        var result = await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                new DateTimeOffset(2026, 8, 21, 18, 9, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 21, 18, 11, 0, TimeSpan.Zero),
                ReplayPrivacyMode.Raw,
                "2.1.3-rc.36"),
            CancellationToken.None);

        Assert.Equal(5, result.EventCount);
        Assert.Equal(4, result.BootstrapEventCount);
        using var archive = ZipFile.OpenRead(destination);
        var journalEntry = archive.GetEntry("journal.jsonl");
        Assert.NotNull(journalEntry);
        using var reader = new StreamReader(journalEntry.Open());
        var lines = (await reader.ReadToEndAsync()).Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            ["Fileheader", "Commander", "LoadGame", "Location", "FSDJump"],
            lines.Select(GetEventName));
        Assert.NotNull(archive.GetEntry("replay-package.json"));
    }

    [Theory]
    [InlineData(ReplayPrivacyMode.Raw)]
    [InlineData(ReplayPrivacyMode.Redacted)]
    public async Task ExportAlwaysRemovesCredentialsAndRedactionIsConsistent(
        ReplayPrivacyMode privacyMode)
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Private Cmdr\",\"FID\":\"F999999\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Private Cmdr\",\"FID\":\"F999999\",\"AccessToken\":\"do-not-share\",\"Nested\":{\"ApiKey\":\"also-secret\"}}",
                "{\"timestamp\":\"2026-08-21T18:00:02Z\",\"event\":\"ReceiveText\",\"From\":\"Private Cmdr\",\"Message\":\"private message\"}",
            ]);
        var destination = Path.Combine(temp.Path, $"{privacyMode}.srvreplay");

        await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(null, null, privacyMode, "test"),
            CancellationToken.None);

        using var archive = ZipFile.OpenRead(destination);
        using var reader = new StreamReader(
            archive.GetEntry("journal.jsonl")!.Open());
        var journal = await reader.ReadToEndAsync();
        Assert.DoesNotContain("do-not-share", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("also-secret", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", journal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", journal, StringComparison.OrdinalIgnoreCase);
        if (privacyMode == ReplayPrivacyMode.Redacted)
        {
            Assert.DoesNotContain("Private Cmdr", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("F999999", journal, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private message", journal, StringComparison.Ordinal);
            Assert.Contains("Replay Commander", journal, StringComparison.Ordinal);
            Assert.Contains("F000000", journal, StringComparison.Ordinal);
        }
    }

    private static string GetEventName(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("event").GetString()!;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-replay-export-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
