using System.Runtime.Versioning;
using PipeWire.NET.Generated;
using PipeWire.NET.Spa;

namespace PipeWire.NET;

/// <summary>
/// Publishes audio samples TO PipeWire (virtual source / playback). PipeWire pulls
/// samples by invoking <see cref="FillSamples"/>; write PCM into the supplied span.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class PipeWireAudioOutput : IAsyncDisposable
{
    /// <summary>Signature for <see cref="FillSamples"/>. Return the number of bytes written (0 = silence).</summary>
    public delegate int FillSamplesHandler(
        PipeWireAudioOutput sender, Span<byte> samples, int sampleRate, int channels, AudioSampleFormat format);

    /// <summary>Invoked on the loop thread when a buffer is ready to fill.</summary>
    public event FillSamplesHandler? FillSamples;

    /// <summary>Raised when the connection state changes.</summary>
    public event Action<PipeWireAudioOutput, PipeWireStreamState, PipeWireStreamState>? StateChanged;

    private readonly PipeWireContext _ctx;
    private readonly string _name;
    private readonly int _sampleRate, _channels;
    private readonly AudioSampleFormat _format;
    private PipeWireStreamCore? _core;

    /// <param name="context">A started <see cref="PipeWireContext"/>.</param>
    /// <param name="nodeName">Name visible to consumers.</param>
    /// <param name="sampleRate">Sample rate to publish (Hz).</param>
    /// <param name="channels">Channel count.</param>
    /// <param name="format">Sample format.</param>
    public PipeWireAudioOutput(PipeWireContext context, string nodeName,
        int sampleRate = 48000, int channels = 2, AudioSampleFormat format = AudioSampleFormat.F32Le)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(nodeName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _ctx = context; _name = nodeName;
        _sampleRate = sampleRate; _channels = channels; _format = format;
    }

    /// <summary>Starts publishing.</summary>
    public unsafe void Connect()
    {
        if (_core is not null) throw new InvalidOperationException("Already connected.");

        var props = new StreamProperties(StreamMediaType.Audio, StreamCategory.Playback)
            .WithRole("Music")
            .WithNodeName(_name);

        _core = new PipeWireStreamCore(_ctx, props, _name, OnBuffer, OnState);

        Span<byte> pod = stackalloc byte[256];
        int len = SpaFormat.WriteAudioFormat(pod, _format, _sampleRate, _channels);
        _core.Connect(spa_direction.SPA_DIRECTION_OUTPUT, Native.PW_ID_ANY,
            pw_stream_flags.PW_STREAM_FLAG_AUTOCONNECT | pw_stream_flags.PW_STREAM_FLAG_MAP_BUFFERS,
            pod[..len]);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _core?.DisposeAsync() ?? ValueTask.CompletedTask;

    private unsafe void OnBuffer(spa_data* d, pw_buffer* buf, in PipeWireStreamCore.StreamClock clock)
    {
        if (d->data is null || d->chunk is null) return;

        int max = (int)d->maxsize;
        var samples = new Span<byte>(d->data, max);
        int written = FillSamples?.Invoke(this, samples, _sampleRate, _channels, _format) ?? 0;
        written = Math.Clamp(written, 0, max);

        d->chunk->offset = 0;
        d->chunk->stride = _format.BytesPerSample() * _channels;
        d->chunk->size   = (uint)written;
    }

    private void OnState(PipeWireStreamState oldState, PipeWireStreamState newState) =>
        StateChanged?.Invoke(this, oldState, newState);
}
