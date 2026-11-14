using System.Buffers.Binary;
using System.Net.Sockets;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class CockpitCameraFrameTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 6, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Nonce = Enumerable.Repeat((byte)0x47, 32).ToArray();

    [Fact]
    public void DecodesTheNativePacketAndTransposesShaderMatrixForNumerics()
    {
        byte[] packet = Packet();
        CockpitCameraFrame frame = Assert.IsType<CockpitCameraFrame>(CockpitCameraFrame.Parse(packet, Nonce, Now));
        Assert.Equal(7UL, frame.Sequence);
        Assert.Equal(3U, frame.MatrixIndex);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), frame.UnixMilliseconds);
        Assert.Equal(5, frame.Transform.M12);
        Assert.Equal(2, frame.Transform.M21);
        Assert.Equal(29, frame.Transform.M43);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 2)]
    [InlineData(12, 135)]
    [InlineData(32, 0)]
    [InlineData(104, 1)]
    public void RejectsBadHeadersUnverifiedReferencesAndOtherSessions(int offset, byte value)
    {
        byte[] packet = Packet();
        packet[offset] = value;
        Assert.Null(CockpitCameraFrame.Parse(packet, Nonce, Now));
    }

    [Fact]
    public void RejectsStaleFutureTruncatedAndNonFiniteSamples()
    {
        byte[] packet = Packet();
        Assert.Null(CockpitCameraFrame.Parse(packet, Nonce, Now.AddMilliseconds(250)));
        Assert.Null(CockpitCameraFrame.Parse(packet, Nonce, Now.AddMilliseconds(-101)));
        Assert.Null(CockpitCameraFrame.Parse(packet.AsSpan(0, 135), Nonce, Now));
        Assert.Null(CockpitCameraFrame.Parse(packet, Nonce.AsSpan(0, 31), Now));
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(40), float.NaN);
        Assert.Null(CockpitCameraFrame.Parse(packet, Nonce, Now));
    }

    [Fact]
    public void RejectsSingularTransforms()
    {
        byte[] packet = Packet();
        packet.AsSpan(40, 64).Clear();
        Assert.Null(CockpitCameraFrame.Parse(packet, Nonce, Now));
    }

    [Fact]
    public void LiveClientExpiresSamplesRenewsItsLeaseAndRecoversAfterAConnectionError()
    {
        var clock = new CameraClock();
        var transport = new CameraTransport();
        using var client = new CockpitCameraClient(transport, clock);
        Assert.Null(client.Poll());
        Assert.Equal("SRVCAM01"u8.ToArray(), transport.Sent[0][..8]);
        byte[] packet = Packet();
        transport.Sent[0].AsSpan(8, 32).CopyTo(packet.AsSpan(104));
        transport.Incoming.Enqueue(packet);
        Assert.Equal(7UL, client.Poll()!.Sequence);

        byte[] older = (byte[])packet.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(older.AsSpan(16), 6);
        transport.Incoming.Enqueue(older);
        Assert.Equal(7UL, client.Poll()!.Sequence);
        clock.Advance(250);
        Assert.Null(client.Poll());
        clock.Advance(250);
        Assert.Null(client.Poll());
        Assert.Equal(2, transport.Sent.Count);

        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(24), clock.GetUtcNow().ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(16), 1);
        transport.Incoming.Enqueue(packet);
        transport.FailNextReceive = true;
        Assert.Null(client.Poll());
        Assert.Equal(1UL, client.Poll()!.Sequence);
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.Poll());
    }

    [Fact]
    public void LiveClientBoundsEachDrainAndRejectsResponsesForAnotherSession()
    {
        var transport = new CameraTransport();
        using var client = new CockpitCameraClient(transport, new CameraClock());
        Assert.Null(client.Poll());
        transport.Incoming.Enqueue(Packet());
        Assert.Null(client.Poll());
        for (ulong sequence = 1; sequence <= 9; sequence++)
        {
            byte[] packet = Packet();
            transport.Sent[0].AsSpan(8, 32).CopyTo(packet.AsSpan(104));
            BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(16), sequence);
            transport.Incoming.Enqueue(packet);
        }
        Assert.Equal(8UL, client.Poll()!.Sequence);
        Assert.Equal(9UL, client.Poll()!.Sequence);
    }

    private sealed class CameraClock : TimeProvider
    {
        private long elapsed;
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => elapsed;

        public override DateTimeOffset GetUtcNow() => Now.AddMilliseconds(elapsed);

        public void Advance(long milliseconds) => elapsed += milliseconds;
    }

    private sealed class CameraTransport : ICockpitCameraTransport
    {
        public Queue<byte[]> Incoming { get; } = new();
        public List<byte[]> Sent { get; } = [];
        public bool FailNextReceive { get; set; }
        public bool CanReceive => Incoming.Count > 0;

        public void Send(ReadOnlySpan<byte> bytes) => Sent.Add(bytes.ToArray());

        public int Receive(Span<byte> buffer)
        {
            if (FailNextReceive)
            {
                FailNextReceive = false;
                throw new SocketException((int)SocketError.ConnectionReset);
            }
            byte[] packet = Incoming.Dequeue();
            packet.CopyTo(buffer);
            return packet.Length;
        }

        public void Dispose() => Incoming.Clear();
    }

    private static byte[] Packet()
    {
        byte[] packet = new byte[136];
        "SRVCAM01"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 136);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(16), 7);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(24), Now.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(36), 3);
        float[] values = [1, 2, 3, 4, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(40 + index * 4), values[index]);
        }
        Nonce.CopyTo(packet, 104);
        return packet;
    }
}
