using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class ExplorationValueCalculatorTests
{
    [Fact]
    public void StarValueMatchesLegacyFormula()
    {
        var value = ExplorationValueCalculator.Calculate(
            "NS",
            isTerraformable: false,
            mass: 1.4,
            isFirstDiscoverer: true,
            isMapped: false,
            isFirstMapped: true,
            isOdyssey: true);

        Assert.Equal(23106, value);
    }

    [Fact]
    public void OdysseyMappingAndEfficiencyBonusesMatchLegacyFormula()
    {
        var scan = ExplorationValueCalculator.Calculate(
            "High metal content body",
            isTerraformable: true,
            mass: 1,
            isFirstDiscoverer: true,
            isMapped: false,
            isFirstMapped: true,
            isOdyssey: true);
        var efficientMapping = ExplorationValueCalculator.Calculate(
            "High metal content body",
            isTerraformable: true,
            mass: 1,
            isFirstDiscoverer: true,
            isMapped: true,
            isFirstMapped: true,
            isOdyssey: true);
        var inefficientMapping = ExplorationValueCalculator.Calculate(
            "High metal content body",
            isTerraformable: true,
            mass: 1,
            isFirstDiscoverer: true,
            isMapped: true,
            isFirstMapped: true,
            isOdyssey: true,
            withEfficiencyBonus: false);

        Assert.Equal(449200, scan);
        Assert.Equal(2700541, efficientMapping);
        Assert.Equal(2160433, inefficientMapping);
    }
}
