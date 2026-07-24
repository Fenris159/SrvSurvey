using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IOverlayPlatformService : IDisposable
{
    OverlayPlatformCapabilities Capabilities { get; }

    OverlayPreparationResult PreparePassiveWindow(Window window);
}

public static class OverlayPlatformService
{
    public static IOverlayPlatformService CreateCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsOverlayPlatformService();
        }

        var capabilities = OverlayPlatformCapabilities.DetectCurrent();
        if (capabilities.Host == OverlayHostKind.LinuxX11)
        {
            return X11OverlayPlatformService.TryCreate()
                ?? new PortableOverlayPlatformService(
                    capabilities with
                    {
                        SupportsClickThrough = false,
                        SupportsGameWindowTracking = false,
                    });
        }

        return new PortableOverlayPlatformService(capabilities);
    }
}

public sealed record OverlayPreparationResult(
    bool IsPrepared,
    bool IsClickThrough,
    string Status);

internal sealed class PortableOverlayPlatformService(
    OverlayPlatformCapabilities capabilities) : IOverlayPlatformService
{
    public OverlayPlatformCapabilities Capabilities { get; } = capabilities;

    public OverlayPreparationResult PreparePassiveWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new OverlayPreparationResult(
            Capabilities.SupportsPassiveOverlay,
            IsClickThrough: false,
            Capabilities.StatusText);
    }

    public void Dispose()
    {
    }
}

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsOverlayPlatformService
    : IOverlayPlatformService
{
    private const int ExtendedWindowStyle = -20;
    private const long ToolWindow = 0x00000080L;
    private const long Transparent = 0x00000020L;
    private const long Layered = 0x00080000L;
    private const long NoActivate = 0x08000000L;

    public OverlayPlatformCapabilities Capabilities { get; } =
        OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

    public OverlayPreparationResult PreparePassiveWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return new OverlayPreparationResult(
                IsPrepared: false,
                IsClickThrough: false,
                "The native Windows overlay handle is not available.");
        }

        var style = GetWindowLongPtr(handle, ExtendedWindowStyle);
        var updated = style | (nint)(ToolWindow | Transparent | Layered | NoActivate);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(handle, ExtendedWindowStyle, updated);
        var error = Marshal.GetLastPInvokeError();
        if (previous == nint.Zero && error != 0)
        {
            return new OverlayPreparationResult(
                IsPrepared: false,
                IsClickThrough: false,
                $"Windows click-through could not be enabled (error {error}).");
        }

        window.IsHitTestVisible = false;
        return new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: true,
            Capabilities.StatusText);
    }

    public void Dispose()
    {
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        SetLastError = true)]
    private static partial nint SetWindowLongPtr(
        nint window,
        int index,
        nint value);
}
