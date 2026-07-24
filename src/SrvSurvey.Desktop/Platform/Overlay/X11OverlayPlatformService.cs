using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class X11OverlayPlatformService : IOverlayPlatformService
{
    private nint display;
    private readonly bool shapeAvailable;

    private X11OverlayPlatformService(nint display, bool shapeAvailable)
    {
        this.display = display;
        this.shapeAvailable = shapeAvailable;
        Capabilities = OverlayPlatformCapabilities.ForHost(
                OverlayHostKind.LinuxX11)
            with
        {
            SupportsClickThrough = shapeAvailable,
            SupportsGameWindowTracking = true,
        };
    }

    public OverlayPlatformCapabilities Capabilities { get; }

    public static IOverlayPlatformService? TryCreate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        nint display;
        try
        {
            display = X11Native.XOpenDisplay(nint.Zero);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return null;
        }

        if (display == nint.Zero)
        {
            return null;
        }

        var shapeAvailable = false;
        try
        {
            shapeAvailable = X11Native.XShapeQueryExtension(
                display,
                out _,
                out _) != 0;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            // X11 tracking can still work when the XShape extension is missing.
        }

        return new X11OverlayPlatformService(display, shapeAvailable);
    }

    public OverlayPreparationResult PreparePassiveWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (display == nint.Zero || !shapeAvailable)
        {
            return new OverlayPreparationResult(
                IsPrepared: false,
                IsClickThrough: false,
                Capabilities.StatusText);
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return new OverlayPreparationResult(
                IsPrepared: false,
                IsClickThrough: false,
                "The native X11 overlay window is not available.");
        }

        try
        {
            X11Native.XShapeCombineRectangles(
                display,
                unchecked((nuint)handle),
                X11Native.ShapeInput,
                0,
                0,
                nint.Zero,
                0,
                X11Native.ShapeSet,
                X11Native.Unsorted);
            _ = X11Native.XFlush(display);
            window.IsHitTestVisible = false;
            return new OverlayPreparationResult(
                IsPrepared: true,
                IsClickThrough: true,
                Capabilities.StatusText);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return new OverlayPreparationResult(
                IsPrepared: false,
                IsClickThrough: false,
                $"X11 click-through could not be enabled: {exception.Message}");
        }
    }

    public void Dispose()
    {
        var currentDisplay = display;
        display = nint.Zero;
        if (currentDisplay != nint.Zero)
        {
            _ = X11Native.XCloseDisplay(currentDisplay);
        }
    }
}
