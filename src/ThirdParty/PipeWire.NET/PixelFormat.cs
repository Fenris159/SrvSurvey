namespace PipeWire.NET;

/// <summary>Pixel formats used by <see cref="PipeWireVideoCapture"/> and <see cref="PipeWireVideoOutput"/>.</summary>
public enum PixelFormat
{
    /// <summary>32-bit RGBA, 8 bits per channel.</summary>
    Rgba,

    /// <summary>32-bit BGRA, 8 bits per channel (common on X11/Wayland compositors).</summary>
    Bgra,

    /// <summary>YUV 4:2:0 planar I420 (separate Y, U, V planes).</summary>
    Yuv420,

    /// <summary>YUV 4:2:0 semi-planar NV12 (Y plane then a single interleaved UV plane). The native
    /// output of most hardware video decoders (VAAPI), so the preferred zero-copy dmabuf format.</summary>
    Nv12,

    /// <summary>YUV 4:2:2 packed (YUYV byte order).</summary>
    Yuyv,

    /// <summary>32-bit RGBX (alpha channel ignored / always 0xFF).</summary>
    Rgbx,

    /// <summary>32-bit BGRX (alpha channel ignored / always 0xFF).</summary>
    Bgrx,
}
