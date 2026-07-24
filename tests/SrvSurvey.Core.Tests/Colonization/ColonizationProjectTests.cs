using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationProjectTests
{
    [Fact]
    public void ReadsRavenProjectShapeAndCalculatesProgress()
    {
        var project = JsonSerializer.Deserialize<ColonizationProject>(
            """
            {
              "buildId":"build-1",
              "buildType":"no_truss",
              "buildName":"Primary port",
              "marketId":3951663874,
              "systemAddress":1180210008826,
              "systemName":"Test Sector AB-C d1",
              "starPos":[1.0,2.0,3.0],
              "maxNeed":1000,
              "sumNeed":250,
              "sumTotal":750,
              "complete":false,
              "commanders":{"Test Cmdr":["steel"]},
              "commodities":{"steel":250},
              "ready":["water"],
              "linkedFC":[{"marketId":1,"name":"ABC-123","displayName":"Carrier","assign":["steel"]}]
            }
            """);

        Assert.NotNull(project);
        Assert.Equal("build-1", project.BuildId);
        Assert.Equal(750, project.Delivered);
        Assert.Equal(0.75, project.Progress);
        Assert.Contains("steel", project.Commanders["Test Cmdr"]);
        Assert.Contains("water", project.Ready);
        Assert.Single(project.LinkedFleetCarriers);
    }

    [Fact]
    public void TotalsExcludeHiddenProjectsAndRoundTripsUp()
    {
        ColonizationProject[] projects =
        [
            new()
            {
                BuildId = "shown-1",
                RemainingRequired = 101,
            },
            new()
            {
                BuildId = "hidden",
                RemainingRequired = 500,
            },
            new()
            {
                BuildId = "shown-2",
                RemainingRequired = 199,
            },
        ];

        var totals = ColonizationProjectCalculator.CalculateTotals(
            projects,
            ["HIDDEN"],
            shipCargoCapacity: 128);

        Assert.Equal(2, totals.SelectedProjectCount);
        Assert.Equal(300, totals.RemainingCargo);
        Assert.Equal(3, totals.TripsInCurrentShip);
    }

    [Fact]
    public void ProgressAndTotalsAreSafeForInvalidServerNumbers()
    {
        var project = new ColonizationProject
        {
            MaximumRequired = 0,
            RemainingRequired = -10,
        };

        var totals = ColonizationProjectCalculator.CalculateTotals(
            [project],
            hiddenBuildIds: null,
            shipCargoCapacity: 0);

        Assert.Null(project.Progress);
        Assert.Equal(0, project.Delivered);
        Assert.Equal(0, totals.RemainingCargo);
        Assert.Null(totals.TripsInCurrentShip);
    }
}
