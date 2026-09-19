using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class WaylandCaptureSettingsViewModel : INotifyPropertyChanged
{
    private readonly string dataDirectory;
    private readonly WaylandCaptureSettingsStore settingsStore;
    private readonly Action<string>? log;
    private readonly WorkspaceCommand chooseCaptureSourceAgainCommand;
    private string statusMessage;
    private bool isEnabled;
    private bool isFssTuningEnabled;
    private bool isFirstFootfallEnabled;
    private bool isSurfaceMiningRigEnabled;
    private bool isBusy;

    public WaylandCaptureSettingsViewModel(
        string dataDirectory,
        bool isApplicable,
        Action<string>? log = null,
        WaylandCaptureSettingsStore? settingsStore = null
    )
    {
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.settingsStore =
            settingsStore
            ?? new WaylandCaptureSettingsStore(Path.Combine(this.dataDirectory, "cross-platform-ui.json"));
        IsApplicable = isApplicable;
        this.log = log;
        WaylandCapturePreferences preferences = this.settingsStore.Load();
        isEnabled = preferences.Enabled;
        isFssTuningEnabled = preferences.FssTuningEnabled;
        isFirstFootfallEnabled = preferences.FirstFootfallEnabled;
        isSurfaceMiningRigEnabled = preferences.SurfaceMiningRigEnabled;
        ApplyCapturePolicy();
        statusMessage = DescribeStatus();
        chooseCaptureSourceAgainCommand = new WorkspaceCommand(
            () => _ = ChooseCaptureSourceAgainAsync(),
            () => IsApplicable && isEnabled && HasEnabledFeatures && !isBusy
        );
        ChooseCaptureSourceAgainCommand = chooseCaptureSourceAgainCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Func<Task>? RestartRequested;

    public bool IsApplicable { get; }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled == value)
            {
                return;
            }

            settingsStore.Save(CurrentPreferences with { Enabled = value });
            isEnabled = value;
            GameScreenCapture.WaylandPortalEnabled = value;
            StatusMessage = DescribeStatus();
            log?.Invoke(
                value
                    ? "Wayland screen capture enabled in Settings."
                    : "Wayland screen capture disabled in Settings; no tracker will use the portal."
            );
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConfigureTrackers));
            chooseCaptureSourceAgainCommand.Refresh();
        }
    }

    public bool CanConfigureTrackers => IsApplicable && IsEnabled;

    public bool IsFssTuningEnabled
    {
        get => isFssTuningEnabled;
        set =>
            SetFeatureEnabled(value, ref isFssTuningEnabled, WaylandCaptureFeatures.FssTuning, "FSS tuning detection");
    }

    public bool IsFirstFootfallEnabled
    {
        get => isFirstFootfallEnabled;
        set =>
            SetFeatureEnabled(
                value,
                ref isFirstFootfallEnabled,
                WaylandCaptureFeatures.FirstFootfall,
                "first-footfall inference"
            );
    }

    public bool IsSurfaceMiningRigEnabled
    {
        get => isSurfaceMiningRigEnabled;
        set =>
            SetFeatureEnabled(
                value,
                ref isSurfaceMiningRigEnabled,
                WaylandCaptureFeatures.SurfaceMiningRig,
                "Surface Mining rig detection"
            );
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value)
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand ChooseCaptureSourceAgainCommand { get; }

    public async Task ChooseCaptureSourceAgainAsync()
    {
        if (!IsApplicable)
        {
            StatusMessage =
                "This session is not using Linux Wayland screen sharing, so no capture source needs to be reset.";
            return;
        }

        if (!isEnabled)
        {
            StatusMessage = DescribeStatus();
            return;
        }

        isBusy = true;
        chooseCaptureSourceAgainCommand.Refresh();
        try
        {
            try
            {
                WaylandCaptureSourceSelection.RequestReselection(dataDirectory);
                log?.Invoke("Requested a fresh Wayland screen-capture source selection after restart.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                StatusMessage = "The saved Wayland capture source could not be cleared: " + exception.Message;
                return;
            }

            Func<Task>? restartHandlers = RestartRequested;
            if (restartHandlers is null)
            {
                StatusMessage =
                    "Capture source cleared. Restart SrvSurvey; the picker opens if capture falls back to Wayland sharing.";
                return;
            }

            StatusMessage = "Capture source cleared; restarting SrvSurvey...";
            try
            {
                foreach (Func<Task> handler in restartHandlers.GetInvocationList().Cast<Func<Task>>())
                {
                    await handler();
                }
            }
            catch (Exception exception)
            {
                StatusMessage =
                    "Capture source cleared, but automatic restart failed: "
                    + exception.Message
                    + " Close and reopen SrvSurvey manually.";
            }
        }
        finally
        {
            isBusy = false;
            chooseCaptureSourceAgainCommand.Refresh();
        }
    }

    private string DescribeStatus()
    {
        if (!IsApplicable)
        {
            return "This session is not using Linux Wayland screen sharing, so no capture source needs to be reset.";
        }

        return isEnabled
            ? DescribeEnabledStatus()
            : "Wayland screen capture is off. No tracker will open the desktop share picker.";
    }

    private WaylandCapturePreferences CurrentPreferences =>
        new(isEnabled, isFssTuningEnabled, isFirstFootfallEnabled, isSurfaceMiningRigEnabled);

    private bool HasEnabledFeatures => isFssTuningEnabled || isFirstFootfallEnabled || isSurfaceMiningRigEnabled;

    private void ApplyCapturePolicy()
    {
        GameScreenCapture.WaylandPortalFeatures = GetEnabledFeatures();
        GameScreenCapture.WaylandPortalEnabled = isEnabled;
    }

    private WaylandCaptureFeatures GetEnabledFeatures()
    {
        WaylandCaptureFeatures features = WaylandCaptureFeatures.None;
        if (isFssTuningEnabled)
        {
            features |= WaylandCaptureFeatures.FssTuning;
        }

        if (isFirstFootfallEnabled)
        {
            features |= WaylandCaptureFeatures.FirstFootfall;
        }

        if (isSurfaceMiningRigEnabled)
        {
            features |= WaylandCaptureFeatures.SurfaceMiningRig;
        }

        return features;
    }

    private void SetFeatureEnabled(
        bool value,
        ref bool field,
        WaylandCaptureFeatures feature,
        string description,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (field == value)
        {
            return;
        }

        WaylandCapturePreferences preferences = feature switch
        {
            WaylandCaptureFeatures.FssTuning => CurrentPreferences with { FssTuningEnabled = value },
            WaylandCaptureFeatures.FirstFootfall => CurrentPreferences with { FirstFootfallEnabled = value },
            WaylandCaptureFeatures.SurfaceMiningRig => CurrentPreferences with { SurfaceMiningRigEnabled = value },
            _ => throw new ArgumentOutOfRangeException(nameof(feature)),
        };
        settingsStore.Save(preferences);
        field = value;
        GameScreenCapture.WaylandPortalFeatures = GetEnabledFeatures();
        StatusMessage = DescribeStatus();
        log?.Invoke($"Wayland screen capture for {description} {(value ? "enabled" : "disabled")} in Settings.");
        OnPropertyChanged(propertyName);
        chooseCaptureSourceAgainCommand.Refresh();
    }

    private string DescribeEnabledStatus()
    {
        var enabled = new List<string>(3);
        if (isFssTuningEnabled)
        {
            enabled.Add("FSS tuning");
        }

        if (isFirstFootfallEnabled)
        {
            enabled.Add("first-footfall");
        }

        if (isSurfaceMiningRigEnabled)
        {
            enabled.Add("Surface Mining rig detection");
        }

        return enabled.Count == 0
            ? "Wayland screen capture is on. Select at least one tracker to allow portal capture."
            : "Allowed trackers: " + string.Join(", ", enabled) + ".";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
