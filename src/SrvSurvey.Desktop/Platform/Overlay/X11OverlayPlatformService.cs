using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class X11OverlayPlatformService
    : IOverlayPlatformService, ICombinedOverlayNativeService
{
    private const int ClientMessage = 33;
    private const int RevertToParent = 2;
    private const uint LeftPointerCursor = 68;
    private const nint SubstructureNotifyMask = 1 << 19;
    private const nint SubstructureRedirectMask = 1 << 20;
    private static readonly X11Native.XErrorHandler ErrorHandler = HandleXError;
    private static readonly object ErrorHandlerSync = new();
    private static readonly ConcurrentDictionary<nint, byte>
        ErrorHandledDisplays = new();
    private static nint previousErrorHandlerPointer;
    private static bool errorHandlerInstalled;
    private readonly object displaySync = new();
    private nint display;
    private readonly HashSet<nuint> interactiveWindowHandles = [];
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
            EnsureErrorHandlerInstalled();
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

        RegisterErrorHandledDisplay(display);

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
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return new OverlayInteractionResult(
                IsPrepared: false,
                IsInteractive: false,
                "The native X11 overlay window is not available.");
        }

        lock (displaySync)
        {
            if (!TryGetDisplay(out var currentDisplay) || !shapeAvailable)
            {
                return new OverlayInteractionResult(
                    IsPrepared: false,
                    IsInteractive: false,
                    Capabilities.StatusText);
            }

            var windowHandle = unchecked((nuint)handle);
            if (!interactive)
            {
                interactiveWindowHandles.Remove(windowHandle);
            }

            try
            {
                if (!IsValidWindow(currentDisplay, windowHandle))
                {
                    return new OverlayInteractionResult(
                        IsPrepared: false,
                        IsInteractive: false,
                        "The native X11 overlay window is no longer available.");
                }

                var stackingApplied = ApplyWindowType(currentDisplay, handle);
                if (interactive)
                {
                    X11Native.XShapeCombineMask(
                        currentDisplay,
                        windowHandle,
                        X11Native.ShapeInput,
                        0,
                        0,
                        0,
                        X11Native.ShapeSet);
                }
                else
                {
                    X11Native.XShapeCombineRectangles(
                        currentDisplay,
                        windowHandle,
                        X11Native.ShapeInput,
                        0,
                        0,
                        nint.Zero,
                        0,
                        X11Native.ShapeSet,
                        X11Native.Unsorted);
                }

                _ = X11Native.XFlush(currentDisplay);
                if (interactive)
                {
                    interactiveWindowHandles.Add(windowHandle);
                }

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
    }

    public IDisposable? BeginVisibleCursorSession(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            window.Activate();
            return null;
        }

        lock (displaySync)
        {
            if (!TryGetDisplay(out var currentDisplay))
            {
                window.Activate();
                return null;
            }

            try
            {
                var rootWindow = X11Native.XDefaultRootWindow(currentDisplay);
                var activeWindowAtom = X11Native.XInternAtom(
                    currentDisplay,
                    "_NET_ACTIVE_WINDOW",
                    onlyIfExists: 0);
                var previousActiveWindow = ReadActiveWindow(
                    currentDisplay,
                    rootWindow,
                    activeWindowAtom);
                var interactionWindow = unchecked((nuint)handle);
                interactiveWindowHandles.RemoveWhere(
                    candidate => !IsValidWindow(currentDisplay, candidate));
                if (!IsValidWindow(currentDisplay, interactionWindow))
                {
                    window.Activate();
                    return null;
                }

                var interactionWindows = interactiveWindowHandles
                    .Append(interactionWindow)
                    .Distinct()
                    .ToArray();

                window.Activate();
                var cursor = X11Native.XCreateFontCursor(
                    currentDisplay,
                    LeftPointerCursor);
                if (cursor != 0)
                {
                    foreach (var currentWindow in interactionWindows)
                    {
                        _ = X11Native.XDefineCursor(
                            currentDisplay,
                            currentWindow,
                            cursor);
                    }
                }

                _ = ActivateWindow(
                    currentDisplay,
                    rootWindow,
                    activeWindowAtom,
                    interactionWindow,
                    previousActiveWindow);
                return new X11CursorVisibilitySession(
                    interactionWindows,
                    cursor,
                    previousActiveWindow,
                    new X11CursorSessionOperations(
                        getActiveWindow: () => ReadLiveActiveWindow(
                            rootWindow,
                            activeWindowAtom),
                        getFocusWindow: ReadLiveFocusWindow,
                        activateWindow: target => ActivateLiveWindow(
                            rootWindow,
                            activeWindowAtom,
                            target,
                            interactionWindow),
                        undefineCursor: UndefineLiveCursor,
                        freeCursor: FreeLiveCursor));
            }
            catch (Exception exception) when (
                exception is DllNotFoundException
                    or EntryPointNotFoundException
                    or BadImageFormatException)
            {
                Trace.TraceWarning(
                    "X11 overlay cursor activation was unavailable; using the "
                    + $"window-manager fallback: {exception.Message}");
                window.Activate();
                return null;
            }
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
        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return false;
        }

        lock (displaySync)
        {
            if (!TryGetDisplay(out var currentDisplay))
            {
                return false;
            }

            try
            {
                _ = X11Native.XUnmapWindow(
                    currentDisplay,
                    unchecked((nuint)handle));
                _ = X11Native.XFlush(currentDisplay);
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
                "The native X11 overlay host is not available.");
        }

        if (regions.Count == 0)
        {
            return SetInteractive(window, interactive: false);
        }

        lock (displaySync)
        {
            if (!TryGetDisplay(out var currentDisplay) || !shapeAvailable)
            {
                return new OverlayInteractionResult(
                    IsPrepared: false,
                    IsInteractive: false,
                    Capabilities.StatusText);
            }

            var windowHandle = unchecked((nuint)handle);
            try
            {
                if (!IsValidWindow(currentDisplay, windowHandle))
                {
                    interactiveWindowHandles.Remove(windowHandle);
                    return new OverlayInteractionResult(
                        IsPrepared: false,
                        IsInteractive: false,
                        "The native X11 overlay host is no longer available.");
                }

                var rectangles = new X11Native.XRectangle[regions.Count];
                var rectangleCount = 0;
                foreach (var region in regions)
                {
                    if (region.Width <= 0 || region.Height <= 0)
                    {
                        continue;
                    }

                    var left = Math.Clamp(
                        region.X,
                        short.MinValue,
                        short.MaxValue);
                    var top = Math.Clamp(
                        region.Y,
                        short.MinValue,
                        short.MaxValue);
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

                var stackingApplied = ApplyWindowType(currentDisplay, handle);
                var pinnedRectangles = GCHandle.Alloc(
                    rectangles,
                    GCHandleType.Pinned);
                try
                {
                    X11Native.XShapeCombineRectangles(
                        currentDisplay,
                        windowHandle,
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
                _ = X11Native.XFlush(currentDisplay);
                interactiveWindowHandles.Add(windowHandle);
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
    }

    public void Dispose()
    {
        lock (displaySync)
        {
            interactiveWindowHandles.Clear();
            var currentDisplay = display;
            display = nint.Zero;
            if (currentDisplay != nint.Zero)
            {
                try
                {
                    _ = X11Native.XCloseDisplay(currentDisplay);
                }
                finally
                {
                    UnregisterErrorHandledDisplay(currentDisplay);
                }
            }
        }
    }

    internal static void EnsureErrorHandlerInstalled()
    {
        lock (ErrorHandlerSync)
        {
            if (errorHandlerInstalled)
            {
                return;
            }

            var errorHandlerPointer = Marshal.GetFunctionPointerForDelegate(
                ErrorHandler);
            var previousHandlerPointer = X11Native.XSetErrorHandler(
                errorHandlerPointer);
            previousErrorHandlerPointer = previousHandlerPointer
                != errorHandlerPointer
                ? previousHandlerPointer
                : nint.Zero;

            errorHandlerInstalled = true;
        }
    }

    private static int HandleXError(
        nint errorDisplay,
        ref X11Native.XErrorEvent errorEvent)
    {
        bool suppressExpectedLifecycleRace;
        try
        {
            suppressExpectedLifecycleRace =
                ShouldSuppressXError(
                    errorDisplay,
                    errorEvent.ErrorCode,
                    errorEvent.RequestCode);
        }
        catch (Exception)
        {
            return 0;
        }

        try
        {
            var detail =
                $"error {errorEvent.ErrorCode}, request "
                + $"{errorEvent.RequestCode}.{errorEvent.MinorCode}, resource "
                + $"{errorEvent.ResourceId}, display {errorDisplay}.";
            if (suppressExpectedLifecycleRace)
            {
                Trace.TraceInformation(
                    "Ignoring an expected X11 window or capture lifecycle "
                        + "race: "
                        + detail);
            }
            else
            {
                Trace.TraceWarning(
                    "X11 request failed and will be delegated to the previous "
                        + "error handler: "
                        + detail);
            }
        }
        catch (Exception)
        {
            // Logging failures must not change Xlib error handling.
        }

        if (suppressExpectedLifecycleRace)
        {
            return 0;
        }

        try
        {
            return X11Native.InvokeErrorHandler(
                previousErrorHandlerPointer,
                errorDisplay,
                ref errorEvent);
        }
        catch (Exception)
        {
            // Managed exceptions must never unwind through this Xlib callback.
            return 0;
        }
    }

    internal static void RegisterErrorHandledDisplay(nint errorDisplay)
    {
        if (errorDisplay != nint.Zero)
        {
            ErrorHandledDisplays.TryAdd(errorDisplay, 0);
        }
    }

    internal static void UnregisterErrorHandledDisplay(nint errorDisplay)
    {
        ErrorHandledDisplays.TryRemove(errorDisplay, out _);
    }

    internal static bool ShouldSuppressXError(
        nint errorDisplay,
        byte errorCode,
        byte requestCode = 0)
    {
        // A window can disappear after it is read from _NET_CLIENT_LIST but
        // before its class, bounds, or input state is queried. The Xlib
        // default handler treats that normal lifecycle race as fatal.
        if (!ErrorHandledDisplays.ContainsKey(errorDisplay))
        {
            return false;
        }

        if (errorCode == X11Native.BadWindow)
        {
            return true;
        }

        // Focus can become non-viewable between the viewability check and
        // XSetInputFocus. Treat only that exact request race as benign.
        if (requestCode == X11Native.SetInputFocusRequest)
        {
            return errorCode == X11Native.BadMatch;
        }

        // XGetImage reports capture failures through the process-global Xlib
        // handler before returning null. Let the capture layer turn only the
        // documented GetImage failures into managed errors.
        return requestCode == X11Native.GetImageRequest
            && errorCode is X11Native.BadValue
                or X11Native.BadMatch
                or X11Native.BadDrawable;
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

    private static nuint ReadActiveWindow(
        nint display,
        nuint rootWindow,
        nuint activeWindowAtom)
    {
        if (activeWindowAtom == 0
            || X11Native.XGetWindowProperty(
                display,
                rootWindow,
                activeWindowAtom,
                nint.Zero,
                1,
                delete: 0,
                requestedType: 0,
                out _,
                out var actualFormat,
                out var itemCount,
                out _,
                out var propertyData) != 0
            || propertyData == nint.Zero)
        {
            return 0;
        }

        try
        {
            return actualFormat == 32 && itemCount > 0
                ? unchecked((nuint)Marshal.ReadIntPtr(propertyData))
                : 0;
        }
        finally
        {
            _ = X11Native.XFree(propertyData);
        }
    }

    private static nuint ReadFocusWindow(nint display)
    {
        return X11Native.XGetInputFocus(
                display,
                out var focusWindow,
                out _) == 0
            ? 0
            : focusWindow;
    }

    private static bool ActivateWindow(
        nint display,
        nuint rootWindow,
        nuint activeWindowAtom,
        nuint targetWindow,
        nuint requestorWindow)
    {
        if (!IsValidWindow(display, targetWindow))
        {
            return false;
        }

        _ = X11Native.XMapRaised(display, targetWindow);
        if (activeWindowAtom != 0)
        {
            var activateEvent = new X11Native.XClientMessageEvent
            {
                Type = ClientMessage,
                Display = display,
                Window = targetWindow,
                MessageType = activeWindowAtom,
                Format = 32,
                Data = new X11Native.XClientMessageData
                {
                    L0 = 1,
                    L1 = 0,
                    L2 = unchecked((nint)requestorWindow),
                },
            };
            _ = X11Native.XSendEvent(
                display,
                rootWindow,
                propagate: 0,
                SubstructureNotifyMask | SubstructureRedirectMask,
                ref activateEvent);
        }

        if (IsViewableWindow(display, targetWindow))
        {
            _ = X11Native.XSetInputFocus(
                display,
                targetWindow,
                RevertToParent,
                time: 0);
        }

        _ = X11Native.XFlush(display);
        return true;
    }

    private static bool IsValidWindow(nint display, nuint window)
    {
        return display != nint.Zero
            && window != 0
            && X11Native.XGetWindowAttributes(
                display,
                window,
                out _) != 0;
    }

    private static bool IsViewableWindow(nint display, nuint window)
    {
        return display != nint.Zero
            && window != 0
            && X11Native.XGetWindowAttributes(
                display,
                window,
                out var attributes) != 0
            && attributes.MapState == X11Native.IsViewable;
    }

    private bool TryGetDisplay(out nint currentDisplay)
    {
        Debug.Assert(Monitor.IsEntered(displaySync));
        currentDisplay = display;
        return currentDisplay != nint.Zero;
    }

    private nuint ReadLiveActiveWindow(
        nuint rootWindow,
        nuint activeWindowAtom)
    {
        lock (displaySync)
        {
            var currentDisplay = display;
            return currentDisplay == nint.Zero
                ? 0
                : ReadActiveWindow(
                    currentDisplay,
                    rootWindow,
                    activeWindowAtom);
        }
    }

    private nuint ReadLiveFocusWindow()
    {
        lock (displaySync)
        {
            var currentDisplay = display;
            return currentDisplay == nint.Zero
                ? 0
                : ReadFocusWindow(currentDisplay);
        }
    }

    private bool ActivateLiveWindow(
        nuint rootWindow,
        nuint activeWindowAtom,
        nuint targetWindow,
        nuint requestorWindow)
    {
        lock (displaySync)
        {
            var currentDisplay = display;
            return currentDisplay != nint.Zero
                && ActivateWindow(
                    currentDisplay,
                    rootWindow,
                    activeWindowAtom,
                    targetWindow,
                    requestorWindow);
        }
    }

    private int UndefineLiveCursor(nuint targetWindow)
    {
        lock (displaySync)
        {
            var currentDisplay = display;
            return currentDisplay == nint.Zero
                || !IsValidWindow(currentDisplay, targetWindow)
                ? 0
                : X11Native.XUndefineCursor(
                    currentDisplay,
                    targetWindow);
        }
    }

    private int FreeLiveCursor(nuint cursor)
    {
        lock (displaySync)
        {
            var currentDisplay = display;
            if (currentDisplay == nint.Zero)
            {
                return 0;
            }

            var result = X11Native.XFreeCursor(currentDisplay, cursor);
            _ = X11Native.XFlush(currentDisplay);
            return result;
        }
    }

    private bool ApplyWindowType(nint currentDisplay, nint handle)
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
                currentDisplay,
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
