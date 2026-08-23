using SrvSurvey.ReplayController;

namespace SrvSurvey.ReplayController.Tests;

public sealed class ReplayControllerViewModelTests
{
    [Fact]
    public async Task ImportLaunchStepAndPreviousReconstructAnIsolatedRun()
    {
        using var temp = new TemporaryDirectory();
        var journalPath = Path.Combine(temp.Path, "Journal.01.log");
        await File.WriteAllLinesAsync(
            journalPath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Imported Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Imported Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:02Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\"}",
            ]);
        var executable = Path.Combine(temp.Path, "SrvSurvey.Desktop.exe");
        await File.WriteAllTextAsync(executable, string.Empty);
        var launcher = new RecordingLauncher();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            launcher);
        viewModel.SrvSurveyExecutablePath = executable;

        Assert.True(await viewModel.ImportAsync(journalPath));
        Assert.Equal("Imported Cmdr", viewModel.CommanderName);
        Assert.Equal("F123456", viewModel.FrontierId);
        Assert.Equal(3, viewModel.TotalEvents);
        Assert.Equal("Unpackaged Elite journal", viewModel.SourceVersion);
        Assert.Contains(
            "checksum verified",
            viewModel.ValidationStatus,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(await viewModel.LaunchAsync());
        Assert.Single(launcher.ManifestPaths);
        Assert.EndsWith(
            "replay-session.json",
            launcher.ManifestPaths[0],
            StringComparison.OrdinalIgnoreCase);

        Assert.True(await viewModel.StepAsync());
        Assert.True(await viewModel.StepAsync());
        Assert.Equal(2, viewModel.Position);
        var replayStateMarker = Path.Combine(viewModel.DataDirectory, "state.json");
        var replayLogMarker = Path.Combine(viewModel.LogsDirectory, "diagnostic.log");
        await File.WriteAllTextAsync(replayStateMarker, "stale state");
        await File.WriteAllTextAsync(replayLogMarker, "retained evidence");

        Assert.True(await viewModel.PreviousAsync());
        Assert.Equal(1, viewModel.Position);
        Assert.Equal(2, launcher.ManifestPaths.Count);
        Assert.Single(await File.ReadAllLinesAsync(viewModel.PlaybackJournalPath));
        Assert.False(File.Exists(replayStateMarker));
        Assert.True(File.Exists(replayLogMarker));
    }

    private sealed class RecordingLauncher : IDiagnosticInstanceLauncher
    {
        public List<string> ManifestPaths { get; } = [];

        public Task<IDiagnosticInstance> LaunchAsync(
            string executablePath,
            string manifestPath,
            CancellationToken cancellationToken)
        {
            ManifestPaths.Add(manifestPath);
            return Task.FromResult<IDiagnosticInstance>(new Instance());
        }

        private sealed class Instance : IDiagnosticInstance
        {
            public bool IsRunning => true;

            public Task StopAsync(CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-controller-tests-{Guid.NewGuid():N}");
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
