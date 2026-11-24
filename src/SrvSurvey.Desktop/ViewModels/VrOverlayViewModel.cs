using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class VrOverlayViewModel : INotifyPropertyChanged
{
    public const string DefaultMode = "Default";
    public const string AllCategoriesLabel = "All overlays";
    private const string OverlaysOffHeadline = "VR overlays are off";
    private readonly VrOverlaySettingsStore settingsStore;
    private readonly VrOverlayCalibrationStore calibrationStore;
    private readonly IReadOnlyList<VrPlatformProfile> platformProfiles;
    private readonly List<RelayCommand> calibrationCommands = [];
    private readonly Dictionary<string, VrOverlayCalibration> pendingAdjustments = new(StringComparer.Ordinal);
    private readonly RelayCommand saveCommand;
    private readonly RelayCommand resetCommand;
    private readonly RelayCommand cancelCommand;
    private VrOverlayPreferences preferences;
    private VrOverlayCalibrationCatalog catalog;
    private VrPlatformProfile selectedPlatformProfile;
    private IReadOnlyList<string> allAvailableOverlays = [];
    private IReadOnlyList<string> availableCategories = [AllCategoriesLabel];
    private IReadOnlyList<string> availableOverlays = [];
    private IReadOnlyList<string> availableModes = [DefaultMode];
    private string selectedCategory = AllCategoriesLabel;
    private string? selectedOverlayName;
    private string selectedMode = DefaultMode;
    private string? currentRuntimeMode;
    private bool isAdjusting;
    private bool isDragModeEnabled;
    private bool isLoadingSelection;
    private double scale = 10;
    private double positionX;
    private double positionY;
    private double positionZ = 45;
    private double rotationPitch;
    private double rotationYaw;
    private double rotationRoll;
    private int adjustmentStepIndex = 1;
    private string statusMessage;
    private VrOverlayConnectionState connectionState;
    private string connectionHeadline;
    private string connectionDetail;
    private string platformSupportMessage;

    public VrOverlayViewModel(
        VrOverlaySettingsStore settingsStore,
        VrOverlayCalibrationStore calibrationStore,
        IReadOnlyList<VrPlatformProfile>? platformProfiles = null
    )
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.calibrationStore = calibrationStore ?? throw new ArgumentNullException(nameof(calibrationStore));
        this.platformProfiles = platformProfiles ?? VrPlatformProfileCatalog.ForCurrentPlatform();
        platformSupportMessage = CreatePlatformSupportMessage();
        preferences = settingsStore.Load();
        selectedPlatformProfile =
            this.platformProfiles.FirstOrDefault(profile => profile.Id == preferences.RuntimeProfileId)
            ?? this.platformProfiles[0];
        if (!selectedPlatformProfile.IsAvailable && preferences.Enabled)
        {
            preferences = preferences with { Enabled = false };
            settingsStore.Save(preferences);
        }
        connectionState = preferences.Enabled
            ? VrOverlayConnectionState.WaitingForRuntime
            : VrOverlayConnectionState.Disabled;
        connectionHeadline = preferences.Enabled ? "Waiting for the VR runtime" : OverlaysOffHeadline;
        connectionDetail = preferences.Enabled
            ? $"Start {selectedPlatformProfile.DisplayName} and complete the pairing checklist."
            : "Choose a connection route, complete its pairing steps, then enable VR overlays.";
        if (!selectedPlatformProfile.IsAvailable)
        {
            connectionHeadline = "VR route unavailable";
            connectionDetail = selectedPlatformProfile.UnavailableReason;
        }
        try
        {
            catalog = calibrationStore.Load(selectedPlatformProfile.Id);
            statusMessage = preferences.Enabled ? "Waiting for the selected VR session." : "VR overlays are disabled.";
            if (!selectedPlatformProfile.IsAvailable)
            {
                statusMessage = selectedPlatformProfile.UnavailableReason;
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or JsonException
                        or FormatException
                        or OverflowException
            )
        {
            catalog = EmptyCatalog();
            statusMessage = $"VR calibrations could not be loaded: {exception.Message}";
        }

        saveCommand = new RelayCommand(SaveCalibration, () => HasSelection);
        resetCommand = new RelayCommand(ResetCalibration, () => HasSelection);
        cancelCommand = new RelayCommand(CancelAdjustment, () => IsAdjusting);
        SaveCommand = saveCommand;
        ResetCommand = resetCommand;
        CancelCommand = cancelCommand;
        RefreshConnectionCommand = new RelayCommand(RequestConnectionCheck, () => true);
        PlaceInFrontCommand = CreateCalibrationCommand(() => PlaceInFrontRequested?.Invoke(this, EventArgs.Empty));
        PreviousCategoryCommand = CreateCalibrationCommand(() => CycleCategory(-1));
        NextCategoryCommand = CreateCalibrationCommand(() => CycleCategory(1));
        PreviousOverlayCommand = CreateCalibrationCommand(() => CycleOverlay(-1));
        NextOverlayCommand = CreateCalibrationCommand(() => CycleOverlay(1));
        PreviousModeCommand = CreateCalibrationCommand(() => CycleMode(-1));
        NextModeCommand = CreateCalibrationCommand(() => CycleMode(1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CalibrationChanged;

    public event EventHandler? ConnectionCheckRequested;

    public event EventHandler? PlaceInFrontRequested;

    public bool Enabled
    {
        get => preferences.Enabled;
        set
        {
            if (value && !CanEnableOverlays)
            {
                SetRuntimeStatus(SelectedPlatformProfile.UnavailableReason);
                return;
            }
            if (preferences.Enabled == value)
            {
                return;
            }

            preferences = preferences with { Enabled = value };
            settingsStore.Save(preferences);
            OnPropertyChanged();
            if (value)
            {
                SetConnectionStatus(
                    VrOverlayConnectionState.WaitingForRuntime,
                    "Waiting for the VR runtime",
                    $"Start {SelectedPlatformProfile.DisplayName} and complete the pairing checklist."
                );
                RequestConnectionCheck();
            }
            else
            {
                SetConnectionStatus(
                    VrOverlayConnectionState.Disabled,
                    OverlaysOffHeadline,
                    "Your saved platform and overlay calibration are unchanged."
                );
            }
        }
    }

    public IReadOnlyList<VrPlatformProfile> PlatformProfiles => platformProfiles;

    public VrPlatformProfile SelectedPlatformProfile
    {
        get => selectedPlatformProfile;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            VrPlatformProfile? option = platformProfiles.FirstOrDefault(profile => profile.Id == value.Id);
            if (option?.IsAvailable != true)
            {
                return;
            }
            value = option;
            if (selectedPlatformProfile == value)
            {
                return;
            }

            if (!TryLoadCalibrations(value.Id))
            {
                return;
            }

            IsChangingRoute = true;
            try
            {
                pendingAdjustments.Clear();
                IsDragModeEnabled = false;
                selectedPlatformProfile = value;
                preferences = preferences with { RuntimeProfileId = value.Id };
                settingsStore.Save(preferences);
                SetAvailableOverlays(catalog.Defaults.Keys.Where(OverlayRuntimePresentationFactory.IsSupported));
                RefreshAvailableModes();
                LoadSelectedCalibration();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomRuntime));
                OnPropertyChanged(nameof(IsVdxr));
                OnPropertyChanged(nameof(CanEnableOverlays));
                OnPropertyChanged(nameof(PlatformSupportMessage));
                SetConnectionStatus(
                    Enabled ? VrOverlayConnectionState.WaitingForRuntime : VrOverlayConnectionState.Disabled,
                    Enabled ? "Connection route changed" : OverlaysOffHeadline,
                    Enabled
                        ? $"Complete the {value.DisplayName} pairing steps while SrvSurvey checks the runtime."
                        : "Complete this route's pairing steps, then enable VR overlays."
                );
            }
            finally
            {
                IsChangingRoute = false;
            }
            RequestConnectionCheck();
        }
    }

    // Reload editor fields as one route change before asking the coordinator to publish.
    internal bool IsChangingRoute { get; private set; }

    // Batch multi-field calibration updates into a single coordinator synchronize pass.
    internal bool IsApplyingCalibrationBatch { get; private set; }

    public bool CanEnableOverlays => SelectedPlatformProfile.IsAvailable;

    private bool TryLoadCalibrations(string profileId)
    {
        try
        {
            catalog = calibrationStore.Load(profileId);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or JsonException
                        or FormatException
                        or OverflowException
            )
        {
            StatusMessage = $"VR calibrations could not be loaded: {exception.Message}";
            return false;
        }
    }

    public bool IsCustomRuntime => SelectedPlatformProfile.IsCustom;

    public bool IsVdxr => SelectedPlatformProfile.Id == VrPlatformProfileCatalog.MetaVdxrProfileId;

    public bool EdvrControllerInput
    {
        get => preferences.EdvrControllerInput;
        set
        {
            if (preferences.EdvrControllerInput == value)
            {
                return;
            }
            preferences = preferences with { EdvrControllerInput = value };
            settingsStore.Save(preferences);
            OnPropertyChanged();
            RequestConnectionCheck();
        }
    }

    public string PlatformSupportMessage
    {
        get
        {
            if (!SelectedPlatformProfile.IsAvailable)
            {
                return SelectedPlatformProfile.UnavailableReason;
            }
            if (SelectedPlatformProfile.Id == VrPlatformProfileCatalog.MetaVdxrProfileId)
            {
                return "VDXR: register the OpenXR layer from the Windows x64 package, then restart Elite. Direct3D 11 is required.";
            }
            if (VrPlatformProfileCatalog.IsLinuxOpenXrProfile(SelectedPlatformProfile.Id))
            {
                return CreateLinuxOpenXrSupportMessage();
            }
            return platformSupportMessage;
        }
    }

    private static string CreateLinuxOpenXrSupportMessage()
    {
        string? library = LinuxVrRuntimeInspector.Default.FindOverlayLibrary();
        return library is null
            ? "Linux OpenXR: packaged XR_EXTX_overlay support (libSrvSurveyLinuxOpenXrOverlay.so) is unavailable. Reinstall SrvSurvey or verify that the package matches your system architecture."
            : "Linux OpenXR: packaged XR_EXTX_overlay Vulkan support is ready. Requires libopenxr_loader and Vulkan.";
    }

    private static string CreatePlatformSupportMessage()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows: the OpenVR client library is included with SrvSurvey.";
        }

        if (OperatingSystem.IsLinux())
        {
            string? library = OpenVrNativeLibraryResolver.FindExistingLinuxLibrary();
            return library is null
                ? "Linux: packaged OpenVR support is unavailable. Reinstall SrvSurvey or verify that the package matches your system architecture."
                : "Linux: packaged OpenVR support is ready.";
        }

        return "VR overlay projection is supported on Windows and Linux.";
    }

    public string RuntimeProcessName
    {
        get => preferences.RuntimeProcessName;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "vrserver" : value.Trim();
            if (string.Equals(preferences.RuntimeProcessName, normalized, StringComparison.Ordinal))
            {
                return;
            }

            preferences = preferences with { RuntimeProcessName = normalized };
            settingsStore.Save(preferences);
            OnPropertyChanged();
            RequestConnectionCheck();
        }
    }

    public VrOverlayConnectionState ConnectionState
    {
        get => connectionState;
        private set
        {
            if (SetField(ref connectionState, value))
            {
                OnPropertyChanged(nameof(ConnectionStateLabel));
            }
        }
    }

    public string ConnectionStateLabel =>
        ConnectionState switch
        {
            VrOverlayConnectionState.Disabled => "OFF",
            VrOverlayConnectionState.WaitingForRuntime => "WAITING",
            VrOverlayConnectionState.Connecting => "CONNECTING",
            VrOverlayConnectionState.Ready => "CONNECTED",
            VrOverlayConnectionState.Error => "NEEDS ATTENTION",
            _ => "UNKNOWN",
        };

    public string ConnectionHeadline
    {
        get => connectionHeadline;
        private set => SetField(ref connectionHeadline, value);
    }

    public string ConnectionDetail
    {
        get => connectionDetail;
        private set => SetField(ref connectionDetail, value);
    }

    public IReadOnlyList<string> AvailableCategories
    {
        get => availableCategories;
        private set => SetField(ref availableCategories, value);
    }

    public string SelectedCategory
    {
        get => selectedCategory;
        set
        {
            string normalized =
                !string.IsNullOrWhiteSpace(value) && availableCategories.Contains(value, StringComparer.Ordinal)
                    ? value
                    : AllCategoriesLabel;
            if (!SetField(ref selectedCategory, normalized))
            {
                return;
            }

            pendingAdjustments.Clear();
            string? previousSelection = SelectedOverlayName;
            ApplyCategoryFilter();
            if (string.Equals(previousSelection, SelectedOverlayName, StringComparison.Ordinal))
            {
                LoadSelectedCalibration();
            }

            OnPropertyChanged(nameof(IsCategoryPreviewActive));
            OnPropertyChanged(nameof(VisibleAdjustmentOverlays));
        }
    }

    public bool IsCategoryPreviewActive =>
        !string.Equals(SelectedCategory, AllCategoriesLabel, StringComparison.Ordinal);

    public IReadOnlyList<string> VisibleAdjustmentOverlays
    {
        get
        {
            if (!IsAdjusting)
            {
                return [];
            }

            if (IsCategoryPreviewActive)
            {
                return AvailableOverlays;
            }

            return SelectedOverlayName is { } name ? [name] : [];
        }
    }

    public IReadOnlyList<string> AvailableOverlays
    {
        get => availableOverlays;
        private set
        {
            if (SetField(ref availableOverlays, value))
            {
                OnPropertyChanged(nameof(VisibleAdjustmentOverlays));
            }
        }
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
            string normalized = string.IsNullOrWhiteSpace(value) ? DefaultMode : value.Trim();
            if (SetField(ref selectedMode, normalized))
            {
                pendingAdjustments.Clear();
                LoadSelectedCalibration();
            }
        }
    }

    public string CurrentRuntimeMode => currentRuntimeMode ?? "No active vehicle or game mode";

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
            OnPropertyChanged(nameof(VisibleAdjustmentOverlays));
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
                if (!value)
                {
                    IsDragModeEnabled = false;
                }
                OnPropertyChanged(nameof(VisibleAdjustmentOverlays));
                RaiseCommandStates();
            }
        }
    }

    public bool IsDragModeEnabled
    {
        get => isDragModeEnabled;
        set
        {
            bool effective = value && IsAdjusting && HasSelection;
            if (!SetField(ref isDragModeEnabled, effective))
            {
                return;
            }

            if (IsAdjusting)
            {
                StatusMessage = effective
                    ? "Drag mode active: point at any visible preview panel, hold trigger to drag, and use the thumbstick to adjust depth."
                    : "Drag mode off.";
            }
        }
    }

    public double Scale
    {
        get => scale;
        set
        {
            if (SetField(ref scale, Math.Clamp(value, 0.1, 50)))
            {
                RecordPendingAdjustment();
            }
        }
    }

    public double PositionX
    {
        get => positionX;
        set
        {
            if (SetField(ref positionX, value))
            {
                RecordPendingAdjustment();
            }
        }
    }

    public double PositionY
    {
        get => positionY;
        set
        {
            if (SetField(ref positionY, value))
            {
                RecordPendingAdjustment();
            }
        }
    }

    public double PositionZ
    {
        get => positionZ;
        set
        {
            if (SetField(ref positionZ, value))
            {
                RecordPendingAdjustment();
            }
        }
    }

    public double RotationPitch
    {
        get => rotationPitch;
        set
        {
            if (SetField(ref rotationPitch, value))
            {
                RecordPendingAdjustment();
            }
        }
    }

    public double RotationYaw
    {
        get => rotationYaw;
        set
        {
            if (SetField(ref rotationYaw, value))
            {
                RecordPendingAdjustment();
            }
        }
    }

    public double RotationRoll
    {
        get => rotationRoll;
        set
        {
            if (SetField(ref rotationRoll, value))
            {
                RecordPendingAdjustment();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public ICommand SaveCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RefreshConnectionCommand { get; }

    public ICommand PlaceInFrontCommand { get; }

    public ICommand PreviousCategoryCommand { get; }
    public ICommand NextCategoryCommand { get; }
    public ICommand PreviousOverlayCommand { get; }
    public ICommand NextOverlayCommand { get; }
    public ICommand PreviousModeCommand { get; }
    public ICommand NextModeCommand { get; }

    private void CycleCategory(int direction)
    {
        if (AvailableCategories.Count == 0)
        {
            return;
        }

        int index = AvailableCategories.ToList().IndexOf(SelectedCategory);
        SelectedCategory = AvailableCategories[
            (index + direction + AvailableCategories.Count) % AvailableCategories.Count
        ];
    }

    private void CycleOverlay(int direction)
    {
        int index = AvailableOverlays.ToList().IndexOf(SelectedOverlayName!);
        SelectedOverlayName = AvailableOverlays[
            (index + direction + AvailableOverlays.Count) % AvailableOverlays.Count
        ];
    }

    private void CycleMode(int direction)
    {
        int index = AvailableModes.ToList().IndexOf(SelectedMode);
        SelectedMode = AvailableModes[(index + direction + AvailableModes.Count) % AvailableModes.Count];
    }

    public IReadOnlyList<string> AdjustmentSteps { get; } = ["Fine", "Normal", "Coarse"];

    public int AdjustmentStepIndex
    {
        get => adjustmentStepIndex;
        set
        {
            if (SetField(ref adjustmentStepIndex, Math.Clamp(value, 0, 2)))
            {
                OnPropertyChanged(nameof(PositionIncrement));
                OnPropertyChanged(nameof(RotationIncrement));
                OnPropertyChanged(nameof(ScaleIncrement));
            }
        }
    }

    private decimal StepMultiplier =>
        AdjustmentStepIndex switch
        {
            0 => 0.1m,
            2 => 5m,
            _ => 1m,
        };

    public decimal PositionIncrement => 0.5m * StepMultiplier;

    public decimal RotationIncrement => StepMultiplier;

    public decimal ScaleIncrement => 0.1m * StepMultiplier;

    public void PlaceInFront(VrOverlayHeadPose pose)
    {
        ApplyCalibration(VrOverlayTransform.PlaceInFront(CreateCalibration(), pose));
        StatusMessage = "Placed the selected panel one metre in front of you. Save to keep this placement.";
    }

    public void ApplyCalibrationEdit(VrOverlayCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ApplyCalibration(calibration);
    }

    public void ReportAdjustmentStatus(string message) => StatusMessage = message;

    public bool BeginAdjustment()
    {
        pendingAdjustments.Clear();
        SetAvailableOverlays(catalog.Defaults.Keys.Where(OverlayRuntimePresentationFactory.IsSupported));
        RefreshAvailableModes();
        if (AvailableOverlays.Count == 0)
        {
            StatusMessage = "No valid VR calibrations are available to adjust.";
            return false;
        }

        SelectedOverlayName ??= AvailableOverlays[0];
        StatusMessage =
            "Sample preview is active. Use the controller-interaction shortcut to operate the headset controls. Save keeps the selected panel and mode.";
        IsAdjusting = true;
        return true;
    }

    public VrOverlayCalibration? GetCalibration(string plotterName, string? mode = null)
    {
        if (IsAdjusting && string.Equals(SelectedOverlayName, plotterName, StringComparison.Ordinal))
        {
            return CreateCalibration();
        }
        if (IsAdjusting && pendingAdjustments.TryGetValue(plotterName, out VrOverlayCalibration? pending))
        {
            return pending;
        }

        return catalog.Resolve(plotterName, mode);
    }

    public void SetAvailableOverlays(IEnumerable<string> plotterNames)
    {
        ArgumentNullException.ThrowIfNull(plotterNames);
        allAvailableOverlays = plotterNames
            .Where(name => catalog.Defaults.ContainsKey(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var activeCategories = OverlayLayoutCatalog
            .Supported.Where(definition => allAvailableOverlays.Contains(definition.Name, StringComparer.Ordinal))
            .Select(definition => definition.Category)
            .ToHashSet();
        AvailableCategories = OverlayLayoutCatalog
            .Categories.Where(category => activeCategories.Contains(category.Category))
            .Select(category => category.DisplayName)
            .Prepend(AllCategoriesLabel)
            .ToArray();

        if (!AvailableCategories.Contains(selectedCategory, StringComparer.Ordinal))
        {
            selectedCategory = AllCategoriesLabel;
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged(nameof(IsCategoryPreviewActive));
        }

        ApplyCategoryFilter();
    }

    private void ApplyCategoryFilter()
    {
        string[] names = allAvailableOverlays.ToArray();
        if (IsCategoryPreviewActive)
        {
            OverlayLayoutCategoryDefinition? category = OverlayLayoutCatalog.Categories.FirstOrDefault(item =>
                string.Equals(item.DisplayName, selectedCategory, StringComparison.Ordinal)
            );
            if (category is not null)
            {
                var inCategory = OverlayLayoutCatalog
                    .ForCategory(category.Category)
                    .Select(item => item.Name)
                    .ToHashSet(StringComparer.Ordinal);
                names = names.Where(inCategory.Contains).ToArray();
            }
        }

        AvailableOverlays = names;
        if (SelectedOverlayName is not null && !names.Contains(SelectedOverlayName, StringComparer.Ordinal))
        {
            SelectedOverlayName = null;
        }

        SelectedOverlayName ??= names.FirstOrDefault();
    }

    public void SetCurrentRuntimeMode(string? mode)
    {
        string? normalized = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim();
        if (string.Equals(currentRuntimeMode, normalized, StringComparison.OrdinalIgnoreCase))
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

    public void SetConnectionStatus(VrOverlayConnectionState state, string headline, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ConnectionState = state;
        ConnectionHeadline = headline;
        ConnectionDetail = detail;
        SetRuntimeStatus(detail);
    }

    private void SaveCalibration()
    {
        if (SelectedOverlayName is null)
        {
            return;
        }

        var toSave = new Dictionary<string, VrOverlayCalibration>(pendingAdjustments, StringComparer.Ordinal)
        {
            [SelectedOverlayName] = CreateCalibration(),
        };
        var savedNames = new List<string>(toSave.Count);
        try
        {
            string? mode = SelectedMode == DefaultMode ? null : SelectedMode;
            VrOverlayCalibrationSaveResult? lastResult = null;
            foreach ((string plotterName, VrOverlayCalibration calibration) in toSave)
            {
                lastResult = calibrationStore.Save(plotterName, calibration, mode, SelectedPlatformProfile.Id);
                savedNames.Add(plotterName);
                pendingAdjustments.Remove(plotterName);
            }

            pendingAdjustments.Clear();
            catalog = calibrationStore.Load(SelectedPlatformProfile.Id);
            RefreshAvailableModes();
            CalibrationChanged?.Invoke(this, EventArgs.Empty);
            if (toSave.Count > 1)
            {
                StatusMessage = $"Saved {toSave.Count} overlays ({SelectedMode}).";
            }
            else
            {
                string backupSuffix = lastResult?.BackupPath is null
                    ? string.Empty
                    : $" Verified backup: {lastResult.BackupPath}";
                StatusMessage = $"Saved {SelectedOverlayName} ({SelectedMode}).{backupSuffix}";
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or JsonException
                        or FormatException
                        or OverflowException
            )
        {
            if (savedNames.Count > 0)
            {
                if (TryLoadCalibrations(SelectedPlatformProfile.Id))
                {
                    RefreshAvailableModes();
                    CalibrationChanged?.Invoke(this, EventArgs.Empty);
                }

                string[] unsaved = toSave
                    .Keys.Where(name => !savedNames.Contains(name, StringComparer.Ordinal))
                    .ToArray();
                StatusMessage =
                    $"Saved {savedNames.Count} of {toSave.Count} overlays; unsaved: {string.Join(", ", unsaved)} ({exception.Message})";
            }
            else
            {
                StatusMessage = $"VR calibration was not saved: {exception.Message}";
            }
        }
    }

    private void ResetCalibration()
    {
        if (SelectedOverlayName is null)
        {
            return;
        }

        IReadOnlyDictionary<string, VrOverlayCalibration> resetSource =
            SelectedMode == DefaultMode ? catalog.FactoryDefaults : catalog.Defaults;
        if (!resetSource.TryGetValue(SelectedOverlayName, out VrOverlayCalibration? factoryDefault))
        {
            StatusMessage = "No factory calibration exists for this overlay.";
            return;
        }

        try
        {
            VrOverlayCalibrationSaveResult result = calibrationStore.Save(
                SelectedOverlayName,
                factoryDefault,
                SelectedMode == DefaultMode ? null : SelectedMode,
                SelectedPlatformProfile.Id
            );
            pendingAdjustments.Remove(SelectedOverlayName);
            catalog = calibrationStore.Load(SelectedPlatformProfile.Id);
            RefreshAvailableModes();
            isLoadingSelection = true;
            try
            {
                ApplyCalibration(factoryDefault, notifyCalibrationChanged: false);
            }
            finally
            {
                isLoadingSelection = false;
            }

            CalibrationChanged?.Invoke(this, EventArgs.Empty);
            StatusMessage =
                $"Saved {SelectedOverlayName} ({SelectedMode})."
                + (result.BackupPath is null ? string.Empty : $" Verified backup: {result.BackupPath}");
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or JsonException
                        or FormatException
                        or OverflowException
            )
        {
            StatusMessage = $"VR calibration was not saved: {exception.Message}";
        }
    }

    private void CancelAdjustment()
    {
        pendingAdjustments.Clear();
        IsAdjusting = false;
        LoadSelectedCalibration();
        StatusMessage = preferences.Enabled ? "VR adjustment mode closed." : "VR overlays are disabled.";
    }

    private void RequestConnectionCheck()
    {
        string currentPlatformSupportMessage = CreatePlatformSupportMessage();
        if (!string.Equals(platformSupportMessage, currentPlatformSupportMessage, StringComparison.Ordinal))
        {
            platformSupportMessage = currentPlatformSupportMessage;
            OnPropertyChanged(nameof(PlatformSupportMessage));
        }

        ConnectionCheckRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LoadSelectedCalibration()
    {
        if (SelectedOverlayName is null)
        {
            return;
        }

        VrOverlayCalibration? calibration;
        if (IsAdjusting && pendingAdjustments.TryGetValue(SelectedOverlayName, out VrOverlayCalibration? pending))
        {
            calibration = pending;
        }
        else if (SelectedMode == DefaultMode)
        {
            calibration = catalog.Defaults.GetValueOrDefault(SelectedOverlayName);
        }
        else
        {
            calibration = catalog.Resolve(SelectedOverlayName, SelectedMode);
        }
        if (calibration is not null)
        {
            isLoadingSelection = true;
            try
            {
                ApplyCalibration(calibration, notifyCalibrationChanged: IsAdjusting);
            }
            finally
            {
                isLoadingSelection = false;
            }
        }
    }

    private void RecordPendingAdjustment()
    {
        if (
            IsAdjusting
            && IsCategoryPreviewActive
            && !isLoadingSelection
            && !IsApplyingCalibrationBatch
            && SelectedOverlayName is not null
        )
        {
            pendingAdjustments[SelectedOverlayName] = CreateCalibration();
        }
    }

    private void RefreshAvailableModes()
    {
        string[] modes = catalog
            .Overrides.Keys.Append(currentRuntimeMode)
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

    private void ApplyCalibration(VrOverlayCalibration calibration, bool notifyCalibrationChanged = true)
    {
        IsApplyingCalibrationBatch = true;
        try
        {
            Scale = calibration.Scale;
            PositionX = calibration.Position.X;
            PositionY = calibration.Position.Y;
            PositionZ = calibration.Position.Z;
            RotationPitch = calibration.Rotation.X;
            RotationYaw = calibration.Rotation.Y;
            RotationRoll = calibration.Rotation.Z;
        }
        finally
        {
            IsApplyingCalibrationBatch = false;
        }

        RecordPendingAdjustment();
        if (notifyCalibrationChanged && !IsChangingRoute)
        {
            CalibrationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private VrOverlayCalibration CreateCalibration()
    {
        return new VrOverlayCalibration(
            (float)Scale,
            new Vector3((float)PositionX, (float)PositionY, (float)PositionZ),
            new Vector3((float)RotationPitch, (float)RotationYaw, (float)RotationRoll)
        );
    }

    private RelayCommand CreateCalibrationCommand(Action action)
    {
        var command = new RelayCommand(action, () => HasSelection);
        calibrationCommands.Add(command);
        return command;
    }

    private void RaiseCommandStates()
    {
        saveCommand.RaiseCanExecuteChanged();
        resetCommand.RaiseCanExecuteChanged();
        cancelCommand.RaiseCanExecuteChanged();
        foreach (RelayCommand command in calibrationCommands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    private static VrOverlayCalibrationCatalog EmptyCatalog()
    {
        return new VrOverlayCalibrationCatalog(
            new Dictionary<string, VrOverlayCalibration>(StringComparer.Ordinal),
            new Dictionary<string, VrOverlayCalibration>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, VrOverlayCalibration>>(StringComparer.OrdinalIgnoreCase)
        );
    }

    private sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
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
