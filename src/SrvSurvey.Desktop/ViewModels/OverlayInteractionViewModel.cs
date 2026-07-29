using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly DelegateCommand toggleCommand;
    private readonly DelegateCommand saveCommand;
    private readonly DelegateCommand cancelCommand;
    private OverlayPositionEditSession? editSession;
    private OverlayPositionEditSession? liveEditSession;
    private OverlayLayoutCategoryDefinition selectedCategory;
    private bool isEditing;
    private bool isLiveInteractionEnabled;
    private PixelRect liveHostBounds;
    private bool disposed;
    private string statusMessage;
    private double globalOpacityPercent = 100d;

    public OverlayInteractionViewModel(OverlayPlatformCapabilities capabilities)
    {
        Capabilities = capabilities
            ?? throw new ArgumentNullException(nameof(capabilities));
        Categories = OverlayLayoutCatalog.Categories;
        selectedCategory = Categories[0];
        toggleCommand = new DelegateCommand(() => _ = Toggle(), () => IsAvailable);
        saveCommand = new DelegateCommand(Save, () => IsEditing);
        cancelCommand = new DelegateCommand(Cancel, () => IsEditing);
        ToggleCommand = toggleCommand;
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
        saveCommand = new DelegateCommand(Save, () => IsEditing);
        cancelCommand = new DelegateCommand(Cancel, () => IsEditing);
        ToggleCommand = toggleCommand;
        SaveCommand = saveCommand;
        CancelCommand = cancelCommand;
        statusMessage = IsAvailable
            ? "Choose Edit Overlay Positions to load categorized previews from an isolated simulated game state. Elite does not need to be running."
            : Capabilities.StatusText;
        this.editorHost.PreviewMoved += OnPreviewMoved;
        this.editorHost.PreviewOpacityChanged += OnPreviewOpacityChanged;
        this.editorHost.Closed += OnEditorClosed;
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

    public string ModeLabel => IsEditing
        ? $"Editing {SelectedCategory.DisplayName}"
        : IsLiveInteractionEnabled
            ? "Visible live overlays are clickable and can be dragged. Use the shortcut again to save and restore click-through mode."
            : "Open categorized previews without starting Elite. Changes are saved only with ✓.";

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
            StatusMessage = $"Global overlay opacity set to {normalized:N0}%. Use ✓ to save all changes or × to cancel them.";
        }
    }

    public string GlobalOpacityLabel => $"{GlobalOpacityPercent:N0}%";

    public ICommand ToggleCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public bool Toggle()
    {
        if (IsEditing)
        {
            Cancel();
            return true;
        }

        return Begin();
    }

    public bool ToggleLiveOverlayInteraction()
    {
        if (IsEditing)
        {
            StatusMessage = "Close the categorized overlay position editor before enabling interaction with live overlays.";
            return false;
        }

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

        if (IsLiveInteractionEnabled)
        {
            EndLiveInteraction(saveChanges: true);
        }

        if (activeLayout?.Error is not null)
        {
            StatusMessage = "Overlay positions cannot be edited until the existing layout error is corrected: "
                + activeLayout.Error;
            return false;
        }

        if (editorHost is null || gameWindowTracker is null || activeLayout is null)
        {
            StatusMessage = "The overlay position editor is not available in this runtime.";
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
            EndSession(closeHost: true, restoreRuntimeWindows: true);
            StatusMessage = $"Saved {result.UpdatedPlacementCount:N0} overlay position/opacity override(s)"
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
            StatusMessage = "Overlay positions and opacity were not saved: " + exception.Message;
        }
    }

    public void Cancel()
    {
        if (!IsEditing)
        {
            return;
        }

        EndSession(closeHost: true, restoreRuntimeWindows: true);
        StatusMessage = "Overlay position and opacity changes were cancelled.";
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

            AttachLiveWindow(registered);
        }

        if (liveWindows.Count == 0)
        {
            liveEditSession = null;
            StatusMessage = "No live overlays could be made clickable. " + lastStatus;
            return false;
        }

        IsLiveInteractionEnabled = true;
        StatusMessage = $"{liveWindows.Count:N0} live overlay(s) are clickable. Drag them into place, then use the shortcut again to save.";
        return true;
    }

    private void EndLiveInteraction(bool saveChanges)
    {
        var changes = liveEditSession?.Changes
            ?? new Dictionary<string, LegacyOverlayPlacement>();
        var failures = new List<string>();
        foreach (var state in liveWindows.Values.ToArray())
        {
            DetachLiveWindow(state.Window);
            if (platform is null)
            {
                continue;
            }

            var result = platform.SetInteractive(
                state.Window,
                interactive: false);
            if (!result.IsPrepared || result.IsInteractive)
            {
                failures.Add(result.Status);
            }
        }

        liveEditSession = null;
        IsLiveInteractionEnabled = false;
        if (!saveChanges || changes.Count == 0)
        {
            StatusMessage = failures.Count == 0
                ? "Live overlays returned to click-through mode; no positions moved."
                : "Live overlay interaction ended, but one or more windows could not be restored: "
                    + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
            return;
        }

        try
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
            StatusMessage = $"Saved {result.UpdatedPlacementCount:N0} live overlay position(s) and restored click-through mode."
                + (failures.Count == 0
                    ? string.Empty
                    : " One or more windows reported a click-through restore warning: "
                        + string.Join(" ", failures.Distinct(StringComparer.Ordinal)));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "Live overlays returned to click-through mode, but their moved positions were not saved: "
                + exception.Message;
        }
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

        var size = GetLiveWindowPixelSize(registered);
        if (!liveEditSession.Move(
                registered.PlotterName,
                position,
                size,
                liveHostBounds))
        {
            return;
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

    private static PixelSize GetLiveWindowPixelSize(
        RegisteredOverlayWindow registered)
    {
        var fallback = OverlayLayoutCatalog.Supported
            .FirstOrDefault(definition => string.Equals(
                definition.Name,
                registered.PlotterName,
                StringComparison.Ordinal))
            ?.PreviewSize
            ?? new PixelSize(1, 1);
        var scaling = Math.Max(0.1, registered.Window.RenderScaling);
        var logicalWidth = registered.Window.Bounds.Width > 0
            ? registered.Window.Bounds.Width
            : registered.Window.Width;
        var logicalHeight = registered.Window.Bounds.Height > 0
            ? registered.Window.Bounds.Height
            : registered.Window.Height;
        var width = double.IsFinite(logicalWidth) && logicalWidth > 0
            ? (int)Math.Ceiling(logicalWidth * scaling)
            : fallback.Width;
        var height = double.IsFinite(logicalHeight) && logicalHeight > 0
            ? (int)Math.Ceiling(logicalHeight * scaling)
            : fallback.Height;
        return new PixelSize(Math.Max(width, 1), Math.Max(height, 1));
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
            editorHost.PreviewOpacityChanged -= OnPreviewOpacityChanged;
            editorHost.Closed -= OnEditorClosed;
            editorHost.Close(restoreRuntimeWindows: false);
            editorHost.Dispose();
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

        var displayName = OverlayLayoutCatalog.Supported
            .First(definition => string.Equals(
                definition.Name,
                eventArgs.PlotterName,
                StringComparison.Ordinal))
            .DisplayName;
        StatusMessage = $"Moved {displayName}. Use ✓ to save all changes or ✕ to cancel them.";
    }

    private void OnPreviewOpacityChanged(
        object? sender,
        OverlayPreviewOpacityChangedEventArgs eventArgs)
    {
        if (!IsEditing
            || editSession is null
            || !editSession.SetOpacityOverride(
                eventArgs.PlotterName,
                eventArgs.OpacityOverride))
        {
            return;
        }

        editorHost?.RefreshPreviewOpacities(editSession);
        var displayName = OverlayLayoutCatalog.Supported
            .First(definition => string.Equals(
                definition.Name,
                eventArgs.PlotterName,
                StringComparison.Ordinal))
            .DisplayName;
        StatusMessage = eventArgs.OpacityOverride is null
            ? $"{displayName} now uses global opacity. Use ✓ to save all changes or × to cancel them."
            : $"{displayName} opacity set to {eventArgs.OpacityOverride.Value * 100d:N0}%. Use ✓ to save all changes or × to cancel them.";
    }

    private void OnEditorClosed(object? sender, EventArgs eventArgs)
    {
        if (!IsEditing)
        {
            return;
        }

        EndSession(closeHost: false, restoreRuntimeWindows: true);
        StatusMessage = "Overlay position and opacity changes were cancelled.";
    }

    private void EndSession(bool closeHost, bool restoreRuntimeWindows)
    {
        editSession = null;
        IsEditing = false;
        if (closeHost)
        {
            editorHost?.Close(restoreRuntimeWindows);
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
