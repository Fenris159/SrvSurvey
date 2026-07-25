using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class GreenGasGiantCriteriaCatalogTests
{
    private readonly GreenGasGiantCriteriaCatalog catalog =
        GreenGasGiantCriteriaCatalog.LoadEmbedded();

    [Theory]
    [InlineData("Sudarsky class I gas giant", 77.450478, "likely")]
    [InlineData("Sudarsky class I gas giant", 77.450978, "likely-approx")]
    [InlineData("Sudarsky class III gas giant", 310, "potential")]
    [InlineData("Sudarsky class III gas giant", 310.0005, "potential-approx")]
    [InlineData("Sudarsky class V gas giant", 310, null)]
    [InlineData("Rocky body", 310, null)]
    public void MatchesShippedTemperatureCriteria(
        string planetClass,
        double temperature,
        string? expected)
    {
        Assert.Equal(expected, catalog.Match(planetClass, temperature));
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        Assert.Null(catalog.Match(null, 310));
        Assert.Null(catalog.Match("Sudarsky class III gas giant", double.NaN));
    }
}
