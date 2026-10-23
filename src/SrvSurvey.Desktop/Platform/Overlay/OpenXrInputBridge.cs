using System.IO.MemoryMappedFiles;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Owns the input control heartbeat and reads the native layer's bounded pointer-event stream.</summary>
public sealed class OpenXrInputBridge : IDisposable
{
    public const string FileSuffix = ".input";
    public const int Capacity = 4096;
    public const int EventCapacity = 64;
    public const int EventOffset = 128;
    public const int EventBytes = 32;
    public const uint Magic = 0x49565253;
    public const uint Version = 1;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private readonly Func<long> clock;
    private readonly ulong generation = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray());
    private uint controlSequence;
    private uint readCount;
    private bool enabled;

    public OpenXrInputBridge(string path, Func<long> clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        using var file = new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete
        );
        if (file.Length != Capacity)
        {
            file.SetLength(Capacity);
        }
        mapping = MemoryMappedFile.CreateFromFile(
            file,
            null,
            Capacity,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true
        );
        try
        {
            view = mapping.CreateViewAccessor();
            controlSequence = view.ReadUInt32(8) & ~1u;
            readCount = view.ReadUInt32(52);
            Refresh();
        }
        catch
        {
            view?.Dispose();
            mapping.Dispose();
            throw;
        }
    }

    public bool IsReady => State == 1;
    public int LastError => view.ReadInt32(44);

    private uint State
    {
        get
        {
            long heartbeat = view.ReadInt64(32);
            Thread.MemoryBarrier();
            long age = clock() - heartbeat;
            return heartbeat > 0 && age is >= 0 and <= 3000 && view.ReadUInt64(56) == generation
                ? view.ReadUInt32(40)
                : 0;
        }
    }

    public void SetEnabled(bool value)
    {
        enabled = value;
        readCount = view.ReadUInt32(52);
        Refresh();
    }

    public void Refresh() => WriteControl(clock());

    private void WriteControl(long timestamp)
    {
        view.Write(8, ++controlSequence);
        Thread.MemoryBarrier();
        view.Write(0, Magic);
        view.Write(4, Version);
        view.Write(12, enabled ? 1u : 0u);
        view.Write(16, timestamp);
        view.Write(24, generation);
        Thread.MemoryBarrier();
        view.Write(8, ++controlSequence);
    }

    public IReadOnlyList<OpenXrPointerEvent> ReadEvents()
    {
        Refresh();
        if (!enabled)
        {
            return [];
        }
        if (!IsReady)
        {
            readCount = view.ReadUInt32(52);
            return [ResetEvent()];
        }
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint before = view.ReadUInt32(48);
            if ((before & 1) != 0)
            {
                continue;
            }
            Thread.MemoryBarrier();
            uint count = view.ReadUInt32(52);
            uint pending = unchecked(count - readCount);
            if (pending > EventCapacity)
            {
                readCount = count;
                return [ResetEvent()];
            }
            var events = new List<OpenXrPointerEvent>((int)pending);
            bool valid = true;
            for (uint index = 0; index < pending; index++)
            {
                int offset = EventOffset + (int)(unchecked(readCount + index) % EventCapacity) * EventBytes;
                OpenXrPointerEvent item = ReadEvent(offset);
                valid &= IsValidEvent(item);
                events.Add(item);
            }
            Thread.MemoryBarrier();
            if (before != view.ReadUInt32(48))
            {
                continue;
            }
            readCount = count;
            return valid ? events : [ResetEvent()];
        }
        readCount = view.ReadUInt32(52);
        return [ResetEvent()];
    }

    private OpenXrPointerEvent ReadEvent(int offset) =>
        new(
            view.ReadUInt64(offset),
            (VrOverlayPointerEventKind)view.ReadUInt32(offset + 8),
            view.ReadSingle(offset + 16),
            view.ReadSingle(offset + 20),
            view.ReadSingle(offset + 24),
            view.ReadSingle(offset + 28)
        );

    private static bool IsValidEvent(OpenXrPointerEvent item) =>
        Enum.IsDefined(item.Kind)
        && IsBounded(item.X, 10)
        && IsBounded(item.Y, 10)
        && IsBounded(item.WheelX, 8)
        && IsBounded(item.WheelY, 8);

    private static bool IsBounded(float value, float limit) => float.IsFinite(value) && Math.Abs(value) <= limit;

    private static OpenXrPointerEvent ResetEvent() => new(0, VrOverlayPointerEventKind.Reset, 0, 0, 0, 0);

    public void Stop()
    {
        enabled = false;
        WriteControl(0);
    }

    public void Dispose()
    {
        Stop();
        view.Dispose();
        mapping.Dispose();
    }
}

public sealed record OpenXrPointerEvent(
    ulong Id,
    VrOverlayPointerEventKind Kind,
    float X,
    float Y,
    float WheelX,
    float WheelY
);
