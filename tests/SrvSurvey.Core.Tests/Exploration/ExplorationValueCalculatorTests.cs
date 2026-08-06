using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class ExplorationValueCalculatorTests
{
    [Fact]
    public void StarValueMatchesLegacyFormula()
    {
        var value = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest(
                "NS",
                IsTerraformable: false,
                Mass: 1.4,
                IsFirstDiscoverer: true,
                IsMapped: false,
                IsFirstMapped: true,
                IsOdyssey: true));

        Assert.Equal(23106, value);
    }

    [Fact]
    public void OdysseyMappingAndEfficiencyBonusesMatchLegacyFormula()
    {
        var scan = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest(
                "High metal content body",
                IsTerraformable: true,
                Mass: 1,
                IsFirstDiscoverer: true,
                IsMapped: false,
                IsFirstMapped: true,
                IsOdyssey: true));
        var efficientMapping = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest(
                "High metal content body",
                IsTerraformable: true,
                Mass: 1,
                IsFirstDiscoverer: true,
                IsMapped: true,
                IsFirstMapped: true,
                IsOdyssey: true));
        var inefficientMapping = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest(
                "High metal content body",
                IsTerraformable: true,
                Mass: 1,
                IsFirstDiscoverer: true,
                IsMapped: true,
                IsFirstMapped: true,
                IsOdyssey: true,
                WithEfficiencyBonus: false));

        Assert.Equal(449200, scan);
        Assert.Equal(2700541, efficientMapping);
        Assert.Equal(2160433, inefficientMapping);
    }
}
