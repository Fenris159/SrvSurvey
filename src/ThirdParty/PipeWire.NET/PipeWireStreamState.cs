namespace PipeWire.NET;

/// <summary>Connection state of a PipeWire stream (mirrors <c>enum pw_stream_state</c>).</summary>
public enum PipeWireStreamState
{
    /// <summary>An error occurred; the stream is not usable.</summary>
    Error       = -1,
    /// <summary>The stream is not yet connected to a node.</summary>
    Unconnected =  0,
    /// <summary>The stream is negotiating with a node.</summary>
    Connecting  =  1,
    /// <summary>The stream is connected but not delivering data.</summary>
    Paused      =  2,
    /// <summary>The stream is actively delivering data.</summary>
    Streaming   =  3,
}
