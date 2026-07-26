using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ReleaseUpdateViewModel : INotifyPropertyChanged
{
    private readonly IReleaseUpdateService service;
    private readonly Version currentVersion;
    private readonly AsyncCommand checkCommand;
    private readonly AsyncCommand openReleaseCommand;
    private Func<Uri, Task<bool>>? uriLauncher;
    private Uri? releaseUri;
    private string latestVersion = "Not checked";
    private string statusMessage = "Update status has not been checked.";
    private bool isChecking;
    private bool isUpdateAvailable;

    public ReleaseUpdateViewModel(
        IReleaseUpdateService service,
        Version currentVersion)
    {
        this.service = service;
        this.currentVersion = currentVersion;
        checkCommand = new AsyncCommand(CheckAsync, () => !IsChecking);
        openReleaseCommand = new AsyncCommand(
            OpenReleaseAsync,
            () => IsUpdateAvailable
                && releaseUri is not null
                && uriLauncher is not null);
        CheckCommand = checkCommand;
        OpenReleaseCommand = openReleaseCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CheckCommand { get; }

    public ICommand OpenReleaseCommand { get; }

    public string CurrentVersion => FormatVersion(currentVersion);

    public string LatestVersion
    {
        get => latestVersion;
        private set => SetField(ref latestVersion, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool IsChecking
    {
        get => isChecking;
        private set
        {
            if (SetField(ref isChecking, value))
            {
                OnPropertyChanged(nameof(CheckButtonText));
                checkCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsUpdateAvailable
    {
        get => isUpdateAvailable;
        private set
        {
            if (SetField(ref isUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(IsCurrent));
                openReleaseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCurrent => !IsUpdateAvailable;

    public string CheckButtonText => IsChecking
        ? "Checking..."
        : "Check for updates";

    public void SetUriLauncher(Func<Uri, Task<bool>>? launchUri)
    {
        uriLauncher = launchUri;
        openReleaseCommand.RaiseCanExecuteChanged();
    }

    public async Task CheckAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var result = await service.CheckAsync(currentVersion);
            releaseUri = result.ReleaseUri;
            LatestVersion = FormatVersion(result.LatestVersion);
            IsUpdateAvailable = result.IsUpdateAvailable;
            StatusMessage = result.IsUpdateAvailable
                ? $"SrvSurvey {LatestVersion} is available. This installation was not changed."
                : $"SrvSurvey {CurrentVersion} is current with the published release index.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            releaseUri = null;
            LatestVersion = "Unavailable";
            IsUpdateAvailable = false;
            StatusMessage = "The update check was unavailable: "
                + exception.Message
                + " The installation and profile were not changed.";
        }
        finally
        {
            IsChecking = false;
            openReleaseCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task OpenReleaseAsync()
    {
        if (releaseUri is null || uriLauncher is null)
        {
            return;
        }

        if (!await uriLauncher(releaseUri))
        {
            StatusMessage = "The releases page could not be opened. No files were changed.";
        }
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is HttpRequestException
            or IOException
            or InvalidDataException
            or JsonException
            or TaskCanceledException
            or InvalidOperationException
            or PlatformNotSupportedException;
    }

    private static string FormatVersion(Version version)
    {
        return version.Revision > 0
            ? version.ToString(4)
            : version.Build >= 0
                ? version.ToString(3)
                : version.ToString(2);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !isExecuting && canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
