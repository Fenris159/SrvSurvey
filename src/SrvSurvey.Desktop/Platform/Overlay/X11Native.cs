using System.Runtime.InteropServices;
using Avalonia;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static partial class X11Native
{
    /// <summary>Shares the stateless Xlib adapter used by overlay placement policy.</summary>
    internal static readonly IX11OverlayWindowOperations OverlayWindowOperations = new OverlayWindowApi();

    internal const byte BadValue = 2;
    internal const byte BadWindow = 3;
    internal const byte BadMatch = 8;
    internal const byte BadDrawable = 9;
    internal const byte SetInputFocusRequest = 42;
    internal const byte GetImageRequest = 73;
    internal const int IsViewable = 2;
    internal const int ShapeInput = 2;
    internal const int ShapeSet = 0;
    internal const int Unsorted = 0;
    internal const int ZPixmap = 2;
    internal const int PropertyReplace = 0;

    internal static bool TryInitializeThreading()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            return XInitThreads() != 0;
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    [LibraryImport("libX11.so.6")]
    private static partial int XInitThreads();

    [LibraryImport("libX11.so.6")]
    internal static partial nint XOpenDisplay(nint displayName);

    [LibraryImport("libX11.so.6")]
    internal static partial int XCloseDisplay(nint display);

    /// <summary>Reads active monitor geometry from the XRandR extension.</summary>
    [LibraryImport("libXrandr.so.2")]
    private static partial nint XRRGetMonitors(nint display, nuint window, int getActive, out int count);

    /// <summary>Frees the monitor array returned by XRandR.</summary>
    [LibraryImport("libXrandr.so.2")]
    private static partial void XRRFreeMonitors(nint monitors);

    /// <summary>Returns X11 monitor bounds for mapping scaled portal sources.</summary>
    internal static IReadOnlyList<PixelRect> ReadMonitorBounds()
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        nint display = nint.Zero;
        nint monitors = nint.Zero;
        try
        {
            display = XOpenDisplay(nint.Zero);
            if (display == nint.Zero)
            {
                return [];
            }

            monitors = XRRGetMonitors(display, XDefaultRootWindow(display), 1, out int count);
            if (monitors == nint.Zero || count is < 1 or > 64)
            {
                return [];
            }

            int entrySize = Marshal.SizeOf<XrrMonitorInfo>();
            List<PixelRect> bounds = new(count);
            for (int index = 0; index < count; index++)
            {
                XrrMonitorInfo monitor = Marshal.PtrToStructure<XrrMonitorInfo>(monitors + index * entrySize);
                if (monitor.Width > 0 && monitor.Height > 0)
                {
                    bounds.Add(new PixelRect(monitor.X, monitor.Y, monitor.Width, monitor.Height));
                }
            }

            return bounds;
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return [];
        }
        finally
        {
            if (monitors != nint.Zero)
            {
                XRRFreeMonitors(monitors);
            }

            if (display != nint.Zero)
            {
                _ = XCloseDisplay(display);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct XrrMonitorInfo
    {
        public readonly nuint Name;
        public readonly int Primary;
        public readonly int Automatic;
        public readonly int OutputCount;
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;
        public readonly int WidthMillimeters;
        public readonly int HeightMillimeters;
        public readonly nint Outputs;
    }

    [LibraryImport("libX11.so.6")]
    internal static partial nint XSetErrorHandler(nint handler);

    /// <summary>Installs an Xlib I/O error callback and returns the previous callback.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial nint XSetIOErrorHandler(nint handler);

    /// <summary>Controls Xlib exit behavior after an I/O error on one display.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial void XSetIOErrorExitHandler(nint display, nint handler, nint userData);

    internal static int InvokeErrorHandler(nint handler, nint display, ref XErrorEvent errorEvent)
    {
        if (handler == nint.Zero)
        {
            return 0;
        }

        // The native pointer may originate from another managed delegate type with the same ABI.
#pragma warning disable CA2263
        Delegate callback = Marshal.GetDelegateForFunctionPointer(handler, typeof(XErrorHandler));
#pragma warning restore CA2263
        object?[] arguments = [display, errorEvent];
        object? result = callback.DynamicInvoke(arguments);
        errorEvent = (XErrorEvent)arguments[1]!;
        return result is int errorCode ? errorCode : 0;
    }

    [LibraryImport("libX11.so.6")]
    internal static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XQueryPointer(
        nint display,
        nuint window,
        out nuint root,
        out nuint child,
        out int rootX,
        out int rootY,
        out int windowX,
        out int windowY,
        out uint mask
    );

    /// <summary>Opens a gesture-scoped pointer reader only for native X11 and XWayland windows.</summary>
    internal static IOverlayDragPointerProbe? TryCreatePointerProbe(string? handleDescriptor)
    {
        if (!OperatingSystem.IsLinux() || handleDescriptor != "XID")
        {
            return null;
        }

        try
        {
            nint pointerDisplay = XOpenDisplay(nint.Zero);
            return pointerDisplay == nint.Zero ? null : new X11PointerProbe(pointerDisplay);
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Reads root screen pixels and button state without waiting for queued window-relative events.</summary>
    private sealed class X11PointerProbe(nint pointerDisplay) : IOverlayDragPointerProbe
    {
        private nint currentDisplay = pointerDisplay;

        /// <summary>Samples the current pointer, including a release occurring between coalesced drag updates.</summary>
        public OverlayDragPointerSample? Read()
        {
            if (
                currentDisplay == nint.Zero
                || XQueryPointer(
                    currentDisplay,
                    XDefaultRootWindow(currentDisplay),
                    out _,
                    out _,
                    out int x,
                    out int y,
                    out _,
                    out _,
                    out uint mask
                ) == 0
            )
            {
                return null;
            }

            return new OverlayDragPointerSample(new Avalonia.PixelPoint(x, y), (mask & (1U << 8)) != 0);
        }

        /// <summary>Closes the gesture's display connection once after its final pointer sample.</summary>
        public void Dispose()
        {
            nint previousDisplay = currentDisplay;
            currentDisplay = nint.Zero;
            if (previousDisplay != nint.Zero)
            {
                _ = XCloseDisplay(previousDisplay);
            }
        }
    }

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nuint XInternAtom(nint display, string atomName, int onlyIfExists);

    [LibraryImport("libX11.so.6")]
    internal static partial int XGetWindowProperty(
        nint display,
        nuint window,
        nuint property,
        nint longOffset,
        nint longLength,
        int delete,
        nuint requestedType,
        out nuint actualType,
        out int actualFormat,
        out nuint itemCount,
        out nuint bytesAfter,
        out nint propertyData
    );

    [LibraryImport("libX11.so.6")]
    internal static partial int XChangeProperty(
        nint display,
        nuint window,
        nuint property,
        nuint type,
        int format,
        int mode,
        nint data,
        int elementCount
    );

    [LibraryImport("libX11.so.6")]
    internal static partial int XGetWindowAttributes(nint display, nuint window, out XWindowAttributes attributes);

    [LibraryImport("libX11.so.6")]
    internal static partial int XTranslateCoordinates(
        nint display,
        nuint sourceWindow,
        nuint destinationWindow,
        int sourceX,
        int sourceY,
        out int destinationX,
        out int destinationY,
        out nuint childWindow
    );

    [LibraryImport("libX11.so.6")]
    internal static partial int XQueryTree(
        nint display,
        nuint window,
        out nuint root,
        out nuint parent,
        out nint children,
        out uint childCount
    );

    [LibraryImport("libX11.so.6")]
    internal static partial int XGetClassHint(nint display, nuint window, out XClassHint classHint);

    [LibraryImport("libX11.so.6")]
    internal static partial int XFetchName(nint display, nuint window, out nint windowName);

    [LibraryImport("libX11.so.6")]
    internal static partial int XFree(nint data);

    [LibraryImport("libX11.so.6")]
    internal static partial nint XGetImage(
        nint display,
        nuint drawable,
        int x,
        int y,
        uint width,
        uint height,
        nuint planeMask,
        int format
    );

    [LibraryImport("libX11.so.6")]
    internal static partial int XDestroyImage(nint image);

    [LibraryImport("libX11.so.6")]
    internal static partial int XFlush(nint display);

    [LibraryImport("libX11.so.6")]
    internal static partial int XMapRaised(nint display, nuint window);

    /// <summary>Changes selected native attributes, including override-redirect before a live panel is mapped.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial int XChangeWindowAttributes(
        nint display,
        nuint window,
        nuint valueMask,
        ref XSetWindowAttributes attributes
    );

    /// <summary>Raises a window without mapping it or requesting keyboard focus.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial int XRaiseWindow(nint display, nuint window);

    /// <summary>Hides the native window without destroying its saved attributes.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial int XUnmapWindow(nint display, nuint window);

    /// <summary>Gives the native window keyboard focus with the requested reversion policy.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial int XSetInputFocus(nint display, nuint focusWindow, int revertTo, nuint time);

    /// <summary>Reads current keyboard focus and its reversion policy from the display server.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial int XGetInputFocus(nint display, out nuint focusWindow, out int revertTo);

    [LibraryImport("libX11.so.6")]
    internal static partial int XSendEvent(
        nint display,
        nuint window,
        int propagate,
        nint eventMask,
        ref XClientMessageEvent eventSend
    );

    [LibraryImport("libX11.so.6")]
    internal static partial nuint XCreateFontCursor(nint display, uint shape);

    [LibraryImport("libX11.so.6")]
    internal static partial int XDefineCursor(nint display, nuint window, nuint cursor);

    [LibraryImport("libX11.so.6")]
    internal static partial int XUndefineCursor(nint display, nuint window);

    [LibraryImport("libX11.so.6")]
    internal static partial int XFreeCursor(nint display, nuint cursor);

    [LibraryImport("libXext.so.6")]
    internal static partial int XShapeQueryExtension(nint display, out int eventBase, out int errorBase);

    [LibraryImport("libXext.so.6")]
    internal static partial void XShapeCombineRectangles(
        nint display,
        nuint destinationWindow,
        int destinationKind,
        int xOffset,
        int yOffset,
        nint rectangles,
        int rectangleCount,
        int operation,
        int ordering
    );

    [LibraryImport("libXext.so.6")]
    internal static partial void XShapeCombineMask(
        nint display,
        nuint destinationWindow,
        int destinationKind,
        int xOffset,
        int yOffset,
        nuint sourcePixmap,
        int operation
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClassHint
    {
        public nint ResourceName;
        public nint ResourceClass;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int XErrorHandler(nint display, ref XErrorEvent errorEvent);

    [StructLayout(LayoutKind.Sequential)]
    internal struct XErrorEvent
    {
        public int Type;
        public nint Display;
        public nuint ResourceId;
        public nuint Serial;
        public byte ErrorCode;
        public byte RequestCode;
        public byte MinorCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XRectangle
    {
        public short X;
        public short Y;
        public ushort Width;
        public ushort Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClientMessageEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public nint Display;
        public nuint Window;
        public nuint MessageType;
        public int Format;
        public XClientMessageData Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClientMessageData
    {
        public nint L0;
        public nint L1;
        public nint L2;
        public nint L3;
        public nint L4;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XSetWindowAttributes
    {
        public nuint BackgroundPixmap;
        public nuint BackgroundPixel;
        public nuint BorderPixmap;
        public nuint BorderPixel;
        public int BitGravity;
        public int WindowGravity;
        public int BackingStore;
        public nuint BackingPlanes;
        public nuint BackingPixel;
        public int SaveUnder;
        public nint EventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public nuint Colormap;
        public nuint Cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XWindowAttributes
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int BorderWidth;
        public int Depth;
        public nint Visual;
        public nuint Root;
        public int Class;
        public int BitGravity;
        public int WindowGravity;
        public int BackingStore;
        public nuint BackingPlanes;
        public nuint BackingPixel;
        public int SaveUnder;
        public nuint Colormap;
        public int MapInstalled;
        public int MapState;
        public nint AllEventMasks;
        public nint YourEventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Screen;
    }

    /// <summary>Forwards overlay window operations to Xlib without changing their arguments or ordering.</summary>
    private sealed class OverlayWindowApi : IX11OverlayWindowOperations
    {
        /// <summary>Returns Xlib's attributes result, including its synchronization reply.</summary>
        public int GetAttributes(nint display, nuint window, out XWindowAttributes attributes) =>
            XGetWindowAttributes(display, window, out attributes);

        /// <summary>Applies the selected attribute values through Xlib.</summary>
        public void ChangeAttributes(
            nint display,
            nuint window,
            nuint valueMask,
            ref XSetWindowAttributes attributes
        ) => _ = XChangeWindowAttributes(display, window, valueMask, ref attributes);

        /// <summary>Raises a window through Xlib without mapping it.</summary>
        public void RaiseWindow(nint display, nuint window) => _ = XRaiseWindow(display, window);

        /// <summary>Forwards explicit keyboard activation to Xlib.</summary>
        public void SetInputFocus(nint display, nuint window, int revertTo, nuint time) =>
            _ = XSetInputFocus(display, window, revertTo, time);

        /// <summary>Flushes requests on the supplied native connection.</summary>
        public void Flush(nint display) => _ = XFlush(display);
    }

    /// <summary>Creates an independently owned native recorder for a transient game display.</summary>
    internal static IX11KeyboardRecordApi CreateKeyboardRecordApi() => new KeyboardRecordApi();

    /// <summary>Owns Xlib/XRecord resources and their platform-specific shutdown recovery.</summary>
    private sealed class KeyboardRecordApi : IX11KeyboardRecordApi
    {
        private nint control;
        private nint data;
        private nuint context;

        /// <summary>Detects disconnection of either transient Xlib connection.</summary>
        public bool HasFailed =>
            X11TransientDisplayRecovery.HasFailed(control) || X11TransientDisplayRecovery.HasFailed(data);

        /// <summary>Enables a keyboard-only recording context using separate control and data connections.</summary>
        public bool Open(string displayName, X11KeyboardRecordCallback callback)
        {
            X11OverlayPlatformService.EnsureErrorHandlerInstalled();
            control = OpenKeyboardDisplay(displayName);
            data = OpenKeyboardDisplay(displayName);
            if (control == nint.Zero || data == nint.Zero || XRecordQueryVersion(control, out _, out _) == 0)
            {
                return false;
            }
            nint range = XRecordAllocRange();
            if (range == nint.Zero)
            {
                return false;
            }
            try
            {
                // XRecordRange.device_events is the two-byte range at offset 18.
                Marshal.WriteByte(range, 18, 2);
                Marshal.WriteByte(range, 19, 3);
                nuint clients = 3; // XRecordAllClients.
                context = XRecordCreateContext(control, 0, ref clients, 1, ref range, 1);
            }
            finally
            {
                _ = XFree(range);
            }
            _ = XSync(control, 0);
            return context != 0 && XRecordEnableContextAsync(data, context, callback, nint.Zero) != 0;
        }

        /// <summary>Resolves a keysym against the actual layout of this game server.</summary>
        public byte ResolveKeyCode(string name)
        {
            nuint symbol = XStringToKeysym(name);
            return symbol == 0 ? (byte)0 : XKeysymToKeycode(control, symbol);
        }

        /// <summary>Flushes buffered server recording replies and drains them without blocking for keys.</summary>
        public void ReadReplies()
        {
            _ = XSync(control, 0);
            XRecordProcessReplies(data);
        }

        /// <summary>Releases one packet handed to the managed callback.</summary>
        public void FreeData(nint pointer) => XRecordFreeData(pointer);

        /// <summary>Disables the context and closes recoverable connections, including already disconnected servers.</summary>
        public void Dispose()
        {
            if (context != 0 && !HasFailed)
            {
                _ = XRecordDisableContext(control, context);
                _ = XRecordFreeContext(control, context);
                _ = XSync(control, 0);
                XRecordProcessReplies(data);
            }
            context = 0;
            CloseKeyboardDisplay(ref data);
            CloseKeyboardDisplay(ref control);
        }

        /// <summary>Registers one named connection for transient I/O-error recovery.</summary>
        private static nint OpenKeyboardDisplay(string name)
        {
            nint text = Marshal.StringToCoTaskMemUTF8(name);
            nint display = nint.Zero;
            try
            {
                display = XOpenDisplay(text);
                if (display != nint.Zero)
                {
                    X11TransientDisplayRecovery.Register(display);
                    X11OverlayPlatformService.RegisterErrorHandledDisplay(display);
                }
                return display;
            }
            catch
            {
                CloseKeyboardDisplay(ref display);
                throw;
            }
            finally
            {
                Marshal.FreeCoTaskMem(text);
            }
        }

        /// <summary>Closes a connection before unregistering its error handlers.</summary>
        private static void CloseKeyboardDisplay(ref nint display)
        {
            nint current = display;
            display = nint.Zero;
            if (current == nint.Zero)
            {
                return;
            }
            try
            {
                _ = XCloseDisplay(current);
            }
            finally
            {
                X11TransientDisplayRecovery.Unregister(current);
                X11OverlayPlatformService.UnregisterErrorHandledDisplay(current);
            }
        }
    }

    /// <summary>Queries whether the server supports recording.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial int XRecordQueryVersion(nint display, out int major, out int minor);

    /// <summary>Allocates a zero-filled XRecord range.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial nint XRecordAllocRange();

    /// <summary>Creates the keyboard recording context.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial nuint XRecordCreateContext(
        nint display,
        int flags,
        ref nuint clients,
        int clientCount,
        ref nint ranges,
        int rangeCount
    );

    /// <summary>Enables asynchronous recording on the data connection.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial int XRecordEnableContextAsync(
        nint display,
        nuint context,
        X11KeyboardRecordCallback callback,
        nint closure
    );

    /// <summary>Processes only immediately available recorded packets.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial void XRecordProcessReplies(nint display);

    /// <summary>Releases a callback packet.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial void XRecordFreeData(nint data);

    /// <summary>Stops the recording context.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial int XRecordDisableContext(nint display, nuint context);

    /// <summary>Releases a disabled recording context.</summary>
    [LibraryImport("libXtst.so.6")]
    private static partial int XRecordFreeContext(nint display, nuint context);

    /// <summary>Completes control requests before enabling the data stream.</summary>
    [LibraryImport("libX11.so.6")]
    internal static partial int XSync(nint display, int discard);

    /// <summary>Looks up a standard X11 keysym.</summary>
    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XStringToKeysym(string name);

    /// <summary>Resolves a keysym on this server's keyboard layout.</summary>
    [LibraryImport("libX11.so.6")]
    private static partial byte XKeysymToKeycode(nint display, nuint keysym);
}
