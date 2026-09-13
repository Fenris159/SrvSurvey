using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PipeWire.NET.Generated;

namespace PipeWire.NET;

/// <summary>
/// Owns the native lifecycle shared by every PipeWire stream wrapper: the
/// <c>pw_stream</c>, its event struct + listener hook, the self <see cref="GCHandle"/>,
/// buffer dequeue/queue, thread-loop locking, and disposal.
/// </summary>
/// <remarks>
/// The four public stream classes (video/audio x capture/output) are thin policy
/// layers over this core. They supply direction, properties, the format pod, and a
/// per-buffer handler; the core handles everything native and error-prone.
/// All <c>pw_stream</c> operations run under the context's thread-loop lock; the
/// <c>process</c> callback is invoked by the loop thread with that lock already held.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed unsafe partial class PipeWireStreamCore : IAsyncDisposable
{
    /// <summary>
    /// Timing snapshot for a processing cycle, from <c>pw_stream_get_time</c>. All on the one
    /// graph clock shared by every stream - the basis for A/V sync and sample-accurate position.
    /// </summary>
    /// <param name="CaptureClockNs">Monotonic graph time (ns) of the cycle. The sync reference.</param>
    /// <param name="MediaClockNs">Media position (ns) at the cycle, from <c>ticks * rate</c>; -1 if unknown.</param>
    /// <param name="DelayNs">Signal delay/latency (ns) between this stream and the hardware.</param>
    internal readonly record struct StreamClock(long CaptureClockNs, long MediaClockNs, long DelayNs);

    /// <summary>Invoked from <c>process</c> with the first data plane of a dequeued buffer.</summary>
    /// <param name="data">First data plane of the buffer.</param>
    /// <param name="buffer">The dequeued buffer (for metadata access).</param>
    /// <param name="clock">Timing snapshot for this cycle.</param>
    /// <remarks>The core dequeues before and queues after (even if this throws).</remarks>
    internal delegate void BufferHandler(spa_data* data, pw_buffer* buffer, in StreamClock clock);

    /// <summary>Invoked from <c>state_changed</c>.</summary>
    internal delegate void StateHandler(PipeWireStreamState oldState, PipeWireStreamState newState);

    /// <summary>Invoked from <c>param_changed</c> for the negotiated Format param only.</summary>
    internal delegate void FormatHandler(spa_pod* param);

    /// <summary>
    /// Invoked from <c>add_buffer</c>/<c>remove_buffer</c> when PipeWire allocates or frees a buffer in
    /// the negotiated pool. A dmabuf producer uses these to back each buffer's <c>spa_data</c> with its
    /// own dmabuf (in add) and release it (in remove). Both run on the loop thread with the lock held.
    /// </summary>
    internal delegate void BufferPoolHandler(pw_buffer* buffer);

    /// <summary>
    /// Invoked after <see cref="FormatHandler"/> so the stream can declare its buffer/meta
    /// requirements (it now knows the negotiated geometry). Call <see cref="RequestParams"/>
    /// from here. If not supplied, the core requests just the SPA_META_Header.
    /// </summary>
    internal delegate void PostFormatHandler(PipeWireStreamCore core);

    /// <summary>
    /// Invoked when a peer (consumer) links and the daemon reports <c>SPA_PARAM_PeerCapability</c>. A dmabuf
    /// DRIVER producer connected INACTIVE uses this to (re-)announce its EnumFormat and activate the stream,
    /// which is what kicks off format negotiation (pipewire's video-src-fixate.c does this).
    /// </summary>
    internal delegate void PeerConnectedHandler(PipeWireStreamCore core);

    // SPA_PARAM_PeerCapability (spa/param/param.h). The generated bindings predate it (they stop at Tag=17:
    // ... Tag(17), PeerEnumFormat(18), Capability(19), PeerCapability(20)); the enum is append-only so the
    // value is stable on the 1.6 runtime.
    private const uint SpaParamPeerCapability = 20;

    private readonly PipeWireContext _ctx;
    private readonly ILogger _logger;
    private readonly string _streamName;
    private bool _firstBufferLogged;
    private readonly BufferHandler _onBuffer;
    private readonly StateHandler? _onState;
    private readonly FormatHandler? _onFormat;
    private readonly PostFormatHandler? _onPostFormat;
    private readonly BufferPoolHandler? _onAddBuffer;
    private readonly BufferPoolHandler? _onRemoveBuffer;
    private readonly PeerConnectedHandler? _onPeerConnected;

    private pw_stream*        _stream;
    private pw_stream_events* _events;
    // The spa_hook MUST live in unmanaged memory, not as a managed field: pw_stream_add_listener stores this
    // pointer in the stream's listener list, and the GC compacting the heap would move a managed field, leaving
    // PipeWire with a dangling pointer that crashes (spa_list_remove on freed memory) the next time it emits an
    // event. _selfHandle is GCHandleType.Normal (non-pinning), so it does not keep a field address stable.
    private spa_hook*         _hook;
    private GCHandle          _selfHandle;
    private volatile bool     _disposed;

    /// <param name="ctx">A started <see cref="PipeWireContext"/>.</param>
    /// <param name="props">Stream properties (consumed by pw_stream_new).</param>
    /// <param name="streamName">node.name advertised by the stream.</param>
    /// <param name="onBuffer">Per-buffer handler (read for capture / fill for output).</param>
    /// <param name="onState">Optional state-change handler.</param>
    /// <param name="onFormat">Optional format-negotiation handler.</param>
    /// <param name="onPostFormat">Optional hook to declare buffer/meta params after format is set.</param>
    /// <param name="onAddBuffer">Optional hook to back a newly-allocated pool buffer with a dmabuf.</param>
    /// <param name="onRemoveBuffer">Optional hook to release a pool buffer's dmabuf before it is freed.</param>
    /// <param name="onPeerConnected">Optional hook invoked on SPA_PARAM_PeerCapability (a consumer linked).</param>
    internal PipeWireStreamCore(
        PipeWireContext ctx,
        StreamProperties props,
        string streamName,
        BufferHandler onBuffer,
        StateHandler? onState = null,
        FormatHandler? onFormat = null,
        PostFormatHandler? onPostFormat = null,
        BufferPoolHandler? onAddBuffer = null,
        BufferPoolHandler? onRemoveBuffer = null,
        PeerConnectedHandler? onPeerConnected = null)
    {
        _ctx            = ctx;
        _logger         = ctx.LoggerFactory.CreateLogger($"PipeWire.NET.{streamName}");
        _streamName     = streamName;
        _onBuffer       = onBuffer;
        _onState        = onState;
        _onPostFormat   = onPostFormat;
        _onFormat       = onFormat;
        _onAddBuffer    = onAddBuffer;
        _onRemoveBuffer = onRemoveBuffer;
        _onPeerConnected = onPeerConnected;

        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);

        _hook = (spa_hook*)NativeMemory.AllocZeroed((nuint)sizeof(spa_hook));
        _events = (pw_stream_events*)NativeMemory.AllocZeroed((nuint)sizeof(pw_stream_events));
        _events->version       = Native.PW_VERSION_STREAM_EVENTS;
        _events->process       = &OnProcess;
        _events->state_changed = &OnStateChanged;
        _events->param_changed = &OnParamChanged;
        if (onAddBuffer is not null)    _events->add_buffer    = &OnAddBuffer;
        if (onRemoveBuffer is not null) _events->remove_buffer = &OnRemoveBuffer;

        pw_properties* nativeProps = props.ToNativeProperties();

        ReadOnlySpan<byte> nameUtf8 = System.Text.Encoding.UTF8.GetBytes(streamName + '\0');
        using (_ctx.Lock())
        {
            fixed (byte* n = nameUtf8)
                _stream = Native.pw_stream_new(_ctx.CoreHandle, (sbyte*)n, nativeProps);

            if (_stream is null)
            {
                _selfHandle.Free();
                NativeMemory.Free(_events);
                _events = null;
                NativeMemory.Free(_hook);
                _hook = null;
                throw new InvalidOperationException("pw_stream_new failed.");
            }

            Native.pw_stream_add_listener(_stream, _hook, _events,
                (void*)GCHandle.ToIntPtr(_selfHandle));
        }
    }

    /// <summary>Connects the stream. <paramref name="formatPod"/> is copied by PipeWire before returning.</summary>
    /// <remarks>
    /// The SPA_META_Header (which carries the presentation timestamp) is NOT requested here -
    /// PipeWire's contract is to declare buffer/meta wants from the <c>param_changed</c> callback
    /// once the format is set, via <c>pw_stream_update_params</c>. The core does that automatically.
    /// </remarks>
    internal void Connect(spa_direction direction, uint targetNodeId, pw_stream_flags flags, ReadOnlySpan<byte> formatPod)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using (_ctx.Lock())
        {
            int rc;
            fixed (byte* fp = formatPod)
            {
                spa_pod* param = (spa_pod*)fp;
                rc = Native.pw_stream_connect(_stream, direction, targetNodeId, flags, &param, 1);
            }
            if (rc < 0)
                throw new InvalidOperationException($"pw_stream_connect failed with code {rc}.");
        }
    }

    /// <summary>
    /// Declares that delivered buffers should carry a SPA_META_Header (presentation timestamp).
    /// Must be called from the param_changed callback after the format is set - that is the
    /// point at which PipeWire accepts buffer/meta requests via pw_stream_update_params.
    /// </summary>
    private void RequestHeaderMeta()
    {
        Span<byte> metaPod = stackalloc byte[64];
        Spa.SpaFormat.WriteHeaderMetaParam(metaPod);
        fixed (byte* mp = metaPod)
        {
            spa_pod* p = (spa_pod*)mp;
            Native.pw_stream_update_params(_stream, &p, 1);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        // Disconnect/destroy must run under the loop lock to avoid racing a callback.
        if (_stream is not null)
        {
            using (_ctx.Lock())
            {
                Native.pw_stream_disconnect(_stream);
                Native.pw_stream_destroy(_stream);
                _stream = null;
            }
        }
        if (_events is not null)
        {
            NativeMemory.Free(_events);
            _events = null;
        }
        // Free the hook only after the stream is destroyed (destroy removes the listener that points at it).
        if (_hook is not null)
        {
            NativeMemory.Free(_hook);
            _hook = null;
        }
        if (_selfHandle.IsAllocated) _selfHandle.Free();

        return ValueTask.CompletedTask;
    }

    // - Native callbacks (invoked by the loop thread with the lock held) -

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnProcess(void* data)
    {
        var self = (PipeWireStreamCore?)GCHandle.FromIntPtr((nint)data).Target;
        if (self is null || self._disposed) return;

        pw_buffer* buf = Native.pw_stream_dequeue_buffer(self._stream);
        if (buf is null)
        {
            // No buffer queued for this cycle: the producer hasn't filled one yet (start-up) or is
            // underrunning. Common and benign at start, so Trace.
            self.LogDequeueEmpty();
            return;
        }
        try
        {
            spa_buffer* spaBuf = buf->buffer;
            if (spaBuf is null || spaBuf->n_datas == 0) return;

            if (!self._firstBufferLogged)
            {
                self._firstBufferLogged = true;
                spa_data* d0 = &spaBuf->datas[0];
                self.LogFirstBuffer(spaBuf->n_datas, d0->type, d0->chunk is null ? 0u : d0->chunk->size, d0->maxsize);
            }

            // Graph clock for this cycle - the common monotonic reference across all streams,
            // plus media position (ticks*rate) and latency (delay*rate) per PipeWire's timing model.
            StreamClock clock = new(-1, -1, 0);
            pw_time t;
            if (Native.pw_stream_get_time_n(self._stream, &t, (nuint)sizeof(pw_time)) == 0)
            {
                long num = t.rate.num, denom = t.rate.denom;     // seconds per tick = num/denom
                long mediaNs = denom != 0 ? (long)((double)(ulong)t.ticks * num / denom * 1e9) : -1;
                long delayNs = denom != 0 ? (long)((double)(long)t.delay * num / denom * 1e9) : 0;
                clock = new StreamClock((long)t.now, mediaNs, delayNs);
            }

            spa_data* d = &spaBuf->datas[0];
            self._onBuffer(d, buf, in clock);
        }
        catch
        {
            // A throwing user handler must not abort the realtime loop thread (unmanaged callback).
        }
        finally
        {
            Native.pw_stream_queue_buffer(self._stream, buf);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAddBuffer(void* data, pw_buffer* buffer)
    {
        var self = (PipeWireStreamCore?)GCHandle.FromIntPtr((nint)data).Target;
        if (self is null || self._disposed) return;
        // Producer backs this buffer with its own dmabuf here. Contain exceptions: an escaping throw
        // from this unmanaged callback would abort the process.
        try { self._onAddBuffer?.Invoke(buffer); } catch { /* leave buffer unbacked; negotiation continues */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRemoveBuffer(void* data, pw_buffer* buffer)
    {
        var self = (PipeWireStreamCore?)GCHandle.FromIntPtr((nint)data).Target;
        if (self is null) return;
        try { self._onRemoveBuffer?.Invoke(buffer); } catch { /* best-effort release */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnStateChanged(void* data, pw_stream_state old, pw_stream_state state, sbyte* error)
    {
        var self = (PipeWireStreamCore?)GCHandle.FromIntPtr((nint)data).Target;
        if (self is null) return;

        if (error is not null)
            self.LogStreamError(Marshal.PtrToStringUTF8((nint)error) ?? "(null)");

        self.LogStateChanged((PipeWireStreamState)(int)old, (PipeWireStreamState)(int)state);
        self._onState?.Invoke((PipeWireStreamState)(int)old, (PipeWireStreamState)(int)state);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnParamChanged(void* data, uint id, spa_pod* param)
    {
        if (param is null) return;
        var self = (PipeWireStreamCore?)GCHandle.FromIntPtr((nint)data).Target;
        if (self is null || self._disposed) return;

        // SPA_PARAM_PeerCapability (a newer PipeWire signal) would be the place to (re-)announce + activate a
        // dmabuf producer (video-src-fixate.c), but it is unavailable on the 1.6.6 runtime - the daemon does
        // not reliably deliver it, and re-announcing EnumFormat from this path crashed the daemon's set_param.
        // On 1.6.6 the producer instead offers a fixated modifier up front (it owns the surfaces, so it knows
        // the single modifier) and negotiates through the normal Format param flow below.
        self.LogParamChanged(id);

        if (self._onPeerConnected is not null && id == SpaParamPeerCapability)
        {
            try { self._onPeerConnected.Invoke(self); } catch { /* keep the loop thread alive */ }
            return;
        }

        if (id != Spa.SpaParam.Format) return;

        // This runs in an unmanaged callback - an escaping exception would abort the process,
        // so contain it. (A managed bug here must not take down the host application.)
        try
        {
            self._onFormat?.Invoke(param);
            // Format is set -> declare buffer/meta requirements via pw_stream_update_params
            // (documented point; the loop lock is already held in this callback).
            if (self._onPostFormat is not null)
                self._onPostFormat(self);
            else
                self.RequestHeaderMeta();
        }
        catch
        {
            // Swallow: negotiation continues with defaults rather than crashing the loop thread.
        }
    }

    /// <summary>The graph node id of this stream once connected (0 if not yet assigned).</summary>
    internal uint NodeId
    {
        get
        {
            if (_disposed || _stream is null)
            {
                return Native.PW_ID_ANY;
            }

            using (_ctx.Lock())
            {
                return Native.pw_stream_get_node_id(_stream);
            }
        }
    }

    /// <summary>
    /// Activates or deactivates the stream (<c>pw_stream_set_active</c>). A dmabuf DRIVER connects INACTIVE and
    /// is activated once its format + buffers are negotiated. Safe to call from the param_changed callback (the
    /// loop lock is held there); otherwise it takes the loop lock itself.
    /// </summary>
    internal void SetActive(bool active, bool lockHeld = false)
    {
        if (_disposed || _stream is null)
        {
            return;
        }

        if (lockHeld)
        {
            Native.pw_stream_set_active(_stream, active);
            return;
        }

        using (_ctx.Lock())
        {
            Native.pw_stream_set_active(_stream, active);
        }
    }

    /// <summary>
    /// Drives one processing cycle (<c>pw_stream_trigger_process</c>) - how a DRIVER producer paces output when
    /// no other node drives the graph clock. No-op if the stream is gone. Takes the loop lock unless held.
    /// </summary>
    internal void TriggerProcess(bool lockHeld = false)
    {
        if (_disposed || _stream is null)
        {
            return;
        }

        if (lockHeld)
        {
            Native.pw_stream_trigger_process(_stream);
            return;
        }

        using (_ctx.Lock())
        {
            Native.pw_stream_trigger_process(_stream);
        }
    }

    /// <summary>
    /// Sends up to two param pods via pw_stream_update_params. Call only from the param_changed
    /// callback (where the loop lock is held), e.g. from a <see cref="PostFormatHandler"/>.
    /// </summary>
    internal void RequestParams(ReadOnlySpan<byte> pod0, ReadOnlySpan<byte> pod1 = default)
    {
        fixed (byte* p0 = pod0)
        fixed (byte* p1 = pod1)
        {
            if (pod1.IsEmpty)
            {
                spa_pod* one = (spa_pod*)p0;
                Native.pw_stream_update_params(_stream, &one, 1);
            }
            else
            {
                spa_pod** arr = stackalloc spa_pod*[2];
                arr[0] = (spa_pod*)p0;
                arr[1] = (spa_pod*)p1;
                Native.pw_stream_update_params(_stream, arr, 2);
            }
        }
    }

    // - Diagnostics (source-generated, level-gated). Enable at Debug/Trace via the host's logger
    //   factory passed to PipeWireContext (e.g. the agent's --verbose). The stream name is the
    //   logger category, so each stream's lifecycle is filterable on its own. -

    [LoggerMessage(Level = LogLevel.Debug, Message = "state {Old} -> {New}")]
    private partial void LogStateChanged(PipeWireStreamState old, PipeWireStreamState @new);

    [LoggerMessage(Level = LogLevel.Error, Message = "stream error: {Error}")]
    private partial void LogStreamError(string error);

    [LoggerMessage(Level = LogLevel.Trace, Message = "param_changed id={Id}")]
    private partial void LogParamChanged(uint id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "first buffer: n_datas={Blocks} type={DataType} size={Size} maxsize={MaxSize}")]
    private partial void LogFirstBuffer(uint blocks, uint dataType, uint size, uint maxSize);

    [LoggerMessage(Level = LogLevel.Trace, Message = "process: no buffer dequeued (producer underrun or not yet started)")]
    private partial void LogDequeueEmpty();
}
