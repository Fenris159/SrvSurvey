using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal readonly record struct OverlayScreenGeometry(PixelRect Bounds, PixelRect WorkingArea);

public static class OverlayWindowPlacement
{
    private const int CrossMonitorInsetTolerance = 32;

    internal static PixelRect GetReliableBottomWorkingArea(
        OverlayScreenGeometry targetScreen,
        IReadOnlyList<OverlayScreenGeometry> screens
    )
    {
        ArgumentNullException.ThrowIfNull(screens);
        ValidateBounds(targetScreen.Bounds, nameof(targetScreen));
        ValidateBounds(targetScreen.WorkingArea, nameof(targetScreen));
        ArgumentOutOfRangeException.ThrowIfZero(screens.Count);

        foreach (OverlayScreenGeometry screen in screens)
        {
            ValidateBounds(screen.Bounds, nameof(screens));
            ValidateBounds(screen.WorkingArea, nameof(screens));
        }

        int targetInset = GetBottomInset(targetScreen);
        int smallestPositiveInset = targetInset;
        foreach (OverlayScreenGeometry screen in screens)
        {
            int inset = GetBottomInset(screen);
            if (inset > 0)
            {
                smallestPositiveInset = Math.Min(smallestPositiveInset, inset);
            }
        }

        if (targetInset <= ((smallestPositiveInset * 2L) + CrossMonitorInsetTolerance))
        {
            return targetScreen.WorkingArea;
        }

        int correctedBottom = targetScreen.Bounds.Bottom - smallestPositiveInset;
        int correctedHeight = correctedBottom - targetScreen.WorkingArea.Y;
        return correctedHeight > 0
            ? new PixelRect(
                targetScreen.WorkingArea.X,
                targetScreen.WorkingArea.Y,
                targetScreen.WorkingArea.Width,
                correctedHeight
            )
            : targetScreen.WorkingArea;
    }

    public static PixelRect GetUsableBounds(PixelRect hostBounds, PixelRect workingArea)
    {
        ValidateBounds(hostBounds, nameof(hostBounds));
        ValidateBounds(workingArea, nameof(workingArea));

        int left = Math.Max(hostBounds.X, workingArea.X);
        int top = Math.Max(hostBounds.Y, workingArea.Y);
        int right = Math.Min(hostBounds.Right, workingArea.Right);
        int bottom = Math.Min(hostBounds.Bottom, workingArea.Bottom);
        return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : workingArea;
    }

    public static PixelPoint TopCenter(PixelRect gameClientBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(gameClientBounds, overlaySize, margin);
        int availableWidth = Math.Max(0, gameClientBounds.Width - (margin * 2));
        int centeredOffset = Math.Max(0, (availableWidth - overlaySize.Width) / 2);
        return new PixelPoint(gameClientBounds.X + margin + centeredOffset, gameClientBounds.Y + margin);
    }

    public static PixelPoint TopLeft(PixelRect hostBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        return new PixelPoint(hostBounds.X + margin, hostBounds.Y + margin);
    }

    public static PixelPoint TopRight(PixelRect hostBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        return new PixelPoint(
            Math.Max(hostBounds.X + margin, hostBounds.Right - overlaySize.Width - margin),
            hostBounds.Y + margin
        );
    }

    public static PixelPoint BottomRight(PixelRect hostBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        return new PixelPoint(
            Math.Max(hostBounds.X + margin, hostBounds.Right - overlaySize.Width - margin),
            Math.Max(hostBounds.Y + margin, hostBounds.Bottom - overlaySize.Height - margin)
        );
    }

    public static PixelPoint MiddleRight(PixelRect hostBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        int availableHeight = Math.Max(0, hostBounds.Height - (margin * 2));
        int centeredOffset = Math.Max(0, (availableHeight - overlaySize.Height) / 2);
        return new PixelPoint(
            Math.Max(hostBounds.X + margin, hostBounds.Right - overlaySize.Width - margin),
            hostBounds.Y + margin + centeredOffset
        );
    }

    public static PixelPoint MiddleLeft(PixelRect hostBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        int availableHeight = Math.Max(0, hostBounds.Height - (margin * 2));
        int centeredOffset = Math.Max(0, (availableHeight - overlaySize.Height) / 2);
        return new PixelPoint(hostBounds.X + margin, hostBounds.Y + margin + centeredOffset);
    }

    public static PixelPoint BottomLeft(PixelRect hostBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        return new PixelPoint(
            hostBounds.X + margin,
            Math.Max(hostBounds.Y + margin, hostBounds.Bottom - overlaySize.Height - margin)
        );
    }

    public static PixelPoint BottomCenter(PixelRect hostBounds, PixelSize overlaySize, int margin = 20)
    {
        Validate(hostBounds, overlaySize, margin);
        int availableWidth = Math.Max(0, hostBounds.Width - (margin * 2));
        int centeredOffset = Math.Max(0, (availableWidth - overlaySize.Width) / 2);
        return new PixelPoint(
            hostBounds.X + margin + centeredOffset,
            Math.Max(hostBounds.Y + margin, hostBounds.Bottom - overlaySize.Height - margin)
        );
    }

    private static void Validate(PixelRect hostBounds, PixelSize overlaySize, int margin)
    {
        ValidateBounds(hostBounds, nameof(hostBounds));

        if (overlaySize.Width <= 0 || overlaySize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlaySize), "Overlay size must have a positive size.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(margin);
    }

    private static void ValidateBounds(PixelRect bounds, string parameterName)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Bounds must have a positive size.");
        }
    }

    private static int GetBottomInset(OverlayScreenGeometry screen) =>
        Math.Max(0, screen.Bounds.Bottom - screen.WorkingArea.Bottom);
}
