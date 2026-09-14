using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class WaylandCaptureSettingsViewModel : INotifyPropertyChanged
{
    private readonly string dataDirectory;
    private readonly Action<string>? log;
    private readonly AsyncCommand chooseCaptureSourceAgainCommand;
    private string statusMessage;
    private bool isBusy;

    public WaylandCaptureSettingsViewModel(string dataDirectory, bool isApplicable, Action<string>? log = null)
    {
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        IsApplicable = isApplicable;
        this.log = log;
        statusMessage = isApplicable
            ? "SrvSurvey will restart. If normal X11 capture fails again, the next capture attempt opens the picker."
            : "This session is not using Linux Wayland screen sharing, so no capture source needs to be reset.";
        chooseCaptureSourceAgainCommand = new AsyncCommand(
            ChooseCaptureSourceAgainAsync,
            () => IsApplicable && !isBusy
        );
        ChooseCaptureSourceAgainCommand = chooseCaptureSourceAgainCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Func<Task>? RestartRequested;

    public bool IsApplicable { get; }

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

        isBusy = true;
        chooseCaptureSourceAgainCommand.RaiseCanExecuteChanged();
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
            chooseCaptureSourceAgainCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
