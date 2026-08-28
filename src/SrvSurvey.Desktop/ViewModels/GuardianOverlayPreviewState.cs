using System.ComponentModel;
using System.Globalization;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

internal sealed class GuardianOverlayPreviewState
    : IGuardianOverlayPresentationState
{
    private const string SampleObeliskName = "A01";
    private const string SampleLogCode = "H12";
    private const string TotemArtifact = "Totem";
    private const string TotemRequirementText = "Casket + Totem";

    private static readonly GuardianObelisk SampleObelisk = new(
        SampleObeliskName,
        SampleLogCode,
        Scanned: false,
        ["Casket", TotemArtifact]);

    private static readonly GuardianSiteTemplate SampleTemplate =
        GuardianSiteTemplateCatalog.LoadEmbedded().Find("Beta")
        ?? throw new InvalidOperationException(
            "The embedded Beta Guardian site template is missing.");

    private static readonly GuardianSiteMapProjection SampleMapProjection =
        new GuardianSiteMapProjector().Project(
            SampleTemplate,
            activeObelisks: [SampleObelisk],
            obeliskGroups: new HashSet<char> { 'A', 'B' },
            neededRamTahLogCodes: new HashSet<string>(
                [SampleLogCode],
                StringComparer.OrdinalIgnoreCase));

    private readonly GuardianStatusPreviewState statusState;

    private GuardianOverlayPreviewState(
        GuardianStatusPreviewState statusState =
            GuardianStatusPreviewState.ObeliskTarget)
    {
        this.statusState = statusState;
        ActiveMapProjection = SampleMapProjection;
        var nearest = SampleTemplate.PointsOfInterest.First(point =>
            string.Equals(point.Name, SampleObeliskName, StringComparison.Ordinal));
        Proximity = new GuardianSiteProximitySnapshot(
            DistanceFromSite: 42.6,
            CommanderX: 12,
            CommanderY: -18,
            MapX: 12,
            MapY: -18,
            new GuardianNearbyPoint(
                nearest,
                Distance: 18.4,
                X: 12,
                Y: -18,
                SampleObelisk),
            SampleObelisk);
        CurrentSystemSites =
        [
            CreateSiteRow(
                siteId: 504,
                siteType: "Beta",
                index: 1,
                progress: 64,
                isDestination: true,
                ramTahLogs: ["H12", "H16"]),
            CreateSiteRow(
                siteId: 505,
                siteType: "Alpha",
                index: 2,
                progress: 100,
                isDestination: false,
                ramTahLogs: []),
        ];
        CurrentRamTahLogs =
        [
            new GuardianRamTahLogViewModel(
                "H12",
                "History #12",
                TotemRequirementText,
                HasArtifacts: true,
                "A01, A03",
                IsCurrentObelisk: true,
                IsTargetObelisk: true,
                [
                    new("ca", "Casket", true, "+"),
                    new("to", TotemArtifact, true, string.Empty),
                ]),
            new GuardianRamTahLogViewModel(
                "H16",
                "History #16",
                "Orb + Urn",
                HasArtifacts: false,
                "B04",
                IsCurrentObelisk: false,
                IsTargetObelisk: true,
                [
                    new("or", "Orb", false, "+"),
                    new("ur", "Urn", true, string.Empty),
                ]),
            new GuardianRamTahLogViewModel(
                "T07",
                "Technology #7",
                $"Tablet + {TotemArtifact}",
                HasArtifacts: true,
                "C02",
                IsCurrentObelisk: false,
                IsTargetObelisk: false,
                [
                    new("ta", "Tablet", true, "+"),
                    new("to", TotemArtifact, true, string.Empty),
                ]),
        ];
    }

    public static GuardianOverlayPreviewState Instance { get; } = new();

    public static GuardianOverlayPreviewState Create(
        GuardianStatusPreviewState statusState) => new(statusState);

    // Never raises: preview state is immutable after construction.
    public event PropertyChangedEventHandler? PropertyChanged = delegate { };

    public int PreferredOverlayWidth => 300;

    public int PreferredOverlayHeight => 400;

    public GuardianSiteMapProjection? ActiveMapProjection { get; }

    public GuardianSiteProximitySnapshot? Proximity { get; }

    public double ActiveMapScale => double.NaN;

    public double ActiveMapRelativeHeading => 17;

    public string? TargetObeliskName => "A01";

    public string? ActiveMapSelectedPointName =>
        Proximity?.NearestPoint?.Point.Name ?? TargetObeliskName;

    public GuardianAlignmentMode? AlignmentMode => null;

    public double AlignmentOpacity => 0;

    public bool IsAlignmentVisible => false;

    public string ActiveMapTitle => "GR 504 - Beta ruins #1";

    public string ActiveMapSummary =>
        "209 mapped objects - 32 of 50 survey points confirmed";

    public bool HasLiveMapPrompt => false;

    public string LiveMapPromptTitle => string.Empty;

    public string LiveMapPromptText => string.Empty;

    public bool HasHeadingGuide => false;

    public string? HeadingGuideAssetPath => null;

    public string AlignmentStatusText => string.Empty;

    public string BlinkGestureText =>
        "Toggle cockpit mode 2x to confirm.";

    public string GuardianChoiceGestureText =>
        "Cycle firegroup to choose; toggle cockpit mode 2x to conf.";

    public string ActiveMapScaleText => "AUTO 1.0x";

    public string TargetObeliskText => "TARGET A01";

    public bool IsGlideApproach =>
        statusState == GuardianStatusPreviewState.GlideApproach;

    public string GlideApproachTitle => "APPROACHING GUARDIAN RUINS";

    public string GlideApproachText => "Beta ruins #1";

    public string GlideApproachFooter => "Maintain glide toward the site.";

    public bool IsLocalGuardianStatus => !IsGlideApproach;

    public bool IsGuardianSiteTypeChoiceVisible =>
        statusState == GuardianStatusPreviewState.SiteTypeChoice;

    public bool IsGuardianHeadingChoiceVisible =>
        statusState == GuardianStatusPreviewState.HeadingChoice;

    public bool IsGuardianOriginVisible =>
        statusState == GuardianStatusPreviewState.SiteOrigin;

    public bool IsGuardianOnFootRelicVisible =>
        statusState == GuardianStatusPreviewState.OnFootRelic;

    public bool IsGuardianObeliskVisible =>
        statusState == GuardianStatusPreviewState.ObeliskTarget;

    public bool IsGuardianPoiChoiceVisible =>
        statusState == GuardianStatusPreviewState.PoiChoice;

    public bool IsGuardianNoPointVisible =>
        statusState == GuardianStatusPreviewState.NoNearbyPoint;

    public string GuardianStatusTitle => statusState switch
    {
        GuardianStatusPreviewState.SiteTypeChoice => "CHOOSE GUARDIAN SITE TYPE",
        GuardianStatusPreviewState.HeadingChoice => "CONFIRM SITE HEADING",
        GuardianStatusPreviewState.SiteOrigin => "ALIGN GUARDIAN SITE ORIGIN",
        GuardianStatusPreviewState.OnFootRelic => "RELIC TOWER GUIDANCE",
        GuardianStatusPreviewState.PoiChoice => "IDENTIFY SURVEY POINT",
        GuardianStatusPreviewState.NoNearbyPoint => "GUARDIAN SITE STATUS",
        _ => "GUARDIAN SITE STATUS",
    };

    public string GuardianStatusDetail => statusState switch
    {
        GuardianStatusPreviewState.HeadingChoice =>
            "Face the main structure and confirm the recorded heading.",
        GuardianStatusPreviewState.SiteOrigin =>
            "Move to the site centre and align the map origin.",
        GuardianStatusPreviewState.OnFootRelic =>
            "Approach the nearest relic tower on foot.",
        GuardianStatusPreviewState.NoNearbyPoint =>
            "No mapped survey point is within the current range.",
        _ => "Surveying Beta ruins #1",
    };

    public string GuardianOriginFooter =>
        "Use the aerial guide to center and orient the site. Type .map to return to the survey map.";

    public string GuardianOnFootFooter =>
        "Nearest relic tower A02 · 38.4 m · toggle shields 2x to conf.";

    public string GuardianStatusObeliskTitle => "A01 - HISTORY #12";

    public string GuardianStatusObeliskLogText => "Ram Tah log H12";

    public string GuardianStatusObeliskRequirementsText => $"Casket + {TotemArtifact}";

    public IReadOnlyList<GuardianArtifactRequirementViewModel>
        GuardianStatusObeliskArtifacts
    { get; } =
        [
            new("ca", "Casket", true, "+"),
            new("to", TotemArtifact, true, string.Empty),
        ];

    public string GuardianStatusObeliskMissionStatus =>
        "Decode this obelisk for the active mission.";

    public string GuardianStatusObeliskScanStatus => "READY TO SCAN";

    public string GuardianStatusObeliskFooter =>
        "Target A01 and scan with the required artifacts aboard.";

    public bool HasGuardianMaterialCapacityWarning => false;

    public string GuardianMaterialCapacityWarning => string.Empty;

    public string GuardianChoiceOneText =>
        statusState == GuardianStatusPreviewState.PoiChoice
            ? "Present"
            : "Alpha";

    public string GuardianChoiceTwoText =>
        statusState == GuardianStatusPreviewState.PoiChoice
            ? "Absent"
            : "Beta";

    public string GuardianChoiceThreeText =>
        statusState == GuardianStatusPreviewState.PoiChoice
            ? "Empty"
            : "Gamma";

    public bool IsGuardianChoiceThreeVisible => true;

    public bool IsGuardianChoiceOneSelected => false;

    public bool IsGuardianChoiceTwoSelected => true;

    public bool IsGuardianChoiceThreeSelected => false;

    public string SiteDistanceText => "42.6 m from site origin";

    public string NearbyPointText => "A01 - 18.4 m";

    public IReadOnlyList<GuardianSiteRowViewModel> CurrentSystemSites { get; }

    public string CurrentSystemGuardianTitle => "GUARDIAN SITES: 2";

    public IReadOnlyList<GuardianRamTahLogViewModel> CurrentRamTahLogs { get; }

    public bool HasCurrentRamTahLogs => true;

    public string CurrentRamTahTitle => "UNSCANNED RAM TAH LOGS: 3";

    public string ActiveSiteTitle => "GR 504 - Beta ruins #1";

    private static GuardianSiteRowViewModel CreateSiteRow(
        int siteId,
        string siteType,
        int index,
        int progress,
        bool isDestination,
        IReadOnlyList<string> ramTahLogs)
    {
        var reference = new GuardianSiteReference(
            siteId,
            GuardianSiteKind.Ruins,
            "Synuefe EU-Q c21-10",
            7_265_829_950_870_112_000,
            "A 3",
            BodyId: 3,
            siteType,
            index,
            DistanceToArrival: 1_122,
            new GalacticCoordinate(120.1, -85.2, 42.8),
            Latitude: -31.734,
            Longitude: 107.231,
            SiteHeading: 142,
            RelicTowerHeading: 37,
            SurveyProgress: progress,
            LastUpdated: ParseDateTimeOffset("2026-08-03T00:00:00Z"),
            RelatedStructure: null,
            RelatedStructureDistance: null);
        var visit = new GuardianSiteVisit(
            reference,
            FirstVisited: ParseDateTimeOffset("2026-08-01T00:00:00Z"),
            LastVisited: ParseDateTimeOffset("2026-08-03T00:00:00Z"),
            Notes: isDestination ? "Current survey destination" : string.Empty,
            SurveyProgress: progress,
            IsSurveyComplete: progress == 100,
            CommanderFilePath: null,
            HasCommanderData: progress > 0,
            Completion: null,
            RecordedObeliskOrLocationCount: progress > 0 ? 4 : 0);
        return new GuardianSiteRowViewModel(
            visit,
            distance: 0,
            isDestination,
            ramTahLogs,
            hasImages: false);
    }

    private static DateTimeOffset ParseDateTimeOffset(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}

internal enum GuardianStatusPreviewState
{
    ObeliskTarget,
    SiteTypeChoice,
    HeadingChoice,
    SiteOrigin,
    OnFootRelic,
    PoiChoice,
    NoNearbyPoint,
    GlideApproach,
}
