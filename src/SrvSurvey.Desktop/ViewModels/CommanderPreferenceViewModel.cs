using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class CommanderPreferenceViewModel : INotifyPropertyChanged
{
    private readonly CommanderPreferenceSettingsStore settingsStore;
    private readonly CommanderProfileCatalog profileCatalog;
    private readonly AsyncCommand saveAndRestartCommand;
    private IReadOnlyList<CommanderPreferenceOptionViewModel> options;
    private CommanderPreferenceOptionViewModel? selectedOption;
    private string statusMessage;
    private bool isBusy;

    public CommanderPreferenceViewModel(
        CommanderPreferenceSettingsStore settingsStore,
        CommanderProfileCatalog profileCatalog,
        bool isCommandLineOverride = false,
        string? initialStatusMessage = null)
    {
        this.settingsStore = settingsStore;
        this.profileCatalog = profileCatalog;
        IsCommandLineOverride = isCommandLineOverride;
        var preference = settingsStore.Load();
        var automatic = CommanderPreferenceOptionViewModel.Automatic;
        var stored = CreateStoredOption(preference);
        options = stored is null ? [automatic] : [automatic, stored];
        selectedOption = stored ?? automatic;
        statusMessage = initialStatusMessage
            ?? (isCommandLineOverride
                ? "A command-line Frontier ID controls this instance. Remove it to use a saved preference."
                : GetPreferenceStatus(preference));
        saveAndRestartCommand = new AsyncCommand(
            SaveAndRestartAsync,
            CanSaveAndRestart);
        SaveAndRestartCommand = saveAndRestartCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Func<Task>? RestartRequested;

    public IReadOnlyList<CommanderPreferenceOptionViewModel> Options
    {
        get => options;
        private set => SetField(ref options, value);
    }

    public CommanderPreferenceOptionViewModel? SelectedOption
    {
        get => selectedOption;
        set
        {
            if (SetField(ref selectedOption, value))
            {
                saveAndRestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCommandLineOverride { get; }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                saveAndRestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public ICommand SaveAndRestartCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var preference = settingsStore.Load();
            var catalog = await profileCatalog.LoadAsync();
            var profileOptions = catalog.Profiles
                .Select(profile => new CommanderPreferenceOptionViewModel(
                    profile.CommanderName,
                    profile.FrontierId,
                    $"{profile.CommanderName} ({profile.FrontierId})",
                    false))
                .ToList();
            Options = [CommanderPreferenceOptionViewModel.Automatic, .. profileOptions];
            SelectedOption = ResolveSelection(preference, profileOptions)
                ?? CommanderPreferenceOptionViewModel.Automatic;

            if (IsCommandLineOverride)
            {
                StatusMessage = "A command-line Frontier ID controls this instance. Remove it to use a saved preference.";
            }
            else if (preference.PreferredFrontierId is not null
                && SelectedOption.IsAutomatic)
            {
                StatusMessage = $"The saved Frontier ID {preference.PreferredFrontierId} has no readable imported profile. Automatic selection is shown until the profile is restored.";
            }
            else if (preference.PreferredCommanderName is not null
                && preference.PreferredFrontierId is null
                && SelectedOption.IsAutomatic)
            {
                StatusMessage = $"The imported preference '{preference.PreferredCommanderName}' is missing or ambiguous. Automatic selection prevents writes to the wrong profile.";
            }
            else
            {
                StatusMessage = SelectedOption.IsAutomatic
                    ? "SrvSurvey will follow the newest active journal."
                    : $"SrvSurvey will start with {SelectedOption.DisplayName}.";
            }

            if (catalog.Warnings.Count > 0)
            {
                StatusMessage += $" {catalog.Warnings.Count:N0} malformed profile file(s) were ignored.";
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Commander profiles could not be scanned: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveAndRestartAsync()
    {
        if (!CanSaveAndRestart() || SelectedOption is not { } selected)
        {
            return;
        }

        try
        {
            settingsStore.Save(selected.IsAutomatic
                ? new CommanderPreferencePreferences(null, null)
                : new CommanderPreferencePreferences(
                    selected.CommanderName,
                    selected.FrontierId));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "The commander preference could not be saved: "
                + exception.Message;
            return;
        }

        if (RestartRequested is not { } handlers)
        {
            StatusMessage = "Commander preference saved. Restart SrvSurvey to use it.";
            return;
        }

        StatusMessage = "Commander preference saved; restarting SrvSurvey...";
        try
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            {
                await handler();
            }
        }
        catch (Exception exception)
        {
            StatusMessage = "Commander preference saved, but automatic restart failed: "
                + exception.Message
                + " Close and reopen SrvSurvey manually.";
        }
    }

    private bool CanSaveAndRestart()
    {
        return !IsBusy
            && !IsCommandLineOverride
            && SelectedOption is not null;
    }

    private static CommanderPreferenceOptionViewModel? ResolveSelection(
        CommanderPreferencePreferences preference,
        IReadOnlyList<CommanderPreferenceOptionViewModel> options)
    {
        if (preference.PreferredFrontierId is not null)
        {
            return options.FirstOrDefault(option => string.Equals(
                option.FrontierId,
                preference.PreferredFrontierId,
                StringComparison.OrdinalIgnoreCase));
        }

        if (preference.PreferredCommanderName is null)
        {
            return null;
        }

        var matches = options
            .Where(option => string.Equals(
                option.CommanderName,
                preference.PreferredCommanderName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static CommanderPreferenceOptionViewModel? CreateStoredOption(
        CommanderPreferencePreferences preference)
    {
        if (preference.PreferredFrontierId is not null)
        {
            var name = preference.PreferredCommanderName
                ?? preference.PreferredFrontierId;
            return new CommanderPreferenceOptionViewModel(
                name,
                preference.PreferredFrontierId,
                $"{name} ({preference.PreferredFrontierId})",
                false);
        }

        return null;
    }

    private static string GetPreferenceStatus(
        CommanderPreferencePreferences preference)
    {
        if (preference.PreferredFrontierId is not null)
        {
            return preference.PreferredCommanderName is null
                ? $"Startup is pinned to {preference.PreferredFrontierId}."
                : $"Startup is pinned to {preference.PreferredCommanderName} ({preference.PreferredFrontierId}).";
        }

        return preference.PreferredCommanderName is null
            ? "SrvSurvey will follow the newest active journal."
            : $"The imported commander preference '{preference.PreferredCommanderName}' has not resolved to a unique Frontier ID yet.";
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

public sealed record CommanderPreferenceOptionViewModel(
    string? CommanderName,
    string? FrontierId,
    string DisplayName,
    bool IsAutomatic)
{
    public static CommanderPreferenceOptionViewModel Automatic { get; } =
        new(null, null, "Automatic (newest active journal)", true);
}
