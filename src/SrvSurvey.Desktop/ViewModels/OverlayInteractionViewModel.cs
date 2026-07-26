using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayInteractionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IOverlayPlatformService? platform;
    private readonly IGameWindowTracker? gameWindowTracker;
    private readonly LegacyOverlayLayoutStore? layoutStore;
    private readonly LegacyOverlayLayout? activeLayout;
    private readonly OverlayWindowRegistry? registry;
    private readonly Dictionary<Window, WindowRegistration> registrations = [];
    private readonly Dictionary<string, LegacyOverlayPlacement> pendingPlacements =
        new(StringComparer.Ordinal);
    private readonly DispatcherTimer saveTimer;
    private bool isEditing;
    private bool disposed;
    private string statusMessage;

    public OverlayInteractionViewModel(OverlayPlatformCapabilities capabilities)
    {
        Capabilities = capabilities
            ?? throw new ArgumentNullException(nameof(capabilities));
        saveTimer = CreateSaveTimer();
        ToggleCommand = new DelegateCommand(() => _ = Toggle(), () => IsAvailable);
        statusMessage = IsAvailable
            ? "Overlay edit mode is ready when the desktop overlay runtime starts."
            : Capabilities.StatusText;
    }

    public OverlayInteractionViewModel(
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayoutStore layoutStore,
        LegacyOverlayLayout activeLayout,
        OverlayWindowRegistry? registry = null)
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
        Capabilities = platform.Capabilities;
        saveTimer = CreateSaveTimer();
        ToggleCommand = new DelegateCommand(() => _ = Toggle(), () => IsAvailable);
        statusMessage = IsAvailable
            ? "Overlays are passive and click-through. Use the shortcut or button to edit positions."
            : Capabilities.StatusText;
        this.registry.Changed += OnRegistryChanged;
        SynchronizeRegistrations();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayPlatformCapabilities Capabilities { get; }

    public bool IsAvailable => platform is not null
        && Capabilities.SupportsPassiveOverlay
        && Capabilities.SupportsClickThrough
        && Capabilities.SupportsGameWindowTracking;

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (isEditing == value)
            {
                return;
            }

            isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeLabel));
            OnPropertyChanged(nameof(ToggleButtonText));
        }
    }

    public string ModeLabel => IsEditing
        ? "Clickable position editing"
        : "Passive click-through";

    public string ToggleButtonText => IsEditing
        ? "Finish positioning"
        : "Edit overlay positions";

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (string.Equals(statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand ToggleCommand { get; }

    public bool Toggle()
    {
        if (!IsAvailable || disposed)
        {
            StatusMessage = Capabilities.StatusText;
            return false;
        }

        IsEditing = !IsEditing;
        var positionsSaved = IsEditing || SavePendingPlacements();

        var failures = 0;
        foreach (var registration in registrations.Values.ToArray())
        {
            if (!ApplyMode(registration.Window, IsEditing))
            {
                failures++;
            }
        }

        StatusMessage = !positionsSaved
            ? StatusMessage
            : failures > 0
            ? $"Changed overlay mode, but {failures:N0} window(s) could not be prepared safely."
            : IsEditing
                ? registrations.Count == 0
                    ? "Overlay editing is enabled. Newly opened overlays will be clickable and draggable."
                    : "Overlay editing is enabled. Drag any visible overlay; positions save automatically."
                : "Overlay positions are saved and every visible overlay is click-through again.";
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        saveTimer.Stop();
        if (IsEditing)
        {
            IsEditing = false;
            foreach (var registration in registrations.Values.ToArray())
            {
                _ = ApplyMode(registration.Window, interactive: false);
            }
        }

        SavePendingPlacements();
        if (registry is not null)
        {
            registry.Changed -= OnRegistryChanged;
        }

        foreach (var window in registrations.Keys.ToArray())
        {
            Detach(window);
        }

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

    private DispatcherTimer CreateSaveTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SavePendingPlacements();
        };
        return timer;
    }

    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizeRegistrations();
    }

    private void SynchronizeRegistrations()
    {
        if (registry is null || disposed)
        {
            return;
        }

        var snapshot = registry.Snapshot();
        var current = snapshot.Select(item => item.Window).ToHashSet();
        foreach (var stale in registrations.Keys.Where(window => !current.Contains(window))
                     .ToArray())
        {
            Detach(stale);
        }

        foreach (var item in snapshot)
        {
            if (registrations.ContainsKey(item.Window))
            {
                continue;
            }

            Attach(item);
        }
    }

    private void Attach(RegisteredOverlayWindow registered)
    {
        EventHandler<PointerPressedEventArgs> pressed = (_, eventArgs) =>
        {
            if (!IsEditing
                || !eventArgs.GetCurrentPoint(registered.Window)
                    .Properties.IsLeftButtonPressed)
            {
                return;
            }

            registered.Window.BeginMoveDrag(eventArgs);
            eventArgs.Handled = true;
        };
        EventHandler<PixelPointEventArgs> positionChanged = (_, _) =>
            OnWindowPositionChanged(registered);
        EventHandler opened = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (IsEditing && !disposed)
            {
                _ = ApplyMode(registered.Window, interactive: true);
            }
        });
        EventHandler closed = (_, _) => Detach(registered.Window);
        var registration = new WindowRegistration(
            registered.Window,
            registered.PlotterName,
            pressed,
            positionChanged,
            opened,
            closed);
        registrations.Add(registered.Window, registration);
        registered.Window.PointerPressed += pressed;
        registered.Window.PositionChanged += positionChanged;
        registered.Window.Opened += opened;
        registered.Window.Closed += closed;

        if (IsEditing && registered.Window.IsVisible)
        {
            _ = ApplyMode(registered.Window, interactive: true);
        }
    }

    private void Detach(Window window)
    {
        if (!registrations.Remove(window, out var registration))
        {
            return;
        }

        window.PointerPressed -= registration.PointerPressed;
        window.PositionChanged -= registration.PositionChanged;
        window.Opened -= registration.Opened;
        window.Closed -= registration.Closed;
    }

    private bool ApplyMode(Window window, bool interactive)
    {
        if (platform is null)
        {
            return false;
        }

        var result = platform.SetInteractive(window, interactive);
        var succeeded = result.IsPrepared
            && result.IsInteractive == interactive;
        if (!succeeded && !interactive && window.IsVisible)
        {
            window.Close();
        }

        return succeeded;
    }

    private void OnWindowPositionChanged(RegisteredOverlayWindow registered)
    {
        if (!IsEditing
            || activeLayout is null
            || gameWindowTracker is null)
        {
            return;
        }

        var original = activeLayout.Placements.GetValueOrDefault(
            registered.PlotterName)
            ?? OverlayLayoutCatalog.Supported.FirstOrDefault(definition =>
                    string.Equals(
                        definition.Name,
                        registered.PlotterName,
                        StringComparison.Ordinal))
                ?.DefaultPlacement
            ?? new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Screen,
                registered.Window.Position.X,
                LegacyVerticalAnchor.Screen,
                registered.Window.Position.Y,
                null);
        var snapshot = gameWindowTracker.GetSnapshot();
        if (!snapshot.IsAvailable
            && (original.Horizontal != LegacyHorizontalAnchor.Screen
                || original.Vertical != LegacyVerticalAnchor.Screen))
        {
            StatusMessage = "The Elite window could not be located, so this relative position was not saved.";
            return;
        }

        var width = registered.Window.Bounds.Width > 0
            ? registered.Window.Bounds.Width
            : registered.Window.Width;
        var height = registered.Window.Bounds.Height > 0
            ? registered.Window.Bounds.Height
            : registered.Window.Height;
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(width * registered.Window.RenderScaling)),
            Math.Max(1, (int)Math.Ceiling(height * registered.Window.RenderScaling)));
        var placement = CreatePlacement(
            original,
            registered.Window.Position,
            size,
            snapshot.ClientBounds);
        if (!activeLayout.SetPlacement(registered.PlotterName, placement))
        {
            return;
        }

        pendingPlacements[registered.PlotterName] = placement;
        saveTimer.Stop();
        saveTimer.Start();
        StatusMessage = $"Moved {registered.PlotterName}; saving position…";
    }

    private bool SavePendingPlacements()
    {
        if (pendingPlacements.Count == 0
            || layoutStore is null
            || activeLayout is null)
        {
            return true;
        }

        var pending = pendingPlacements.ToDictionary(StringComparer.Ordinal);
        try
        {
            var result = layoutStore.Save(pending);
            var updated = layoutStore.Load();
            if (updated.Error is not null)
            {
                throw new InvalidDataException(updated.Error);
            }

            activeLayout.ReplaceWith(updated);
            foreach (var name in pending.Keys)
            {
                pendingPlacements.Remove(name);
            }

            StatusMessage = $"Saved {result.UpdatedPlacementCount:N0} dragged overlay position(s)."
                + (result.BackupPath is null
                    ? string.Empty
                    : $" Previous layout backup: {result.BackupPath}");
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "The dragged overlay position was not saved: "
                + exception.Message;
            return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record WindowRegistration(
        Window Window,
        string PlotterName,
        EventHandler<PointerPressedEventArgs> PointerPressed,
        EventHandler<PixelPointEventArgs> PositionChanged,
        EventHandler Opened,
        EventHandler Closed);

    private sealed class DelegateCommand(Action execute, Func<bool> canExecute)
        : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();
    }
}
