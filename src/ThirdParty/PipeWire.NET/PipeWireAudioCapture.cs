using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PipeWire.NET.Generated;
using PipeWire.NET.Spa;

namespace PipeWire.NET;

/// <summary>
/// Receives audio samples from a PipeWire source (microphone, virtual source, or the
/// monitor of an output sink).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class PipeWireAudioCapture : IAsyncDisposable
{
    /// <summary>Wildcard node id - let PipeWire auto-select a source.</summary>
    public const uint AnyNode = Native.PW_ID_ANY;

    /// <summary>Signature for <see cref="FrameReady"/>.</summary>
    public delegate void FrameReadyHandler(PipeWireAudioCapture sender, AudioFrame frame);

    /// <summary>Raised on the loop thread when an audio chunk is available. Do not cache the frame.</summary>
    public event FrameReadyHandler? FrameReady;

    /// <summary>Raised when the connection state changes.</summary>
    public event Action<PipeWireAudioCapture, PipeWireStreamState, PipeWireStreamState>? StateChanged;

    private readonly PipeWireContext _ctx;
    private readonly string _name;
    private readonly ILogger _logger;
    private PipeWireStreamCore? _core;
    private ulong _sequence;
    private SpaFormat.AudioFormatInfo _fmt = new(AudioSampleFormat.F32Le, 48000, 2);

    /// <param name="context">A started <see cref="PipeWireContext"/>.</param>
    /// <param name="name">node.name advertised in the graph.</param>
    public PipeWireAudioCapture(PipeWireContext context, string name = "PipeWire.NET.AudioCapture")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _ctx = context; _name = name;
        _logger = context.LoggerFactory.CreateLogger($"PipeWire.NET.{name}");
    }

    /// <summary>Connects to an audio source.</summary>
    /// <param name="targetNodeId">Source node id, or <see cref="AnyNode"/>.</param>
    /// <param name="sampleRate">Preferred sample rate (Hz).</param>
    /// <param name="channels">Preferred channel count.</param>
    /// <param name="format">Preferred sample format.</param>
    /// <param name="targetObjectName">
    /// Optional <c>target.object</c> - bind to a specific node by name/serial regardless of
    /// the session manager's default-device routing.
    /// </param>
    public unsafe void Connect(uint targetNodeId = AnyNode,
        int sampleRate = 48000, int channels = 2, AudioSampleFormat format = AudioSampleFormat.F32Le,
        string? targetObjectName = null)
    {
        if (_core is not null) throw new InvalidOperationException("Already connected.");
        _fmt = new SpaFormat.AudioFormatInfo(format, sampleRate, channels);

        var props = new StreamProperties(StreamMediaType.Audio, StreamCategory.Capture)
            .WithRole("Music")
            .WithNodeName(_name);
        if (targetObjectName is not null) props.WithTargetObject(targetObjectName);

        _core = new PipeWireStreamCore(_ctx, props, _name, OnBuffer, OnState, OnFormat);

        Span<byte> pod = stackalloc byte[256];
        int len = SpaFormat.WriteAudioFormat(pod, format, sampleRate, channels);
        _core.Connect(spa_direction.SPA_DIRECTION_INPUT, targetNodeId,
            pw_stream_flags.PW_STREAM_FLAG_AUTOCONNECT | pw_stream_flags.PW_STREAM_FLAG_MAP_BUFFERS,
            pod[..len]);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _core?.DisposeAsync() ?? ValueTask.CompletedTask;

    private unsafe void OnBuffer(spa_data* d, pw_buffer* buf, in PipeWireStreamCore.StreamClock clock)
    {
        if (d->data is null || d->chunk is null) return;
        uint offset = d->chunk->offset;
        uint size   = d->chunk->size;
        if (size == 0) return;

        var samples = new ReadOnlySpan<byte>((byte*)d->data + offset, (int)size);
        var frame = new AudioFrame(samples, _fmt.SampleRate, _fmt.Channels, _fmt.Format, ++_sequence,
            presentationTimeNs: SpaFormat.FindPresentationTimeNs(buf->buffer),
            captureClockNs: clock.CaptureClockNs,
            mediaClockNs: clock.MediaClockNs,
            delayNs: clock.DelayNs);
        FrameReady?.Invoke(this, frame);
    }

    private void OnState(PipeWireStreamState oldState, PipeWireStreamState newState) =>
        StateChanged?.Invoke(this, oldState, newState);

    private unsafe void OnFormat(spa_pod* param)
    {
        _fmt = SpaFormat.ParseAudioFormat(param, _fmt);
        LogNegotiatedFormat(_fmt.Format, _fmt.SampleRate, _fmt.Channels);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "negotiated audio format {Format} {SampleRate}Hz {Channels}ch")]
    private partial void LogNegotiatedFormat(AudioSampleFormat format, int sampleRate, int channels);
}
