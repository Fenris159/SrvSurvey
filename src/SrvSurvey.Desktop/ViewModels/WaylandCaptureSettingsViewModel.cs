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
        isEnabled = this.settingsStore.Load().Enabled;
        GameScreenCapture.WaylandPortalEnabled = () => isEnabled;
        statusMessage = DescribeStatus();
        chooseCaptureSourceAgainCommand = new WorkspaceCommand(
            () => _ = ChooseCaptureSourceAgainAsync(),
            () => IsApplicable && isEnabled && !isBusy
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

            isEnabled = value;
            settingsStore.Save(new WaylandCapturePreferences(value));
            GameScreenCapture.WaylandPortalEnabled = () => isEnabled;
            StatusMessage = DescribeStatus();
            log?.Invoke(
                value
                    ? "Wayland screen capture enabled in Settings."
                    : "Wayland screen capture disabled in Settings; FSS, first-footfall, and rig detection will not use the portal."
            );
            OnPropertyChanged();
            chooseCaptureSourceAgainCommand.Refresh();
        }
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
            ? "SrvSurvey will restart. If normal X11 capture fails again, the next capture attempt opens the picker."
            : "Wayland screen capture is off. FSS tuning, first-footfall inference, and Surface Mining rig detection will not open the desktop share picker.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
