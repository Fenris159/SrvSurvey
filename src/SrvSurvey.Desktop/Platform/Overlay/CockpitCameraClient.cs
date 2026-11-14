using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Receives a bounded live camera stream over loopback, with a new session nonce for every opt-in connection.</summary>
public sealed class CockpitCameraClient : IDisposable
{
    private readonly ICockpitCameraTransport transport;
    private readonly TimeProvider clock;
    private readonly byte[] nonce = RandomNumberGenerator.GetBytes(CockpitCameraFrame.NonceLength);
    private readonly byte[] hello = new byte[40];
    private long? lastHello;
    private long? lastReceived;
    private CockpitCameraFrame? latest;
    private bool disposed;

    /// <summary>Creates a local-only connection; no game data is requested until Poll is called.</summary>
    public CockpitCameraClient(TimeProvider? clock = null)
        : this(new CockpitCameraUdpTransport(), clock ?? TimeProvider.System) { }

    internal CockpitCameraClient(ICockpitCameraTransport transport, TimeProvider clock)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        "SRVCAM01"u8.CopyTo(hello);
        nonce.CopyTo(hello, 8);
    }

    /// <summary>Renews the opt-in lease and drains at most eight datagrams, never waiting for the game or GPU.</summary>
    public CockpitCameraFrame? Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            if (lastHello is null || clock.GetElapsedTime(lastHello.Value) >= TimeSpan.FromMilliseconds(500))
            {
                transport.Send(hello);
                lastHello = clock.GetTimestamp();
            }
            ReadAvailable();
        }
        catch (SocketException)
        {
            // An absent/restarting optional bridge must leave the normal overlay and journal runtimes running.
        }
        return
            latest is { } frame
            && frame.IsCurrent(clock.GetUtcNow())
            && lastReceived is { } received
            && clock.GetElapsedTime(received) < TimeSpan.FromMilliseconds(250)
            ? frame
            : null;
    }

    private void ReadAvailable()
    {
        Span<byte> packet = stackalloc byte[1024];
        for (int attempt = 0; attempt < 8 && transport.CanReceive; attempt++)
        {
            int length = transport.Receive(packet);
            var frame = CockpitCameraFrame.Parse(packet[..length], nonce, clock.GetUtcNow());
            if (
                frame is not null
                && (
                    latest is not { } previous
                    || !previous.IsCurrent(clock.GetUtcNow())
                    || lastReceived is not { } received
                    || clock.GetElapsedTime(received) >= TimeSpan.FromMilliseconds(250)
                    || frame.Sequence > previous.Sequence
                )
            )
            {
                latest = frame;
                lastReceived = clock.GetTimestamp();
            }
        }
    }

    /// <summary>Stops renewing the native bridge lease as soon as the user opts out.</summary>
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            transport.Dispose();
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(hello);
        }
    }
}

/// <summary>The optional native bridge's loopback datagram boundary.</summary>
internal interface ICockpitCameraTransport : IDisposable
{
    bool CanReceive { get; }
    void Send(ReadOnlySpan<byte> bytes);
    int Receive(Span<byte> buffer);
}

internal sealed class CockpitCameraUdpTransport : ICockpitCameraTransport
{
    private readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    {
        Blocking = false,
    };

    public CockpitCameraUdpTransport()
    {
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Connect(new IPEndPoint(IPAddress.Loopback, 47765));
    }

    public bool CanReceive => socket.Poll(0, SelectMode.SelectRead);

    public void Send(ReadOnlySpan<byte> bytes) => _ = socket.Send(bytes);

    public int Receive(Span<byte> buffer) => socket.Receive(buffer);

    public void Dispose() => socket.Dispose();
}
