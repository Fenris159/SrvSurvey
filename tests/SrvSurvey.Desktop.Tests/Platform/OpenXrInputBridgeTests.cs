using System.IO.MemoryMappedFiles;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OpenXrInputBridgeTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "srvsurvey-input-" + Guid.NewGuid().ToString("N")
    );
    private const long Now = 100_000;
    private string InputPath => Path.Combine(directory, "frames.bin.input");

    public OpenXrInputBridgeTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void ControlHandshakeRejectsOldGameAndPublisherGenerationsAndStopsOnDispose()
    {
        using (var bridge = new OpenXrInputBridge(InputPath, () => Now))
        {
            using var native = new OpenXrInputWriter(InputPath);
            Assert.Equal(OpenXrInputBridge.Magic, native.View.ReadUInt32(0));
            Assert.Equal(OpenXrInputBridge.Version, native.View.ReadUInt32(4));
            Assert.Equal(Now, native.View.ReadInt64(16));
            Assert.Equal(0u, native.View.ReadUInt32(8) & 1);
            Assert.False(bridge.IsReady);
            native.Ready(Now, generation: 1);
            Assert.False(bridge.IsReady);
            native.Ready(Now);
            Assert.True(bridge.IsReady);
            native.View.Write(44, -6);
            Assert.Equal(-6, bridge.LastError);
            bridge.SetEnabled(true);
            Assert.Equal(1u, native.View.ReadUInt32(12));
            bridge.Stop();
            Assert.Equal(0u, native.View.ReadUInt32(12));
            Assert.Equal(0L, native.View.ReadInt64(16));
        }
        using var previous = new OpenXrInputWriter(InputPath);
        Assert.Equal(0u, previous.View.ReadUInt32(12));
        using var restarted = new OpenXrInputBridge(InputPath, () => Now);
        Assert.False(restarted.IsReady);
        previous.Ready(Now);
        Assert.True(restarted.IsReady);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(Now - 3001, false)]
    [InlineData(Now + 1, false)]
    [InlineData(Now - 3000, true)]
    public void ReadinessRequiresFreshNativeHeartbeat(long heartbeat, bool expected)
    {
        using var bridge = new OpenXrInputBridge(InputPath, () => Now);
        using var native = new OpenXrInputWriter(InputPath);
        native.Ready(heartbeat);
        Assert.Equal(expected, bridge.IsReady);
    }

    [Fact]
    public void EventsPreserveOrderCoordinatesAndButtonsAcrossTheRingBoundary()
    {
        using var bridge = new OpenXrInputBridge(InputPath, () => Now);
        using var native = new OpenXrInputWriter(InputPath);
        native.Ready(Now);
        native.View.Write(52, uint.MaxValue - 1);
        bridge.SetEnabled(true);
        foreach (VrOverlayPointerEventKind kind in Enum.GetValues<VrOverlayPointerEventKind>())
        {
            native.Add(new OpenXrPointerEvent(42, kind, 0.25f, 0.75f, -0.5f, 1));
        }
        IReadOnlyList<OpenXrPointerEvent> events = bridge.ReadEvents();
        Assert.Equal(Enum.GetValues<VrOverlayPointerEventKind>(), events.Select(item => item.Kind));
        Assert.All(events, item => Assert.Equal(new OpenXrPointerEvent(42, item.Kind, 0.25f, 0.75f, -0.5f, 1), item));
        Assert.Empty(bridge.ReadEvents());
    }

    [Fact]
    public void DisablingDropsBufferedEventsAndReenablingStartsAtTheCurrentPosition()
    {
        using var bridge = new OpenXrInputBridge(InputPath, () => Now);
        using var native = new OpenXrInputWriter(InputPath);
        native.Ready(Now);
        native.Add(Event());
        Assert.Empty(bridge.ReadEvents());
        bridge.SetEnabled(true);
        Assert.Empty(bridge.ReadEvents());
        native.Add(Event());
        Assert.Single(bridge.ReadEvents());
        bridge.SetEnabled(false);
        native.Add(Event());
        Assert.Empty(bridge.ReadEvents());
        bridge.SetEnabled(true);
        Assert.Empty(bridge.ReadEvents());
    }

    [Theory]
    [InlineData("overflow")]
    [InlineData("torn")]
    [InlineData("stale")]
    [InlineData("unknown")]
    [InlineData("nan")]
    [InlineData("position")]
    [InlineData("wheel")]
    [InlineData("wheel-infinity")]
    public void AmbiguousInputCancelsCaptureWithoutReplayingClicks(string failure)
    {
        using var bridge = new OpenXrInputBridge(InputPath, () => Now);
        using var native = new OpenXrInputWriter(InputPath);
        native.Ready(Now);
        bridge.SetEnabled(true);
        native.Add(
            failure switch
            {
                "unknown" => Event() with { Kind = (VrOverlayPointerEventKind)99 },
                "nan" => Event() with { X = float.NaN },
                "position" => Event() with { Y = 11 },
                "wheel" => Event() with { WheelX = 9 },
                "wheel-infinity" => Event() with { WheelY = float.PositiveInfinity },
                _ => Event(),
            }
        );
        switch (failure)
        {
            case "overflow":
                for (int i = 0; i < OpenXrInputBridge.EventCapacity; i++)
                {
                    native.Add(Event());
                }
                break;
            case "torn":
                native.View.Write(48, 1u);
                break;
            case "stale":
                native.Ready(Now - 4000);
                break;
        }
        Assert.Equal(VrOverlayPointerEventKind.Reset, Assert.Single(bridge.ReadEvents()).Kind);
        native.Ready(Now);
        native.View.Write(48, 2u);
        Assert.Empty(bridge.ReadEvents());
        native.Add(Event() with { Kind = VrOverlayPointerEventKind.Move });
        Assert.Equal(VrOverlayPointerEventKind.Move, Assert.Single(bridge.ReadEvents()).Kind);
    }

    [Fact]
    public void ConstructorResizesOldFilesAndRecoversTornControlSequence()
    {
        File.WriteAllBytes(InputPath, [1, 2, 3]);
        using (var bridge = new OpenXrInputBridge(InputPath, () => Now))
        {
            using var native = new OpenXrInputWriter(InputPath);
            native.View.Write(8, 5u);
        }
        using var restarted = new OpenXrInputBridge(InputPath, () => Now);
        using var reader = new OpenXrInputWriter(InputPath);
        Assert.Equal(OpenXrInputBridge.Capacity, new FileInfo(InputPath).Length);
        Assert.Equal(0u, reader.View.ReadUInt32(8) & 1);
        Assert.Throws<ArgumentException>(() => new OpenXrInputBridge("", () => Now));
        Assert.Throws<ArgumentNullException>(() => new OpenXrInputBridge(InputPath, null!));
    }

    private static OpenXrPointerEvent Event() => new(42, VrOverlayPointerEventKind.LeftButtonDown, 0.5f, 0.5f, 0, 0);

    public void Dispose() => Directory.Delete(directory, true);
}

internal sealed class OpenXrInputWriter : IDisposable
{
    private readonly MemoryMappedFile mapping;
    public MemoryMappedViewAccessor View { get; }

    public OpenXrInputWriter(string path)
    {
        using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete
        );
        mapping = MemoryMappedFile.CreateFromFile(
            file,
            null,
            OpenXrInputBridge.Capacity,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true
        );
        View = mapping.CreateViewAccessor();
    }

    public void Ready(long now, ulong? generation = null)
    {
        View.Write(40, 1u);
        View.Write(56, generation ?? View.ReadUInt64(24));
        View.Write(32, now);
    }

    public void Add(OpenXrPointerEvent item)
    {
        uint count = View.ReadUInt32(52);
        uint sequence = View.ReadUInt32(48) & ~1u;
        View.Write(48, sequence + 1);
        int offset =
            OpenXrInputBridge.EventOffset
            + (int)(count % OpenXrInputBridge.EventCapacity) * OpenXrInputBridge.EventBytes;
        View.Write(offset, item.Id);
        View.Write(offset + 8, (uint)item.Kind);
        View.Write(offset + 16, item.X);
        View.Write(offset + 20, item.Y);
        View.Write(offset + 24, item.WheelX);
        View.Write(offset + 28, item.WheelY);
        View.Write(52, unchecked(count + 1));
        View.Write(48, sequence + 2);
    }

    public void Dispose()
    {
        View.Dispose();
        mapping.Dispose();
    }
}
