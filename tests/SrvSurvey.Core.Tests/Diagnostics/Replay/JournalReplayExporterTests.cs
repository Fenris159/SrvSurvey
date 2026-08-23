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

    [Fact]
    public async Task RedactionPreservesJsonKeysAndPseudonymizesEveryIdentityAndLocation()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Name\",\"FID\":\"F111111\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Secret System\",\"SystemAddress\":123456,\"StarPos\":[1.5,2.5,3.5]}",
                "{\"timestamp\":\"2026-08-21T18:01:00Z\",\"event\":\"Commander\",\"Name\":\"Other Cmdr\",\"FID\":\"F222222\"}",
                "{\"timestamp\":\"2026-08-21T18:01:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Other Cmdr\",\"FID\":\"F222222\"}",
                "{\"timestamp\":\"2026-08-21T18:01:02Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Secret System\",\"SystemAddress\":123456,\"StarPos\":[1.5,2.5,3.5]}",
                "{\"timestamp\":\"2026-08-21T18:01:03Z\",\"event\":\"CodexEntry\",\"System\":\"Secret Codex System\",\"SystemAddress\":456789,\"BodyID\":7,\"BodyName\":\"Secret Codex Body\",\"NearestDestination\":\"Secret Port\"}",
                "{\"timestamp\":\"2026-08-21T18:01:04Z\",\"event\":\"Screenshot\",\"Filename\":\"C:\\\\Users\\\\Private Cmdr\\\\Pictures\\\\Secret System.bmp\",\"System\":\"Secret System\",\"Body\":\"Secret Body\"}",
                "{\"timestamp\":\"2026-08-21T18:01:05Z\",\"event\":\"FSDTarget\",\"Name\":\"Secret Destination\",\"SystemAddress\":987654,\"DestinationSystemAddress\":987654}",
                "{\"timestamp\":\"2026-08-21T18:01:06Z\",\"event\":\"ReceiveText\",\"From\":\"Other Cmdr\",\"Message\":\"received secret\",\"Message_Localised\":\"received localized secret\"}",
                "{\"timestamp\":\"2026-08-21T18:01:07Z\",\"event\":\"SendText\",\"To\":\"Other Cmdr\",\"Message\":\"sent secret\",\"Message_Localised\":\"sent localized secret\"}",
            ]);
        var destination = Path.Combine(temp.Path, "redacted.srvreplay");

        await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                null,
                null,
                ReplayPrivacyMode.Redacted,
                "test"),
            CancellationToken.None);

        using (var archive = ZipFile.OpenRead(destination))
        using (var reader = new StreamReader(
                   archive.GetEntry("journal.jsonl")!.Open()))
        {
            var journal = await reader.ReadToEndAsync();
            Assert.Contains("\"Name\":", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("Other Cmdr", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("F222222", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret System", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("123456", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("1.5", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret Codex", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret Destination", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("987654", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("Filename", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("received secret", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("sent secret", journal, StringComparison.Ordinal);
            Assert.Contains("Replay Commander 2", journal, StringComparison.Ordinal);
            Assert.Contains("Replay Location 001", journal, StringComparison.Ordinal);
        }

        var imported = await new ReplaySessionManager().ImportAsync(
            destination,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        Assert.Equal("Replay Commander", imported.Commander.Name);
        Assert.Equal(10, imported.Events.Count);
    }

    [Fact]
    public async Task ExportStreamsRangesOutsideTheHistoryDisplayWindow()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Older\"}",
                "{\"timestamp\":\"2026-08-21T18:00:02Z\",\"event\":\"Music\"}",
                "{\"timestamp\":\"2026-08-21T18:00:03Z\",\"event\":\"Shutdown\"}",
            ]);
        var destination = Path.Combine(temp.Path, "older-range.srvreplay");
        var exporter = new JournalReplayExporter(
            new JournalHistoryReader(maximumLoadedEvents: 2));

        var result = await exporter.ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                DateTimeOffset.Parse("2026-08-21T18:00:01Z"),
                DateTimeOffset.Parse("2026-08-21T18:00:01Z"),
                ReplayPrivacyMode.Raw,
                "test"),
            CancellationToken.None);

        Assert.Equal(2, result.EventCount);
        Assert.Equal(1, result.BootstrapEventCount);
        using var archive = ZipFile.OpenRead(destination);
        using var reader = new StreamReader(
            archive.GetEntry("journal.jsonl")!.Open());
        Assert.Equal(
            ["Commander", "Location"],
            (await reader.ReadToEndAsync())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(GetEventName));
    }

    [Fact]
    public async Task RedactionPreservesSystemAndBodyRelationshipsAcrossEvents()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Private\",\"FID\":\"F111111\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Origin\",\"SystemAddress\":123}",
                "{\"timestamp\":\"2026-08-21T18:00:02Z\",\"event\":\"Scan\",\"SystemAddress\":123,\"BodyID\":7,\"BodyName\":\"Origin 7\"}",
                "{\"timestamp\":\"2026-08-21T18:00:03Z\",\"event\":\"FSDTarget\",\"Name\":\"Destination\",\"DestinationSystemAddress\":456}",
                "{\"timestamp\":\"2026-08-21T18:00:04Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Destination\",\"SystemAddress\":456}",
                "{\"timestamp\":\"2026-08-21T18:00:05Z\",\"event\":\"Scan\",\"SystemAddress\":456,\"BodyID\":7,\"BodyName\":\"Destination 7\"}",
            ]);
        var destination = Path.Combine(temp.Path, "relationships.srvreplay");

        await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                null,
                null,
                ReplayPrivacyMode.Redacted,
                "test"),
            CancellationToken.None);

        using var archive = ZipFile.OpenRead(destination);
        using var reader = new StreamReader(
            archive.GetEntry("journal.jsonl")!.Open());
        var events = (await reader.ReadToEndAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(json => System.Text.Json.JsonDocument.Parse(json))
            .ToArray();
        try
        {
            var targetAddress = events[3].RootElement
                .GetProperty("DestinationSystemAddress")
                .GetInt64();
            var arrivalAddress = events[4].RootElement
                .GetProperty("SystemAddress")
                .GetInt64();
            var originBody = events[2].RootElement
                .GetProperty("BodyID")
                .GetInt64();
            var destinationBody = events[5].RootElement
                .GetProperty("BodyID")
                .GetInt64();

            Assert.Equal(targetAddress, arrivalAddress);
            Assert.NotEqual(originBody, destinationBody);
        }
        finally
        {
            foreach (var replayEvent in events)
            {
                replayEvent.Dispose();
            }
        }
    }

    [Fact]
    public async Task RedactionDoesNotRewriteGeneratedAliasesOnIdentityCollisions()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.01.log"),
            [
                "{\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F111111\"}",
                "{\"event\":\"Commander\",\"Name\":\"Replay\",\"FID\":\"F000000\"}",
            ]);
        var destination = Path.Combine(temp.Path, "identity-collision.srvreplay");

        await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                null,
                null,
                ReplayPrivacyMode.Redacted,
                "test"),
            CancellationToken.None);

        using var archive = ZipFile.OpenRead(destination);
        using var reader = new StreamReader(
            archive.GetEntry("journal.jsonl")!.Open());
        var lines = (await reader.ReadToEndAsync()).Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        using var first = System.Text.Json.JsonDocument.Parse(lines[0]);
        using var second = System.Text.Json.JsonDocument.Parse(lines[1]);
        Assert.Equal(
            "Replay Commander",
            first.RootElement.GetProperty("Name").GetString());
        Assert.Equal(
            "F000000",
            first.RootElement.GetProperty("FID").GetString());
        Assert.Equal(
            "Replay Commander 2",
            second.RootElement.GetProperty("Name").GetString());
        Assert.Equal(
            "F000001",
            second.RootElement.GetProperty("FID").GetString());
    }

    [Fact]
    public async Task RangeBootstrapUsesOneCoherentCommanderIdentity()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"First Cmdr\",\"FID\":\"F111111\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"First Cmdr\",\"FID\":\"F111111\"}",
                "{\"timestamp\":\"2026-08-21T18:10:00Z\",\"event\":\"Commander\",\"Name\":\"Second Cmdr\",\"FID\":\"F222222\"}",
                "{\"timestamp\":\"2026-08-21T18:10:02Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\"}",
            ]);
        var destination = Path.Combine(temp.Path, "second.srvreplay");

        await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                DateTimeOffset.Parse("2026-08-21T18:10:02Z"),
                DateTimeOffset.Parse("2026-08-21T18:10:02Z"),
                ReplayPrivacyMode.Raw,
                "test"),
            CancellationToken.None);

        using var archive = ZipFile.OpenRead(destination);
        using var reader = new StreamReader(
            archive.GetEntry("journal.jsonl")!.Open());
        var journal = await reader.ReadToEndAsync();
        Assert.DoesNotContain("First Cmdr", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("F111111", journal, StringComparison.Ordinal);
        Assert.Contains("Second Cmdr", journal, StringComparison.Ordinal);
        Assert.Contains("F222222", journal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RangeBootstrapDoesNotAttachAPriorCommandersLocation()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"First Cmdr\",\"FID\":\"F111111\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"First System\"}",
                "{\"timestamp\":\"2026-08-21T18:10:00Z\",\"event\":\"Commander\",\"Name\":\"Second Cmdr\",\"FID\":\"F222222\"}",
                "{\"timestamp\":\"2026-08-21T18:10:01Z\",\"event\":\"Shutdown\"}",
            ]);
        var destination = Path.Combine(temp.Path, "second-no-location.srvreplay");

        await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                DateTimeOffset.Parse("2026-08-21T18:10:01Z"),
                DateTimeOffset.Parse("2026-08-21T18:10:01Z"),
                ReplayPrivacyMode.Raw,
                "test"),
            CancellationToken.None);

        using var archive = ZipFile.OpenRead(destination);
        using var reader = new StreamReader(
            archive.GetEntry("journal.jsonl")!.Open());
        var journal = await reader.ReadToEndAsync();
        Assert.DoesNotContain("First System", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("\"event\":\"Location\"", journal, StringComparison.Ordinal);
        Assert.Contains("Second Cmdr", journal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportCarriesPortableOverlayPresentationAndNamesMissingTimelines()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.01.log"),
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}\n");
        var destination = Path.Combine(temp.Path, "presentation.srvreplay");
        var presentation = new ReplayPresentationSnapshot(
            2560,
            1440,
            3,
            0.75,
            new Dictionary<string, bool> { ["PlotFSSInfo"] = false },
            new Dictionary<string, ReplayOverlayPlacement>
            {
                ["PlotFSSInfo"] = new(
                    ReplayHorizontalAnchor.Right,
                    42,
                    ReplayVerticalAnchor.Top,
                    24,
                    0.8,
                    4),
            });

        await new JournalReplayExporter().ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                null,
                null,
                ReplayPrivacyMode.Redacted,
                "test",
                presentation),
            CancellationToken.None);

        using (var archive = ZipFile.OpenRead(destination))
        using (var reader = new StreamReader(
                   archive.GetEntry("replay-package.json")!.Open()))
        {
            var manifest = await reader.ReadToEndAsync();
            Assert.Contains("missingCompanionTimelines", manifest);
            Assert.DoesNotContain("supportedCompanionTimelines", manifest);
            Assert.Contains("presentationSnapshot", manifest);
        }

        var imported = await new ReplaySessionManager().ImportAsync(
            destination,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        Assert.Equal(2560, imported.PresentationSnapshot?.ViewportWidth);
        Assert.Equal(1440, imported.PresentationSnapshot?.ViewportHeight);
        Assert.False(imported.PresentationSnapshot?
            .OverlayEnablement["PlotFSSInfo"]);
        Assert.Equal(
            42,
            imported.PresentationSnapshot?
                .OverlayPlacements["PlotFSSInfo"].HorizontalOffset);
    }

    [Fact]
    public async Task FailedExportPreservesAnExistingPackage()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.01.log"),
            "{\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}\n");
        var destination = Path.Combine(temp.Path, "existing.srvreplay");
        await File.WriteAllTextAsync(destination, "valid existing evidence");
        var exporter = new JournalReplayExporter(new FailingPackageWriter());

        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(
            journals,
            destination,
            new JournalReplayExportRequest(
                null,
                null,
                ReplayPrivacyMode.Redacted,
                "test"),
            CancellationToken.None));

        Assert.Equal(
            "valid existing evidence",
            await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(
            temp.Path,
            ".existing.srvreplay.*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(
            temp.Path,
            ".journal-export.*.tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExportRejectsBlankSourceVersion(string sourceVersion)
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.01.log"),
            "{\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}\n");
        var destination = Path.Combine(temp.Path, "invalid.srvreplay");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new JournalReplayExporter().ExportAsync(
                journals,
                destination,
                new JournalReplayExportRequest(
                    null,
                    null,
                    ReplayPrivacyMode.Raw,
                    sourceVersion),
                CancellationToken.None));

        Assert.Contains(
            "source version",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ExportRejectsOversizedSourceVersion()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.01.log"),
            "{\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}\n");
        var destination = Path.Combine(temp.Path, "invalid.srvreplay");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new JournalReplayExporter().ExportAsync(
                journals,
                destination,
                new JournalReplayExportRequest(
                    null,
                    null,
                    ReplayPrivacyMode.Raw,
                    new string(
                        'x',
                        ReplaySessionManager.MaximumSourceVersionCharacters + 1)),
                CancellationToken.None));

        Assert.Contains(
            "source version",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData(2_000_001, 1)]
    [InlineData(1, (256L * 1024L * 1024L) + 1)]
    public void ExportBoundsMatchWhatTheImporterCanRead(
        int eventCount,
        long byteCount)
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            JournalReplayExporter.ValidateOutputBounds(eventCount, byteCount));

        Assert.Contains(
            "supported package limit",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FailingPackageWriter : IReplayPackageWriter
    {
        public async Task WriteAsync(
            string path,
            JournalReplayPackageManifest package,
            string journalPath,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(path, "partial", cancellationToken);
            throw new IOException("simulated archive failure");
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
