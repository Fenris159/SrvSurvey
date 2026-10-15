using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OpenXrOverlayRuntime : IVrOverlayRuntime
{
    private readonly Func<OpenXrFrameBridge> createBridge;
    private readonly Func<bool> platformSupported;
    private readonly Dictionary<string, OpenXrOverlayFrame> frames = new(StringComparer.Ordinal);
    private OpenXrFrameBridge? bridge;
    private uint originRevision;
    private bool interactionEnabled;
    private readonly Dictionary<ulong, string> identities = [];

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
            return VrRuntimeProbe.RuntimeUnavailable("The VDXR overlay layer requires Windows x64 and Direct3D 11.");
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

        identities[OpenXrFramePacket.GetOverlayId(plotterName)] = plotterName;
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

    public void RemoveOverlay(string plotterName)
    {
        frames.Remove(plotterName);
        identities.Remove(OpenXrFramePacket.GetOverlayId(plotterName));
    }

    public VrRuntimeResult ResetOrientation()
    {
        if (!IsInitialized)
        {
            return VrRuntimeResult.Failure("Connect the VDXR OpenXR layer before resetting the overlay origin.");
        }

        originRevision++;
        return CompleteFrame();
    }

    public VrRuntimeResult SetInteractionEnabled(bool enabled)
    {
        if (enabled && (!IsInitialized || bridge?.IsInputReady != true))
        {
            return VrRuntimeResult.Failure(
                $"Waiting for VDXR controller input (OpenXR error {bridge?.InputError ?? 0}). Update the OpenXR layer and restart Elite before using this shortcut."
            );
        }
        interactionEnabled = enabled;
        bridge?.SetInteractionEnabled(enabled);
        return VrRuntimeResult.Success(
            enabled
                ? "VDXR controller interaction is enabled. Point at a panel and use the trigger to click."
                : "VR overlays are click-through again."
        );
    }

    public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents()
    {
        if (!interactionEnabled || bridge is null)
        {
            return [];
        }
        var result = new List<VrOverlayPointerEvent>();
        foreach (OpenXrPointerEvent pointer in bridge.ReadPointerEvents())
        {
            if (
                pointer.Kind == VrOverlayPointerEventKind.Reset
                || !identities.TryGetValue(pointer.Id, out string? name)
            )
            {
                result.Add(new VrOverlayPointerEvent(string.Empty, VrOverlayPointerEventKind.Reset, 0, 0));
                continue;
            }
            VrOverlayFrame frame = frames[name].Frame;
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
        return result;
    }

    public void Shutdown()
    {
        IsInitialized = false;
        interactionEnabled = false;
        identities.Clear();
        frames.Clear();
        bridge?.Dispose();
        bridge = null;
    }

    public void Dispose()
    {
        Shutdown();
    }
}
