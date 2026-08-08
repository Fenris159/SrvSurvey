using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// Builds real overlay view-models populated with representative simulated
/// Elite session data so the position editor hosts the same presentation
/// XAML as live panels.
/// </summary>
internal static class OverlayEditorPreviewCatalog
{
    private static readonly OverlayPreviewSimulationState State =
        OverlayPreviewSimulationState.Default;

    private static readonly BiologyRewardThresholds Thresholds =
        BiologyRewardThresholds.Default;

    public static object Create(string plotterName) => plotterName switch
    {
        "PlotBioStatus"
            or "PlotBioSystem"
            or "PlotBodyInfo"
            or "PlotFlightWarning"
            or "PlotFSS"
            or "PlotFSSInfo"
            or "PlotSysStatus" => CreateSystemSurveyPreview(plotterName),
        "PlotGuardians"
            or "PlotGuardianStatus"
            or "PlotGuardianSystem"
            or "PlotRamTah" => GuardianOverlayViewModel.CreateEditorPreview(),
        "PlotRouteBio" => CreateRouteBioPreview(),
        "PlotBuildCommodities" => CreateColonizationPreview(),
        "PlotFloatie" => CreateNotificationPreview(),
        "PlotFootCombat" or "PlotMassacre" => CreateCombatPreview(plotterName),
        "PlotGalMap" => CreateGalaxyMapPreview(),
        "PlotGrounded" or "PlotMiniTrack" => CreateSurfaceSurveyPreview(plotterName),
        "PlotHumanSite" => CreateHumanSitePreview(),
        "PlotJumpInfo" => CreateJumpInfoPreview(),
        "PlotFleetCarrierRoute" => CreateFleetCarrierRoutePreview(),
        "PlotMultiGameCommander" => CreateMultiCommanderPreview(),
        "PlotPriorScans" => CreatePriorScansPreview(),
        "PlotPulse" => CreatePulsePreview(),
        "PlotQuestMini" => CreateQuestPreview(),
        "PlotSphericalSearch" => CreateSphericalSearchPreview(),
        "PlotStationInfo" => CreateStationInfoPreview(),
        "PlotTrackTarget" => CreateGroundTargetPreview(),
        _ => throw new InvalidOperationException(
            $"No editor preview data context is defined for {plotterName}."),
    };

    private static SystemSurveyOverlayViewModel CreateSystemSurveyPreview(
        string plotterName)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-OverlayEditorPreview",
            "ui-settings.json");
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(settingsPath));
        survey.InstallEditorPreview(BuildSystemSurveyEditorState(plotterName));
        return new SystemSurveyOverlayViewModel(
            survey,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));
    }

    private static SystemSurveyEditorPreviewState BuildSystemSurveyEditorState(
        string plotterName)
    {
        var thresholds = Thresholds;
        var bioSystem = plotterName == "PlotBioSystem"
            ? CreateBiologySystemOverview()
            : CreateBiologyBodyDetail();
        var bioStatus = CreateBiologyStatus();
        var bodyInfo = CreateBodyInformation();
        return new SystemSurveyEditorPreviewState(
            Snapshot: CreatePreviewSnapshot(),
            BiologySurvey: bioSystem,
            BiologyStatus: bioStatus,
            BodyInformation: bodyInfo,
            FssBodies: CreateFssBodies(),
            DssBodies:
            [
                new SurveyBodyReferenceViewModel("B 1", false, false),
                new SurveyBodyReferenceViewModel("B 2 a", true, false),
                new SurveyBodyReferenceViewModel("C 3", false, false),
            ],
            BiologicalBodies:
            [
                new SurveyBodyReferenceViewModel("A4", false, false),
                new SurveyBodyReferenceViewModel("A5", false, false),
                new SurveyBodyReferenceViewModel("BC3", false, false),
            ],
            ShowNonBodySignals: true,
            NonBodySignalCount: 2,
            LastFssRewardBands:
            [
                BiologySignalRewardBandViewModel.Known(
                    1_000_000, false, false, thresholds),
                BiologySignalRewardBandViewModel.Predicted(
                    1_000_000, 9_400_000, false, thresholds),
                BiologySignalRewardBandViewModel.Known(
                    7_600_000, true, false, thresholds),
            ],
            LastFssRewardText: "10.89 M – 34.34 M CR",
            FlightWarningBodyName: State.CurrentBody,
            FlightWarningGravity: 2.84);
    }

    private static SystemScanSnapshot CreatePreviewSnapshot()
    {
        var body = CreatePreviewBody(
            bodyId: 3,
            name: State.CurrentBody,
            shortName: "B 3",
            biologicalSignals: 6,
            geologicalSignals: 2,
            isLandable: true,
            surfaceGravity: 28.4,
            isDssComplete: false);
        return new SystemScanSnapshot(
            SystemName: State.CurrentSystem,
            SystemAddress: 1,
            StarPosition: new GalacticCoordinate(100, 20, -40),
            Population: 0,
            ExpectedBodyCount: 24,
            HasDiscoveryScan: true,
            AllBodiesFound: false,
            FssBodyCount: 18,
            ScannedBodyCount: 18,
            DssCompletedBodyCount: 2,
            CurrentScanValue: 18_400_000,
            RawNonBodySignalCount: 2,
            NonBodySignalCount: 2,
            CurrentBodyId: 3,
            LastDetailedBodyId: 3,
            Bodies: [body]);
    }

    private static SystemScanBodySnapshot CreatePreviewBody(
        int bodyId,
        string name,
        string shortName,
        int biologicalSignals,
        int geologicalSignals,
        bool isLandable,
        double surfaceGravity,
        bool isDssComplete) =>
        new(
            BodyId: bodyId,
            Name: name,
            ShortName: shortName,
            Kind: SystemBodyKind.LandablePlanet,
            StarClass: null,
            PlanetClass: "High metal content body",
            IsLandable: isLandable,
            IsTerraformable: false,
            IsScanned: true,
            IsDssComplete: isDssComplete,
            WasDiscovered: true,
            WasMapped: false,
            WasFootfalled: false,
            IsFirstFootfall: false,
            HasRingParent: false,
            TidalLock: false,
            Mass: 0.42,
            DistanceFromArrivalLs: 1842,
            RadiusMeters: 4_200_000,
            SurfaceGravity: surfaceGravity,
            SurfaceTemperature: 187,
            SurfacePressure: 0.08,
            SemiMajorAxis: 0,
            AbsoluteMagnitude: 0,
            Atmosphere: "Thin carbon dioxide",
            AtmosphereType: "CarbonDioxide",
            Volcanism: null,
            BiologicalSignalCount: biologicalSignals,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: geologicalSignals,
            AnalyzedGeologicalSignalCount: 0,
            ScanValue: 842_310,
            EstimatedMappedValue: 2_840_000,
            CurrentScanValue: 842_310,
            ScanSequence: 1,
            AtmosphereComposition: new Dictionary<string, double>(
                StringComparer.Ordinal)
            {
                ["Carbon dioxide"] = 97.2,
                ["Sulphur dioxide"] = 2.8,
            },
            Materials: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["Iron"] = 32.1,
                ["Polonium"] = 0.8,
                ["Tellurium"] = 1.2,
            },
            Rings: [],
            Parents: [],
            Organisms: [],
            AnalyzedGeologicalSignals: []);

    private static BiologySurveyViewModel CreateBiologySystemOverview()
    {
        var thresholds = Thresholds;
        return new BiologySurveyViewModel
        {
            Mode = BiologySurveyMode.System,
            Heading = State.CurrentSystem,
            ProgressText = "4 of 12 biological signals analyzed",
            Bodies =
            [
                new BiologyBodyRowViewModel
                {
                    BodyId = 4,
                    Name = "A4",
                    AnalyzedSignalCount = 1,
                    SignalCount = 4,
                    KnownReward = 0,
                    MinimumReward = 10_890_000,
                    MaximumReward = 34_340_000,
                    HasPredictedReward = true,
                    RewardBands =
                    [
                        BiologySignalRewardBandViewModel.Known(
                            1_000_000, false, false, thresholds),
                        BiologySignalRewardBandViewModel.Known(
                            2_400_000, false, false, thresholds),
                        BiologySignalRewardBandViewModel.Known(
                            7_600_000, true, false, thresholds),
                        BiologySignalRewardBandViewModel.Unknown(thresholds),
                    ],
                    RewardBucketOneMillions = thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = thresholds.BucketThreeMillions,
                },
                new BiologyBodyRowViewModel
                {
                    BodyId = 5,
                    Name = "A5",
                    AnalyzedSignalCount = 0,
                    SignalCount = 4,
                    MinimumReward = 9_470_000,
                    MaximumReward = 31_540_000,
                    HasPredictedReward = true,
                    RewardBands =
                    [
                        BiologySignalRewardBandViewModel.Known(
                            2_200_000, false, false, thresholds),
                        BiologySignalRewardBandViewModel.Known(
                            5_200_000, false, false, thresholds),
                        BiologySignalRewardBandViewModel.Predicted(
                            1_000_000, 9_400_000, false, thresholds),
                        BiologySignalRewardBandViewModel.Known(
                            13_000_000, true, false, thresholds),
                    ],
                    RewardBucketOneMillions = thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = thresholds.BucketThreeMillions,
                },
                new BiologyBodyRowViewModel
                {
                    BodyId = 8,
                    Name = "BC3",
                    AnalyzedSignalCount = 2,
                    SignalCount = 2,
                    KnownReward = 20_700_000,
                    MinimumReward = 20_700_000,
                    MaximumReward = 20_700_000,
                    RewardBands =
                    [
                        BiologySignalRewardBandViewModel.Known(
                            4_100_000, false, false, thresholds),
                        BiologySignalRewardBandViewModel.Known(
                            16_600_000, false, false, thresholds),
                    ],
                    RewardBucketOneMillions = thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = thresholds.BucketThreeMillions,
                },
                new BiologyBodyRowViewModel
                {
                    BodyId = 9,
                    Name = "BC4",
                    AnalyzedSignalCount = 1,
                    SignalCount = 2,
                    MinimumReward = 1_690_000,
                    MaximumReward = 19_010_000,
                    HasPredictedReward = true,
                    HasCanonnSignals = true,
                    RewardBands =
                    [
                        BiologySignalRewardBandViewModel.Predicted(
                            1_690_000, 19_010_000, false, thresholds),
                        BiologySignalRewardBandViewModel.Known(
                            7_600_000, false, false, thresholds),
                    ],
                    RewardBucketOneMillions = thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = thresholds.BucketThreeMillions,
                },
            ],
            Organisms = [],
            RewardSummary = "Estimated reward: 42.75 M – 106 M CR",
            RequiresDss = false,
        };
    }

    private static BiologySurveyViewModel CreateBiologyBodyDetail() =>
        new()
        {
            Mode = BiologySurveyMode.Body,
            SelectedBodyId = 3,
            Heading = $"{State.CurrentBody} biology",
            ProgressText = "2 biological signals",
            Bodies = [],
            Organisms =
            [
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Bacterium Bullaris - Cobalt",
                    GenusName = "Bacterium",
                    Reward = 1_150_000,
                    HasReward = true,
                    IsPrediction = true,
                    IsHighlightedFirst = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Fonticulua Digitos - Emerald",
                    GenusName = "Fonticulua",
                    Reward = 1_800_000,
                    HasReward = true,
                    IsPrediction = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
            ],
            RewardSummary = "Estimated reward: 2.96 M CR",
            RequiresDss = true,
        };

    private static BiologyStatusViewModel CreateBiologyStatus() =>
        new(
            BodyId: 3,
            BodyName: State.CurrentBody,
            AnalyzedSignalCount: 2,
            SignalCount: 6,
            Signals:
            [
                new BiologyStatusSignalViewModel(
                    "Bacterium Acies",
                    "sample 2 of 3",
                    IsAnalyzed: false,
                    IsActive: true,
                    IsGeological: false),
                new BiologyStatusSignalViewModel(
                    "Tussock Capillum",
                    "analyzed",
                    IsAnalyzed: true,
                    IsActive: false,
                    IsGeological: false),
            ],
            ActiveSample: new BiologyActiveSampleViewModel(
                DisplayName: "Bacterium Acies",
                Stage: 2,
                RequiredDistanceMeters: 500,
                NearestDistanceMeters: 320,
                RemainingDistanceMeters: 180,
                Reward: 1_000_000,
                IsFirstFootfall: false),
            CodexNotification: null,
            RequiresDss: false,
            Warning: string.Empty,
            Footer: "BIO SAMPLE 2 / 3",
            TemperatureRange: null,
            HasCodexImageIndicator: true,
            HasCodexImage: true);

    private static BodyInformationViewModel CreateBodyInformation() =>
        new(
            BodyId: 3,
            Name: State.CurrentBody,
            BodyClass: "High metal content world",
            Distance: "1,842 ls",
            Markers: "LANDABLE · BIO 6 · GEO 2",
            ScanValue: "842,310 CR",
            MappedValue: "2.84 M CR",
            Temperature: "187 K",
            Gravity: "2.84 g",
            IsHighGravity: true,
            IsHighValue: true,
            Pressure: "0.08 atm",
            IsPlanet: true,
            BiologicalSignals: "6 biological",
            BiologicalReward: "10.89 – 34.34 M CR",
            GeologicalSignals: "2 geological",
            Volcanism: "No volcanism",
            Atmosphere: "Thin carbon dioxide",
            AtmosphereComposition:
            [
                new BodyCompositionRowViewModel("Carbon dioxide", "97.2%", false),
                new BodyCompositionRowViewModel("Sulphur dioxide", "2.8%", false),
            ],
            Materials:
            [
                new BodyCompositionRowViewModel("Iron", "32.1%", false),
                new BodyCompositionRowViewModel("Polonium", "0.8%", true),
                new BodyCompositionRowViewModel("Tellurium", "1.2%", true),
            ],
            Rings: [],
            IsScanRequired: false);

    private static IReadOnlyList<FssBodyRowViewModel> CreateFssBodies() =>
    [
        new(
            "B 1",
            "Class II gas giant",
            string.Empty,
            "126,400 CR",
            string.Empty,
            BiologicalSignalCount: 0,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 0,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: false,
            IsDssCandidate: false),
        new(
            "B 3",
            "HMC world",
            "BIO 6 · GEO 2",
            "842,310 CR",
            "2.84 M CR",
            BiologicalSignalCount: 6,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 2,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: true,
            IsDssCandidate: true),
        new(
            "C 1",
            "Water world",
            "TERRAFORMABLE",
            "1.24 M CR",
            string.Empty,
            BiologicalSignalCount: 0,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 0,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: false,
            IsDssCandidate: true),
    ];

    private static object CreateRouteBioPreview() =>
        OverlayEditorPreviewFactories.CreateRouteBio();

    private static object CreateColonizationPreview() =>
        OverlayEditorPreviewFactories.CreateColonization();

    private static object CreateNotificationPreview() =>
        OverlayEditorPreviewFactories.CreateNotification();

    private static object CreateCombatPreview(string plotterName) =>
        OverlayEditorPreviewFactories.CreateCombat();

    private static object CreateGalaxyMapPreview() =>
        OverlayEditorPreviewFactories.CreateGalaxyMap();

    private static object CreateSurfaceSurveyPreview(string plotterName) =>
        OverlayEditorPreviewFactories.CreateSurfaceSurvey();

    private static object CreateHumanSitePreview() =>
        OverlayEditorPreviewFactories.CreateHumanSite();

    private static object CreateJumpInfoPreview() =>
        OverlayEditorPreviewFactories.CreateJumpInfo();

    private static object CreateFleetCarrierRoutePreview() =>
        OverlayEditorPreviewFactories.CreateFleetCarrierRoute();

    private static object CreateMultiCommanderPreview() =>
        OverlayEditorPreviewFactories.CreateMultiCommander();

    private static object CreatePriorScansPreview() =>
        OverlayEditorPreviewFactories.CreatePriorScans();

    private static object CreatePulsePreview() =>
        OverlayEditorPreviewFactories.CreatePulse();

    private static object CreateQuestPreview() =>
        OverlayEditorPreviewFactories.CreateQuest();

    private static object CreateSphericalSearchPreview() =>
        OverlayEditorPreviewFactories.CreateSphericalSearch();

    private static object CreateStationInfoPreview() =>
        OverlayEditorPreviewFactories.CreateStationInfo();

    private static object CreateGroundTargetPreview() =>
        OverlayEditorPreviewFactories.CreateGroundTarget();
}
