using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IOverlayPlatformService : IDisposable
{
    OverlayPlatformCapabilities Capabilities { get; }

    OverlayPreparationResult PreparePassiveWindow(Window window);

    OverlayInteractionResult SetInteractive(Window window, bool interactive);
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

    public OverlayInteractionResult SetInteractive(Window window, bool interactive)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new OverlayInteractionResult(
            IsPrepared: false,
            IsInteractive: false,
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
    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint NoZOrder = 0x0004;
    private const uint DoNotActivate = 0x0010;
    private const uint FrameChanged = 0x0020;

    public OverlayPlatformCapabilities Capabilities { get; } =
        OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

    public OverlayPreparationResult PreparePassiveWindow(Window window)
    {
        var result = SetInteractive(window, interactive: false);
        return new OverlayPreparationResult(
            result.IsPrepared,
            IsClickThrough: result.IsPrepared && !result.IsInteractive,
            result.Status);
    }

    public OverlayInteractionResult SetInteractive(Window window, bool interactive)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                "The native Windows overlay handle is not available.");
        }

        var style = GetWindowLongPtr(handle, ExtendedWindowStyle);
        var updated = interactive
            ? (style | (nint)(ToolWindow | Layered))
                & ~(nint)(Transparent | NoActivate)
            : style | (nint)(ToolWindow | Transparent | Layered | NoActivate);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(handle, ExtendedWindowStyle, updated);
        var error = Marshal.GetLastPInvokeError();
        if (previous == nint.Zero && error != 0)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                $"Windows overlay interaction mode could not be changed (error {error}).");
        }

        if (!SetWindowPos(
                handle,
                nint.Zero,
                0,
                0,
                0,
                0,
                NoSize | NoMove | NoZOrder | DoNotActivate | FrameChanged))
        {
            error = Marshal.GetLastPInvokeError();
            _ = SetWindowLongPtr(handle, ExtendedWindowStyle, style);
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                $"Windows overlay interaction mode could not be refreshed (error {error}).");
        }

        window.IsHitTestVisible = interactive;
        return new OverlayInteractionResult(
            IsPrepared: true,
            IsInteractive: interactive,
            interactive
                ? "Overlay edit mode is active on Windows."
                : Capabilities.StatusText);
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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

public sealed record OverlayInteractionResult(
    bool IsPrepared,
    bool IsInteractive,
    string Status);
