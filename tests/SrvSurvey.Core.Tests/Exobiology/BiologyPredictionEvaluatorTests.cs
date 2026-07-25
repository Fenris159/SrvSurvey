using System.Text;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class BiologyPredictionEvaluatorTests
{
    [Fact]
    public void EmbeddedCriteriaPredictAleoidaCoronamusLime()
    {
        var evaluator = new BiologyPredictionEvaluator(
            BiologyCriteriaCatalog.LoadEmbedded());

        var result = evaluator.Evaluate(CompleteAleoidaContext());

        Assert.Contains("Aleoida Coronamus - Lime", result.Predictions);
        var prediction = Assert.Single(
            result.PredictionDetails,
            candidate => candidate.Name == "Aleoida Coronamus - Lime");
        Assert.Equal("Aleoida", prediction.Genus);
        Assert.Equal("Coronamus", prediction.Species);
        Assert.Equal("Lime", prediction.Variant);
        Assert.True(result.HasCompleteContext);
        Assert.Empty(result.MissingProperties);
    }

    [Fact]
    public void CommonChildrenInheritGenusAndSpeciesQueries()
    {
        var evaluator = CreateEvaluator(
            """
            {
              "genus": "Test",
              "commonChildren": [
                { "variant": "Blue", "query": [ "star [F]" ] }
              ],
              "query": [ "body [Rocky]" ],
              "children": [
                {
                  "species": "Plant",
                  "useCommonChildren": true,
                  "query": [ "temp [100 ~ 200]" ]
                }
              ]
            }
            """);
        var context = new BiologyPredictionContext
        {
            PlanetClass = "Rocky body",
            SurfaceTemperature = 150,
            StarTypes = ["F"],
        };

        var result = evaluator.Evaluate(
            context,
            targetVariant: "Test Plant - Blue");

        Assert.Equal(["Test Plant - Blue"], result.Predictions);
        Assert.Equal(3, result.TargetClauses.Count);
    }

    [Fact]
    public void CompositionAlternativesAreOrConditions()
    {
        var evaluator = CreateEvaluator(
            LeafCriteria(
                "atmosComp [Argon >= 100 | Nitrogen >= 0.5]"));
        var context = new BiologyPredictionContext
        {
            AtmosphereComposition = new Dictionary<string, double>
            {
                ["Argon"] = 20,
                ["Nitrogen"] = 0.5,
            },
        };

        var result = evaluator.Evaluate(context);

        Assert.Equal(["Test Plant - Blue"], result.Predictions);
    }

    [Fact]
    public void SingleAtmosphereComponentIsNormalizedToOneHundredPercent()
    {
        var evaluator = CreateEvaluator(
            LeafCriteria("atmosComp [CarbonDioxide >= 100]"));
        var context = new BiologyPredictionContext
        {
            AtmosphereComposition = new Dictionary<string, double>
            {
                ["CarbonDioxide"] = 99.9,
            },
        };

        var result = evaluator.Evaluate(context);

        Assert.Single(result.Predictions);
    }

    [Fact]
    public void MaterialsMustExceedLegacyPresenceThreshold()
    {
        var evaluator = CreateEvaluator(LeafCriteria("mats [Iron]"));

        var atThreshold = evaluator.Evaluate(new BiologyPredictionContext
        {
            Materials = new Dictionary<string, double> { ["Iron"] = 0.25 },
        });
        var aboveThreshold = evaluator.Evaluate(new BiologyPredictionContext
        {
            Materials = new Dictionary<string, double> { ["Iron"] = 0.251 },
        });

        Assert.Empty(atThreshold.Predictions);
        Assert.Single(aboveThreshold.Predictions);
    }

    [Fact]
    public void AllNotAndAnyQueriesRetainLegacySetSemantics()
    {
        var evaluator = CreateEvaluator(
            """
            {
              "genus": "Test",
              "species": "Plant",
              "variant": "Blue",
              "query": [
                "mats &[Iron,Nickel]",
                "regions ![CentreTop]",
                "volcanism [Any]"
              ]
            }
            """);
        var context = new BiologyPredictionContext
        {
            Materials = new Dictionary<string, double>
            {
                ["Iron"] = 0.1,
                ["Nickel"] = 0.1,
            },
            RegionId = 8,
            Volcanism = "Minor Water Geysers",
        };

        var result = evaluator.Evaluate(context);

        Assert.Equal(["Test Plant - Blue"], result.Predictions);
    }

    [Fact]
    public void MissingInputsAreReportedAndDoNotProducePredictions()
    {
        var evaluator = CreateEvaluator(
            LeafCriteria("nebulae [ ~ 150]"));

        var result = evaluator.Evaluate(new BiologyPredictionContext());

        Assert.Empty(result.Predictions);
        Assert.False(result.HasCompleteContext);
        Assert.Equal(["nebulae"], result.MissingProperties);
    }

    [Fact]
    public void KnownSpeciesSuppressesOtherPredictionsFromItsGenus()
    {
        var evaluator = new BiologyPredictionEvaluator(
            BiologyCriteriaCatalog.LoadEmbedded());
        var knowledge = new BiologyPredictionKnowledge
        {
            KnownSpeciesByGenus = new Dictionary<string, string>
            {
                ["Aleoida"] = "Coronamus",
            },
        };

        var result = evaluator.Evaluate(CompleteAleoidaContext(), knowledge);

        Assert.DoesNotContain(
            result.Predictions,
            prediction => prediction.StartsWith(
                "Aleoida ",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AllKnownGeneraFilterUnobservedGenusTrees()
    {
        var evaluator = new BiologyPredictionEvaluator(
            BiologyCriteriaCatalog.LoadEmbedded());
        var knowledge = new BiologyPredictionKnowledge
        {
            AllGeneraKnown = true,
            KnownGenera = ["Bacterium"],
        };

        var result = evaluator.Evaluate(CompleteAleoidaContext(), knowledge);

        Assert.DoesNotContain(
            result.Predictions,
            prediction => prediction.StartsWith(
                "Aleoida ",
                StringComparison.Ordinal));
    }

    private static BiologyPredictionContext CompleteAleoidaContext()
    {
        return new BiologyPredictionContext
        {
            PlanetClass = "Rocky body",
            SurfaceGravity = 0.2,
            SurfaceTemperature = 185,
            SurfacePressure = 0.03,
            Atmosphere = "thin carbon dioxide",
            AtmosphereType = "CarbonDioxide",
            AtmosphereComposition = new Dictionary<string, double>
            {
                ["CarbonDioxide"] = 100,
            },
            DistanceFromArrivalLs = 500,
            Volcanism = "None",
            Materials = new Dictionary<string, double>
            {
                ["Iron"] = 20,
            },
            RegionId = 18,
            StarTypes = ["L"],
            ParentStarTypes = ["L"],
            PrimaryStarType = "L",
            NebulaDistanceLy = 500,
            IsWithinGuardianBubble = false,
        };
    }

    private static BiologyPredictionEvaluator CreateEvaluator(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new BiologyPredictionEvaluator(
            BiologyCriteriaCatalog.Load(stream));
    }

    private static string LeafCriteria(string clause)
    {
        return $$"""
            {
              "genus": "Test",
              "species": "Plant",
              "variant": "Blue",
              "query": [ "{{clause}}" ]
            }
            """;
    }
}
