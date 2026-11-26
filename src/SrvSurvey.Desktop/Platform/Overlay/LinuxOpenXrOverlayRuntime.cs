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

public sealed record LinuxOpenXrInteractionOutcome(bool Succeeded, int XrError, string Message);

public interface ILinuxOpenXrOverlayBackend
{
    bool IsAvailable { get; }

    LinuxOpenXrProbeOutcome Probe(string expectedRuntime);

    LinuxOpenXrCreateOutcome Create(string expectedRuntime);

    bool Publish(nint session, byte[] packet);

    LinuxOpenXrSessionStatus GetStatus(nint session);

    bool TryGetHeadPose(nint session, uint expectedOriginRevision, out VrOverlayHeadPose? pose);

    LinuxOpenXrInteractionOutcome SetInteractionEnabled(nint session, bool enabled);

    IReadOnlyList<OpenXrPointerEvent> PollPointerEvents(nint session);

    void Destroy(nint session);
}

/// <summary>Direct Linux OpenXR overlay runtime using XR_EXTX_overlay and headless Vulkan for WiVRn and Monado.</summary>
public sealed class LinuxOpenXrOverlayRuntime : IVrOverlayRuntime
{
    private static readonly IReadOnlyDictionary<string, OpenXrOverlayFrame> EmptyFrames =
        new Dictionary<string, OpenXrOverlayFrame>();
    private static readonly EliteGameProcessDetector DefaultGameDetector = new();
    private readonly string profileId;
    private readonly string expectedRuntime;
    private readonly ILinuxOpenXrOverlayBackend backend;
    private readonly LinuxVrRuntimeInspector inspector;
    private readonly Func<bool> isGameRunning;
    private readonly Dictionary<string, OpenXrOverlayFrame> frames = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, string> identities = [];
    private readonly List<VrOverlayPointerEvent> bufferedEvents = [];
    private nint sessionHandle;
    private uint originRevision;
    private bool interactionEnabled;
    private bool pendingInteractionReset;
    private ulong? activePointerOverlayId;
    private int maximumOverlays = OpenXrFramePacket.MaximumOverlays;
    private int nextFrameSortOrder;

    public LinuxOpenXrOverlayRuntime(
        string profileId,
        ILinuxOpenXrOverlayBackend? backend = null,
        LinuxVrRuntimeInspector? inspector = null,
        Func<bool>? isGameRunning = null
    )
    {
        this.profileId = profileId;
        expectedRuntime = profileId == VrPlatformProfileCatalog.MonadoOpenXrProfileId ? "monado" : "wivrn";
        this.inspector = inspector ?? LinuxVrRuntimeInspector.Default;
        this.backend = backend ?? new NativeLinuxOpenXrOverlayBackend(this.inspector);
        this.isGameRunning = isGameRunning ?? DefaultGameDetector.IsRunning;
    }

    public bool IsInitialized => sessionHandle != nint.Zero;

    public string DisplayName =>
        profileId == VrPlatformProfileCatalog.MonadoOpenXrProfileId ? "Monado / OpenXR" : "WiVRn / OpenXR";

    public int MaximumOverlays => interactionEnabled && maximumOverlays > 1 ? maximumOverlays - 1 : maximumOverlays;

    private string WaitingForGameMessage =>
        profileId == VrPlatformProfileCatalog.MonadoOpenXrProfileId
            ? "Waiting for Elite Dangerous to start. SrvSurvey attaches to Monado once the game is running."
            : "Waiting for Elite Dangerous to start. SrvSurvey attaches to WiVRn once the game is running so WiVRn's lobby stays usable.";

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

        if (!isGameRunning())
        {
            Shutdown();
            return VrRuntimeProbe.HeadsetUnavailable(WaitingForGameMessage);
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
            if (!isGameRunning())
            {
                Shutdown();
                return VrRuntimeResult.Failure(WaitingForGameMessage);
            }

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

        if (!isGameRunning())
        {
            return VrRuntimeResult.Failure(WaitingForGameMessage);
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

        if (
            frames.TryGetValue(plotterName, out OpenXrOverlayFrame? previous)
            && HasDifferentPointerBasis(previous.Frame, frame)
        )
        {
            InvalidateOverlayInteraction(plotterName);
        }

        identities[OpenXrFramePacket.GetOverlayId(plotterName)] = plotterName;
        frames[plotterName] = new OpenXrOverlayFrame(frame, calibration, alpha, nextFrameSortOrder++);
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
        finally
        {
            nextFrameSortOrder = 0;
        }
    }

    public void RemoveOverlay(string plotterName)
    {
        if (frames.Remove(plotterName))
        {
            InvalidateOverlayInteraction(plotterName);
        }

        identities.Remove(OpenXrFramePacket.GetOverlayId(plotterName));
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
        if (!enabled)
        {
            interactionEnabled = false;
            ClearInteractionBuffers();
            if (IsInitialized)
            {
                _ = backend.SetInteractionEnabled(sessionHandle, false);
            }
            return VrRuntimeResult.Success("VR overlays are click-through again.");
        }

        if (!IsInitialized)
        {
            return VrRuntimeResult.Failure($"Connect {DisplayName} before enabling controller interaction.");
        }

        LinuxOpenXrInteractionOutcome outcome = backend.SetInteractionEnabled(sessionHandle, true);
        if (!outcome.Succeeded)
        {
            interactionEnabled = false;
            ClearInteractionBuffers();
            return VrRuntimeResult.Failure(
                string.IsNullOrWhiteSpace(outcome.Message)
                    ? $"Controller-pointer interaction is unavailable on {DisplayName} (OpenXR error {outcome.XrError})."
                    : outcome.Message
            );
        }

        interactionEnabled = true;
        ClearInteractionBuffers();
        return VrRuntimeResult.Success(
            string.IsNullOrWhiteSpace(outcome.Message)
                ? $"{DisplayName} controller interaction is enabled. Point at a panel and use the trigger to click."
                : outcome.Message
        );
    }

    public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents()
    {
        if (!interactionEnabled || !IsInitialized)
        {
            return [];
        }

        IReadOnlyList<OpenXrPointerEvent> events = backend.PollPointerEvents(sessionHandle);
        bool prependReset = pendingInteractionReset;
        pendingInteractionReset = false;
        if (events.Count == 0 && bufferedEvents.Count == 0 && !prependReset)
        {
            return [];
        }

        var result = new List<VrOverlayPointerEvent>(events.Count + bufferedEvents.Count + (prependReset ? 1 : 0));
        if (prependReset)
        {
            result.Add(new VrOverlayPointerEvent(string.Empty, VrOverlayPointerEventKind.Reset, 0, 0));
        }

        if (bufferedEvents.Count > 0)
        {
            result.AddRange(bufferedEvents);
            bufferedEvents.Clear();
        }

        foreach (OpenXrPointerEvent pointer in events)
        {
            AppendPointerEvent(result, pointer);
        }

        return result;
    }

    private void AppendPointerEvent(List<VrOverlayPointerEvent> result, OpenXrPointerEvent pointer)
    {
        if (
            pointer.Kind == VrOverlayPointerEventKind.Reset
            || !identities.TryGetValue(pointer.Id, out string? name)
            || !frames.TryGetValue(name, out OpenXrOverlayFrame? overlayFrame)
        )
        {
            activePointerOverlayId = null;
            if (result.Count == 0 || result[^1].Kind != VrOverlayPointerEventKind.Reset)
            {
                result.Add(new VrOverlayPointerEvent(string.Empty, VrOverlayPointerEventKind.Reset, 0, 0));
            }
            return;
        }

        activePointerOverlayId = pointer.Id;
        VrOverlayFrame frame = overlayFrame.Frame;
        double width = frame.PointerWidth > 0 ? frame.PointerWidth : frame.Width;
        double height = frame.PointerHeight > 0 ? frame.PointerHeight : frame.Height;
        result.Add(
            new VrOverlayPointerEvent(
                name,
                pointer.Kind,
                pointer.X * width,
                pointer.Y * height,
                pointer.WheelX,
                pointer.WheelY
            )
        );
    }

    public void Shutdown()
    {
        interactionEnabled = false;
        ClearInteractionBuffers();
        if (sessionHandle != nint.Zero)
        {
            backend.Destroy(sessionHandle);
            sessionHandle = nint.Zero;
        }

        identities.Clear();
        frames.Clear();
        maximumOverlays = OpenXrFramePacket.MaximumOverlays;
        nextFrameSortOrder = 0;
    }

    private void ClearInteractionBuffers()
    {
        pendingInteractionReset = false;
        activePointerOverlayId = null;
        bufferedEvents.Clear();
    }

    private void InvalidateOverlayInteraction(string plotterName)
    {
        if (!interactionEnabled || !IsInitialized)
        {
            return;
        }

        ulong overlayId = OpenXrFramePacket.GetOverlayId(plotterName);
        bool hadActiveOverlay = activePointerOverlayId == overlayId;
        int removedBuffered = bufferedEvents.RemoveAll(ev =>
            string.Equals(ev.PlotterName, plotterName, StringComparison.Ordinal)
        );
        bool sawInvalidatedNativeEvent = false;
        foreach (OpenXrPointerEvent pointer in backend.PollPointerEvents(sessionHandle))
        {
            if (pointer.Id == overlayId)
            {
                sawInvalidatedNativeEvent = true;
                continue;
            }

            AppendPointerEvent(bufferedEvents, pointer);
        }

        if (!hadActiveOverlay && removedBuffered == 0 && !sawInvalidatedNativeEvent)
        {
            return;
        }

        if (activePointerOverlayId == overlayId)
        {
            activePointerOverlayId = null;
        }

        if (bufferedEvents.Count > 0)
        {
            if (hadActiveOverlay)
            {
                pendingInteractionReset = true;
            }

            return;
        }

        ClearInteractionBuffers();
        _ = backend.SetInteractionEnabled(sessionHandle, true);
        pendingInteractionReset = true;
    }

    private static bool HasDifferentPointerBasis(VrOverlayFrame previous, VrOverlayFrame current)
    {
        double previousWidth = previous.PointerWidth > 0 ? previous.PointerWidth : previous.Width;
        double previousHeight = previous.PointerHeight > 0 ? previous.PointerHeight : previous.Height;
        double currentWidth = current.PointerWidth > 0 ? current.PointerWidth : current.Width;
        double currentHeight = current.PointerHeight > 0 ? current.PointerHeight : current.Height;
        return previous.Width != current.Width
            || previous.Height != current.Height
            || Math.Abs(previousWidth - currentWidth) > 0.001
            || Math.Abs(previousHeight - currentHeight) > 0.001;
    }

    public void Dispose()
    {
        Shutdown();
    }
}

internal sealed class NativeLinuxOpenXrOverlayBackend : ILinuxOpenXrOverlayBackend
{
    private const int MessageBufferBytes = 512;
    private const int PointerEventBatchCapacity = 64;
    private readonly LinuxVrRuntimeInspector inspector;
    private readonly Lock loadLock = new();
    private nint libraryHandle;
    private ProbeDelegate? probeFn;
    private CreateDelegate? createFn;
    private PublishDelegate? publishFn;
    private GetStatusDelegate? getStatusFn;
    private GetHeadPoseDelegate? getHeadPoseFn;
    private SetInteractionEnabledDelegate? setInteractionEnabledFn;
    private PollPointerEventsDelegate? pollPointerEventsFn;
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

    public LinuxOpenXrInteractionOutcome SetInteractionEnabled(nint session, bool enabled)
    {
        if (!EnsureLoaded() || session == nint.Zero)
        {
            return new(false, 0, "Linux OpenXR overlay session is not active.");
        }

        if (setInteractionEnabledFn is null)
        {
            return new(
                false,
                0,
                "The installed libSrvSurveyLinuxOpenXrOverlay.so does not support controller-pointer interaction. Update or rebuild SrvSurvey."
            );
        }

        byte[] buffer = new byte[MessageBufferBytes];
        bool succeeded = setInteractionEnabledFn(session, enabled ? 1 : 0, out int xrError, buffer, buffer.Length) != 0;
        return new(succeeded, xrError, DecodeUtf8Z(buffer));
    }

    public IReadOnlyList<OpenXrPointerEvent> PollPointerEvents(nint session)
    {
        if (!EnsureLoaded() || pollPointerEventsFn is null || session == nint.Zero)
        {
            return [];
        }

        var buffer = new NativePointerEvent[PointerEventBatchCapacity];
        uint count = pollPointerEventsFn(session, buffer, (uint)buffer.Length);
        return ConvertPointerEvents(buffer.AsSpan(0, (int)Math.Min(count, (uint)buffer.Length)));
    }

    internal static IReadOnlyList<OpenXrPointerEvent> ConvertPointerEvents(ReadOnlySpan<NativePointerEvent> rawEvents)
    {
        if (rawEvents.IsEmpty)
        {
            return [];
        }

        var results = new List<OpenXrPointerEvent>(rawEvents.Length);
        foreach (NativePointerEvent raw in rawEvents)
        {
            if (raw.Kind > (uint)VrOverlayPointerEventKind.Reset)
            {
                continue;
            }

            var kind = (VrOverlayPointerEventKind)raw.Kind;
            if (kind == VrOverlayPointerEventKind.Reset)
            {
                results.Add(new OpenXrPointerEvent(0, VrOverlayPointerEventKind.Reset, 0, 0, 0, 0));
                continue;
            }

            if (
                !IsBounded(raw.X, 10)
                || !IsBounded(raw.Y, 10)
                || !IsBounded(raw.WheelX, 8)
                || !IsBounded(raw.WheelY, 8)
            )
            {
                continue;
            }

            results.Add(new OpenXrPointerEvent(raw.Id, kind, raw.X, raw.Y, raw.WheelX, raw.WheelY));
        }

        return results;
    }

    private static bool IsBounded(float value, float limit) => float.IsFinite(value) && Math.Abs(value) <= limit;

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

            _ = TryBind(handle, "srvsurvey_linux_openxr_set_interaction_enabled", out setInteractionEnabledFn);
            _ = TryBind(handle, "srvsurvey_linux_openxr_poll_pointer_events", out pollPointerEventsFn);

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

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePointerEvent
    {
        public ulong Id;
        public uint Kind;
        public uint Reserved;
        public float X;
        public float Y;
        public float WheelX;
        public float WheelY;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetInteractionEnabledDelegate(
        nint handle,
        int enabled,
        out int outXrError,
        [Out] byte[] outMessage,
        int messageCapacity
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint PollPointerEventsDelegate(nint handle, [Out] NativePointerEvent[] outEvents, uint capacity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyDelegate(nint handle);
}
