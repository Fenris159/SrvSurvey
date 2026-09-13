using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PipeWire.NET.Generated;
using PipeWire.NET.Spa;

namespace PipeWire.NET;

/// <summary>
/// Publishes video frames TO PipeWire as a virtual camera. Two modes:
/// <list type="bullet">
/// <item>Host memory (default): PipeWire pulls frames by invoking <see cref="FillFrame"/>; write your
/// pixels into the supplied span.</item>
/// <item>Zero-copy dmabuf: call <see cref="ConnectDmaBuf"/> with the DRM modifiers your GPU can export.
/// The library negotiates dmabuf buffers and asks you (via <see cref="AllocateDmaBuf"/>) to back each
/// pool buffer with a dmabuf you own; you render into it and publish from <see cref="FillDmaBuf"/>. No
/// pixel copy ever touches the CPU.</item>
/// </list>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class PipeWireVideoOutput : IAsyncDisposable
{
    /// <summary>Signature for <see cref="FillFrame"/>. Return <see langword="true"/> to publish the frame.</summary>
    public delegate bool FillFrameHandler(
        PipeWireVideoOutput sender, Span<byte> pixels, int stride, int width, int height, PixelFormat format);

    /// <summary>
    /// Asks the app to back output pool buffer <paramref name="bufferIndex"/> with a dmabuf it owns: fill
    /// one <see cref="VideoPlane"/> per plane into <paramref name="planes"/> and return the plane count
    /// (0 to decline). Called once per pool buffer, on the loop thread, after dmabuf format negotiation -
    /// allocate your GPU surface (e.g. a Vulkan image exported to a dmabuf fd) for <paramref name="modifier"/> here.
    /// </summary>
    public delegate int AllocateDmaBufHandler(
        PipeWireVideoOutput sender, int bufferIndex, int width, int height, ulong modifier, Span<VideoPlane> planes);

    /// <summary>
    /// Asks the app to render the current frame into pool buffer <paramref name="bufferIndex"/>'s dmabuf and
    /// return <see langword="true"/> to publish it (false to emit an empty frame). Called on the loop thread.
    /// </summary>
    public delegate bool FillDmaBufHandler(PipeWireVideoOutput sender, int bufferIndex);

    /// <summary>Notifies the app that pool buffer <paramref name="bufferIndex"/>'s dmabuf can be released.</summary>
    public delegate void ReleaseDmaBufHandler(PipeWireVideoOutput sender, int bufferIndex);

    /// <summary>Invoked on the loop thread when a host-memory buffer is ready to fill.</summary>
    public event FillFrameHandler? FillFrame;

    /// <summary>Invoked (dmabuf mode) to back a pool buffer with an app-owned dmabuf.</summary>
    public event AllocateDmaBufHandler? AllocateDmaBuf;

    /// <summary>Invoked (dmabuf mode) to render and publish the current frame.</summary>
    public event FillDmaBufHandler? FillDmaBuf;

    /// <summary>Invoked (dmabuf mode) when a pool buffer's dmabuf can be released.</summary>
    public event ReleaseDmaBufHandler? ReleaseDmaBuf;

    /// <summary>Raised when the connection state changes.</summary>
    public event Action<PipeWireVideoOutput, PipeWireStreamState, PipeWireStreamState>? StateChanged;

    private readonly PipeWireContext _ctx;
    private readonly string _name;
    private readonly int _width, _height, _frameRate;
    private readonly PixelFormat _format;
    private readonly ILogger _logger;
    private PipeWireStreamCore? _core;

    // dmabuf-mode state. Set once at ConnectDmaBuf; the modifier is fixated during negotiation. We never
    // retain the offered modifier list (see VideoFormatInfo for why) - fixation re-offers _fmt.Modifier.
    private bool _dmaBufMode;
    private bool _modifierFixated;
    private long[] _modifiers = [];
    private SpaFormat.VideoFormatInfo _fmt;
    private int _planeCount;
    private int _nextBufferIndex;

    /// <param name="context">A started <see cref="PipeWireContext"/>.</param>
    /// <param name="nodeName">Name visible to consumers.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="format">Pixel format to publish.</param>
    /// <param name="frameRate">Target frame rate (Hz).</param>
    public PipeWireVideoOutput(PipeWireContext context, string nodeName,
        int width, int height, PixelFormat format = PixelFormat.Bgra, int frameRate = 30)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(nodeName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRate);
        _ctx = context; _name = nodeName;
        _width = width; _height = height; _format = format; _frameRate = frameRate;
        _fmt = new SpaFormat.VideoFormatInfo(format, width, height, VideoColorInfo.Unknown);
        _logger = context.LoggerFactory.CreateLogger($"PipeWire.NET.{nodeName}");
    }

    /// <summary>Starts publishing host-memory frames and registers the node in the graph.</summary>
    public unsafe void Connect()
    {
        if (_core is not null) throw new InvalidOperationException("Already connected.");

        var props = new StreamProperties(StreamMediaType.Video, StreamCategory.Playback)
            .WithRole("Camera")
            .WithNodeName(_name);

        // OnPostFormatHostMem declares the buffer requirements once the format is set. This is mandatory for a
        // video producer: unlike audio (whose buffer size PipeWire derives from the graph clock), a video node
        // must advertise the exact image size/stride, or the daemon cannot size the shared-memory buffers -
        // negotiation then drives pw_impl_port_set_param into a bad dereference (a hard crash) and the consumer
        // only ever dequeues empty (size-0) buffers. PipeWire's own video-src.c declares Buffers the same way.
        _core = new PipeWireStreamCore(_ctx, props, _name, OnBuffer, OnState, onPostFormat: OnPostFormatHostMem);

        Span<byte> pod = stackalloc byte[512];
        int len = SpaFormat.WriteVideoFormat(pod,
            stackalloc[] { _format }, (uint)_width, (uint)_height, (uint)_frameRate, fixedSize: true);
        _core.Connect(spa_direction.SPA_DIRECTION_OUTPUT, Native.PW_ID_ANY,
            pw_stream_flags.PW_STREAM_FLAG_AUTOCONNECT | pw_stream_flags.PW_STREAM_FLAG_MAP_BUFFERS,
            pod[..len]);
    }

    // Host-memory buffer requirements: one contiguous MemPtr block holding the whole image (the OnBuffer path
    // fills datas[0] with stride*height bytes), plus the SPA_META_Header so frames carry a PTS.
    private void OnPostFormatHostMem(PipeWireStreamCore core)
    {
        int stride = SpaFormat.VideoStride(_format, _width);
        int size = SpaFormat.VideoImageSize(_format, _width, _height);
        Span<byte> buffers = stackalloc byte[256];
        int bl = SpaFormat.WriteVideoBuffersParam(buffers, size, stride,
            dataTypes: 1 << (int)SpaType.DataMemPtr, blocks: 1);

        Span<byte> meta = stackalloc byte[64];
        int ml = SpaFormat.WriteHeaderMetaParam(meta);
        core.RequestParams(buffers[..bl], meta[..ml]);
    }

    /// <summary>
    /// Starts publishing zero-copy dmabuf frames, offering the given DRM format modifiers (the set your
    /// GPU can export for the configured <see cref="PixelFormat"/>). Wire <see cref="AllocateDmaBuf"/>,
    /// <see cref="FillDmaBuf"/> and (optionally) <see cref="ReleaseDmaBuf"/> before calling.
    /// </summary>
    public unsafe void ConnectDmaBuf(ReadOnlySpan<long> modifiers)
    {
        if (_core is not null) throw new InvalidOperationException("Already connected.");
        if (modifiers.IsEmpty) throw new ArgumentException("At least one DRM modifier must be offered.", nameof(modifiers));

        _dmaBufMode = true;
        _modifierFixated = false;
        _planeCount = SpaFormat.PlaneCount(_format);

        var props = new StreamProperties(StreamMediaType.Video, StreamCategory.Playback)
            .WithRole("Camera")
            .WithNodeName(_name);

        // add_buffer/remove_buffer let us back each pool buffer with an app-owned dmabuf; the producer
        // supplies the memory, so we use ALLOC_BUFFERS (and NOT MAP_BUFFERS - there is nothing to mmap).
        _modifiers = modifiers.ToArray();
        _core = new PipeWireStreamCore(_ctx, props, _name, OnBuffer, OnState, OnFormat, OnPostFormat,
            OnAddBuffer, OnRemoveBuffer, OnPeerConnected);

        // Offer modifiers with DONT_FIXATE so a GL consumer's EGL selects an importable modifier (radeonsi only
        // imports the tiled AMD modifiers, not LINEAR). Connect INACTIVE|ALLOC_BUFFERS (NOT a DRIVER - the
        // consumer drives the graph clock; a DRIVER would have to pace itself via trigger_process, which is
        // unsafe to call from another thread and crashes libpipewire). On a consumer link the daemon delivers
        // SPA_PARAM_PeerCapability, where OnPeerConnected re-announces the EnumFormat and activates the stream,
        // kicking the (now-correct) modifier fixation; the consumer then drives FillDmaBuf.
        Span<byte> pod = stackalloc byte[512];
        int len = SpaFormat.WriteVideoFormat(pod,
            stackalloc[] { _format }, (uint)_width, (uint)_height, (uint)_frameRate, fixedSize: true,
            modifiers: modifiers);
        _core.Connect(spa_direction.SPA_DIRECTION_OUTPUT, Native.PW_ID_ANY,
            pw_stream_flags.PW_STREAM_FLAG_INACTIVE | pw_stream_flags.PW_STREAM_FLAG_ALLOC_BUFFERS,
            pod[..len]);
    }

    // A consumer linked (SPA_PARAM_PeerCapability): re-announce the EnumFormat so the daemon negotiates a
    // format with the peer, then activate the INACTIVE stream - the video-src-fixate.c producer flow. Loop
    // lock is held here. (Earlier crashes from this path were the separate trigger_process threading race,
    // now fixed by gating TriggerFrame on the streaming state.)
    private unsafe void OnPeerConnected(PipeWireStreamCore core)
    {
        Span<byte> pod = stackalloc byte[512];
        ReadOnlySpan<PixelFormat> fmt = [_format];
        int len = SpaFormat.WriteVideoFormat(pod, fmt,
            (uint)_width, (uint)_height, (uint)_frameRate, fixedSize: true, modifiers: _modifiers);
        core.RequestParams(pod[..len]);
        core.SetActive(true, lockHeld: true);
    }

    /// <summary>
    /// Drives one publish cycle for the dmabuf DRIVER producer (calls <c>pw_stream_trigger_process</c>), which
    /// fires <see cref="FillDmaBuf"/> on the loop thread. Call after staging a new frame; no-op until connected.
    /// </summary>
    public void TriggerFrame() => _core?.TriggerProcess();

    /// <summary>The graph node id of this output once connected (for a consumer to target it directly).</summary>
    public uint NodeId => _core?.NodeId ?? Native.PW_ID_ANY;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _core?.DisposeAsync() ?? ValueTask.CompletedTask;

    private unsafe void OnBuffer(spa_data* d, pw_buffer* buf, in PipeWireStreamCore.StreamClock clock)
    {
        if (_dmaBufMode) { FillDmaBufBuffer(buf); return; }

        if (d->data is null || d->chunk is null) return;

        int stride  = _width * SpaFormat.BytesPerPixel(_format);
        int byteLen = stride * _height;
        if ((uint)byteLen > d->maxsize) byteLen = (int)d->maxsize;

        var pixels = new Span<byte>(d->data, byteLen);
        bool publish = FillFrame?.Invoke(this, pixels, stride, _width, _height, _format) ?? false;

        d->chunk->offset = 0;
        d->chunk->stride = stride;
        d->chunk->size   = publish ? (uint)byteLen : 0;
    }

    // Producer process for a dmabuf buffer: the dmabuf layout (offset/stride) was fixed in add_buffer, so
    // here we only ask the app to render the frame and then mark each plane's chunk size to publish it (or
    // 0 to emit an empty frame). The fd/plane geometry never changes, hence no per-frame copy.
    private unsafe void FillDmaBufBuffer(pw_buffer* buf)
    {
        spa_buffer* sb = buf->buffer;
        if (sb is null) return;
        int index = (int)(nint)buf->user_data - 1; // we store index+1 so 0 means "unassigned"
        if (index < 0) return;

        bool publish = FillDmaBuf?.Invoke(this, index) ?? false;

        for (uint i = 0; i < sb->n_datas; i++)
        {
            spa_chunk* c = sb->datas[i].chunk;
            if (c is null) continue;
            // maxsize was set to the plane's byte size in add_buffer; publish that, or 0 to skip.
            c->size = publish ? sb->datas[i].maxsize : 0;
        }
    }

    private unsafe void OnFormat(spa_pod* param)
    {
        _fmt = SpaFormat.ParseVideoFormat(param, _fmt);
        LogOnFormat(_fmt.Modifier, _fmt.ModifierNeedsFixation);
    }

    private void OnPostFormat(PipeWireStreamCore core)
    {
        LogOnPostFormat(_modifierFixated, _fmt.ModifierNeedsFixation, _planeCount);

        // Mirror the consumer's two-step modifier fixation on the producer side: when the peer honoured
        // DONT_FIXATE we re-offer our preferred returned modifier alone (DONT_FIXATE cleared) to settle it.
        if (!_modifierFixated && _fmt.ModifierNeedsFixation)
        {
            _modifierFixated = true;
            Span<byte> fixate = stackalloc byte[512];
            ReadOnlySpan<PixelFormat> fmt = [_format];
            ReadOnlySpan<long> chosen = [(long)_fmt.Modifier];
            int fl = SpaFormat.WriteVideoFormat(fixate, fmt,
                (uint)_width, (uint)_height, (uint)_frameRate, fixedSize: true,
                modifiers: chosen, fixateModifier: true);
            core.RequestParams(fixate[..fl]);
            return; // a fresh param_changed will arrive with the fixated modifier
        }

        // Declare dmabuf buffers: one block per plane, sized for the negotiated geometry. dataType is
        // DMA-BUF only - we are committing to hand over GPU buffers, not host memory.
        int stride = SpaFormat.VideoStride(_format, _width);
        int size = SpaFormat.VideoImageSize(_format, _width, _height);
        Span<byte> buffers = stackalloc byte[256];
        int bl = SpaFormat.WriteVideoBuffersParam(buffers, size, stride,
            dataTypes: 1 << (int)SpaType.DataDmaBuf, blocks: _planeCount);

        Span<byte> meta = stackalloc byte[64];
        int ml = SpaFormat.WriteHeaderMetaParam(meta);
        core.RequestParams(buffers[..bl], meta[..ml]);
    }

    // PipeWire allocated an (empty) buffer with _planeCount data blocks; back each block with one plane of
    // an app-owned dmabuf. We hand the app a stable per-buffer index (stored in pw_buffer.user_data) so it
    // can pair the buffer with a GPU surface it keeps for the buffer's lifetime.
    private unsafe void OnAddBuffer(pw_buffer* buf)
    {
        spa_buffer* sb = buf->buffer;
        if (sb is null) return;

        int index = _nextBufferIndex++;
        buf->user_data = (void*)(nint)(index + 1); // +1 so 0 distinguishes "unassigned"

        Span<VideoPlane> planes = stackalloc VideoPlane[8];
        int n = AllocateDmaBuf?.Invoke(this, index, _width, _height, _fmt.Modifier, planes) ?? 0;
        if (n <= 0) return; // app declined; buffer stays unbacked and PipeWire will not use it

        uint blocks = Math.Min(sb->n_datas, (uint)n);
        for (uint i = 0; i < blocks; i++)
        {
            VideoPlane p = planes[(int)i];
            spa_data* dd = &sb->datas[i];
            dd->type      = SpaType.DataDmaBuf;
            dd->flags     = SpaDataFlag.Readable;
            dd->fd        = (nint)p.Fd;
            dd->mapoffset = 0;
            dd->maxsize   = p.Size;
            dd->data      = null;       // dmabuf: consumer imports via fd, never a host pointer
            if (dd->chunk is not null)
            {
                dd->chunk->offset = p.Offset;
                dd->chunk->stride = p.Stride;
                dd->chunk->size   = p.Size;
            }
        }
    }

    private unsafe void OnRemoveBuffer(pw_buffer* buf)
    {
        int index = (int)(nint)buf->user_data - 1;
        if (index >= 0) ReleaseDmaBuf?.Invoke(this, index);
    }

    private void OnState(PipeWireStreamState oldState, PipeWireStreamState newState) =>
        StateChanged?.Invoke(this, oldState, newState);

    [LoggerMessage(Level = LogLevel.Debug, Message = "OnFormat modifier=0x{Modifier:x} needsFixation={NeedsFixation}")]
    private partial void LogOnFormat(ulong modifier, bool needsFixation);

    [LoggerMessage(Level = LogLevel.Debug, Message = "OnPostFormat fixated={Fixated} needsFixation={NeedsFixation} planeCount={PlaneCount}")]
    private partial void LogOnPostFormat(bool fixated, bool needsFixation, int planeCount);
}
