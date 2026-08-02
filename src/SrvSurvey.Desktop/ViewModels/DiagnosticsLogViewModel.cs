using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class DiagnosticsLogViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ApplicationLogService? applicationLog;
    private readonly Action<Action> dispatch;
    private readonly AsyncCommand copyCommand;
    private readonly AsyncCommand openFolderCommand;
    private Func<string, Task>? clipboardWriter;
    private Func<DirectoryInfo, Task<bool>>? directoryLauncher;
    private string logText;
    private string sessionDescription;
    private string persistenceStatus;
    private string statusMessage = string.Empty;
    private bool disposed;

    public DiagnosticsLogViewModel(
        ApplicationLogService? applicationLog,
        Action<Action>? dispatch = null)
    {
        this.applicationLog = applicationLog;
        this.dispatch = dispatch ?? DispatchToUiThread;
        logText = GetLogText();
        sessionDescription = GetSessionDescription();
        persistenceStatus = GetPersistenceStatus();
        copyCommand = new AsyncCommand(CopyAsync, CanCopy);
        openFolderCommand = new AsyncCommand(OpenFolderAsync, CanOpenFolder);
        CopyCommand = copyCommand;
        OpenFolderCommand = openFolderCommand;
        ClearCommand = new DelegateCommand(Clear, () => applicationLog is not null);
        if (applicationLog is not null)
        {
            applicationLog.Changed += OnLogChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CopyCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public string LogText
    {
        get => logText;
        private set => SetField(ref logText, value);
    }

    public string SessionDescription
    {
        get => sessionDescription;
        private set => SetField(ref sessionDescription, value);
    }

    public string PersistenceStatus
    {
        get => persistenceStatus;
        private set => SetField(ref persistenceStatus, value);
    }

    public string LogDirectory => applicationLog?.LogDirectory
        ?? "Application logging is unavailable.";

    public string CurrentLogPath => applicationLog?.CurrentLogPath
        ?? "No session log file is available.";

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetField(ref statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public void SetPlatformServices(
        Func<string, Task>? writeClipboard,
        Func<DirectoryInfo, Task<bool>>? launchDirectory)
    {
        clipboardWriter = writeClipboard;
        directoryLauncher = launchDirectory;
        copyCommand.RaiseCanExecuteChanged();
        openFolderCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (applicationLog is not null)
        {
            applicationLog.Changed -= OnLogChanged;
        }

        SetPlatformServices(null, null);
    }

    private bool CanCopy()
    {
        return applicationLog is not null && clipboardWriter is not null;
    }

    public async Task CopyAsync()
    {
        if (applicationLog is null || clipboardWriter is null)
        {
            StatusMessage = "The desktop clipboard is unavailable.";
            return;
        }

        try
        {
            await clipboardWriter(applicationLog.Text);
            applicationLog.Append("Logs copied");
            StatusMessage = "The current session log was copied to the clipboard.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or NotSupportedException
                or System.Runtime.InteropServices.ExternalException
                or UnauthorizedAccessException)
        {
            StatusMessage = "The session log could not be copied: "
                + exception.Message;
        }
    }

    public void Clear()
    {
        if (applicationLog is null)
        {
            StatusMessage = "Application logging is unavailable.";
            return;
        }

        applicationLog.Clear();
        StatusMessage = "The on-screen log was reset. Earlier entries remain in the session file.";
    }

    private bool CanOpenFolder()
    {
        return applicationLog is not null && directoryLauncher is not null;
    }

    public async Task OpenFolderAsync()
    {
        if (applicationLog is null || directoryLauncher is null)
        {
            StatusMessage = "The log folder cannot be opened on this platform.";
            return;
        }

        try
        {
            var launched = await directoryLauncher(
                new DirectoryInfo(applicationLog.LogDirectory));
            StatusMessage = launched
                ? "Opened the application log folder."
                : "The log folder could not be opened.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or NotSupportedException
                or System.Runtime.InteropServices.ExternalException
                or UnauthorizedAccessException)
        {
            StatusMessage = "The log folder could not be opened: "
                + exception.Message;
        }
    }

    private void OnLogChanged(object? sender, EventArgs eventArgs)
    {
        dispatch(RefreshSnapshot);
    }

    private void RefreshSnapshot()
    {
        LogText = GetLogText();
        SessionDescription = GetSessionDescription();
        PersistenceStatus = GetPersistenceStatus();
    }

    private string GetLogText()
    {
        var text = applicationLog?.Text;
        return string.IsNullOrEmpty(text)
            ? "No log entries have been recorded for this session."
            : text;
    }

    private string GetSessionDescription()
    {
        var count = applicationLog?.Entries.Count ?? 0;
        return applicationLog is null
            ? "Application logging is unavailable."
            : $"{count:N0} session {(count == 1 ? "entry" : "entries")}";
    }

    private string GetPersistenceStatus()
    {
        if (applicationLog is null)
        {
            return "No session log file is available.";
        }

        return string.IsNullOrWhiteSpace(applicationLog.LastWriteError)
            ? "The session is being saved to disk. The newest ten log files are retained."
            : "Entries remain visible in this session, but the log file could not be updated: "
                + applicationLog.LastWriteError;
    }

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
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

    private sealed class DelegateCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();

    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !isExecuting && canExecute();
        }

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
