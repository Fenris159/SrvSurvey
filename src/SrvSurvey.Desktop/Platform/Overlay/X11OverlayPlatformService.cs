using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class X11OverlayPlatformService
    : IOverlayPlatformService, ICombinedOverlayNativeService
{
    private nint display;
    private readonly bool shapeAvailable;
    private readonly X11OverlayStackingMode stackingMode;
    private readonly nuint atomType;
    private readonly nuint windowTypeAtom;
    private readonly nuint kdeOnScreenDisplayAtom;
    private readonly nuint normalWindowAtom;

    private X11OverlayPlatformService(X11OverlayPlatformContext context)
    {
        display = context.Display;
        shapeAvailable = context.ShapeAvailable;
        stackingMode = context.StackingMode;
        atomType = context.AtomType;
        windowTypeAtom = context.WindowTypeAtom;
        kdeOnScreenDisplayAtom = context.KdeOnScreenDisplayAtom;
        normalWindowAtom = context.NormalWindowAtom;
        Capabilities = OverlayPlatformCapabilities.ForHost(context.Host)
            with
        {
            SupportsClickThrough = context.ShapeAvailable,
            SupportsGameWindowTracking = true,
        };
    }

    private sealed class X11OverlayPlatformContext
    {
        public nint Display { get; init; }

        public bool ShapeAvailable { get; init; }

        public OverlayHostKind Host { get; init; }

        public X11OverlayStackingMode StackingMode { get; init; }

        public nuint AtomType { get; init; }

        public nuint WindowTypeAtom { get; init; }

        public nuint KdeOnScreenDisplayAtom { get; init; }

        public nuint NormalWindowAtom { get; init; }
    }

    public OverlayPlatformCapabilities Capabilities { get; }

    public static IOverlayPlatformService? TryCreate(OverlayHostKind host)
    {
        if (!OperatingSystem.IsLinux()
            || !OverlayPlatformCapabilities.IsX11Compatible(host))
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

        nuint atomType = 0;
        nuint windowTypeAtom = 0;
        nuint kdeOnScreenDisplayAtom = 0;
        nuint normalWindowAtom = 0;
        var stackingMode = X11OverlayStackingMode.StandardTopmost;
        try
        {
            atomType = X11Native.XInternAtom(
                display,
                "ATOM",
                onlyIfExists: 1);
            windowTypeAtom = X11Native.XInternAtom(
                display,
                X11OverlayWindowManagerPolicy.WindowTypeAtomName,
                onlyIfExists: 0);
            kdeOnScreenDisplayAtom = X11Native.XInternAtom(
                display,
                X11OverlayWindowManagerPolicy.KdeOnScreenDisplayAtomName,
                onlyIfExists: 1);
            normalWindowAtom = X11Native.XInternAtom(
                display,
                X11OverlayWindowManagerPolicy.NormalWindowAtomName,
                onlyIfExists: 0);
            var supportedAtoms = ReadSupportedAtoms(display, atomType);
            stackingMode = X11OverlayWindowManagerPolicy.Select(
                kdeOnScreenDisplayAtom,
                supportedAtoms);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            Trace.TraceWarning(
                "X11 window-manager capabilities could not be queried; "
                + $"using standard topmost overlays: {exception.Message}");
        }

        Trace.TraceInformation(
            stackingMode == X11OverlayStackingMode.KdeOnScreenDisplay
                ? "X11 overlay stacking policy: KDE on-screen display (advertised by the window manager)."
                : "X11 overlay stacking policy: standard topmost (KDE on-screen display support was not advertised).");

        return new X11OverlayPlatformService(
            new X11OverlayPlatformContext
    {
        Display = display,
        ShapeAvailable = shapeAvailable,
        Host = host,
        StackingMode = stackingMode,
        AtomType = atomType,
        WindowTypeAtom = windowTypeAtom,
        KdeOnScreenDisplayAtom = kdeOnScreenDisplayAtom,
        NormalWindowAtom = normalWindowAtom
    });
    }

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
        if (display == nint.Zero || !shapeAvailable)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                Capabilities.StatusText);
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                "The native X11 overlay window is not available.");
        }

        try
        {
            var stackingApplied = ApplyWindowType(handle);
            if (interactive)
            {
                X11Native.XShapeCombineMask(
                    display,
                    unchecked((nuint)handle),
                    X11Native.ShapeInput,
                    0,
                    0,
                    0,
                    X11Native.ShapeSet);
            }
            else
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
            }

            _ = X11Native.XFlush(display);
            window.IsHitTestVisible = interactive;
            return new OverlayInteractionResult(
                IsPrepared: true,
                IsInteractive: interactive,
                CreateStatus(interactive, stackingApplied));
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                $"X11 overlay interaction mode could not be changed: {exception.Message}");
        }
    }

    public void BeginMoveDrag(
        Window window,
        PointerPressedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(eventArgs);
        if (stackingMode == X11OverlayStackingMode.KdeOnScreenDisplay)
        {
            ManagedOverlayWindowDragSession.Begin(window, eventArgs);
            return;
        }

        window.BeginMoveDrag(eventArgs);
    }

    public bool SuppressNativeWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (display == nint.Zero)
        {
            return false;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return false;
        }

        try
        {
            _ = X11Native.XUnmapWindow(
                display,
                unchecked((nuint)handle));
            _ = X11Native.XFlush(display);
            return true;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return false;
        }
    }

    public OverlayInteractionResult SetInteractiveRegions(
        Window window,
        IReadOnlyList<PixelRect> regions)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(regions);
        if (display == nint.Zero || !shapeAvailable)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                Capabilities.StatusText);
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                "The native X11 overlay host is not available.");
        }

        if (regions.Count == 0)
        {
            return SetInteractive(window, interactive: false);
        }

        try
        {
            var rectangles = new X11Native.XRectangle[regions.Count];
            var rectangleCount = 0;
            foreach (var region in regions)
            {
                if (region.Width <= 0 || region.Height <= 0)
                {
                    continue;
                }

                var left = Math.Clamp(region.X, short.MinValue, short.MaxValue);
                var top = Math.Clamp(region.Y, short.MinValue, short.MaxValue);
                var width = Math.Clamp(region.Width, 1, ushort.MaxValue);
                var height = Math.Clamp(region.Height, 1, ushort.MaxValue);
                rectangles[rectangleCount++] = new X11Native.XRectangle
                {
                    X = (short)left,
                    Y = (short)top,
                    Width = (ushort)width,
                    Height = (ushort)height,
                };
            }

            if (rectangleCount == 0)
            {
                return SetInteractive(window, interactive: false);
            }

            var stackingApplied = ApplyWindowType(handle);
            var pinnedRectangles = GCHandle.Alloc(
                rectangles,
                GCHandleType.Pinned);
            try
            {
                X11Native.XShapeCombineRectangles(
                    display,
                    unchecked((nuint)handle),
                    X11Native.ShapeInput,
                    0,
                    0,
                    pinnedRectangles.AddrOfPinnedObject(),
                    rectangleCount,
                    X11Native.ShapeSet,
                    X11Native.Unsorted);
            }
            finally
            {
                pinnedRectangles.Free();
            }
            _ = X11Native.XFlush(display);
            window.IsHitTestVisible = true;
            return new OverlayInteractionResult(
                IsPrepared: true,
                IsInteractive: true,
                stackingApplied
                    ? "Combined overlay edit mode is active through the X11 input region."
                    : "Combined overlay edit mode is active, but the preferred stacking hint could not be applied.");
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                $"The combined X11 overlay input region could not be changed: {exception.Message}");
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

    private static nuint[] ReadSupportedAtoms(nint display, nuint atomType)
    {
        if (atomType == 0)
        {
            return [];
        }

        var supportedAtom = X11Native.XInternAtom(
            display,
            X11OverlayWindowManagerPolicy.SupportedAtomName,
            onlyIfExists: 1);
        if (supportedAtom == 0)
        {
            return [];
        }

        var root = X11Native.XDefaultRootWindow(display);
        var status = X11Native.XGetWindowProperty(
            display,
            root,
            supportedAtom,
            0,
            16_384,
            delete: 0,
            atomType,
            out var actualType,
            out var actualFormat,
            out var itemCount,
            out _,
            out var propertyData);
        try
        {
            if (status != 0
                || propertyData == nint.Zero
                || actualType != atomType
                || actualFormat != 32
                || itemCount > int.MaxValue)
            {
                return [];
            }

            var atoms = new nuint[(int)itemCount];
            for (var index = 0; index < atoms.Length; index++)
            {
                atoms[index] = unchecked((nuint)Marshal.ReadIntPtr(
                    propertyData,
                    index * nint.Size));
            }

            return atoms;
        }
        finally
        {
            if (propertyData != nint.Zero)
            {
                _ = X11Native.XFree(propertyData);
            }
        }
    }

    private bool ApplyWindowType(nint handle)
    {
        var windowTypes = X11OverlayWindowManagerPolicy.CreateWindowTypes(
            stackingMode,
            kdeOnScreenDisplayAtom,
            normalWindowAtom);
        if (windowTypes.Length == 0)
        {
            return stackingMode == X11OverlayStackingMode.StandardTopmost;
        }

        if (atomType == 0 || windowTypeAtom == 0)
        {
            return false;
        }

        var values = new nint[windowTypes.Length];
        for (var index = 0; index < windowTypes.Length; index++)
        {
            values[index] = unchecked((nint)windowTypes[index]);
        }

        var pinnedValues = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            return X11Native.XChangeProperty(
                display,
                unchecked((nuint)handle),
                windowTypeAtom,
                atomType,
                format: 32,
                X11Native.PropertyReplace,
                pinnedValues.AddrOfPinnedObject(),
                windowTypes.Length) != 0;
        }
        finally
        {
            pinnedValues.Free();
        }
    }

    private string CreateStatus(bool interactive, bool stackingApplied)
    {
        if (!stackingApplied)
        {
            return "The KDE on-screen-display stacking hint could not be applied; the overlay is using standard topmost behavior.";
        }

        if (!interactive)
        {
            return Capabilities.StatusText;
        }

        return stackingMode == X11OverlayStackingMode.KdeOnScreenDisplay
            ? "Overlay edit mode is active through the X11 input region with KDE on-screen-display stacking."
            : "Overlay edit mode is active through the X11 input region.";
    }
}
