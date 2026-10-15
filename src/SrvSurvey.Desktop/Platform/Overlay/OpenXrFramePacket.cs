using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Versioned little-endian wire format shared with the native OpenXR layer.</summary>
public static class OpenXrFramePacket
{
    public const uint Magic = 0x58565253;
    public const int Version = 1;
    public const int Capacity = 64 * 1024 * 1024;
    public const int HeaderBytes = 128;
    public const int DescriptorBytes = 64;
    public const int MaximumOverlays = 16;

    public static byte[] Encode(IReadOnlyDictionary<string, OpenXrOverlayFrame> overlays, uint originRevision)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        if (overlays.Count > MaximumOverlays)
        {
            throw new InvalidDataException("The OpenXR layer supports at most 16 overlay panels.");
        }

        int length = HeaderBytes + overlays.Count * DescriptorBytes;
        foreach (OpenXrOverlayFrame overlay in overlays.Values)
        {
            Validate(overlay);
            length = checked(length + overlay.Frame.RgbaBytes.Length);
            if (length > Capacity)
            {
                throw new InvalidDataException("The OpenXR overlay frames exceed the 64 MiB transport limit.");
            }
        }

        byte[] packet = new byte[length];
        WriteUInt32(packet, 0, Magic);
        WriteUInt32(packet, 4, Version);
        WriteUInt32(packet, 12, (uint)length);
        WriteUInt32(packet, 24, (uint)overlays.Count);
        WriteUInt32(packet, 28, originRevision);
        int descriptorOffset = HeaderBytes;
        int pixelOffset = HeaderBytes + overlays.Count * DescriptorBytes;
        foreach (
            (string name, OpenXrOverlayFrame overlay) in overlays.OrderBy(entry => entry.Key, StringComparer.Ordinal)
        )
        {
            Span<byte> descriptor = packet.AsSpan(descriptorOffset, DescriptorBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(
                descriptor,
                BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(name)))
            );
            WriteUInt32(descriptor, 8, (uint)overlay.Frame.Width);
            WriteUInt32(descriptor, 12, (uint)overlay.Frame.Height);
            WriteUInt32(descriptor, 16, (uint)pixelOffset);
            WriteUInt32(descriptor, 20, (uint)overlay.Frame.RgbaBytes.Length);
            float width = overlay.Calibration.Scale / 10;
            WriteSingle(descriptor, 24, width);
            WriteSingle(descriptor, 28, width * overlay.Frame.Height / overlay.Frame.Width);
            WriteSingle(descriptor, 32, overlay.Calibration.Position.X / 10);
            WriteSingle(descriptor, 36, overlay.Calibration.Position.Y / 10);
            WriteSingle(descriptor, 40, -overlay.Calibration.Position.Z / 10);
            Vector3 rotation = overlay.Calibration.Rotation * (MathF.PI / 180);
            var orientation = Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z);
            WriteSingle(descriptor, 44, orientation.X);
            WriteSingle(descriptor, 48, orientation.Y);
            WriteSingle(descriptor, 52, orientation.Z);
            WriteSingle(descriptor, 56, orientation.W);
            WriteSingle(descriptor, 60, Math.Clamp(overlay.Alpha, 0, 1));
            overlay.Frame.RgbaBytes.CopyTo(packet, pixelOffset);
            pixelOffset += overlay.Frame.RgbaBytes.Length;
            descriptorOffset += DescriptorBytes;
        }

        return packet;
    }

    private static void Validate(OpenXrOverlayFrame overlay)
    {
        overlay.Calibration.Validate();
        if (
            overlay.Frame.Width is <= 0 or > 4096
            || overlay.Frame.Height is <= 0 or > 4096
            || (long)overlay.Frame.Width * overlay.Frame.Height * 4 != overlay.Frame.RgbaBytes.Length
            || !float.IsFinite(overlay.Alpha)
        )
        {
            throw new InvalidDataException("The OpenXR overlay frame has invalid pixels, dimensions, or opacity.");
        }
    }

    private static void WriteUInt32(Span<byte> target, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(target[offset..], value);

    private static void WriteSingle(Span<byte> target, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(target[offset..], value);
}

public sealed record OpenXrOverlayFrame(VrOverlayFrame Frame, VrOverlayCalibration Calibration, float Alpha);
