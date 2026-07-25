using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class GalacticRegionMapTests
{
    [Theory]
    [InlineData(0, 0, 0, 18, "Inner Orion Spur")]
    [InlineData(-9530.5, -910.28125, 19808.125, 9, "Inner Scutum-Centaurus Arm")]
    [InlineData(25.21875, -20.90625, 25899.96875, 1, "Galactic Centre")]
    public void FindsLegacyRegionForGalacticCoordinates(
        double x,
        double y,
        double z,
        int expectedId,
        string expectedName)
    {
        var region = GalacticRegionMap.Find(new GalacticCoordinate(x, y, z));

        Assert.Equal(new GalacticRegion(expectedId, expectedName), region);
    }

    [Fact]
    public void ReturnsNullOutsideRegionGrid()
    {
        Assert.Null(GalacticRegionMap.Find(
            new GalacticCoordinate(-100_000, 0, -100_000)));
    }
}
