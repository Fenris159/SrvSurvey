namespace SrvSurvey.Desktop.Platform.Overlay;

public enum VrOverlayTransportKind
{
    OpenVr,
    WindowsVdxrOpenXr,
    LinuxWivrnOpenXr,
    LinuxMonadoOpenXr,
}

/// <summary>Owns one transport at a time and releases it when the connection route changes.</summary>
public sealed class VrOverlayRuntimeRouter : IVrOverlayRuntime
{
    private readonly Func<string> profile;
    private readonly Func<VrOverlayTransportKind, IVrOverlayRuntime> factory;
    private IVrOverlayRuntime? active;
    private VrOverlayTransportKind activeKind;

    public VrOverlayRuntimeRouter(
        Func<string> profile,
        Func<bool, IVrOverlayRuntime>? factory = null,
        Func<bool>? edvrControllerInput = null,
        Func<bool>? isCustomRuntime = null
    )
        : this(
            profile,
            factory is not null
                ? (VrOverlayTransportKind kind) => factory(kind != VrOverlayTransportKind.OpenVr)
                : CreateDefaultFactory(edvrControllerInput, isCustomRuntime)
        ) { }

    public VrOverlayRuntimeRouter(
        Func<string> profile,
        Func<VrOverlayTransportKind, IVrOverlayRuntime> transportFactory
    )
    {
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        factory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    private static Func<VrOverlayTransportKind, IVrOverlayRuntime> CreateDefaultFactory(
        Func<bool>? edvrControllerInput,
        Func<bool>? isCustomRuntime
    ) =>
        kind =>
            kind switch
            {
                VrOverlayTransportKind.WindowsVdxrOpenXr => new OpenXrOverlayRuntime(
                    edvrControllerInput: edvrControllerInput
                ),
                VrOverlayTransportKind.LinuxWivrnOpenXr => new LinuxOpenXrOverlayRuntime(
                    VrPlatformProfileCatalog.WivrnOpenXrProfileId
                ),
                VrOverlayTransportKind.LinuxMonadoOpenXr => new LinuxOpenXrOverlayRuntime(
                    VrPlatformProfileCatalog.MonadoOpenXrProfileId
                ),
                _ => new OpenVrRuntime(isCustomRuntime: isCustomRuntime),
            };

    internal static VrOverlayTransportKind ResolveTransport(string profileId) =>
        profileId switch
        {
            VrPlatformProfileCatalog.MetaVdxrProfileId => VrOverlayTransportKind.WindowsVdxrOpenXr,
            VrPlatformProfileCatalog.WivrnOpenXrProfileId => VrOverlayTransportKind.LinuxWivrnOpenXr,
            VrPlatformProfileCatalog.MonadoOpenXrProfileId => VrOverlayTransportKind.LinuxMonadoOpenXr,
            _ => VrOverlayTransportKind.OpenVr,
        };

    private IVrOverlayRuntime Current
    {
        get
        {
            VrOverlayTransportKind requested = ResolveTransport(profile());
            if (active is null || activeKind != requested)
            {
                active?.Dispose();
                active = factory(requested);
                activeKind = requested;
            }

            return active;
        }
    }

    public bool IsInitialized => Current.IsInitialized;
    public string DisplayName => Current.DisplayName;
    public int MaximumOverlays => Current.MaximumOverlays;

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

    public bool TryGetHeadPose(out VrOverlayHeadPose? pose) => Current.TryGetHeadPose(out pose);

    public VrRuntimeResult SetInteractionEnabled(bool enabled) => Current.SetInteractionEnabled(enabled);

    public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents() => Current.PollPointerEvents();

    public void Shutdown() => active?.Shutdown();

    public void Dispose()
    {
        active?.Dispose();
        active = null;
    }
}
