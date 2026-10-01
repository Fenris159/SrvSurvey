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

    /// <summary>Verifies reselection clears a prior token without restarting the app.</summary>
    [Fact]
    public async Task ChooseAgainClearsSavedSourceWithoutRestart()
    {
        Directory.CreateDirectory(dataDirectory);
        string tokenPath = WaylandCaptureSourceSelection.GetRestoreTokenPath(dataDirectory);
        string requestPath = WaylandCaptureSourceSelection.GetReselectionRequestPath(dataDirectory);
        await File.WriteAllTextAsync(tokenPath, "saved-source");
        var logs = new List<string>();
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory, logs.Add);

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.False(File.Exists(tokenPath));
        Assert.True(File.Exists(requestPath));
        Assert.Contains("did not save", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(logs, message => message.Contains("fresh", StringComparison.Ordinal));
    }

    /// <summary>Verifies reselection works before a source has ever been saved.</summary>
    [Fact]
    public async Task ChooseAgainWorksWhenNoSavedSourceExists()
    {
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory);

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.True(File.Exists(WaylandCaptureSourceSelection.GetReselectionRequestPath(dataDirectory)));
    }

    /// <summary>Verifies Settings opens the picker without waiting for game capture.</summary>
    [Fact]
    public async Task ChooseAgainOpensPickerWithoutWaitingForGameCapture()
    {
        Directory.CreateDirectory(dataDirectory);
        WaylandCaptureSettingsStore store = new(Path.Combine(dataDirectory, "cross-platform-ui.json"));
        store.Save(new WaylandCapturePreferences(Enabled: true, FssTuningEnabled: true));
        int pickerCalls = 0;
        var viewModel = new WaylandCaptureSettingsViewModel(
            dataDirectory,
            isApplicable: true,
            settingsStore: store,
            openSourcePicker: () =>
            {
                pickerCalls++;
                return Task.CompletedTask;
            }
        );

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.Equal(1, pickerCalls);
    }

    /// <summary>Verifies capture stays paused until manual source selection completes.</summary>
    [Fact]
    public async Task ChooseAgainKeepsCapturePausedUntilSelectionFinishes()
    {
        var picker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(
            dataDirectory,
            openSourcePicker: () => picker.Task
        );

        Task selection = viewModel.ChooseCaptureSourceAgainAsync();

        Assert.True(WaylandCaptureSourceSelection.IsManualSelectionInProgress);
        Assert.False(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
        picker.SetResult();
        await selection;
        Assert.False(WaylandCaptureSourceSelection.IsManualSelectionInProgress);
        Assert.True(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
    }

    /// <summary>Verifies picker failure is reported and releases the capture pause.</summary>
    [Fact]
    public async Task ChooseAgainReportsPickerFailureAndReleasesCapturePause()
    {
        var logs = new List<string>();
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(
            dataDirectory,
            logs.Add,
            openSourcePicker: () => throw new NotSupportedException("Picker unavailable")
        );

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.Contains("Picker unavailable", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(logs, message => message.Contains("Picker unavailable", StringComparison.Ordinal));
        Assert.False(WaylandCaptureSourceSelection.IsManualSelectionInProgress);
    }

    /// <summary>Verifies picker failure remains visible when no logger was supplied.</summary>
    [Fact]
    public async Task ChooseAgainReportsPickerFailureWithoutLogger()
    {
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(
            dataDirectory,
            openSourcePicker: () => throw new NotSupportedException("Picker unavailable")
        );

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.Contains("Picker unavailable", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(WaylandCaptureSourceSelection.IsManualSelectionInProgress);
    }

    /// <summary>Verifies non-Wayland sessions cannot reset portal source state.</summary>
    [Fact]
    public async Task ChooseAgainIsDisabledOutsideWaylandSessions()
    {
        Directory.CreateDirectory(dataDirectory);
        string tokenPath = WaylandCaptureSourceSelection.GetRestoreTokenPath(dataDirectory);
        await File.WriteAllTextAsync(tokenPath, "saved-source");
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: false);

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.True(File.Exists(tokenPath));
        Assert.False(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
    }

    /// <summary>Verifies a newly saved portal source is reported to the user.</summary>
    [Fact]
    public async Task ChooseAgainReportsWhenSelectionWasSaved()
    {
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(
            dataDirectory,
            openSourcePicker: () =>
            {
                WaylandCaptureSourceSelection.StoreRestoreToken(dataDirectory, "new-source");
                return Task.CompletedTask;
            }
        );

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.Contains("Capture source selected", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("new-source", WaylandCaptureSourceSelection.ReadRestoreToken(dataDirectory));
        Assert.True(viewModel.ChooseCaptureSourceAgainCommand.CanExecute(null));
    }

    /// <summary>Verifies the Settings command starts the source-reselection workflow.</summary>
    [Fact]
    public void ChooseAgainCommandRunsTheReselectionWorkflow()
    {
        WaylandCaptureSettingsViewModel viewModel = CreateEnabledViewModel(dataDirectory);

        viewModel.ChooseCaptureSourceAgainCommand.Execute(null);

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

    /// <summary>Verifies disabled Wayland capture keeps the saved source intact.</summary>
    [Fact]
    public async Task ChooseAgainDoesNotClearSourceWhileDisabled()
    {
        Directory.CreateDirectory(dataDirectory);
        string tokenPath = WaylandCaptureSourceSelection.GetRestoreTokenPath(dataDirectory);
        await File.WriteAllTextAsync(tokenPath, "saved-source");
        var viewModel = new WaylandCaptureSettingsViewModel(dataDirectory, isApplicable: true);

        await viewModel.ChooseCaptureSourceAgainAsync();

        Assert.True(File.Exists(tokenPath));
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

    /// <summary>Builds an enabled view model with an injectable picker for tests.</summary>
    private static WaylandCaptureSettingsViewModel CreateEnabledViewModel(
        string dataDirectory,
        Action<string>? log = null,
        Func<Task>? openSourcePicker = null
    )
    {
        Directory.CreateDirectory(dataDirectory);
        WaylandCaptureSettingsStore store = new(Path.Combine(dataDirectory, "cross-platform-ui.json"));
        store.Save(new WaylandCapturePreferences(Enabled: true, FssTuningEnabled: true));
        return new WaylandCaptureSettingsViewModel(
            dataDirectory,
            isApplicable: true,
            log,
            store,
            openSourcePicker ?? (() => Task.CompletedTask)
        );
    }
}
