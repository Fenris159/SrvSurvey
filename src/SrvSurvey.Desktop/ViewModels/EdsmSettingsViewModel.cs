using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Edsm;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class EdsmSettingsViewModel : INotifyPropertyChanged
{
    private readonly Func<
        string,
        string?,
        bool,
        string?,
        string?,
        CancellationToken,
        Task> saveCredentialsAsync;
    private readonly AsyncCommand saveCredentialsCommand;
    private readonly AsyncCommand confirmClearCredentialsCommand;
    private readonly DelegateCommand requestClearCredentialsCommand;
    private readonly DelegateCommand cancelClearCredentialsCommand;
    private string edsmCommanderName = string.Empty;
    private string apiKey = string.Empty;
    private string? storedEdsmCommanderName;
    private string? storedApiKey;
    private string? profileFrontierId;
    private string? activeCommanderName;
    private bool profileIsOdyssey = true;
    private int profileGeneration;
    private bool isClearCredentialsConfirmationVisible;
    private string credentialStatus =
        "Load a commander profile to configure EDSM synchronization.";
    private string publicationStatus = string.Empty;

    public EdsmSettingsViewModel(CommanderProfileStore commanderProfileStore)
        : this(commanderProfileStore, null)
    {
    }

    internal EdsmSettingsViewModel(
        CommanderProfileStore commanderProfileStore,
        Func<
            string,
            string?,
            bool,
            string?,
            string?,
            CancellationToken,
            Task>? saveCredentialsAsync)
    {
        ArgumentNullException.ThrowIfNull(commanderProfileStore);
        this.saveCredentialsAsync = saveCredentialsAsync
            ?? commanderProfileStore.SaveEdsmCredentialsAsync;
        saveCredentialsCommand = new AsyncCommand(
            SaveCredentialsAsync,
            CanSaveCredentials);
        confirmClearCredentialsCommand = new AsyncCommand(
            ClearCredentialsAsync,
            () => HasStoredCredentials
                && IsClearCredentialsConfirmationVisible);
        requestClearCredentialsCommand = new DelegateCommand(
            RequestClearCredentials,
            () => HasStoredCredentials
                && !IsClearCredentialsConfirmationVisible);
        cancelClearCredentialsCommand = new DelegateCommand(
            CancelClearCredentials,
            () => IsClearCredentialsConfirmationVisible);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CredentialsChanged;

    public ICommand SaveCredentialsCommand => saveCredentialsCommand;

    public ICommand RequestClearCredentialsCommand =>
        requestClearCredentialsCommand;

    public ICommand ConfirmClearCredentialsCommand =>
        confirmClearCredentialsCommand;

    public ICommand CancelClearCredentialsCommand =>
        cancelClearCredentialsCommand;

    public string EdsmCommanderName
    {
        get => edsmCommanderName;
        set
        {
            if (SetField(ref edsmCommanderName, value ?? string.Empty))
            {
                saveCredentialsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ApiKey
    {
        get => apiKey;
        set
        {
            if (SetField(ref apiKey, value ?? string.Empty))
            {
                saveCredentialsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasCommanderProfile => profileFrontierId is not null;

    public bool HasStoredCredentials =>
        storedEdsmCommanderName is not null && storedApiKey is not null;

    public string ActiveCommanderDisplayName =>
        activeCommanderName ?? "No commander loaded";

    public bool IsClearCredentialsConfirmationVisible
    {
        get => isClearCredentialsConfirmationVisible;
        private set
        {
            if (!SetField(ref isClearCredentialsConfirmationVisible, value))
            {
                return;
            }

            confirmClearCredentialsCommand.RaiseCanExecuteChanged();
            requestClearCredentialsCommand.RaiseCanExecuteChanged();
            cancelClearCredentialsCommand.RaiseCanExecuteChanged();
        }
    }

    public string CredentialStatus
    {
        get => credentialStatus;
        private set => SetField(ref credentialStatus, value);
    }

    public string PublicationStatus
    {
        get => publicationStatus;
        private set
        {
            if (SetField(ref publicationStatus, value))
            {
                OnPropertyChanged(nameof(HasPublicationStatus));
            }
        }
    }

    public bool HasPublicationStatus =>
        !string.IsNullOrWhiteSpace(PublicationStatus);

    internal string? StoredEdsmCommanderName => storedEdsmCommanderName;

    internal string? StoredApiKey => storedApiKey;

    public void SetCommanderProfile(
        string? frontierId,
        string? commanderName,
        bool isOdyssey,
        string? savedEdsmCommanderName,
        string? savedApiKey)
    {
        profileGeneration++;
        profileFrontierId = Normalize(frontierId);
        activeCommanderName = Normalize(commanderName);
        profileIsOdyssey = isOdyssey;
        storedEdsmCommanderName = Normalize(savedEdsmCommanderName);
        storedApiKey = Normalize(savedApiKey);
        edsmCommanderName = storedEdsmCommanderName
            ?? activeCommanderName
            ?? string.Empty;
        apiKey = storedApiKey ?? string.Empty;
        IsClearCredentialsConfirmationVisible = false;
        PublicationStatus = string.Empty;

        OnPropertyChanged(nameof(EdsmCommanderName));
        OnPropertyChanged(nameof(ApiKey));
        OnPropertyChanged(nameof(HasCommanderProfile));
        OnPropertyChanged(nameof(HasStoredCredentials));
        OnPropertyChanged(nameof(ActiveCommanderDisplayName));
        CredentialStatus = profileFrontierId is null
            ? "Load a commander profile to configure EDSM synchronization."
            : HasStoredCredentials
                ? $"EDSM synchronization is enabled for {ActiveCommanderDisplayName}."
                : $"No EDSM credentials are saved for {ActiveCommanderDisplayName}.";
        RaiseCommandStates();
    }

    public void ReportPublicationResult(EdsmPublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Warnings.Count > 0)
        {
            PublicationStatus = string.Join(Environment.NewLine, result.Warnings);
        }
        else if (result.AcceptedEventCount > 0)
        {
            PublicationStatus =
                $"EDSM accepted {result.AcceptedEventCount:N0} journal event(s).";
        }
        else if (result.QueuedEventCount > 0)
        {
            PublicationStatus =
                $"Queued {result.QueuedEventCount:N0} EDSM journal event(s); "
                + $"{result.PendingEventCount:N0} waiting for the next batch.";
        }
    }

    public void ReportPublicationFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        PublicationStatus =
            "EDSM processing was skipped without affecting journal tracking: "
            + exception.Message;
    }

    private async Task SaveCredentialsAsync()
    {
        await PersistCredentialsAsync(
            Normalize(EdsmCommanderName),
            Normalize(ApiKey));
    }

    private async Task ClearCredentialsAsync()
    {
        await PersistCredentialsAsync(null, null);
    }

    private async Task PersistCredentialsAsync(
        string? commanderName,
        string? key)
    {
        if (profileFrontierId is null)
        {
            return;
        }

        var saveGeneration = profileGeneration;
        var saveFrontierId = profileFrontierId;
        var saveActiveCommanderName = activeCommanderName;
        var saveIsOdyssey = profileIsOdyssey;
        try
        {
            await saveCredentialsAsync(
                saveFrontierId,
                saveActiveCommanderName,
                saveIsOdyssey,
                commanderName,
                key,
                CancellationToken.None);
            if (saveGeneration != profileGeneration)
            {
                return;
            }

            storedEdsmCommanderName = commanderName;
            storedApiKey = key;
            EdsmCommanderName = commanderName
                ?? activeCommanderName
                ?? string.Empty;
            ApiKey = key ?? string.Empty;
            IsClearCredentialsConfirmationVisible = false;
            OnPropertyChanged(nameof(HasStoredCredentials));
            CredentialStatus = commanderName is null || key is null
                ? $"EDSM synchronization was disabled for {ActiveCommanderDisplayName}."
                : $"EDSM synchronization was enabled for {ActiveCommanderDisplayName}.";
            CredentialsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            if (saveGeneration == profileGeneration)
            {
                CredentialStatus =
                    "The EDSM credentials were not saved: " + exception.Message;
            }
        }
        finally
        {
            RaiseCommandStates();
        }
    }

    private bool CanSaveCredentials()
    {
        var commanderName = Normalize(EdsmCommanderName);
        var key = Normalize(ApiKey);
        return profileFrontierId is not null
            && commanderName is not null
            && key is not null
            && (!string.Equals(
                    commanderName,
                    storedEdsmCommanderName,
                    StringComparison.Ordinal)
                || !string.Equals(
                    key,
                    storedApiKey,
                    StringComparison.Ordinal));
    }

    private void RequestClearCredentials()
    {
        IsClearCredentialsConfirmationVisible = true;
    }

    private void CancelClearCredentials()
    {
        IsClearCredentialsConfirmationVisible = false;
    }

    private void RaiseCommandStates()
    {
        saveCredentialsCommand.RaiseCanExecuteChanged();
        confirmClearCredentialsCommand.RaiseCanExecuteChanged();
        requestClearCredentialsCommand.RaiseCanExecuteChanged();
        cancelClearCredentialsCommand.RaiseCanExecuteChanged();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) =>
            !isExecuting && canExecute();

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

        internal void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class DelegateCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
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

        internal void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
