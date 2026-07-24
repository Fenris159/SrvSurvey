using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GlobalInputSettingsViewModel : INotifyPropertyChanged
{
    private readonly GlobalInputSettingsStore store;
    private GlobalInputSettings settings;
    private string persistenceStatus = string.Empty;
    private string runtimeStatus;
    private string lastActionStatus = string.Empty;

    public GlobalInputSettingsViewModel(
        GlobalInputSettingsStore store,
        OverlayPlatformCapabilities capabilities)
    {
        this.store = store
            ?? throw new ArgumentNullException(nameof(store));
        Capabilities = capabilities
            ?? throw new ArgumentNullException(nameof(capabilities));
        settings = store.Load();
        Bindings = GlobalInputActionCatalog.All
            .Select(definition => new InputBindingViewModel(
                definition,
                settings.Bindings.GetValueOrDefault(definition.Action)
                    ?? definition.DefaultChord,
                SaveBinding))
            .ToArray();
        ResetBindingsCommand = new DelegateCommand(ResetBindings);
        runtimeStatus = IsKeyboardAvailable
            ? "Global keyboard input is ready to start."
            : Capabilities.StatusText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<GlobalInputSettingsChangedEventArgs>?
        SettingsChanged;

    public OverlayPlatformCapabilities Capabilities { get; }

    public IReadOnlyList<InputBindingViewModel> Bindings { get; }

    public ICommand ResetBindingsCommand { get; }

    public bool IsKeyboardAvailable => Capabilities.SupportsGlobalInput;

    public bool KeyboardEnabled
    {
        get => settings.KeyboardEnabled;
        set
        {
            if (value == settings.KeyboardEnabled
                || (value && !IsKeyboardAvailable))
            {
                return;
            }

            Apply(settings with { KeyboardEnabled = value });
            OnPropertyChanged();
        }
    }

    public string RuntimeStatus
    {
        get => runtimeStatus;
        private set => SetField(ref runtimeStatus, value);
    }

    public string PersistenceStatus
    {
        get => persistenceStatus;
        private set
        {
            if (SetField(ref persistenceStatus, value))
            {
                OnPropertyChanged(nameof(HasPersistenceStatus));
            }
        }
    }

    public bool HasPersistenceStatus => PersistenceStatus.Length > 0;

    public string LastActionStatus
    {
        get => lastActionStatus;
        private set
        {
            if (SetField(ref lastActionStatus, value))
            {
                OnPropertyChanged(nameof(HasLastActionStatus));
            }
        }
    }

    public bool HasLastActionStatus => LastActionStatus.Length > 0;

    public GlobalInputSettings CurrentSettings => settings;

    public void UpdateRuntimeStatus(string status)
    {
        RuntimeStatus = status;
    }

    public void ReportAction(GlobalInputAction action, bool handled)
    {
        var definition = GlobalInputActionCatalog.Get(action);
        LastActionStatus = handled
            ? $"Shortcut received: {definition.DisplayName}."
            : $"Shortcut received: {definition.DisplayName}; its target view is not ported yet.";
    }

    private void SaveBinding(InputBindingViewModel binding, string chord)
    {
        var bindings = settings.Bindings.ToDictionary();
        bindings[binding.Definition.Action] = chord;
        Apply(settings with { Bindings = bindings });
    }

    private void ResetBindings()
    {
        var bindings = GlobalInputActionCatalog.All.ToDictionary(
            definition => definition.Action,
            definition => definition.DefaultChord);
        foreach (var binding in Bindings)
        {
            binding.Reset(bindings[binding.Definition.Action]);
        }

        Apply(settings with { Bindings = bindings });
    }

    private void Apply(GlobalInputSettings updatedSettings)
    {
        settings = updatedSettings;
        try
        {
            store.Save(settings);
            PersistenceStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            PersistenceStatus =
                "Input settings changed for this session but could not be saved: "
                + exception.Message;
        }

        SettingsChanged?.Invoke(
            this,
            new GlobalInputSettingsChangedEventArgs(settings));
    }

    private bool SetField(
        ref string field,
        string value,
        [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
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

    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}

public sealed record GlobalInputSettingsChangedEventArgs(
    GlobalInputSettings Settings);
