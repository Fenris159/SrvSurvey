using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class InaraSettingsViewModel : INotifyPropertyChanged
{
    private readonly Func<string, string?, bool, string?, CancellationToken, Task>
        saveInaraApiKeyAsync;
    private readonly AsyncCommand saveApiKeyCommand;
    private readonly AsyncCommand confirmClearApiKeyCommand;
    private readonly DelegateCommand requestClearApiKeyCommand;
    private readonly DelegateCommand cancelClearApiKeyCommand;
    private string apiKey = string.Empty;
    private string? storedApiKey;
    private string? profileFrontierId;
    private string? commanderName;
    private bool profileIsOdyssey = true;
    private int profileGeneration;
    private bool isClearKeyConfirmationVisible;
    private string credentialStatus =
        "Load a commander profile to configure an Inara API key.";
    private string publicationStatus = string.Empty;

    public InaraSettingsViewModel(
        CommanderProfileStore commanderProfileStore)
        : this(commanderProfileStore, null)
    {
    }

    internal InaraSettingsViewModel(
        CommanderProfileStore commanderProfileStore,
        Func<string, string?, bool, string?, CancellationToken, Task>?
            saveInaraApiKeyAsync)
    {
        ArgumentNullException.ThrowIfNull(commanderProfileStore);
        this.saveInaraApiKeyAsync = saveInaraApiKeyAsync
            ?? commanderProfileStore.SaveInaraApiKeyAsync;
        saveApiKeyCommand = new AsyncCommand(
            () => SaveApiKeyAsync(ApiKey.Trim()),
            CanSaveApiKey);
        confirmClearApiKeyCommand = new AsyncCommand(
            () => SaveApiKeyAsync(apiKey: null),
            () => HasStoredApiKey && IsClearKeyConfirmationVisible);
        requestClearApiKeyCommand = new DelegateCommand(
            RequestClearApiKey,
            () => HasStoredApiKey && !IsClearKeyConfirmationVisible);
        cancelClearApiKeyCommand = new DelegateCommand(
            CancelClearApiKey,
            () => IsClearKeyConfirmationVisible);
        SaveApiKeyCommand = saveApiKeyCommand;
        ConfirmClearApiKeyCommand = confirmClearApiKeyCommand;
        RequestClearApiKeyCommand = requestClearApiKeyCommand;
        CancelClearApiKeyCommand = cancelClearApiKeyCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ApiKeyChanged;

    public ICommand SaveApiKeyCommand { get; }

    public ICommand RequestClearApiKeyCommand { get; }

    public ICommand ConfirmClearApiKeyCommand { get; }

    public ICommand CancelClearApiKeyCommand { get; }

    public string ApiKey
    {
        get => apiKey;
        set
        {
            if (!SetField(ref apiKey, value ?? string.Empty))
            {
                return;
            }

            saveApiKeyCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasCommanderProfile => profileFrontierId is not null;

    public bool HasStoredApiKey => storedApiKey is not null;

    public bool IsClearKeyConfirmationVisible
    {
        get => isClearKeyConfirmationVisible;
        private set
        {
            if (!SetField(ref isClearKeyConfirmationVisible, value))
            {
                return;
            }

            confirmClearApiKeyCommand.RaiseCanExecuteChanged();
            requestClearApiKeyCommand.RaiseCanExecuteChanged();
            cancelClearApiKeyCommand.RaiseCanExecuteChanged();
        }
    }

    public string CommanderDisplayName => commanderName ?? "No commander loaded";

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
            if (!SetField(ref publicationStatus, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasPublicationStatus));
        }
    }

    public bool HasPublicationStatus =>
        !string.IsNullOrWhiteSpace(PublicationStatus);

    internal string? StoredApiKey => storedApiKey;

    public void SetCommanderProfile(
        string? frontierId,
        string? activeCommanderName,
        bool isOdyssey,
        string? inaraApiKey)
    {
        profileGeneration++;
        profileFrontierId = string.IsNullOrWhiteSpace(frontierId)
            ? null
            : frontierId.Trim();
        commanderName = string.IsNullOrWhiteSpace(activeCommanderName)
            ? null
            : activeCommanderName.Trim();
        profileIsOdyssey = isOdyssey;
        storedApiKey = string.IsNullOrWhiteSpace(inaraApiKey)
            ? null
            : inaraApiKey.Trim();
        apiKey = storedApiKey ?? string.Empty;
        IsClearKeyConfirmationVisible = false;

        OnPropertyChanged(nameof(ApiKey));
        OnPropertyChanged(nameof(HasCommanderProfile));
        OnPropertyChanged(nameof(HasStoredApiKey));
        OnPropertyChanged(nameof(CommanderDisplayName));
        var noCommanderText = "Load a commander profile to configure an Inara API key.";
        if (profileFrontierId is null)
        {
            CredentialStatus = noCommanderText;
        }
        else
        {
            CredentialStatus = storedApiKey is null
                ? $"No Inara API key is saved for {CommanderDisplayName}."
                : $"An Inara API key is saved for {CommanderDisplayName}.";
        }
        saveApiKeyCommand.RaiseCanExecuteChanged();
        confirmClearApiKeyCommand.RaiseCanExecuteChanged();
        requestClearApiKeyCommand.RaiseCanExecuteChanged();
    }

    public void ReportPublicationResult(InaraPublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Warnings.Count > 0)
        {
            PublicationStatus = string.Join(
                Environment.NewLine,
                result.Warnings);
        }
        else if (result.AcceptedEventCount > 0)
        {
            PublicationStatus =
                $"Inara accepted {result.AcceptedEventCount:N0} event(s).";
        }
        else if (result.QueuedEventCount > 0)
        {
            PublicationStatus =
                $"Queued {result.QueuedEventCount:N0} Inara event(s); "
                + $"{result.PendingEventCount:N0} waiting for the next batch.";
        }
    }

    public void ReportPublicationFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        PublicationStatus =
            "Inara processing was skipped without affecting journal tracking: "
            + exception.Message;
    }

    private void RequestClearApiKey()
    {
        IsClearKeyConfirmationVisible = true;
    }

    private void CancelClearApiKey()
    {
        IsClearKeyConfirmationVisible = false;
    }

    private async Task SaveApiKeyAsync(string? apiKey)
    {
        if (profileFrontierId is null)
        {
            return;
        }

        var normalized = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : apiKey.Trim();
        var saveGeneration = profileGeneration;
        var saveFrontierId = profileFrontierId;
        var saveCommanderName = commanderName;
        var saveIsOdyssey = profileIsOdyssey;
        try
        {
            await saveInaraApiKeyAsync(
                saveFrontierId,
                saveCommanderName,
                saveIsOdyssey,
                normalized,
                CancellationToken.None);
            if (saveGeneration != profileGeneration)
            {
                return;
            }

            storedApiKey = normalized;
            ApiKey = normalized ?? string.Empty;
            IsClearKeyConfirmationVisible = false;
            OnPropertyChanged(nameof(HasStoredApiKey));
            confirmClearApiKeyCommand.RaiseCanExecuteChanged();
            requestClearApiKeyCommand.RaiseCanExecuteChanged();
            CredentialStatus = normalized is null
                ? $"The Inara API key was removed from {CommanderDisplayName}."
                : $"The Inara API key was saved for {CommanderDisplayName}.";
            ApiKeyChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            if (saveGeneration == profileGeneration)
            {
                CredentialStatus =
                    "The Inara API key was not saved: " + exception.Message;
            }
        }
        finally
        {
            saveApiKeyCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanSaveApiKey()
    {
        var normalized = string.IsNullOrWhiteSpace(ApiKey)
            ? null
            : ApiKey.Trim();
        return profileFrontierId is not null
            && normalized is not null
            && !string.Equals(
                normalized,
                storedApiKey,
                StringComparison.Ordinal);
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

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
