using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JournalSettingsViewModel : INotifyPropertyChanged
{
    private readonly JournalSettingsStore settingsStore;
    private readonly AsyncCommand saveAndRestartCommand;
    private readonly AsyncCommand restartCommand;
    private readonly RelayCommand addOrUpdateCommand;
    private string directoryPath;
    private string statusMessage;
    private string? editingPath;
    private bool hasPendingRestart;

    public JournalSettingsViewModel(JournalSettingsStore settingsStore, string? commandLineOverride = null)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        IsCommandLineOverride = !string.IsNullOrWhiteSpace(commandLineOverride);
        SavedFolders = new ObservableCollection<string>(settingsStore.Load().Directories);
        directoryPath = IsCommandLineOverride ? commandLineOverride!.Trim() : string.Empty;
        statusMessage = IsCommandLineOverride
            ? "The --journal-directory startup option controls live journal data for this instance. Saved folders can still populate commander choices."
            : GetPathStatus(directoryPath);
        addOrUpdateCommand = new RelayCommand(AddOrUpdateFolder, () => Directory.Exists(DirectoryPath));
        restartCommand = new AsyncCommand(RestartAsync, () => !IsCommandLineOverride && hasPendingRestart);
        saveAndRestartCommand = new AsyncCommand(
            SaveAndRestartAsync,
            () => !IsCommandLineOverride && Directory.Exists(DirectoryPath)
        );
        AddOrUpdateCommand = addOrUpdateCommand;
        RestartCommand = restartCommand;
        SaveAndRestartCommand = saveAndRestartCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Func<Task>? RestartRequested;

    public bool IsCommandLineOverride { get; }

    public ObservableCollection<string> SavedFolders { get; }

    public string AddButtonLabel => editingPath is null ? "Add folder" : "Save change";

    public string DirectoryPath
    {
        get => directoryPath;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (directoryPath == normalized)
            {
                return;
            }

            directoryPath = normalized;
            StatusMessage = IsCommandLineOverride ? statusMessage : GetPathStatus(normalized);
            OnPropertyChanged();
            addOrUpdateCommand.RaiseCanExecuteChanged();
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

    public ICommand AddOrUpdateCommand { get; }

    public ICommand RestartCommand { get; }

    // Retained for callers that use the old single-folder save flow.
    public ICommand SaveAndRestartCommand { get; }

    public void EditFolder(string path)
    {
        if (!SavedFolders.Contains(path))
        {
            return;
        }

        editingPath = path;
        DirectoryPath = path;
        OnPropertyChanged(nameof(AddButtonLabel));
    }

    public void RemoveFolder(string path)
    {
        string[] updated = SavedFolders.Where(folder => folder != path).ToArray();
        if (updated.Length == SavedFolders.Count || !SaveFolders(updated))
        {
            return;
        }

        if (editingPath == path)
        {
            editingPath = null;
            DirectoryPath = string.Empty;
            OnPropertyChanged(nameof(AddButtonLabel));
        }
    }

    public void ClearSelection()
    {
        DirectoryPath = string.Empty;
    }

    public async Task SaveAndRestartAsync()
    {
        if (!saveAndRestartCommand.CanExecute(null))
        {
            StatusMessage = IsCommandLineOverride ? statusMessage : GetPathStatus(DirectoryPath);
            return;
        }

        AddOrUpdateFolder();
        if (hasPendingRestart)
        {
            await RestartAsync();
        }
    }

    private void AddOrUpdateFolder()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            StatusMessage = GetPathStatus(DirectoryPath);
            return;
        }

        var updated = SavedFolders.ToList();
        if (editingPath is not null)
        {
            updated.Remove(editingPath);
        }

        if (
            !updated.Contains(
                DirectoryPath,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
            )
        )
        {
            updated.Add(DirectoryPath);
        }

        if (!SaveFolders(updated))
        {
            return;
        }

        editingPath = null;
        DirectoryPath = string.Empty;
        OnPropertyChanged(nameof(AddButtonLabel));
    }

    private bool SaveFolders(IReadOnlyList<string> folders)
    {
        try
        {
            settingsStore.Save(
                new JournalPreferences(folders.Count > 0 ? folders[0] : null, folders.Skip(1).ToArray())
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "The journal folders could not be saved: " + exception.Message;
            return false;
        }

        SavedFolders.Clear();
        foreach (string folder in folders)
        {
            SavedFolders.Add(folder);
        }

        hasPendingRestart = true;
        restartCommand.RaiseCanExecuteChanged();
        StatusMessage = IsCommandLineOverride
            ? "Folders saved. Refresh the commander lists to find profiles in them. Live journal data remains pinned to --journal-directory."
            : "Folders saved. Restart SrvSurvey to scan them.";
        return true;
    }

    private async Task RestartAsync()
    {
        if (!restartCommand.CanExecute(null))
        {
            return;
        }

        Func<Task>? restartHandlers = RestartRequested;
        if (restartHandlers is null)
        {
            StatusMessage = "Journal folders saved. Restart SrvSurvey to use them.";
            return;
        }

        StatusMessage = "Journal folders saved; restarting SrvSurvey...";
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
                "Journal folders saved, but automatic restart failed: "
                + exception.Message
                + " Close and reopen SrvSurvey manually.";
        }
    }

    private static string GetPathStatus(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Choose a journal folder, then add it to the saved list. Automatic discovery is also used.";
        }

        return Directory.Exists(path)
            ? "This Elite Dangerous journal folder is available. Add it to the list below."
            : "This folder is unavailable on the current platform. Choose the journal folder used by this Elite installation.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
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

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
