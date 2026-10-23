namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Owns one transport at a time and releases it when the connection route changes.</summary>
public sealed class VrOverlayRuntimeRouter(Func<string> profile, Func<bool, IVrOverlayRuntime>? factory = null)
    : IVrOverlayRuntime
{
    private readonly Func<bool, IVrOverlayRuntime> factory =
        factory ?? (direct => direct ? new OpenXrOverlayRuntime() : new OpenVrRuntime());
    private IVrOverlayRuntime? active;
    private bool direct;

    private IVrOverlayRuntime Current
    {
        get
        {
            bool requested = profile() == VrPlatformProfileCatalog.MetaVdxrProfileId;
            if (active is null || direct != requested)
            {
                active?.Dispose();
                active = factory(requested);
                direct = requested;
            }

            return active;
        }
    }

    public bool IsInitialized => Current.IsInitialized;
    public string DisplayName => Current.DisplayName;

    public VrRuntimeProbe Probe() => Current.Probe();

    public VrRuntimeResult Initialize() => Current.Initialize();

    public VrRuntimeResult PublishOverlay(
        string plotterName,
        VrOverlayFrame frame,
        VrOverlayCalibration calibration,
        float alpha
    ) => Current.PublishOverlay(plotterName, frame, calibration, alpha);

    public VrRuntimeResult CompleteFrame() => Current.CompleteFrame();

    public void RemoveOverlay(string plotterName) => Current.RemoveOverlay(plotterName);

    public VrRuntimeResult ResetOrientation() => Current.ResetOrientation();

    public VrRuntimeResult SetInteractionEnabled(bool enabled) => Current.SetInteractionEnabled(enabled);

    public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents() => Current.PollPointerEvents();

    public void Shutdown() => active?.Shutdown();

    public void Dispose()
    {
        active?.Dispose();
        active = null;
    }
}
