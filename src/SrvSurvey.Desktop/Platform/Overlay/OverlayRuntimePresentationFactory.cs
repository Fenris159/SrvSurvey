using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>
/// Creates the shared presentation control for each passive plotter and a
/// simulated data context for the position editor. Live windows host the same
/// presentation type with real view-models; the editor hosts the same XAML
/// with <see cref="CreateEditorDataContext"/>.
/// </summary>
internal static class OverlayRuntimePresentationFactory
{
    public static bool IsSupported(string plotterName) =>
        plotterName is
            "PlotBioStatus"
            or "PlotBioSystem"
            or "PlotBodyInfo"
            or "PlotBuildCommodities"
            or "PlotFlightWarning"
            or "PlotFloatie"
            or "PlotFootCombat"
            or "PlotFSS"
            or "PlotFSSInfo"
            or "PlotGalMap"
            or "PlotGrounded"
            or "PlotGuardians"
            or "PlotGuardianStatus"
            or "PlotGuardianSystem"
            or "PlotHumanSite"
            or "PlotJumpInfo"
            or "PlotFleetCarrierRoute"
            or "PlotRouteBio"
            or "PlotMassacre"
            or "PlotMiniTrack"
            or "PlotMultiGameCommander"
            or "PlotPriorScans"
            or "PlotPulse"
            or "PlotQuestMini"
            or "PlotRamTah"
            or "PlotSphericalSearch"
            or "PlotStationInfo"
            or "PlotSysStatus"
            or "PlotTrackTarget";

    public static bool TryCreate(
        string plotterName,
        out Control? presentation,
        out object? dataContext)
    {
        if (!IsSupported(plotterName))
        {
            presentation = null;
            dataContext = null;
            return false;
        }

        dataContext = CreateEditorDataContext(plotterName);
        presentation = CreatePresentation(plotterName);
        if (presentation is null)
        {
            dataContext = null;
            return false;
        }

        presentation.DataContext = dataContext;
        return true;
    }

    /// <summary>
    /// Builds only the simulated editor data context (no Avalonia control tree).
    /// Useful for tests and tooling that should not load XAML.
    /// </summary>
    public static object CreateEditorDataContextOnly(string plotterName)
    {
        if (!IsSupported(plotterName))
        {
            throw new ArgumentOutOfRangeException(
                nameof(plotterName),
                plotterName,
                "No shared presentation is registered for this plotter.");
        }

        return CreateEditorDataContext(plotterName);
    }

    public static Control? CreatePresentation(string plotterName) =>
        plotterName switch
        {
            "PlotBioStatus" => new BiologyStatusOverlayPresentation(),
            "PlotBioSystem" => new BiologySurveyOverlayPresentation(),
            "PlotBodyInfo" => new BodyInformationOverlayPresentation(),
            "PlotBuildCommodities" => new ColonizationCommodityOverlayPresentation(),
            "PlotFlightWarning" => new FlightWarningOverlayPresentation(),
            "PlotFloatie" => new NotificationOverlayPresentation(),
            "PlotFootCombat" => new FootCombatOverlayPresentation(),
            "PlotFSS" => new LastFssBodyOverlayPresentation(),
            "PlotFSSInfo" => new FssInfoOverlayPresentation(),
            "PlotGalMap" => new GalaxyMapOverlayPresentation(),
            "PlotGrounded" => new SurfaceSurveyOverlayPresentation(),
            "PlotGuardians" => new GuardianSiteOverlayPresentation(),
            "PlotGuardianStatus" => new GuardianStatusOverlayPresentation(),
            "PlotGuardianSystem" => new GuardianSystemOverlayPresentation(),
            "PlotHumanSite" => new HumanSiteOverlayPresentation(),
            "PlotJumpInfo" => new JumpInfoOverlayPresentation(),
            "PlotFleetCarrierRoute" => new FleetCarrierRouteOverlayPresentation(),
            "PlotRouteBio" => new RouteBioOverlayPresentation(),
            "PlotMassacre" => new MassacreMissionsOverlayPresentation(),
            "PlotMiniTrack" => new MiniTrackOverlayPresentation(),
            "PlotMultiGameCommander" => new MultiGameCommanderOverlayPresentation(),
            "PlotPriorScans" => new PriorScansOverlayPresentation(),
            "PlotPulse" => new PulseOverlayPresentation(),
            "PlotQuestMini" => new QuestIndicatorOverlayPresentation(),
            "PlotRamTah" => new RamTahOverlayPresentation(),
            "PlotSphericalSearch" => new SphericalSearchOverlayPresentation(),
            "PlotStationInfo" => new StationInfoOverlayPresentation(),
            "PlotSysStatus" => new SystemStatusOverlayPresentation(),
            "PlotTrackTarget" => new GroundTargetOverlayPresentation(),
            _ => null,
        };

    public static object CreateEditorDataContext(string plotterName) =>
        OverlayEditorPreviewCatalog.Create(plotterName);

    /// <summary>
    /// Guardian plotters already used dedicated transparent host chrome.
    /// All shared presentations own their surface chrome, so every runtime
    /// presentation path uses the same dedicated host treatment.
    /// </summary>
    public static bool UsesDedicatedHostChrome(string plotterName) =>
        IsSupported(plotterName);
}
