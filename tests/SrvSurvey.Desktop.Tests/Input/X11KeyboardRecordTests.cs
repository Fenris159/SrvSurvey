using System.Runtime.InteropServices;
using SharpHook.Data;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class X11KeyboardRecordTests
{
    /// <summary>Exercises actual native packet decoding and stream lifetime on every test platform.</summary>
    [Fact]
    public void DecodesBufferedPressesAndReleasesAndDisposesExactlyOnce()
    {
        var native = new FakeRecordApi();
        using IX11KeyboardRecord? record = X11KeyboardRecord.TryCreate(":nested", native);
        Assert.NotNull(record);
        native.Emit(4, []);
        native.Emit(0, Packet(2, 64, 100));
        native.Emit(0, Packet(2, 32, 120));
        native.Emit(0, Packet(3, 32, 140));
        IReadOnlyList<UioHookEvent> events = record.ReadEvents();
        Assert.Equal([KeyCode.VcLeftAlt, KeyCode.VcO, KeyCode.VcO], events.Select(e => e.Keyboard.KeyCode));
        Assert.Equal([EventType.KeyPressed, EventType.KeyPressed, EventType.KeyReleased], events.Select(e => e.Type));
        Assert.Equal(120UL, events[1].Time);
        Assert.Empty(record.ReadEvents());
        native.Emit(0, Packet(2, 255, 150));
        native.Emit(0, Packet(12, 32, 160));
        native.Emit(0, Packet(2, 32, 170), swapped: true);
        native.Emit(0, new byte[4]);
        native.Emit(0, []);
        Assert.Empty(record.ReadEvents());
        Assert.Equal(9, native.FreedPackets);
        native.HasFailed = true;
        Assert.True(record.HasFailed);
        Assert.Empty(record.ReadEvents());
        record.Dispose();
        Assert.Empty(record.ReadEvents());
        record.Dispose();
        Assert.Equal(1, native.Disposals);
    }

    /// <summary>Checks unsuccessful setup and missing native libraries release all owned resources.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SetupFailuresReleaseTheRecorder(int failure)
    {
        var native = new FakeRecordApi { SetupFailure = failure };
        Assert.Null(X11KeyboardRecord.TryCreate(":nested", native));
        Assert.Equal(1, native.Disposals);
    }

    /// <summary>Builds a native X11 keyboard wire event with its server timestamp.</summary>
    private static byte[] Packet(byte type, byte keycode, uint time)
    {
        byte[] bytes = new byte[32];
        bytes[0] = type;
        bytes[1] = keycode;
        BitConverter.GetBytes(time).CopyTo(bytes, 4);
        return bytes;
    }

    /// <summary>Models only native resource ownership; packets still pass through production decoding.</summary>
    private sealed class FakeRecordApi : IX11KeyboardRecordApi
    {
        private X11KeyboardRecordCallback? callback;
        private readonly List<(int Category, byte[] Bytes, bool Swapped)> packets = [];
        public bool HasFailed { get; set; }
        public int SetupFailure { get; init; } = -1;
        public int Disposals { get; private set; }
        public int FreedPackets { get; private set; }

        /// <summary>Simulates supported, unavailable, and missing-library setup outcomes.</summary>
        public bool Open(string displayName, X11KeyboardRecordCallback callback)
        {
            this.callback = callback;
            return SetupFailure switch
            {
                0 => false,
                1 => throw new DllNotFoundException(),
                2 => throw new EntryPointNotFoundException(),
                3 => throw new BadImageFormatException(),
                _ => true,
            };
        }

        /// <summary>Returns a minimal keyboard layout while production code maps every supported key.</summary>
        public byte ResolveKeyCode(string name) =>
            name switch
            {
                "o" => 32,
                "Alt_L" => 64,
                _ => 0,
            };

        /// <summary>Buffers one native-style packet for the next stream read.</summary>
        public void Emit(int category, byte[] bytes, bool swapped = false) => packets.Add((category, bytes, swapped));

        /// <summary>Allocates callback packets using the real native ABI and invokes their decoder.</summary>
        public void ReadReplies()
        {
            foreach ((int category, byte[] bytes, bool swapped) in packets)
            {
                nint payload = bytes.Length == 0 ? nint.Zero : Marshal.AllocHGlobal(bytes.Length);
                if (payload != nint.Zero)
                {
                    Marshal.Copy(bytes, 0, payload, bytes.Length);
                }
                nint packet = Marshal.AllocHGlobal(Marshal.SizeOf<X11KeyboardRecordData>());
                Marshal.StructureToPtr(
                    new X11KeyboardRecordData
                    {
                        Category = category,
                        ClientSwapped = swapped ? 1 : 0,
                        Data = payload,
                        DataLength = (nuint)(bytes.Length / 4),
                    },
                    packet,
                    false
                );
                callback!(nint.Zero, packet);
            }
            packets.Clear();
        }

        /// <summary>Frees exactly the allocation transferred to the native-style callback.</summary>
        public void FreeData(nint pointer)
        {
            X11KeyboardRecordData packet = Marshal.PtrToStructure<X11KeyboardRecordData>(pointer);
            Marshal.FreeHGlobal(packet.Data);
            Marshal.FreeHGlobal(pointer);
            FreedPackets++;
        }

        /// <summary>Tracks disposal without requiring a platform display server.</summary>
        public void Dispose() => Disposals++;
    }
}
