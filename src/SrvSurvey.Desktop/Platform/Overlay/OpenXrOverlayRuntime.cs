using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OpenXrOverlayRuntime : IVrOverlayRuntime
{
    private readonly Func<OpenXrFrameBridge> createBridge;
    private readonly Func<bool> platformSupported;
    private readonly Dictionary<string, OpenXrOverlayFrame> frames = new(StringComparer.Ordinal);
    private OpenXrFrameBridge? bridge;
    private uint originRevision;

    public OpenXrOverlayRuntime(Func<OpenXrFrameBridge>? createBridge = null, Func<bool>? platformSupported = null)
    {
        this.createBridge = createBridge ?? (() => new OpenXrFrameBridge(OpenXrFrameBridge.DefaultPath));
        this.platformSupported =
            platformSupported
            ?? (() => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
    }

    public bool IsInitialized { get; private set; }

    public string DisplayName => "VDXR / OpenXR";

    public VrRuntimeProbe Probe()
    {
        if (!platformSupported())
        {
            return VrRuntimeProbe.RuntimeUnavailable(
                "The experimental VDXR overlay layer requires Windows x64 and Direct3D 11."
            );
        }

        try
        {
            bridge ??= createBridge();
            if (!IsInitialized)
            {
                bridge.Publish(OpenXrFramePacket.Encode(frames, originRevision));
            }

            return bridge.ReadState() switch
            {
                OpenXrLayerState.Ready => VrRuntimeProbe.Ready(),
                OpenXrLayerState.UnsupportedGraphics => VrRuntimeProbe.HeadsetUnavailable(
                    "The OpenXR layer reached Elite, but this session does not use Direct3D 11."
                ),
                OpenXrLayerState.WrongRuntime => VrRuntimeProbe.HeadsetUnavailable(
                    "The OpenXR layer reached Elite, but its active runtime is not VDXR."
                ),
                OpenXrLayerState.Error => VrRuntimeProbe.HeadsetUnavailable(
                    $"The OpenXR layer could not submit the overlay (OpenXR error {bridge.LastError}). Restart Elite to retry."
                ),
                _ => VrRuntimeProbe.HeadsetUnavailable(
                    "Install the SrvSurvey OpenXR layer, then start Elite using VDXR. Restart Elite after installing the layer."
                ),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return VrRuntimeProbe.RuntimeUnavailable("The OpenXR frame mailbox is unavailable: " + exception.Message);
        }
    }

    public VrRuntimeResult Initialize()
    {
        IsInitialized = bridge?.ReadState() == OpenXrLayerState.Ready;
        return IsInitialized
            ? VrRuntimeResult.Success("The VDXR OpenXR layer is active.")
            : VrRuntimeResult.Failure("Waiting for Elite's VDXR session.");
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
            return VrRuntimeResult.Failure("The VDXR OpenXR layer is not connected.");
        }

        frames[plotterName] = new OpenXrOverlayFrame(frame, calibration, alpha);
        return VrRuntimeResult.Success("Queued the OpenXR overlay frame.");
    }

    public VrRuntimeResult CompleteFrame()
    {
        try
        {
            bridge?.Publish(OpenXrFramePacket.Encode(frames, originRevision));
            return VrRuntimeResult.Success("Published the OpenXR overlay frames.");
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            bridge?.Stop();
            return VrRuntimeResult.Failure("The OpenXR overlay frames could not be published: " + exception.Message);
        }
    }

    public void RemoveOverlay(string plotterName) => frames.Remove(plotterName);

    public VrRuntimeResult ResetOrientation()
    {
        if (!IsInitialized)
        {
            return VrRuntimeResult.Failure("Connect the VDXR OpenXR layer before resetting the overlay origin.");
        }

        originRevision++;
        return CompleteFrame();
    }

    public VrRuntimeResult SetInteractionEnabled(bool enabled) =>
        VrRuntimeResult.Failure(
            "The experimental VDXR route displays passive panels. Controller-pointer interaction is available through SteamVR."
        );

    public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents() => [];

    public void Shutdown()
    {
        IsInitialized = false;
        frames.Clear();
        bridge?.Dispose();
        bridge = null;
    }

    public void Dispose()
    {
        Shutdown();
    }
}
