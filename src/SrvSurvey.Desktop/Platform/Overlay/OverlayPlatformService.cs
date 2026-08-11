using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IOverlayPlatformService : IDisposable
{
    OverlayPlatformCapabilities Capabilities { get; }

    OverlayPreparationResult PreparePassiveWindow(Window window);

    OverlayInteractionResult SetInteractive(Window window, bool interactive);

    IDisposable? BeginVisibleCursorSession(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Activate();
        return null;
    }

    OverlayInteractionResult PrepareInteractiveWindow(Window window)
    {
        return SetInteractive(window, interactive: true);
    }

    void BeginMoveDrag(Window window, PointerPressedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(eventArgs);
        window.BeginMoveDrag(eventArgs);
    }

}

internal interface ICombinedOverlayNativeService
{
    bool SuppressNativeWindow(Window window);

    OverlayInteractionResult SetInteractiveRegions(
        Window window,
        IReadOnlyList<PixelRect> regions);
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
        if (capabilities.UsesX11Compatibility)
        {
            return X11OverlayPlatformService.TryCreate(capabilities.Host)
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
    : IOverlayPlatformService, ICombinedOverlayNativeService
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
    private const int HideWindow = 0;
    private const int RegionOr = 2;
    private const int ArrowCursorResource = 32512;

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

    public IDisposable BeginVisibleCursorSession(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        var previousForeground = GetForegroundWindow();
        window.Activate();
        if (handle != nint.Zero)
        {
            ActivateOverlayWindow(handle, previousForeground);
        }

        var visibility = CursorVisibilitySession.Begin(ShowCursor);
        var arrow = LoadCursor(nint.Zero, (nint)ArrowCursorResource);
        if (arrow != nint.Zero)
        {
            _ = SetCursor(arrow);
        }

        return new ForegroundCursorVisibilitySession(
            visibility,
            handle,
            previousForeground,
            GetForegroundWindow,
            SetForegroundWindow,
            IsCurrentProcessOverlayWindow);
    }

    public void Dispose()
    {
    }

    public bool SuppressNativeWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return false;
        }

        _ = ShowWindow(handle, HideWindow);
        return true;
    }

    public OverlayInteractionResult SetInteractiveRegions(
        Window window,
        IReadOnlyList<PixelRect> regions)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(regions);
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                "The native Windows overlay host is not available.");
        }

        if (regions.Count == 0)
        {
            _ = SetWindowRgn(handle, nint.Zero, redraw: true);
            return SetInteractive(window, interactive: false);
        }

        var combinedRegion = CreateRectRgn(0, 0, 0, 0);
        if (combinedRegion == nint.Zero)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                "The Windows overlay input region could not be created.");
        }

        var regionTransferred = false;
        try
        {
            foreach (var rectangle in regions)
            {
                var right = checked(rectangle.X + rectangle.Width);
                var bottom = checked(rectangle.Y + rectangle.Height);
                var part = CreateRectRgn(
                    rectangle.X,
                    rectangle.Y,
                    right,
                    bottom);
                if (part == nint.Zero)
                {
                    continue;
                }

                try
                {
                    _ = CombineRgn(
                        combinedRegion,
                        combinedRegion,
                        part,
                        RegionOr);
                }
                finally
                {
                    _ = DeleteObject(part);
                }
            }

            var interaction = SetInteractive(window, interactive: true);
            if (!interaction.IsPrepared || !interaction.IsInteractive)
            {
                return interaction;
            }

            if (SetWindowRgn(handle, combinedRegion, redraw: true) == 0)
            {
                _ = SetInteractive(window, interactive: false);
                return new OverlayInteractionResult(
                    IsPrepared: false,
                    IsInteractive: false,
                    "The Windows overlay input region could not be applied.");
            }

            regionTransferred = true;
            return new OverlayInteractionResult(
                IsPrepared: true,
                IsInteractive: true,
                "Combined overlay edit mode is active on Windows.");
        }
        finally
        {
            if (!regionTransferred)
            {
                _ = DeleteObject(combinedRegion);
            }
        }
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    private static partial int ShowCursor(
        [MarshalAs(UnmanagedType.Bool)] bool show);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SetActiveWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(
        uint attachThread,
        uint attachToThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(
        nint instance,
        nint cursorName);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(
        nint window,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [LibraryImport("gdi32.dll")]
    private static partial int CombineRgn(
        nint destination,
        nint source1,
        nint source2,
        int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint objectHandle);

    private static void ActivateOverlayWindow(
        nint handle,
        nint previousForeground)
    {
        var currentThread = GetCurrentThreadId();
        var foregroundThread = previousForeground == nint.Zero
            ? 0
            : GetWindowThreadProcessId(previousForeground, out _);
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, attach: true);
        try
        {
            _ = BringWindowToTop(handle);
            _ = SetForegroundWindow(handle);
            _ = SetActiveWindow(handle);
            _ = SetFocus(handle);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: false);
            }
        }
    }

    private static bool IsCurrentProcessOverlayWindow(nint handle)
    {
        if (handle == nint.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        return processId == (uint)Environment.ProcessId
            && ((long)GetWindowLongPtr(handle, ExtendedWindowStyle)
                & ToolWindow) != 0;
    }

}

internal sealed class CursorVisibilitySession : IDisposable
{
    private const int MaximumAdjustments = 64;
    private readonly Func<bool, int> showCursor;
    private int remainingAdjustments;

    private CursorVisibilitySession(
        Func<bool, int> showCursor,
        int adjustments)
    {
        this.showCursor = showCursor;
        remainingAdjustments = adjustments;
    }

    public static CursorVisibilitySession Begin(Func<bool, int> showCursor)
    {
        ArgumentNullException.ThrowIfNull(showCursor);

        var adjustments = 1;
        var displayCount = showCursor(true);
        while (displayCount < 0 && adjustments < MaximumAdjustments)
        {
            displayCount = showCursor(true);
            adjustments++;
        }

        return new CursorVisibilitySession(showCursor, adjustments);
    }

    public void Dispose()
    {
        var remaining = Interlocked.Exchange(
            ref remainingAdjustments,
            0);
        for (var index = 0; index < remaining; index++)
        {
            _ = showCursor(false);
        }
    }
}

internal sealed class ForegroundCursorVisibilitySession : IDisposable
{
    private readonly nint interactionWindow;
    private readonly nint previousForeground;
    private readonly Func<nint> getForegroundWindow;
    private readonly Func<nint, bool> setForegroundWindow;
    private readonly Func<nint, bool> isInteractionWindow;
    private IDisposable? cursorVisibilitySession;

    public ForegroundCursorVisibilitySession(
        IDisposable cursorVisibilitySession,
        nint interactionWindow,
        nint previousForeground,
        Func<nint> getForegroundWindow,
        Func<nint, bool> setForegroundWindow,
        Func<nint, bool> isInteractionWindow)
    {
        this.cursorVisibilitySession = cursorVisibilitySession
            ?? throw new ArgumentNullException(nameof(cursorVisibilitySession));
        this.interactionWindow = interactionWindow;
        this.previousForeground = previousForeground;
        this.getForegroundWindow = getForegroundWindow
            ?? throw new ArgumentNullException(nameof(getForegroundWindow));
        this.setForegroundWindow = setForegroundWindow
            ?? throw new ArgumentNullException(nameof(setForegroundWindow));
        this.isInteractionWindow = isInteractionWindow
            ?? throw new ArgumentNullException(nameof(isInteractionWindow));
    }

    public void Dispose()
    {
        var visibility = Interlocked.Exchange(
            ref cursorVisibilitySession,
            null);
        if (visibility is null)
        {
            return;
        }

        visibility.Dispose();
        var currentForeground = getForegroundWindow();
        if (previousForeground != nint.Zero
            && previousForeground != interactionWindow
            && (currentForeground == interactionWindow
                || isInteractionWindow(currentForeground)))
        {
            _ = setForegroundWindow(previousForeground);
        }
    }
}

public sealed record OverlayInteractionResult(
    bool IsPrepared,
    bool IsInteractive,
    string Status);
