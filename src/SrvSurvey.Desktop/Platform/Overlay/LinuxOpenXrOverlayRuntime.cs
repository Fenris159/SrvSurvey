using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum LinuxOpenXrNativeStatus
{
    Ok = 0,
    LoaderMissing = 1,
    VulkanMissing = 2,
    OverlayExtensionUnsupported = 3,
    GraphicsExtensionUnsupported = 4,
    WrongRuntime = 5,
    HeadsetUnavailable = 6,
    SessionLost = 7,
    Error = 8,
}

public sealed record LinuxOpenXrProbeOutcome(LinuxOpenXrNativeStatus Status, int XrError, string Message);

public sealed record LinuxOpenXrCreateOutcome(nint Handle, LinuxOpenXrNativeStatus Status, int XrError, string Message);

public sealed record LinuxOpenXrSessionStatus(
    LinuxOpenXrNativeStatus Status,
    int XrError,
    uint RenderedCount,
    uint MaxLayers,
    string Message
);

public interface ILinuxOpenXrOverlayBackend
{
    bool IsAvailable { get; }

    LinuxOpenXrProbeOutcome Probe(string expectedRuntime);

    LinuxOpenXrCreateOutcome Create(string expectedRuntime);

    bool Publish(nint session, byte[] packet);

    LinuxOpenXrSessionStatus GetStatus(nint session);

    bool TryGetHeadPose(nint session, uint expectedOriginRevision, out VrOverlayHeadPose? pose);

    void Destroy(nint session);
}

/// <summary>Direct Linux OpenXR overlay runtime using XR_EXTX_overlay and headless Vulkan for WiVRn and Monado.</summary>
public sealed class LinuxOpenXrOverlayRuntime : IVrOverlayRuntime
{
    private static readonly IReadOnlyDictionary<string, OpenXrOverlayFrame> EmptyFrames =
        new Dictionary<string, OpenXrOverlayFrame>();
    private readonly string profileId;
    private readonly string expectedRuntime;
    private readonly ILinuxOpenXrOverlayBackend backend;
    private readonly LinuxVrRuntimeInspector inspector;
    private readonly Dictionary<string, OpenXrOverlayFrame> frames = new(StringComparer.Ordinal);
    private nint sessionHandle;
    private uint originRevision;
    private int maximumOverlays = OpenXrFramePacket.MaximumOverlays;

    public LinuxOpenXrOverlayRuntime(
        string profileId,
        ILinuxOpenXrOverlayBackend? backend = null,
        LinuxVrRuntimeInspector? inspector = null
    )
    {
        this.profileId = profileId;
        expectedRuntime = profileId == VrPlatformProfileCatalog.MonadoOpenXrProfileId ? "monado" : "wivrn";
        this.inspector = inspector ?? LinuxVrRuntimeInspector.Default;
        this.backend = backend ?? new NativeLinuxOpenXrOverlayBackend(this.inspector);
    }

    public bool IsInitialized => sessionHandle != nint.Zero;

    public string DisplayName =>
        profileId == VrPlatformProfileCatalog.MonadoOpenXrProfileId ? "Monado / OpenXR" : "WiVRn / OpenXR";

    public int MaximumOverlays => maximumOverlays;

    public VrRuntimeProbe Probe()
    {
        VrRuntimeProbe? preflight = inspector.CheckLinuxOpenXrPreflight(profileId);
        if (preflight is not null)
        {
            Shutdown();
            return preflight;
        }

        if (!backend.IsAvailable)
        {
            Shutdown();
            return VrRuntimeProbe.RuntimeUnavailable(
                "Packaged Linux OpenXR overlay support (libSrvSurveyLinuxOpenXrOverlay.so) is unavailable. Reinstall SrvSurvey or verify that the package matches your system architecture."
            );
        }

        if (IsInitialized)
        {
            LinuxOpenXrSessionStatus live = backend.GetStatus(sessionHandle);
            if (live.Status == LinuxOpenXrNativeStatus.Ok)
            {
                maximumOverlays = Math.Clamp((int)live.MaxLayers, 1, OpenXrFramePacket.MaximumOverlays);
                return VrRuntimeProbe.Ready();
            }

            Shutdown();
            return VrRuntimeProbe.HeadsetUnavailable(
                string.IsNullOrWhiteSpace(live.Message)
                    ? $"The {DisplayName} overlay session disconnected."
                    : live.Message
            );
        }

        LinuxOpenXrProbeOutcome outcome = backend.Probe(expectedRuntime);
        return outcome.Status switch
        {
            LinuxOpenXrNativeStatus.Ok => VrRuntimeProbe.Ready(),
            LinuxOpenXrNativeStatus.LoaderMissing or LinuxOpenXrNativeStatus.VulkanMissing =>
                VrRuntimeProbe.RuntimeUnavailable(outcome.Message),
            _ => VrRuntimeProbe.HeadsetUnavailable(outcome.Message),
        };
    }

    public VrRuntimeResult Initialize()
    {
        if (IsInitialized)
        {
            return VrRuntimeResult.Success($"{DisplayName} overlay session is active.");
        }

        VrRuntimeProbe? preflight = inspector.CheckLinuxOpenXrPreflight(profileId);
        if (preflight is not null)
        {
            return VrRuntimeResult.Failure(preflight.Message);
        }

        if (!backend.IsAvailable)
        {
            return VrRuntimeResult.Failure(
                "Packaged Linux OpenXR overlay support (libSrvSurveyLinuxOpenXrOverlay.so) is unavailable."
            );
        }

        LinuxOpenXrCreateOutcome created = backend.Create(expectedRuntime);
        if (created.Status != LinuxOpenXrNativeStatus.Ok || created.Handle == nint.Zero)
        {
            return VrRuntimeResult.Failure(
                string.IsNullOrWhiteSpace(created.Message)
                    ? $"Could not initialize the {DisplayName} overlay session (OpenXR error {created.XrError})."
                    : created.Message
            );
        }

        sessionHandle = created.Handle;
        LinuxOpenXrSessionStatus status = backend.GetStatus(sessionHandle);
        if (status.Status != LinuxOpenXrNativeStatus.Ok)
        {
            Shutdown();
            return VrRuntimeResult.Failure(
                string.IsNullOrWhiteSpace(status.Message)
                    ? $"The {DisplayName} overlay session ended during startup."
                    : status.Message
            );
        }

        if (status.MaxLayers > 0)
        {
            maximumOverlays = Math.Clamp((int)status.MaxLayers, 1, OpenXrFramePacket.MaximumOverlays);
        }

        _ = CompleteFrame();
        return VrRuntimeResult.Success($"{DisplayName} overlay session is active.");
    }

    public VrRuntimeResult PublishOverlay(
        string plotterName,
        VrOverlayFrame frame,
        VrOverlayCalibration calibration,
        float alpha
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(calibration);
        if (!IsInitialized)
        {
            return VrRuntimeResult.Failure($"{DisplayName} overlay session is not active.");
        }

        frames[plotterName] = new OpenXrOverlayFrame(frame, calibration, alpha);
        return VrRuntimeResult.Success($"Queued {plotterName} for {DisplayName}.");
    }

    public VrRuntimeResult CompleteFrame()
    {
        if (!IsInitialized)
        {
            return VrRuntimeResult.Failure($"{DisplayName} overlay session is not active.");
        }

        try
        {
            byte[] packet = OpenXrFramePacket.Encode(frames, originRevision);
            return backend.Publish(sessionHandle, packet)
                ? VrRuntimeResult.Success($"Published overlay frames to {DisplayName}.")
                : VrRuntimeResult.Failure($"{DisplayName} rejected the overlay frame packet.");
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            byte[] emptyPacket = OpenXrFramePacket.Encode(EmptyFrames, originRevision);
            _ = backend.Publish(sessionHandle, emptyPacket);
            return VrRuntimeResult.Failure($"Could not encode overlay frames for {DisplayName}: {exception.Message}");
        }
    }

    public void RemoveOverlay(string plotterName)
    {
        frames.Remove(plotterName);
    }

    public VrRuntimeResult ResetOrientation()
    {
        if (!IsInitialized)
        {
            return VrRuntimeResult.Failure($"Connect {DisplayName} before resetting the overlay origin.");
        }

        originRevision++;
        VrRuntimeResult result = CompleteFrame();
        return result.Succeeded
            ? VrRuntimeResult.Success("Recentered the Linux OpenXR overlay origin to the current headset pose.")
            : result;
    }

    public bool TryGetHeadPose(out VrOverlayHeadPose? pose)
    {
        pose = null;
        return IsInitialized && backend.TryGetHeadPose(sessionHandle, originRevision, out pose);
    }

    public VrRuntimeResult SetInteractionEnabled(bool enabled)
    {
        return enabled
            ? VrRuntimeResult.Failure(
                "Controller-pointer interaction is not yet supported on Linux OpenXR (WiVRn/Monado). Use desktop VR settings or Adjust overlays."
            )
            : VrRuntimeResult.Success("VR overlays are click-through.");
    }

    public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents() => [];

    public void Shutdown()
    {
        if (sessionHandle != nint.Zero)
        {
            backend.Destroy(sessionHandle);
            sessionHandle = nint.Zero;
        }

        frames.Clear();
        maximumOverlays = OpenXrFramePacket.MaximumOverlays;
    }

    public void Dispose()
    {
        Shutdown();
    }
}

internal sealed class NativeLinuxOpenXrOverlayBackend : ILinuxOpenXrOverlayBackend
{
    private const int MessageBufferBytes = 512;
    private readonly LinuxVrRuntimeInspector inspector;
    private readonly Lock loadLock = new();
    private nint libraryHandle;
    private ProbeDelegate? probeFn;
    private CreateDelegate? createFn;
    private PublishDelegate? publishFn;
    private GetStatusDelegate? getStatusFn;
    private GetHeadPoseDelegate? getHeadPoseFn;
    private DestroyDelegate? destroyFn;

    public NativeLinuxOpenXrOverlayBackend(LinuxVrRuntimeInspector inspector)
    {
        this.inspector = inspector;
    }

    public bool IsAvailable => EnsureLoaded();

    public LinuxOpenXrProbeOutcome Probe(string expectedRuntime)
    {
        if (!EnsureLoaded() || probeFn is null)
        {
            return new(
                LinuxOpenXrNativeStatus.LoaderMissing,
                0,
                "Packaged Linux OpenXR overlay library is unavailable."
            );
        }

        byte[] buffer = new byte[MessageBufferBytes];
        _ = probeFn(expectedRuntime, out int status, out int xrError, buffer, buffer.Length);
        return new((LinuxOpenXrNativeStatus)status, xrError, DecodeUtf8Z(buffer));
    }

    public LinuxOpenXrCreateOutcome Create(string expectedRuntime)
    {
        if (!EnsureLoaded() || createFn is null)
        {
            return new(
                nint.Zero,
                LinuxOpenXrNativeStatus.LoaderMissing,
                0,
                "Packaged Linux OpenXR overlay library is unavailable."
            );
        }

        byte[] buffer = new byte[MessageBufferBytes];
        nint handle = createFn(expectedRuntime, out int status, out int xrError, buffer, buffer.Length);
        return new(handle, (LinuxOpenXrNativeStatus)status, xrError, DecodeUtf8Z(buffer));
    }

    public bool Publish(nint session, byte[] packet)
    {
        return EnsureLoaded()
            && publishFn is not null
            && session != nint.Zero
            && publishFn(session, packet, (uint)packet.Length) != 0;
    }

    public LinuxOpenXrSessionStatus GetStatus(nint session)
    {
        if (!EnsureLoaded() || getStatusFn is null || session == nint.Zero)
        {
            return new(
                LinuxOpenXrNativeStatus.SessionLost,
                0,
                0,
                OpenXrFramePacket.MaximumOverlays,
                "Linux OpenXR overlay session is not active."
            );
        }

        byte[] buffer = new byte[MessageBufferBytes];
        _ = getStatusFn(
            session,
            out int status,
            out int xrError,
            out uint renderedCount,
            out uint maxLayers,
            buffer,
            buffer.Length
        );
        return new((LinuxOpenXrNativeStatus)status, xrError, renderedCount, maxLayers, DecodeUtf8Z(buffer));
    }

    public bool TryGetHeadPose(nint session, uint expectedOriginRevision, out VrOverlayHeadPose? pose)
    {
        pose = null;
        if (!EnsureLoaded() || getHeadPoseFn is null || session == nint.Zero)
        {
            return false;
        }

        if (
            getHeadPoseFn(session, expectedOriginRevision, out float x, out float y, out float z, out float yaw) == 0
            || !float.IsFinite(x)
            || !float.IsFinite(y)
            || !float.IsFinite(z)
            || !float.IsFinite(yaw)
        )
        {
            return false;
        }

        pose = new VrOverlayHeadPose(new Vector3(x, y, z), yaw);
        return true;
    }

    public void Destroy(nint session)
    {
        if (session != nint.Zero && destroyFn is not null)
        {
            destroyFn(session);
        }
    }

    private bool EnsureLoaded()
    {
        lock (loadLock)
        {
            if (libraryHandle != nint.Zero)
            {
                return true;
            }

            string? path = inspector.FindOverlayLibrary();
            if (path is null || !NativeLibrary.TryLoad(path, out nint handle))
            {
                return false;
            }

            if (
                !TryBind(handle, "srvsurvey_linux_openxr_probe", out probeFn)
                || !TryBind(handle, "srvsurvey_linux_openxr_create", out createFn)
                || !TryBind(handle, "srvsurvey_linux_openxr_publish", out publishFn)
                || !TryBind(handle, "srvsurvey_linux_openxr_get_status", out getStatusFn)
                || !TryBind(handle, "srvsurvey_linux_openxr_get_head_pose", out getHeadPoseFn)
                || !TryBind(handle, "srvsurvey_linux_openxr_destroy", out destroyFn)
            )
            {
                NativeLibrary.Free(handle);
                return false;
            }

            libraryHandle = handle;
            return true;
        }
    }

    private static bool TryBind<T>(nint handle, string symbol, out T? target)
        where T : Delegate
    {
        if (NativeLibrary.TryGetExport(handle, symbol, out nint address))
        {
            target = Marshal.GetDelegateForFunctionPointer<T>(address);
            return true;
        }

        target = null;
        return false;
    }

    private static string DecodeUtf8Z(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProbeDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string expectedRuntime,
        out int outStatus,
        out int outXrError,
        [Out] byte[] outMessage,
        int messageCapacity
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string expectedRuntime,
        out int outStatus,
        out int outXrError,
        [Out] byte[] outMessage,
        int messageCapacity
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PublishDelegate(nint handle, [In] byte[] packet, uint packetLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetStatusDelegate(
        nint handle,
        out int outState,
        out int outXrError,
        out uint outRenderedCount,
        out uint outMaxLayers,
        [Out] byte[] outMessage,
        int messageCapacity
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetHeadPoseDelegate(
        nint handle,
        uint expectedOriginRevision,
        out float outPosX,
        out float outPosY,
        out float outPosZ,
        out float outYaw
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyDelegate(nint handle);
}
