using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "—";

    private readonly JournalFolderResolution folderResolution;
    private bool isBusy;
    private string statusMessage;
    private string commanderName = Unavailable;
    private string frontierId = Unavailable;
    private string gameDescription = Unavailable;
    private string gameMode = Unavailable;
    private string systemDescription = Unavailable;
    private string bodyName = Unavailable;
    private string sessionState = "Not loaded";
    private string lastUpdated = string.Empty;

    public MainWindowViewModel(string? configuredJournalDirectory)
    {
        folderResolution = JournalFolderLocator.ResolveCurrent(configuredJournalDirectory);
        JournalFolderPath = folderResolution.SelectedPath
            ?? folderResolution.CandidatePaths.FirstOrDefault()
            ?? "No journal location is configured.";
        CandidatePaths = folderResolution.CandidatePaths.Count == 0
            ? "No default locations are available for this platform."
            : string.Join(Environment.NewLine, folderResolution.CandidatePaths);
        statusMessage = folderResolution.IsFound
            ? "Ready to read the newest Journal.*.log file."
            : $"Journal folder not found. Set {JournalFolderLocator.EnvironmentVariableName} "
                + "or start with --journal-directory <path>.";
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string JournalFolderPath { get; }

    public string CandidatePaths { get; }

    public ICommand RefreshCommand { get; }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                ((AsyncCommand)RefreshCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string CommanderName
    {
        get => commanderName;
        private set => SetField(ref commanderName, value);
    }

    public string FrontierId
    {
        get => frontierId;
        private set => SetField(ref frontierId, value);
    }

    public string GameDescription
    {
        get => gameDescription;
        private set => SetField(ref gameDescription, value);
    }

    public string GameMode
    {
        get => gameMode;
        private set => SetField(ref gameMode, value);
    }

    public string SystemDescription
    {
        get => systemDescription;
        private set => SetField(ref systemDescription, value);
    }

    public string BodyName
    {
        get => bodyName;
        private set => SetField(ref bodyName, value);
    }

    public string SessionState
    {
        get => sessionState;
        private set => SetField(ref sessionState, value);
    }

    public string LastUpdated
    {
        get => lastUpdated;
        private set => SetField(ref lastUpdated, value);
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (folderResolution.SelectedPath is null)
        {
            StatusMessage = $"Journal folder not found. Set "
                + $"{JournalFolderLocator.EnvironmentVariableName} or use "
                + "--journal-directory <path>.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Reading the newest journal…";

            var snapshot = await JournalSnapshotReader.ReadLatestAsync(
                folderResolution.SelectedPath);
            ApplySnapshot(snapshot);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            LastUpdated = $"Last refresh: {DateTimeOffset.Now:G}";
        }
    }

    private void ApplySnapshot(JournalSnapshot snapshot)
    {
        CommanderName = Display(snapshot.CommanderName);
        FrontierId = Display(snapshot.FrontierId);
        GameDescription = string.Join(
            " ",
            new[]
            {
                snapshot.GameVersion,
                snapshot.GameBuild is null ? null : $"({snapshot.GameBuild})",
                snapshot.IsOdyssey switch
                {
                    true => "Odyssey",
                    false => "Horizons",
                    null => null,
                },
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(GameDescription))
        {
            GameDescription = Unavailable;
        }

        GameMode = Display(snapshot.GameMode);
        SystemDescription = snapshot.SystemAddress is null
            ? Display(snapshot.SystemName)
            : $"{Display(snapshot.SystemName)} ({snapshot.SystemAddress})";
        BodyName = Display(snapshot.BodyName);
        SessionState = snapshot.IsShutdown ? "Shut down" : "Active or unclosed";

        var malformedSuffix = snapshot.MalformedLineCount == 0
            ? string.Empty
            : $"; ignored {snapshot.MalformedLineCount} malformed/partial line(s)";
        StatusMessage = $"Loaded {snapshot.ValidLineCount} events from "
            + $"{Path.GetFileName(snapshot.SourcePath)}; "
            + $"{snapshot.RecognizedEventCount} bootstrap events recognized"
            + malformedSuffix
            + ".";
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unavailable : value;
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

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
