using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IGameScreenCapture : IDisposable
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    CapturedPixelBuffer Capture(PixelRect bounds);
}

public sealed class CapturedPixelBuffer : IFssPixelSource
{
    private readonly byte[] bgraPixels;

    public CapturedPixelBuffer(int width, int height, byte[] bgraPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        ArgumentNullException.ThrowIfNull(bgraPixels);
        var expectedLength = checked(width * height * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException(
                "The BGRA buffer length does not match its dimensions.",
                nameof(bgraPixels));
        }

        Width = width;
        Height = height;
        this.bgraPixels = bgraPixels;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> BgraPixels => bgraPixels;

    public FssRgbPixel GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "The pixel is outside the captured image.");
        }

        var offset = checked(((y * Width) + x) * 4);
        return new FssRgbPixel(
            bgraPixels[offset + 2],
            bgraPixels[offset + 1],
            bgraPixels[offset]);
    }
}

public static class GameScreenCapture
{
    public static IGameScreenCapture CreateCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGameScreenCapture();
        }

        if (OverlayPlatformCapabilities.DetectCurrent()
            .UsesX11Compatibility)
        {
            return X11GameScreenCapture.TryCreate()
                ?? new UnavailableGameScreenCapture(
                    "X11 screen capture could not connect to the display.");
        }

        return new UnavailableGameScreenCapture(
            OperatingSystem.IsLinux()
                ? "FSS tuning detection requires an X11 session; direct "
                    + "screen capture is unavailable on Wayland."
                : "FSS tuning detection is not supported on this platform.");
    }
}

public sealed class UnavailableGameScreenCapture : IGameScreenCapture
{
    public UnavailableGameScreenCapture(string reason)
    {
        UnavailableReason = string.IsNullOrWhiteSpace(reason)
            ? "Screen capture is unavailable."
            : reason;
    }

    public bool IsAvailable => false;

    public string UnavailableReason { get; }

    public CapturedPixelBuffer Capture(PixelRect bounds)
    {
        throw new NotSupportedException(UnavailableReason);
    }

    public void Dispose()
    {
    }
}

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGameScreenCapture : IGameScreenCapture
{
    private const int MaximumCaptureBytes = 256 * 1024 * 1024;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public CapturedPixelBuffer Capture(PixelRect bounds)
    {
        var byteCount = ValidateBounds(bounds);
        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw CreateWin32Exception("Could not open the desktop surface.");
        }

        var memoryDc = nint.Zero;
        var bitmap = nint.Zero;
        var previousBitmap = nint.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
            {
                throw CreateWin32Exception(
                    "Could not create an FSS capture surface.");
            }

            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = bounds.Width,
                    Height = -bounds.Height,
                    Planes = 1,
                    BitCount = 32,
                },
            };
            bitmap = CreateDIBSection(
                screenDc,
                ref bitmapInfo,
                usage: 0,
                out var pixels,
                section: nint.Zero,
                offset: 0);
            if (bitmap == nint.Zero || pixels == nint.Zero)
            {
                throw CreateWin32Exception(
                    "Could not allocate the FSS capture buffer.");
            }

            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == nint.Zero || previousBitmap == new nint(-1))
            {
                throw CreateWin32Exception(
                    "Could not select the FSS capture buffer.");
            }

            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    screenDc,
                    bounds.X,
                    bounds.Y,
                    SrcCopy | CaptureBlt))
            {
                throw CreateWin32Exception(
                    "Could not copy the Elite Dangerous window.");
            }

            var managedPixels = new byte[byteCount];
            Marshal.Copy(pixels, managedPixels, 0, managedPixels.Length);
            return new CapturedPixelBuffer(
                bounds.Width,
                bounds.Height,
                managedPixels);
        }
        finally
        {
            if (previousBitmap != nint.Zero
                && previousBitmap != new nint(-1)
                && memoryDc != nint.Zero)
            {
                _ = SelectObject(memoryDc, previousBitmap);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    public void Dispose()
    {
    }

    private static int ValidateBounds(PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "The capture bounds must have a positive size.");
        }

        var byteCount = checked((long)bounds.Width * bounds.Height * 4);
        if (byteCount > MaximumCaptureBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "The capture bounds exceed the 256 MiB safety limit.");
        }

        return (int)byteCount;
    }

    private static Win32Exception CreateWin32Exception(string context)
    {
        var error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, context + $" Win32 error {error}.");
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ReleaseDC(nint window, nint deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint pixels,
        nint section,
        uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(
        nint deviceContext,
        nint graphicsObject);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint graphicsObject);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint deviceContext);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }
}

internal sealed class X11GameScreenCapture : IGameScreenCapture
{
    private const int MaximumCaptureBytes = 256 * 1024 * 1024;
    private const int LsbFirst = 0;
    private nint display;
    private readonly nuint rootWindow;

    private X11GameScreenCapture(nint display)
    {
        this.display = display;
        rootWindow = X11Native.XDefaultRootWindow(display);
    }

    public bool IsAvailable => display != nint.Zero;

    public string? UnavailableReason => IsAvailable
        ? null
        : "The X11 display connection is closed.";

    public static IGameScreenCapture? TryCreate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var display = nint.Zero;
        try
        {
            X11OverlayPlatformService.EnsureErrorHandlerInstalled();
            display = X11Native.XOpenDisplay(nint.Zero);
            if (display == nint.Zero)
            {
                return null;
            }

            X11OverlayPlatformService.RegisterErrorHandledDisplay(display);
            return new X11GameScreenCapture(display);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            if (display != nint.Zero)
            {
                try
                {
                    _ = X11Native.XCloseDisplay(display);
                }
                finally
                {
                    X11OverlayPlatformService.UnregisterErrorHandledDisplay(
                        display);
                }
            }

            return null;
        }
    }

    public CapturedPixelBuffer Capture(PixelRect bounds)
    {
        ObjectDisposedException.ThrowIf(
            display == nint.Zero,
            this);

        ValidateBounds(bounds);
        var captureBounds = ClipToRootWindow(bounds);
        var image = X11Native.XGetImage(
            display,
            rootWindow,
            captureBounds.X,
            captureBounds.Y,
            (uint)captureBounds.Width,
            (uint)captureBounds.Height,
            nuint.MaxValue,
            X11Native.ZPixmap);
        if (image == nint.Zero)
        {
            throw new InvalidOperationException(
                "X11 could not capture the Elite Dangerous window.");
        }

        try
        {
            var metadata = Marshal.PtrToStructure<X11ImageMetadata>(image);
            return Decode(metadata);
        }
        finally
        {
            _ = X11Native.XDestroyImage(image);
        }
    }

    public void Dispose()
    {
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
                X11OverlayPlatformService.UnregisterErrorHandledDisplay(
                    currentDisplay);
            }
        }
    }

    internal static CapturedPixelBuffer Decode(X11ImageMetadata image)
    {
        if (image.Width <= 0
            || image.Height <= 0
            || image.Data == nint.Zero
            || image.BytesPerLine <= 0
            || image.BitsPerPixel is not (16 or 24 or 32)
            || image.RedMask == 0
            || image.GreenMask == 0
            || image.BlueMask == 0)
        {
            throw new InvalidDataException(
                "The X11 capture returned an unsupported image layout.");
        }

        var bytesPerPixel = image.BitsPerPixel / 8;
        if (image.BytesPerLine < checked(image.Width * bytesPerPixel))
        {
            throw new InvalidDataException(
                "The X11 capture stride is shorter than a pixel row.");
        }

        var sourceLength = checked((long)image.BytesPerLine * image.Height);
        var targetLength = checked((long)image.Width * image.Height * 4);
        if (sourceLength > MaximumCaptureBytes
            || targetLength > MaximumCaptureBytes)
        {
            throw new InvalidDataException(
                "The X11 capture exceeds the 256 MiB safety limit.");
        }

        var source = new byte[(int)sourceLength];
        Marshal.Copy(image.Data, source, 0, source.Length);
        var target = new byte[(int)targetLength];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var sourceOffset = (y * image.BytesPerLine)
                    + (x * bytesPerPixel);
                var pixel = ReadPixel(
                    source.AsSpan(sourceOffset, bytesPerPixel),
                    image.ByteOrder == LsbFirst);
                var targetOffset = ((y * image.Width) + x) * 4;
                target[targetOffset] = ExtractChannel(pixel, image.BlueMask);
                target[targetOffset + 1] =
                    ExtractChannel(pixel, image.GreenMask);
                target[targetOffset + 2] =
                    ExtractChannel(pixel, image.RedMask);
                target[targetOffset + 3] = 255;
            }
        }

        return new CapturedPixelBuffer(image.Width, image.Height, target);
    }

    private static ulong ReadPixel(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        ulong value = 0;
        if (littleEndian)
        {
            for (var index = bytes.Length - 1; index >= 0; index--)
            {
                value = (value << 8) | bytes[index];
            }
        }
        else
        {
            foreach (var current in bytes)
            {
                value = (value << 8) | current;
            }
        }

        return value;
    }

    private static byte ExtractChannel(ulong pixel, nuint nativeMask)
    {
        var mask = unchecked((ulong)nativeMask);
        var shift = BitOperations.TrailingZeroCount(mask);
        var maximum = mask >> shift;
        var value = (pixel & mask) >> shift;
        return checked((byte)((value * 255 + (maximum / 2)) / maximum));
    }

    private static void ValidateBounds(PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "The capture bounds must have a positive size.");
        }

        var byteCount = checked((long)bounds.Width * bounds.Height * 4);
        if (byteCount > MaximumCaptureBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "The capture bounds exceed the 256 MiB safety limit.");
        }
    }

    private PixelRect ClipToRootWindow(PixelRect bounds)
    {
        if (X11Native.XGetWindowAttributes(
                display,
                rootWindow,
                out var rootAttributes) == 0
            || rootAttributes.Width <= 0
            || rootAttributes.Height <= 0)
        {
            throw new InvalidOperationException(
                "X11 could not read the desktop capture bounds.");
        }

        return ClipToRootWindow(
            bounds,
            rootAttributes.Width,
            rootAttributes.Height);
    }

    internal static PixelRect ClipToRootWindow(
        PixelRect bounds,
        int rootWidth,
        int rootHeight)
    {
        ValidateBounds(bounds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootHeight);

        var left = Math.Max(0L, bounds.X);
        var top = Math.Max(0L, bounds.Y);
        var right = Math.Min(
            rootWidth,
            checked((long)bounds.X + bounds.Width));
        var bottom = Math.Min(
            rootHeight,
            checked((long)bounds.Y + bounds.Height));
        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException(
                "The Elite Dangerous capture area is outside the X11 desktop.");
        }

        return new PixelRect(
            checked((int)left),
            checked((int)top),
            checked((int)(right - left)),
            checked((int)(bottom - top)));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct X11ImageMetadata
    {
        public int Width;
        public int Height;
        public int XOffset;
        public int Format;
        public nint Data;
        public int ByteOrder;
        public int BitmapUnit;
        public int BitmapBitOrder;
        public int BitmapPad;
        public int Depth;
        public int BytesPerLine;
        public int BitsPerPixel;
        public nuint RedMask;
        public nuint GreenMask;
        public nuint BlueMask;
    }
}
