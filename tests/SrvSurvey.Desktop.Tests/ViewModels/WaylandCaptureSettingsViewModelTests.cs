using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
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
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory, logs.Add);
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
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory);
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

    [Fact]
    public async Task ChooseAgainWithoutRestartHandlerExplainsManualRestart()
    {
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory);

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.Contains("Restart SrvSurvey", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChooseAgainExplainsAutomaticRestartFailure()
    {
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory);
        viewModel.RestartRequested += () => throw new InvalidOperationException("replacement unavailable");

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.Contains("automatic restart failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replacement unavailable", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChooseAgainCommandRunsTheReselectionWorkflow()
    {
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory);
        viewModel.RestartRequested += () =>
        {
            restarted.SetResult();
            return Task.CompletedTask;
        };

        viewModel.ChooseCaptureSourceAgainCommand.Execute(null);
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(File.Exists(WaylandCaptureSourceSelection.GetReselectionRequestPath(dataDirectory)));
    }

    [Fact]
    public void CaptureDefaultsOffAndBlocksReselection()
    {
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true);

        Assert.False(viewModel.IsEnabled);
        Assert.False(viewModel.IsFssTuningEnabled);
        Assert.False(viewModel.IsFirstFootfallEnabled);
        Assert.False(viewModel.IsSurfaceMiningRigEnabled);
        Assert.False(viewModel.CanConfigureTrackers);
        Assert.False(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
        Assert.Contains("off", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(GameScreenCapture.WaylandPortalEnabled);
        Assert.Equal(WaylandCaptureFeatures.None, GameScreenCapture.WaylandPortalFeatures);
    }

    [Fact]
    public void EnablingMasterRequiresAtLeastOneTracker()
    {
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true);

        viewModel.IsEnabled = true;

        Assert.True(viewModel.IsEnabled);
        Assert.True(viewModel.CanConfigureTrackers);
        Assert.False(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
        Assert.Contains("Select at least one tracker", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True(GameScreenCapture.WaylandPortalEnabled);
        Assert.True(new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true).IsEnabled);
    }

    [Fact]
    public void TrackerSelectionsPersistAndGateFeaturesIndependently()
    {
        var logs = new List<string>();
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true, logs.Add);
        viewModel.IsEnabled = true;

        viewModel.IsFssTuningEnabled = true;
        viewModel.IsFirstFootfallEnabled = true;
        viewModel.IsSurfaceMiningRigEnabled = true;

        Assert.True(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
        Assert.True(GameScreenCapture.IsWaylandPortalAllowed(WaylandCaptureFeatures.FssTuning));
        Assert.True(GameScreenCapture.IsWaylandPortalAllowed(WaylandCaptureFeatures.FirstFootfall));
        Assert.True(GameScreenCapture.IsWaylandPortalAllowed(WaylandCaptureFeatures.SurfaceMiningRig));
        Assert.Contains(logs, message => message.Contains("FSS tuning detection enabled", StringComparison.Ordinal));

        viewModel.IsFirstFootfallEnabled = false;

        var reloaded = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true);
        Assert.True(reloaded.IsFssTuningEnabled);
        Assert.False(reloaded.IsFirstFootfallEnabled);
        Assert.True(reloaded.IsSurfaceMiningRigEnabled);

        reloaded.IsFssTuningEnabled = false;

        Assert.False(GameScreenCapture.IsWaylandPortalAllowed(WaylandCaptureFeatures.FssTuning));
        Assert.True(GameScreenCapture.IsWaylandPortalAllowed(WaylandCaptureFeatures.SurfaceMiningRig));
    }

    [Fact]
    public async Task ChooseAgainDoesNotClearSourceWhileDisabled()
    {
        Directory.CreateDirectory(dataDirectory);
        string tokenPath = WaylandCaptureSourceSelection.GetRestoreTokenPath(dataDirectory);
        await File.WriteAllTextAsync(tokenPath, "saved-source");
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true);
        bool restartRequested = false;
        viewModel.RestartRequested += () =>
        {
            restartRequested = true;
            return Task.CompletedTask;
        };

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.True(File.Exists(tokenPath));
        Assert.False(restartRequested);
        Assert.False(File.Exists(WaylandCaptureSourceSelection.GetReselectionRequestPath(dataDirectory)));
    }

    public void Dispose()
    {
        GameScreenCapture.WaylandPortalEnabled = false;
        GameScreenCapture.WaylandPortalFeatures = WaylandCaptureFeatures.None;
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static WaylandCaptureSettingsViewModel CreateEnabledViewModel(
        string dataDirectory,
        Action<string>? log = null
    )
    {
        Directory.CreateDirectory(dataDirectory);
        WaylandCaptureSettingsStore store = new(Path.Combine(dataDirectory, "cross-platform-ui.json"));
        store.Save(new WaylandCapturePreferences(Enabled: true, FssTuningEnabled: true));
        return new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true, log, store);
    }
}
