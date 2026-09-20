using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayInteractionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IOverlayPlatformService? platform;
    private readonly IGameWindowTracker? gameWindowTracker;
    private readonly LegacyOverlayLayoutStore? layoutStore;
    private readonly LegacyOverlayLayout? activeLayout;
    private readonly IOverlayPositionEditorHost? editorHost;
    private readonly OverlayWindowRegistry? registry;
    private readonly Dictionary<Window, LiveOverlayWindowState> liveWindows = [];
    private readonly HashSet<Window> interactiveWindows = [];
    private readonly DelegateCommand toggleCommand;
    private readonly DelegateCommand snapToCenterCommand;
    private readonly DelegateCommand saveCommand;
    private readonly DelegateCommand cancelCommand;
    private readonly DelegateCommand toggleCategoryMenuCommand;
    private readonly DelegateCommand selectCategoryCommand;
    private readonly DelegateCommand toggleTypographySettingsCommand;
    private readonly DelegateCommand resetTypographyCommand;
    private readonly DelegateCommand resetOverlaySizeCommand;
    private OverlayPositionEditSession? editSession;
    private OverlayPositionEditSession? liveEditSession;
    private IDisposable? cursorVisibilitySession;
    private OverlayLayoutCategoryDefinition selectedCategory;
    private bool isEditing;
    private bool isLiveInteractionEnabled;
    private PixelRect liveHostBounds;
    private bool disposed;
    private string statusMessage;
    private double globalOpacityPercent = 100d;
    private string? selectedOverlaySettingsPlotterName;
    private bool updatingSelectedOverlaySettings;
    private bool useGlobalOverlayOpacity = true;
    private double selectedOverlayOpacityPercent = 100d;
    private bool useGlobalOverlayScale = true;
    private double selectedOverlayScalePercent;
    private bool isCategoryMenuOpen;
    private bool isTypographySettingsOpen;

    public OverlayInteractionViewModel(OverlayPlatformCapabilities capabilities)
    {
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Categories = OverlayLayoutCatalog.Categories;
        selectedCategory = Categories[0];
        toggleCommand = new DelegateCommand(() => _ = Toggle(), () => IsAvailable);
        snapToCenterCommand = new DelegateCommand(SnapCurrentCategoryToCenter, () => IsEditing);
        saveCommand = new DelegateCommand(Save, () => IsEditing);
        cancelCommand = new DelegateCommand(Cancel, () => IsEditing);
        toggleCategoryMenuCommand = new DelegateCommand(ToggleCategoryMenu, () => IsEditing);
        selectCategoryCommand = new DelegateCommand(SelectCategory, () => IsEditing);
        toggleTypographySettingsCommand = new DelegateCommand(ToggleTypographySettings, () => IsOverlaySettingsOpen);
        resetTypographyCommand = new DelegateCommand(ResetTypography, () => IsOverlaySettingsOpen);
        resetOverlaySizeCommand = new DelegateCommand(ResetOverlaySize, () => IsOverlaySettingsOpen);
        ToggleCommand = toggleCommand;
        SnapToCenterCommand = snapToCenterCommand;
        SaveCommand = saveCommand;
        CancelCommand = cancelCommand;
        ToggleCategoryMenuCommand = toggleCategoryMenuCommand;
        SelectCategoryCommand = selectCategoryCommand;
        ToggleTypographySettingsCommand = toggleTypographySettingsCommand;
        ResetTypographyCommand = resetTypographyCommand;
        ResetOverlaySizeCommand = resetOverlaySizeCommand;
        TypographyRoles = CreateTypographyRoles();
        statusMessage = IsAvailable
            ? "Overlay position previews use an isolated simulated game state."
            : Capabilities.StatusText;
    }

    public OverlayInteractionViewModel(
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayoutStore layoutStore,
        LegacyOverlayLayout activeLayout,
        OverlayWindowRegistry? registry = null,
        IOverlayPositionEditorHost? editorHost = null
    )
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.layoutStore = layoutStore ?? throw new ArgumentNullException(nameof(layoutStore));
        this.activeLayout = activeLayout ?? throw new ArgumentNullException(nameof(activeLayout));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        this.editorHost = editorHost ?? new AvaloniaOverlayPositionEditorHost(platform, this.registry);
        Capabilities = platform.Capabilities;
        Categories = OverlayLayoutCatalog.Categories;
        selectedCategory = Categories[0];
        toggleCommand = new DelegateCommand(() => _ = Toggle(), () => IsAvailable);
        snapToCenterCommand = new DelegateCommand(SnapCurrentCategoryToCenter, () => IsEditing);
        saveCommand = new DelegateCommand(Save, () => IsEditing);
        cancelCommand = new DelegateCommand(Cancel, () => IsEditing);
        toggleCategoryMenuCommand = new DelegateCommand(ToggleCategoryMenu, () => IsEditing);
        selectCategoryCommand = new DelegateCommand(SelectCategory, () => IsEditing);
        toggleTypographySettingsCommand = new DelegateCommand(ToggleTypographySettings, () => IsOverlaySettingsOpen);
        resetTypographyCommand = new DelegateCommand(ResetTypography, () => IsOverlaySettingsOpen);
        resetOverlaySizeCommand = new DelegateCommand(ResetOverlaySize, () => IsOverlaySettingsOpen);
        ToggleCommand = toggleCommand;
        SnapToCenterCommand = snapToCenterCommand;
        SaveCommand = saveCommand;
        CancelCommand = cancelCommand;
        ToggleCategoryMenuCommand = toggleCategoryMenuCommand;
        SelectCategoryCommand = selectCategoryCommand;
        ToggleTypographySettingsCommand = toggleTypographySettingsCommand;
        ResetTypographyCommand = resetTypographyCommand;
        ResetOverlaySizeCommand = resetOverlaySizeCommand;
        TypographyRoles = CreateTypographyRoles();
        statusMessage = IsAvailable
            ? "Choose Edit Overlay Positions to load categorized previews from an isolated simulated game state. Elite does not need to be running."
            : Capabilities.StatusText;
        this.editorHost.PreviewMoved += OnPreviewMoved;
        this.editorHost.PreviewSizeChanged += OnPreviewSizeChanged;
        this.editorHost.Closed += OnEditorClosed;
        this.activeLayout.ScaleIndexChanged += OnOverlayScaleIndexChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public MiningDetectionViewModel? MiningDetection { get; set; }

    public OverlayPlatformCapabilities Capabilities { get; }

    public IReadOnlyList<OverlayLayoutCategoryDefinition> Categories { get; }

    public OverlayLayoutCategoryDefinition SelectedCategory
    {
        get => selectedCategory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            IsCategoryMenuOpen = false;
            if (!SetField(ref selectedCategory, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ModeLabel));
            if (IsEditing && editSession is not null)
            {
                CloseOverlaySettings();
                editorHost?.ShowCategory(editSession, value.Category);
                StatusMessage =
                    $"Showing {value.DisplayName} with simulated game data. Drag the previews, then use ✓ to save or ✕ to cancel.";
            }
        }
    }

    public bool IsAvailable =>
        platform is not null
        && Capabilities.SupportsPassiveOverlay
        && Capabilities.SupportsClickThrough
        && Capabilities.SupportsGameWindowTracking;

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (!SetField(ref isEditing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ModeLabel));
            OnPropertyChanged(nameof(ToggleButtonText));
            toggleCommand.RaiseCanExecuteChanged();
            snapToCenterCommand.RaiseCanExecuteChanged();
            saveCommand.RaiseCanExecuteChanged();
            cancelCommand.RaiseCanExecuteChanged();
            toggleCategoryMenuCommand.RaiseCanExecuteChanged();
            selectCategoryCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsLiveInteractionEnabled
    {
        get => isLiveInteractionEnabled;
        private set
        {
            if (SetField(ref isLiveInteractionEnabled, value))
            {
                OnPropertyChanged(nameof(ModeLabel));
            }
        }
    }

    public string ModeLabel
    {
        get
        {
            if (IsEditing)
            {
                return IsLiveInteractionEnabled
                    ? $"Editing {SelectedCategory.DisplayName} with live overlays"
                    : $"Editing {SelectedCategory.DisplayName}";
            }

            return IsLiveInteractionEnabled
                ? "Visible live overlays are clickable and can be dragged. Use the shortcut again to save and restore click-through mode."
                : "Open categorized previews without starting Elite. Changes are saved only with ✓.";
        }
    }

    public string ToggleButtonText => IsEditing ? "Cancel Position Editing" : "Edit Overlay Positions";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public double GlobalOpacityPercent
    {
        get => globalOpacityPercent;
        set
        {
            double normalized = Math.Clamp(value, 0, 100);
            if (!SetField(ref globalOpacityPercent, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(GlobalOpacityLabel));
            if (!IsEditing || editSession is null || !editSession.SetDefaultOpacity(normalized / 100d))
            {
                return;
            }

            editorHost?.RefreshPreviewOpacities(editSession);
            if (IsOverlaySettingsOpen && UseGlobalOverlayOpacity)
            {
                selectedOverlayOpacityPercent = normalized;
                OnPropertyChanged(nameof(SelectedOverlayOpacityPercent));
                OnPropertyChanged(nameof(SelectedOverlayOpacityLabel));
            }

            StatusMessage =
                $"Global overlay opacity set to {normalized:N0}%. Use ✓ to save all changes or × to cancel them.";
        }
    }

    public string GlobalOpacityLabel => $"{GlobalOpacityPercent:N0}%";

    public bool IsOverlaySettingsOpen => selectedOverlaySettingsPlotterName is not null;

    public bool IsCategoryMenuOpen
    {
        get => isCategoryMenuOpen;
        private set => SetField(ref isCategoryMenuOpen, value);
    }

    public bool IsTypographySettingsOpen
    {
        get => isTypographySettingsOpen;
        set => SetField(ref isTypographySettingsOpen, value);
    }

    public IReadOnlyList<OverlayTypographyRoleViewModel> TypographyRoles { get; }

    public string SelectedOverlaySettingsTitle
    {
        get
        {
            if (selectedOverlaySettingsPlotterName is null)
            {
                return "Overlay appearance";
            }

            OverlayLayoutDefinition definition = OverlayLayoutCatalog.Supported.First(candidate =>
                candidate.Name == selectedOverlaySettingsPlotterName
            );
            return $"{definition.DisplayName} appearance";
        }
    }

    public bool UseGlobalOverlayOpacity
    {
        get => useGlobalOverlayOpacity;
        set
        {
            if (!SetField(ref useGlobalOverlayOpacity, value) || updatingSelectedOverlaySettings)
            {
                return;
            }

            SetSelectedOverlayOpacity(value ? null : SelectedOverlayOpacityPercent / 100d);
        }
    }

    public double SelectedOverlayOpacityPercent
    {
        get => selectedOverlayOpacityPercent;
        set
        {
            double normalized = Math.Clamp(value, 0, 100);
            if (!SetField(ref selectedOverlayOpacityPercent, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedOverlayOpacityLabel));
            if (!updatingSelectedOverlaySettings && !UseGlobalOverlayOpacity)
            {
                SetSelectedOverlayOpacity(normalized / 100d);
            }
        }
    }

    public string SelectedOverlayOpacityLabel => $"{SelectedOverlayOpacityPercent:N0}%";

    public bool UseGlobalOverlayScale
    {
        get => useGlobalOverlayScale;
        set
        {
            if (!SetField(ref useGlobalOverlayScale, value) || updatingSelectedOverlaySettings)
            {
                return;
            }

            SetSelectedOverlayScale(
                value ? null : OverlayScaleCatalog.GetIndex((int)Math.Round(SelectedOverlayScalePercent))
            );
        }
    }

    public double SelectedOverlayScalePercent
    {
        get => selectedOverlayScalePercent;
        set
        {
            int normalized = OverlayScaleCatalog.NormalizePercent(value);
            if (!SetField(ref selectedOverlayScalePercent, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedOverlayScaleLabel));
            if (!updatingSelectedOverlaySettings && !UseGlobalOverlayScale)
            {
                SetSelectedOverlayScale(OverlayScaleCatalog.GetIndex(normalized));
            }
        }
    }

    public string SelectedOverlayScaleLabel
    {
        get
        {
            int percent =
                UseGlobalOverlayScale && editSession is not null
                    ? OverlayScaleCatalog.GetPercent(editSession.ScaleIndex)
                    : (int)SelectedOverlayScalePercent;
            return OverlayScaleCatalog.FormatPercent(percent);
        }
    }

    public ICommand ToggleCommand { get; }

    public ICommand SnapToCenterCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ToggleCategoryMenuCommand { get; }

    public ICommand SelectCategoryCommand { get; }

    public ICommand ToggleTypographySettingsCommand { get; }

    public ICommand ResetTypographyCommand { get; }

    public ICommand ResetOverlaySizeCommand { get; }

    public void OpenOverlaySettings(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (!IsEditing || editSession is null)
        {
            return;
        }

        IsCategoryMenuOpen = false;

        OverlayLayoutDefinition? definition =
            OverlayLayoutCatalog.Supported.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, plotterName, StringComparison.Ordinal)
            )
            ?? throw new ArgumentOutOfRangeException(
                nameof(plotterName),
                plotterName,
                "The overlay is not available in the position editor."
            );
        LegacyOverlayPlacement placement = editSession.GetPlacement(plotterName);
        updatingSelectedOverlaySettings = true;
        selectedOverlaySettingsPlotterName = plotterName;
        useGlobalOverlayOpacity = placement.Opacity is null;
        selectedOverlayOpacityPercent = editSession.GetOpacity(plotterName) * 100d;
        useGlobalOverlayScale = placement.ScaleIndex is null;
        selectedOverlayScalePercent = OverlayScaleCatalog.GetPercent(placement.ScaleIndex ?? editSession.ScaleIndex);
        OverlayTypographyScale typographyScale = editSession.GetTypographyScale(plotterName);
        foreach (OverlayTypographyRoleViewModel role in TypographyRoles)
        {
            role.Load(typographyScale.GetPercent(role.Role));
        }
        updatingSelectedOverlaySettings = false;
        OnPropertyChanged(nameof(IsOverlaySettingsOpen));
        OnPropertyChanged(nameof(SelectedOverlaySettingsTitle));
        OnPropertyChanged(nameof(UseGlobalOverlayOpacity));
        OnPropertyChanged(nameof(SelectedOverlayOpacityPercent));
        OnPropertyChanged(nameof(SelectedOverlayOpacityLabel));
        OnPropertyChanged(nameof(UseGlobalOverlayScale));
        OnPropertyChanged(nameof(SelectedOverlayScalePercent));
        OnPropertyChanged(nameof(SelectedOverlayScaleLabel));
        toggleTypographySettingsCommand.RaiseCanExecuteChanged();
        resetTypographyCommand.RaiseCanExecuteChanged();
        resetOverlaySizeCommand.RaiseCanExecuteChanged();
        StatusMessage = $"Editing {definition.DisplayName}. Use the top ✓ to save all changes and close the editor.";
    }

    public bool Toggle()
    {
        if (IsEditing)
        {
            Cancel();
            return true;
        }

        return Begin();
    }

    private void SnapCurrentCategoryToCenter()
    {
        if (!IsEditing || editSession is null)
        {
            return;
        }

        CloseOverlaySettings();
        IReadOnlyList<OverlayLayoutDefinition> definitions = OverlayLayoutCatalog.ForCategory(
            SelectedCategory.Category
        );
        int snappedCount = editorHost?.SnapPreviewsToCenter(editSession) ?? 0;
        foreach (OverlayLayoutDefinition definition in definitions)
        {
            SynchronizeLiveOverlayFromPreview(definition.Name);
        }

        StatusMessage =
            $"Snapped {snappedCount:N0} {SelectedCategory.DisplayName} overlay(s) to the center. Rearrange them, then use ✓ to save or × to cancel.";
    }

    public bool ToggleLiveOverlayInteraction()
    {
        if (IsLiveInteractionEnabled)
        {
            EndLiveInteraction(saveChanges: true);
            return true;
        }

        return BeginLiveInteraction();
    }

    public bool Begin()
    {
        if (!IsAvailable || disposed)
        {
            StatusMessage = Capabilities.StatusText;
            return false;
        }

        if (editorHost is null || gameWindowTracker is null || activeLayout is null)
        {
            StatusMessage = "The overlay position editor is not available in this runtime.";
            return false;
        }

        if (IsLiveInteractionEnabled)
        {
            if (!PersistPendingLivePositionsForEditor())
            {
                return false;
            }
        }
        else if (!ReloadPersistedLayout("Overlay positions cannot be edited"))
        {
            return false;
        }

        if (activeLayout.Error is not null)
        {
            StatusMessage =
                "Overlay positions cannot be edited until the existing layout error is corrected: "
                + activeLayout.Error;
            return false;
        }

        var session = new OverlayPositionEditSession(activeLayout);
        MiningDetection?.BeginEdit();
        editSession = session;
        globalOpacityPercent = session.DefaultOpacity * 100d;
        OnPropertyChanged(nameof(GlobalOpacityPercent));
        OnPropertyChanged(nameof(GlobalOpacityLabel));
        IsEditing = true;
        GameWindowSnapshot game = gameWindowTracker.GetSnapshot();
        PixelRect? preferredBounds = game.IsAvailable ? game.ClientBounds : (PixelRect?)null;
        StatusMessage =
            $"Showing {SelectedCategory.DisplayName} with simulated game data. Drag the previews, then use ✓ to save or ✕ to cancel.";
        if (editorHost.Open(this, session, SelectedCategory.Category, preferredBounds))
        {
            return true;
        }

        editSession = null;
        IsEditing = false;
        StatusMessage = "The overlay position editor could not find a usable display.";
        MiningDetection?.EndEdit();
        return false;
    }

    public void Save()
    {
        if (!IsEditing || editSession is null || layoutStore is null || activeLayout is null)
        {
            return;
        }

        IReadOnlyDictionary<string, LegacyOverlayPlacement> changes = editSession.Changes;
        bool saveDefaultOpacity = editSession.HasDefaultOpacityChange;
        if (changes.Count == 0 && !saveDefaultOpacity && MiningDetection?.HasCalibrationChanges != true)
        {
            EndSession(closeHost: true, restoreRuntimeWindows: true);
            StatusMessage = "Overlay editing closed; no positions or opacity values changed.";
            return;
        }

        try
        {
            bool saveMiningCalibration = MiningDetection?.HasCalibrationChanges == true;
            if (changes.Count == 0 && !saveDefaultOpacity)
            {
                // The no-change guard above leaves this path only for a pending calibration.
                MiningDetection!.SaveEdit();
                EndSession(closeHost: true, restoreRuntimeWindows: true);
                StatusMessage = "Saved mining HUD calibration.";
                return;
            }
            LegacyOverlayLayoutSaveResult result = layoutStore.Save(
                changes,
                editSession.DefaultOpacity,
                saveDefaultOpacity
            );
            LegacyOverlayLayout updated = layoutStore.Load();
            if (updated.Error is not null)
            {
                throw new InvalidDataException(updated.Error);
            }

            MiningDetection?.SaveEdit();
            activeLayout.ReplaceWith(updated);
            ApplySavedLayoutToRuntimeWindows();

            EndSession(closeHost: true, restoreRuntimeWindows: true);
            StatusMessage =
                $"Saved {result.UpdatedPlacementCount:N0} overlay position and appearance override(s)"
                + (saveDefaultOpacity ? " and the global opacity." : ".")
                + (saveMiningCalibration ? " Saved mining HUD calibration." : string.Empty)
                + (result.BackupPath is null ? string.Empty : $" Previous layout backup: {result.BackupPath}");
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            StatusMessage = "Overlay positions and appearance were not saved: " + exception.Message;
        }
    }

    public void Cancel()
    {
        if (!IsEditing)
        {
            return;
        }

        EndSession(closeHost: true, restoreRuntimeWindows: true);
        StatusMessage = "Overlay position, opacity, and scale changes were cancelled.";
    }

    private bool BeginLiveInteraction()
    {
        if (
            !IsAvailable
            || disposed
            || platform is null
            || gameWindowTracker is null
            || activeLayout is null
            || registry is null
        )
        {
            StatusMessage = Capabilities.StatusText;
            return false;
        }

        if (!IsEditing && !ReloadPersistedLayout("Live overlay positions cannot be edited"))
        {
            return false;
        }

        if (activeLayout.Error is not null)
        {
            StatusMessage =
                "Live overlay positions cannot be edited until the existing layout error is corrected: "
                + activeLayout.Error;
            return false;
        }

        GameWindowSnapshot game = gameWindowTracker.GetSnapshot();
        if (!game.IsAvailable || game.ClientBounds is not { Width: > 0, Height: > 0 })
        {
            StatusMessage =
                "No tracked Elite window is available. Use Edit Overlay Positions for offline layout changes.";
            return false;
        }

        liveHostBounds = game.ClientBounds;
        liveEditSession = new OverlayPositionEditSession(activeLayout);
        string lastStatus = "No registered live overlay accepted interactive mode.";
        foreach (RegisteredOverlayWindow registered in registry.Snapshot())
        {
            OverlayInteractionResult result = platform.SetInteractive(registered.Window, interactive: true);
            lastStatus = result.Status;
            if (!result.IsPrepared || !result.IsInteractive)
            {
                continue;
            }

            interactiveWindows.Add(registered.Window);
            if (registered.ParticipatesInPlacement)
            {
                AttachLiveWindow(registered);
            }
        }

        if (liveWindows.Count == 0)
        {
            List<string> failures = DetachAndRestoreClickThrough();
            liveEditSession = null;
            StatusMessage = "No live overlays could be made clickable. " + lastStatus + FormatFailureSuffix(failures);
            return false;
        }

        cursorVisibilitySession = platform.BeginVisibleCursorSession(liveWindows.Keys.First());
        IsLiveInteractionEnabled = true;
        if (IsEditing)
        {
            editorHost?.SetRuntimeOverlaysVisibleDuringEditing(true);
        }

        StatusMessage =
            $"{liveWindows.Count:N0} live overlay(s) are clickable. Drag them into place, then use the shortcut again to save.";
        return true;
    }

    private void EndLiveInteraction(bool saveChanges)
    {
        OverlayPositionEditSession? session = liveEditSession;
        IReadOnlyDictionary<string, LegacyOverlayPlacement> changes =
            session?.Changes ?? new Dictionary<string, LegacyOverlayPlacement>();
        List<string> failures;
        try
        {
            failures = DetachAndRestoreClickThrough();
        }
        finally
        {
            cursorVisibilitySession?.Dispose();
            cursorVisibilitySession = null;
        }
        liveEditSession = null;
        IsLiveInteractionEnabled = false;
        if (IsEditing)
        {
            editorHost?.SetRuntimeOverlaysVisibleDuringEditing(false);
        }

        if (!saveChanges)
        {
            RestoreLivePlacements(session, changes.Keys);
            StatusMessage = GetUnsavedInteractionStatus(changes.Count, failures);
            return;
        }

        if (changes.Count == 0)
        {
            StatusMessage = GetNoChangeInteractionStatus(failures);
            return;
        }

        SaveLiveInteractionChanges(session, changes, failures);
    }

    private List<string> DetachAndRestoreClickThrough()
    {
        var failures = new List<string>();
        foreach (Window? window in interactiveWindows.ToArray())
        {
            DetachLiveWindow(window);
            interactiveWindows.Remove(window);
            if (platform is null)
            {
                continue;
            }

            OverlayInteractionResult result = platform.SetInteractive(window, interactive: false);
            if (!result.IsPrepared || result.IsInteractive)
            {
                failures.Add(result.Status);
            }
        }

        return failures;
    }

    private void SaveLiveInteractionChanges(
        OverlayPositionEditSession? session,
        IReadOnlyDictionary<string, LegacyOverlayPlacement> changes,
        List<string> failures
    )
    {
        try
        {
            if (layoutStore is null || activeLayout is null)
            {
                throw new InvalidOperationException("The overlay layout store is unavailable.");
            }

            LegacyOverlayLayoutSaveResult result = PersistLivePositions(changes);
            StatusMessage =
                $"Saved {result.UpdatedPlacementCount:N0} live overlay position(s) and restored click-through mode."
                + FormatFailureSuffix(failures);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            RestoreLivePlacements(session, changes.Keys);
            StatusMessage =
                "Live overlays returned to click-through mode, but their moved positions were not saved: "
                + exception.Message;
        }
    }

    private bool PersistPendingLivePositionsForEditor()
    {
        IReadOnlyDictionary<string, LegacyOverlayPlacement>? changes = liveEditSession?.Changes;
        if (changes is null || changes.Count == 0)
        {
            return true;
        }

        try
        {
            _ = PersistLivePositions(changes);
            // Continue live interaction from the layout now shared by disk,
            // runtime overlays, and the editor. Rebasing prevents the same
            // placements from remaining pending after the editor opens.
            LegacyOverlayLayout synchronizedLayout =
                activeLayout ?? throw new InvalidOperationException("The active overlay layout is unavailable.");
            liveEditSession = new OverlayPositionEditSession(synchronizedLayout);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            StatusMessage =
                "Overlay positions cannot be edited because pending live positions could not be synchronized: "
                + exception.Message;
            return false;
        }
    }

    private LegacyOverlayLayoutSaveResult PersistLivePositions(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> changes
    )
    {
        if (layoutStore is null || activeLayout is null)
        {
            throw new InvalidOperationException("The overlay layout store is unavailable.");
        }

        LegacyOverlayLayoutSaveResult result = layoutStore.Save(changes);
        LegacyOverlayLayout updated = layoutStore.Load();
        if (updated.Error is not null)
        {
            throw new InvalidDataException(updated.Error);
        }

        activeLayout.ReplaceWith(updated);
        return result;
    }

    private void ApplySavedLayoutToRuntimeWindows()
    {
        if (registry is null || activeLayout is null)
        {
            return;
        }

        GameWindowSnapshot? game = gameWindowTracker?.GetSnapshot();
        foreach (RegisteredOverlayWindow registered in registry.Snapshot())
        {
            OverlayThemeResources.ApplyScale(registered.Window, activeLayout, registered.PlotterName);
            if (
                !registered.ParticipatesInPlacement
                || game is not { IsAvailable: true, ClientBounds: { Width: > 0, Height: > 0 } }
            )
            {
                continue;
            }

            PixelPoint? position = activeLayout.GetPosition(
                registered.PlotterName,
                game.ClientBounds,
                OverlayWindowMetrics.GetPixelSize(registered)
            );
            if (position is { } savedPosition && registered.Window.Position != savedPosition)
            {
                registered.Window.Position = savedPosition;
            }
        }
    }

    private static string GetNoChangeInteractionStatus(List<string> failures)
    {
        return failures.Count == 0
            ? "Live overlays returned to click-through mode; no positions moved."
            : "Live overlay interaction ended, but one or more windows could not be restored: "
                + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
    }

    private static string FormatFailureSuffix(List<string> failures)
    {
        if (failures.Count == 0)
        {
            return string.Empty;
        }

        return " One or more windows reported a click-through restore warning: "
            + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
    }

    private static string GetUnsavedInteractionStatus(int changedPlacementCount, List<string> failures)
    {
        if (failures.Count > 0)
        {
            return "Live overlay interaction ended, but one or more windows could not be restored: "
                + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return changedPlacementCount == 0
            ? "Live overlays returned to click-through mode; no positions moved."
            : "Live overlays returned to click-through mode; moved positions were restored without saving.";
    }

    private void AttachLiveWindow(RegisteredOverlayWindow registered)
    {
        if (liveWindows.ContainsKey(registered.Window))
        {
            return;
        }

        EventHandler<PointerPressedEventArgs> pointerPressed = (_, eventArgs) =>
            OnLiveWindowPointerPressed(registered.Window, eventArgs);
        EventHandler<PixelPointEventArgs> positionChanged = (_, eventArgs) =>
            OnLiveWindowPositionChanged(registered, eventArgs.Point);
        EventHandler closed = (_, _) => DetachLiveWindow(registered.Window);
        var state = new LiveOverlayWindowState(
            registered.Window,
            registered.PlotterName,
            pointerPressed,
            positionChanged,
            closed
        );
        liveWindows.Add(registered.Window, state);
        registered.Window.PointerPressed += pointerPressed;
        registered.Window.PositionChanged += positionChanged;
        registered.Window.Closed += closed;
    }

    private void DetachLiveWindow(Window window)
    {
        if (!liveWindows.Remove(window, out LiveOverlayWindowState? state))
        {
            return;
        }

        window.PointerPressed -= state.PointerPressed;
        window.PositionChanged -= state.PositionChanged;
        window.Closed -= state.Closed;
    }

    private void OnLiveWindowPointerPressed(Window window, PointerPressedEventArgs eventArgs)
    {
        if (!IsLiveInteractionEnabled || !eventArgs.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
        {
            return;
        }

        platform?.BeginMoveDrag(window, eventArgs);
        eventArgs.Handled = true;
    }

    private void OnLiveWindowPositionChanged(RegisteredOverlayWindow registered, PixelPoint position)
    {
        if (!IsLiveInteractionEnabled || liveEditSession is null)
        {
            return;
        }

        PixelSize size = OverlayWindowMetrics.GetPixelSize(registered);
        if (
            activeLayout is null
            || !MoveLiveOverlay(
                liveEditSession,
                activeLayout,
                registered.PlotterName,
                position,
                size,
                liveHostBounds,
                IsEditing ? editSession : null
            )
        )
        {
            return;
        }

        if (IsEditing && editSession is not null)
        {
            editorHost?.RefreshPreviewPositions(editSession);
        }

        string name =
            OverlayLayoutCatalog
                .Supported.FirstOrDefault(definition =>
                    string.Equals(definition.Name, registered.PlotterName, StringComparison.Ordinal)
                )
                ?.DisplayName
            ?? registered.PlotterName;
        StatusMessage = $"Moved live overlay {name}. Use the shortcut again to save and restore click-through mode.";
    }

    internal static bool MoveLiveOverlay(
        OverlayPositionEditSession session,
        LegacyOverlayLayout activeLayout,
        string plotterName,
        PixelPoint position,
        PixelSize overlaySize,
        PixelRect hostBounds,
        OverlayPositionEditSession? previewSession = null
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(activeLayout);
        if (!session.Move(plotterName, position, overlaySize, hostBounds))
        {
            return false;
        }

        LegacyOverlayPlacement placement = session.GetPlacement(plotterName);
        activeLayout.SetPlacement(plotterName, placement);
        previewSession?.SetPlacement(plotterName, placement);
        return true;
    }

    private void RestoreLivePlacements(OverlayPositionEditSession? session, IEnumerable<string> plotterNames)
    {
        if (session is null || activeLayout is null)
        {
            return;
        }

        foreach (string plotterName in plotterNames)
        {
            LegacyOverlayPlacement original = session.GetOriginalPlacement(plotterName);
            activeLayout.SetPlacement(plotterName, original);
            if (IsEditing && editSession is not null)
            {
                editSession.SetPlacement(plotterName, original);
            }
        }

        if (IsEditing && editSession is not null)
        {
            editorHost?.RefreshPreviewPositions(editSession);
        }
    }

    private bool ReloadPersistedLayout(string errorPrefix)
    {
        if (layoutStore is null || activeLayout is null)
        {
            StatusMessage = errorPrefix + " because the overlay layout store is unavailable.";
            return false;
        }

        LegacyOverlayLayout updated = layoutStore.Load();
        if (updated.Error is not null)
        {
            StatusMessage = errorPrefix + ": " + updated.Error;
            return false;
        }

        activeLayout.ReplaceWith(updated);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (IsLiveInteractionEnabled)
        {
            EndLiveInteraction(saveChanges: false);
        }

        if (editorHost is not null)
        {
            editorHost.PreviewMoved -= OnPreviewMoved;
            editorHost.PreviewSizeChanged -= OnPreviewSizeChanged;
            editorHost.Closed -= OnEditorClosed;
            editorHost.Close(restoreRuntimeWindows: false);
            editorHost.Dispose();
        }

        activeLayout?.ScaleIndexChanged -= OnOverlayScaleIndexChanged;

        editSession = null;
        IsEditing = false;
        gameWindowTracker?.Dispose();
        platform?.Dispose();
    }

    internal static LegacyOverlayPlacement CreatePlacement(
        LegacyOverlayPlacement original,
        PixelPoint position,
        PixelSize overlaySize,
        PixelRect gameBounds
    )
    {
        ArgumentNullException.ThrowIfNull(original);
        int horizontalOffset = original.Horizontal switch
        {
            LegacyHorizontalAnchor.Left => position.X - gameBounds.X,
            LegacyHorizontalAnchor.Center => position.X - (gameBounds.X + ((gameBounds.Width - overlaySize.Width) / 2)),
            LegacyHorizontalAnchor.Right => gameBounds.Right - overlaySize.Width - position.X,
            _ => position.X,
        };
        int verticalOffset = original.Vertical switch
        {
            LegacyVerticalAnchor.Top => position.Y - gameBounds.Y,
            LegacyVerticalAnchor.Middle => position.Y - (gameBounds.Y + ((gameBounds.Height - overlaySize.Height) / 2)),
            LegacyVerticalAnchor.Bottom => gameBounds.Bottom - overlaySize.Height - position.Y,
            _ => position.Y,
        };
        return original with
        {
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
            PositionReference = new OverlayPositionReference(gameBounds.Width, gameBounds.Height),
        };
    }

    private void OnPreviewMoved(object? sender, OverlayPreviewMovedEventArgs eventArgs)
    {
        if (!IsEditing || editSession is null)
        {
            return;
        }

        if (!editSession.Move(eventArgs.PlotterName, eventArgs.Position, eventArgs.PreviewSize, eventArgs.HostBounds))
        {
            return;
        }

        SynchronizeLiveOverlayFromPreview(eventArgs.PlotterName);
        string displayName = OverlayLayoutCatalog
            .Supported.First(definition =>
                string.Equals(definition.Name, eventArgs.PlotterName, StringComparison.Ordinal)
            )
            .DisplayName;
        StatusMessage = $"Moved {displayName}. Use ✓ to save all changes or ✕ to cancel them.";
    }

    private void OnPreviewSizeChanged(object? sender, OverlayPreviewSizeChangedEventArgs eventArgs)
    {
        if (!IsEditing || editSession is null || !editSession.SetSizeOverride(eventArgs.PlotterName, eventArgs.Size))
        {
            return;
        }

        SynchronizeLiveOverlayFromPreview(eventArgs.PlotterName);
        string displayName = OverlayLayoutCatalog.GetRequired(eventArgs.PlotterName).DisplayName;
        StatusMessage =
            $"Resized {displayName} to {eventArgs.Size.Width:N0} × {eventArgs.Size.Height:N0}. Use ✓ to save all changes or ✕ to cancel them.";
    }

    private void SynchronizeLiveOverlayFromPreview(string plotterName)
    {
        if (
            !IsLiveInteractionEnabled
            || editSession is null
            || liveEditSession is null
            || activeLayout is null
            || registry is null
        )
        {
            return;
        }

        LegacyOverlayPlacement placement = editSession.GetPlacement(plotterName);
        liveEditSession.SetPlacement(plotterName, placement);
        activeLayout.SetPlacement(plotterName, placement);
        RegisteredOverlayWindow? registered = registry
            .Snapshot()
            .FirstOrDefault(candidate =>
                candidate.ParticipatesInPlacement
                && string.Equals(candidate.PlotterName, plotterName, StringComparison.Ordinal)
            );
        if (registered is null)
        {
            return;
        }

        PixelPoint? runtimePosition = activeLayout.GetPosition(
            plotterName,
            liveHostBounds,
            OverlayWindowMetrics.GetPixelSize(registered)
        );
        if (runtimePosition is { } position)
        {
            registered.Window.Position = position;
        }
    }

    private void OnOverlayScaleIndexChanged(object? sender, EventArgs eventArgs)
    {
        if (!IsEditing || editSession is null || activeLayout is null)
        {
            return;
        }

        editSession.SetScaleIndex(activeLayout.ScaleIndex);
        editorHost?.RefreshPreviewScales(editSession);
        if (IsOverlaySettingsOpen && UseGlobalOverlayScale)
        {
            selectedOverlayScalePercent = OverlayScaleCatalog.GetPercent(editSession.ScaleIndex);
            OnPropertyChanged(nameof(SelectedOverlayScalePercent));
            OnPropertyChanged(nameof(SelectedOverlayScaleLabel));
        }

        StatusMessage =
            "Overlay previews updated to the selected scale. "
            + "Use ✓ to save position, opacity, or scale changes, or × to cancel them.";
    }

    private void OnEditorClosed(object? sender, EventArgs eventArgs)
    {
        if (!IsEditing)
        {
            return;
        }

        EndSession(closeHost: false, restoreRuntimeWindows: true);
        StatusMessage = "Overlay position, opacity, and scale changes were cancelled.";
    }

    private void EndSession(bool closeHost, bool restoreRuntimeWindows)
    {
        MiningDetection?.EndEdit();
        IsCategoryMenuOpen = false;
        CloseOverlaySettings();
        editSession = null;
        IsEditing = false;
        if (closeHost)
        {
            editorHost?.Close(restoreRuntimeWindows);
        }
    }

    private void SetSelectedOverlayOpacity(double? opacityOverride)
    {
        if (selectedOverlaySettingsPlotterName is { } plotterName)
        {
            SetOverlayOpacityOverride(plotterName, opacityOverride);
        }
    }

    private void SetOverlayOpacityOverride(string plotterName, double? opacityOverride)
    {
        if (!IsEditing || editSession is null || !editSession.SetOpacityOverride(plotterName, opacityOverride))
        {
            return;
        }

        editorHost?.RefreshPreviewOpacities(editSession);
        string displayName = OverlayLayoutCatalog
            .Supported.First(definition => definition.Name == plotterName)
            .DisplayName;
        StatusMessage = opacityOverride is null
            ? $"{displayName} now uses global opacity. Use the top ✓ to save and close."
            : $"{displayName} opacity set to {opacityOverride.Value * 100d:N0}%. Use the top ✓ to save and close.";
    }

    private void SetSelectedOverlayScale(int? scaleOverride)
    {
        if (selectedOverlaySettingsPlotterName is { } plotterName)
        {
            SetOverlayScaleOverride(plotterName, scaleOverride);
        }
    }

    private void SetOverlayScaleOverride(string plotterName, int? scaleOverride)
    {
        if (!IsEditing || editSession is null || !editSession.SetScaleOverride(plotterName, scaleOverride))
        {
            return;
        }

        editorHost?.RefreshPreviewScales(editSession);
        string displayName = OverlayLayoutCatalog
            .Supported.First(definition => definition.Name == plotterName)
            .DisplayName;
        StatusMessage = scaleOverride is null
            ? $"{displayName} now uses global scale. Use the top ✓ to save and close."
            : $"{displayName} now uses its own scale. Use the top ✓ to save and close.";
    }

    private IReadOnlyList<OverlayTypographyRoleViewModel> CreateTypographyRoles() =>
        [
            new(OverlayTypographyRole.Header, "Header", SetTypographyPercent),
            new(OverlayTypographyRole.Title, "Title", SetTypographyPercent),
            new(OverlayTypographyRole.Value, "Value", SetTypographyPercent),
            new(OverlayTypographyRole.Body, "Body", SetTypographyPercent),
            new(OverlayTypographyRole.Detail, "Detail", SetTypographyPercent),
            new(OverlayTypographyRole.Caption, "Caption", SetTypographyPercent),
            new(OverlayTypographyRole.Icons, "Icons", SetTypographyPercent),
        ];

    private void ToggleCategoryMenu()
    {
        if (!IsEditing)
        {
            return;
        }

        bool open = !IsCategoryMenuOpen;
        CloseOverlaySettings();
        IsCategoryMenuOpen = open;
    }

    private void SelectCategory(object? parameter)
    {
        if (parameter is OverlayLayoutCategoryDefinition category)
        {
            SelectedCategory = category;
        }
    }

    private void ToggleTypographySettings()
    {
        if (IsOverlaySettingsOpen)
        {
            IsTypographySettingsOpen = !IsTypographySettingsOpen;
        }
    }

    private void ResetTypography()
    {
        if (!IsEditing || editSession is null || selectedOverlaySettingsPlotterName is not { } plotterName)
        {
            return;
        }

        updatingSelectedOverlaySettings = true;
        foreach (OverlayTypographyRoleViewModel role in TypographyRoles)
        {
            role.Load(0);
        }
        updatingSelectedOverlaySettings = false;
        if (!editSession.SetTypographyScale(plotterName, OverlayTypographyScale.Default))
        {
            return;
        }

        editorHost?.RefreshPreviewTypography(editSession);
        string displayName = OverlayLayoutCatalog.GetRequired(plotterName).DisplayName;
        StatusMessage = $"Reset {displayName} text and icon scales to 0%. Use the top ✓ to save and close.";
    }

    private void ResetOverlaySize()
    {
        if (!IsEditing || editSession is null || selectedOverlaySettingsPlotterName is not { } plotterName)
        {
            return;
        }

        if (!editSession.SetSizeOverride(plotterName, null))
        {
            return;
        }

        editorHost?.RefreshPreviewSizes(editSession);
        SynchronizeLiveOverlayFromPreview(plotterName);
        string displayName = OverlayLayoutCatalog.GetRequired(plotterName).DisplayName;
        StatusMessage = $"Restored {displayName} to its original measured size. Use the top ✓ to save and close.";
    }

    private void SetTypographyPercent(OverlayTypographyRole role, int percent)
    {
        if (!IsEditing || editSession is null || selectedOverlaySettingsPlotterName is not { } plotterName)
        {
            return;
        }

        OverlayTypographyScale updated = editSession.GetTypographyScale(plotterName).WithPercent(role, percent);
        if (!editSession.SetTypographyScale(plotterName, updated))
        {
            return;
        }

        editorHost?.RefreshPreviewTypography(editSession);
        string displayName = OverlayLayoutCatalog.GetRequired(plotterName).DisplayName;
        StatusMessage =
            $"{displayName} {role.ToString().ToLowerInvariant()} text set to {percent:+0;-0;0}% from baseline. Use the top ✓ to save and close.";
    }

    private void CloseOverlaySettings()
    {
        if (selectedOverlaySettingsPlotterName is null)
        {
            return;
        }

        selectedOverlaySettingsPlotterName = null;
        IsTypographySettingsOpen = false;
        OnPropertyChanged(nameof(IsOverlaySettingsOpen));
        OnPropertyChanged(nameof(SelectedOverlaySettingsTitle));
        toggleTypographySettingsCommand.RaiseCanExecuteChanged();
        resetTypographyCommand.RaiseCanExecuteChanged();
        resetOverlaySizeCommand.RaiseCanExecuteChanged();
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

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action<object?> execute;
        private readonly Func<bool> canExecute;

        public DelegateCommand(Action execute, Func<bool> canExecute)
            : this(_ => execute(), canExecute) { }

        public DelegateCommand(Action<object?> execute, Func<bool> canExecute)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute(parameter);

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record LiveOverlayWindowState(
        Window Window,
        string PlotterName,
        EventHandler<PointerPressedEventArgs> PointerPressed,
        EventHandler<PixelPointEventArgs> PositionChanged,
        EventHandler Closed
    );
}

public sealed class OverlayTypographyRoleViewModel : INotifyPropertyChanged
{
    private readonly Action<OverlayTypographyRole, int> changed;
    private int percent;

    public OverlayTypographyRoleViewModel(
        OverlayTypographyRole role,
        string displayName,
        Action<OverlayTypographyRole, int> changed
    )
    {
        Role = role;
        DisplayName = displayName;
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayTypographyRole Role { get; }

    public string DisplayName { get; }

    public int Percent
    {
        get => percent;
        set
        {
            int normalized = OverlayTypographyScale.Normalize(value);
            if (percent == normalized)
            {
                return;
            }

            percent = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Percent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            changed(Role, normalized);
        }
    }

    public string Label => $"{Percent:+0;-0;0}%";

    internal void Load(int value)
    {
        int normalized = OverlayTypographyScale.Normalize(value);
        if (percent == normalized)
        {
            return;
        }

        percent = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Percent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
}
