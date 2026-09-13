namespace PipeWire.NET;

/// <summary>
/// Strongly-typed view of a PipeWire node's <c>media.class</c> property.
/// </summary>
/// <remarks>
/// PipeWire reports media class as a free-form string. This enum covers the well-known
/// values; anything unrecognized maps to <see cref="Other"/> and the raw string remains
/// available via <see cref="PipeWireSource.MediaClass"/>.
/// </remarks>
public enum PipeWireMediaClass
{
    /// <summary>An unrecognized or absent media class. Inspect the raw string.</summary>
    Other,

    /// <summary><c>Video/Source</c> - a hardware camera or capture device.</summary>
    VideoSource,

    /// <summary><c>Video/Source/Virtual</c> - a screen-capture portal or virtual camera node.</summary>
    VideoSourceVirtual,

    /// <summary><c>Stream/Output/Video</c> - an application publishing video (acts as a source to consumers).</summary>
    VideoStreamOutput,

    /// <summary><c>Video/Sink</c> - a video output device.</summary>
    VideoSink,

    /// <summary><c>Audio/Source</c> - a microphone or audio capture device.</summary>
    AudioSource,

    /// <summary><c>Audio/Source/Virtual</c> - a virtual audio source.</summary>
    AudioSourceVirtual,

    /// <summary><c>Stream/Output/Audio</c> - an application publishing audio.</summary>
    AudioStreamOutput,

    /// <summary><c>Audio/Sink</c> - a speaker or audio output device. Its monitor is a capturable source.</summary>
    AudioSink,
}

/// <summary>Parsing helpers for <see cref="PipeWireMediaClass"/>.</summary>
public static class PipeWireMediaClassExtensions
{
    /// <summary>Maps a raw PipeWire <c>media.class</c> string to <see cref="PipeWireMediaClass"/>.</summary>
    public static PipeWireMediaClass ParseMediaClass(string? raw) => raw switch
    {
        "Video/Source"          => PipeWireMediaClass.VideoSource,
        "Video/Source/Virtual"  => PipeWireMediaClass.VideoSourceVirtual,
        "Stream/Output/Video"   => PipeWireMediaClass.VideoStreamOutput,
        "Video/Sink"            => PipeWireMediaClass.VideoSink,
        "Audio/Source"          => PipeWireMediaClass.AudioSource,
        "Audio/Source/Virtual"  => PipeWireMediaClass.AudioSourceVirtual,
        "Stream/Output/Audio"   => PipeWireMediaClass.AudioStreamOutput,
        "Audio/Sink"            => PipeWireMediaClass.AudioSink,
        _                       => PipeWireMediaClass.Other,
    };

    /// <summary><see langword="true"/> for any class that produces video frames a consumer can capture.</summary>
    public static bool IsVideo(this PipeWireMediaClass mc) => mc is
        PipeWireMediaClass.VideoSource or
        PipeWireMediaClass.VideoSourceVirtual or
        PipeWireMediaClass.VideoStreamOutput;

    /// <summary><see langword="true"/> for any class that produces audio a consumer can capture.</summary>
    public static bool IsAudio(this PipeWireMediaClass mc) => mc is
        PipeWireMediaClass.AudioSource or
        PipeWireMediaClass.AudioSourceVirtual or
        PipeWireMediaClass.AudioStreamOutput or
        PipeWireMediaClass.AudioSink;   // sink's monitor is capturable
}
