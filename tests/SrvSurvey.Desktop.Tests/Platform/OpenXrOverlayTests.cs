using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OpenXrOverlayTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "srvsurvey-openxr-" + Guid.NewGuid().ToString("N")
    );
    private string MailboxPath => Path.Combine(directory, "frames.bin");
    private const long Now = 100_000;

    [Fact]
    public void PacketPreservesPixelsAndConvertsCalibrationToMetersAndQuaternion()
    {
        byte[] pixels = [255, 128, 64, 32, 1, 2, 3, 4];
        var calibration = new VrOverlayCalibration(20, new Vector3(10, 20, 30), new Vector3(0, 90, 0));
        byte[] packet = OpenXrFramePacket.Encode(
            new Dictionary<string, OpenXrOverlayFrame>
            {
                ["PlotJumpInfo"] = new(new VrOverlayFrame(2, 1, pixels), calibration, 0.5f),
            },
            9
        );

        Assert.Equal(OpenXrFramePacket.Magic, UInt(packet, 0));
        Assert.Equal(1u, UInt(packet, 4));
        Assert.Equal((uint)packet.Length, UInt(packet, 12));
        Assert.Equal(1u, UInt(packet, 24));
        Assert.Equal(9u, UInt(packet, 28));
        const int descriptor = OpenXrFramePacket.HeaderBytes;
        Assert.Equal(2u, UInt(packet, descriptor + 8));
        Assert.Equal(1u, UInt(packet, descriptor + 12));
        Assert.Equal(2f, Single(packet, descriptor + 24));
        Assert.Equal(1f, Single(packet, descriptor + 28));
        Assert.Equal(1f, Single(packet, descriptor + 32));
        Assert.Equal(2f, Single(packet, descriptor + 36));
        Assert.Equal(-3f, Single(packet, descriptor + 40));
        Assert.Equal(MathF.Sqrt(0.5f), Single(packet, descriptor + 48), 5);
        Assert.Equal(MathF.Sqrt(0.5f), Single(packet, descriptor + 56), 5);
        Assert.Equal(0.5f, Single(packet, descriptor + 60));
        Assert.Equal(pixels, packet[(int)UInt(packet, descriptor + 16)..]);
        Assert.Equal(
            packet,
            OpenXrFramePacket.Encode(
                new Dictionary<string, OpenXrOverlayFrame>
                {
                    ["PlotJumpInfo"] = new(new VrOverlayFrame(2, 1, pixels), calibration, 0.5f),
                },
                9
            )
        );
    }

    [Fact]
    public void PacketOrderIsStableAndEachPanelHasIndependentPixelsAndIdentity()
    {
        OpenXrOverlayFrame overlay = Overlay();
        byte[] packet = OpenXrFramePacket.Encode(
            new Dictionary<string, OpenXrOverlayFrame> { ["B"] = overlay, ["A"] = overlay },
            0
        );
        byte[] reordered = OpenXrFramePacket.Encode(
            new Dictionary<string, OpenXrOverlayFrame> { ["A"] = overlay, ["B"] = overlay },
            0
        );
        Assert.Equal(packet, reordered);
        Assert.NotEqual(UInt(packet, 128), UInt(packet, 192));
        Assert.Equal(256u, UInt(packet, 128 + 16));
        Assert.Equal(260u, UInt(packet, 192 + 16));
        Assert.Equal(128, OpenXrFramePacket.Encode(new Dictionary<string, OpenXrOverlayFrame>(), 0).Length);
    }

    [Theory]
    [InlineData(0, 1, 4, 1)]
    [InlineData(1, 0, 4, 1)]
    [InlineData(4097, 1, 4, 1)]
    [InlineData(1, 4097, 4, 1)]
    [InlineData(1, 1, 3, 1)]
    [InlineData(1, 1, 4, float.NaN)]
    public void PacketRejectsInvalidFrames(int width, int height, int bytes, float alpha)
    {
        OpenXrOverlayFrame overlay = Overlay() with
        {
            Frame = new VrOverlayFrame(width, height, new byte[bytes]),
            Alpha = alpha,
        };
        Assert.Throws<InvalidDataException>(() =>
            OpenXrFramePacket.Encode(new Dictionary<string, OpenXrOverlayFrame> { ["A"] = overlay }, 0)
        );
    }

    [Fact]
    public void PacketBoundsCountAndTotalMemoryAndClampsOpacity()
    {
        var frames = Enumerable
            .Range(0, 17)
            .ToDictionary(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => Overlay());
        Assert.Throws<InvalidDataException>(() => OpenXrFramePacket.Encode(frames, 0));
        frames.Clear();
        frames["A"] = Overlay() with { Frame = new VrOverlayFrame(4096, 4096, new byte[64 * 1024 * 1024]) };
        Assert.Throws<InvalidDataException>(() => OpenXrFramePacket.Encode(frames, 0));
        frames["A"] = Overlay() with { Alpha = -1 };
        Assert.Equal(0f, Single(OpenXrFramePacket.Encode(frames, 0), 188));
        frames["A"] = Overlay() with { Alpha = 2 };
        Assert.Equal(1f, Single(OpenXrFramePacket.Encode(frames, 0), 188));
    }

    [Fact]
    public void BridgePublishesConsistentFramesWithoutOverwritingLayerHealthAndStopsOnDispose()
    {
        using (var bridge = new OpenXrFrameBridge(MailboxPath, () => Now))
        {
            WriteState(OpenXrLayerState.Ready, Now);
            bridge.Publish(
                OpenXrFramePacket.Encode(new Dictionary<string, OpenXrOverlayFrame> { ["A"] = Overlay() }, 3)
            );
            byte[] bytes = ReadPacket();
            Assert.Equal(0u, UInt(bytes, 8) & 1);
            Assert.Equal(Now, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16)));
            Assert.Equal(OpenXrLayerState.Ready, bridge.ReadState());
            Assert.Equal(1u, UInt(bytes, 24));
            Assert.Throws<InvalidDataException>(() => bridge.Publish([]));
        }
        byte[] stopped = ReadPacket();
        Assert.Equal(0u, UInt(stopped, 0));
        Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(stopped.AsSpan(16)));
        uint sequence = UInt(stopped, 8);
        using var restarted = new OpenXrFrameBridge(MailboxPath, () => Now);
        restarted.Publish(OpenXrFramePacket.Encode(new Dictionary<string, OpenXrOverlayFrame>(), 0));
        Assert.True(UInt(ReadPacket(), 8) > sequence);
    }

    [Theory]
    [InlineData(0, OpenXrLayerState.Waiting)]
    [InlineData(Now - 3001, OpenXrLayerState.Waiting)]
    [InlineData(Now + 1, OpenXrLayerState.Waiting)]
    [InlineData(Now - 3000, OpenXrLayerState.Ready)]
    public void BridgeRejectsMissingStaleOrFutureLayerHeartbeats(long heartbeat, OpenXrLayerState expected)
    {
        using var bridge = new OpenXrFrameBridge(MailboxPath, () => Now);
        WriteState(OpenXrLayerState.Ready, heartbeat);
        Assert.Equal(expected, bridge.ReadState());
    }

    [Fact]
    public void BridgeRejectsConcurrentPublishersAndReleasesOwnershipOnShutdown()
    {
        using (var first = new OpenXrFrameBridge(MailboxPath, () => Now))
        {
            Assert.Throws<IOException>(() => new OpenXrFrameBridge(MailboxPath, () => Now));
        }
        using var replacement = new OpenXrFrameBridge(MailboxPath, () => Now);
        Assert.Equal(OpenXrLayerState.Waiting, replacement.ReadState());
    }

    [Theory]
    [InlineData(OpenXrLayerState.Waiting, "Install")]
    [InlineData(OpenXrLayerState.UnsupportedGraphics, "Direct3D 11")]
    [InlineData(OpenXrLayerState.WrongRuntime, "not VDXR")]
    [InlineData(OpenXrLayerState.Error, "could not submit")]
    public void RuntimeWaitsForActualLayerSessionAndExplainsIncompatibleRoutes(OpenXrLayerState state, string message)
    {
        using OpenXrOverlayRuntime runtime = Runtime();
        Assert.False(runtime.Probe().HeadsetPresent);
        WriteState(state, Now);
        Assert.Contains(message, runtime.Probe().Message);
        Assert.False(runtime.Initialize().Succeeded);
        Assert.False(runtime.PublishOverlay("A", Overlay().Frame, Overlay().Calibration, 1).Succeeded);
        Assert.False(runtime.ResetOrientation().Succeeded);
        Assert.False(runtime.SetInteractionEnabled(true).Succeeded);
        Assert.Empty(runtime.PollPointerEvents());
        Assert.Equal("VDXR / OpenXR", runtime.DisplayName);
    }

    [Fact]
    public void RuntimePublishesRemovesRecentersAndClearsPanelsOnShutdown()
    {
        using OpenXrOverlayRuntime runtime = Runtime();
        runtime.Probe();
        WriteState(OpenXrLayerState.Ready, Now);
        Assert.True(runtime.Probe().HeadsetPresent);
        Assert.True(runtime.Initialize().Succeeded);
        Assert.True(runtime.IsInitialized);
        VrRuntimeResult interaction = runtime.SetInteractionEnabled(true);
        Assert.False(interaction.Succeeded);
        Assert.Contains("Update the OpenXR layer and restart Elite", interaction.Message);
        Assert.Empty(runtime.PollPointerEvents());
        OpenXrOverlayFrame frame = Overlay();
        Assert.True(runtime.PublishOverlay("A", frame.Frame, frame.Calibration, frame.Alpha).Succeeded);
        Assert.True(runtime.CompleteFrame().Succeeded);
        Assert.Equal(1u, UInt(ReadPacket(), 24));
        Assert.True(runtime.ResetOrientation().Succeeded);
        Assert.Equal(1u, UInt(ReadPacket(), 28));
        runtime.RemoveOverlay("A");
        runtime.CompleteFrame();
        Assert.Equal(0u, UInt(ReadPacket(), 24));
        runtime.Shutdown();
        Assert.False(runtime.IsInitialized);
        Assert.Equal(0u, UInt(ReadPacket(), 0));
    }

    [Fact]
    public void RuntimeClearsTransportOnMalformedFramesAndExplainsUnsupportedPlatformsOrIoFailures()
    {
        using var unsupported = new OpenXrOverlayRuntime(platformSupported: () => false);
        Assert.False(unsupported.Probe().RuntimeAvailable);
        using var unavailable = new OpenXrOverlayRuntime(() => throw new IOException("locked"), () => true);
        Assert.Contains("locked", unavailable.Probe().Message);
        using OpenXrOverlayRuntime runtime = Runtime();
        runtime.Probe();
        WriteState(OpenXrLayerState.Ready, Now);
        runtime.Initialize();
        runtime.PublishOverlay("A", new VrOverlayFrame(0, 0, []), Overlay().Calibration, 1);
        Assert.False(runtime.CompleteFrame().Succeeded);
        Assert.Equal(0u, UInt(ReadPacket(), 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeMapsNativePointerToLogicalPanelAndDropsRemovedTargets(bool logicalSize)
    {
        using OpenXrOverlayRuntime runtime = Runtime();
        runtime.Probe();
        WriteState(OpenXrLayerState.Ready, Now);
        runtime.Initialize();
        using var native = new OpenXrInputWriter(MailboxPath + ".input");
        native.Ready(Now);
        Assert.True(runtime.SetInteractionEnabled(true).Succeeded);
        var frame = new VrOverlayFrame(4, 2, new byte[32], logicalSize ? 2 : 0, logicalSize ? 1 : 0);
        runtime.PublishOverlay("A", frame, Overlay().Calibration, 1);
        byte[] packet = OpenXrFramePacket.Encode(
            new Dictionary<string, OpenXrOverlayFrame> { ["A"] = new(frame, Overlay().Calibration, 1) },
            0
        );
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(128));
        native.Add(new OpenXrPointerEvent(id, VrOverlayPointerEventKind.Move, 0.25f, 0.75f, 0, 0));
        native.Add(new OpenXrPointerEvent(id, VrOverlayPointerEventKind.Wheel, 0.25f, 0.75f, -1, 2));
        IReadOnlyList<VrOverlayPointerEvent> events = runtime.PollPointerEvents();
        Assert.Equal(2, events.Count);
        Assert.Equal(
            new VrOverlayPointerEvent(
                "A",
                VrOverlayPointerEventKind.Move,
                logicalSize ? 0.5 : 1,
                logicalSize ? 0.75 : 1.5
            ),
            events[0]
        );
        Assert.Equal(-1, events[1].WheelX);
        Assert.Equal(2, events[1].WheelY);
        native.Add(new OpenXrPointerEvent(0, VrOverlayPointerEventKind.Reset, 0, 0, 0, 0));
        Assert.Equal(VrOverlayPointerEventKind.Reset, Assert.Single(runtime.PollPointerEvents()).Kind);
        runtime.RemoveOverlay("A");
        native.Add(new OpenXrPointerEvent(id, VrOverlayPointerEventKind.LeftButtonDown, 0, 0, 0, 0));
        Assert.Equal(VrOverlayPointerEventKind.Reset, Assert.Single(runtime.PollPointerEvents()).Kind);
        Assert.True(runtime.SetInteractionEnabled(false).Succeeded);
        Assert.Equal(0u, native.View.ReadUInt32(12));
        Assert.Empty(runtime.PollPointerEvents());
    }

    [Fact]
    public void RouterKeepsSteamVrForExistingProfilesAndDisposesTheOldTransportOnSwitch()
    {
        string profile = VrPlatformProfileCatalog.SteamVrProfileId;
        var created = new List<bool>();
        using var router = new VrOverlayRuntimeRouter(
            () => profile,
            direct =>
            {
                created.Add(direct);
                return new OpenXrOverlayRuntime(platformSupported: () => false);
            }
        );
        Assert.False(router.Probe().RuntimeAvailable);
        Assert.False(router.IsInitialized);
        Assert.False(router.Initialize().Succeeded);
        Assert.Equal("VDXR / OpenXR", router.DisplayName);
        Assert.False(router.PublishOverlay("A", Overlay().Frame, Overlay().Calibration, 1).Succeeded);
        router.CompleteFrame();
        router.RemoveOverlay("A");
        Assert.False(router.ResetOrientation().Succeeded);
        Assert.False(router.SetInteractionEnabled(true).Succeeded);
        Assert.Empty(router.PollPointerEvents());
        profile = VrPlatformProfileCatalog.MetaVdxrProfileId;
        router.Probe();
        profile = VrPlatformProfileCatalog.MetaVirtualDesktopProfileId;
        router.Probe();
        router.Shutdown();
        Assert.Collection(
            created,
            value => Assert.False(value),
            value => Assert.True(value),
            value => Assert.False(value)
        );
    }

    [Fact]
    public void DefaultTransportFactorySelectsTheRequestedBackendWithoutStartingSteamVr()
    {
        string profile = VrPlatformProfileCatalog.SteamVrProfileId;
        using var router = new VrOverlayRuntimeRouter(() => profile);
        Assert.False(router.IsInitialized);
        Assert.Equal("SteamVR/OpenVR", router.DisplayName);
        profile = VrPlatformProfileCatalog.MetaVdxrProfileId;
        Assert.False(router.IsInitialized);
        Assert.Equal("VDXR / OpenXR", router.DisplayName);
    }

    [Fact]
    public void BridgeDefaultClockUsesUtcAndReportsNativeErrors()
    {
        Assert.EndsWith(Path.Combine("SrvSurvey", "OpenXR", "frames.bin"), OpenXrFrameBridge.DefaultPath);
        using var bridge = new OpenXrFrameBridge(MailboxPath);
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bridge.Publish(OpenXrFramePacket.Encode(new Dictionary<string, OpenXrOverlayFrame>(), 0));
        long timestamp = BinaryPrimitives.ReadInt64LittleEndian(ReadPacket().AsSpan(16));
        Assert.InRange(timestamp, before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Assert.Equal(0, bridge.LastError);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private OpenXrOverlayRuntime Runtime() => new(() => new OpenXrFrameBridge(MailboxPath, () => Now), () => true);

    private static OpenXrOverlayFrame Overlay() =>
        new(
            new VrOverlayFrame(1, 1, [255, 128, 0, 255]),
            new VrOverlayCalibration(10, new Vector3(0, 0, 15), Vector3.Zero),
            1
        );

    private static uint UInt(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static float Single(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));

    private byte[] ReadPacket()
    {
        using var file = new FileStream(
            MailboxPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        byte[] bytes = new byte[1024];
        file.ReadExactly(bytes);
        return bytes;
    }

    private void WriteState(OpenXrLayerState state, long heartbeat)
    {
        // Match the native reader's shared file access, including on Windows.
        using var file = new FileStream(
            MailboxPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var mapping = MemoryMappedFile.CreateFromFile(
            file,
            null,
            OpenXrFramePacket.Capacity,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true
        );
        using MemoryMappedViewAccessor view = mapping.CreateViewAccessor();
        view.Write(40, (uint)state);
        view.Write(32, heartbeat);
    }
}
