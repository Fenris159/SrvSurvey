using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Storage;
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
            ViewModel = new MainWindowViewModel(
                configuredJournalDirectory: null,
                new MainWindowViewModelOptions
                {
                    AppDataPaths = paths,
                    ApplicationLogService = ApplicationLog,
                });
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
