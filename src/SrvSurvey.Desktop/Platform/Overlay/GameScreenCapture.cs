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

    CapturedPixelBuffer Capture(PixelRect bounds, PixelRect sourceBounds) => Capture(bounds);
}

public sealed class CapturedPixelBuffer : IFssPixelSource
{
    private readonly byte[] bgraPixels;

    public CapturedPixelBuffer(int width, int height, byte[] bgraPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        ArgumentNullException.ThrowIfNull(bgraPixels);
        int expectedLength = checked(width * height * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException("The BGRA buffer length does not match its dimensions.", nameof(bgraPixels));
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
            throw new ArgumentOutOfRangeException(nameof(x), "The pixel is outside the captured image.");
        }

        int offset = checked(((y * Width) + x) * 4);
        return new FssRgbPixel(bgraPixels[offset + 2], bgraPixels[offset + 1], bgraPixels[offset]);
    }
}

public static class GameScreenCapture
{
    public static IGameScreenCapture CreateCurrent(
        bool enableWaylandPortalFallback = false,
        Func<CancellationToken, Task<bool>>? confirmWaylandScreenShare = null
    )
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGameScreenCapture();
        }

        var capabilities = OverlayPlatformCapabilities.DetectCurrent();
        if (capabilities.UsesX11Compatibility)
        {
            IGameScreenCapture x11Capture =
                X11GameScreenCapture.TryCreate()
                ?? new UnavailableGameScreenCapture("X11 screen capture could not connect to the display.");
            IGameScreenCapture capture =
                enableWaylandPortalFallback && OperatingSystem.IsLinux() && IsWaylandSession()
                    ? new FallbackGameScreenCapture(
                        x11Capture,
                        new WaylandPortalGameScreenCapture(confirmWaylandScreenShare)
                    )
                    : x11Capture;
            return OperatingSystem.IsLinux() ? new BackoffGameScreenCapture(capture) : capture;
        }

        if (enableWaylandPortalFallback && OperatingSystem.IsLinux() && IsWaylandSession())
        {
            return new BackoffGameScreenCapture(new WaylandPortalGameScreenCapture(confirmWaylandScreenShare));
        }

        return new UnavailableGameScreenCapture(
            OperatingSystem.IsLinux()
                ? "FSS tuning detection requires an X11 session; direct " + "screen capture is unavailable on Wayland."
                : "FSS tuning detection is not supported on this platform."
        );
    }

    private static bool IsWaylandSession() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
        || string.Equals(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            "wayland",
            StringComparison.OrdinalIgnoreCase
        );

    internal static bool IsRecoverableFailure(Exception exception) =>
        exception
            is Win32Exception
                or ExternalException
                or IOException
                or InvalidDataException
                or InvalidOperationException
                or NotSupportedException;

    internal static bool ShouldShowWaylandSelectionGuidance(uint portalVersion, string? restoreToken) =>
        portalVersion < 4 || string.IsNullOrWhiteSpace(restoreToken);
}

public sealed class UnavailableGameScreenCapture : IGameScreenCapture
{
    public UnavailableGameScreenCapture(string reason)
    {
        UnavailableReason = string.IsNullOrWhiteSpace(reason) ? "Screen capture is unavailable." : reason;
    }

    public bool IsAvailable => false;

    public string UnavailableReason { get; }

    public CapturedPixelBuffer Capture(PixelRect bounds)
    {
        throw new NotSupportedException(UnavailableReason);
    }

    public void Dispose() { }
}

internal sealed class FallbackGameScreenCapture : IGameScreenCapture
{
    private readonly Lock gate = new();
    private IGameScreenCapture? primary;
    private IGameScreenCapture? fallback;

    public FallbackGameScreenCapture(IGameScreenCapture primary, IGameScreenCapture fallback)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        this.primary = primary;
        this.fallback = fallback;
    }

    public bool IsAvailable
    {
        get
        {
            lock (gate)
            {
                return primary?.IsAvailable == true || fallback?.IsAvailable == true;
            }
        }
    }

    public string? UnavailableReason
    {
        get
        {
            lock (gate)
            {
                return primary?.UnavailableReason ?? fallback?.UnavailableReason;
            }
        }
    }

    public CapturedPixelBuffer Capture(PixelRect bounds) => CaptureCore(capture => capture.Capture(bounds));

    public CapturedPixelBuffer Capture(PixelRect bounds, PixelRect sourceBounds) =>
        CaptureCore(capture => capture.Capture(bounds, sourceBounds));

    public void Dispose()
    {
        lock (gate)
        {
            primary?.Dispose();
            fallback?.Dispose();
            primary = null;
            fallback = null;
        }
    }

    private CapturedPixelBuffer CaptureCore(Func<IGameScreenCapture, CapturedPixelBuffer> captureFrame)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(primary is null && fallback is null, this);
            if (primary is not null && primary.IsAvailable)
            {
                try
                {
                    return captureFrame(primary);
                }
                catch (Exception exception) when (GameScreenCapture.IsRecoverableFailure(exception))
                {
                    primary.Dispose();
                    primary = null;
                }
            }

            return captureFrame(fallback ?? throw new ObjectDisposedException(nameof(FallbackGameScreenCapture)));
        }
    }
}

internal sealed class BackoffGameScreenCapture : IGameScreenCapture
{
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();
    private readonly TimeProvider timeProvider;
    private IGameScreenCapture? inner;
    private DateTimeOffset retryAfter;
    private int consecutiveFailures;

    public BackoffGameScreenCapture(IGameScreenCapture inner, TimeProvider? timeProvider = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsAvailable
    {
        get
        {
            lock (gate)
            {
                return inner?.IsAvailable == true;
            }
        }
    }

    public string? UnavailableReason
    {
        get
        {
            lock (gate)
            {
                return inner?.UnavailableReason;
            }
        }
    }

    public CapturedPixelBuffer Capture(PixelRect bounds) => CaptureCore(capture => capture.Capture(bounds));

    public CapturedPixelBuffer Capture(PixelRect bounds, PixelRect sourceBounds) =>
        CaptureCore(capture => capture.Capture(bounds, sourceBounds));

    public void Dispose()
    {
        lock (gate)
        {
            inner?.Dispose();
            inner = null;
        }
    }

    private CapturedPixelBuffer CaptureCore(Func<IGameScreenCapture, CapturedPixelBuffer> captureFrame)
    {
        lock (gate)
        {
            IGameScreenCapture capture = inner ?? throw new ObjectDisposedException(nameof(BackoffGameScreenCapture));
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (now < retryAfter)
            {
                throw new ScreenCaptureBackoffException(retryAfter - now);
            }

            try
            {
                CapturedPixelBuffer result = captureFrame(capture);
                consecutiveFailures = 0;
                retryAfter = default;
                return result;
            }
            catch (Exception exception) when (GameScreenCapture.IsRecoverableFailure(exception))
            {
                consecutiveFailures++;
                retryAfter = now + GetBackoff(consecutiveFailures);
                throw;
            }
        }
    }

    private static TimeSpan GetBackoff(int consecutiveFailures)
    {
        int exponent = Math.Min(Math.Max(consecutiveFailures - 1, 0), 5);
        return TimeSpan.FromSeconds(Math.Min(1 << exponent, MaximumBackoff.TotalSeconds));
    }
}

internal sealed class ScreenCaptureBackoffException(TimeSpan remaining)
    : InvalidOperationException($"Screen capture will retry in {Math.Max(remaining.TotalSeconds, 0):0.0} seconds.");

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
        int byteCount = ValidateBounds(bounds);
        nint screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw CreateWin32Exception("Could not open the desktop surface.");
        }

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previousBitmap = nint.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
            {
                throw CreateWin32Exception("Could not create an FSS capture surface.");
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
                out nint pixels,
                section: nint.Zero,
                offset: 0
            );
            if (bitmap == nint.Zero || pixels == nint.Zero)
            {
                throw CreateWin32Exception("Could not allocate the FSS capture buffer.");
            }

            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == nint.Zero || previousBitmap == new nint(-1))
            {
                throw CreateWin32Exception("Could not select the FSS capture buffer.");
            }

            if (
                !BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, SrcCopy | CaptureBlt)
            )
            {
                throw CreateWin32Exception("Could not copy the Elite Dangerous window.");
            }

            byte[] managedPixels = new byte[byteCount];
            Marshal.Copy(pixels, managedPixels, 0, managedPixels.Length);
            return new CapturedPixelBuffer(bounds.Width, bounds.Height, managedPixels);
        }
        finally
        {
            if (previousBitmap != nint.Zero && previousBitmap != new nint(-1) && memoryDc != nint.Zero)
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

    public void Dispose() { }

    private static int ValidateBounds(PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "The capture bounds must have a positive size.");
        }

        long byteCount = checked((long)bounds.Width * bounds.Height * 4);
        if (byteCount > MaximumCaptureBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "The capture bounds exceed the 256 MiB safety limit."
            );
        }

        return (int)byteCount;
    }

    private static Win32Exception CreateWin32Exception(string context)
    {
        int error = Marshal.GetLastPInvokeError();
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
        uint offset
    );

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint deviceContext, nint graphicsObject);

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
        uint rasterOperation
    );

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

    public string? UnavailableReason => IsAvailable ? null : "The X11 display connection is closed.";

    public static IGameScreenCapture? TryCreate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        nint display = nint.Zero;
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
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            if (display != nint.Zero)
            {
                try
                {
                    _ = X11Native.XCloseDisplay(display);
                }
                finally
                {
                    X11OverlayPlatformService.UnregisterErrorHandledDisplay(display);
                }
            }

            return null;
        }
    }

    public CapturedPixelBuffer Capture(PixelRect bounds)
    {
        ObjectDisposedException.ThrowIf(display == nint.Zero, this);

        ValidateBounds(bounds);
        PixelRect captureBounds = ClipToRootWindow(bounds);
        nint image = X11Native.XGetImage(
            display,
            rootWindow,
            captureBounds.X,
            captureBounds.Y,
            (uint)captureBounds.Width,
            (uint)captureBounds.Height,
            nuint.MaxValue,
            X11Native.ZPixmap
        );
        if (image == nint.Zero)
        {
            throw new InvalidOperationException("X11 could not capture the Elite Dangerous window.");
        }

        try
        {
            X11ImageMetadata metadata = Marshal.PtrToStructure<X11ImageMetadata>(image);
            return Decode(metadata);
        }
        finally
        {
            _ = X11Native.XDestroyImage(image);
        }
    }

    public void Dispose()
    {
        nint currentDisplay = display;
        display = nint.Zero;
        if (currentDisplay != nint.Zero)
        {
            try
            {
                _ = X11Native.XCloseDisplay(currentDisplay);
            }
            finally
            {
                X11OverlayPlatformService.UnregisterErrorHandledDisplay(currentDisplay);
            }
        }
    }

    internal static CapturedPixelBuffer Decode(X11ImageMetadata image)
    {
        if (
            image.Width <= 0
            || image.Height <= 0
            || image.Data == nint.Zero
            || image.BytesPerLine <= 0
            || image.BitsPerPixel is not (16 or 24 or 32)
            || image.RedMask == 0
            || image.GreenMask == 0
            || image.BlueMask == 0
        )
        {
            throw new InvalidDataException("The X11 capture returned an unsupported image layout.");
        }

        int bytesPerPixel = image.BitsPerPixel / 8;
        if (image.BytesPerLine < checked(image.Width * bytesPerPixel))
        {
            throw new InvalidDataException("The X11 capture stride is shorter than a pixel row.");
        }

        long sourceLength = checked((long)image.BytesPerLine * image.Height);
        long targetLength = checked((long)image.Width * image.Height * 4);
        if (sourceLength > MaximumCaptureBytes || targetLength > MaximumCaptureBytes)
        {
            throw new InvalidDataException("The X11 capture exceeds the 256 MiB safety limit.");
        }

        byte[] source = new byte[(int)sourceLength];
        Marshal.Copy(image.Data, source, 0, source.Length);
        byte[] target = new byte[(int)targetLength];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int sourceOffset = (y * image.BytesPerLine) + (x * bytesPerPixel);
                ulong pixel = ReadPixel(source.AsSpan(sourceOffset, bytesPerPixel), image.ByteOrder == LsbFirst);
                int targetOffset = ((y * image.Width) + x) * 4;
                target[targetOffset] = ExtractChannel(pixel, image.BlueMask);
                target[targetOffset + 1] = ExtractChannel(pixel, image.GreenMask);
                target[targetOffset + 2] = ExtractChannel(pixel, image.RedMask);
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
            for (int index = bytes.Length - 1; index >= 0; index--)
            {
                value = (value << 8) | bytes[index];
            }
        }
        else
        {
            foreach (byte current in bytes)
            {
                value = (value << 8) | current;
            }
        }

        return value;
    }

    private static byte ExtractChannel(ulong pixel, nuint nativeMask)
    {
        ulong mask = unchecked((ulong)nativeMask);
        int shift = BitOperations.TrailingZeroCount(mask);
        ulong maximum = mask >> shift;
        ulong value = (pixel & mask) >> shift;
        return checked((byte)((value * 255 + (maximum / 2)) / maximum));
    }

    private static void ValidateBounds(PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "The capture bounds must have a positive size.");
        }

        long byteCount = checked((long)bounds.Width * bounds.Height * 4);
        if (byteCount > MaximumCaptureBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "The capture bounds exceed the 256 MiB safety limit."
            );
        }
    }

    private PixelRect ClipToRootWindow(PixelRect bounds)
    {
        if (
            X11Native.XGetWindowAttributes(display, rootWindow, out X11Native.XWindowAttributes rootAttributes) == 0
            || rootAttributes.Width <= 0
            || rootAttributes.Height <= 0
        )
        {
            throw new InvalidOperationException("X11 could not read the desktop capture bounds.");
        }

        return ClipToRootWindow(bounds, rootAttributes.Width, rootAttributes.Height);
    }

    internal static PixelRect ClipToRootWindow(PixelRect bounds, int rootWidth, int rootHeight)
    {
        ValidateBounds(bounds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootHeight);

        long left = Math.Max(0L, bounds.X);
        long top = Math.Max(0L, bounds.Y);
        long right = Math.Min(rootWidth, checked((long)bounds.X + bounds.Width));
        long bottom = Math.Min(rootHeight, checked((long)bounds.Y + bounds.Height));
        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException("The Elite Dangerous capture area is outside the X11 desktop.");
        }

        return new PixelRect(
            checked((int)left),
            checked((int)top),
            checked((int)(right - left)),
            checked((int)(bottom - top))
        );
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
