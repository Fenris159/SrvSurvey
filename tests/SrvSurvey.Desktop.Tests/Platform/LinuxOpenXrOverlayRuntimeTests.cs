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
        Assert.True(runtime.SetInteractionEnabled(true).Succeeded);
        Assert.Empty(runtime.PollPointerEvents());
        Assert.True(runtime.SetInteractionEnabled(false).Succeeded);

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
        // Startup status check tears down the session if it already entered a non-Ok state.
        backend.SessionStatus = new(LinuxOpenXrNativeStatus.SessionLost, 0, 0, 16, "Session ended immediately");
        VrRuntimeResult earlyLoss = runtime.Initialize();
        Assert.False(earlyLoss.Succeeded);
        Assert.False(runtime.IsInitialized);
        Assert.Contains("Session ended immediately", earlyLoss.Message);
        Assert.Equal(1, backend.DestroyCalls);

        backend.SessionStatus = new(LinuxOpenXrNativeStatus.Error, 0, 0, 16, " ");
        VrRuntimeResult blankEarlyLoss = runtime.Initialize();
        Assert.False(blankEarlyLoss.Succeeded);
        Assert.Contains("ended during startup", blankEarlyLoss.Message);
        Assert.Equal(2, backend.DestroyCalls);

        backend.SessionStatus = new(LinuxOpenXrNativeStatus.Ok, 0, 0, 16, "Active");
        Assert.True(runtime.Initialize().Succeeded);

        // Publish a valid panel, then remove it while another panel fails encoding: native submission must be cleared.
        Assert.True(runtime.PublishOverlay("Good", ValidFrame(), ValidCalibration(), 1f).Succeeded);
        Assert.True(runtime.CompleteFrame().Succeeded);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(backend.LastPacket.AsSpan(24)));

        runtime.RemoveOverlay("Good");
        Assert.True(runtime.PublishOverlay("Bad", new VrOverlayFrame(0, 0, []), ValidCalibration(), 1f).Succeeded);
        Assert.False(runtime.CompleteFrame().Succeeded);
        Assert.NotNull(backend.LastPacket);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(backend.LastPacket.AsSpan(24)));

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
    public void InteractionEnablesDisablesPollsScaledPointerEventsAndHandlesFailures()
    {
        var backend = new StubBackend
        {
            CreateOutcome = new(new nint(456), LinuxOpenXrNativeStatus.Ok, 0, "Created"),
            SessionStatus = new(LinuxOpenXrNativeStatus.Ok, 0, 1, 16, "Active"),
        };
        using var runtime = new LinuxOpenXrOverlayRuntime(
            VrPlatformProfileCatalog.MonadoOpenXrProfileId,
            backend,
            ActiveLinuxInspector("monado-service")
        );

        // Before initialization, enabling interaction fails cleanly while disabling is a safe no-op.
        Assert.False(runtime.SetInteractionEnabled(true).Succeeded);
        Assert.True(runtime.SetInteractionEnabled(false).Succeeded);
        Assert.Empty(runtime.PollPointerEvents());
        Assert.Equal(0, backend.SetInteractionCalls);

        Assert.True(runtime.Initialize().Succeeded);

        // Native failure surfaces native message or fallback with OpenXR error code.
        backend.InteractionOutcome = new(false, -15, "Controller actions unavailable");
        VrRuntimeResult failedWithMessage = runtime.SetInteractionEnabled(true);
        Assert.False(failedWithMessage.Succeeded);
        Assert.Contains("Controller actions unavailable", failedWithMessage.Message);
        Assert.Empty(runtime.PollPointerEvents());

        backend.InteractionOutcome = new(false, -15, " ");
        VrRuntimeResult failedFallback = runtime.SetInteractionEnabled(true);
        Assert.False(failedFallback.Succeeded);
        Assert.Contains("-15", failedFallback.Message);

        // Native success supports custom and fallback messages.
        backend.InteractionOutcome = new(true, 0, " ");
        VrRuntimeResult enabledFallback = runtime.SetInteractionEnabled(true);
        Assert.True(enabledFallback.Succeeded);
        Assert.Contains("Monado / OpenXR", enabledFallback.Message);

        backend.InteractionOutcome = new(true, 0, "Pointer active");
        VrRuntimeResult enabledCustom = runtime.SetInteractionEnabled(true);
        Assert.True(enabledCustom.Succeeded);
        Assert.Equal("Pointer active", enabledCustom.Message);

        // Publish panels with explicit PointerWidth/PointerHeight and fallback Width/Height.
        var scaledFrame = new VrOverlayFrame(2, 1, [255, 0, 0, 255, 0, 255, 0, 255], 400, 200);
        Assert.True(runtime.PublishOverlay("PlotAdjustVR", scaledFrame, ValidCalibration(), 1f).Succeeded);
        Assert.True(runtime.PublishOverlay("PlotJumpInfo", ValidFrame(), ValidCalibration(), 1f).Succeeded);

        ulong adjustId = OpenXrFramePacket.GetOverlayId("PlotAdjustVR");
        ulong jumpId = OpenXrFramePacket.GetOverlayId("PlotJumpInfo");
        backend.PointerEvents =
        [
            new OpenXrPointerEvent(adjustId, VrOverlayPointerEventKind.Move, 0.5f, 0.25f, 0, 0),
            new OpenXrPointerEvent(jumpId, VrOverlayPointerEventKind.Wheel, 0.5f, 1.0f, 0.25f, -0.5f),
            new OpenXrPointerEvent(999999UL, VrOverlayPointerEventKind.LeftButtonDown, 0.1f, 0.1f, 0, 0),
            new OpenXrPointerEvent(0UL, VrOverlayPointerEventKind.Reset, 0, 0, 0, 0),
        ];

        IReadOnlyList<VrOverlayPointerEvent> polled = runtime.PollPointerEvents();
        Assert.Equal(4, polled.Count);
        Assert.Equal("PlotAdjustVR", polled[0].PlotterName);
        Assert.Equal(VrOverlayPointerEventKind.Move, polled[0].Kind);
        Assert.Equal(200, polled[0].X, 3);
        Assert.Equal(50, polled[0].Y, 3);

        Assert.Equal("PlotJumpInfo", polled[1].PlotterName);
        Assert.Equal(VrOverlayPointerEventKind.Wheel, polled[1].Kind);
        Assert.Equal(1, polled[1].X, 3);
        Assert.Equal(1, polled[1].Y, 3);
        Assert.Equal(0.25, polled[1].WheelX, 3);
        Assert.Equal(-0.5, polled[1].WheelY, 3);

        Assert.Equal(VrOverlayPointerEventKind.Reset, polled[2].Kind);
        Assert.Equal(string.Empty, polled[2].PlotterName);
        Assert.Equal(VrOverlayPointerEventKind.Reset, polled[3].Kind);

        // Removing an overlay invalidates its identity and yields Reset if a stale event arrives.
        runtime.RemoveOverlay("PlotJumpInfo");
        backend.PointerEvents = [new OpenXrPointerEvent(jumpId, VrOverlayPointerEventKind.Move, 0.5f, 0.5f, 0, 0)];
        IReadOnlyList<VrOverlayPointerEvent> afterRemove = runtime.PollPointerEvents();
        Assert.Single(afterRemove);
        Assert.Equal(VrOverlayPointerEventKind.Reset, afterRemove[0].Kind);

        // Disabling interaction forwards false to the backend and stops polling.
        Assert.True(runtime.SetInteractionEnabled(false).Succeeded);
        Assert.False(backend.LastInteractionEnabled);
        Assert.Empty(runtime.PollPointerEvents());

        // Shutdown clears active interaction state.
        Assert.True(runtime.SetInteractionEnabled(true).Succeeded);
        runtime.Shutdown();
        Assert.Empty(runtime.PollPointerEvents());
    }

    [Fact]
    public void NativePointerEventConversionFiltersInvalidAndOutOfRangeValues()
    {
        Assert.Empty(NativeLinuxOpenXrOverlayBackend.ConvertPointerEvents([]));

        NativeLinuxOpenXrOverlayBackend.NativePointerEvent[] raw =
        [
            new()
            {
                Id = 1,
                Kind = (uint)VrOverlayPointerEventKind.Move,
                X = 0.5f,
                Y = 0.25f,
            },
            new()
            {
                Id = 2,
                Kind = (uint)VrOverlayPointerEventKind.Reset,
                X = float.NaN,
                Y = float.NaN,
            },
            new()
            {
                Id = 3,
                Kind = 99u,
                X = 0.5f,
                Y = 0.5f,
            },
            new()
            {
                Id = 4,
                Kind = (uint)VrOverlayPointerEventKind.Move,
                X = float.NaN,
                Y = 0.5f,
            },
            new()
            {
                Id = 5,
                Kind = (uint)VrOverlayPointerEventKind.Move,
                X = 0.5f,
                Y = 11.0f,
            },
            new()
            {
                Id = 6,
                Kind = (uint)VrOverlayPointerEventKind.Wheel,
                X = 0.5f,
                Y = 0.5f,
                WheelX = 9.0f,
            },
            new()
            {
                Id = 7,
                Kind = (uint)VrOverlayPointerEventKind.Wheel,
                X = 0.5f,
                Y = 0.5f,
                WheelY = float.PositiveInfinity,
            },
        ];

        IReadOnlyList<OpenXrPointerEvent> converted = NativeLinuxOpenXrOverlayBackend.ConvertPointerEvents(raw);
        Assert.Equal(2, converted.Count);
        Assert.Equal(1UL, converted[0].Id);
        Assert.Equal(VrOverlayPointerEventKind.Move, converted[0].Kind);
        Assert.Equal(0UL, converted[1].Id);
        Assert.Equal(VrOverlayPointerEventKind.Reset, converted[1].Kind);
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
        Assert.False(missing.SetInteractionEnabled(nint.Zero, true).Succeeded);
        Assert.Empty(missing.PollPointerEvents(nint.Zero));
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
        Assert.False(native.SetInteractionEnabled(nint.Zero, true).Succeeded);
        Assert.Empty(native.PollPointerEvents(nint.Zero));
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
        public LinuxOpenXrInteractionOutcome InteractionOutcome { get; set; } = new(true, 0, "Enabled");
        public IReadOnlyList<OpenXrPointerEvent> PointerEvents { get; set; } = [];
        public VrOverlayHeadPose? HeadPose { get; set; }
        public bool PublishResult { get; set; } = true;
        public int CreateCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        public int SetInteractionCalls { get; private set; }
        public bool LastInteractionEnabled { get; private set; }
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

        public LinuxOpenXrInteractionOutcome SetInteractionEnabled(nint session, bool enabled)
        {
            SetInteractionCalls++;
            LastInteractionEnabled = enabled;
            return enabled ? InteractionOutcome : new(true, 0, "Disabled");
        }

        public IReadOnlyList<OpenXrPointerEvent> PollPointerEvents(nint session) => PointerEvents;

        public void Destroy(nint session) => DestroyCalls++;
    }
}
