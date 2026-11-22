using System.Buffers.Binary;
using System.Numerics;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class LinuxOpenXrOverlayRuntimeTests
{
    [Fact]
    public void ProbeMapsPreflightMissingLibraryAndEveryNativeStatus()
    {
        LinuxVrRuntimeInspector activeInspector = ActiveLinuxInspector("wivrn-server");
        var missingBackend = new StubBackend { IsAvailable = false };
        using var missingRuntime = new LinuxOpenXrOverlayRuntime(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId,
            missingBackend,
            activeInspector
        );
        Assert.Equal("WiVRn / OpenXR", missingRuntime.DisplayName);
        Assert.False(missingRuntime.Probe().RuntimeAvailable);
        Assert.False(missingRuntime.Initialize().Succeeded);

        var backend = new StubBackend();
        using var runtime = new LinuxOpenXrOverlayRuntime(
            VrPlatformProfileCatalog.MonadoOpenXrProfileId,
            backend,
            ActiveLinuxInspector("monado-service")
        );
        Assert.Equal("Monado / OpenXR", runtime.DisplayName);

        backend.ProbeOutcome = new(LinuxOpenXrNativeStatus.LoaderMissing, 0, "Missing libopenxr_loader.so.1");
        Assert.False(runtime.Probe().RuntimeAvailable);

        backend.ProbeOutcome = new(LinuxOpenXrNativeStatus.VulkanMissing, 0, "Missing libvulkan.so.1");
        Assert.False(runtime.Probe().RuntimeAvailable);

        backend.ProbeOutcome = new(LinuxOpenXrNativeStatus.OverlayExtensionUnsupported, 0, "No XR_EXTX_overlay");
        VrRuntimeProbe extProbe = runtime.Probe();
        Assert.True(extProbe.RuntimeAvailable);
        Assert.False(extProbe.HeadsetPresent);
        Assert.Contains("XR_EXTX_overlay", extProbe.Message);

        backend.ProbeOutcome = new(LinuxOpenXrNativeStatus.HeadsetUnavailable, -35, "Connect headset");
        Assert.False(runtime.Probe().HeadsetPresent);

        backend.ProbeOutcome = new(LinuxOpenXrNativeStatus.Ok, 0, "Ready");
        Assert.True(runtime.Probe().HeadsetPresent);
    }

    [Fact]
    public void RuntimeLifecyclePublishesFramesUpdatesMaxLayersTracksHeadPoseAndRecoversFromDisconnect()
    {
        var backend = new StubBackend
        {
            CreateOutcome = new(new nint(123), LinuxOpenXrNativeStatus.Ok, 0, "Created"),
            SessionStatus = new(LinuxOpenXrNativeStatus.Ok, 0, 1, 12, "Active"),
            HeadPose = new VrOverlayHeadPose(new Vector3(0.1f, 0.2f, -0.3f), 0.5f),
        };
        using var runtime = new LinuxOpenXrOverlayRuntime(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId,
            backend,
            ActiveLinuxInspector("wivrn-server")
        );

        Assert.False(runtime.PublishOverlay("PlotJumpInfo", ValidFrame(), ValidCalibration(), 1f).Succeeded);
        Assert.False(runtime.CompleteFrame().Succeeded);
        Assert.False(runtime.ResetOrientation().Succeeded);
        Assert.False(runtime.TryGetHeadPose(out _));

        Assert.True(runtime.Initialize().Succeeded);
        Assert.True(runtime.IsInitialized);
        Assert.Equal(12, runtime.MaximumOverlays);
        Assert.True(runtime.Initialize().Succeeded);
        Assert.Equal(1, backend.CreateCalls);

        Assert.True(runtime.PublishOverlay("PlotJumpInfo", ValidFrame(), ValidCalibration(), 0.75f).Succeeded);
        Assert.True(runtime.CompleteFrame().Succeeded);
        Assert.NotNull(backend.LastPacket);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(backend.LastPacket.AsSpan(24)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(backend.LastPacket.AsSpan(28)));

        Assert.True(runtime.TryGetHeadPose(out VrOverlayHeadPose? pose));
        Assert.Equal(new Vector3(0.1f, 0.2f, -0.3f), pose!.Position);
        Assert.Equal(0.5f, pose.Yaw);
        Assert.Equal(0u, backend.LastRequestedOriginRevision);

        Assert.True(runtime.ResetOrientation().Succeeded);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(backend.LastPacket.AsSpan(28)));

        runtime.RemoveOverlay("PlotJumpInfo");
        Assert.True(runtime.CompleteFrame().Succeeded);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(backend.LastPacket.AsSpan(24)));

        Assert.True(runtime.SetInteractionEnabled(false).Succeeded);
        Assert.False(runtime.SetInteractionEnabled(true).Succeeded);
        Assert.Empty(runtime.PollPointerEvents());

        // Live probe stays ready while native session is Ok, then shuts down cleanly on SessionLost.
        backend.SessionStatus = new(LinuxOpenXrNativeStatus.Ok, 0, 0, 8, "Active");
        Assert.True(runtime.Probe().HeadsetPresent);
        Assert.Equal(8, runtime.MaximumOverlays);

        backend.SessionStatus = new(LinuxOpenXrNativeStatus.SessionLost, 0, 0, 8, "WiVRn stopped");
        VrRuntimeProbe lostProbe = runtime.Probe();
        Assert.False(lostProbe.HeadsetPresent);
        Assert.Contains("WiVRn stopped", lostProbe.Message);
        Assert.False(runtime.IsInitialized);
        Assert.Equal(1, backend.DestroyCalls);
    }

    [Fact]
    public void RuntimeHandlesInitializationAndPacketEncodingFailuresCleanly()
    {
        var backend = new StubBackend
        {
            CreateOutcome = new(nint.Zero, LinuxOpenXrNativeStatus.Error, -12, "Session creation failed"),
        };
        using var runtime = new LinuxOpenXrOverlayRuntime(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId,
            backend,
            ActiveLinuxInspector("wivrn-server")
        );
        VrRuntimeResult failedInit = runtime.Initialize();
        Assert.False(failedInit.Succeeded);
        Assert.Contains("Session creation failed", failedInit.Message);

        backend.CreateOutcome = new(new nint(77), LinuxOpenXrNativeStatus.Ok, 0, "Created");
        Assert.True(runtime.Initialize().Succeeded);

        // Invalid frame fails encode gracefully without crashing.
        Assert.True(runtime.PublishOverlay("Bad", new VrOverlayFrame(0, 0, []), ValidCalibration(), 1f).Succeeded);
        Assert.False(runtime.CompleteFrame().Succeeded);

        // Native publish rejection returns failure.
        runtime.RemoveOverlay("Bad");
        backend.PublishResult = false;
        Assert.False(runtime.CompleteFrame().Succeeded);

        // Preflight failure during Probe shuts down an active session.
        var inactive = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: _ => null,
            fileExists: _ => false,
            isProcessRunning: _ => false
        );
        using var blockedRuntime = new LinuxOpenXrOverlayRuntime(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId,
            backend,
            inactive
        );
        Assert.False(blockedRuntime.Probe().HeadsetPresent);
        Assert.False(blockedRuntime.Initialize().Succeeded);
    }

    [Fact]
    public void NativeBackendHandlesMissingLibraryAndExercisesBuiltLinuxHelperWhenPresent()
    {
        var missingInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            fileExists: _ => false
        );
        var missing = new NativeLinuxOpenXrOverlayBackend(missingInspector);
        Assert.False(missing.IsAvailable);
        Assert.Equal(LinuxOpenXrNativeStatus.LoaderMissing, missing.Probe("wivrn").Status);
        Assert.Equal(LinuxOpenXrNativeStatus.LoaderMissing, missing.Create("wivrn").Status);
        Assert.False(missing.Publish(nint.Zero, [1, 2, 3]));
        Assert.Equal(LinuxOpenXrNativeStatus.SessionLost, missing.GetStatus(nint.Zero).Status);
        Assert.False(missing.TryGetHeadPose(nint.Zero, 0, out _));
        missing.Destroy(nint.Zero);

        string builtLibrary = Path.Combine(AppContext.BaseDirectory, "OpenXR", "libSrvSurveyLinuxOpenXrOverlay.so");
        if (!OperatingSystem.IsLinux() || !File.Exists(builtLibrary))
        {
            return;
        }

        var presentInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key =>
                key == LinuxVrRuntimeInspector.LinuxOpenXrLibraryEnvironmentVariable ? builtLibrary : null,
            fileExists: File.Exists
        );
        var native = new NativeLinuxOpenXrOverlayBackend(presentInspector);
        Assert.True(native.IsAvailable);
        Assert.True(native.IsAvailable);
        LinuxOpenXrProbeOutcome probe = native.Probe("wivrn");
        Assert.NotEmpty(probe.Message);
        Assert.False(native.Publish(nint.Zero, [1, 2, 3]));
        Assert.Equal(LinuxOpenXrNativeStatus.SessionLost, native.GetStatus(nint.Zero).Status);
        Assert.False(native.TryGetHeadPose(nint.Zero, 0, out _));
        native.Destroy(nint.Zero);
    }

    private static LinuxVrRuntimeInspector ActiveLinuxInspector(string processName) =>
        new(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: _ => null,
            fileExists: _ => false,
            isProcessRunning: name => name == processName
        );

    private static VrOverlayFrame ValidFrame() => new(2, 1, [255, 128, 64, 255, 0, 10, 20, 255]);

    private static VrOverlayCalibration ValidCalibration() => new(8, new Vector3(0, 0, 10), Vector3.Zero);

    private sealed class StubBackend : ILinuxOpenXrOverlayBackend
    {
        public bool IsAvailable { get; init; } = true;
        public LinuxOpenXrProbeOutcome ProbeOutcome { get; set; } = new(LinuxOpenXrNativeStatus.Ok, 0, "Ready");
        public LinuxOpenXrCreateOutcome CreateOutcome { get; set; } =
            new(new nint(1), LinuxOpenXrNativeStatus.Ok, 0, "Created");
        public LinuxOpenXrSessionStatus SessionStatus { get; set; } =
            new(LinuxOpenXrNativeStatus.Ok, 0, 0, 16, "Active");
        public VrOverlayHeadPose? HeadPose { get; set; }
        public bool PublishResult { get; set; } = true;
        public int CreateCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        public byte[]? LastPacket { get; private set; }
        public uint LastRequestedOriginRevision { get; private set; }

        public LinuxOpenXrProbeOutcome Probe(string expectedRuntime) => ProbeOutcome;

        public LinuxOpenXrCreateOutcome Create(string expectedRuntime)
        {
            CreateCalls++;
            return CreateOutcome;
        }

        public bool Publish(nint session, byte[] packet)
        {
            LastPacket = packet;
            return PublishResult;
        }

        public LinuxOpenXrSessionStatus GetStatus(nint session) => SessionStatus;

        public bool TryGetHeadPose(nint session, uint expectedOriginRevision, out VrOverlayHeadPose? pose)
        {
            LastRequestedOriginRevision = expectedOriginRevision;
            pose = HeadPose;
            return pose is not null;
        }

        public void Destroy(nint session) => DestroyCalls++;
    }
}
