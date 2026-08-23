using SrvSurvey.Core.Diagnostics.Replay;

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
            "escapes",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
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
}
