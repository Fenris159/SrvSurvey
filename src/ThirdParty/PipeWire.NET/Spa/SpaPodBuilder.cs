using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PipeWire.NET.Spa;

// SPA POD wire format (docs.pipewire.org/page_spa_pod.html):
//
//   [uint32 size][uint32 type][payload...][0-padding to 8-byte boundary]
//
// The SPA pod_builder C API is varargs macros that ClangSharp cannot translate.
// This builder reimplements the wire format in pure C# writing to a caller-supplied
// Span<byte>. Design inspired by pipewire-rs libspa::pod::serialize.
//
// SpaPodBuilder is a `ref struct` and every mutator returns `void` ON PURPOSE: a
// fluent-chaining API (`b.Add(...).Add(...)`) on a mutable struct would mutate a
// returned COPY, silently corrupting the output. void returns make that a compile
// error - always call methods on the same instance:
//
//   var b = new SpaPodBuilder(stackalloc byte[512]);
//   b.PushObject(SpaType.ObjectFormat, SpaParam.EnumFormat);
//   b.AddId(SpaFormatVideo.MediaType, SpaMediaType.Video);
//   b.AddChoiceEnum(SpaFormatVideo.Format, SpaVideoFormat.BGRA, SpaVideoFormat.RGBA);
//   ReadOnlySpan<byte> pod = b.GetPod();

/// <summary>
/// Writes SPA POD objects into a <see cref="Span{T}"/> without heap allocation.
/// Every mutator returns <see langword="void"/>; call them on the same instance
/// (this is a <see langword="ref struct"/> - chaining would mutate a copy).
/// </summary>
internal ref struct SpaPodBuilder
{
    private readonly Span<byte> _buf;
    private int _pos;

    // Stack of open object/array offsets (for back-patching sizes).
    // Maximum nesting depth of 8 covers all real-world PipeWire format objects.
    private OffsetStack _stack;
    private int _depth;

    [InlineArray(8)]
    private struct OffsetStack { private int _e; }

    public SpaPodBuilder(Span<byte> buffer)
    {
        _buf   = buffer;
        _pos   = 0;
        _depth = 0;
    }

    // - Bare primitive values (rarely used directly; prefer the keyed overloads) -

    public void AddInt(int value)       => WritePod(SpaType.Int,    value);
    public void AddFloat(float value)   => WritePod(SpaType.Float,  value);
    public void AddDouble(double value) => WritePod(SpaType.Double, value);
    public void AddBool(bool value)     => WritePod(SpaType.Bool,   value ? 1 : 0);
    public void AddId(uint id)          => WritePod(SpaType.Id,     (int)id);
    public void AddLong(long value)     => WritePodLong(value);

    // - Keyed properties (inside an Object) -

    public void AddId(uint key, uint value)            { WritePropHeader(key); AddId(value); }
    public void AddInt(uint key, int value)            { WritePropHeader(key); AddInt(value); }
    public void AddLong(uint key, long value)          { WritePropHeader(key); AddLong(value); }
    public void AddLong(uint key, long value, uint propFlags) { WritePropHeader(key, propFlags); AddLong(value); }
    public void AddFraction(uint key, uint n, uint d)  { WritePropHeader(key); WriteFraction(n, d); }
    public void AddRectangle(uint key, uint w, uint h) { WritePropHeader(key); WriteRectangle(w, h); }

    // - Choice: Enum over Long (DRM format modifiers) -

    /// <summary>
    /// Choice(Enum) over <c>Long</c> values - used to offer DRM format modifiers. The first
    /// modifier is the preferred/default; the rest are alternatives. Pass
    /// <see cref="SpaPodPropFlag.Mandatory"/> | <see cref="SpaPodPropFlag.DontFixate"/> as
    /// <paramref name="propFlags"/> on the first negotiation pass so the producer narrows the set
    /// to what it supports without fixating, then re-offer a single modifier to fixate.
    /// </summary>
    public void AddChoiceEnumLong(uint key, ReadOnlySpan<long> values, uint propFlags = 0)
    {
        WritePropHeader(key, propFlags);
        int start = _pos;
        WriteU32(0);                // pod size - back-patched
        WriteU32(SpaType.Choice);
        WriteU32(SpaChoiceType.Enum);
        WriteU32(0);                // choice flags
        WriteU32(8);                // child.size - Long is 8 bytes
        WriteU32(SpaType.Long);     // child.type
        // SPA Choice Enum layout is { default, alt0, alt1, ... }: the FIRST child is the default/preferred
        // value and the rest are the selectable alternatives, so the default must also appear among the
        // alternatives. Emit values[0] once as the default and then every value. A single modifier written
        // once would be a default with NO alternatives - the consumer then has nothing to select and dmabuf
        // negotiation fails ("no more input formats"). Matches pipewire's video-src-fixate.c, which writes the
        // first modifier twice.
        if (!values.IsEmpty)
        {
            MemoryMarshal.Write(_buf.Slice(_pos, 8), values[0]);
            _pos += 8;
        }

        foreach (long v in values)
        {
            MemoryMarshal.Write(_buf.Slice(_pos, 8), v);
            _pos += 8;
        }

        Align8();
        PatchSize(start);
    }

    // - Choice: Enum (list of allowed Id values) -

    public void AddChoiceEnum(uint key, params ReadOnlySpan<uint> values)
    {
        // SPA Choice wire format (spa/pod/pod.h):
        //   [size][type=Choice]                            <- pod header
        //   [choiceType][flags][child.size][child.type]    <- spa_pod_choice_body
        //   [value0][value1][...]                          <- raw values, child.size each
        WritePropHeader(key);
        int start = _pos;
        WriteU32(0);                // pod size - back-patched
        WriteU32(SpaType.Choice);
        WriteU32(SpaChoiceType.Enum);
        WriteU32(0);                // flags
        WriteU32(4);                // child.size - Id is 4 bytes
        WriteU32(SpaType.Id);       // child.type
        foreach (uint v in values)
            WriteU32(v);
        Align8();
        PatchSize(start);
    }

    /// <summary>Choice(Range) over Int - default, min, max.</summary>
    public void AddChoiceRangeInt(uint key, int def, int min, int max)
    {
        WritePropHeader(key);
        int start = _pos;
        WriteU32(0);
        WriteU32(SpaType.Choice);
        WriteU32(SpaChoiceType.Range);
        WriteU32(0);                // flags
        WriteU32(4);                // child.size - Int = 4
        WriteU32(SpaType.Int);
        WriteU32((uint)def); WriteU32((uint)min); WriteU32((uint)max);
        Align8();
        PatchSize(start);
    }

    /// <summary>Choice(Flags) over Int - a single bitmask value (e.g. allowed buffer data types).</summary>
    public void AddChoiceFlagsInt(uint key, int flags)
    {
        WritePropHeader(key);
        int start = _pos;
        WriteU32(0);
        WriteU32(SpaType.Choice);
        WriteU32(SpaChoiceType.Flags);
        WriteU32(0);                // flags
        WriteU32(4);                // child.size - Int = 4
        WriteU32(SpaType.Int);
        WriteU32((uint)flags);
        Align8();
        PatchSize(start);
    }

    // - Choice: Range (default, min, max) -

    public void AddChoiceRangeRectangle(uint key,
        uint defaultW, uint defaultH,
        uint minW,     uint minH,
        uint maxW,     uint maxH)
    {
        WritePropHeader(key);
        int start = _pos;
        WriteU32(0);
        WriteU32(SpaType.Choice);
        WriteU32(SpaChoiceType.Range);
        WriteU32(0);                // flags
        WriteU32(8);                // child.size - Rectangle = 8 bytes
        WriteU32(SpaType.Rectangle);
        WriteU32(defaultW); WriteU32(defaultH);
        WriteU32(minW);     WriteU32(minH);
        WriteU32(maxW);     WriteU32(maxH);
        Align8();
        PatchSize(start);
    }

    public void AddChoiceRangeFraction(uint key,
        uint defaultNum, uint defaultDenom,
        uint minNum,     uint minDenom,
        uint maxNum,     uint maxDenom)
    {
        WritePropHeader(key);
        int start = _pos;
        WriteU32(0);
        WriteU32(SpaType.Choice);
        WriteU32(SpaChoiceType.Range);
        WriteU32(0);                // flags
        WriteU32(8);                // child.size - Fraction = 8 bytes
        WriteU32(SpaType.Fraction);
        WriteU32(defaultNum); WriteU32(defaultDenom);
        WriteU32(minNum);     WriteU32(minDenom);
        WriteU32(maxNum);     WriteU32(maxDenom);
        Align8();
        PatchSize(start);
    }

    // - Object -

    public void PushObject(uint objectType, uint paramId)
    {
        Push(_pos);
        WriteU32(0);                // size placeholder - back-patched in Pop
        WriteU32(SpaType.Object);
        WriteU32(objectType);
        WriteU32(paramId);
    }

    /// <summary>Closes the innermost open object, back-patching its size.</summary>
    public void Pop()
    {
        int start = _stack[--_depth];
        PatchSize(start);
        Align8();
    }

    /// <summary>Closes any still-open object and returns the complete POD bytes.</summary>
    public ReadOnlySpan<byte> GetPod()
    {
        while (_depth > 0) Pop();
        return _buf[.._pos];
    }

    // - Private helpers -

    private void WritePod<T>(uint type, T value) where T : struct
    {
        int size = Unsafe.SizeOf<T>();
        WriteU32((uint)size);
        WriteU32(type);
        MemoryMarshal.Write(_buf.Slice(_pos, size), value);
        _pos += size;
        Align8();
    }

    private void WritePodLong(long value)
    {
        WriteU32(8);
        WriteU32(SpaType.Long);
        MemoryMarshal.Write(_buf.Slice(_pos, 8), value);
        _pos += 8;                  // already 8-byte aligned
    }

    private void WriteFraction(uint num, uint denom)
    {
        WriteU32(8);                // sizeof(spa_fraction) = 2xuint32
        WriteU32(SpaType.Fraction);
        WriteU32(num);
        WriteU32(denom);            // already aligned
    }

    private void WriteRectangle(uint width, uint height)
    {
        WriteU32(8);                // sizeof(spa_rectangle) = 2xuint32
        WriteU32(SpaType.Rectangle);
        WriteU32(width);
        WriteU32(height);           // already aligned
    }

    private void WritePropHeader(uint key, uint flags = 0)
    {
        // spa_pod_prop header: key (uint32) + flags (uint32), then the value pod.
        WriteU32(key);
        WriteU32(flags);
    }

    /// <summary>Back-patches the 4-byte size field of a pod whose header starts at <paramref name="start"/>.</summary>
    private void PatchSize(int start)
    {
        uint bodySize = (uint)(_pos - start - 8);
        MemoryMarshal.Write(_buf.Slice(start, 4), bodySize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteU32(uint value)
    {
        MemoryMarshal.Write(_buf.Slice(_pos, 4), value);
        _pos += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Align8()
    {
        int rem = _pos & 7;
        if (rem != 0) _pos += 8 - rem;
    }

    private void Push(int offset)
    {
        if (_depth >= 8) ThrowNestingOverflow();
        _stack[_depth++] = offset;
    }

    private static void ThrowNestingOverflow() =>
        throw new InvalidOperationException("SpaPodBuilder: nesting depth exceeds 8.");
}
