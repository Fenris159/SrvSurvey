using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Runtime;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MainWindowViewModelOwnershipTests
{
    [AvaloniaFact]
    public async Task DisposingParentStopsChildLogProjection()
    {
        await using var context = new TestViewModelContext();
        context.ApplicationLog.Append("Before parent disposal");
        Assert.Contains(
            "Before parent disposal",
            context.ViewModel.DiagnosticsLog.LogText,
            StringComparison.Ordinal);

        await context.ViewModel.DisposeAsync();
        var disposedSnapshot = context.ViewModel.DiagnosticsLog.LogText;
        context.ApplicationLog.Append("After parent disposal");

        Assert.Equal(
            disposedSnapshot,
            context.ViewModel.DiagnosticsLog.LogText);
    }

    [AvaloniaFact]
    public async Task DisposingParentDetachesScreenshotProjection()
    {
        await using var context = new TestViewModelContext();
        await context.ViewModel.DisposeAsync();
        var guardianNotifications = 0;
        context.ViewModel.Guardian.PropertyChanged += (_, _) =>
            guardianNotifications++;

        context.ViewModel.ScreenshotProcessing.TargetFolder = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-screenshot-target-{Guid.NewGuid():N}");

        Assert.Equal(0, guardianNotifications);
    }

    [AvaloniaFact]
    public async Task InjectedWindowLeavesViewModelLifetimeToDesktopRuntime()
    {
        await using var context = new TestViewModelContext();
        var window = new MainWindow(context.ViewModel);
        window.Show();

        window.Close();
        context.ApplicationLog.Append("After production window closed");

        Assert.Contains(
            "After production window closed",
            context.ViewModel.DiagnosticsLog.LogText,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task StandaloneWindowDisposesItsViewModelBeforeClosing()
    {
        await using var context = new TestViewModelContext();
        var window = new MainWindow(
            context.ViewModel,
            ownsApplicationLifetime: true);
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.SetResult();

        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposedSnapshot = context.ViewModel.DiagnosticsLog.LogText;
        context.ApplicationLog.Append("After standalone window closed");

        Assert.Equal(
            disposedSnapshot,
            context.ViewModel.DiagnosticsLog.LogText);
    }

    [AvaloniaTheory]
    [InlineData((int)MainWindowViewModelConstructionCheckpoint.FoundationReady)]
    [InlineData((int)MainWindowViewModelConstructionCheckpoint.OverlayReady)]
    [InlineData((int)MainWindowViewModelConstructionCheckpoint.ExplorationReady)]
    [InlineData((int)MainWindowViewModelConstructionCheckpoint.TravelReady)]
    [InlineData((int)MainWindowViewModelConstructionCheckpoint.OnlineAndShellReady)]
    public void ConstructionFailureRollsBackEveryCompletedFamily(
        int checkpointValue)
    {
        var root = CreateTemporaryRoot();
        try
        {
            List<string> disposalOrder = [];
            var inference = new RecordingFirstFootfallInferenceService(
                disposalOrder);
            var publisher = new RecordingInaraPublisher(disposalOrder);
            var switcher = new RecordingGameWindowSwitcher(disposalOrder);
            var failure = new InvalidOperationException("construction failed");

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                MainWindowViewModelTestBuilder.Create(
                    configuredJournalDirectory: null,
                    builder => builder
                        .WithAppDataPaths(CreatePaths(root))
                        .WithFirstFootfallInferenceService(inference)
                        .WithInaraPublisher(publisher)
                        .WithGameWindowSwitcher(switcher)
                        .FailAt(
                            (MainWindowViewModelConstructionCheckpoint)
                                checkpointValue,
                            failure)));

            Assert.Same(failure, thrown);
            Assert.True(inference.IsDisposed);
            string[] expectedDisposalOrder =
                checkpointValue == (int)
                    MainWindowViewModelConstructionCheckpoint.OnlineAndShellReady
                    ?
                    [
                        "game-window-switcher",
                        "inara",
                        "first-footfall",
                    ]
                    :
                    [
                        "inara",
                        "game-window-switcher",
                        "first-footfall",
                    ];
            Assert.Equal(
                expectedDisposalOrder,
                disposalOrder);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [AvaloniaFact]
    public void ConstructionRollbackIsReversedAndPreservesPrimaryFailure()
    {
        var root = CreateTemporaryRoot();
        try
        {
            List<string> disposalOrder = [];
            var cleanupFailure = new InvalidOperationException(
                "first-footfall cleanup failed");
            var inference = new RecordingFirstFootfallInferenceService(
                disposalOrder,
                cleanupFailure);
            var publisher = new RecordingInaraPublisher(disposalOrder);
            var switcher = new RecordingGameWindowSwitcher(disposalOrder);
            var applicationLog = new ApplicationLogService(
                CreatePaths(root).DataDirectory);
            var primaryFailure = new InvalidOperationException(
                "primary construction failure");

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                MainWindowViewModelTestBuilder.Create(
                    configuredJournalDirectory: null,
                    builder => builder
                        .WithAppDataPaths(CreatePaths(root))
                        .WithApplicationLogService(applicationLog)
                        .WithFirstFootfallInferenceService(inference)
                        .WithInaraPublisher(publisher)
                        .WithGameWindowSwitcher(switcher)
                        .FailAt(
                            MainWindowViewModelConstructionCheckpoint
                                .OnlineAndShellReady,
                            primaryFailure)));

            Assert.Same(primaryFailure, thrown);
            Assert.Equal(
                ["game-window-switcher", "inara", "first-footfall"],
                disposalOrder);
            Assert.Contains(
                "first-footfall cleanup failed",
                applicationLog.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateTemporaryRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-runtime-owner-{Guid.NewGuid():N}");
    }

    private static AppDataPaths CreatePaths(string root)
    {
        return new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []);
    }

    private static void DeleteTemporaryRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
        }
    }

    private sealed class RecordingFirstFootfallInferenceService(
        List<string> disposalOrder,
        Exception? disposeFailure = null)
        : IFirstFootfallInferenceService
    {
        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public bool IsDisposed { get; private set; }

        public Task<FirstFootfallInferenceResult> DetectAsync(
            FirstFootfallInferencePreferences preferences,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FirstFootfallInferenceResult(
                FirstFootfallInferenceOutcome.NotDetected,
                0,
                0,
                null));
        }

        public void Dispose()
        {
            IsDisposed = true;
            disposalOrder.Add("first-footfall");
            if (disposeFailure is not null)
            {
                throw disposeFailure;
            }
        }
    }

    private sealed class RecordingInaraPublisher(List<string> disposalOrder)
        : IInaraPublisher
    {
        public Task<InaraPublicationResult> ApplyAsync(
            InaraPublicationUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(InaraPublicationResult.Empty);
        }

        public Task<InaraPublicationResult> StopAsync(
            CancellationToken cancellationToken = default)
        {
            disposalOrder.Add("inara");
            return Task.FromResult(InaraPublicationResult.Empty);
        }

        public void CancelPendingPublication()
        {
        }

        public void Dispose()
        {
            disposalOrder.Add("inara");
        }
    }

    private sealed class RecordingGameWindowSwitcher(List<string> disposalOrder)
        : IGameWindowSwitcher
    {
        public int GetAvailableWindowCount() => 0;

        public bool TryActivateCurrent() => false;

        public bool TryActivateNext() => false;

        public void Dispose()
        {
            disposalOrder.Add("game-window-switcher");
        }
    }

    private sealed class TestViewModelContext : IAsyncDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-runtime-owner-{Guid.NewGuid():N}");

        public TestViewModelContext()
        {
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            ApplicationLog = new ApplicationLogService(paths.DataDirectory);
            ViewModel = MainWindowViewModelTestBuilder.Create(
                configuredJournalDirectory: null,
                builder => builder
                    .WithAppDataPaths(paths)
                    .WithApplicationLogService(ApplicationLog));
        }

        public ApplicationLogService ApplicationLog { get; }

        public MainWindowViewModel ViewModel { get; }

        public async ValueTask DisposeAsync()
        {
            await ViewModel.DisposeAsync();
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup.
            }
        }
    }
}
