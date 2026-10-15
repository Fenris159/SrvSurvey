using System.IO.MemoryMappedFiles;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Owns the single-writer frame mailbox; the game writes health into a separate header region.</summary>
public sealed class OpenXrFrameBridge : IDisposable
{
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private readonly FileStream writerLease;
    private readonly Func<long> clock;
    private uint sequence;

    public OpenXrFrameBridge(string path, Func<long>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        FileStream lease = new(
            Path.ChangeExtension(path, "writer"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        try
        {
            using var file = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete
            );
            if (file.Length != OpenXrFramePacket.Capacity)
            {
                file.SetLength(OpenXrFramePacket.Capacity);
            }
            mapping = MemoryMappedFile.CreateFromFile(
                file,
                null,
                OpenXrFramePacket.Capacity,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true
            );
            view = mapping.CreateViewAccessor();
            writerLease = lease;
            sequence = view.ReadUInt32(8) & ~1u;
            Stop();
        }
        catch
        {
            view?.Dispose();
            mapping?.Dispose();
            lease.Dispose();
            throw;
        }
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SrvSurvey",
            "OpenXR",
            "frames.bin"
        );

    public OpenXrLayerState ReadState()
    {
        long heartbeat = view.ReadInt64(32);
        long age = clock() - heartbeat;
        return heartbeat > 0 && age is >= 0 and <= 3000
            ? (OpenXrLayerState)view.ReadUInt32(40)
            : OpenXrLayerState.Waiting;
    }

    public int LastError => view.ReadInt32(52);

    public void Publish(byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Length < OpenXrFramePacket.HeaderBytes || packet.Length > OpenXrFramePacket.Capacity)
        {
            throw new InvalidDataException("The OpenXR mailbox packet size is invalid.");
        }

        view.Write(8, ++sequence);
        Thread.MemoryBarrier();
        view.WriteArray(0, packet, 0, 8);
        view.Write(12, packet.Length);
        view.Write(16, clock());
        view.WriteArray(24, packet, 24, 8);
        view.WriteArray(
            OpenXrFramePacket.HeaderBytes,
            packet,
            OpenXrFramePacket.HeaderBytes,
            packet.Length - OpenXrFramePacket.HeaderBytes
        );
        Thread.MemoryBarrier();
        view.Write(8, ++sequence);
    }

    public void Stop()
    {
        view.Write(8, ++sequence);
        Thread.MemoryBarrier();
        view.Write(0, 0u);
        view.Write(16, 0L);
        view.Write(32, 0L);
        Thread.MemoryBarrier();
        view.Write(8, ++sequence);
        view.Flush();
    }

    public void Dispose()
    {
        Stop();
        view.Dispose();
        mapping.Dispose();
        writerLease.Dispose();
    }
}

public enum OpenXrLayerState : uint
{
    Waiting,
    Ready,
    UnsupportedGraphics,
    WrongRuntime,
    Error,
}
