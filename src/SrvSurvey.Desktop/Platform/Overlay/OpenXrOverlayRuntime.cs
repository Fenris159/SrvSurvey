using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OpenXrOverlayRuntime : IVrOverlayRuntime
{
    private const int ActionSetsAlreadyAttachedError = -47;
    private readonly Func<OpenXrFrameBridge> createBridge;
    private readonly Func<bool> platformSupported;
    private readonly Func<bool> edvrControllerInput;
    private readonly Dictionary<string, OpenXrOverlayFrame> frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> activeOverlayIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> surfaceRevisions = new(StringComparer.Ordinal);
    private OpenXrFrameBridge? bridge;
    private uint originRevision;
    private bool interactionEnabled;
    private int nextFrameSortOrder;
    private readonly Dictionary<ulong, string> identities = [];

    public OpenXrOverlayRuntime(
        Func<OpenXrFrameBridge>? createBridge = null,
        Func<bool>? platformSupported = null,
        Func<bool>? edvrControllerInput = null
    )
    {
        this.createBridge = createBridge ?? (() => new OpenXrFrameBridge(OpenXrFrameBridge.DefaultPath));
        this.edvrControllerInput = edvrControllerInput ?? (() => false);
        this.platformSupported =
            platformSupported
            ?? (() => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
    }

    public bool IsInitialized { get; private set; }

    public string DisplayName => "VDXR / OpenXR";

    public int MaximumOverlays
    {
        get
        {
            if (!IsInitialized || bridge?.AvailableOverlayLayers is not { } available)
            {
                return OpenXrFramePacket.MaximumOverlays;
            }

            int panelBudget = interactionEnabled && available >= 2 ? available - 1 : available;
            return Math.Min(panelBudget, OpenXrFramePacket.MaximumOverlays);
        }
    }

    public bool TryGetHeadPose(out VrOverlayHeadPose? pose)
    {
        pose = null;
        return IsInitialized && bridge is not null && bridge.TryReadHeadPose(originRevision, out pose);
    }

    public VrRuntimeProbe Probe()
    {
        if (!platformSupported())
        {
            return VrRuntimeProbe.RuntimeUnavailable("The VDXR overlay layer requires Windows x64 and Direct3D 11.");
        }

        try
        {
            bridge ??= createBridge();
            bridge.SetEdvrControllerInput(edvrControllerInput());
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

        ulong overlayId;
        if (
            frames.TryGetValue(plotterName, out OpenXrOverlayFrame? previous)
            && HasDifferentPointerBasis(previous.Frame, frame)
        )
        {
            identities.Remove(activeOverlayIds[plotterName]);
            overlayId = AdvanceOverlayId(plotterName);
        }
        else if (!activeOverlayIds.TryGetValue(plotterName, out overlayId))
        {
            overlayId = OpenXrFramePacket.GetOverlayId(plotterName, surfaceRevisions.GetValueOrDefault(plotterName));
            activeOverlayIds[plotterName] = overlayId;
        }

        identities[overlayId] = plotterName;
        frames[plotterName] = new OpenXrOverlayFrame(frame, calibration, alpha, nextFrameSortOrder++, overlayId);
        return VrRuntimeResult.Success("Queued the OpenXR overlay frame.");
    }

    public VrRuntimeResult CompleteFrame()
    {
        try
        {
            bridge?.SetEdvrControllerInput(edvrControllerInput());
            bridge?.Publish(OpenXrFramePacket.Encode(frames, originRevision));
            return VrRuntimeResult.Success("Published the OpenXR overlay frames.");
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            bridge?.Stop();
            return VrRuntimeResult.Failure("The OpenXR overlay frames could not be published: " + exception.Message);
        }
        finally
        {
            nextFrameSortOrder = 0;
        }
    }

    public void RemoveOverlay(string plotterName)
    {
        frames.Remove(plotterName);
        if (activeOverlayIds.Remove(plotterName, out ulong previousId))
        {
            identities.Remove(previousId);
            surfaceRevisions[plotterName] = surfaceRevisions.GetValueOrDefault(plotterName) + 1;
        }
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
        if (enabled && edvrControllerInput() && bridge?.InputError == ActionSetsAlreadyAttachedError)
        {
            return VrRuntimeResult.Failure(
                "Controller input has a conflicting OpenXR action attachment (error -47). Turn off Experimental EDVR controller input and restart Elite. Overlay rendering remains available."
            );
        }
        if (enabled && bridge?.IsFrameDrivenInput == true && !edvrControllerInput())
        {
            return VrRuntimeResult.Failure(
                "Experimental EDVR controller input is off. Restart Elite after changing the input source before enabling controller interaction again."
            );
        }
        if (enabled && (!IsInitialized || bridge?.IsInputReady != true))
        {
            return VrRuntimeResult.Failure(
                $"Waiting for VDXR controller input (OpenXR error {bridge?.InputError ?? 0}). Update the OpenXR layer and restart Elite before using this shortcut. For EDVR, enable Experimental EDVR controller input in the VR settings."
            );
        }
        if (enabled && bridge!.AvailableOverlayLayers is < 2)
        {
            return VrRuntimeResult.Failure(
                "The active VDXR runtime needs at least two free composition layers for controller interaction. Free a composition layer before enabling controller interaction."
            );
        }
        interactionEnabled = enabled;
        bridge?.SetInteractionEnabled(enabled);
        string enabledMessage =
            bridge?.IsFrameDrivenInput == true
                ? "Experimental EDVR controller interaction is enabled. Point at a panel and use the trigger to click."
                : "VDXR controller interaction is enabled. Point at a panel and use the trigger to click.";
        return VrRuntimeResult.Success(enabled ? enabledMessage : "VR overlays are click-through again.");
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
                || !frames.TryGetValue(name, out OpenXrOverlayFrame? overlayFrame)
            )
            {
                result.Add(new VrOverlayPointerEvent(string.Empty, VrOverlayPointerEventKind.Reset, 0, 0));
                continue;
            }
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
        return result;
    }

    public void Shutdown()
    {
        IsInitialized = false;
        interactionEnabled = false;
        nextFrameSortOrder = 0;
        identities.Clear();
        activeOverlayIds.Clear();
        surfaceRevisions.Clear();
        frames.Clear();
        bridge?.Dispose();
        bridge = null;
    }

    private ulong AdvanceOverlayId(string plotterName)
    {
        ulong nextRevision = surfaceRevisions.GetValueOrDefault(plotterName) + 1;
        surfaceRevisions[plotterName] = nextRevision;
        ulong nextId = OpenXrFramePacket.GetOverlayId(plotterName, nextRevision);
        activeOverlayIds[plotterName] = nextId;
        return nextId;
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
