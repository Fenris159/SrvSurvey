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
    private static readonly OverlayEditorPreviewStateDefinition DefaultState =
        new("default", "Default");

    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyList<OverlayEditorPreviewStateDefinition>> PreviewStates =
        new Dictionary<
            string,
            IReadOnlyList<OverlayEditorPreviewStateDefinition>>(
            StringComparer.Ordinal)
        {
            ["PlotBioSystem"] =
            [
                new("system-overview", "System overview"),
                new("body-predictions", "Body predictions"),
                new("body-identified", "Body identified"),
            ],
            ["PlotBioStatus"] =
            [
                new("active-sample", "Active sample"),
                new("signal-summary", "Signal summary"),
                new("dss-required", "DSS required"),
                new("stale-sample", "Stale sample"),
            ],
            ["PlotGuardianStatus"] =
            [
                new("obelisk", "Obelisk target"),
                new("site-type", "Site type choice"),
                new("heading", "Heading choice"),
                new("origin", "Site origin"),
                new("on-foot", "On-foot relic"),
                new("poi-choice", "POI choice"),
                new("no-point", "No nearby point"),
                new("glide", "Glide approach"),
            ],
            ["PlotFleetCarrierRoute"] =
            [
                new("cooldown", "Jump cooldown"),
                new("scheduled", "Jump scheduled"),
                new("route-only", "Route only"),
            ],
            ["PlotPulse"] =
            [
                new("cooling", "SCO cooling"),
                new("active", "SCO active"),
                new("ready", "SCO ready"),
                new("journal", "Journal pulse"),
            ],
        };

    private static readonly OverlayPreviewSimulationState State =
        OverlayPreviewSimulationState.Default;

    private static readonly BiologyRewardThresholds Thresholds =
        BiologyRewardThresholds.Default;

    public static IReadOnlyList<OverlayEditorPreviewStateDefinition> GetStates(
        string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        return PreviewStates.TryGetValue(plotterName, out var states)
            ? states
            : [DefaultState];
    }

    public static object Create(string plotterName) => Create(plotterName, 0);

    public static object Create(string plotterName, int stateIndex)
    {
        var states = GetStates(plotterName);
        if (stateIndex < 0 || stateIndex >= states.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stateIndex),
                stateIndex,
                $"Preview state index must be from 0 to {states.Count - 1}.");
        }

        return Create(plotterName, states[stateIndex].Key);
    }

    private static object Create(string plotterName, string previewState) =>
        plotterName switch
    {
        "PlotBioStatus"
            or "PlotBioSystem"
            or "PlotBodyInfo"
            or "PlotFlightWarning"
            or "PlotFSS"
            or "PlotFSSInfo"
            or "PlotSysStatus" =>
                CreateSystemSurveyPreview(plotterName, previewState),
        "PlotGuardians"
            or "PlotGuardianSystem"
            or "PlotRamTah" => GuardianOverlayViewModel.CreateEditorPreview(),
        "PlotGuardianStatus" => GuardianOverlayViewModel.CreateEditorPreview(
            ParseGuardianStatusState(previewState)),
        "PlotRouteBio" => CreateRouteBioPreview(),
        "PlotBuildCommodities" => CreateColonizationPreview(),
        "PlotFloatie" => CreateNotificationPreview(),
        "PlotFootCombat" or "PlotMassacre" => CreateCombatPreview(),
        "PlotGalMap" => CreateGalaxyMapPreview(),
        "PlotGrounded" or "PlotMiniTrack" => CreateSurfaceSurveyPreview(),
        "PlotHumanSite" => CreateHumanSitePreview(),
        "PlotJumpInfo" => CreateJumpInfoPreview(),
        "PlotFleetCarrierRoute" => CreateFleetCarrierRoutePreview(
            ParseFleetCarrierRouteState(previewState)),
        "PlotMultiGameCommander" => CreateMultiCommanderPreview(),
        "PlotPriorScans" => CreatePriorScansPreview(),
        "PlotPulse" => CreatePulsePreview(ParsePulseState(previewState)),
        "PlotQuestMini" => CreateQuestPreview(),
        "PlotSphericalSearch" => CreateSphericalSearchPreview(),
        "PlotStationInfo" => CreateStationInfoPreview(),
        "PlotTrackTarget" => CreateGroundTargetPreview(),
        _ => throw new InvalidOperationException(
            $"No editor preview data context is defined for {plotterName}."),
    };

    private static SystemSurveyOverlayViewModel CreateSystemSurveyPreview(
        string plotterName,
        string previewState)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-OverlayEditorPreview",
            "ui-settings.json");
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(settingsPath));
        survey.InstallEditorPreview(BuildSystemSurveyEditorState(
            plotterName,
            previewState));
        return new SystemSurveyOverlayViewModel(
            survey,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));
    }

    private static SystemSurveyEditorPreviewState BuildSystemSurveyEditorState(
        string plotterName,
        string previewState)
    {
        var thresholds = Thresholds;
        var bioSystem = plotterName == "PlotBioSystem"
            ? previewState switch
            {
                "body-predictions" => CreateBiologyBodyPredictions(),
                "body-identified" => CreateBiologyBodyIdentified(),
                _ => CreateBiologySystemOverview(),
            }
            : CreateBiologyBodyDetail();
        var bioStatus = plotterName == "PlotBioStatus"
            ? CreateBiologyStatus(previewState)
            : CreateBiologyStatus("active-sample");
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
        var body = CreatePreviewBody();
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

    private static SystemScanBodySnapshot CreatePreviewBody() =>
        new(
            BodyId: 3,
            Name: State.CurrentBody,
            ShortName: "B 3",
            Kind: SystemBodyKind.LandablePlanet,
            StarClass: null,
            PlanetClass: "High metal content body",
            IsLandable: true,
            IsTerraformable: false,
            IsScanned: true,
            IsDssComplete: false,
            WasDiscovered: true,
            WasMapped: false,
            WasFootfalled: false,
            IsFirstFootfall: false,
            HasRingParent: false,
            TidalLock: false,
            Mass: 0.42,
            DistanceFromArrivalLs: 1842,
            RadiusMeters: 4_200_000,
            SurfaceGravity: 28.4,
            SurfaceTemperature: 187,
            SurfacePressure: 0.08,
            SemiMajorAxis: 0,
            AbsoluteMagnitude: 0,
            Atmosphere: "Thin carbon dioxide",
            AtmosphereType: "CarbonDioxide",
            Volcanism: null,
            BiologicalSignalCount: 6,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 2,
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
                    BodySubtype = "Rocky body",
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
                    BodySubtype = "Earth-like world",
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
                    BodySubtype = "High metal content world",
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
                    BodySubtype = "Rocky ice body",
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
        CreateBiologyBodyPredictions();

    private static BiologySurveyViewModel CreateBiologyBodyPredictions() =>
        new()
        {
            Mode = BiologySurveyMode.Body,
            SelectedBodyId = 3,
            Heading = $"{State.CurrentBody} biology",
            ProgressText = "4 biological signals",
            Bodies = [],
            Organisms =
            [
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Stratum Limaxus - Emerald",
                    GenusName = "Stratum",
                    SpeciesName = "Limaxus",
                    VariantName = "Emerald",
                    Reward = 1_360_000,
                    HasReward = true,
                    IsPrediction = true,
                    IsCommanderFirst = true,
                    IsHighlightedFirst = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Stratum Paleas - Emerald",
                    GenusName = "Stratum",
                    SpeciesName = "Paleas",
                    VariantName = "Emerald",
                    Reward = 1_360_000,
                    HasReward = true,
                    IsPrediction = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Bacterium Aurasus - Lime",
                    GenusName = "Bacterium",
                    SpeciesName = "Aurasus",
                    VariantName = "Lime",
                    Reward = 1_000_000,
                    HasReward = true,
                    IsPrediction = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Tubus Cavas - Grey",
                    GenusName = "Tubus",
                    SpeciesName = "Cavas",
                    VariantName = "Grey",
                    Reward = 7_770_000,
                    HasReward = true,
                    IsPrediction = true,
                    IsRegionalFirst = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Tubus Compagibus - Grey",
                    GenusName = "Tubus",
                    SpeciesName = "Compagibus",
                    VariantName = "Grey",
                    Reward = 11_870_000,
                    HasReward = true,
                    IsPrediction = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Tussock Ignis - Yellow",
                    GenusName = "Tussock",
                    SpeciesName = "Ignis",
                    VariantName = "Yellow",
                    Reward = 1_000_000,
                    HasReward = true,
                    IsPrediction = true,
                    IsGlobalRegionalFirst = true,
                    IsHighlightedFirst = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Tussock Propagito - Yellow",
                    GenusName = "Tussock",
                    SpeciesName = "Propagito",
                    VariantName = "Yellow",
                    Reward = 1_850_000,
                    HasReward = true,
                    IsPrediction = true,
                    IsCommanderFirst = true,
                    IsHighlightedFirst = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Tussock Capillum - Yellow",
                    GenusName = "Tussock",
                    SpeciesName = "Capillum",
                    VariantName = "Yellow",
                    Reward = 19_010_000,
                    HasReward = true,
                    IsPrediction = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
            ],
            RewardSummary = "Estimated reward: 11.13 M – 33.24 M CR",
            RequiresDss = true,
        };

    private static BiologySurveyViewModel CreateBiologyBodyIdentified() =>
        new()
        {
            Mode = BiologySurveyMode.Body,
            SelectedBodyId = 3,
            Heading = $"{State.CurrentBody} biology",
            ProgressText = "3 of 6 biological signals analyzed",
            Bodies = [],
            Organisms =
            [
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Bacterium Acies",
                    GenusName = "Bacterium",
                    SpeciesName = "Acies",
                    VariantName = "Cobalt",
                    Reward = 7_620_000,
                    HasReward = true,
                    IsCurrentSample = true,
                    IsCommanderFirst = true,
                    IsHighlightedFirst = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Tussock Capillum",
                    GenusName = "Tussock",
                    SpeciesName = "Capillum",
                    VariantName = "Yellow",
                    Reward = 19_010_000,
                    HasReward = true,
                    IsAnalyzed = true,
                    ShouldDim = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
                new BiologyOrganismRowViewModel
                {
                    DisplayName = "Stratum Tectonicas",
                    GenusName = "Stratum",
                    SpeciesName = "Tectonicas",
                    VariantName = "Emerald",
                    Reward = 95_190_000,
                    HasReward = true,
                    IsGlobalRegionalFirst = true,
                    IsHighlightedFirst = true,
                    RewardBucketOneMillions = Thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
                },
            ],
            RewardSummary = "Known reward: 121.82 M CR",
            FirstFootfallRewardSummary = "First-footfall total: 609.10 M CR",
            RequiresDss = false,
            PredictionStatus = "DSS scan complete; exact organisms identified.",
            GeologicalSignalCount = 2,
            GeologicalSignals = ["Fumarole", "Lava spout"],
        };

    private static BiologyStatusViewModel CreateBiologyStatus(
        string previewState)
    {
        var active = CreateActiveBiologyStatus();
        return previewState switch
        {
            "signal-summary" => active with
            {
                ActiveSample = null,
                Footer = "Select an organism to begin sampling.",
            },
            "dss-required" => active with
            {
                AnalyzedSignalCount = 0,
                Signals = [],
                ActiveSample = null,
                RequiresDss = true,
                Footer = "Map this body to resolve biological signals.",
                HasCodexImageIndicator = false,
            },
            "stale-sample" => active with
            {
                Warning = "Active sample belongs to another body",
                Footer = string.Empty,
                IsStaleActiveSample = true,
            },
            _ => active,
        };
    }

    private static BiologyStatusViewModel CreateActiveBiologyStatus() =>
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
            IsDssCandidate: false,
            IsSurfaceScanned: false),
        new(
            "B 2",
            "Rocky body",
            "LANDABLE",
            "✓ 251,600 CR",
            string.Empty,
            BiologicalSignalCount: 0,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 0,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: false,
            IsDssCandidate: true,
            IsSurfaceScanned: true),
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
            IsDssCandidate: true,
            IsSurfaceScanned: false),
        new(
            "C 1",
            "Water world",
            "TERRAFORMABLE",
            "✓ 1.24 M CR",
            string.Empty,
            BiologicalSignalCount: 0,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 0,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: false,
            IsDssCandidate: true,
            IsSurfaceScanned: true),
        new(
            "C 10",
            "Ammonia world",
            "TERRAFORMABLE",
            "✓ 1.68 M CR",
            string.Empty,
            BiologicalSignalCount: 0,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 0,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: false,
            IsDssCandidate: true,
            IsSurfaceScanned: true),
    ];

    private static RouteBioOverlayViewModel CreateRouteBioPreview() =>
        OverlayEditorPreviewFactories.CreateRouteBio();

    private static ColonizationCommodityOverlayViewModel CreateColonizationPreview() =>
        OverlayEditorPreviewFactories.CreateColonization();

    private static NotificationViewModel CreateNotificationPreview() =>
        OverlayEditorPreviewFactories.CreateNotification();

    private static CombatOverlayViewModel CreateCombatPreview() =>
        OverlayEditorPreviewFactories.CreateCombat();

    private static GalaxyMapOverlayViewModel CreateGalaxyMapPreview() =>
        OverlayEditorPreviewFactories.CreateGalaxyMap();

    private static SurfaceSurveyOverlayViewModel CreateSurfaceSurveyPreview() =>
        OverlayEditorPreviewFactories.CreateSurfaceSurvey();

    private static HumanSiteOverlayViewModel CreateHumanSitePreview() =>
        OverlayEditorPreviewFactories.CreateHumanSite();

    private static JumpInfoOverlayViewModel CreateJumpInfoPreview() =>
        OverlayEditorPreviewFactories.CreateJumpInfo();

    private static FleetCarrierRouteOverlayViewModel CreateFleetCarrierRoutePreview(
        FleetCarrierRouteEditorPreviewState state) =>
        OverlayEditorPreviewFactories.CreateFleetCarrierRoute(state);

    private static CommanderInstancesViewModel CreateMultiCommanderPreview() =>
        OverlayEditorPreviewFactories.CreateMultiCommander();

    private static PriorScansOverlayViewModel CreatePriorScansPreview() =>
        OverlayEditorPreviewFactories.CreatePriorScans();

    private static PulseOverlayViewModel CreatePulsePreview(
        PulseEditorPreviewState state) =>
        OverlayEditorPreviewFactories.CreatePulse(state);

    private static QuestIndicatorViewModel CreateQuestPreview() =>
        OverlayEditorPreviewFactories.CreateQuest();

    private static SphericalSearchOverlayViewModel CreateSphericalSearchPreview() =>
        OverlayEditorPreviewFactories.CreateSphericalSearch();

    private static StationInfoOverlayViewModel CreateStationInfoPreview() =>
        OverlayEditorPreviewFactories.CreateStationInfo();

    private static GroundTargetOverlayViewModel CreateGroundTargetPreview() =>
        OverlayEditorPreviewFactories.CreateGroundTarget();

    private static GuardianStatusPreviewState ParseGuardianStatusState(
        string state) => state switch
        {
            "site-type" => GuardianStatusPreviewState.SiteTypeChoice,
            "heading" => GuardianStatusPreviewState.HeadingChoice,
            "origin" => GuardianStatusPreviewState.SiteOrigin,
            "on-foot" => GuardianStatusPreviewState.OnFootRelic,
            "poi-choice" => GuardianStatusPreviewState.PoiChoice,
            "no-point" => GuardianStatusPreviewState.NoNearbyPoint,
            "glide" => GuardianStatusPreviewState.GlideApproach,
            _ => GuardianStatusPreviewState.ObeliskTarget,
        };

    private static FleetCarrierRouteEditorPreviewState
        ParseFleetCarrierRouteState(string state) => state switch
        {
            "scheduled" => FleetCarrierRouteEditorPreviewState.Scheduled,
            "route-only" => FleetCarrierRouteEditorPreviewState.RouteOnly,
            _ => FleetCarrierRouteEditorPreviewState.Cooldown,
        };

    private static PulseEditorPreviewState ParsePulseState(string state) =>
        state switch
        {
            "active" => PulseEditorPreviewState.ScoActive,
            "ready" => PulseEditorPreviewState.ScoReady,
            "journal" => PulseEditorPreviewState.JournalPulse,
            _ => PulseEditorPreviewState.ScoCooling,
        };
}

internal sealed record OverlayEditorPreviewStateDefinition(
    string Key,
    string DisplayName);
