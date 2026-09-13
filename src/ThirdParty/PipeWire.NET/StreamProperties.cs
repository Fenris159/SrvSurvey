using System.Runtime.Versioning;
using System.Text;
using PipeWire.NET.Generated;

namespace PipeWire.NET;

/// <summary>The kind of media a stream carries.</summary>
public enum StreamMediaType
{
    /// <summary><c>media.type=Video</c>.</summary>
    Video,
    /// <summary><c>media.type=Audio</c>.</summary>
    Audio,
}

/// <summary>How a stream participates in the graph (maps to <c>media.category</c>).</summary>
public enum StreamCategory
{
    /// <summary>Receiving data from a source (<c>media.category=Capture</c>).</summary>
    Capture,
    /// <summary>Sending data to a sink / publishing as a source (<c>media.category=Playback</c>).</summary>
    Playback,
}

/// <summary>
/// Type-safe builder for the PipeWire property dictionary passed to <c>pw_stream_new</c>.
/// Replaces hand-formatted <c>media.type=Video media.category=Capture ...</c> strings.
/// </summary>
/// <remarks>
/// Construct with the required keys, optionally chain <see cref="WithRole"/> /
/// <see cref="WithTargetObject"/> / <see cref="With"/>, then the stream classes call
/// <see cref="ToNativeProperties"/> internally.
/// </remarks>
public sealed class StreamProperties
{
    private readonly Dictionary<string, string> _props = new(StringComparer.Ordinal);

    /// <param name="mediaType">Video or audio.</param>
    /// <param name="category">Capture or playback.</param>
    public StreamProperties(StreamMediaType mediaType, StreamCategory category)
    {
        _props["media.type"]     = mediaType == StreamMediaType.Video ? "Video" : "Audio";
        _props["media.category"] = category == StreamCategory.Capture ? "Capture" : "Playback";
    }

    /// <summary>Sets <c>media.role</c> (e.g. "Camera", "Music", "Screen").</summary>
    public StreamProperties WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrEmpty(role);
        _props["media.role"] = role;
        return this;
    }

    /// <summary>Sets <c>target.object</c> to bind the stream to a specific node by serial/name.</summary>
    public StreamProperties WithTargetObject(string targetObject)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetObject);
        _props["target.object"] = targetObject;
        return this;
    }

    /// <summary>Sets <c>node.name</c> - the stable name this stream advertises in the graph.</summary>
    public StreamProperties WithNodeName(string nodeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodeName);
        _props["node.name"] = nodeName;
        return this;
    }

    /// <summary>Sets <c>node.description</c> - the human-readable name shown to users.</summary>
    public StreamProperties WithNodeDescription(string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        _props["node.description"] = description;
        return this;
    }

    /// <summary>Sets an arbitrary PipeWire property key/value.</summary>
    public StreamProperties With(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        _props[key] = value;
        return this;
    }

    /// <summary>The property key/value pairs accumulated so far.</summary>
    public IReadOnlyDictionary<string, string> Values => _props;

    /// <summary>
    /// Builds a native <c>pw_properties*</c>. Ownership transfers to <c>pw_stream_new</c>
    /// (which consumes it); callers do not free the result.
    /// </summary>
    [SupportedOSPlatform("linux")]
    internal unsafe pw_properties* ToNativeProperties()
    {
        // pw_properties_new_string parses a single "key=value key2=value2" string.
        // We build that string and hand it over UTF-8 encoded.
        var sb = new StringBuilder();
        bool first = true;
        foreach ((string key, string value) in _props)
        {
            if (!first) sb.Append(' ');
            first = false;
            // PipeWire's parser treats whitespace as a separator; quote values with spaces.
            if (value.Contains(' ', StringComparison.Ordinal))
                sb.Append(key).Append("=\"").Append(value).Append('"');
            else
                sb.Append(key).Append('=').Append(value);
        }

        ReadOnlySpan<byte> utf8 = Encoding.UTF8.GetBytes(sb.Append('\0').ToString());
        fixed (byte* p = utf8)
            return Native.pw_properties_new_string((sbyte*)p);
    }
}
