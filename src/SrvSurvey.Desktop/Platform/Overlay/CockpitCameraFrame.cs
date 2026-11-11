using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>A live graphics bridge sample whose transform has been verified to belong to cockpit geometry.</summary>
public sealed record CockpitCameraFrame(ulong Sequence, long UnixMilliseconds, uint MatrixIndex, Matrix4x4 Transform)
{
    public const int PacketLength = 136;
    public const int NonceLength = 32;

    /// <summary>Applies the same capture-time deadline to newly received and cached samples.</summary>
    public bool IsCurrent(DateTimeOffset now) => IsCurrent(UnixMilliseconds, now);

    private static bool IsCurrent(long timestamp, DateTimeOffset now)
    {
        long current = now.ToUnixTimeMilliseconds();
        return timestamp > current - 250 && timestamp <= current + 100;
    }

    /// <summary>Accepts only complete, authenticated, current protocol packets with a usable cockpit reference.</summary>
    public static CockpitCameraFrame? Parse(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> nonce, DateTimeOffset now)
    {
        if (
            packet.Length != PacketLength
            || nonce.Length != NonceLength
            || !packet[..8].SequenceEqual("SRVCAM01"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]) != 1
            || BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]) != PacketLength
            || BinaryPrimitives.ReadUInt32LittleEndian(packet[32..]) != 1
            || !CryptographicOperations.FixedTimeEquals(packet[104..], nonce)
        )
        {
            return null;
        }
        long timestamp = BinaryPrimitives.ReadInt64LittleEndian(packet[24..]);
        if (!IsCurrent(timestamp, now))
        {
            return null;
        }
        Span<float> values = stackalloc float[16];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(packet[(40 + index * 4)..]);
            if (!float.IsFinite(values[index]))
            {
                return null;
            }
        }
        // Shader buffers multiply column vectors; System.Numerics multiplies row vectors.
        var transform = new Matrix4x4(
            values[0],
            values[4],
            values[8],
            values[12],
            values[1],
            values[5],
            values[9],
            values[13],
            values[2],
            values[6],
            values[10],
            values[14],
            values[3],
            values[7],
            values[11],
            values[15]
        );
        return Matrix4x4.Invert(transform, out _)
            ? new CockpitCameraFrame(
                BinaryPrimitives.ReadUInt64LittleEndian(packet[16..]),
                timestamp,
                BinaryPrimitives.ReadUInt32LittleEndian(packet[36..]),
                transform
            )
            : null;
    }
}
