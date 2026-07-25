using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class GuardianBubbleLocatorTests
{
    [Theory]
    [InlineData(1099.21875, -146.6875, -133.59375)]
    [InlineData(-840.65625, -561.15625, 13361.8125)]
    [InlineData(-9298.6875, -419.40625, 7911.15625)]
    public void FindsLegacyGuardianBubbleCenters(double x, double y, double z)
    {
        Assert.True(GuardianBubbleLocator.IsWithinKnownBubble(
            new GalacticCoordinate(x, y, z)));
    }

    [Fact]
    public void UsesStrictLegacyBubbleBoundary()
    {
        Assert.True(GuardianBubbleLocator.IsWithinKnownBubble(
            new GalacticCoordinate(1099.21875 + 749.99, -146.6875, -133.59375)));
        Assert.False(GuardianBubbleLocator.IsWithinKnownBubble(
            new GalacticCoordinate(1099.21875 + 750, -146.6875, -133.59375)));
    }
}
