using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JournalSettingsViewModel : INotifyPropertyChanged
{
    private readonly JournalSettingsStore settingsStore;
    private readonly AsyncCommand saveAndRestartCommand;
    private string directoryPath;
    private string statusMessage;

    public JournalSettingsViewModel(
        JournalSettingsStore settingsStore,
        string? commandLineOverride = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        IsCommandLineOverride = !string.IsNullOrWhiteSpace(commandLineOverride);
        directoryPath = (commandLineOverride
                ?? settingsStore.Load().Directory
                ?? string.Empty)
            .Trim();
        statusMessage = IsCommandLineOverride
            ? "The --journal-directory startup option controls this instance. "
                + "Remove that option to use the persisted folder."
            : GetPathStatus(directoryPath);
        saveAndRestartCommand = new AsyncCommand(
            SaveAndRestartAsync,
            () => !IsCommandLineOverride && Directory.Exists(DirectoryPath));
        SaveAndRestartCommand = saveAndRestartCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Func<Task>? RestartRequested;

    public bool IsCommandLineOverride { get; }

    public string DirectoryPath
    {
        get => directoryPath;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (directoryPath == normalized)
            {
                return;
            }

            directoryPath = normalized;
            StatusMessage = IsCommandLineOverride
                ? statusMessage
                : GetPathStatus(normalized);
            OnPropertyChanged();
            saveAndRestartCommand.RaiseCanExecuteChanged();
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

    public ICommand SaveAndRestartCommand { get; }

    public async Task SaveAndRestartAsync()
    {
        if (!saveAndRestartCommand.CanExecute(null))
        {
            StatusMessage = IsCommandLineOverride
                ? statusMessage
                : GetPathStatus(DirectoryPath);
            return;
        }

        try
        {
            settingsStore.Save(new JournalPreferences(DirectoryPath));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            StatusMessage = "The journal folder could not be saved: "
                + exception.Message;
            return;
        }

        var restartHandlers = RestartRequested;
        if (restartHandlers is null)
        {
            StatusMessage = "Journal folder saved. Restart SrvSurvey to use it.";
            return;
        }

        StatusMessage = "Journal folder saved; restarting SrvSurvey...";
        try
        {
            foreach (var handler in restartHandlers.GetInvocationList().Cast<Func<Task>>())
            {
                await handler();
            }
        }
        catch (Exception exception)
        {
            StatusMessage = "Journal folder saved, but automatic restart failed: "
                + exception.Message
                + " Close and reopen SrvSurvey manually.";
        }
    }

    private static string GetPathStatus(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "No persisted override is set; platform defaults and "
                + "SRVSURVEY_JOURNAL_DIR will be checked.";
        }

        return Directory.Exists(path)
            ? "This Elite Dangerous journal folder is available."
            : "This folder is unavailable on the current platform. Choose the "
                + "journal folder used by this Elite installation.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
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
