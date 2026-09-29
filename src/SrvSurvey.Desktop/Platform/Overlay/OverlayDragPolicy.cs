using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class OverlayDragPolicy : AvaloniaObject
{
    private static readonly AttachedProperty<Func<OverlayDragOptions>?> OptionsFactoryProperty =
        AvaloniaProperty.RegisterAttached<OverlayDragPolicy, Window, Func<OverlayDragOptions>?>("OptionsFactory");

    private OverlayDragPolicy() { }

    internal static void SetOptionsFactory(Window window, Func<OverlayDragOptions>? factory)
    {
        window.SetValue(OptionsFactoryProperty, factory);
    }

    internal static OverlayDragOptions GetOptions(Window window) =>
        window.GetValue(OptionsFactoryProperty)?.Invoke() ?? new OverlayDragOptions();

    internal static OverlayDragOptions CreateOptions(
        Window window,
        OverlayBehaviorViewModel? behavior,
        PixelRect referenceBounds,
        PixelSize panelSize,
        PixelPoint panelOffset = default
    )
    {
        if (behavior?.LockToMonitor != true)
        {
            return new OverlayDragOptions();
        }

        MainWindowMonitor? monitor = ResolveMonitor(
            MainWindowPlacement.DescribeScreens(window.Screens.All),
            behavior.PreferredMonitorId,
            referenceBounds
        );
        return monitor is null
            ? new OverlayDragOptions()
            : new OverlayDragOptions
            {
                MonitorBounds = monitor.Bounds,
                ConstrainPosition = position => ClampPanel(position, panelSize, panelOffset, monitor.Bounds),
            };
    }

    internal static MainWindowMonitor? ResolveMonitor(
        IReadOnlyList<MainWindowMonitor> monitors,
        string? preferredMonitorId,
        PixelRect referenceBounds
    )
    {
        if (preferredMonitorId is not null)
        {
            return AvaloniaOverlayPositionEditorHost.ResolveFallbackMonitor(monitors, preferredMonitorId);
        }

        MainWindowMonitor? best = null;
        long bestArea = 0;
        foreach (MainWindowMonitor monitor in monitors)
        {
            PixelRect intersection = monitor.Bounds.Intersect(referenceBounds);
            long area = (long)intersection.Width * intersection.Height;
            if (area > bestArea)
            {
                best = monitor;
                bestArea = area;
            }
        }

        return best ?? AvaloniaOverlayPositionEditorHost.ResolveFallbackMonitor(monitors, null);
    }

    internal static PixelPoint ClampPanel(
        PixelPoint windowPosition,
        PixelSize panelSize,
        PixelPoint panelOffset,
        PixelRect monitorBounds
    )
    {
        return new PixelPoint(
            Math.Clamp(
                windowPosition.X + panelOffset.X,
                monitorBounds.X,
                monitorBounds.X + Math.Max(0, monitorBounds.Width - panelSize.Width)
            ) - panelOffset.X,
            Math.Clamp(
                windowPosition.Y + panelOffset.Y,
                monitorBounds.Y,
                monitorBounds.Y + Math.Max(0, monitorBounds.Height - panelSize.Height)
            ) - panelOffset.Y
        );
    }

    internal static OverlayDragOptions RestrictToCombinedHost(
        OverlayDragOptions options,
        PixelRect hostBounds,
        PixelSize panelSize
    )
    {
        if (options.MonitorBounds is not { } monitorBounds)
        {
            return options;
        }

        PixelRect intersection = hostBounds.Intersect(monitorBounds);
        PixelRect boundary = intersection is { Width: > 0, Height: > 0 } ? intersection : hostBounds;
        return options with
        {
            MonitorBounds = boundary,
            ConstrainPosition = position => ClampPanel(position, panelSize, default, boundary),
        };
    }
}

internal sealed record OverlayDragOptions
{
    internal PixelRect? MonitorBounds { get; init; }
    internal Func<PixelPoint, PixelPoint>? ConstrainPosition { get; init; }
    internal Action<string>? Log { get; init; }
    internal IOverlayDragPointerProbe? PointerProbe { get; init; }
}
