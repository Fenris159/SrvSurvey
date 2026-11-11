using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Controls the opt-in desktop cockpit experiment and the current editor's panel angles.</summary>
public sealed class CockpitOverlayViewModel : INotifyPropertyChanged
{
    private readonly CockpitOverlaySettingsStore settingsStore;
    private readonly CockpitCaptureProfileUpdater? profileUpdater;
    private CockpitOverlayPreferences preferences;
    private Dictionary<string, CockpitPanelPose>? originalPanels;
    private string? selectedPanel;
    private string statusMessage = "Cockpit panels are off.";
    private bool canCalibrate;
    private bool checkingProfile;
    private string captureProfileStatus = "Camera profiles are checked on launch while cockpit mode is enabled.";

    /// <summary>Loads settings without enabling a camera connection or changing the current desktop presentation.</summary>
    public CockpitOverlayViewModel(
        CockpitOverlaySettingsStore settingsStore,
        CockpitCaptureProfileUpdater? profileUpdater = null
    )
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.profileUpdater = profileUpdater;
        preferences = settingsStore.Load();
        StartupEnabled = preferences.Enabled;
        CalibrateCommand = new DelegateCommand(
            () => CalibrationRequested?.Invoke(this, EventArgs.Empty),
            () => CanCalibrate
        );
        ResetPanelCommand = new DelegateCommand(() => SetPose(new CockpitPanelPose()), () => selectedPanel is not null);
        if (Enabled)
        {
            statusMessage = "Waiting for the live cockpit camera bridge.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CalibrationRequested;

    public bool StartupEnabled { get; }

    /// <summary>Persists explicit opt-in; selecting the shared presentation host takes effect after restarting SrvSurvey.</summary>
    public bool Enabled
    {
        get => preferences.Enabled;
        set
        {
            if (value == Enabled)
            {
                return;
            }
            CockpitOverlayPreferences updated = preferences with { Enabled = value };
            settingsStore.Save(updated with { Panels = originalPanels ?? updated.Panels });
            preferences = updated;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RestartRequired));
            StatusMessage = value ? "Waiting for the live cockpit camera bridge." : "Cockpit panels are off.";
        }
    }

    public bool RestartRequired => Enabled && !StartupEnabled;

    public string CaptureProfileStatus
    {
        get => captureProfileStatus;
        private set
        {
            captureProfileStatus = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Runs independently of journal processing; the bridge reads a refreshed profile on Elite's next launch.</summary>
    public async Task RefreshCaptureProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled || checkingProfile || profileUpdater is null)
        {
            return;
        }
        checkingProfile = true;
        CaptureProfileStatus = "Checking the published camera profile...";
        try
        {
            CockpitCaptureProfileRefreshResult result = await profileUpdater.RefreshAsync(cancellationToken);
            CaptureProfileStatus = result.State switch
            {
                CockpitCaptureProfileRefreshState.Updated =>
                    $"Downloaded camera profile v{result.ProfileVersion}. Elite checks it on its next launch.",
                CockpitCaptureProfileRefreshState.Current =>
                    $"Camera profile v{result.ProfileVersion} is current. Checked at SrvSurvey launch.",
                CockpitCaptureProfileRefreshState.Unavailable =>
                    $"Could not check GitHub. Camera profile v{result.ProfileVersion} remains available.",
                CockpitCaptureProfileRefreshState.Invalid =>
                    "The published camera profile was invalid. The current profile was retained.",
                CockpitCaptureProfileRefreshState.Incompatible =>
                    "The published camera profile uses an unsupported protocol. The current profile was retained.",
                CockpitCaptureProfileRefreshState.VersionConflict =>
                    "The published camera profile changed without a version bump. The current profile was retained.",
                _ => "Another SrvSurvey instance is updating the camera profile.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CaptureProfileStatus = "Camera profile check canceled; the current profile was retained.";
        }
        catch (Exception)
        {
            CaptureProfileStatus = "Camera profile check failed; the current profile was retained.";
        }
        finally
        {
            checkingProfile = false;
        }
    }

    public bool HasChanges =>
        originalPanels is not null
        && (
            originalPanels.Count != preferences.Panels.Count
            || originalPanels.Any(entry =>
                !preferences.Panels.TryGetValue(entry.Key, out CockpitPanelPose? pose) || pose != entry.Value
            )
        );

    public string StatusMessage
    {
        get => statusMessage;
        internal set
        {
            if (statusMessage != value)
            {
                statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand CalibrateCommand { get; }
    public ICommand ResetPanelCommand { get; }

    public bool CanCalibrate
    {
        get => canCalibrate;
        internal set
        {
            if (canCalibrate != value)
            {
                canCalibrate = value;
                OnPropertyChanged();
                ((DelegateCommand)CalibrateCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public double Yaw
    {
        get => SelectedPose.Yaw;
        set => SetPose(SelectedPose with { Yaw = value });
    }
    public double Pitch
    {
        get => SelectedPose.Pitch;
        set => SetPose(SelectedPose with { Pitch = value });
    }
    public double Roll
    {
        get => SelectedPose.Roll;
        set => SetPose(SelectedPose with { Roll = value });
    }
    public double Depth
    {
        get => SelectedPose.Depth;
        set => SetPose(SelectedPose with { Depth = value });
    }

    private CockpitPanelPose SelectedPose => Resolve(selectedPanel);

    /// <summary>Returns an independent panel pose, with a front-facing default for panels never edited.</summary>
    public CockpitPanelPose Resolve(string? plotterName) =>
        plotterName is not null && preferences.Panels.TryGetValue(plotterName, out CockpitPanelPose? pose)
            ? pose
            : new CockpitPanelPose();

    /// <summary>Starts an angle edit transaction alongside the existing desktop layout editor.</summary>
    public void BeginEdit() =>
        originalPanels = new Dictionary<string, CockpitPanelPose>(preferences.Panels, StringComparer.Ordinal);

    /// <summary>Selects the same panel as the existing overlay editor.</summary>
    public void SelectPanel(string plotterName)
    {
        selectedPanel = plotterName;
        NotifyPose();
        ((DelegateCommand)ResetPanelCommand).RaiseCanExecuteChanged();
    }

    /// <summary>Saves the angle transaction only when the existing editor saves its layout.</summary>
    public void SaveEdit()
    {
        if (originalPanels is null)
        {
            return;
        }
        settingsStore.Save(preferences);
        originalPanels = null;
    }

    /// <summary>Restores the opening poses when editing is cancelled or the editor closes.</summary>
    public void CancelEdit()
    {
        if (originalPanels is null)
        {
            return;
        }
        preferences = preferences with { Panels = originalPanels };
        originalPanels = null;
        NotifyPose();
    }

    private void SetPose(CockpitPanelPose pose)
    {
        if (selectedPanel is null || !pose.IsValid || pose == SelectedPose)
        {
            return;
        }
        var panels = new Dictionary<string, CockpitPanelPose>(preferences.Panels, StringComparer.Ordinal)
        {
            [selectedPanel] = pose,
        };
        preferences = preferences with { Panels = panels };
        NotifyPose();
    }

    private void NotifyPose()
    {
        OnPropertyChanged(nameof(Yaw));
        OnPropertyChanged(nameof(Pitch));
        OnPropertyChanged(nameof(Roll));
        OnPropertyChanged(nameof(Depth));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class DelegateCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
            if (canExecute())
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
