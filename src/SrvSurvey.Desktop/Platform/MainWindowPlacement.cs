using Avalonia;
using Avalonia.Platform;
using System.Globalization;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform;

internal static class MainWindowPlacement
{
    internal const double DefaultWidth = 1180;
    internal const double DefaultHeight = 760;
    internal const double DefaultMinimumWidth = 860;
    internal const double DefaultMinimumHeight = 600;
    private const double WorkingAreaMargin = 24;

    public static IReadOnlyList<MainWindowMonitor> DescribeScreens(
        IEnumerable<Screen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        return screens.Select((screen, index) => DescribeScreen(screen, index))
            .ToArray();
    }

    public static MainWindowPlacementResult Resolve(
        IReadOnlyList<MainWindowMonitor> monitors,
        string? preferredMonitorId,
        int applicationScalePercent,
        string? automaticMonitorId = null,
        ApplicationWindowPosition? lastPosition = null)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var requestedScale = ApplicationWindowScaleCatalog.Normalize(
                applicationScalePercent)
            / 100d;
        var savedMonitor = lastPosition is null
            ? null
            : FindMonitor(monitors, lastPosition.MonitorId)
                ?? (lastPosition.MonitorId is null
                    ? FindMonitorContainingPoint(
                        monitors,
                        new PixelPoint(lastPosition.X, lastPosition.Y))
                    : null);
        var preferredMonitor = FindMonitor(monitors, preferredMonitorId);
        var targetMonitor = savedMonitor
            ?? preferredMonitor
            ?? FindMonitor(monitors, automaticMonitorId)
            ?? monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? monitors.FirstOrDefault();
        var shouldPosition = lastPosition is not null
            || !string.IsNullOrWhiteSpace(preferredMonitorId);

        if (targetMonitor is null)
        {
            return CreateResult(
                requestedScale,
                monitor: null,
                position: null,
                usedPreferredMonitor: false);
        }

        var screenScale = double.IsFinite(targetMonitor.Scaling)
            && targetMonitor.Scaling > 0
                ? targetMonitor.Scaling
                : 1d;
        var workingArea = targetMonitor.WorkingArea.Width > 0
            && targetMonitor.WorkingArea.Height > 0
                ? targetMonitor.WorkingArea
                : targetMonitor.Bounds;
        var availableWidth = (workingArea.Width / screenScale)
            - (WorkingAreaMargin * 2);
        var availableHeight = (workingArea.Height / screenScale)
            - (WorkingAreaMargin * 2);
        var fitScale = Math.Min(
            availableWidth / DefaultWidth,
            availableHeight / DefaultHeight);
        var effectiveScale = fitScale > 0
            ? Math.Min(requestedScale, fitScale)
            : requestedScale;
        PixelPoint? position = null;
        if (shouldPosition)
        {
            var widthInPixels = (int)Math.Round(
                DefaultWidth * effectiveScale * screenScale);
            var heightInPixels = (int)Math.Round(
                DefaultHeight * effectiveScale * screenScale);
            position = savedMonitor is not null && lastPosition is not null
                ? ClampPosition(
                    new PixelPoint(lastPosition.X, lastPosition.Y),
                    workingArea,
                    widthInPixels,
                    heightInPixels)
                : new PixelPoint(
                    workingArea.X
                        + Math.Max(0, (workingArea.Width - widthInPixels) / 2),
                    workingArea.Y
                        + Math.Max(0, (workingArea.Height - heightInPixels) / 2));
        }

        return CreateResult(
            effectiveScale,
            targetMonitor,
            position,
            preferredMonitor is not null
                && ReferenceEquals(targetMonitor, preferredMonitor));
    }

    private static MainWindowPlacementResult CreateResult(
        double scale,
        MainWindowMonitor? monitor,
        PixelPoint? position,
        bool usedPreferredMonitor)
    {
        return new MainWindowPlacementResult(
            DefaultWidth * scale,
            DefaultHeight * scale,
            DefaultMinimumWidth * scale,
            DefaultMinimumHeight * scale,
            scale,
            monitor,
            position,
            usedPreferredMonitor);
    }

    private static MainWindowMonitor DescribeScreen(Screen screen, int index)
    {
        var displayName = screen.DisplayName?.Trim();
        var id = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : string.Create(
                CultureInfo.InvariantCulture,
                $"bounds:{screen.Bounds.X},{screen.Bounds.Y},"
                + $"{screen.Bounds.Width},{screen.Bounds.Height}");
        var friendlyName = displayName?.Replace(@"\\.\", string.Empty)
            ?? $"Monitor {index + 1}";
        var primary = screen.IsPrimary ? " (Primary)" : string.Empty;
        var displayScale = double.IsFinite(screen.Scaling) && screen.Scaling > 0
            ? screen.Scaling
            : 1d;
        var label = string.Create(
            CultureInfo.InvariantCulture,
            $"{friendlyName}{primary} - {screen.Bounds.Width} x "
            + $"{screen.Bounds.Height} - {displayScale * 100:0}%");
        return new MainWindowMonitor(
            id,
            label,
            screen.Bounds,
            screen.WorkingArea,
            displayScale,
            screen.IsPrimary);
    }

    private static MainWindowMonitor? FindMonitor(
        IEnumerable<MainWindowMonitor> monitors,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return monitors.FirstOrDefault(monitor => string.Equals(
            monitor.Id,
            id,
            comparison));
    }

    private static MainWindowMonitor? FindMonitorContainingPoint(
        IEnumerable<MainWindowMonitor> monitors,
        PixelPoint point)
    {
        return monitors.FirstOrDefault(monitor =>
            point.X >= monitor.Bounds.X
            && point.X < monitor.Bounds.X + monitor.Bounds.Width
            && point.Y >= monitor.Bounds.Y
            && point.Y < monitor.Bounds.Y + monitor.Bounds.Height);
    }

    private static PixelPoint ClampPosition(
        PixelPoint position,
        PixelRect workingArea,
        int windowWidth,
        int windowHeight)
    {
        var maximumX = workingArea.X
            + Math.Max(0, workingArea.Width - windowWidth);
        var maximumY = workingArea.Y
            + Math.Max(0, workingArea.Height - windowHeight);
        return new PixelPoint(
            Math.Clamp(position.X, workingArea.X, maximumX),
            Math.Clamp(position.Y, workingArea.Y, maximumY));
    }
}

internal sealed record MainWindowMonitor(
    string Id,
    string DisplayName,
    PixelRect Bounds,
    PixelRect WorkingArea,
    double Scaling,
    bool IsPrimary);

internal sealed record MainWindowPlacementResult(
    double Width,
    double Height,
    double MinimumWidth,
    double MinimumHeight,
    double ApplicationScale,
    MainWindowMonitor? Monitor,
    PixelPoint? Position,
    bool UsedPreferredMonitor);
