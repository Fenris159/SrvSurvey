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
    private const string TussockGenus = "Tussock";
    private const string YellowVariant = "Yellow";

    private static readonly OverlayEditorPreviewStateDefinition DefaultState =
        new("default", "Default");

    private static readonly Dictionary<
        string,
        OverlayEditorPreviewStateDefinition[]> PreviewStates =
        new Dictionary<
            string,
            OverlayEditorPreviewStateDefinition[]>(
            StringComparer.Ordinal)
        {
            ["PlotBioSystem"] = CreatePreviewStates(
                ("system-overview", "System overview"),
                ("body-predictions", "Body predictions"),
                ("body-identified", "Body identified")),
            ["PlotBioStatus"] = CreatePreviewStates(
                ("active-sample", "Active sample"),
                ("signal-summary", "Signal summary"),
                ("dss-required", "DSS required"),
                ("stale-sample", "Stale sample")),
            ["PlotGuardianStatus"] = CreatePreviewStates(
                ("obelisk", "Obelisk target"),
                ("site-type", "Site type choice"),
                ("heading", "Heading choice"),
                ("origin", "Site origin"),
                ("on-foot", "On-foot relic"),
                ("poi-choice", "POI choice"),
                ("no-point", "No nearby point"),
                ("glide", "Glide approach")),
            ["PlotFleetCarrierRoute"] = CreatePreviewStates(
                ("cooldown", "Jump cooldown"),
                ("scheduled", "Jump scheduled"),
                ("route-only", "Route only")),
            ["PlotPulse"] = CreatePreviewStates(
                ("cooling", "SCO cooling"),
                ("active", "SCO active"),
                ("ready", "SCO ready"),
                ("journal", "Journal pulse")),
        };

    private static readonly OverlayPreviewSimulationState State =
        OverlayPreviewSimulationState.Default;

    private static readonly BiologyRewardThresholds Thresholds =
        BiologyRewardThresholds.Default;

    private static OverlayEditorPreviewStateDefinition[]
        CreatePreviewStates(
            params (string Key, string DisplayName)[] states) =>
        states.Select(state => new OverlayEditorPreviewStateDefinition(
            state.Key,
            state.DisplayName)).ToArray();

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
                    1_000_000,
                    9_400_000,
                    true,
                    thresholds,
                    isGlobalRegionalFirst: true),
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
            Title = "System biology",
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
                            1_000_000,
                            9_400_000,
                            true,
                            thresholds,
                            isGlobalRegionalFirst: true),
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
                        BiologySignalRewardBandViewModel.Known(
                            7_600_000, false, false, thresholds),
                        BiologySignalRewardBandViewModel.Predicted(
                            1_690_000, 19_010_000, false, thresholds),
                        BiologySignalRewardBandViewModel.Predicted(
                            3_330_000, 7_620_000, false, thresholds),
                        BiologySignalRewardBandViewModel.Predicted(
                            10_100_000, 19_010_000, true, thresholds),
                    ],
                    RewardBucketOneMillions = thresholds.BucketOneMillions,
                    RewardBucketTwoMillions = thresholds.BucketTwoMillions,
                    RewardBucketThreeMillions = thresholds.BucketThreeMillions,
                },
            ],
            Organisms = [],
            RewardSummary = "Estimated reward:\n42.75 M – 106 M",
            RequiresDss = false,
        };
    }

    private static BiologySurveyViewModel CreateBiologyBodyDetail() =>
        CreateBiologyBodyPredictions();

    private static BiologySurveyViewModel CreateBiologyBodyPredictions() =>
        new()
        {
            Mode = BiologySurveyMode.Body,
            Title = "Body Predictions",
            SelectedBodyId = 3,
            Heading = State.CurrentBody,
            ProgressText = "4 biological signals",
            Bodies = [],
            Organisms = CreateBiologyOrganismPreviews(
                new("Stratum", "Limaxus", "Emerald", 1_360_000,
                    BiologyOrganismPreviewTraits.Prediction
                        | BiologyOrganismPreviewTraits.CommanderFirst
                        | BiologyOrganismPreviewTraits.HighlightedFirst),
                new("Stratum", "Paleas", "Emerald", 1_360_000,
                    BiologyOrganismPreviewTraits.Prediction),
                new("Bacterium", "Aurasus", "Lime", 1_000_000,
                    BiologyOrganismPreviewTraits.Prediction),
                new("Tubus", "Cavas", "Grey", 7_770_000,
                    BiologyOrganismPreviewTraits.Prediction
                        | BiologyOrganismPreviewTraits.RegionalFirst),
                new("Tubus", "Compagibus", "Grey", 11_870_000,
                    BiologyOrganismPreviewTraits.Prediction),
                new(TussockGenus, "Ignis", YellowVariant, 1_000_000,
                    BiologyOrganismPreviewTraits.Prediction
                        | BiologyOrganismPreviewTraits.GlobalRegionalFirst
                        | BiologyOrganismPreviewTraits.HighlightedFirst),
                new(TussockGenus, "Propagito", YellowVariant, 1_850_000,
                    BiologyOrganismPreviewTraits.Prediction
                        | BiologyOrganismPreviewTraits.CommanderFirst
                        | BiologyOrganismPreviewTraits.HighlightedFirst),
                new(TussockGenus, "Capillum", YellowVariant, 19_010_000,
                    BiologyOrganismPreviewTraits.Prediction)),
            RewardSummary = "Estimated reward:\n11.13 M – 33.24 M",
            RequiresDss = true,
        };

    private static BiologySurveyViewModel CreateBiologyBodyIdentified() =>
        new()
        {
            Mode = BiologySurveyMode.Body,
            Title = "Identified Bio",
            SelectedBodyId = 3,
            Heading = State.CurrentBody,
            ProgressText = "3 biological signals",
            Bodies = [],
            Organisms = CreateBiologyOrganismPreviews(
                new("Bacterium", "Acies", "Cobalt", 7_620_000,
                    BiologyOrganismPreviewTraits.CurrentSample
                        | BiologyOrganismPreviewTraits.CommanderFirst
                        | BiologyOrganismPreviewTraits.HighlightedFirst),
                new(TussockGenus, "Capillum", YellowVariant, 19_010_000,
                    BiologyOrganismPreviewTraits.Analyzed
                        | BiologyOrganismPreviewTraits.Dimmed),
                new("Stratum", "Tectonicas", "Emerald", 95_190_000,
                    BiologyOrganismPreviewTraits.RegionalFirst
                        | BiologyOrganismPreviewTraits.HighlightedFirst)),
            RewardSummary = "Known reward:\n121.82 M",
            FirstFootfallRewardSummary = "First-footfall total:\n609.10 M",
            RequiresDss = false,
            PredictionStatus = "DSS Scan Complete\nExact Organisms Identified",
            GeologicalSignalCount = 2,
            GeologicalSignals = ["Fumarole", "Lava spout"],
        };

    private static BiologyOrganismRowViewModel[] CreateBiologyOrganismPreviews(
        params BiologyOrganismPreviewSpec[] previews) =>
        previews.Select(CreateBiologyOrganismPreview).ToArray();

    private static BiologyOrganismRowViewModel CreateBiologyOrganismPreview(
        BiologyOrganismPreviewSpec preview)
    {
        var isPrediction = preview.Traits.HasFlag(
            BiologyOrganismPreviewTraits.Prediction);
        return new BiologyOrganismRowViewModel
        {
            DisplayName = isPrediction
                ? $"{preview.Genus} {preview.Species} - {preview.Variant}"
                : $"{preview.Genus} {preview.Species}",
            GenusName = preview.Genus,
            SpeciesName = preview.Species,
            VariantName = preview.Variant,
            Reward = preview.Reward,
            HasReward = true,
            IsPrediction = isPrediction,
            IsCommanderFirst = preview.Traits.HasFlag(
                BiologyOrganismPreviewTraits.CommanderFirst),
            IsRegionalFirst = preview.Traits.HasFlag(
                BiologyOrganismPreviewTraits.RegionalFirst),
            IsGlobalRegionalFirst = preview.Traits.HasFlag(
                BiologyOrganismPreviewTraits.GlobalRegionalFirst),
            IsHighlightedFirst = preview.Traits.HasFlag(
                BiologyOrganismPreviewTraits.HighlightedFirst),
            IsCurrentSample = preview.Traits.HasFlag(
                BiologyOrganismPreviewTraits.CurrentSample),
            IsAnalyzed = preview.Traits.HasFlag(
                BiologyOrganismPreviewTraits.Analyzed),
            ShouldDim = preview.Traits.HasFlag(
                BiologyOrganismPreviewTraits.Dimmed),
            RewardBucketOneMillions = Thresholds.BucketOneMillions,
            RewardBucketTwoMillions = Thresholds.BucketTwoMillions,
            RewardBucketThreeMillions = Thresholds.BucketThreeMillions,
        };
    }

    private readonly record struct BiologyOrganismPreviewSpec(
        string Genus,
        string Species,
        string Variant,
        long Reward,
        BiologyOrganismPreviewTraits Traits);

    [Flags]
    private enum BiologyOrganismPreviewTraits
    {
        None = 0,
        Prediction = 1 << 0,
        CommanderFirst = 1 << 1,
        RegionalFirst = 1 << 2,
        GlobalRegionalFirst = 1 << 3,
        HighlightedFirst = 1 << 4,
        CurrentSample = 1 << 5,
        Analyzed = 1 << 6,
        Dimmed = 1 << 7,
    }

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
                ActiveSample = null,
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
            string.Empty,
            "✓ 251,600 CR",
            string.Empty,
            BiologicalSignalCount: 0,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 0,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: false,
            IsDssCandidate: true,
            IsSurfaceScanned: true,
            IsLandable: true),
        new(
            "B 3",
            "HMC world",
            string.Empty,
            "842,310 CR",
            "2.84 M CR",
            BiologicalSignalCount: 6,
            AnalyzedBiologicalSignalCount: 0,
            GeologicalSignalCount: 2,
            AnalyzedGeologicalSignalCount: 0,
            IsHighlighted: true,
            IsDssCandidate: true,
            IsSurfaceScanned: false,
            IsLandable: true),
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
