using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OVRSharp.Math;
using Valve.VR;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IOpenVrRuntime : IDisposable
{
    bool IsInitialized { get; }

    VrRuntimeProbe Probe();

    VrRuntimeResult Initialize();

    VrRuntimeResult PublishOverlay(
        string plotterName,
        VrOverlayFrame frame,
        VrOverlayCalibration calibration,
        float alpha
    );

    VrRuntimeResult SetInteractionEnabled(bool enabled);

    IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents();

    void RemoveOverlay(string plotterName);

    VrRuntimeResult ResetOrientation();

    void Shutdown();
}

public sealed class OpenVrRuntime : IOpenVrRuntime
{
    private readonly Dictionary<string, ulong> handles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double X, double Y)> pointerPositions = new(StringComparer.Ordinal);
    private CVRSystem? system;
    private CVROverlay? overlay;
    private float headsetYawOffset;
    private Matrix4x4 headsetOrientationOffset = Matrix4x4.Identity;
    private bool interactionEnabled;

    public bool IsInitialized => system is not null && overlay is not null;

    public VrRuntimeProbe Probe()
    {
        try
        {
            OpenVrNativeLibraryResolver.Register();
            if (!OpenVR.IsRuntimeInstalled())
            {
                return VrRuntimeProbe.RuntimeUnavailable("SteamVR/OpenVR is not installed or registered.");
            }

            return OpenVR.IsHmdPresent()
                ? VrRuntimeProbe.Ready()
                : VrRuntimeProbe.HeadsetUnavailable("SteamVR is available, but no ready headset was detected.");
        }
        catch (Exception exception)
            when (exception
                    is DllNotFoundException
                        or BadImageFormatException
                        or EntryPointNotFoundException
                        or TypeInitializationException
                        or InvalidOperationException
            )
        {
            return VrRuntimeProbe.RuntimeUnavailable("OpenVR could not be probed: " + exception.Message);
        }
    }

    public VrRuntimeResult Initialize()
    {
        if (IsInitialized)
        {
            return VrRuntimeResult.Success("OpenVR is active.");
        }

        try
        {
            OpenVrNativeLibraryResolver.Register();
            EVRInitError error = EVRInitError.None;
            system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
            overlay = OpenVR.Overlay;
            if (error != EVRInitError.None || system is null || overlay is null)
            {
                Shutdown();
                return VrRuntimeResult.Failure($"OpenVR initialization failed: {error}.");
            }

            headsetYawOffset = 0;
            headsetOrientationOffset = Matrix4x4.Identity;
            return VrRuntimeResult.Success("OpenVR is active.");
        }
        catch (Exception exception)
            when (exception
                    is DllNotFoundException
                        or BadImageFormatException
                        or EntryPointNotFoundException
                        or TypeInitializationException
                        or InvalidOperationException
            )
        {
            Shutdown();
            return VrRuntimeResult.Failure("OpenVR could not be initialized: " + exception.Message);
        }
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
        if (!IsInitialized || overlay is null)
        {
            return VrRuntimeResult.Failure("OpenVR is not active.");
        }

        if (frame.Width <= 0 || frame.Height <= 0 || frame.RgbaBytes.Length != checked(frame.Width * frame.Height * 4))
        {
            return VrRuntimeResult.Failure("The VR overlay frame is invalid.");
        }

        try
        {
            ulong handle = GetOrCreateHandle(plotterName);
            var mouseScale = new HmdVector2_t
            {
                v0 = (float)(frame.PointerWidth > 0 ? frame.PointerWidth : frame.Width),
                v1 = (float)(frame.PointerHeight > 0 ? frame.PointerHeight : frame.Height),
            };
            Check(overlay.SetOverlayMouseScale(handle, ref mouseScale));
            Check(overlay.SetOverlayAlpha(handle, Math.Clamp(alpha, 0, 1)));
            Check(overlay.SetOverlayWidthInMeters(handle, calibration.Scale / 10));
            var matrix = VrOverlayTransform
                .Create(calibration, headsetYawOffset, headsetOrientationOffset)
                .ToHmdMatrix34_t();
            Check(
                overlay.SetOverlayTransformAbsolute(
                    handle,
                    ETrackingUniverseOrigin.TrackingUniverseStanding,
                    ref matrix
                )
            );
            var pinned = GCHandle.Alloc(frame.RgbaBytes, GCHandleType.Pinned);
            try
            {
                Check(
                    overlay.SetOverlayRaw(handle, pinned.AddrOfPinnedObject(), (uint)frame.Width, (uint)frame.Height, 4)
                );
            }
            finally
            {
                pinned.Free();
            }

            Check(overlay.ShowOverlay(handle));
            return VrRuntimeResult.Success($"Published {plotterName} to OpenVR.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return VrRuntimeResult.Failure($"OpenVR rejected {plotterName}: {exception.Message}");
        }
    }

    public void RemoveOverlay(string plotterName)
    {
        if (overlay is null || !handles.Remove(plotterName, out ulong handle))
        {
            return;
        }

        _ = overlay.HideOverlay(handle);
        _ = overlay.DestroyOverlay(handle);
        pointerPositions.Remove(plotterName);
    }

    public VrRuntimeResult SetInteractionEnabled(bool enabled)
    {
        if (!IsInitialized || overlay is null)
        {
            return VrRuntimeResult.Failure("OpenVR is not active.");
        }

        VROverlayInputMethod previousMethod = ToInputMethod(interactionEnabled);
        VROverlayInputMethod nextMethod = ToInputMethod(enabled);
        (bool succeeded, string? error, bool rollbackFailed) = UpdateOverlayInputMethods(
            handles.Values.ToArray(),
            previousMethod,
            nextMethod,
            overlay.SetOverlayInputMethod
        );
        if (!succeeded)
        {
            if (rollbackFailed)
            {
                Shutdown();
            }

            return VrRuntimeResult.Failure(
                "OpenVR could not change overlay interaction: "
                    + error
                    + (rollbackFailed ? " The rollback failed, so the OpenVR runtime was shut down." : string.Empty)
            );
        }

        interactionEnabled = enabled;
        return VrRuntimeResult.Success(
            enabled ? "VR overlay controller interaction is enabled." : "VR overlays are click-through again."
        );
    }

    internal static (bool Succeeded, string? Error, bool RollbackFailed) UpdateOverlayInputMethods(
        IReadOnlyList<ulong> overlayHandles,
        VROverlayInputMethod previousMethod,
        VROverlayInputMethod nextMethod,
        Func<ulong, VROverlayInputMethod, EVROverlayError> setInputMethod
    )
    {
        ArgumentNullException.ThrowIfNull(overlayHandles);
        ArgumentNullException.ThrowIfNull(setInputMethod);

        var updatedHandles = new List<ulong>(overlayHandles.Count);
        try
        {
            foreach (ulong handle in overlayHandles)
            {
                Check(setInputMethod(handle, nextMethod));
                updatedHandles.Add(handle);
            }

            return (true, null, false);
        }
        catch (InvalidOperationException exception)
        {
            bool rollbackFailed = false;
            for (int index = updatedHandles.Count - 1; index >= 0; index--)
            {
                try
                {
                    rollbackFailed |= setInputMethod(updatedHandles[index], previousMethod) != EVROverlayError.None;
                }
                catch (InvalidOperationException)
                {
                    rollbackFailed = true;
                }
            }

            return (false, exception.Message, rollbackFailed);
        }
    }

    public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents()
    {
        if (!interactionEnabled || overlay is null)
        {
            return [];
        }

        var events = new List<VrOverlayPointerEvent>();
        uint eventSize = (uint)Marshal.SizeOf<VREvent_t>();
        foreach ((string plotterName, ulong handle) in handles)
        {
            VREvent_t source = default;
            while (overlay.PollNextOverlayEvent(handle, ref source, eventSize))
            {
                (double X, double Y) position = pointerPositions.GetValueOrDefault(plotterName);
                if (
                    TryCreatePointerEvent(plotterName, source, ref position, out VrOverlayPointerEvent? item)
                    && item is not null
                )
                {
                    pointerPositions[plotterName] = position;
                    events.Add(item);
                }

                source = default;
            }
        }

        return events;
    }

    public VrRuntimeResult ResetOrientation()
    {
        if (system is null)
        {
            return VrRuntimeResult.Failure("OpenVR is not active.");
        }

        var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
        if (!poses[0].bPoseIsValid)
        {
            return VrRuntimeResult.Failure("The headset pose is not currently valid.");
        }

        headsetYawOffset = VrOverlayTransform.ExtractYaw(poses[0].mDeviceToAbsoluteTracking);
        headsetOrientationOffset = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, headsetYawOffset);
        return VrRuntimeResult.Success("Captured the current headset yaw as the overlay origin.");
    }

    public void Shutdown()
    {
        if (overlay is not null)
        {
            foreach (ulong handle in handles.Values)
            {
                _ = overlay.HideOverlay(handle);
                _ = overlay.DestroyOverlay(handle);
            }
        }

        handles.Clear();
        pointerPositions.Clear();
        if (system is not null)
        {
            OpenVR.Shutdown();
        }

        overlay = null;
        system = null;
        headsetYawOffset = 0;
        headsetOrientationOffset = Matrix4x4.Identity;
        interactionEnabled = false;
    }

    public void Dispose()
    {
        Shutdown();
    }

    private ulong GetOrCreateHandle(string plotterName)
    {
        if (handles.TryGetValue(plotterName, out ulong handle))
        {
            return handle;
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plotterName)))[..16];
        EVROverlayError error = overlay!.CreateOverlay(
            $"com.ravencolonial.srvsurvey.{hash}",
            $"SrvSurvey {plotterName}",
            ref handle
        );
        Check(error);
        try
        {
            Check(overlay.SetOverlayInputMethod(handle, ToInputMethod(interactionEnabled)));
            handles[plotterName] = handle;
            return handle;
        }
        catch
        {
            _ = overlay.DestroyOverlay(handle);
            throw;
        }
    }

    internal static bool TryCreatePointerEvent(
        string plotterName,
        VREvent_t source,
        ref (double X, double Y) position,
        out VrOverlayPointerEvent? result
    )
    {
        VrOverlayPointerEventKind? kind = (EVREventType)source.eventType switch
        {
            EVREventType.VREvent_MouseMove => VrOverlayPointerEventKind.Move,
            EVREventType.VREvent_MouseButtonDown => ToButtonKind(source.data.mouse.button, pressed: true),
            EVREventType.VREvent_MouseButtonUp => ToButtonKind(source.data.mouse.button, pressed: false),
            EVREventType.VREvent_ScrollDiscrete or EVREventType.VREvent_ScrollSmooth => VrOverlayPointerEventKind.Wheel,
            _ => null,
        };
        if (kind is null)
        {
            result = null;
            return false;
        }

        if (kind == VrOverlayPointerEventKind.Move)
        {
            position = (source.data.mouse.x, source.data.mouse.y);
        }

        result = new VrOverlayPointerEvent(
            plotterName,
            kind.Value,
            position.X,
            position.Y,
            kind == VrOverlayPointerEventKind.Wheel ? source.data.scroll.xdelta : 0,
            kind == VrOverlayPointerEventKind.Wheel ? source.data.scroll.ydelta : 0
        );
        return true;
    }

    private static VROverlayInputMethod ToInputMethod(bool enabled)
    {
        return enabled ? VROverlayInputMethod.Mouse : VROverlayInputMethod.None;
    }

    private static VrOverlayPointerEventKind? ToButtonKind(uint button, bool pressed)
    {
        return (EVRMouseButton)button switch
        {
            EVRMouseButton.Left => pressed
                ? VrOverlayPointerEventKind.LeftButtonDown
                : VrOverlayPointerEventKind.LeftButtonUp,
            EVRMouseButton.Right => pressed
                ? VrOverlayPointerEventKind.RightButtonDown
                : VrOverlayPointerEventKind.RightButtonUp,
            EVRMouseButton.Middle => pressed
                ? VrOverlayPointerEventKind.MiddleButtonDown
                : VrOverlayPointerEventKind.MiddleButtonUp,
            _ => null,
        };
    }

    private static void Check(EVROverlayError error)
    {
        if (error != EVROverlayError.None)
        {
            throw new InvalidOperationException(error.ToString());
        }
    }
}

public enum VrOverlayPointerEventKind
{
    Move,
    LeftButtonDown,
    LeftButtonUp,
    RightButtonDown,
    RightButtonUp,
    MiddleButtonDown,
    MiddleButtonUp,
    Wheel,
}

public sealed record VrOverlayPointerEvent(
    string PlotterName,
    VrOverlayPointerEventKind Kind,
    double X,
    double Y,
    double WheelX = 0,
    double WheelY = 0
);

public sealed record VrRuntimeResult(bool Succeeded, string Message)
{
    public static VrRuntimeResult Success(string message)
    {
        return new VrRuntimeResult(true, message);
    }

    public static VrRuntimeResult Failure(string message)
    {
        return new VrRuntimeResult(false, message);
    }
}

public sealed record VrRuntimeProbe(bool RuntimeAvailable, bool HeadsetPresent, string Message)
{
    public static VrRuntimeProbe Ready()
    {
        return new VrRuntimeProbe(true, true, "SteamVR and a headset are available.");
    }

    public static VrRuntimeProbe RuntimeUnavailable(string message)
    {
        return new VrRuntimeProbe(false, false, message);
    }

    public static VrRuntimeProbe HeadsetUnavailable(string message)
    {
        return new VrRuntimeProbe(true, false, message);
    }
}
