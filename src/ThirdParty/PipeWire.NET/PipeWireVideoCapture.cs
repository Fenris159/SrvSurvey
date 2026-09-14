using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PipeWire.NET.Generated;
using PipeWire.NET.Spa;

namespace PipeWire.NET;

/// <summary>
/// Receives video frames from a PipeWire source (V4L2 camera, virtual camera,
/// screen-capture portal node, or another app's video output).
/// </summary>
/// <remarks>
/// <see cref="FrameReady"/> fires on the PipeWire loop thread; the <see cref="VideoFrame"/>
/// is a <see langword="ref struct"/> whose data is valid only for the duration of the handler.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed partial class PipeWireVideoCapture : IAsyncDisposable
{
    /// <summary>Wildcard node id - let PipeWire auto-select a source.</summary>
    public const uint AnyNode = Native.PW_ID_ANY;

    /// <summary>Signature for <see cref="FrameReady"/>.</summary>
    public delegate void FrameReadyHandler(PipeWireVideoCapture sender, VideoFrame frame);

    /// <summary>Signature for <see cref="StateChanged"/>.</summary>
    public delegate void StateChangedHandler(PipeWireVideoCapture sender, PipeWireStreamState oldState, PipeWireStreamState newState);

    /// <summary>Raised on the loop thread when a frame is ready. Do not cache the frame.</summary>
    public event FrameReadyHandler? FrameReady;

    /// <summary>Raised when the connection state changes.</summary>
    public event StateChangedHandler? StateChanged;

    private readonly PipeWireContext _ctx;
    private readonly string _name;
    private readonly ILogger _logger;
    private PipeWireStreamCore? _core;
    private ulong _sequence;
    private SpaFormat.VideoFormatInfo _fmt = new(PixelFormat.Bgra, 0, 0, VideoColorInfo.Unknown);

    // Whether the caller opted into modifier negotiation, and the single pixel format the modifiers
    // apply to. We do NOT retain the offered modifier list: fixation re-offers the producer's preferred
    // returned modifier (carried as the scalar _fmt.Modifier), which is always within our offered set.
    private bool _modifiersOffered;
    private PixelFormat _modifierFormat;
    private bool _modifierFixated;

    /// <summary>
    /// The DRM format modifier negotiated for delivered dmabuf frames, or
    /// <see cref="DrmFormatModifier.Invalid"/> when none (host-memory path or no modifier offered).
    /// </summary>
    public ulong NegotiatedModifier => _fmt.Modifier;

    /// <param name="context">A started <see cref="PipeWireContext"/>.</param>
    /// <param name="name">node.name advertised in the graph.</param>
    public PipeWireVideoCapture(PipeWireContext context, string name = "PipeWire.NET.VideoCapture")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _ctx = context;
        _name = name;
        _logger = context.LoggerFactory.CreateLogger($"PipeWire.NET.{name}");
    }

    /// <summary>Connects to a discovered source.</summary>
    public void Connect(PipeWireSource source, ReadOnlySpan<PixelFormat> preferredFormats = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Connect(source.NodeId, preferredFormats);
    }

    /// <summary>Connects to a source by node id (default: auto-select).</summary>
    /// <param name="targetNodeId">Source node id, or <see cref="AnyNode"/> to auto-select.</param>
    /// <param name="preferredFormats">Preferred pixel formats in priority order.</param>
    /// <param name="targetObjectName">
    /// Optional <c>target.object</c> - bind to a specific node by name/serial regardless of
    /// the session manager's default-device routing.
    /// </param>
    /// <param name="modifiers">
    /// DRM format modifiers to offer for a zero-copy dmabuf negotiation (the consumer's
    /// GPU-importable set). When non-empty, <paramref name="preferredFormats"/> must name exactly one
    /// format. The library auto-fixates to the producer's preferred modifier from this set.
    /// </param>
    public unsafe void Connect(uint targetNodeId = AnyNode,
        ReadOnlySpan<PixelFormat> preferredFormats = default,
        string? targetObjectName = null,
        ReadOnlySpan<long> modifiers = default)
    {
        if (_core is not null) throw new InvalidOperationException("Already connected.");
        if (!modifiers.IsEmpty && preferredFormats.Length != 1)
            throw new ArgumentException(
                "Exactly one pixel format must be specified when offering DRM modifiers (modifiers are per-format).",
                nameof(preferredFormats));

        _modifiersOffered = !modifiers.IsEmpty;
        _modifierFormat = preferredFormats.Length == 1 ? preferredFormats[0] : default;
        _modifierFixated = false;

        var props = new StreamProperties(StreamMediaType.Video, StreamCategory.Capture)
            .WithRole("Camera")
            .WithNodeName(_name);
        if (targetObjectName is not null) props.WithTargetObject(targetObjectName);

        _core = new PipeWireStreamCore(_ctx, props, _name, OnBuffer, OnState, OnFormat, OnPostFormat);

        Span<byte> pod = stackalloc byte[1024];
        int len = SpaFormat.WriteVideoFormat(pod, preferredFormats, 1920, 1080, 30, fixedSize: false,
            modifiers: modifiers);
        _core.Connect(spa_direction.SPA_DIRECTION_INPUT, targetNodeId,
            pw_stream_flags.PW_STREAM_FLAG_AUTOCONNECT | pw_stream_flags.PW_STREAM_FLAG_MAP_BUFFERS,
            pod[..len]);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _core?.DisposeAsync() ?? ValueTask.CompletedTask;

    private unsafe void OnBuffer(spa_data* d, pw_buffer* buf, in PipeWireStreamCore.StreamClock clock)
    {
        if (d->chunk is null) return;

        uint offset = d->chunk->offset;
        uint size   = d->chunk->size;
        if (size == 0) return;

        // data may be null for a pure DMA-BUF buffer that wasn't host-mapped.
        var pixels = d->data is null
            ? ReadOnlySpan<byte>.Empty
            : new ReadOnlySpan<byte>((byte*)d->data + offset, (int)size);

        PipeWireBufferType bufferType = SpaFormat.ToBufferType(d->type);
        bool fdBacked = bufferType is PipeWireBufferType.DmaBuf or PipeWireBufferType.MemFd;
        long fd = fdBacked && (long)d->fd >= 0 ? (long)d->fd : -1;

        // Surface every plane of an fd-backed frame so a consumer can import it zero-copy: a planar
        // dmabuf (NV12) carries one spa_data per plane, each with its own fd/offset/stride. Host-memory
        // frames keep the single-plane view (Planes stays empty).
        spa_buffer* spaBuf = buf->buffer;
        Span<VideoPlane> planes = stackalloc VideoPlane[8];
        int planeCount = 0;
        if (fdBacked)
        {
            uint n = Math.Min(spaBuf->n_datas, (uint)planes.Length);
            for (uint i = 0; i < n; i++)
            {
                spa_data* p = &spaBuf->datas[i];
                if (p->chunk is null) continue;
                // For dmabuf the plane's offset within its fd is chunk->offset; mapoffset is an mmap
                // concept (0 for dmabuf). stride is signed in SPA but always >= 0 for video here.
                planes[planeCount++] = new VideoPlane(
                    (long)p->fd, p->chunk->offset, p->chunk->stride, p->maxsize);
            }
        }

        var frame = new VideoFrame(
            pixels, d->chunk->stride, _fmt.Width, _fmt.Height, _fmt.Format, ++_sequence,
            bufferType: bufferType,
            fd: fd,
            mapOffset: d->mapoffset,
            presentationTimeNs: SpaFormat.FindPresentationTimeNs(buf->buffer),
            color: _fmt.Color,
            captureClockNs: clock.CaptureClockNs,
            mediaClockNs: clock.MediaClockNs,
            delayNs: clock.DelayNs,
            modifier: fdBacked ? _fmt.Modifier : DrmFormatModifier.Invalid,
            planes: planes[..planeCount]);

        FrameReady?.Invoke(this, frame);
    }

    private void OnState(PipeWireStreamState oldState, PipeWireStreamState newState) =>
        StateChanged?.Invoke(this, oldState, newState);

    private unsafe void OnFormat(spa_pod* param)
    {
        _fmt = SpaFormat.ParseVideoFormat(param, _fmt);
        LogNegotiatedFormat(_fmt.Format, _fmt.Width, _fmt.Height, _fmt.Modifier, _fmt.ModifierNeedsFixation);
    }

    // After the format is negotiated we know the geometry, so declare our buffer needs:
    // accept host memory AND DMA-BUF (zero-copy GPU), plus request the PTS header meta.
    private void OnPostFormat(PipeWireStreamCore core)
    {
        // Two-step modifier fixation: when we offered a modifier choice with DONT_FIXATE the producer
        // returns the subset it supports without collapsing it (ModifierNeedsFixation). Because we only
        // ever offer modifiers our GPU can import, the producer's preferred returned modifier
        // (_fmt.Modifier) is always safe - so re-offer just that single value, with DONT_FIXATE cleared,
        // to fixate the negotiation. One scalar, one stack buffer, no allocation. Done once.
        if (!_modifierFixated && _modifiersOffered && _fmt.ModifierNeedsFixation)
        {
            _modifierFixated = true;
            Span<byte> fixate = stackalloc byte[512];
            ReadOnlySpan<PixelFormat> fmt = [_modifierFormat];
            ReadOnlySpan<long> chosen = [(long)_fmt.Modifier];
            int fl = SpaFormat.WriteVideoFormat(fixate, fmt,
                (uint)_fmt.Width, (uint)_fmt.Height, 30, fixedSize: false,
                modifiers: chosen, fixateModifier: true);
            core.RequestParams(fixate[..fl]);
            return; // a fresh param_changed will arrive with the fixated format
        }

        Span<byte> meta = stackalloc byte[64];
        int ml = SpaFormat.WriteHeaderMetaParam(meta);

        int stride = SpaFormat.VideoStride(_fmt.Format, _fmt.Width);
        int size = SpaFormat.VideoImageSize(_fmt.Format, _fmt.Width, _fmt.Height);
        if (size <= 0) { core.RequestParams(meta[..ml]); return; }   // geometry not known yet

        // Block count = number of planes. A planar format (I420=3, NV12=2) is carried as one spa_data
        // block per plane for BOTH host memory (one MemFd per plane) and DMA-BUF (one fd per plane) -
        // gst's pipewiresink splits the planes either way. Declaring a single block for a multi-plane
        // format makes the daemon reject buffer allocation ("alloc buffers: Invalid argument"); packed
        // formats are a single block. Offer host memory and DMA-BUF so a GPU producer can go zero-copy.
        int blocks = SpaFormat.VideoPlaneCount(_fmt.Format);
        Span<byte> buffers = stackalloc byte[256];
        int bl = SpaFormat.WriteVideoBuffersParam(buffers, size, stride, SpaFormat.VideoCaptureDataTypeMask, blocks);

        LogRequestedBuffers(blocks, size, stride, SpaFormat.VideoCaptureDataTypeMask);
        core.RequestParams(buffers[..bl], meta[..ml]);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "negotiated format {Format} {Width}x{Height} modifier=0x{Modifier:x} needsFixation={NeedsFixation}")]
    private partial void LogNegotiatedFormat(PixelFormat format, int width, int height, ulong modifier, bool needsFixation);

    [LoggerMessage(Level = LogLevel.Debug, Message = "requesting buffers blocks={Blocks} size={Size} stride={Stride} dataTypeMask=0x{DataTypeMask:x}")]
    private partial void LogRequestedBuffers(int blocks, int size, int stride, int dataTypeMask);
}
