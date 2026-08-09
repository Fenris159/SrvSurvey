using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BiologyOrganismGroupViewModelTests
{
    [Fact]
    public void GroupsAnyNumberOfPredictedSpeciesUnderOneCompactGenusRow()
    {
        BiologyOrganismRowViewModel[] organisms =
        [
            Prediction("Ignis", "Yellow", 1_000_000, isCommanderFirst: true),
            Prediction("Propagito", "Yellow", 1_850_000),
            Prediction("Capillum", "Yellow", 19_010_000),
        ];

        var group = Assert.Single(
            BiologyOrganismGroupViewModel.Create(organisms));

        Assert.Equal("Tussock:", group.GenusLabel);
        Assert.Equal("1 M – 19.01 M", group.RewardText);
        Assert.True(group.IsPrediction);
        Assert.True(group.IsCommanderFirst);
        Assert.True(group.IsHighlightedFirst);
        Assert.Collection(
            group.Species,
            row => AssertSpecies(row, "Ignis", "Yellow", true),
            row => AssertSpecies(row, "Propagito", "Yellow", false),
            row => AssertSpecies(row, "Capillum", "Yellow", false));
    }

    [Fact]
    public void DiscoveryMarkerPrecedenceMatchesTheLegacyRenderer()
    {
        BiologyOrganismRowViewModel[] organisms =
        [
            Prediction("Limaxus", "Emerald", 1_360_000, isCommanderFirst: true),
            Prediction("Paleas", "Emerald", 1_360_000, isRegionalFirst: true),
            Prediction("Tectonicas", "Emerald", 95_190_000, isGlobalRegionalFirst: true),
        ];

        var group = Assert.Single(
            BiologyOrganismGroupViewModel.Create(organisms));

        Assert.True(group.IsGlobalRegionalFirst);
        Assert.False(group.IsCommanderFirst);
        Assert.False(group.IsRegionalFirst);
        Assert.True(group.IsHighlightedFirst);
        Assert.True(group.Species[0].IsCommanderFirst);
        Assert.True(group.Species[1].IsRegionalFirst);
        Assert.True(group.Species[2].IsGlobalRegionalFirst);
    }

    [Fact]
    public void AnalyzedGroupUsesExplicitCompletionStateWithoutDimmingVariantRows()
    {
        BiologyOrganismRowViewModel[] organisms =
        [
            new()
            {
                DisplayName = "Tussock Capillum - Yellow",
                GenusName = "Tussock",
                SpeciesName = "Capillum",
                VariantName = "Yellow",
                Reward = 19_010_000,
                HasReward = true,
                IsAnalyzed = true,
                ShouldDim = true,
            },
        ];

        var group = Assert.Single(
            BiologyOrganismGroupViewModel.Create(organisms));

        Assert.True(group.IsAnalyzed);
        Assert.Equal("Yellow", Assert.Single(group.Species).VariantName);
    }

    [Fact]
    public void IdentifiedBodyDropsPredictionsForOrganismsThatAreNotPresent()
    {
        var scan = new SystemScanState();
        scan.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""));
        scan.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","Landable":true,"SurfaceTemperature":180,"SurfaceGravity":2,"SurfacePressure":1000} """));
        scan.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}]}"""));
        scan.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""));
        var evaluator = new BiologyPredictionEvaluator(
            new BiologyCriteriaCatalog(
            [
                new BiologyCriteriaNode(
                    "Tussock",
                    "Capillum",
                    "Yellow",
                    [],
                    [],
                    false,
                    null),
            ]));

        var survey = BiologySurveyViewModel.CreateBodyDetail(
            scan.CreateSnapshot(),
            1,
            ExobiologySnapshot.Empty,
            new BiologySurveyBodyDetailOptions(
                highlightRegionalFirsts: false,
                dimAnalyzedOrganisms: false,
                hideGeoCount: false,
                disablePredictions: false)
            {
                PredictionEvaluator = evaluator,
            });

        Assert.NotNull(survey);
        Assert.DoesNotContain(survey.Organisms, row => row.IsPrediction);
        Assert.Contains(survey.Organisms, row => row.SpeciesName == "Arcus");
        Assert.Contains(survey.Organisms, row => row.IsUnknown);
    }

    [Fact]
    public void GenusOnlyRestartMarksOnlyExactActivePredictionAsCurrent()
    {
        var scan = new SystemScanState();
        scan.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""));
        scan.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"G","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""));
        scan.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Parents":[{"Star":0}],"PlanetClass":"Rocky body","Landable":true,"SurfaceTemperature":180,"SurfaceGravity":2,"SurfacePressure":1000,"SemiMajorAxis":100000}"""));
        scan.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""));
        var evaluator = new BiologyPredictionEvaluator(
            new BiologyCriteriaCatalog(
            [
                new BiologyCriteriaNode(
                    "Aleoida",
                    "Arcus",
                    "Green",
                    [],
                    [],
                    false,
                    null),
                new BiologyCriteriaNode(
                    "Aleoida",
                    "Coronamus",
                    "Lime",
                    [],
                    [],
                    false,
                    null),
            ]));
        var activeScan = new BioSampleSnapshot(
            new SurfaceLocation(1, 2),
            150,
            "$Codex_Ent_Aleoids_Genus_Name;",
            "$Codex_Ent_Aleoids_02_Name;",
            "Aleoida Coronamus - Lime",
            2310206,
            "Test 1");

        var survey = BiologySurveyViewModel.CreateBodyDetail(
            scan.CreateSnapshot(),
            1,
            new ExobiologySnapshot(null, activeScan, null, 0, [], 0),
            new BiologySurveyBodyDetailOptions(
                highlightRegionalFirsts: false,
                dimAnalyzedOrganisms: false,
                hideGeoCount: false,
                disablePredictions: false)
            {
                PredictionEvaluator = evaluator,
            });

        Assert.NotNull(survey);
        Assert.Equal(2, survey.Organisms.Count);
        Assert.False(Assert.Single(
            survey.Organisms,
            organism => organism.SpeciesName == "Arcus").IsCurrentSample);
        Assert.True(Assert.Single(
            survey.Organisms,
            organism => organism.SpeciesName == "Coronamus").IsCurrentSample);
    }

    [Fact]
    public void KnownVariantResolvesMissingEntryIdWithoutTreatingZeroAsAFirst()
    {
        var scan = new SystemScanState();
        scan.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""));
        scan.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","Landable":true}"""));
        scan.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""));
        scan.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_B_Name;"}"""));
        var snapshot = scan.CreateSnapshot();
        var body = Assert.Single(snapshot.Bodies);
        var organism = Assert.Single(body.Organisms);
        var discoveryContext = new BiologyDiscoveryContext(
            42,
            new CommanderCodexData(
                "fid",
                "Drew",
                0,
                null,
                new Dictionary<long, CommanderCodexFirst>()),
            null,
            null,
            RegionalCodexCandidateCatalog.Empty);
        var options = new BiologySurveyBodyDetailOptions(
            highlightRegionalFirsts: false,
            dimAnalyzedOrganisms: false,
            hideGeoCount: false,
            disablePredictions: true)
        {
            DiscoveryContext = discoveryContext,
        };

        var missingEntryBody = body with
        {
            Organisms = [organism with { EntryId = null }],
        };
        var resolved = BiologySurveyViewModel.CreateBodyDetail(
            snapshot with { Bodies = [missingEntryBody] },
            1,
            ExobiologySnapshot.Empty,
            options);
        Assert.True(Assert.Single(resolved!.Organisms).IsCommanderFirst);

        var unresolvedBody = body with
        {
            Organisms =
            [
                organism with
                {
                    EntryId = 0,
                    Species = "$Unknown_Species;",
                    Variant = "$Unknown_Variant;",
                },
            ],
        };
        var unresolved = BiologySurveyViewModel.CreateBodyDetail(
            snapshot with { Bodies = [unresolvedBody] },
            1,
            ExobiologySnapshot.Empty,
            options);
        Assert.False(Assert.Single(unresolved!.Organisms).IsCommanderFirst);
    }

    private static BiologyOrganismRowViewModel Prediction(
        string species,
        string variant,
        long reward,
        bool isCommanderFirst = false,
        bool isRegionalFirst = false,
        bool isGlobalRegionalFirst = false)
    {
        return new BiologyOrganismRowViewModel
        {
            DisplayName = $"Tussock {species} - {variant}",
            GenusName = "Tussock",
            SpeciesName = species,
            VariantName = variant,
            Reward = reward,
            HasReward = true,
            IsPrediction = true,
            IsCommanderFirst = isCommanderFirst,
            IsRegionalFirst = isRegionalFirst,
            IsGlobalRegionalFirst = isGlobalRegionalFirst,
            IsHighlightedFirst = isCommanderFirst || isGlobalRegionalFirst,
        };
    }

    private static void AssertSpecies(
        BiologyOrganismVariantRowViewModel row,
        string species,
        string variant,
        bool isCommanderFirst)
    {
        Assert.Equal(species, row.SpeciesName);
        Assert.Equal(variant, row.VariantName);
        Assert.True(row.HasVariantColor);
        Assert.True(row.HasPredictionMarkers);
        Assert.Equal(
            "Predicted from current body data; not yet confirmed.",
            row.PredictionMarkerToolTip);
        Assert.Equal(isCommanderFirst, row.IsCommanderFirst);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var value, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(value);
    }
}
