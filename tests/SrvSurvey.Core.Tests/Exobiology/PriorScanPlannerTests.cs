using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class PriorScanPlannerTests
{
    private const double Radius = 1_000_000;
    private const string AleoidaSpecies = "$Codex_Ent_Aleoids_01_Name;";
    private const string BacteriumSpecies = "$Codex_Ent_Bacterial_01_Name;";

    private readonly PriorScanPlanner planner = new(new ExobiologyReferenceCatalog(
    [
        new ExobiologyReference(
            2310101,
            "$Codex_Ent_Aleoids_01_B_Name;",
            AleoidaSpecies,
            "Aleoida Arcus - Green",
            7_252_500,
            HudCategory: "Biology"),
        new ExobiologyReference(
            2320101,
            "$Codex_Ent_Bacterial_01_A_Name;",
            BacteriumSpecies,
            "Bacterium Aurasus - Teal",
            1_000,
            HudCategory: "Biology"),
    ]));

    [Fact]
    public void CreatePlanFiltersGroupsSortsAndCalculatesNavigation()
    {
        var request = Request(
        [
            Signal("A 1", 2320101, 0, 0.02),
            Signal("A 1", 2310101, 0, 0.01),
            Signal("A 1", 2310101, 0, -0.01),
            Signal("A 2", 2310101, 0, 0.001),
            Signal("A 1", 9999999, 0, 0.001),
        ],
        heading: 90,
        activeSpecies: AleoidaSpecies);

        var plan = planner.CreatePlan(request);

        Assert.Collection(
            plan.Species,
            species =>
            {
                Assert.Equal(2310101, species.EntryId);
                Assert.Equal(7_252_500, species.Reward);
                Assert.True(species.IsActive);
                Assert.Equal(2, species.Targets.Count);
                Assert.All(
                    species.Targets,
                    target => Assert.Equal(
                        PriorScanTargetState.Standard,
                        target.State));
                Assert.Equal(90, species.Targets[0].BearingDegrees, 6);
                Assert.Equal(0, species.Targets[0].RelativeBearingDegrees, 6);
                Assert.Equal(174.5329, species.Targets[0].DistanceMeters, 3);
            },
            species =>
            {
                Assert.Equal(2320101, species.EntryId);
                Assert.False(species.IsActive);
            });
    }

    [Fact]
    public void CreatePlanAppliesValueAnalyzedAndPersonalSampleFilters()
    {
        var signals = new[]
        {
            Signal("A 1", 2310101, 0, 0.01),
            Signal("A 1", 2310101, 0, 0.02),
            Signal("A 1", 2320101, 0, 0.03),
        };
        var analyzed = planner.CreatePlan(Request(
            signals,
            analyzed: [2310101]));
        Assert.Equal(
            PriorScanTargetState.Analyzed,
            analyzed.Species[0].Targets[0].State);

        var filtered = planner.CreatePlan(Request(
            signals,
            analyzed: [2310101],
            personalSamples:
            [
                new PriorScanPersonalSample(
                    BacteriumSpecies,
                    new SurfaceCoordinate(0, 0.03)),
            ],
            skipLowValue: true,
            hideAnalyzed: true));

        Assert.Empty(filtered.Species);
    }

    [Fact]
    public void CreatePlanDeduplicatesBySurfaceSeparationNotRadialDistance()
    {
        var plan = planner.CreatePlan(Request(
        [
            Signal("A 1", 2310101, 0.01, 0),
            Signal("A 1", 2310101, -0.01, 0),
            Signal("A 1", 2310101, 0.0101, 0),
        ]));

        var species = Assert.Single(plan.Species);
        Assert.Equal(2, species.Targets.Count);
    }

    [Fact]
    public void CreatePlanClassifiesCloseAndFarTargets()
    {
        var plan = planner.CreatePlan(Request(
        [
            Signal("A 1", 2310101, 0, 0.001),
            Signal("A 1", 2310101, 0, 100),
        ]));

        Assert.Collection(
            Assert.Single(plan.Species).Targets,
            target => Assert.Equal(PriorScanTargetState.Close, target.State),
            target => Assert.Equal(PriorScanTargetState.Far, target.State));
    }

    private static PriorScanPlanRequest Request(
        IReadOnlyList<CanonnSurfaceBiologySignal> signals,
        double heading = 0,
        IReadOnlyCollection<long>? analyzed = null,
        IReadOnlyList<PriorScanPersonalSample>? personalSamples = null,
        string? activeSpecies = null,
        bool skipLowValue = false,
        bool hideAnalyzed = false)
    {
        return new PriorScanPlanRequest(
            "A 1",
            Radius,
            new SurfaceCoordinate(0, 0),
            heading,
            signals,
            analyzed ?? [],
            personalSamples ?? [],
            activeSpecies,
            skipLowValue,
            1_000_000,
            hideAnalyzed);
    }

    private static CanonnSurfaceBiologySignal Signal(
        string body,
        long entryId,
        double latitude,
        double longitude)
    {
        return new CanonnSurfaceBiologySignal(
            body,
            null,
            entryId,
            new SurfaceCoordinate(latitude, longitude),
            false);
    }
}
