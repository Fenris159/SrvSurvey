using SrvSurvey.Core.Diagnostics.Replay;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Tests.Diagnostics.Replay;

public sealed class ReplaySessionManagerTests
{
    [Fact]
    public async Task ImportCreatesAnIsolatedSessionFromACommanderJournal()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log");
        await File.WriteAllLinesAsync(
            sourcePath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Fileheader\",\"gameversion\":\"4.2\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Replay Cmdr\",\"FID\":\"F123456\",\"Odyssey\":true}",
            ]);

        var session = await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);

        Assert.Equal("Replay Cmdr", session.Commander.Name);
        Assert.Equal("F123456", session.Commander.FrontierId);
        Assert.Equal(3, session.Events.Count);
        Assert.True(File.Exists(session.ManifestPath));
        Assert.True(File.Exists(session.SourceJournalPath));
        Assert.True(File.Exists(session.PlaybackJournalPath));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(
            session.PlaybackJournalPath));
        Assert.StartsWith(
            Path.GetFullPath(session.SessionDirectory),
            Path.GetFullPath(session.DataDirectory),
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(session.SourceJournalPath));
    }

    [Fact]
    public async Task LoadRejectsEvidenceChangedAfterImport()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllLinesAsync(
            sourcePath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}",
            ]);
        var imported = await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        await File.AppendAllTextAsync(
            imported.SourceJournalPath,
            "{\"event\":\"Shutdown\"}\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => DiagnosticReplaySession.LoadAsync(
                imported.ManifestPath,
                CancellationToken.None));

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAcceptsAVersionedReplayPackage()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Package Cmdr\",\"FID\":\"F777777\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Package Cmdr\",\"FID\":\"F777777\"}",
            ]);
        var packagePath = Path.Combine(temp.Path, "incident.srvreplay");
        await new JournalReplayExporter().ExportAsync(
            journals,
            packagePath,
            new JournalReplayExportRequest(
                null,
                null,
                ReplayPrivacyMode.Raw,
                "test"),
            CancellationToken.None);

        var session = await new ReplaySessionManager().ImportAsync(
            packagePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);

        Assert.Equal("Package Cmdr", session.Commander.Name);
        Assert.Equal(2, session.Events.Count);
        Assert.Equal("test", session.SourceVersion);
        Assert.Equal(ReplayPrivacyMode.Raw, session.PrivacyMode);
        Assert.Equal(
            "Commander",
            session.Events[0].EventName);
    }

    [Fact]
    public async Task ImportRejectsJournalWithoutCommanderIdentity()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllTextAsync(
            sourcePath,
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\"}\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReplaySessionManager().ImportAsync(
                sourcePath,
                Path.Combine(temp.Path, "managed"),
                CancellationToken.None));

        Assert.Contains(
            "Personal profile data will not be used as a fallback",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadRejectsManifestPathThatEscapesTheSession()
    {
        using var temp = new TemporaryDirectory();
        var session = await ImportCommanderJournalAsync(temp.Path);
        var manifest = await File.ReadAllTextAsync(session.ManifestPath);
        await File.WriteAllTextAsync(
            session.ManifestPath,
            manifest.Replace(
                "\"configDirectory\": \"config\"",
                "\"configDirectory\": \"../outside\"",
                StringComparison.Ordinal));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            DiagnosticReplaySession.LoadAsync(
                session.ManifestPath,
                CancellationToken.None));

        Assert.Contains(
            "path schema",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("configDirectory", ".")]
    [InlineData("playbackJournal", "source/journal.jsonl")]
    public async Task LoadRejectsManifestPathsThatAliasRetainedEvidence(
        string propertyName,
        string replacement)
    {
        using var temp = new TemporaryDirectory();
        var session = await ImportCommanderJournalAsync(temp.Path);
        var manifest = JsonNode.Parse(
            await File.ReadAllTextAsync(session.ManifestPath))!.AsObject();
        manifest["paths"]!.AsObject()[propertyName] = replacement;
        await File.WriteAllTextAsync(
            session.ManifestPath,
            manifest.ToJsonString());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            DiagnosticReplaySession.LoadAsync(
                session.ManifestPath,
                CancellationToken.None));

        Assert.Contains(
            "path schema",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(session.SourceJournalPath));
    }

    [Theory]
    [InlineData("commander")]
    [InlineData("paths")]
    public async Task LoadRejectsMissingRequiredManifestObjects(string propertyName)
    {
        using var temp = new TemporaryDirectory();
        var session = await ImportCommanderJournalAsync(temp.Path);
        var manifest = JsonNode.Parse(
            await File.ReadAllTextAsync(session.ManifestPath))!.AsObject();
        manifest[propertyName] = null;
        await File.WriteAllTextAsync(
            session.ManifestPath,
            manifest.ToJsonString());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            DiagnosticReplaySession.LoadAsync(
                session.ManifestPath,
                CancellationToken.None));

        Assert.Contains(
            "required",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportRejectsPackageWithMissingCommanderObject()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.01.log"),
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Package Cmdr\",\"FID\":\"F777777\"}\n");
        var packagePath = Path.Combine(temp.Path, "incident.srvreplay");
        await new JournalReplayExporter().ExportAsync(
            journals,
            packagePath,
            new JournalReplayExportRequest(
                null,
                null,
                ReplayPrivacyMode.Raw,
                "test"),
            CancellationToken.None);
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("replay-package.json")!;
            string manifest;
            using (var reader = new StreamReader(entry.Open()))
            {
                manifest = await reader.ReadToEndAsync();
            }

            entry.Delete();
            var replacement = archive.CreateEntry("replay-package.json");
            await using var output = replacement.Open();
            await using var writer = new StreamWriter(output);
            var commanderStart = manifest.IndexOf(
                "\"commander\": {",
                StringComparison.Ordinal);
            var commanderEnd = manifest.IndexOf(
                "  },",
                commanderStart,
                StringComparison.Ordinal);
            await writer.WriteAsync(
                manifest[..commanderStart]
                + "\"commander\": null,"
                + manifest[(commanderEnd + 4)..]);
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReplaySessionManager().ImportAsync(
                packagePath,
                Path.Combine(temp.Path, "managed-package"),
                CancellationToken.None));

        Assert.Contains(
            "commander",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportRejectsOversizedLineBeforeMaterializingItAsAnEvent()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "Journal.oversized.log");
        await File.WriteAllTextAsync(
            sourcePath,
            new string('x', (4 * 1024 * 1024) + 1));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReplaySessionManager().ImportAsync(
                sourcePath,
                Path.Combine(temp.Path, "managed-oversized"),
                CancellationToken.None));

        Assert.Contains(
            "line",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "supported limit",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BoundedReaderStopsConsumingAnUnbrokenLineNearTheLimit()
    {
        var source = new CountingTextReader(
            ReplaySessionManager.MaximumJournalLineCharacters * 4);
        var reader = new ReplaySessionManager.BoundedJournalLineReader(source);

        _ = await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadLineAsync(
                ReplaySessionManager.MaximumJournalLineCharacters,
                CancellationToken.None));

        Assert.InRange(
            source.CharactersRead,
            ReplaySessionManager.MaximumJournalLineCharacters + 1,
            ReplaySessionManager.MaximumJournalLineCharacters + (64 * 1024));
    }

    [Fact]
    public async Task ResetRejectsRuntimeDirectoryLinkWithoutTouchingTarget()
    {
        using var temp = new TemporaryDirectory();
        var session = await ImportCommanderJournalAsync(temp.Path);
        var outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(outside);
        var marker = Path.Combine(outside, "must-survive.txt");
        await File.WriteAllTextAsync(marker, "personal data");
        Directory.Delete(session.ConfigDirectory);
        try
        {
            _ = Directory.CreateSymbolicLink(session.ConfigDirectory, outside);
        }
        catch (Exception linkException) when (
            linkException is UnauthorizedAccessException
                or IOException
                or PlatformNotSupportedException)
        {
            return;
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            session.ResetRuntimeAsync(CancellationToken.None));

        Assert.Contains(
            "symbolic link or reparse point",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(marker));
    }

    private static async Task<DiagnosticReplaySession>
        ImportCommanderJournalAsync(string root)
    {
        var sourcePath = Path.Combine(root, "Journal.01.log");
        await File.WriteAllTextAsync(
            sourcePath,
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}\n");
        return await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(root, "managed"),
            CancellationToken.None);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-replay-tests-{Guid.NewGuid():N}");
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
                // Best-effort cleanup for files still being released by Windows.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup for files still being released by Windows.
            }
        }
    }

    private sealed class CountingTextReader(int length) : TextReader
    {
        private int remaining = length;

        public int CharactersRead { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, remaining);
            buffer.Span[..count].Fill('x');
            remaining -= count;
            CharactersRead += count;
            return ValueTask.FromResult(count);
        }
    }
}
