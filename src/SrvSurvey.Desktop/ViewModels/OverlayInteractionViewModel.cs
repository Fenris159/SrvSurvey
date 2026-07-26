using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayInteractionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IOverlayPlatformService? platform;
    private readonly IGameWindowTracker? gameWindowTracker;
    private readonly LegacyOverlayLayoutStore? layoutStore;
    private readonly LegacyOverlayLayout? activeLayout;
    private readonly IOverlayPositionEditorHost? editorHost;
    private readonly DelegateCommand toggleCommand;
    private readonly DelegateCommand saveCommand;
    private readonly DelegateCommand cancelCommand;
    private OverlayPositionEditSession? editSession;
    private OverlayLayoutCategoryDefinition selectedCategory;
    private bool isEditing;
    private bool disposed;
    private string statusMessage;

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
            ? "Overlay position previews are ready when the desktop overlay runtime starts."
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
        this.editorHost = editorHost
            ?? new AvaloniaOverlayPositionEditorHost(
                registry ?? OverlayWindowRegistry.Shared);
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
            ? "Choose Edit Overlay Positions to open categorized previews. Elite does not need to be running."
            : Capabilities.StatusText;
        this.editorHost.PreviewMoved += OnPreviewMoved;
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
                StatusMessage = $"Showing {value.DisplayName}. Drag the previews, then use ✓ to save or ✕ to cancel.";
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

    public string ModeLabel => IsEditing
        ? $"Editing {SelectedCategory.DisplayName}"
        : "Open categorized previews without starting Elite. Changes are saved only with ✓.";

    public string ToggleButtonText => IsEditing
        ? "Cancel Position Editing"
        : "Edit Overlay Positions";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

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

    public bool Begin()
    {
        if (!IsAvailable || disposed)
        {
            StatusMessage = Capabilities.StatusText;
            return false;
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
        IsEditing = true;
        var game = gameWindowTracker.GetSnapshot();
        var preferredBounds = game.IsAvailable
            ? game.ClientBounds
            : (PixelRect?)null;
        StatusMessage = $"Showing {SelectedCategory.DisplayName}. Drag the previews, then use ✓ to save or ✕ to cancel.";
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
        if (changes.Count == 0)
        {
            EndSession(closeHost: true, restoreRuntimeWindows: true);
            StatusMessage = "Overlay position editing closed; no positions changed.";
            return;
        }

        try
        {
            var result = layoutStore.Save(changes);
            var updated = layoutStore.Load();
            if (updated.Error is not null)
            {
                throw new InvalidDataException(updated.Error);
            }

            activeLayout.ReplaceWith(updated);
            EndSession(closeHost: true, restoreRuntimeWindows: true);
            StatusMessage = $"Saved {result.UpdatedPlacementCount:N0} overlay position(s)."
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
            StatusMessage = "Overlay positions were not saved: " + exception.Message;
        }
    }

    public void Cancel()
    {
        if (!IsEditing)
        {
            return;
        }

        EndSession(closeHost: true, restoreRuntimeWindows: true);
        StatusMessage = "Overlay position changes were cancelled.";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (editorHost is not null)
        {
            editorHost.PreviewMoved -= OnPreviewMoved;
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

    private void OnEditorClosed(object? sender, EventArgs eventArgs)
    {
        if (!IsEditing)
        {
            return;
        }

        EndSession(closeHost: false, restoreRuntimeWindows: true);
        StatusMessage = "Overlay position changes were cancelled.";
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
}
