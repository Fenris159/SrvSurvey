using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Runtime;

public sealed class DiagnosticReplayCommanderIntegrationTests
{
    [Fact]
    public async Task ImportedJournalEstablishesTheApplicationCommander()
    {
        using var temp = new TemporaryDirectory();
        var personalData = Path.Combine(temp.Path, "personal-profile");
        Directory.CreateDirectory(personalData);
        var personalMarker = Path.Combine(personalData, "DO-NOT-LOAD.txt");
        await File.WriteAllTextAsync(personalMarker, "Personal Cmdr");
        var sourcePath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllLinesAsync(
            sourcePath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Imported Cmdr\",\"FID\":\"F987654\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Imported Cmdr\",\"FID\":\"F987654\",\"Odyssey\":true}",
            ]);
        var session = await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        var context = await DiagnosticReplayContext.LoadAsync(
            session.ManifestPath,
            CancellationToken.None);
        using var blockedNetwork = DiagnosticReplayContext.CreateNetworkClient();
        using var viewModel = MainWindowViewModelTestBuilder.Create(
            context.JournalDirectory,
            builder => builder
                .WithAppDataPaths(context.AppDataPaths)
                .WithExternalNetworkClient(blockedNetwork)
                .WithFrontierProfile(new CommanderProfileViewModel(
                    new DiagnosticReplayFrontierAccountService()))
                .AsDiagnosticReplay("External effects disabled."));
        var player = new JournalReplayPlayer(session);
        Assert.NotEqual("Imported Cmdr", viewModel.CommanderName);
        _ = await player.StepAsync(CancellationToken.None);
        _ = await player.StepAsync(CancellationToken.None);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsDiagnosticReplay);
        Assert.Equal("Imported Cmdr", viewModel.CommanderName);
        Assert.Equal("F987654", viewModel.FrontierId);
        Assert.Equal(session.DataDirectory, viewModel.AppDataPaths.DataDirectory);
        Assert.Equal("Personal Cmdr", await File.ReadAllTextAsync(personalMarker));
        Assert.DoesNotContain(
            personalData,
            viewModel.ProfileDataDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaterCommanderEventsTransitionApplicationWideIdentityInOrder()
    {
        using var temp = new TemporaryDirectory();
        var sourcePath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllLinesAsync(
            sourcePath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"First Cmdr\",\"FID\":\"F111111\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"First Cmdr\",\"FID\":\"F111111\",\"Odyssey\":true}",
                "{\"timestamp\":\"2026-08-21T18:10:00Z\",\"event\":\"Commander\",\"Name\":\"Second Cmdr\",\"FID\":\"F222222\"}",
                "{\"timestamp\":\"2026-08-21T18:10:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Second Cmdr\",\"FID\":\"F222222\",\"Odyssey\":true}",
            ]);
        var session = await new ReplaySessionManager().ImportAsync(
            sourcePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        var context = await DiagnosticReplayContext.LoadAsync(
            session.ManifestPath,
            CancellationToken.None);
        using var blockedNetwork = DiagnosticReplayContext.CreateNetworkClient();
        using var viewModel = MainWindowViewModelTestBuilder.Create(
            context.JournalDirectory,
            builder => builder
                .WithAppDataPaths(context.AppDataPaths)
                .WithExternalNetworkClient(blockedNetwork)
                .WithFrontierProfile(new CommanderProfileViewModel(
                    new DiagnosticReplayFrontierAccountService()))
                .AsDiagnosticReplay("External effects disabled."));
        var player = new JournalReplayPlayer(session);

        _ = await player.StepAsync(CancellationToken.None);
        _ = await player.StepAsync(CancellationToken.None);
        await viewModel.RefreshAsync();
        Assert.Equal("First Cmdr", viewModel.CommanderName);
        Assert.Equal("F111111", viewModel.FrontierId);

        _ = await player.StepAsync(CancellationToken.None);
        _ = await player.StepAsync(CancellationToken.None);
        await viewModel.RefreshAsync();
        Assert.Equal("Second Cmdr", viewModel.CommanderName);
        Assert.Equal("F222222", viewModel.FrontierId);
        Assert.StartsWith(
            session.DataDirectory,
            viewModel.ProfileDataDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticJournalResolutionNeverFallsBackToLiveCandidates()
    {
        using var temp = new TemporaryDirectory();
        var missingPlayback = Path.Combine(temp.Path, "missing-playback");
        var paths = new SrvSurvey.Core.Storage.AppDataPaths(
            Path.Combine(temp.Path, "config"),
            Path.Combine(temp.Path, "data"),
            Path.Combine(temp.Path, "cache"),
            []);

        using var viewModel = MainWindowViewModelTestBuilder.Create(
            missingPlayback,
            builder => builder
                .WithAppDataPaths(paths)
                .AsDiagnosticReplay("External effects disabled."));

        Assert.Equal(missingPlayback, viewModel.JournalFolderPath);
        Assert.Equal(missingPlayback, viewModel.CandidatePaths);
        Assert.Null(viewModel.CurrentJournalPath);
    }

    [Fact]
    public async Task DiagnosticModeRejectsLegacyProfileImport()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "personal-profile");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-live.json"),
            "personal commander");
        var paths = new SrvSurvey.Core.Storage.AppDataPaths(
            Path.Combine(temp.Path, "config"),
            Path.Combine(temp.Path, "diagnostic-data"),
            Path.Combine(temp.Path, "cache"),
            []);

        using var viewModel = MainWindowViewModelTestBuilder.Create(
            Path.Combine(temp.Path, "playback"),
            builder => builder
                .WithAppDataPaths(paths)
                .AsDiagnosticReplay("External effects disabled."));
        viewModel.LegacyProfileSourcePath = source;

        Assert.False(viewModel.ImportLegacyProfileCommand.CanExecute(null));
        await viewModel.ImportLegacyProfileAsync();

        Assert.False(File.Exists(Path.Combine(
            paths.DataDirectory,
            "F123-live.json")));
        Assert.Contains(
            "unavailable during diagnostic replay",
            viewModel.ProfileStatusMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-diagnostic-commander-{Guid.NewGuid():N}");
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
