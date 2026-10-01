using System.Runtime.InteropServices;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static partial class X11Native
{
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

    private sealed class X11PointerProbe(nint pointerDisplay) : IOverlayDragPointerProbe
    {
        private nint currentDisplay = pointerDisplay;
        private long lastQueryTimestamp;

        public OverlayDragPointerSample? Read()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (System.Diagnostics.Stopwatch.GetElapsedTime(lastQueryTimestamp, now) < TimeSpan.FromMilliseconds(16))
            {
                return null;
            }

            lastQueryTimestamp = now;
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

    [LibraryImport("libX11.so.6")]
    internal static partial int XUnmapWindow(nint display, nuint window);

    [LibraryImport("libX11.so.6")]
    internal static partial int XSetInputFocus(nint display, nuint focusWindow, int revertTo, nuint time);

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
}
