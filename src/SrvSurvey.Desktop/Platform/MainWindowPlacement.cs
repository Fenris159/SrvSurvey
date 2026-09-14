using System.Globalization;
using Avalonia;
using Avalonia.Platform;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform;

internal static class MainWindowPlacement
{
    internal const double DefaultWidth = 1180;
    internal const double DefaultHeight = 760;
    internal const double DefaultMinimumWidth = 860;
    internal const double DefaultMinimumHeight = 600;
    private const double WorkingAreaMargin = 24;

    public static IReadOnlyList<MainWindowMonitor> DescribeScreens(IEnumerable<Screen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        return screens.Select((screen, index) => DescribeScreen(screen, index)).ToArray();
    }

    public static MainWindowPlacementResult Resolve(
        IReadOnlyList<MainWindowMonitor> monitors,
        string? preferredMonitorId,
        int applicationScalePercent,
        string? automaticMonitorId = null,
        ApplicationWindowPosition? lastPosition = null
    )
    {
        ArgumentNullException.ThrowIfNull(monitors);
        double requestedScale = ApplicationWindowScaleCatalog.Normalize(applicationScalePercent) / 100d;
        MainWindowMonitor? savedMonitor = ResolveSavedMonitor(monitors, lastPosition);

        MainWindowMonitor? preferredMonitor = FindMonitor(monitors, preferredMonitorId);
        MainWindowMonitor? targetMonitor =
            savedMonitor
            ?? preferredMonitor
            ?? FindMonitor(monitors, automaticMonitorId)
            ?? monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? (monitors.Count > 0 ? monitors[0] : null);
        bool shouldPosition = lastPosition is not null || !string.IsNullOrWhiteSpace(preferredMonitorId);

        if (targetMonitor is null)
        {
            return CreateResult(requestedScale, monitor: null, position: null, usedPreferredMonitor: false);
        }

        double screenScale =
            double.IsFinite(targetMonitor.Scaling) && targetMonitor.Scaling > 0 ? targetMonitor.Scaling : 1d;
        PixelRect workingArea =
            targetMonitor.WorkingArea.Width > 0 && targetMonitor.WorkingArea.Height > 0
                ? targetMonitor.WorkingArea
                : targetMonitor.Bounds;
        double availableWidth = (workingArea.Width / screenScale) - (WorkingAreaMargin * 2);
        double availableHeight = (workingArea.Height / screenScale) - (WorkingAreaMargin * 2);
        double fitScale = Math.Min(availableWidth / DefaultWidth, availableHeight / DefaultHeight);
        double effectiveScale = fitScale > 0 ? Math.Min(requestedScale, fitScale) : requestedScale;
        PixelPoint? position = ResolvePosition(
            shouldPosition,
            savedMonitor,
            lastPosition,
            workingArea,
            effectiveScale,
            screenScale
        );

        return CreateResult(
            effectiveScale,
            targetMonitor,
            position,
            preferredMonitor is not null && ReferenceEquals(targetMonitor, preferredMonitor)
        );
    }

    private static MainWindowMonitor? ResolveSavedMonitor(
        IReadOnlyList<MainWindowMonitor> monitors,
        ApplicationWindowPosition? lastPosition
    )
    {
        if (lastPosition is null)
        {
            return null;
        }

        MainWindowMonitor? monitor = FindMonitor(monitors, lastPosition.MonitorId);
        return monitor
            ?? (
                lastPosition.MonitorId is null
                    ? FindMonitorContainingPoint(monitors, new PixelPoint(lastPosition.X, lastPosition.Y))
                    : null
            );
    }

    private static PixelPoint? ResolvePosition(
        bool shouldPosition,
        MainWindowMonitor? savedMonitor,
        ApplicationWindowPosition? lastPosition,
        PixelRect workingArea,
        double effectiveScale,
        double screenScale
    )
    {
        if (!shouldPosition)
        {
            return null;
        }

        int widthInPixels = (int)Math.Round(DefaultWidth * effectiveScale * screenScale);
        int heightInPixels = (int)Math.Round(DefaultHeight * effectiveScale * screenScale);
        return savedMonitor is not null
            ? ClampPosition(new PixelPoint(lastPosition!.X, lastPosition.Y), workingArea, widthInPixels, heightInPixels)
            : new PixelPoint(
                workingArea.X + Math.Max(0, (workingArea.Width - widthInPixels) / 2),
                workingArea.Y + Math.Max(0, (workingArea.Height - heightInPixels) / 2)
            );
    }

    private static MainWindowPlacementResult CreateResult(
        double scale,
        MainWindowMonitor? monitor,
        PixelPoint? position,
        bool usedPreferredMonitor
    )
    {
        return new MainWindowPlacementResult(
            DefaultWidth * scale,
            DefaultHeight * scale,
            DefaultMinimumWidth * scale,
            DefaultMinimumHeight * scale,
            scale,
            monitor,
            position,
            usedPreferredMonitor
        );
    }

    private static MainWindowMonitor DescribeScreen(Screen screen, int index)
    {
        string? displayName = screen.DisplayName?.Trim();
        string id = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : string.Create(
                CultureInfo.InvariantCulture,
                $"bounds:{screen.Bounds.X},{screen.Bounds.Y}," + $"{screen.Bounds.Width},{screen.Bounds.Height}"
            );
        string friendlyName = displayName?.Replace(@"\\.\", string.Empty) ?? $"Monitor {index + 1}";
        string primary = screen.IsPrimary ? " (Primary)" : string.Empty;
        double displayScale = double.IsFinite(screen.Scaling) && screen.Scaling > 0 ? screen.Scaling : 1d;
        string label = string.Create(
            CultureInfo.InvariantCulture,
            $"{friendlyName}{primary} - {screen.Bounds.Width} x " + $"{screen.Bounds.Height} - {displayScale * 100:0}%"
        );
        return new MainWindowMonitor(id, label, screen.Bounds, screen.WorkingArea, displayScale, screen.IsPrimary);
    }

    private static MainWindowMonitor? FindMonitor(IEnumerable<MainWindowMonitor> monitors, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return monitors.FirstOrDefault(monitor => string.Equals(monitor.Id, id, comparison));
    }

    private static MainWindowMonitor? FindMonitorContainingPoint(
        IEnumerable<MainWindowMonitor> monitors,
        PixelPoint point
    )
    {
        return monitors.FirstOrDefault(monitor =>
            point.X >= monitor.Bounds.X
            && point.X < monitor.Bounds.X + monitor.Bounds.Width
            && point.Y >= monitor.Bounds.Y
            && point.Y < monitor.Bounds.Y + monitor.Bounds.Height
        );
    }

    private static PixelPoint ClampPosition(
        PixelPoint position,
        PixelRect workingArea,
        int windowWidth,
        int windowHeight
    )
    {
        int maximumX = workingArea.X + Math.Max(0, workingArea.Width - windowWidth);
        int maximumY = workingArea.Y + Math.Max(0, workingArea.Height - windowHeight);
        return new PixelPoint(
            Math.Clamp(position.X, workingArea.X, maximumX),
            Math.Clamp(position.Y, workingArea.Y, maximumY)
        );
    }
}

internal sealed record MainWindowMonitor(
    string Id,
    string DisplayName,
    PixelRect Bounds,
    PixelRect WorkingArea,
    double Scaling,
    bool IsPrimary
);

internal sealed record MainWindowPlacementResult(
    double Width,
    double Height,
    double MinimumWidth,
    double MinimumHeight,
    double ApplicationScale,
    MainWindowMonitor? Monitor,
    PixelPoint? Position,
    bool UsedPreferredMonitor
);
