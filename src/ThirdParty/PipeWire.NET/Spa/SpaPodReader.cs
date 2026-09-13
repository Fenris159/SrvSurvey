using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PipeWire.NET.Spa;

// SPA POD wire format (docs.pipewire.org/page_spa_pod.html):
//
//   [uint32 size][uint32 type][payload size bytes][padding to 8-byte boundary]
//
// SpaPodReader walks a top-level pod, decoding object properties and primitive values.
// Mirror of SpaPodBuilder; pure C#, no native dispatch, NativeAOT-safe.

/// <summary>
/// Parses SPA POD wire-format bytes without allocation.
/// </summary>
/// <remarks>
/// Use one of the static <c>Read*</c> helpers to extract a single value, or call
/// <see cref="EnterObject"/> + <see cref="TryReadProperty(out uint, out SpaPodReader)"/> in a loop
/// to walk an object.
/// </remarks>
internal ref struct SpaPodReader
{
    private readonly ReadOnlySpan<byte> _buf;
    private int _pos;

    public SpaPodReader(ReadOnlySpan<byte> buffer)
    {
        _buf = buffer;
        _pos = 0;
    }

    /// <summary>Total size of the buffer in bytes.</summary>
    public int Length => _buf.Length;

    /// <summary>Current read offset.</summary>
    public int Position => _pos;

    /// <summary>True when no more bytes remain to read.</summary>
    public bool IsAtEnd => _pos >= _buf.Length;

    // - Object iteration -

    /// <summary>
    /// Reads the outermost pod header. Succeeds only when the pod is a SPA object;
    /// returns the object's type, id, and remaining body size.
    /// </summary>
    public bool EnterObject(out uint objectType, out uint objectId, out uint bodySize)
    {
        objectType = 0; objectId = 0; bodySize = 0;
        if (!TryReadHeader(out uint size, out uint type)) return false;
        if (type != SpaType.Object) return false;
        if (!TryReadU32(out objectType)) return false;
        if (!TryReadU32(out objectId))   return false;
        bodySize = size - 8; // size includes the object-type + object-id 8 bytes already consumed
        return true;
    }

    /// <summary>
    /// Reads the next property in the current object.
    /// </summary>
    /// <param name="key">SPA property key (e.g. <see cref="SpaFormatVideo.Format"/>).</param>
    /// <param name="value">Reader positioned at the property's value pod.</param>
    /// <returns><see langword="false"/> when no more properties remain in the current object body.</returns>
    public bool TryReadProperty(out uint key, out SpaPodReader value) =>
        TryReadProperty(out key, out _, out value);

    /// <summary>
    /// Reads the next property and also reports its <c>spa_pod_prop</c> flags (e.g.
    /// <see cref="SpaPodPropFlag.DontFixate"/> on an unfixated modifier choice).
    /// </summary>
    public bool TryReadProperty(out uint key, out uint flags, out SpaPodReader value)
    {
        key = 0;
        flags = 0;
        value = default;

        // spa_pod_prop header = [uint32 key][uint32 flags][value pod...]
        if (_pos + 8 > _buf.Length) return false;
        if (!TryReadU32(out key))    return false;
        if (!TryReadU32(out flags))  return false; // flags

        // The value pod sits at the current offset. Peek its size.
        if (_pos + 8 > _buf.Length) return false;
        uint vSize = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4));
        int valueLen = 8 + (int)vSize;          // pod header + body
        int valueLenAligned = (valueLen + 7) & ~7;

        if (_pos + valueLen > _buf.Length) return false;

        value = new SpaPodReader(_buf.Slice(_pos, valueLen));
        _pos += valueLenAligned;
        return true;
    }

    // - Primitive readers (assume reader is positioned at a single value pod) -

    public int ReadInt()
    {
        ReadHeaderOrThrow(SpaType.Int, expectedSize: 4);
        int v = MemoryMarshal.Read<int>(_buf.Slice(_pos, 4));
        _pos += 4; AlignTo8();
        return v;
    }

    public long ReadLong()
    {
        ReadHeaderOrThrow(SpaType.Long, expectedSize: 8);
        long v = MemoryMarshal.Read<long>(_buf.Slice(_pos, 8));
        _pos += 8;
        return v;
    }

    public float ReadFloat()
    {
        ReadHeaderOrThrow(SpaType.Float, expectedSize: 4);
        float v = MemoryMarshal.Read<float>(_buf.Slice(_pos, 4));
        _pos += 4; AlignTo8();
        return v;
    }

    public double ReadDouble()
    {
        ReadHeaderOrThrow(SpaType.Double, expectedSize: 8);
        double v = MemoryMarshal.Read<double>(_buf.Slice(_pos, 8));
        _pos += 8;
        return v;
    }

    public bool ReadBool()
    {
        ReadHeaderOrThrow(SpaType.Bool, expectedSize: 4);
        int v = MemoryMarshal.Read<int>(_buf.Slice(_pos, 4));
        _pos += 4; AlignTo8();
        return v != 0;
    }

    public uint ReadId()
    {
        ReadHeaderOrThrow(SpaType.Id, expectedSize: 4);
        uint v = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4));
        _pos += 4; AlignTo8();
        return v;
    }

    public (uint Width, uint Height) ReadRectangle()
    {
        ReadHeaderOrThrow(SpaType.Rectangle, expectedSize: 8);
        uint w = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4)); _pos += 4;
        uint h = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4)); _pos += 4;
        return (w, h);
    }

    public (uint Numerator, uint Denominator) ReadFraction()
    {
        ReadHeaderOrThrow(SpaType.Fraction, expectedSize: 8);
        uint n = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4)); _pos += 4;
        uint d = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4)); _pos += 4;
        return (n, d);
    }

    /// <summary>
    /// Reads a DRM format modifier value pod: either a plain <c>Long</c> or a Choice(Enum) of
    /// <c>Long</c>s. Returns the first (preferred) modifier in <paramref name="first"/> and how many
    /// the pod carried in <paramref name="count"/> - so a caller distinguishes a fixated single
    /// modifier (count == 1) from a still-open choice (count &gt; 1) it must fixate. Allocation-free:
    /// the values are scanned in place, none are materialised.
    /// </summary>
    /// <returns><see langword="false"/> when the pod is neither a Long nor a Long choice.</returns>
    public bool TryReadModifier(out long first, out int count)
    {
        first = 0;
        count = 0;
        int savedPos = _pos;
        if (!TryReadHeader(out uint size, out uint type)) { _pos = savedPos; return false; }

        if (type == SpaType.Long)
        {
            if (size < 8 || _pos + 8 > _buf.Length) { _pos = savedPos; return false; }
            first = MemoryMarshal.Read<long>(_buf.Slice(_pos, 8));
            _pos += 8;
            count = 1;
            return true;
        }

        if (type != SpaType.Choice) { _pos = savedPos; return false; }

        // spa_pod_choice_body: [choiceType][flags][childSize][childType], then the values.
        if (_pos + 16 > _buf.Length) { _pos = savedPos; return false; }
        if (!TryReadU32(out _))               { _pos = savedPos; return false; } // choiceType
        if (!TryReadU32(out _))               { _pos = savedPos; return false; } // flags
        if (!TryReadU32(out uint childSize))  { _pos = savedPos; return false; }
        if (!TryReadU32(out uint childType))  { _pos = savedPos; return false; }
        if (childType != SpaType.Long || childSize != 8) { _pos = savedPos; return false; }

        // The choice body length (size) covers the 16-byte header + N child values. We only need the
        // first value and the count, so read the first in place and skip the rest - nothing allocated.
        int valuesBytes = (int)size - 16;
        if (valuesBytes < 8 || _pos + valuesBytes > _buf.Length) { _pos = savedPos; return false; }
        first = MemoryMarshal.Read<long>(_buf.Slice(_pos, 8));
        count = valuesBytes / 8;
        _pos += valuesBytes;
        return true;
    }

    /// <summary>
    /// If the current value pod is a Choice, advances past the choice header and returns
    /// a reader positioned at the first concrete value. Useful when format negotiation
    /// returns Choice-wrapped values.
    /// </summary>
    public bool TryUnwrapChoice(out SpaPodReader first)
    {
        first = default;
        // Peek the header non-destructively: a plain (non-choice) value must be left
        // untouched so the caller can fall back to ReadId()/ReadRectangle()/etc.
        int savedPos = _pos;
        if (!TryReadHeader(out uint size, out uint type) || type != SpaType.Choice)
        {
            _pos = savedPos;
            return false;
        }

        // Skip 4xu32 choice header (choiceType, flags, childSize, childType)
        if (_pos + 16 > _buf.Length) return false;
        if (!TryReadU32(out _)) return false; // choiceType
        if (!TryReadU32(out _)) return false; // flags
        if (!TryReadU32(out uint childSize)) return false;
        if (!TryReadU32(out uint childType)) return false;

        // Rebuild a synthetic pod header so the returned reader can call ReadXxx directly.
        // Allocate a tiny stack span and pre-populate [childSize][childType][body].
        int bodyLen = (int)childSize;
        if (_pos + bodyLen > _buf.Length) return false;

        ReadOnlySpan<byte> body = _buf.Slice(_pos, bodyLen);
        // Caller cannot mutate ReadOnlySpan; we re-emit a new pod into a buffer the caller owns.
        // For now expose the body via a child-only reader and let the caller call typed ReadXxx
        // - but ReadXxx expects a full pod header. The simpler contract: return a reader whose
        // ReadXxx assumes a "headerless" body and pass childType so the caller knows.
        first = new SpaPodReader(body) { _synthesizedType = childType };
        return true;
    }

    private uint _synthesizedType;

    // - Header parsing -

    private bool TryReadHeader(out uint size, out uint type)
    {
        size = 0; type = 0;
        if (_pos + 8 > _buf.Length) return false;
        size = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4));
        type = MemoryMarshal.Read<uint>(_buf.Slice(_pos + 4, 4));
        _pos += 8;
        return true;
    }

    private void ReadHeaderOrThrow(uint expectedType, uint expectedSize)
    {
        // When TryUnwrapChoice synthesized this reader, there is no embedded header
        // - the type is carried out-of-band via _synthesizedType.
        if (_synthesizedType != 0)
        {
            if (_synthesizedType != expectedType)
                throw new InvalidOperationException(
                    $"SPA pod type mismatch: expected {expectedType}, got synthesized {_synthesizedType}");
            return;
        }

        if (!TryReadHeader(out uint size, out uint type))
            throw new InvalidOperationException("Truncated SPA pod.");
        if (type != expectedType)
            throw new InvalidOperationException(
                $"SPA pod type mismatch: expected {expectedType}, got {type}");
        if (size != expectedSize)
            throw new InvalidOperationException(
                $"SPA pod size mismatch: expected {expectedSize}, got {size}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadU32(out uint value)
    {
        if (_pos + 4 > _buf.Length) { value = 0; return false; }
        value = MemoryMarshal.Read<uint>(_buf.Slice(_pos, 4));
        _pos += 4;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AlignTo8()
    {
        int rem = _pos & 7;
        if (rem != 0) _pos += 8 - rem;
    }
}
