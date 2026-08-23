using SrvSurvey.ReplayController;
using SrvSurvey.Core.Diagnostics.Replay;
using System.ComponentModel;

namespace SrvSurvey.ReplayController.Tests;

public sealed class ReplayControllerViewModelTests
{
    [Fact]
    public void SelectedEventTextIsSafeBeforeImport()
    {
        using var temp = new TemporaryDirectory();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            new RecordingLauncher());
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName);

        Assert.Equal(string.Empty, viewModel.SelectedEventRawJson);

        viewModel.SelectedEvent = new JournalReplayEvent(
            0,
            DateTimeOffset.Parse(
                "2026-08-21T18:01:00Z",
                System.Globalization.CultureInfo.InvariantCulture),
            "FSDJump",
            "{\"event\":\"FSDJump\"}");

        Assert.Equal(
            "{\"event\":\"FSDJump\"}",
            viewModel.SelectedEventRawJson);
        Assert.Contains(nameof(viewModel.SelectedEventRawJson), changedProperties);
    }

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

    [Fact]
    public async Task LaunchFailureFromInvalidExecutableIsReported()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, executable) = await CreateInputsAsync(temp.Path);
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            new FailingLauncher(new Win32Exception("not executable")));
        viewModel.SrvSurveyExecutablePath = executable;
        Assert.True(await viewModel.ImportAsync(journalPath));

        Assert.False(await viewModel.LaunchAsync());

        Assert.Contains(
            "Launch failed",
            viewModel.StatusMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "not executable",
            viewModel.StatusMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnexpectedChildExitReportsCodeAndRetainedLogs()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, executable) = await CreateInputsAsync(temp.Path);
        var launcher = new ControlledLauncher();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            launcher);
        viewModel.SrvSurveyExecutablePath = executable;
        Assert.True(await viewModel.ImportAsync(journalPath));
        Assert.True(await viewModel.LaunchAsync());

        launcher.Instances[0].Exit(17);
        await WaitUntilAsync(() => viewModel.StatusMessage.Contains(
            "unexpectedly",
            StringComparison.Ordinal));

        Assert.Contains("unexpectedly", viewModel.StatusMessage);
        Assert.Contains("code 17", viewModel.StatusMessage);
        Assert.Contains(viewModel.LogsDirectory, viewModel.StatusMessage);
    }

    [Fact]
    public async Task PlaybackLocksPreviousStepAndSpeedChangesUntilPaused()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, executable) = await CreateInputsAsync(temp.Path);
        var launcher = new ControlledLauncher();
        var delay = new BlockingDelay();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            launcher,
            playerFactory: session => new JournalReplayPlayer(session, delay));
        viewModel.SrvSurveyExecutablePath = executable;
        Assert.True(await viewModel.ImportAsync(journalPath));
        Assert.True(await viewModel.LaunchAsync());
        viewModel.SpeedMultiplier = 10;

        var playback = viewModel.PlayAsync();
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.True(viewModel.IsPlaying);
            Assert.False(viewModel.CanChangeSpeed);
            Assert.Equal(1, viewModel.Position);
            Assert.False(viewModel.PreviousCommand.CanExecute(null));
            Assert.False(viewModel.StepCommand.CanExecute(null));

            viewModel.SpeedMultiplier = 25;

            Assert.Equal(10, viewModel.SpeedMultiplier);
            Assert.False(await viewModel.PreviousAsync());
            Assert.False(await viewModel.StepAsync());
            Assert.Equal(1, viewModel.Position);
        }
        finally
        {
            viewModel.Pause();
            await playback;
        }

        Assert.True(viewModel.CanChangeSpeed);
        Assert.True(viewModel.PreviousCommand.CanExecute(null));
        viewModel.SpeedMultiplier = 25;
        Assert.Equal(25, viewModel.SpeedMultiplier);
    }

    [Fact]
    public async Task ChildExitCancelsPlaybackAndKeepsExitOutcomeAuthoritative()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, executable) = await CreateInputsAsync(temp.Path);
        var launcher = new ControlledLauncher();
        var delay = new BlockingDelay();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            launcher,
            playerFactory: session => new JournalReplayPlayer(session, delay));
        viewModel.SrvSurveyExecutablePath = executable;
        Assert.True(await viewModel.ImportAsync(journalPath));
        Assert.True(await viewModel.LaunchAsync());
        var playback = viewModel.PlayAsync();
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        launcher.Instances[0].Exit(23);
        await playback;
        await WaitUntilAsync(() => viewModel.StatusMessage.Contains(
            "code 23",
            StringComparison.Ordinal));

        Assert.False(viewModel.IsPlaying);
        Assert.False(viewModel.IsInstanceRunning);
        Assert.Contains("unexpectedly", viewModel.StatusMessage);
        Assert.Contains(viewModel.LogsDirectory, viewModel.StatusMessage);
    }

    [Fact]
    public async Task PlaybackIoFailureIsReportedWithoutEscapingTheCommandPath()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, executable) = await CreateInputsAsync(temp.Path);
        var launcher = new ControlledLauncher();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            launcher);
        viewModel.SrvSurveyExecutablePath = executable;
        Assert.True(await viewModel.ImportAsync(journalPath));
        Assert.True(await viewModel.LaunchAsync());
        Directory.Delete(
            Path.GetDirectoryName(viewModel.PlaybackJournalPath)!,
            recursive: true);

        await viewModel.PlayAsync();

        Assert.False(viewModel.IsPlaying);
        Assert.Contains(
            "I/O failure",
            viewModel.StatusMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.LogsDirectory, viewModel.StatusMessage);
    }

    [Fact]
    public async Task ConcurrentRestartIsRejectedWhileStopIsPending()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, executable) = await CreateInputsAsync(temp.Path);
        var launcher = new ControlledLauncher();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            launcher);
        viewModel.SrvSurveyExecutablePath = executable;
        Assert.True(await viewModel.ImportAsync(journalPath));
        Assert.True(await viewModel.LaunchAsync());
        launcher.Instances[0].BlockStop();

        var firstRestart = viewModel.RestartAsync();
        await launcher.Instances[0].StopStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        var secondRestart = await viewModel.RestartAsync();

        Assert.False(secondRestart);
        launcher.Instances[0].ReleaseStop();
        Assert.True(await firstRestart);
        Assert.Equal(2, launcher.Instances.Count);
    }

    [Fact]
    public async Task WindowCloseWaitsForTheDiagnosticProcessToStop()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, executable) = await CreateInputsAsync(temp.Path);
        var launcher = new ControlledLauncher();
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            launcher);
        viewModel.SrvSurveyExecutablePath = executable;
        Assert.True(await viewModel.ImportAsync(journalPath));
        Assert.True(await viewModel.LaunchAsync());
        var diagnosticInstance = launcher.Instances[0];
        diagnosticInstance.BlockStop();
        var closeCompleted = false;
        var coordinator = new ReplayControllerWindowCloseCoordinator(
            viewModel.DisposeAsync,
            () => closeCompleted = true);
        var closeStarted = false;

        try
        {
            closeStarted = true;
            Assert.True(coordinator.ShouldCancelClose());
            await diagnosticInstance.StopStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            Assert.False(closeCompleted);
            Assert.True(diagnosticInstance.IsRunning);

            diagnosticInstance.ReleaseStop();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(closeCompleted);
            Assert.False(diagnosticInstance.IsRunning);
            Assert.False(coordinator.ShouldCancelClose());
        }
        finally
        {
            diagnosticInstance.ReleaseStop();
            if (closeStarted)
            {
                await coordinator.Completion;
            }
            else
            {
                await viewModel.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task DisposalReleasesTheOwnedReplayPlayer()
    {
        using var temp = new TemporaryDirectory();
        var (journalPath, _) = await CreateInputsAsync(temp.Path);
        JournalReplayPlayer? ownedPlayer = null;
        var viewModel = new ReplayControllerViewModel(
            Path.Combine(temp.Path, "sessions"),
            playerFactory: session =>
            {
                ownedPlayer = new JournalReplayPlayer(session);
                return ownedPlayer;
            });
        Assert.True(await viewModel.ImportAsync(journalPath));

        await viewModel.DisposeAsync();

        var disposedPlayer = Assert.IsType<JournalReplayPlayer>(ownedPlayer);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            disposedPlayer.StepAsync(CancellationToken.None));
    }

    private static async Task<(string JournalPath, string Executable)>
        CreateInputsAsync(string root)
    {
        var journalPath = Path.Combine(root, "Journal.01.log");
        await File.WriteAllLinesAsync(
            journalPath,
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"Imported Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:05Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\"}",
            ]);
        var executable = Path.Combine(root, "SrvSurvey.Desktop.exe");
        await File.WriteAllTextAsync(executable, string.Empty);
        return (journalPath, executable);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
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

            public async Task<int> WaitForExitAsync(
                CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }

            public Task StopAsync(CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FailingLauncher(Exception exception)
        : IDiagnosticInstanceLauncher
    {
        public Task<IDiagnosticInstance> LaunchAsync(
            string executablePath,
            string manifestPath,
            CancellationToken cancellationToken) =>
            Task.FromException<IDiagnosticInstance>(exception);
    }

    private sealed class ControlledLauncher : IDiagnosticInstanceLauncher
    {
        public List<ControlledInstance> Instances { get; } = [];

        public Task<IDiagnosticInstance> LaunchAsync(
            string executablePath,
            string manifestPath,
            CancellationToken cancellationToken)
        {
            var instance = new ControlledInstance();
            Instances.Add(instance);
            return Task.FromResult<IDiagnosticInstance>(instance);
        }
    }

    private sealed class ControlledInstance : IDiagnosticInstance
    {
        private readonly TaskCompletionSource<int> exit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? stopRelease;
        private bool running = true;

        public TaskCompletionSource StopStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning => running;

        public void BlockStop()
        {
            stopRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseStop() => stopRelease?.TrySetResult();

        public void Exit(int exitCode)
        {
            running = false;
            exit.TrySetResult(exitCode);
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            exit.Task.WaitAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            StopStarted.TrySetResult();
            if (stopRelease is not null)
            {
                await stopRelease.Task.WaitAsync(cancellationToken);
            }

            Exit(0);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingDelay : IReplayDelay
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
