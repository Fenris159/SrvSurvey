using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class DiagnosticsLogViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-diagnostics-log-{Guid.NewGuid():N}");

    [Fact]
    public void LiveEntriesRefreshTheDiagnosticsSnapshot()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        using var viewModel = new DiagnosticsLogViewModel(
            log,
            action => action());

        log.Append("Journal loaded");

        Assert.EndsWith(": Journal loaded", viewModel.LogText);
        Assert.Equal("1 session entry", viewModel.SessionDescription);
        Assert.Contains("saved to disk", viewModel.PersistenceStatus);
        Assert.Equal(log.CurrentLogPath, viewModel.CurrentLogPath);
    }

    [Fact]
    public async Task CopyUsesCurrentTextBeforeRecordingTheAction()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        log.Append("First entry");
        using var viewModel = new DiagnosticsLogViewModel(
            log,
            action => action());
        string? copied = null;
        viewModel.SetPlatformServices(
            text =>
            {
                copied = text;
                return Task.CompletedTask;
            },
            null);

        await viewModel.CopyAsync();

        Assert.EndsWith(": First entry", copied);
        Assert.DoesNotContain("Logs copied", copied);
        Assert.EndsWith(": Logs copied", log.Entries[^1]);
        Assert.Contains("copied to the clipboard", viewModel.StatusMessage);
    }

    [Fact]
    public void ClearMatchesLegacyInMemoryResetBehavior()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        log.Append("First entry");
        using var viewModel = new DiagnosticsLogViewModel(
            log,
            action => action());

        viewModel.Clear();

        Assert.Single(log.Entries);
        Assert.EndsWith(": Logs reset", viewModel.LogText);
        Assert.Contains("Earlier entries remain", viewModel.StatusMessage);
    }

    [Fact]
    public async Task OpenFolderUsesTheCrossPlatformDirectoryLauncher()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        using var viewModel = new DiagnosticsLogViewModel(
            log,
            action => action());
        DirectoryInfo? launchedDirectory = null;
        viewModel.SetPlatformServices(
            null,
            directory =>
            {
                launchedDirectory = directory;
                return Task.FromResult(true);
            });

        await viewModel.OpenFolderAsync();

        Assert.Equal(log.LogDirectory, launchedDirectory?.FullName);
        Assert.Equal("Opened the application log folder.", viewModel.StatusMessage);
    }

    [Fact]
    public void MissingServiceProducesAnExplicitUnavailableState()
    {
        using var viewModel = new DiagnosticsLogViewModel(
            null,
            action => action());

        Assert.Equal(
            "No log entries have been recorded for this session.",
            viewModel.LogText);
        Assert.Equal(
            "Application logging is unavailable.",
            viewModel.SessionDescription);
        Assert.False(viewModel.CopyCommand.CanExecute(null));
        Assert.False(viewModel.ClearCommand.CanExecute(null));
        Assert.False(viewModel.OpenFolderCommand.CanExecute(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
