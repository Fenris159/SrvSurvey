using System.Buffers.Binary;
using System.Net.Sockets;
using System.Numerics;
using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class CockpitOverlayCoordinatorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-camera-session-{Guid.NewGuid():N}"
    );
    private static readonly Matrix4x4 Lens = new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0.025f, 0);

    [Fact]
    public void LiveTrackingRequiresCalibrationFollowsFreelookAndRestoresDesktopOnExpiry()
    {
        CockpitOverlayViewModel model = CreateModel(true);
        model.BeginEdit();
        model.SelectPanel("PlotJumpInfo");
        model.Yaw = 15;
        model.Depth = 1.5;
        var host = new ProjectionHost();
        var timer = new ManualTimer();
        var transport = new CameraTransport();
        var clock = new CameraClock();
        using var client = new CockpitCameraClient(transport, clock);
        bool vr = false;
        using var coordinator = new CockpitOverlayCoordinator(model, host, () => vr, () => client, timer);
        timer.Pulse();
        Assert.Null(host.Change);
        Assert.False(model.CanCalibrate);

        transport.Enqueue(Lens, 1, 7, clock);
        timer.Pulse();
        Assert.True(model.CanCalibrate);
        Assert.Null(host.Change);
        model.CalibrateCommand.Execute(null);
        Assert.Equal(new CockpitPanelPose(Yaw: 15, Depth: 1.5), host.PoseProvider!("PlotJumpInfo"));
        AssertCenter(host, 500, 500);

        transport.Enqueue(Matrix4x4.CreateRotationY(MathF.PI / 6) * Lens, 2, 291, clock);
        timer.Pulse();
        AssertCenter(host, 788.675, 500);
        model.Yaw = 20;
        Assert.Equal(new CockpitPanelPose(Yaw: 20, Depth: 1.5), host.PoseProvider("PlotJumpInfo"));
        model.CancelEdit();
        Assert.Equal(new CockpitPanelPose(), host.PoseProvider("PlotJumpInfo"));
        transport.Enqueue(Lens, 3, 439, clock);
        timer.Pulse();
        AssertCenter(host, 500, 500);

        vr = true;
        timer.Pulse();
        Assert.Null(host.Change);
        Assert.False(model.CanCalibrate);
        vr = false;
        timer.Pulse();
        Assert.Null(host.Change);
        Assert.True(model.CanCalibrate);
        model.CalibrateCommand.Execute(null);
        AssertCenter(host, 500, 500);

        clock.Elapsed = 250;
        timer.Pulse();
        Assert.Null(host.Change);
        Assert.False(model.CanCalibrate);
        model.Enabled = false;
        Assert.False(timer.Running);
        Assert.Null(host.Change);
        coordinator.Dispose();
        model.Enabled = true;
        Assert.False(timer.Running);
    }

    [Fact]
    public void OptOutRestartAndUnsupportedHostsDoNotOpenCameraConnections()
    {
        CockpitOverlayViewModel model = CreateModel(false);
        var host = new ProjectionHost();
        var timer = new ManualTimer();
        using (
            var coordinator = new CockpitOverlayCoordinator(
                model,
                host,
                () => false,
                () => throw new InvalidOperationException("Disabled mode must not open network connections."),
                timer
            )
        )
        {
            Assert.False(timer.Running);
            model.Enabled = true;
            Assert.True(model.RestartRequired);
            Assert.False(timer.Running);
        }

        CockpitOverlayViewModel restarted = CreateModel(true);
        using var unsupported = new CockpitOverlayCoordinator(
            restarted,
            new ProjectionHost { SupportsCockpitPanels = false },
            () => false,
            () => throw new InvalidOperationException("Unsupported hosts must not open a camera connection."),
            new ManualTimer()
        );
        Assert.Equal("Cockpit panels need a supported shared desktop overlay host.", restarted.StatusMessage);
    }

    [Fact]
    public void CameraConnectionFailuresAndUnsupportedProjectionsLeaveDesktopPanelsAvailable()
    {
        CockpitOverlayViewModel model = CreateModel(true);
        var host = new ProjectionHost();
        var timer = new ManualTimer();
        using (
            var unavailable = new CockpitOverlayCoordinator(
                model,
                host,
                () => false,
                () => throw new SocketException((int)SocketError.AccessDenied),
                timer
            )
        )
        {
            Assert.False(timer.Running);
            Assert.Null(host.Change);
            Assert.Contains("Desktop positions remain active.", model.StatusMessage, StringComparison.Ordinal);
        }

        var transport = new CameraTransport();
        var clock = new CameraClock();
        using var client = new CockpitCameraClient(transport, clock);
        var activeTimer = new ManualTimer();
        using var coordinator = new CockpitOverlayCoordinator(model, host, () => false, () => client, activeTimer);
        activeTimer.Pulse();
        transport.Enqueue(Matrix4x4.Identity, 1, 1, clock);
        activeTimer.Pulse();
        model.CalibrateCommand.Execute(null);
        Assert.Null(host.Change);
        Assert.Equal("A current cockpit camera sample is needed before calibration.", model.StatusMessage);
    }

    [Fact]
    public void DelayedPacketsExpireAtTheCaptureDeadlineInsteadOfReceivingAnotherLease()
    {
        var clock = new CameraClock();
        var transport = new CameraTransport();
        using var client = new CockpitCameraClient(transport, clock);
        Assert.Null(client.Poll());
        transport.Enqueue(Lens, 1, 1, clock);
        clock.Elapsed = 200;
        Assert.Equal(1UL, Assert.IsType<CockpitCameraFrame>(client.Poll()).Sequence);
        clock.Elapsed = 249;
        Assert.Equal(1UL, Assert.IsType<CockpitCameraFrame>(client.Poll()).Sequence);
        clock.Elapsed = 250;
        Assert.Null(client.Poll());
    }

    private CockpitOverlayViewModel CreateModel(bool enabled)
    {
        var store = new CockpitOverlaySettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new CockpitOverlayPreferences(enabled, new Dictionary<string, CockpitPanelPose>()));
        return new CockpitOverlayViewModel(store);
    }

    private static void AssertCenter(ProjectionHost host, double x, double y)
    {
        Matrix4x4 change = Assert.IsType<Matrix4x4>(host.Change);
        Matrix projection = Assert.IsType<Matrix>(
            CockpitPanelProjection.Create(
                new Rect(400, 450, 200, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(),
                change,
                host.CapturedLens
            )
        );
        Point center = projection.Transform(new Point(100, 50));
        Assert.InRange(center.X, x - 0.001, x + 0.001);
        Assert.InRange(center.Y, y - 0.001, y + 0.001);
    }

    private sealed class ProjectionHost : ICockpitProjectionHost
    {
        public bool SupportsCockpitPanels { get; init; } = true;
        public Matrix4x4? Change { get; private set; }
        public CockpitProjectionLens? CapturedLens { get; private set; }
        public Func<string, CockpitPanelPose>? PoseProvider { get; private set; }

        public void SetCockpitProjection(
            Matrix4x4? cameraChange,
            Func<string, CockpitPanelPose>? poseProvider,
            CockpitProjectionLens? lens = null
        )
        {
            Change = cameraChange;
            CapturedLens = lens;
            PoseProvider = poseProvider;
        }
    }

    private sealed class ManualTimer : IHostedOverlayTimer
    {
        public event EventHandler? Tick;
        public bool Running { get; private set; }

        public void Start() => Running = true;

        public void Stop() => Running = false;

        public void Dispose() => Stop();

        public void Pulse()
        {
            if (Running)
            {
                Tick?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class CameraClock : TimeProvider
    {
        public long Elapsed { get; set; }
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Elapsed;

        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 11, 11, 6, 0, 0, TimeSpan.Zero).AddMilliseconds(Elapsed);
    }

    private sealed class CameraTransport : ICockpitCameraTransport
    {
        private readonly Queue<byte[]> pending = new();
        private byte[] nonce = [];
        public bool CanReceive => pending.Count > 0;

        public void Send(ReadOnlySpan<byte> bytes) => nonce = bytes[8..].ToArray();

        public int Receive(Span<byte> buffer)
        {
            byte[] packet = pending.Dequeue();
            packet.CopyTo(buffer);
            return packet.Length;
        }

        public void Dispose() => pending.Clear();

        public void Enqueue(Matrix4x4 rowTransform, ulong sequence, uint sourceIndex, CameraClock clock)
        {
            byte[] packet = new byte[136];
            "SRVCAM01"u8.CopyTo(packet);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 136);
            BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(16), sequence);
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(24), clock.GetUtcNow().ToUnixTimeMilliseconds());
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(36), sourceIndex);
            var column = Matrix4x4.Transpose(rowTransform);
            float[] values =
            [
                column.M11,
                column.M12,
                column.M13,
                column.M14,
                column.M21,
                column.M22,
                column.M23,
                column.M24,
                column.M31,
                column.M32,
                column.M33,
                column.M34,
                column.M41,
                column.M42,
                column.M43,
                column.M44,
            ];
            for (int index = 0; index < values.Length; index++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(40 + index * 4), values[index]);
            }
            nonce.CopyTo(packet, 104);
            pending.Enqueue(packet);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
