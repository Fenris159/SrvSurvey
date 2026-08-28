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
    private static readonly OverlayScaleOption[] IndividualScaleOptions =
        OverlayScaleCatalog.Options
            .Where(option => option.AbsoluteScale is not null)
            .OrderBy(option => option.AbsoluteScale)
            .ToArray();
    private readonly IOverlayPlatformService? platform;
    private readonly IGameWindowTracker? gameWindowTracker;
    private readonly LegacyOverlayLayoutStore? layoutStore;
    private readonly LegacyOverlayLayout? activeLayout;
    private readonly IOverlayPositionEditorHost? editorHost;
    private readonly OverlayWindowRegistry? registry;
    private readonly Dictionary<Window, LiveOverlayWindowState> liveWindows = [];
    private readonly DelegateCommand toggleCommand;
    private readonly DelegateCommand snapToCenterCommand;
    private readonly DelegateCommand saveCommand;
    private readonly DelegateCommand cancelCommand;
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
    private double selectedOverlayScaleOrdinal;

    public OverlayInteractionViewModel(OverlayPlatformCapabilities capabilities)
    {
        Capabilities = capabilities
            ?? throw new ArgumentNullException(nameof(capabilities));
        Categories = OverlayLayoutCatalog.Categories;
        selectedCategory = Categories[0];
        toggleCommand = new DelegateCommand(() => _ = Toggle(), () => IsAvailable);
        snapToCenterCommand = new DelegateCommand(
            SnapCurrentCategoryToCenter,
            () => IsEditing);
        saveCommand = new DelegateCommand(Save, () => IsEditing);
        cancelCommand = new DelegateCommand(Cancel, () => IsEditing);
        ToggleCommand = toggleCommand;
        SnapToCenterCommand = snapToCenterCommand;
        SaveCommand = saveCommand;
        CancelCommand = cancelCommand;
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
        IOverlayPositionEditorHost? editorHost = null)
    {
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.layoutStore = layoutStore
            ?? throw new ArgumentNullException(nameof(layoutStore));
        this.activeLayout = activeLayout
            ?? throw new ArgumentNullException(nameof(activeLayout));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        this.editorHost = editorHost
            ?? new AvaloniaOverlayPositionEditorHost(
                platform,
                this.registry);
        Capabilities = platform.Capabilities;
        Categories = OverlayLayoutCatalog.Categories;
        selectedCategory = Categories[0];
        toggleCommand = new DelegateCommand(() => _ = Toggle(), () => IsAvailable);
        snapToCenterCommand = new DelegateCommand(
            SnapCurrentCategoryToCenter,
            () => IsEditing);
        saveCommand = new DelegateCommand(Save, () => IsEditing);
        cancelCommand = new DelegateCommand(Cancel, () => IsEditing);
        ToggleCommand = toggleCommand;
        SnapToCenterCommand = snapToCenterCommand;
        SaveCommand = saveCommand;
        CancelCommand = cancelCommand;
        statusMessage = IsAvailable
            ? "Choose Edit Overlay Positions to load categorized previews from an isolated simulated game state. Elite does not need to be running."
            : Capabilities.StatusText;
        this.editorHost.PreviewMoved += OnPreviewMoved;
        this.editorHost.Closed += OnEditorClosed;
        this.activeLayout.ScaleIndexChanged += OnOverlayScaleIndexChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayPlatformCapabilities Capabilities { get; }

    public IReadOnlyList<OverlayLayoutCategoryDefinition> Categories { get; }

    public OverlayLayoutCategoryDefinition SelectedCategory
    {
        get => selectedCategory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetField(ref selectedCategory, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ModeLabel));
            if (IsEditing && editSession is not null)
            {
                CloseOverlaySettings();
                editorHost?.ShowCategory(editSession, value.Category);
                StatusMessage = $"Showing {value.DisplayName} with simulated game data. Drag the previews, then use ✓ to save or ✕ to cancel.";
            }
        }
    }

    public bool IsAvailable => platform is not null
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

    public string ToggleButtonText => IsEditing
        ? "Cancel Position Editing"
        : "Edit Overlay Positions";

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
            var normalized = Math.Clamp(value, 0, 100);
            if (!SetField(ref globalOpacityPercent, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(GlobalOpacityLabel));
            if (!IsEditing
                || editSession is null
                || !editSession.SetDefaultOpacity(normalized / 100d))
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

            StatusMessage = $"Global overlay opacity set to {normalized:N0}%. Use ✓ to save all changes or × to cancel them.";
        }
    }

    public string GlobalOpacityLabel => $"{GlobalOpacityPercent:N0}%";

    public bool IsOverlaySettingsOpen =>
        selectedOverlaySettingsPlotterName is not null;

    public string SelectedOverlaySettingsTitle
    {
        get
        {
            if (selectedOverlaySettingsPlotterName is null)
            {
                return "Overlay opacity and scale";
            }

            var definition = OverlayLayoutCatalog.Supported.First(candidate =>
                candidate.Name == selectedOverlaySettingsPlotterName);
            return $"{definition.DisplayName} opacity and scale";
        }
    }

    public bool UseGlobalOverlayOpacity
    {
        get => useGlobalOverlayOpacity;
        set
        {
            if (!SetField(ref useGlobalOverlayOpacity, value)
                || updatingSelectedOverlaySettings)
            {
                return;
            }

            SetSelectedOverlayOpacity(
                value ? null : SelectedOverlayOpacityPercent / 100d);
        }
    }

    public double SelectedOverlayOpacityPercent
    {
        get => selectedOverlayOpacityPercent;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
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

    public string SelectedOverlayOpacityLabel =>
        $"{SelectedOverlayOpacityPercent:N0}%";

    public bool UseGlobalOverlayScale
    {
        get => useGlobalOverlayScale;
        set
        {
            if (!SetField(ref useGlobalOverlayScale, value)
                || updatingSelectedOverlaySettings)
            {
                return;
            }

            SetSelectedOverlayScale(
                value
                    ? null
                    : GetScaleOption(
                        (int)Math.Round(SelectedOverlayScaleOrdinal)).Index);
        }
    }

    public double SelectedOverlayScaleOrdinal
    {
        get => selectedOverlayScaleOrdinal;
        set
        {
            var normalized = Math.Clamp(
                Math.Round(value),
                0,
                SelectedOverlayScaleMaximum);
            if (!SetField(ref selectedOverlayScaleOrdinal, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedOverlayScaleLabel));
            if (!updatingSelectedOverlaySettings && !UseGlobalOverlayScale)
            {
                SetSelectedOverlayScale(
                    GetScaleOption((int)normalized).Index);
            }
        }
    }

    public double SelectedOverlayScaleMaximum { get; } =
        IndividualScaleOptions.Length - 1;

    public string SelectedOverlayScaleLabel
    {
        get
        {
            var option = UseGlobalOverlayScale && editSession is not null
                ? OverlayScaleCatalog.Options.Single(candidate =>
                    candidate.Index == editSession.ScaleIndex)
                : GetScaleOption(
                    (int)Math.Round(SelectedOverlayScaleOrdinal));
            return option.AbsoluteScale is { } scale
                ? scale.ToString("0%")
                : "OS";
        }
    }

    public ICommand ToggleCommand { get; }

    public ICommand SnapToCenterCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public void OpenOverlaySettings(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (!IsEditing || editSession is null)
        {
            return;
        }

        var definition = OverlayLayoutCatalog.Supported.FirstOrDefault(
            candidate => string.Equals(
                candidate.Name,
                plotterName,
                StringComparison.Ordinal));
        if (definition is null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plotterName),
                plotterName,
                "The overlay is not available in the position editor.");
        }

        var placement = editSession.GetPlacement(plotterName);
        updatingSelectedOverlaySettings = true;
        selectedOverlaySettingsPlotterName = plotterName;
        useGlobalOverlayOpacity = placement.Opacity is null;
        selectedOverlayOpacityPercent = editSession.GetOpacity(plotterName) * 100d;
        useGlobalOverlayScale = placement.ScaleIndex is null;
        selectedOverlayScaleOrdinal = GetScaleOptionOrdinal(
            placement.ScaleIndex ?? GetIndividualFallback(editSession.ScaleIndex).Index);
        updatingSelectedOverlaySettings = false;
        OnPropertyChanged(nameof(IsOverlaySettingsOpen));
        OnPropertyChanged(nameof(SelectedOverlaySettingsTitle));
        OnPropertyChanged(nameof(UseGlobalOverlayOpacity));
        OnPropertyChanged(nameof(SelectedOverlayOpacityPercent));
        OnPropertyChanged(nameof(SelectedOverlayOpacityLabel));
        OnPropertyChanged(nameof(UseGlobalOverlayScale));
        OnPropertyChanged(nameof(SelectedOverlayScaleOrdinal));
        OnPropertyChanged(nameof(SelectedOverlayScaleLabel));
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
        var definitions = OverlayLayoutCatalog.ForCategory(
            SelectedCategory.Category);
        var snappedCount = editorHost?.SnapPreviewsToCenter(editSession) ?? 0;
        foreach (var definition in definitions)
        {
            SynchronizeLiveOverlayFromPreview(definition.Name);
        }

        StatusMessage = $"Snapped {snappedCount:N0} {SelectedCategory.DisplayName} overlay(s) to the center. Rearrange them, then use ✓ to save or × to cancel.";
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
            StatusMessage = "Overlay positions cannot be edited until the existing layout error is corrected: "
                + activeLayout.Error;
            return false;
        }

        var session = new OverlayPositionEditSession(activeLayout);
        editSession = session;
        globalOpacityPercent = session.DefaultOpacity * 100d;
        OnPropertyChanged(nameof(GlobalOpacityPercent));
        OnPropertyChanged(nameof(GlobalOpacityLabel));
        IsEditing = true;
        var game = gameWindowTracker.GetSnapshot();
        var preferredBounds = game.IsAvailable
            ? game.ClientBounds
            : (PixelRect?)null;
        StatusMessage = $"Showing {SelectedCategory.DisplayName} with simulated game data. Drag the previews, then use ✓ to save or ✕ to cancel.";
        if (editorHost.Open(
                this,
                session,
                SelectedCategory.Category,
                preferredBounds))
        {
            return true;
        }

        editSession = null;
        IsEditing = false;
        StatusMessage = "The overlay position editor could not find a usable display.";
        return false;
    }

    public void Save()
    {
        if (!IsEditing
            || editSession is null
            || layoutStore is null
            || activeLayout is null)
        {
            return;
        }

        var changes = editSession.Changes;
        var saveDefaultOpacity = editSession.HasDefaultOpacityChange;
        if (changes.Count == 0 && !saveDefaultOpacity)
        {
            EndSession(closeHost: true, restoreRuntimeWindows: true);
            StatusMessage = "Overlay editing closed; no positions or opacity values changed.";
            return;
        }

        try
        {
            var result = layoutStore.Save(
                changes,
                editSession.DefaultOpacity,
                saveDefaultOpacity);
            var updated = layoutStore.Load();
            if (updated.Error is not null)
            {
                throw new InvalidDataException(updated.Error);
            }

            activeLayout.ReplaceWith(updated);
            ApplySavedLayoutToRuntimeWindows();

            EndSession(closeHost: true, restoreRuntimeWindows: true);
            StatusMessage = $"Saved {result.UpdatedPlacementCount:N0} overlay position/opacity override(s), including scale settings"
                + (saveDefaultOpacity ? " and the global opacity." : ".")
                + (result.BackupPath is null
                    ? string.Empty
                    : $" Previous layout backup: {result.BackupPath}");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "Overlay positions, opacity, and scale were not saved: " + exception.Message;
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
        if (!IsAvailable
            || disposed
            || platform is null
            || gameWindowTracker is null
            || activeLayout is null
            || registry is null)
        {
            StatusMessage = Capabilities.StatusText;
            return false;
        }

        if (!IsEditing
            && !ReloadPersistedLayout("Live overlay positions cannot be edited"))
        {
            return false;
        }

        if (activeLayout.Error is not null)
        {
            StatusMessage = "Live overlay positions cannot be edited until the existing layout error is corrected: "
                + activeLayout.Error;
            return false;
        }

        var game = gameWindowTracker.GetSnapshot();
        if (!game.IsAvailable || game.ClientBounds is not { Width: > 0, Height: > 0 })
        {
            StatusMessage = "No tracked Elite window is available. Use Edit Overlay Positions for offline layout changes.";
            return false;
        }

        liveHostBounds = game.ClientBounds;
        liveEditSession = new OverlayPositionEditSession(activeLayout);
        var lastStatus = "No registered live overlay accepted interactive mode.";
        foreach (var registered in registry.Snapshot())
        {
            var result = platform.SetInteractive(
                registered.Window,
                interactive: true);
            lastStatus = result.Status;
            if (!result.IsPrepared || !result.IsInteractive)
            {
                continue;
            }

            if (registered.ParticipatesInPlacement)
            {
                AttachLiveWindow(registered);
            }
        }

        if (liveWindows.Count == 0)
        {
            liveEditSession = null;
            StatusMessage = "No live overlays could be made clickable. " + lastStatus;
            return false;
        }

        cursorVisibilitySession = platform.BeginVisibleCursorSession(
            liveWindows.Keys.First());
        IsLiveInteractionEnabled = true;
        if (IsEditing)
        {
            editorHost?.SetRuntimeOverlaysVisibleDuringEditing(true);
        }

        StatusMessage = $"{liveWindows.Count:N0} live overlay(s) are clickable. Drag them into place, then use the shortcut again to save.";
        return true;
    }

    private void EndLiveInteraction(bool saveChanges)
    {
        var session = liveEditSession;
        var changes = session?.Changes
            ?? new Dictionary<string, LegacyOverlayPlacement>();
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
            StatusMessage = GetUnsavedInteractionStatus(
                changes.Count,
                failures);
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
        foreach (var window in liveWindows.Values
            .Select(state => state.Window)
            .ToArray())
        {
            DetachLiveWindow(window);
            if (platform is null)
            {
                continue;
            }

            var result = platform.SetInteractive(
                window,
                interactive: false);
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
        List<string> failures)
    {
        try
        {
            if (layoutStore is null || activeLayout is null)
            {
                throw new InvalidOperationException(
                    "The overlay layout store is unavailable.");
            }

            var result = PersistLivePositions(changes);
            StatusMessage = $"Saved {result.UpdatedPlacementCount:N0} live overlay position(s) and restored click-through mode."
                + FormatFailureSuffix(failures);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            RestoreLivePlacements(session, changes.Keys);
            StatusMessage = "Live overlays returned to click-through mode, but their moved positions were not saved: "
                + exception.Message;
        }
    }

    private bool PersistPendingLivePositionsForEditor()
    {
        var changes = liveEditSession?.Changes;
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
            var synchronizedLayout = activeLayout
                ?? throw new InvalidOperationException(
                    "The active overlay layout is unavailable.");
            liveEditSession = new OverlayPositionEditSession(
                synchronizedLayout);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "Overlay positions cannot be edited because pending live positions could not be synchronized: "
                + exception.Message;
            return false;
        }
    }

    private LegacyOverlayLayoutSaveResult PersistLivePositions(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> changes)
    {
        if (layoutStore is null || activeLayout is null)
        {
            throw new InvalidOperationException(
                "The overlay layout store is unavailable.");
        }

        var result = layoutStore.Save(changes);
        var updated = layoutStore.Load();
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

        var game = gameWindowTracker?.GetSnapshot();
        foreach (var registered in registry.Snapshot())
        {
            OverlayThemeResources.ApplyScale(
                registered.Window,
                activeLayout,
                registered.PlotterName);
            if (!registered.ParticipatesInPlacement
                || game is not
                {
                    IsAvailable: true,
                    ClientBounds: { Width: > 0, Height: > 0 },
                })
            {
                continue;
            }

            var position = activeLayout.GetPosition(
                registered.PlotterName,
                game.ClientBounds,
                OverlayWindowMetrics.GetPixelSize(registered));
            if (position is { } savedPosition
                && registered.Window.Position != savedPosition)
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

    private static string GetUnsavedInteractionStatus(
        int changedPlacementCount,
        List<string> failures)
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
            closed);
        liveWindows.Add(registered.Window, state);
        registered.Window.PointerPressed += pointerPressed;
        registered.Window.PositionChanged += positionChanged;
        registered.Window.Closed += closed;
    }

    private void DetachLiveWindow(Window window)
    {
        if (!liveWindows.Remove(window, out var state))
        {
            return;
        }

        window.PointerPressed -= state.PointerPressed;
        window.PositionChanged -= state.PositionChanged;
        window.Closed -= state.Closed;
    }

    private void OnLiveWindowPointerPressed(
        Window window,
        PointerPressedEventArgs eventArgs)
    {
        if (!IsLiveInteractionEnabled
            || !eventArgs.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
        {
            return;
        }

        platform?.BeginMoveDrag(window, eventArgs);
        eventArgs.Handled = true;
    }

    private void OnLiveWindowPositionChanged(
        RegisteredOverlayWindow registered,
        PixelPoint position)
    {
        if (!IsLiveInteractionEnabled || liveEditSession is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.GetPixelSize(registered);
        if (activeLayout is null
            || !MoveLiveOverlay(
                liveEditSession,
                activeLayout,
                registered.PlotterName,
                position,
                size,
                liveHostBounds,
                IsEditing ? editSession : null))
        {
            return;
        }

        if (IsEditing && editSession is not null)
        {
            editorHost?.RefreshPreviewPositions(editSession);
        }

        var name = OverlayLayoutCatalog.Supported
            .FirstOrDefault(definition => string.Equals(
                definition.Name,
                registered.PlotterName,
                StringComparison.Ordinal))
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
        OverlayPositionEditSession? previewSession = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(activeLayout);
        if (!session.Move(
                plotterName,
                position,
                overlaySize,
                hostBounds))
        {
            return false;
        }

        var placement = session.GetPlacement(plotterName);
        activeLayout.SetPlacement(
            plotterName,
            placement);
        previewSession?.SetPlacement(plotterName, placement);
        return true;
    }

    private void RestoreLivePlacements(
        OverlayPositionEditSession? session,
        IEnumerable<string> plotterNames)
    {
        if (session is null || activeLayout is null)
        {
            return;
        }

        foreach (var plotterName in plotterNames)
        {
            var original = session.GetOriginalPlacement(plotterName);
            activeLayout.SetPlacement(
                plotterName,
                original);
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

        var updated = layoutStore.Load();
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
            editorHost.Closed -= OnEditorClosed;
            editorHost.Close(restoreRuntimeWindows: false);
            editorHost.Dispose();
        }

        if (activeLayout is not null)
        {
            activeLayout.ScaleIndexChanged -= OnOverlayScaleIndexChanged;
        }

        editSession = null;
        IsEditing = false;
        gameWindowTracker?.Dispose();
        platform?.Dispose();
    }

    internal static LegacyOverlayPlacement CreatePlacement(
        LegacyOverlayPlacement original,
        PixelPoint position,
        PixelSize overlaySize,
        PixelRect gameBounds)
    {
        ArgumentNullException.ThrowIfNull(original);
        var horizontalOffset = original.Horizontal switch
        {
            LegacyHorizontalAnchor.Left => position.X - gameBounds.X,
            LegacyHorizontalAnchor.Center => position.X
                - (gameBounds.X + ((gameBounds.Width - overlaySize.Width) / 2)),
            LegacyHorizontalAnchor.Right => gameBounds.Right
                - overlaySize.Width
                - position.X,
            _ => position.X,
        };
        var verticalOffset = original.Vertical switch
        {
            LegacyVerticalAnchor.Top => position.Y - gameBounds.Y,
            LegacyVerticalAnchor.Middle => position.Y
                - (gameBounds.Y + ((gameBounds.Height - overlaySize.Height) / 2)),
            LegacyVerticalAnchor.Bottom => gameBounds.Bottom
                - overlaySize.Height
                - position.Y,
            _ => position.Y,
        };
        return original with
        {
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
        };
    }

    private void OnPreviewMoved(
        object? sender,
        OverlayPreviewMovedEventArgs eventArgs)
    {
        if (!IsEditing || editSession is null)
        {
            return;
        }

        if (!editSession.Move(
                eventArgs.PlotterName,
                eventArgs.Position,
                eventArgs.PreviewSize,
                eventArgs.HostBounds))
        {
            return;
        }

        SynchronizeLiveOverlayFromPreview(eventArgs.PlotterName);
        var displayName = OverlayLayoutCatalog.Supported
            .First(definition => string.Equals(
                definition.Name,
                eventArgs.PlotterName,
                StringComparison.Ordinal))
            .DisplayName;
        StatusMessage = $"Moved {displayName}. Use ✓ to save all changes or ✕ to cancel them.";
    }

    private void SynchronizeLiveOverlayFromPreview(string plotterName)
    {
        if (!IsLiveInteractionEnabled
            || editSession is null
            || liveEditSession is null
            || activeLayout is null
            || registry is null)
        {
            return;
        }

        var placement = editSession.GetPlacement(plotterName);
        liveEditSession.SetPlacement(plotterName, placement);
        activeLayout.SetPlacement(plotterName, placement);
        var registered = registry.Snapshot().FirstOrDefault(candidate =>
            candidate.ParticipatesInPlacement
            && string.Equals(
                candidate.PlotterName,
                plotterName,
                StringComparison.Ordinal));
        if (registered is null)
        {
            return;
        }

        var runtimePosition = activeLayout.GetPosition(
            plotterName,
            liveHostBounds,
            OverlayWindowMetrics.GetPixelSize(registered));
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
            selectedOverlayScaleOrdinal = GetScaleOptionOrdinal(
                GetIndividualFallback(editSession.ScaleIndex).Index);
            OnPropertyChanged(nameof(SelectedOverlayScaleOrdinal));
            OnPropertyChanged(nameof(SelectedOverlayScaleLabel));
        }

        StatusMessage = "Overlay previews updated to the selected scale. "
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

    private void SetOverlayOpacityOverride(
        string plotterName,
        double? opacityOverride)
    {
        if (!IsEditing
            || editSession is null
            || !editSession.SetOpacityOverride(plotterName, opacityOverride))
        {
            return;
        }

        editorHost?.RefreshPreviewOpacities(editSession);
        var displayName = OverlayLayoutCatalog.Supported
            .First(definition => definition.Name == plotterName)
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

    private void SetOverlayScaleOverride(
        string plotterName,
        int? scaleOverride)
    {
        if (!IsEditing
            || editSession is null
            || !editSession.SetScaleOverride(plotterName, scaleOverride))
        {
            return;
        }

        editorHost?.RefreshPreviewScales(editSession);
        var displayName = OverlayLayoutCatalog.Supported
            .First(definition => definition.Name == plotterName)
            .DisplayName;
        StatusMessage = scaleOverride is null
            ? $"{displayName} now uses global scale. Use the top ✓ to save and close."
            : $"{displayName} now uses its own scale. Use the top ✓ to save and close.";
    }

    private void CloseOverlaySettings()
    {
        if (selectedOverlaySettingsPlotterName is null)
        {
            return;
        }

        selectedOverlaySettingsPlotterName = null;
        OnPropertyChanged(nameof(IsOverlaySettingsOpen));
        OnPropertyChanged(nameof(SelectedOverlaySettingsTitle));
    }

    private static OverlayScaleOption GetScaleOption(int ordinal)
    {
        return IndividualScaleOptions[Math.Clamp(
            ordinal,
            0,
            IndividualScaleOptions.Length - 1)];
    }

    private static int GetScaleOptionOrdinal(int scaleIndex)
    {
        for (var index = 0; index < IndividualScaleOptions.Length; index++)
        {
            if (IndividualScaleOptions[index].Index == scaleIndex)
            {
                return index;
            }
        }

        var scale = OverlayScaleCatalog.Options
            .FirstOrDefault(option => option.Index == scaleIndex)
            ?.AbsoluteScale
            ?? 1d;
        return IndividualScaleOptions
            .Select((option, index) => new
            {
                index,
                distance = Math.Abs(option.AbsoluteScale!.Value - scale),
            })
            .MinBy(entry => entry.distance)!
            .index;
    }

    private static OverlayScaleOption GetIndividualFallback(int scaleIndex)
    {
        return GetScaleOption(GetScaleOptionOrdinal(scaleIndex));
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

    private sealed class DelegateCommand(Action execute, Func<bool> canExecute)
        : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();

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
        EventHandler Closed);
}
