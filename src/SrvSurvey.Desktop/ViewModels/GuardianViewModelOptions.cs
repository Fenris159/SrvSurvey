using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuardianViewModelOptions
{
    public GuardianSiteCatalog? References { get; init; }

    public GuardianPublishedSiteCatalog? PublishedSites { get; init; }

    public GuardianSiteTemplateCatalog? Templates { get; init; }

    public RamTahViewModel? RamTah { get; init; }

    public GuardianOverlaySettingsStore? OverlaySettingsStore { get; init; }

    public IStarSystemResolver? SystemResolver { get; init; }

    public Func<GuardianAerialAltitudes>? AerialAltitudeProvider { get; init; }

    public GuardianGesturePreferences? GesturePreferences { get; init; }

    public Func<string?>? ScreenshotTargetFolderProvider { get; init; }
}

public sealed class GuardianAutomaticMapScaleOptions
{
    public required GuardianSiteKind? SiteKind { get; init; }

    public required double? DistanceFromSite { get; init; }

    public required bool OnFoot { get; init; }

    public required bool UsingSrvTurret { get; init; }

    public required bool MobileOnSurface { get; init; }

    public required double NearestObeliskDistance { get; init; }

    public required bool AutoZoomNearObelisks { get; init; }

    public required bool AutoZoomInSrvTurret { get; init; }
}

public sealed class GuardianSurveyCopyOptions
{
    public required GuardianSurveyData Source { get; init; }

    public string? SiteType { get; init; }

    public int? SiteHeading { get; init; }

    public int? RelicTowerHeading { get; init; }

    public IReadOnlyDictionary<string, GuardianPoiStatus>? PoiStatuses
    {
        get;
        init;
    }

    public IReadOnlyDictionary<string, int>? RelicHeadings { get; init; }

    public IReadOnlyList<GuardianPointOfInterest>? RawPoints { get; init; }

    public bool ReplaceRawPoints { get; init; }
}
