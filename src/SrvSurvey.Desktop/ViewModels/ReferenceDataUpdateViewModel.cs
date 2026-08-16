using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ReferenceDataUpdateViewModel : INotifyPropertyChanged
{
    private const string CatalogStatusPrefix = "Catalogs updated this session: ";
    private const string BackupStatusPrefix = "Backup created this session: ";

    private readonly IPublishedReferenceUpdateService service;
    private readonly string dataDirectory;
    private readonly Action<string>? log;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand restartCommand;
    private Func<Task>? restartHandler;
    private string statusMessage;
    private string updatedCatalogs = CatalogStatusPrefix
        + "None yet; the automatic check is pending.";
    private string backupDirectory = BackupStatusPrefix
        + "Not needed unless catalogs are replaced.";
    private bool isRefreshing;
    private bool isRestartRequired;

    public ReferenceDataUpdateViewModel(
        IPublishedReferenceUpdateService service,
        string dataDirectory,
        string initialStatus,
        Action<string>? log = null)
    {
        this.service = service;
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.log = log;
        statusMessage = initialStatus;
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsRefreshing);
        restartCommand = new AsyncCommand(
            RestartAsync,
            () => IsRestartRequired && restartHandler is not null);
        RefreshCommand = refreshCommand;
        RestartCommand = restartCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand RestartCommand { get; }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string UpdatedCatalogs
    {
        get => updatedCatalogs;
        private set => SetField(ref updatedCatalogs, value);
    }

    public string BackupDirectory
    {
        get => backupDirectory;
        private set => SetField(ref backupDirectory, value);
    }

    public bool IsRefreshing
    {
        get => isRefreshing;
        private set
        {
            if (SetField(ref isRefreshing, value))
            {
                OnPropertyChanged(nameof(RefreshButtonText));
                refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRestartRequired
    {
        get => isRestartRequired;
        private set
        {
            if (SetField(ref isRestartRequired, value))
            {
                restartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RefreshButtonText => IsRefreshing
        ? "Refreshing..."
        : "Refresh reference data";

    public void SetRestartHandler(Func<Task>? handler)
    {
        restartHandler = handler;
        restartCommand.RaiseCanExecuteChanged();
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        StatusMessage = "Checking and validating published reference data...";
        try
        {
            var result = await service.RefreshAsync(dataDirectory);
            if (result.UpdatedCatalogs.Count == 0)
            {
                UpdatedCatalogs = CatalogStatusPrefix
                    + "None needed; already current.";
                BackupDirectory = BackupStatusPrefix
                    + "Not needed; no catalogs were replaced.";
                IsRestartRequired = false;
                StatusMessage = result.Warnings.Count == 0
                    ? "Published reference data is current."
                    : string.Join(" ", result.Warnings);
                return;
            }

            UpdatedCatalogs = CatalogStatusPrefix
                + string.Join(", ", result.UpdatedCatalogs);
            BackupDirectory = result.BackupDirectory
                is { } backup
                ? BackupStatusPrefix + backup
                : BackupStatusPrefix
                    + "Not needed; no prior downloaded catalogs were replaced.";
            IsRestartRequired = result.RestartRequired;
            StatusMessage = $"Activated {result.UpdatedCatalogs.Count:N0} verified "
                + "reference update(s). Restart SrvSurvey to use them.";
            if (result.Warnings.Count > 0)
            {
                StatusMessage += " " + string.Join(" ", result.Warnings);
            }

            log?.Invoke(StatusMessage + " " + BackupDirectory);
        }
        catch (Exception exception)
        {
            IsRestartRequired = false;
            UpdatedCatalogs = CatalogStatusPrefix
                + "None; refresh failed before activation.";
            BackupDirectory = BackupStatusPrefix
                + "Not needed; existing reference data remains active.";
            StatusMessage = "Reference refresh failed safely: "
                + exception.Message
                + " Player profile and survey files were not changed.";
            log?.Invoke(StatusMessage);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task RestartAsync()
    {
        if (restartHandler is not null && IsRestartRequired)
        {
            await restartHandler();
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
