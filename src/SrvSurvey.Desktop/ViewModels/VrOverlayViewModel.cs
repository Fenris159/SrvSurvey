using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class VrOverlayViewModel : INotifyPropertyChanged
{
    public const string DefaultMode = "Default";
    private readonly VrOverlaySettingsStore settingsStore;
    private readonly VrOverlayCalibrationStore calibrationStore;
    private readonly RelayCommand saveCommand;
    private readonly RelayCommand resetCommand;
    private readonly RelayCommand cancelCommand;
    private VrOverlayPreferences preferences;
    private VrOverlayCalibrationCatalog catalog;
    private IReadOnlyList<string> availableOverlays = [];
    private IReadOnlyList<string> availableModes = [DefaultMode];
    private string? selectedOverlayName;
    private string selectedMode = DefaultMode;
    private string? currentRuntimeMode;
    private bool isAdjusting;
    private double scale = 10;
    private double positionX;
    private double positionY;
    private double positionZ = 45;
    private double rotationPitch;
    private double rotationYaw;
    private double rotationRoll;
    private string statusMessage;

    public VrOverlayViewModel(
        VrOverlaySettingsStore settingsStore,
        VrOverlayCalibrationStore calibrationStore)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.calibrationStore = calibrationStore
            ?? throw new ArgumentNullException(nameof(calibrationStore));
        preferences = settingsStore.Load();
        try
        {
            catalog = calibrationStore.Load();
            statusMessage = preferences.Enabled
                ? "Waiting for the configured OpenVR runtime process."
                : "OpenVR overlays are disabled.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or FormatException
                or OverflowException)
        {
            catalog = EmptyCatalog();
            statusMessage =
                $"VR calibrations could not be loaded: {exception.Message}";
        }

        saveCommand = new RelayCommand(SaveCalibration, () => HasSelection);
        resetCommand = new RelayCommand(ResetCalibration, () => HasSelection);
        cancelCommand = new RelayCommand(CancelAdjustment, () => IsAdjusting);
        SaveCommand = saveCommand;
        ResetCommand = resetCommand;
        CancelCommand = cancelCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CalibrationChanged;

    public bool Enabled
    {
        get => preferences.Enabled;
        set
        {
            if (preferences.Enabled == value)
            {
                return;
            }

            preferences = preferences with { Enabled = value };
            settingsStore.Save(preferences);
            OnPropertyChanged();
            StatusMessage = value
                ? "Waiting for the configured OpenVR runtime process."
                : "OpenVR overlays are disabled.";
        }
    }

    public string RuntimeProcessName
    {
        get => preferences.RuntimeProcessName;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "vrserver"
                : value.Trim();
            if (string.Equals(
                    preferences.RuntimeProcessName,
                    normalized,
                    StringComparison.Ordinal))
            {
                return;
            }

            preferences = preferences with { RuntimeProcessName = normalized };
            settingsStore.Save(preferences);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> AvailableOverlays
    {
        get => availableOverlays;
        private set => SetField(ref availableOverlays, value);
    }

    public IReadOnlyList<string> AvailableModes
    {
        get => availableModes;
        private set => SetField(ref availableModes, value);
    }

    public string SelectedMode
    {
        get => selectedMode;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? DefaultMode
                : value.Trim();
            if (SetField(ref selectedMode, normalized))
            {
                LoadSelectedCalibration();
            }
        }
    }

    public string CurrentRuntimeMode => currentRuntimeMode
        ?? "No active vehicle or game mode";

    public string? SelectedOverlayName
    {
        get => selectedOverlayName;
        set
        {
            if (!SetField(ref selectedOverlayName, value))
            {
                return;
            }

            LoadSelectedCalibration();
            OnPropertyChanged(nameof(HasSelection));
            RaiseCommandStates();
        }
    }

    public bool HasSelection => SelectedOverlayName is not null;

    public bool IsAdjusting
    {
        get => isAdjusting;
        private set
        {
            if (SetField(ref isAdjusting, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public double Scale
    {
        get => scale;
        set => SetField(ref scale, Math.Clamp(value, 0.1, 50));
    }

    public double PositionX
    {
        get => positionX;
        set => SetField(ref positionX, value);
    }

    public double PositionY
    {
        get => positionY;
        set => SetField(ref positionY, value);
    }

    public double PositionZ
    {
        get => positionZ;
        set => SetField(ref positionZ, value);
    }

    public double RotationPitch
    {
        get => rotationPitch;
        set => SetField(ref rotationPitch, value);
    }

    public double RotationYaw
    {
        get => rotationYaw;
        set => SetField(ref rotationYaw, value);
    }

    public double RotationRoll
    {
        get => rotationRoll;
        set => SetField(ref rotationRoll, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public ICommand SaveCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand CancelCommand { get; }

    public bool BeginAdjustment()
    {
        SetAvailableOverlays(catalog.Defaults.Keys);
        RefreshAvailableModes();
        if (AvailableOverlays.Count == 0)
        {
            StatusMessage = "No valid VR calibrations are available to adjust.";
            return false;
        }

        IsAdjusting = true;
        SelectedOverlayName ??= AvailableOverlays[0];
        StatusMessage =
            "Adjustment mode is active. Save creates a verified plotters.json backup.";
        return true;
    }

    public VrOverlayCalibration? GetCalibration(
        string plotterName,
        string? mode = null)
    {
        if (IsAdjusting
            && string.Equals(
                SelectedOverlayName,
                plotterName,
                StringComparison.Ordinal))
        {
            return CreateCalibration();
        }

        return catalog.Resolve(plotterName, mode);
    }

    public void SetAvailableOverlays(IEnumerable<string> plotterNames)
    {
        ArgumentNullException.ThrowIfNull(plotterNames);
        var names = plotterNames
            .Where(name => catalog.Defaults.ContainsKey(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        AvailableOverlays = names;
        if (SelectedOverlayName is not null
            && !names.Contains(SelectedOverlayName, StringComparer.Ordinal))
        {
            SelectedOverlayName = null;
        }

        SelectedOverlayName ??= names.FirstOrDefault();
    }

    public void SetCurrentRuntimeMode(string? mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim();
        if (string.Equals(
                currentRuntimeMode,
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        currentRuntimeMode = normalized;
        OnPropertyChanged(nameof(CurrentRuntimeMode));
        RefreshAvailableModes();
    }

    public void SetRuntimeStatus(string message)
    {
        if (!IsAdjusting)
        {
            StatusMessage = message;
        }
    }

    private void SaveCalibration()
    {
        if (SelectedOverlayName is null)
        {
            return;
        }

        try
        {
            var result = calibrationStore.Save(
                SelectedOverlayName,
                CreateCalibration(),
                SelectedMode == DefaultMode ? null : SelectedMode);
            catalog = calibrationStore.Load();
            RefreshAvailableModes();
            CalibrationChanged?.Invoke(this, EventArgs.Empty);
            StatusMessage = $"Saved {SelectedOverlayName} ({SelectedMode})."
                + (result.BackupPath is null
                    ? string.Empty
                    : $" Verified backup: {result.BackupPath}");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or FormatException
                or OverflowException)
        {
            StatusMessage = $"VR calibration was not saved: {exception.Message}";
        }
    }

    private void ResetCalibration()
    {
        if (SelectedOverlayName is null)
        {
            return;
        }

        var resetSource = SelectedMode == DefaultMode
            ? catalog.FactoryDefaults
            : catalog.Defaults;
        if (!resetSource.TryGetValue(
                SelectedOverlayName,
                out var factoryDefault))
        {
            StatusMessage = "No factory calibration exists for this overlay.";
            return;
        }

        ApplyCalibration(factoryDefault);
        SaveCalibration();
    }

    private void CancelAdjustment()
    {
        IsAdjusting = false;
        LoadSelectedCalibration();
        StatusMessage = preferences.Enabled
            ? "VR adjustment mode closed."
            : "OpenVR overlays are disabled.";
    }

    private void LoadSelectedCalibration()
    {
        if (SelectedOverlayName is null)
        {
            return;
        }

        var calibration = SelectedMode == DefaultMode
            ? catalog.Defaults.GetValueOrDefault(SelectedOverlayName)
            : catalog.Resolve(SelectedOverlayName, SelectedMode);
        if (calibration is not null)
        {
            ApplyCalibration(calibration);
        }
    }

    private void RefreshAvailableModes()
    {
        var modes = catalog.Overrides.Keys
            .Append(currentRuntimeMode)
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Select(mode => mode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Prepend(DefaultMode)
            .ToArray();
        AvailableModes = modes;
        if (!modes.Contains(SelectedMode, StringComparer.OrdinalIgnoreCase))
        {
            SelectedMode = DefaultMode;
        }
    }

    private void ApplyCalibration(VrOverlayCalibration calibration)
    {
        Scale = calibration.Scale;
        PositionX = calibration.Position.X;
        PositionY = calibration.Position.Y;
        PositionZ = calibration.Position.Z;
        RotationPitch = calibration.Rotation.X;
        RotationYaw = calibration.Rotation.Y;
        RotationRoll = calibration.Rotation.Z;
    }

    private VrOverlayCalibration CreateCalibration()
    {
        return new VrOverlayCalibration(
            (float)Scale,
            new Vector3(
                (float)PositionX,
                (float)PositionY,
                (float)PositionZ),
            new Vector3(
                (float)RotationPitch,
                (float)RotationYaw,
                (float)RotationRoll));
    }

    private void RaiseCommandStates()
    {
        saveCommand.RaiseCanExecuteChanged();
        resetCommand.RaiseCanExecuteChanged();
        cancelCommand.RaiseCanExecuteChanged();
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private static VrOverlayCalibrationCatalog EmptyCatalog()
    {
        return new VrOverlayCalibrationCatalog(
            new Dictionary<string, VrOverlayCalibration>(StringComparer.Ordinal),
            new Dictionary<string, VrOverlayCalibration>(StringComparer.Ordinal),
            new Dictionary<
                string,
                IReadOnlyDictionary<string, VrOverlayCalibration>>(
                    StringComparer.OrdinalIgnoreCase));
    }

    private sealed class RelayCommand(Action execute, Func<bool> canExecute)
        : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public void Execute(object? parameter)
        {
            execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
