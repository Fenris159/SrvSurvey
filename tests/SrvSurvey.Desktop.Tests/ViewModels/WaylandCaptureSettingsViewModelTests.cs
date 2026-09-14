using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class WaylandCaptureSettingsViewModelTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-wayland-capture-settings-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public async Task ChooseAgainClearsSavedSourceLogsAndRequestsRestart()
    {
        Directory.CreateDirectory(dataDirectory);
        string tokenPath = WaylandCaptureSourceSelection.GetRestoreTokenPath(dataDirectory);
        string requestPath = WaylandCaptureSourceSelection.GetReselectionRequestPath(dataDirectory);
        await File.WriteAllTextAsync(tokenPath, "saved-source");
        var logs = new List<string>();
        bool restartRequested = false;
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true, logs.Add);
        viewModel.RestartRequested += () =>
        {
            restartRequested = true;
            return Task.CompletedTask;
        };

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.False(File.Exists(tokenPath));
        Assert.True(File.Exists(requestPath));
        Assert.True(restartRequested);
        Assert.Contains("restarting", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logs, message => message.Contains("fresh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChooseAgainStillRequestsRestartWhenSavedSourceWasAlreadyConsumed()
    {
        bool restartRequested = false;
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true);
        viewModel.RestartRequested += () =>
        {
            restartRequested = true;
            return Task.CompletedTask;
        };

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.True(restartRequested);
        Assert.True(File.Exists(WaylandCaptureSourceSelection.GetReselectionRequestPath(dataDirectory)));
    }

    [Fact]
    public async Task ChooseAgainIsDisabledOutsideWaylandSessions()
    {
        Directory.CreateDirectory(dataDirectory);
        string tokenPath = WaylandCaptureSourceSelection.GetRestoreTokenPath(dataDirectory);
        await File.WriteAllTextAsync(tokenPath, "saved-source");
        bool restartRequested = false;
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: false);
        viewModel.RestartRequested += () =>
        {
            restartRequested = true;
            return Task.CompletedTask;
        };

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.True(File.Exists(tokenPath));
        Assert.False(restartRequested);
        Assert.False(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
