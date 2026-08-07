using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class ExobiologyBodyNamesTests
{
    [Theory]
    [InlineData("1 a", "1a", null)]
    [InlineData("1a", "1 A", null)]
    [InlineData("Col 285 Sector AB-C d1-2 1 a", "1 a", "Col 285 Sector AB-C d1-2")]
    [InlineData("Col 285 Sector AB-C d1-2 1 a", "1a", "Col 285 Sector AB-C d1-2")]
    [InlineData("A 1", "A1", "Some System")]
    public void MatchesNormalizesSpacesAndSystemPrefix(
        string first,
        string second,
        string? systemName)
    {
        Assert.True(ExobiologyBodyNames.Matches(first, second, systemName));
    }

    [Theory]
    [InlineData("1 a", "1 b", null)]
    [InlineData("Solitude 1", "1", "Sol")]
    [InlineData("Other System 1 a", "1 a", "Col 285 Sector AB-C d1-2")]
    public void MatchesRejectsDifferentBodies(
        string first,
        string second,
        string? systemName)
    {
        Assert.False(ExobiologyBodyNames.Matches(first, second, systemName));
    }

    [Fact]
    public void NormalizeKeyStripsSystemPrefixOnlyOnBoundary()
    {
        Assert.Equal(
            "1a",
            ExobiologyBodyNames.NormalizeKey(
                "Test System 1 a",
                "Test System"));
        // "Sol" must not strip the start of "Solitude".
        Assert.Equal(
            "Solitude1",
            ExobiologyBodyNames.NormalizeKey("Solitude 1", "Sol"));
    }
}
