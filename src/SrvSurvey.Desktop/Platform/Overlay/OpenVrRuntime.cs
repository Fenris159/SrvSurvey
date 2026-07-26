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

    VrRuntimeResult Initialize();

    VrRuntimeResult PublishOverlay(
        string plotterName,
        VrOverlayFrame frame,
        VrOverlayCalibration calibration,
        float alpha);

    void RemoveOverlay(string plotterName);

    VrRuntimeResult ResetOrientation();

    void Shutdown();
}

public sealed class OpenVrRuntime : IOpenVrRuntime
{
    private readonly Dictionary<string, ulong> handles = new(
        StringComparer.Ordinal);
    private CVRSystem? system;
    private CVROverlay? overlay;
    private float headsetYawOffset;
    private Matrix4x4 headsetOrientationOffset = Matrix4x4.Identity;

    public bool IsInitialized => system is not null && overlay is not null;

    public VrRuntimeResult Initialize()
    {
        if (IsInitialized)
        {
            return VrRuntimeResult.Success("OpenVR is active.");
        }

        try
        {
            OpenVrNativeLibraryResolver.Register();
            var error = EVRInitError.None;
            system = OpenVR.Init(
                ref error,
                EVRApplicationType.VRApplication_Overlay);
            overlay = OpenVR.Overlay;
            if (error != EVRInitError.None || system is null || overlay is null)
            {
                Shutdown();
                return VrRuntimeResult.Failure(
                    $"OpenVR initialization failed: {error}.");
            }

            headsetYawOffset = 0;
            headsetOrientationOffset = Matrix4x4.Identity;
            return VrRuntimeResult.Success("OpenVR is active.");
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or BadImageFormatException
                or EntryPointNotFoundException
                or TypeInitializationException
                or InvalidOperationException)
        {
            Shutdown();
            return VrRuntimeResult.Failure(
                "OpenVR could not be initialized: " + exception.Message);
        }
    }

    public VrRuntimeResult PublishOverlay(
        string plotterName,
        VrOverlayFrame frame,
        VrOverlayCalibration calibration,
        float alpha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(calibration);
        if (!IsInitialized || overlay is null)
        {
            return VrRuntimeResult.Failure("OpenVR is not active.");
        }

        if (frame.Width <= 0 || frame.Height <= 0
            || frame.RgbaBytes.Length
                != checked(frame.Width * frame.Height * 4))
        {
            return VrRuntimeResult.Failure("The VR overlay frame is invalid.");
        }

        try
        {
            var handle = GetOrCreateHandle(plotterName);
            Check(overlay.SetOverlayAlpha(handle, Math.Clamp(alpha, 0, 1)));
            Check(overlay.SetOverlayWidthInMeters(
                handle,
                calibration.Scale / 10));
            var matrix = VrOverlayTransform.Create(
                    calibration,
                    headsetYawOffset,
                    headsetOrientationOffset)
                .ToHmdMatrix34_t();
            Check(overlay.SetOverlayTransformAbsolute(
                handle,
                ETrackingUniverseOrigin.TrackingUniverseStanding,
                ref matrix));
            var pinned = GCHandle.Alloc(
                frame.RgbaBytes,
                GCHandleType.Pinned);
            try
            {
                Check(overlay.SetOverlayRaw(
                    handle,
                    pinned.AddrOfPinnedObject(),
                    (uint)frame.Width,
                    (uint)frame.Height,
                    4));
            }
            finally
            {
                pinned.Free();
            }

            Check(overlay.ShowOverlay(handle));
            return VrRuntimeResult.Success($"Published {plotterName} to OpenVR.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or OverflowException)
        {
            return VrRuntimeResult.Failure(
                $"OpenVR rejected {plotterName}: {exception.Message}");
        }
    }

    public void RemoveOverlay(string plotterName)
    {
        if (overlay is null
            || !handles.Remove(plotterName, out var handle))
        {
            return;
        }

        _ = overlay.HideOverlay(handle);
        _ = overlay.DestroyOverlay(handle);
    }

    public VrRuntimeResult ResetOrientation()
    {
        if (system is null)
        {
            return VrRuntimeResult.Failure("OpenVR is not active.");
        }

        var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        system.GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            0,
            poses);
        if (!poses[0].bPoseIsValid)
        {
            return VrRuntimeResult.Failure(
                "The headset pose is not currently valid.");
        }

        headsetYawOffset = VrOverlayTransform.ExtractYaw(
            poses[0].mDeviceToAbsoluteTracking);
        headsetOrientationOffset = Matrix4x4.CreateFromAxisAngle(
            Vector3.UnitY,
            headsetYawOffset);
        return VrRuntimeResult.Success(
            "Captured the current headset yaw as the overlay origin.");
    }

    public void Shutdown()
    {
        if (overlay is not null)
        {
            foreach (var handle in handles.Values)
            {
                _ = overlay.HideOverlay(handle);
                _ = overlay.DestroyOverlay(handle);
            }
        }

        handles.Clear();
        if (system is not null)
        {
            OpenVR.Shutdown();
        }

        overlay = null;
        system = null;
        headsetYawOffset = 0;
        headsetOrientationOffset = Matrix4x4.Identity;
    }

    public void Dispose()
    {
        Shutdown();
    }

    private ulong GetOrCreateHandle(string plotterName)
    {
        if (handles.TryGetValue(plotterName, out var handle))
        {
            return handle;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(plotterName)))[..16];
        var error = overlay!.CreateOverlay(
            $"com.ravencolonial.srvsurvey.{hash}",
            $"SrvSurvey {plotterName}",
            ref handle);
        Check(error);
        handles[plotterName] = handle;
        return handle;
    }

    private static void Check(EVROverlayError error)
    {
        if (error != EVROverlayError.None)
        {
            throw new InvalidOperationException(error.ToString());
        }
    }
}

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
