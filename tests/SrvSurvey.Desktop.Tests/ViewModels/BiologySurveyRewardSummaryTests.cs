using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BiologySurveyRewardSummaryTests
{
    [Fact]
    public void IdentifiedBodyFormatsKnownThousandsWithPendingSignals()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 2,
            isDssComplete: true,
            isFirstFootfall: false,
            [KnownOrganism(2_500)]
        );

        BiologySurveyViewModel survey = CreateBodyDetail(snapshot, disablePredictions: true);

        Assert.Equal("Known reward:\n2.5 K + pending", survey.RewardSummary);
        Assert.Empty(survey.FirstFootfallRewardSummary);
    }

    [Fact]
    public void IdentifiedBodyFormatsRawKnownRewardAndFirstFootfallTotal()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 1,
            isDssComplete: true,
            isFirstFootfall: true,
            [KnownOrganism(500)]
        );

        BiologySurveyViewModel survey = CreateBodyDetail(snapshot, disablePredictions: true);

        Assert.Equal("Known reward:\n500", survey.RewardSummary);
        Assert.Equal("First-footfall total:\n2.5 K", survey.FirstFootfallRewardSummary);
    }

    [Fact]
    public void IdentifiedBodyFormatsPredictedFirstFootfallRangeWithPendingSignals()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 2,
            isDssComplete: true,
            isFirstFootfall: true,
            []
        );
        BiologySurveyBodyDetailOptions options = CreatePredictionOptions(
            ("Arcus", "Green", 1_000),
            ("Coronamus", "Lime", 2_000)
        );

        BiologySurveyViewModel survey = CreateBodyDetail(snapshot, options);

        Assert.Equal("Reward pending identification", survey.RewardSummary);
        Assert.Equal("First-footfall estimate:\n5.0 K – 10.0 K + pending", survey.FirstFootfallRewardSummary);
    }

    [Fact]
    public void IdentifiedBodyFormatsSinglePredictedFirstFootfallValue()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 1,
            isDssComplete: true,
            isFirstFootfall: true,
            []
        );
        BiologySurveyBodyDetailOptions options = CreatePredictionOptions(("Arcus", "Green", 1_000));

        BiologySurveyViewModel survey = CreateBodyDetail(snapshot, options);

        Assert.Empty(survey.RewardSummary);
        Assert.Equal("First-footfall estimate:\n5.0 K", survey.FirstFootfallRewardSummary);
    }

    [Fact]
    public void IdentifiedBodyFormatsUnscannedOrganismAsEstimatedReward()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 1,
            isDssComplete: true,
            isFirstFootfall: false,
            [UnscannedOrganism(6_284_600)]
        );

        BiologySurveyViewModel survey = CreateBodyDetail(snapshot, disablePredictions: true);

        Assert.Equal("Estimated reward:\n6.28 M CR", survey.RewardSummary);
    }

    [Fact]
    public void IdentifiedBodyFormatsMixedScannedAndUnscannedAsEstimatedReward()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 2,
            isDssComplete: true,
            isFirstFootfall: true,
            [KnownOrganism(1_000_000), UnscannedOrganism(2_000_000)]
        );

        BiologySurveyViewModel survey = CreateBodyDetail(snapshot, disablePredictions: true);

        Assert.Equal("Estimated reward:\n3.00 M CR", survey.RewardSummary);
        Assert.Equal("First-footfall estimate:\n15.00 M", survey.FirstFootfallRewardSummary);
    }

    [Fact]
    public void IdentifiedBodyFormatsUnscannedWithPendingSignalsAsEstimatedRange()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 2,
            isDssComplete: true,
            isFirstFootfall: false,
            [UnscannedOrganism(1_000_000)]
        );
        BiologySurveyBodyDetailOptions options = CreatePredictionOptions(("Arcus", "Green", 500_000));

        BiologySurveyViewModel survey = CreateBodyDetail(snapshot, options);

        Assert.StartsWith("Estimated reward:\n", survey.RewardSummary);
        Assert.Contains("pending", survey.RewardSummary);
    }

    [Fact]
    public void SystemOverviewOmitsZeroKnownRewardWithoutPendingSignals()
    {
        SystemScanSnapshot snapshot = CreateSnapshot(
            biologicalSignalCount: 1,
            isDssComplete: false,
            isFirstFootfall: false,
            [KnownOrganism(reward: null)]
        );

        var survey = BiologySurveyViewModel.CreateSystemOverview(
            snapshot,
            status: null,
            new BiologySurveySystemOverviewOptions(disablePredictions: true)
        );

        Assert.NotNull(survey);
        Assert.Empty(survey.RewardSummary);
    }

    private static BiologySurveyViewModel CreateBodyDetail(SystemScanSnapshot snapshot, bool disablePredictions)
    {
        return CreateBodyDetail(
            snapshot,
            new BiologySurveyBodyDetailOptions(
                highlightRegionalFirsts: false,
                dimAnalyzedOrganisms: false,
                hideGeoCount: false,
                disablePredictions
            )
        );
    }

    private static BiologySurveyViewModel CreateBodyDetail(
        SystemScanSnapshot snapshot,
        BiologySurveyBodyDetailOptions options
    )
    {
        var survey = BiologySurveyViewModel.CreateBodyDetail(snapshot, bodyId: 1, ExobiologySnapshot.Empty, options);
        return Assert.IsType<BiologySurveyViewModel>(survey);
    }

    private static BiologySurveyBodyDetailOptions CreatePredictionOptions(
        params (string Species, string Variant, long Reward)[] predictions
    )
    {
        BiologyCriteriaNode[] criteria = predictions
            .Select(prediction => new BiologyCriteriaNode(
                "Aleoida",
                prediction.Species,
                prediction.Variant,
                [],
                [],
                false,
                null
            ))
            .ToArray();
        ExobiologyReference[] references = predictions
            .Select(
                (prediction, index) =>
                    new ExobiologyReference(
                        10_001 + index,
                        $"$Codex_Aleoida_{prediction.Species}_{prediction.Variant};",
                        $"$Codex_Aleoida_{prediction.Species};",
                        $"Aleoida {prediction.Species} - {prediction.Variant}",
                        prediction.Reward
                    )
            )
            .ToArray();

        return new BiologySurveyBodyDetailOptions(
            highlightRegionalFirsts: false,
            dimAnalyzedOrganisms: false,
            hideGeoCount: false,
            disablePredictions: false
        )
        {
            PredictionEvaluator = new BiologyPredictionEvaluator(new BiologyCriteriaCatalog(criteria)),
            ReferenceCatalog = new ExobiologyReferenceCatalog(references),
        };
    }

    private static SystemScanSnapshot CreateSnapshot(
        int biologicalSignalCount,
        bool isDssComplete,
        bool isFirstFootfall,
        IReadOnlyList<SystemOrganismSnapshot> organisms
    )
    {
        var scan = new SystemScanState();
        scan.Apply(Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""));
        scan.Apply(
            Parse(
                """{"event":"Scan","SystemAddress":42,"BodyName":"Test","BodyID":0,"StarType":"G","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""
            )
        );
        scan.Apply(
            Parse(
                """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Parents":[{"Star":0}],"PlanetClass":"Rocky body","Landable":true,"SurfaceTemperature":180,"SurfaceGravity":2,"SurfacePressure":1000,"SemiMajorAxis":100000}"""
            )
        );
        scan.Apply(
            Parse(
                $$"""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":{{biologicalSignalCount}}}]}"""
            )
        );

        SystemScanSnapshot snapshot = scan.CreateSnapshot();
        SystemScanBodySnapshot[] bodies = snapshot
            .Bodies.Select(body =>
                body.BodyId == 1
                    ? body with
                    {
                        IsDssComplete = isDssComplete,
                        IsFirstFootfall = isFirstFootfall,
                        Organisms = organisms,
                    }
                    : body
            )
            .ToArray();
        return snapshot with { Bodies = bodies };
    }

    private static SystemOrganismSnapshot KnownOrganism(long? reward)
    {
        return new SystemOrganismSnapshot(
            "$Codex_Ent_Aleoids_Genus_Name;",
            "Aleoida",
            "$Codex_Ent_Aleoids_01_Name;",
            "Aleoida Arcus",
            "$Codex_Ent_Aleoids_01_B_Name;",
            "Aleoida Arcus - Green",
            10_001,
            reward,
            IsScanned: true,
            IsAnalyzed: true,
            IsRegionalFirst: false
        );
    }

    private static SystemOrganismSnapshot UnscannedOrganism(long reward)
    {
        return new SystemOrganismSnapshot(
            "$Codex_Ent_Aleoids_Genus_Name;",
            "Aleoida",
            "$Codex_Ent_Aleoids_02_Name;",
            "Aleoida Coronamus",
            "$Codex_Ent_Aleoids_02_C_Name;",
            "Aleoida Coronamus - Lime",
            10_002,
            reward,
            IsScanned: false,
            IsAnalyzed: false,
            IsRegionalFirst: false
        );
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? value, out string? error), error);
        return Assert.IsType<JournalEventEnvelope>(value);
    }
}
