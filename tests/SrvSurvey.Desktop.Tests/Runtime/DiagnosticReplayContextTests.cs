using Avalonia;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Tests.Runtime;

public sealed class DiagnosticReplayContextTests
{
    [Fact]
    public async Task ContextRedirectsAllMutableRootsAndSimulatesTheGameHost()
    {
        using var temp = new TemporaryDirectory();
        var journalPath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllTextAsync(
            journalPath,
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Replay Cmdr\",\"FID\":\"F123456\"}\n");
        var session = await new ReplaySessionManager().ImportAsync(
            journalPath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);

        var context = await DiagnosticReplayContext.LoadAsync(
            session.ManifestPath,
            CancellationToken.None);
        using var gameWindow = context.CreateGameWindowTracker();
        var snapshot = gameWindow.GetSnapshot();

        Assert.Equal(session.ConfigDirectory, context.AppDataPaths.ConfigDirectory);
        Assert.Equal(session.DataDirectory, context.AppDataPaths.DataDirectory);
        Assert.Equal(session.CacheDirectory, context.AppDataPaths.CacheDirectory);
        Assert.Empty(context.AppDataPaths.LegacyProfileCandidates);
        Assert.Equal(
            Path.GetDirectoryName(session.PlaybackJournalPath),
            context.JournalDirectory);
        Assert.Equal("Replay Cmdr", context.Commander.Name);
        Assert.False(context.ExternalEffectsAllowed);
        Assert.True(snapshot.IsAvailable);
        Assert.True(snapshot.IsVisible);
        Assert.True(snapshot.IsForeground);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), snapshot.ClientBounds);
    }

    [Fact]
    public async Task StartupDoesNotResolveNormalUserPathsInDiagnosticMode()
    {
        using var temp = new TemporaryDirectory();
        var journalPath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllTextAsync(
            journalPath,
            "{\"event\":\"Commander\",\"Name\":\"Imported\",\"FID\":\"F987654\"}\n");
        var session = await new ReplaySessionManager().ImportAsync(
            journalPath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        var normalPathsResolved = false;

        var startup = await DesktopStartupContext.ResolveAsync(
            ["--diagnostic-replay", session.ManifestPath],
            () =>
            {
                normalPathsResolved = true;
                throw new InvalidOperationException(
                    "Normal personal paths must not be resolved.");
            },
            CancellationToken.None);

        Assert.False(normalPathsResolved);
        Assert.True(startup.IsDiagnosticReplay);
        Assert.Equal(session.DataDirectory, startup.AppDataPaths.DataDirectory);
        Assert.Equal("Imported", startup.DiagnosticReplay?.Commander.Name);
    }

    [Fact]
    public async Task StartupRejectsDiagnosticOptionWithoutAManifest()
    {
        var normalPathsResolved = false;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            DesktopStartupContext.ResolveAsync(
                ["--diagnostic-replay"],
                () =>
                {
                    normalPathsResolved = true;
                    throw new InvalidOperationException(
                        "Normal personal paths must not be resolved.");
                },
                CancellationToken.None));

        Assert.False(normalPathsResolved);
        Assert.Contains(
            "manifest path",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnosticNetworkClientDeniesEveryRequest()
    {
        using var temp = new TemporaryDirectory();
        var journalPath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllTextAsync(
            journalPath,
            "{\"event\":\"Commander\",\"Name\":\"Imported\",\"FID\":\"F987654\"}\n");
        var session = await new ReplaySessionManager().ImportAsync(
            journalPath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        var context = await DiagnosticReplayContext.LoadAsync(
            session.ManifestPath,
            CancellationToken.None);
        using var client = context.CreateNetworkClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(
                "https://example.com/must-not-run",
                CancellationToken.None));

        Assert.Contains(
            "disabled during diagnostic replay",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-diagnostic-context-{Guid.NewGuid():N}");
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
