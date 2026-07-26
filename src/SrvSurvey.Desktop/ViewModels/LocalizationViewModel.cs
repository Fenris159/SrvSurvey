using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class LocalizationViewModel : INotifyPropertyChanged
{
    private readonly LocalizationSettingsStore settingsStore;
    private readonly AsyncCommand restartCommand;
    private Func<Task>? restartHandler;
    private LocalizationLanguage selectedLanguage;
    private string statusMessage = string.Empty;
    private bool isRestartRequired;

    public LocalizationViewModel(LocalizationSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
        Languages = LocalizationCatalog.Languages;
        var selectedCode = settingsStore.Load();
        selectedLanguage = Languages.Single(language => language.Code == selectedCode);
        restartCommand = new AsyncCommand(
            RestartAsync,
            () => IsRestartRequired && restartHandler is not null);
        RestartCommand = restartCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LocalizationLanguage> Languages { get; }

    public ICommand RestartCommand { get; }

    public LocalizationLanguage SelectedLanguage
    {
        get => selectedLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (selectedLanguage == value)
            {
                return;
            }

            selectedLanguage = value;
            OnPropertyChanged();
            try
            {
                settingsStore.Save(value.Code);
                IsRestartRequired = value.Code != LocalizationCatalog.CurrentLanguage;
                StatusMessage = IsRestartRequired
                    ? "Restart SrvSurvey to apply the selected language."
                    : string.Empty;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
            {
                StatusMessage = "The language changed for this session but could not be saved: "
                    + exception.Message;
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
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

    public void SetRestartHandler(Func<Task>? handler)
    {
        restartHandler = handler;
        restartCommand.RaiseCanExecuteChanged();
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
