using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GlobalInputSettingsViewModel : INotifyPropertyChanged
{
    private readonly GlobalInputSettingsStore store;
    private readonly IControllerDeviceProvider controllerDeviceProvider;
    private GlobalInputSettings settings;
    private string persistenceStatus = string.Empty;
    private string runtimeStatus;
    private string controllerRuntimeStatus;
    private string controllerDiscoveryStatus = string.Empty;
    private string lastActionStatus = string.Empty;
    private IReadOnlyList<ControllerDeviceOptionViewModel> controllerDevices =
        [];
    private ControllerDeviceOptionViewModel? selectedController;

    public GlobalInputSettingsViewModel(
        GlobalInputSettingsStore store,
        OverlayPlatformCapabilities capabilities,
        IControllerDeviceProvider? controllerDeviceProvider = null)
    {
        this.store = store
            ?? throw new ArgumentNullException(nameof(store));
        this.controllerDeviceProvider = controllerDeviceProvider
            ?? new SdlControllerDeviceProvider();
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
        MiningBindings = Bindings.Where(binding => binding.Definition.Action is
            >= GlobalInputAction.MiningRig1 and <= GlobalInputAction.MiningRig6).ToArray();
        RefreshControllersCommand = new DelegateCommand(
            RefreshControllerDevices);
        runtimeStatus = IsKeyboardAvailable
            ? "Global keyboard input is ready to start."
            : Capabilities.StatusText;
        controllerRuntimeStatus = IsControllerAvailable
            ? "Controller input is ready to start."
            : "Controller input is unavailable on this platform.";
        RefreshControllerDevices();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<GlobalInputSettingsChangedEventArgs>?
        SettingsChanged;

    public OverlayPlatformCapabilities Capabilities { get; }

    public IReadOnlyList<InputBindingViewModel> Bindings { get; }

    public IReadOnlyList<InputBindingViewModel> MiningBindings { get; }

    public ICommand ResetBindingsCommand { get; }

    public ICommand RefreshControllersCommand { get; }

    public bool IsKeyboardAvailable => Capabilities.SupportsGlobalInput;

    public bool IsControllerAvailable => Capabilities.Host is
        OverlayHostKind.Windows
        or OverlayHostKind.LinuxX11
        or OverlayHostKind.LinuxXWayland
        or OverlayHostKind.LinuxWayland;

    public IReadOnlyList<ControllerDeviceOptionViewModel> ControllerDevices
    {
        get => controllerDevices;
        private set
        {
            if (ReferenceEquals(controllerDevices, value))
            {
                return;
            }

            controllerDevices = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasControllerDevices));
        }
    }

    public bool HasControllerDevices => ControllerDevices.Count > 0;

    public bool CanEnableControllerInput => IsControllerAvailable
        && selectedController is not null;

    public ControllerDeviceOptionViewModel? SelectedController
    {
        get => selectedController;
        set
        {
            if (string.Equals(
                    selectedController?.Id,
                    value?.Id,
                    StringComparison.Ordinal))
            {
                return;
            }

            selectedController = value;
            Apply(settings with
            {
                ControllerDeviceId = value?.Id,
                ControllerEnabled = value is not null
                    && settings.ControllerEnabled,
            });
            OnPropertyChanged();
            OnPropertyChanged(nameof(ControllerEnabled));
            OnPropertyChanged(nameof(CanEnableControllerInput));
        }
    }

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

    public bool ControllerEnabled
    {
        get => settings.ControllerEnabled;
        set
        {
            if (value == settings.ControllerEnabled
                || (value
                    && (!IsControllerAvailable
                        || string.IsNullOrWhiteSpace(
                            settings.ControllerDeviceId))))
            {
                return;
            }

            Apply(settings with { ControllerEnabled = value });
            OnPropertyChanged();
        }
    }

    public string RuntimeStatus
    {
        get => runtimeStatus;
        private set => SetField(ref runtimeStatus, value);
    }

    public string ControllerRuntimeStatus
    {
        get => controllerRuntimeStatus;
        private set => SetField(ref controllerRuntimeStatus, value);
    }

    public string ControllerDiscoveryStatus
    {
        get => controllerDiscoveryStatus;
        private set => SetField(ref controllerDiscoveryStatus, value);
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

    public void UpdateControllerRuntimeStatus(string status)
    {
        ControllerRuntimeStatus = status;
    }

    public void ReportAction(GlobalInputAction action, bool handled)
    {
        var definition = GlobalInputActionCatalog.Get(action);
        LastActionStatus = handled
            ? $"Shortcut received: {definition.DisplayName}."
            : $"Shortcut received: {definition.DisplayName}; it is not available in the current game context.";
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

    private void RefreshControllerDevices()
    {
        if (!IsControllerAvailable)
        {
            ControllerDevices = [];
            selectedController = null;
            OnPropertyChanged(nameof(SelectedController));
            OnPropertyChanged(nameof(CanEnableControllerInput));
            ControllerDiscoveryStatus =
                "SDL controller discovery is unavailable on this platform.";
            return;
        }

        var result = controllerDeviceProvider.Discover();
        var devices = result.Devices
            .Select(device => new ControllerDeviceOptionViewModel(
                device.Id,
                device.Name,
                device.Description,
                IsConnected: true))
            .ToList();
        var configuredId = settings.ControllerDeviceId;
        var configured = devices.FirstOrDefault(device => string.Equals(
            device.Id,
            configuredId,
            StringComparison.Ordinal));
        if (configured is null && !string.IsNullOrWhiteSpace(configuredId))
        {
            configured = new ControllerDeviceOptionViewModel(
                configuredId,
                "Previously selected controller",
                "Not currently connected; input will resume when it returns.",
                IsConnected: false);
            devices.Add(configured);
        }

        ControllerDevices = devices;
        selectedController = configured;
        OnPropertyChanged(nameof(SelectedController));
        OnPropertyChanged(nameof(CanEnableControllerInput));
        ControllerDiscoveryStatus = result.ErrorMessage
            ?? (result.Devices.Count switch
            {
                0 => "No controllers are currently connected.",
                1 => "Found 1 connected controller.",
                _ => $"Found {result.Devices.Count} connected controllers.",
            });
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
            add { /* This command is always executable. */ }
            remove { /* This command is always executable. */ }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}

public sealed record GlobalInputSettingsChangedEventArgs(
    GlobalInputSettings Settings);

public sealed record ControllerDeviceOptionViewModel(
    string Id,
    string DisplayName,
    string Description,
    bool IsConnected);
