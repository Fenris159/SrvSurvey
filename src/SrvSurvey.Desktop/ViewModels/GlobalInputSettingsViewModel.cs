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
    private IReadOnlyList<ControllerDeviceOptionViewModel> controllerDevices = [];
    private ControllerDeviceOptionViewModel? selectedController;
    private KeyboardInputDiagnostics keyboardDiagnostics = new(
        null,
        false,
        false,
        false,
        "No configured shortcut received yet.",
        "Waiting for Elite Dangerous.",
        "No separate game display is connected."
    );
    private IReadOnlyList<KeyboardInputSourceOption> keyboardSourceOptions = [];
    private readonly WorkspaceCommand desktopShortcutSettingsCommand;
    private Func<Task>? openDesktopShortcutSettings;
    private bool openingDesktopShortcutSettings;
    private string? desktopShortcutSettingsError;

    /// <summary>Loads profile-scoped bindings and source preferences and prepares keyboard recovery controls.</summary>
    public GlobalInputSettingsViewModel(
        GlobalInputSettingsStore store,
        OverlayPlatformCapabilities capabilities,
        IControllerDeviceProvider? controllerDeviceProvider = null
    )
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.controllerDeviceProvider = controllerDeviceProvider ?? new SdlControllerDeviceProvider();
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        settings = store.Load();
        Bindings = GlobalInputActionCatalog
            .All.Select(definition => new InputBindingViewModel(
                definition,
                settings.Bindings.GetValueOrDefault(definition.Action) ?? definition.DefaultChord,
                SaveBinding
            ))
            .ToArray();
        ResetBindingsCommand = new DelegateCommand(ResetBindings);
        ResetKeyboardDetectionCommand = new DelegateCommand(ResetKeyboardDetection);
        desktopShortcutSettingsCommand = new WorkspaceCommand(
            () => _ = OpenDesktopShortcutSettingsAsync(),
            () => CanOpenDesktopShortcutSettings
        );
        UpdateKeyboardDiagnostics(keyboardDiagnostics);
        MiningBindings = Bindings
            .Where(binding => binding.Definition.Action is >= GlobalInputAction.Track1 and <= GlobalInputAction.Track6)
            .ToArray();
        RefreshControllersCommand = new DelegateCommand(RefreshControllerDevices);
        runtimeStatus = IsKeyboardAvailable ? "Global keyboard input is ready to start." : Capabilities.StatusText;
        controllerRuntimeStatus = IsControllerAvailable
            ? "Controller input is ready to start."
            : "Controller input is unavailable on this platform.";
        RefreshControllerDevices();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<GlobalInputSettingsChangedEventArgs>? SettingsChanged;
    public event EventHandler? KeyboardDetectionResetRequested;

    public OverlayPlatformCapabilities Capabilities { get; }

    public IReadOnlyList<InputBindingViewModel> Bindings { get; }

    public IReadOnlyList<InputBindingViewModel> MiningBindings { get; }

    public ICommand DesktopShortcutSettingsCommand => desktopShortcutSettingsCommand;
    public bool IsDesktopShortcutSettingsVisible =>
        Capabilities.Host is OverlayHostKind.LinuxX11 or OverlayHostKind.LinuxXWayland or OverlayHostKind.LinuxWayland;
    public bool CanOpenDesktopShortcutSettings =>
        IsDesktopShortcutSettingsVisible
        && KeyboardEnabled
        && openDesktopShortcutSettings is not null
        && keyboardDiagnostics.CanOpenDesktopShortcutSettings
        && !openingDesktopShortcutSettings;
    public string DesktopShortcutSettingsStatus =>
        desktopShortcutSettingsError ?? keyboardDiagnostics.DesktopShortcutSettingsStatus.Split('\n')[0];

    /// <summary>Displays readable desktop grants separately from the compact portal status.</summary>
    public string ApprovedDesktopShortcuts
    {
        get
        {
            string status = keyboardDiagnostics.DesktopShortcutSettingsStatus;
            int separator = status.IndexOf('\n');
            return separator >= 0 ? status[(separator + 1)..] : string.Empty;
        }
    }
    public bool HasApprovedDesktopShortcuts => ApprovedDesktopShortcuts.Length > 0;

    /// <summary>Connects the explicit desktop settings button without invoking registration or opening a dialog.</summary>
    public void SetDesktopShortcutSettingsHandler(Func<Task>? handler)
    {
        openDesktopShortcutSettings = handler;
        desktopShortcutSettingsCommand.Refresh();
    }

    /// <summary>Serializes button requests and reports failures without an unobserved UI task exception.</summary>
    internal async Task OpenDesktopShortcutSettingsAsync()
    {
        if (!CanOpenDesktopShortcutSettings)
        {
            return;
        }
        openingDesktopShortcutSettings = true;
        desktopShortcutSettingsError = null;
        desktopShortcutSettingsCommand.Refresh();
        OnPropertyChanged(nameof(DesktopShortcutSettingsStatus));
        try
        {
            await openDesktopShortcutSettings!();
        }
        catch (OperationCanceledException)
        {
            // Disabling keyboard input or closing SrvSurvey cancels the desktop request.
        }
        catch (Exception)
        {
            desktopShortcutSettingsError =
                "Desktop shortcut settings could not open. Try again when the desktop portal is available.";
        }
        finally
        {
            openingDesktopShortcutSettings = false;
            desktopShortcutSettingsCommand.Refresh();
            OnPropertyChanged(nameof(DesktopShortcutSettingsStatus));
        }
    }

    public ICommand ResetBindingsCommand { get; }
    public ICommand ResetKeyboardDetectionCommand { get; }
    public IReadOnlyList<KeyboardInputSourceOption> KeyboardSourceOptions => keyboardSourceOptions;

    /// <summary>Persists one manual input preference for every keyboard shortcut in this profile.</summary>
    public KeyboardInputSourceOption SelectedKeyboardSource
    {
        get => keyboardSourceOptions.First(option => option.Mode == settings.KeyboardSource);
        set
        {
            if (value is null || !value.IsAvailable)
            {
                OnPropertyChanged();
                return;
            }
            if (value.Mode != settings.KeyboardSource)
            {
                Apply(settings with { KeyboardSource = value.Mode });
                OnPropertyChanged();
                OnPropertyChanged(nameof(KeyboardSourceDetails));
                OnPropertyChanged(nameof(KeyboardSourceStatus));
            }
        }
    }

    public string KeyboardSourceDetails => SelectedKeyboardSource.Details;
    public string KeyboardFocusStatus => keyboardDiagnostics.FocusStatus;
    public string LastKeyboardInput => keyboardDiagnostics.LastInput;
    public string KeyboardSourceStatus
    {
        get
        {
            if (settings.KeyboardSource == KeyboardInputMode.Automatic)
            {
                return keyboardDiagnostics.SelectedSource is { } source
                    ? $"Automatic - using {keyboardSourceOptions.First(option => option.Mode == source).Label}."
                    : "Automatic - waiting for a shortcut with Elite Dangerous focused.";
            }
            string unavailable = SelectedKeyboardSource.IsAvailable ? string.Empty : " (unavailable)";
            return $"Manual - {SelectedKeyboardSource.Label}{unavailable}.";
        }
    }

    /// <summary>Updates source choices and diagnostics without changing saved bindings or granting permissions.</summary>
    public void UpdateKeyboardDiagnostics(KeyboardInputDiagnostics diagnostics)
    {
        if (keyboardSourceOptions.Count > 0 && keyboardDiagnostics == diagnostics)
        {
            return;
        }
        keyboardDiagnostics = diagnostics;
        keyboardSourceOptions =
        [
            new(
                KeyboardInputMode.Automatic,
                "Automatic (recommended)",
                true,
                "Uses one detected source for all keyboard shortcuts. Relearns when the game changes or repeated input proves another source is working."
            ),
            new(
                KeyboardInputMode.Desktop,
                "Desktop keyboard",
                diagnostics.DesktopAvailable,
                "Uses the desktop keyboard listener. A separate game display or native Wayland game may not deliver input to this listener."
            ),
            new(
                KeyboardInputMode.GameDisplay,
                "Game display",
                diagnostics.GameDisplayAvailable,
                diagnostics.GameDisplayStatus
                    + " Uses the game's separate X11 display. The overlay monitor helps select a client when desktop geometry is available."
            ),
            new(
                KeyboardInputMode.WaylandPortal,
                "Wayland portal",
                diagnostics.PortalAvailable,
                "Requires desktop portal support and approval for all configured keyboard shortcuts. Use Desktop shortcut settings to approve or change bindings. The desktop may override requested keys; its approved keys are shown below. Native Wayland may not expose which window has focus."
            ),
        ];
        OnPropertyChanged(nameof(KeyboardSourceOptions));
        OnPropertyChanged(nameof(SelectedKeyboardSource));
        OnPropertyChanged(nameof(KeyboardSourceDetails));
        OnPropertyChanged(nameof(KeyboardSourceStatus));
        OnPropertyChanged(nameof(KeyboardFocusStatus));
        OnPropertyChanged(nameof(LastKeyboardInput));
        desktopShortcutSettingsError = null;
        OnPropertyChanged(nameof(DesktopShortcutSettingsStatus));
        OnPropertyChanged(nameof(ApprovedDesktopShortcuts));
        OnPropertyChanged(nameof(HasApprovedDesktopShortcuts));
        desktopShortcutSettingsCommand.Refresh();
    }

    /// <summary>Returns to Automatic and requests fresh session detection while preserving bindings and approvals.</summary>
    private void ResetKeyboardDetection()
    {
        if (settings.KeyboardSource != KeyboardInputMode.Automatic)
        {
            Apply(settings with { KeyboardSource = KeyboardInputMode.Automatic });
            OnPropertyChanged(nameof(SelectedKeyboardSource));
            OnPropertyChanged(nameof(KeyboardSourceDetails));
            OnPropertyChanged(nameof(KeyboardSourceStatus));
        }
        KeyboardDetectionResetRequested?.Invoke(this, EventArgs.Empty);
    }

    public ICommand RefreshControllersCommand { get; }

    /// <summary>Allows Wayland keyboard enablement while runtime portal discovery reports actual availability.</summary>
    public bool IsKeyboardAvailable =>
        Capabilities.SupportsGlobalInput || Capabilities.Host == OverlayHostKind.LinuxWayland;

    public bool IsControllerAvailable =>
        Capabilities.Host
            is OverlayHostKind.Windows
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

    public bool CanEnableControllerInput => IsControllerAvailable && selectedController is not null;

    public ControllerDeviceOptionViewModel? SelectedController
    {
        get => selectedController;
        set
        {
            if (string.Equals(selectedController?.Id, value?.Id, StringComparison.Ordinal))
            {
                return;
            }

            selectedController = value;
            Apply(
                settings with
                {
                    ControllerDeviceId = value?.Id,
                    ControllerEnabled = value is not null && settings.ControllerEnabled,
                }
            );
            OnPropertyChanged();
            OnPropertyChanged(nameof(ControllerEnabled));
            OnPropertyChanged(nameof(CanEnableControllerInput));
        }
    }

    /// <summary>Persists keyboard enablement and updates whether desktop configuration can be opened.</summary>
    public bool KeyboardEnabled
    {
        get => settings.KeyboardEnabled;
        set
        {
            if (value == settings.KeyboardEnabled || (value && !IsKeyboardAvailable))
            {
                return;
            }

            Apply(settings with { KeyboardEnabled = value });
            OnPropertyChanged();
            desktopShortcutSettingsCommand.Refresh();
        }
    }

    public bool ControllerEnabled
    {
        get => settings.ControllerEnabled;
        set
        {
            if (
                value == settings.ControllerEnabled
                || (value && (!IsControllerAvailable || string.IsNullOrWhiteSpace(settings.ControllerDeviceId)))
            )
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
        GlobalInputActionDefinition definition = GlobalInputActionCatalog.Get(action);
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
            definition => definition.DefaultChord
        );
        foreach (InputBindingViewModel binding in Bindings)
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
            ControllerDiscoveryStatus = "SDL controller discovery is unavailable on this platform.";
            return;
        }

        ControllerDeviceDiscoveryResult result = controllerDeviceProvider.Discover();
        var devices = result
            .Devices.Select(device => new ControllerDeviceOptionViewModel(
                device.Id,
                device.Name,
                device.Description,
                IsConnected: true
            ))
            .ToList();
        string? configuredId = settings.ControllerDeviceId;
        ControllerDeviceOptionViewModel? configured = devices.FirstOrDefault(device =>
            string.Equals(device.Id, configuredId, StringComparison.Ordinal)
        );
        if (configured is null && !string.IsNullOrWhiteSpace(configuredId))
        {
            configured = new ControllerDeviceOptionViewModel(
                configuredId,
                "Previously selected controller",
                "Not currently connected; input will resume when it returns.",
                IsConnected: false
            );
            devices.Add(configured);
        }

        ControllerDevices = devices;
        selectedController = configured;
        OnPropertyChanged(nameof(SelectedController));
        OnPropertyChanged(nameof(CanEnableControllerInput));
        ControllerDiscoveryStatus =
            result.ErrorMessage
            ?? (
                result.Devices.Count switch
                {
                    0 => "No controllers are currently connected.",
                    1 => "Found 1 connected controller.",
                    _ => $"Found {result.Devices.Count} connected controllers.",
                }
            );
    }

    private void Apply(GlobalInputSettings updatedSettings)
    {
        settings = updatedSettings;
        try
        {
            store.Save(settings);
            PersistenceStatus = string.Empty;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            PersistenceStatus = "Input settings changed for this session but could not be saved: " + exception.Message;
        }

        SettingsChanged?.Invoke(this, new GlobalInputSettingsChangedEventArgs(settings));
    }

    private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
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

    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add
            { /* This command is always executable. */
            }
            remove
            { /* This command is always executable. */
            }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}

public sealed class GlobalInputSettingsChangedEventArgs(GlobalInputSettings settings) : EventArgs
{
    public GlobalInputSettings Settings { get; } = settings;
}

public sealed record ControllerDeviceOptionViewModel(
    string Id,
    string DisplayName,
    string Description,
    bool IsConnected
);
