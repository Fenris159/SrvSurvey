namespace PipeWire.NET;

/// <summary>
/// A single video frame delivered by <see cref="PipeWireVideoCapture.FrameReady"/>.
/// The <see cref="Data"/> span is valid only for the duration of the event handler.
/// </summary>
public readonly ref struct VideoFrame
{
    /// <param name="data">
    /// Raw pixel data (host-mapped). Empty for a pure <see cref="PipeWireBufferType.DmaBuf"/>
    /// frame that was not memory-mapped - use <paramref name="fd"/> for zero-copy import.
    /// </param>
    /// <param name="stride">Bytes per row in the primary plane.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="format">Pixel format.</param>
    /// <param name="sequenceNumber">Monotonically increasing per-session counter.</param>
    /// <param name="bufferType">How the data is backed (host memory, fd, or DMA-BUF).</param>
    /// <param name="fd">Backing file descriptor (DMA-BUF / MemFd), or -1 when host-only.</param>
    /// <param name="mapOffset">Offset of the mapped region within the backing memory/fd.</param>
    /// <param name="presentationTimeNs">Presentation timestamp in nanoseconds, or -1 if unavailable.</param>
    /// <param name="color">Negotiated color metadata.</param>
    /// <param name="captureClockNs">Graph clock time (monotonic ns) of the capture cycle.</param>
    /// <param name="mediaClockNs">Media position (ns) at the cycle; -1 if unknown.</param>
    /// <param name="delayNs">Signal delay/latency (ns) from source to this stream.</param>
    /// <param name="modifier">
    /// Negotiated DRM format modifier for a dmabuf frame, or <see cref="DrmFormatModifier.Invalid"/>
    /// when none (host-memory path). Drives the GPU import layout.
    /// </param>
    /// <param name="planes">
    /// Per-plane dmabuf layout (fd/offset/stride/size). Multi-plane formats expose every plane here;
    /// for a single-plane frame this carries the one plane and mirrors
    /// <paramref name="fd"/>/<paramref name="stride"/>. Empty for a host-memory frame.
    /// </param>
    public VideoFrame(
        ReadOnlySpan<byte> data,
        int stride,
        int width,
        int height,
        PixelFormat format,
        ulong sequenceNumber,
        PipeWireBufferType bufferType = PipeWireBufferType.MemPtr,
        long fd = -1,
        uint mapOffset = 0,
        long presentationTimeNs = -1,
        VideoColorInfo color = default,
        long captureClockNs = -1,
        long mediaClockNs = -1,
        long delayNs = 0,
        ulong modifier = DrmFormatModifier.Invalid,
        ReadOnlySpan<VideoPlane> planes = default)
    {
        Data               = data;
        Stride             = stride;
        Width              = width;
        Height             = height;
        Format             = format;
        SequenceNumber     = sequenceNumber;
        BufferType         = bufferType;
        Fd                 = fd;
        MapOffset          = mapOffset;
        PresentationTimeNs = presentationTimeNs;
        Color              = color;
        CaptureClockNs     = captureClockNs;
        MediaClockNs       = mediaClockNs;
        DelayNs            = delayNs;
        Modifier           = modifier;
        Planes             = planes;
    }

    /// <summary>Raw pixel bytes. Empty for an unmapped DMA-BUF frame (use <see cref="Fd"/>).</summary>
    public ReadOnlySpan<byte> Data { get; }

    /// <summary>Bytes per row in the primary plane.</summary>
    public int Stride { get; }

    /// <summary>Frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Frame height in pixels.</summary>
    public int Height { get; }

    /// <summary>Pixel format of <see cref="Data"/>.</summary>
    public PixelFormat Format { get; }

    /// <summary>Monotonically increasing frame index for this session.</summary>
    public ulong SequenceNumber { get; }

    /// <summary>How the frame data is backed in memory.</summary>
    public PipeWireBufferType BufferType { get; }

    /// <summary>
    /// Backing file descriptor for <see cref="PipeWireBufferType.DmaBuf"/> /
    /// <see cref="PipeWireBufferType.MemFd"/>, or -1 when host-memory only.
    /// Import this into the GPU for a zero-copy pipeline.
    /// </summary>
    public long Fd { get; }

    /// <summary>Offset of the mapped region within the backing memory / fd.</summary>
    public uint MapOffset { get; }

    /// <summary>Negotiated color metadata (range/matrix/transfer/primaries) - all <c>Unknown</c> if unreported.</summary>
    public VideoColorInfo Color { get; }

    /// <summary>
    /// Graph clock time (monotonic ns) of the processing cycle that delivered this frame,
    /// from <c>pw_stream_get_time</c>. This is the SAME clock for every stream in the graph,
    /// so audio and video frames can be aligned on it for A/V sync. -1 if unavailable.
    /// </summary>
    public long CaptureClockNs { get; }

    /// <summary>Media position (ns) of this stream at the capture cycle (<c>ticks*rate</c>); -1 if unknown.</summary>
    public long MediaClockNs { get; }

    /// <summary>
    /// Signal delay (ns) between the source and this stream. The frame's content corresponds to
    /// roughly <see cref="CaptureClockNs"/> - <see cref="DelayNs"/> on the shared clock - use this
    /// for latency-compensated, sample/frame-accurate timestamping.
    /// </summary>
    public long DelayNs { get; }

    /// <summary>
    /// Presentation timestamp in nanoseconds (from SPA_META_Header), or -1 if the source
    /// did not attach a header. Use for A/V sync over a transport like WebRTC.
    /// </summary>
    public long PresentationTimeNs { get; }

    /// <summary>
    /// Negotiated DRM format modifier for a DMA-BUF frame (tiling/compression layout), or
    /// <see cref="DrmFormatModifier.Invalid"/> when none was negotiated. Pair with
    /// <see cref="Planes"/> to import the frame zero-copy via <c>VK_EXT_image_drm_format_modifier</c>.
    /// </summary>
    public ulong Modifier { get; }

    /// <summary>
    /// Per-plane DMA-BUF layout (fd/offset/stride/size). Empty for a host-memory frame; for a
    /// DMA-BUF frame this carries every plane (NV12 = 2, packed = 1, plus any modifier aux planes).
    /// Valid only for the duration of the <see cref="PipeWireVideoCapture.FrameReady"/> handler.
    /// </summary>
    public ReadOnlySpan<VideoPlane> Planes { get; }

    /// <summary>True when the frame is backed by a DMA-BUF or MemFd file descriptor.</summary>
    public bool IsFdBacked => Fd >= 0;
}
