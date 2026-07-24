using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static partial class X11Native
{
    internal const int IsViewable = 2;
    internal const int ShapeInput = 2;
    internal const int ShapeSet = 0;
    internal const int Unsorted = 0;

    [LibraryImport("libX11.so.6")]
    internal static partial nint XOpenDisplay(nint displayName);

    [LibraryImport("libX11.so.6")]
    internal static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6")]
    internal static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nuint XInternAtom(
        nint display,
        string atomName,
        int onlyIfExists);

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
        out nint propertyData);

    [LibraryImport("libX11.so.6")]
    internal static partial int XGetWindowAttributes(
        nint display,
        nuint window,
        out XWindowAttributes attributes);

    [LibraryImport("libX11.so.6")]
    internal static partial int XTranslateCoordinates(
        nint display,
        nuint sourceWindow,
        nuint destinationWindow,
        int sourceX,
        int sourceY,
        out int destinationX,
        out int destinationY,
        out nuint childWindow);

    [LibraryImport("libX11.so.6")]
    internal static partial int XQueryTree(
        nint display,
        nuint window,
        out nuint root,
        out nuint parent,
        out nint children,
        out uint childCount);

    [LibraryImport("libX11.so.6")]
    internal static partial int XGetClassHint(
        nint display,
        nuint window,
        out XClassHint classHint);

    [LibraryImport("libX11.so.6")]
    internal static partial int XFetchName(
        nint display,
        nuint window,
        out nint windowName);

    [LibraryImport("libX11.so.6")]
    internal static partial int XFree(nint data);

    [LibraryImport("libX11.so.6")]
    internal static partial int XFlush(nint display);

    [LibraryImport("libXext.so.6")]
    internal static partial int XShapeQueryExtension(
        nint display,
        out int eventBase,
        out int errorBase);

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
        int ordering);

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClassHint
    {
        public nint ResourceName;
        public nint ResourceClass;
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
