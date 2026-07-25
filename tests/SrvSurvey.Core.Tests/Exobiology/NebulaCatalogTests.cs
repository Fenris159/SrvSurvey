using System.Text;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class NebulaCatalogTests
{
    [Fact]
    public void EmbeddedCatalogLoadsShadowCopyAndFindsKnownCoordinate()
    {
        var catalog = NebulaCatalog.LoadEmbedded();
        var knownCoordinate = new GalacticCoordinate(
            4549.97,
            -850.562,
            33732.4);

        Assert.Equal(5743, catalog.Count);
        Assert.Equal(0, catalog.FindDistanceToClosest(knownCoordinate));
    }

    [Fact]
    public void FindsEuclideanDistanceToClosestCoordinate()
    {
        var catalog = new NebulaCatalog(
        [
            new GalacticCoordinate(10, 0, 0),
            new GalacticCoordinate(100, 100, 100),
        ]);

        Assert.Equal(
            5,
            catalog.FindDistanceToClosest(new GalacticCoordinate(5, 0, 0)));
    }

    [Fact]
    public void EmptyCatalogMatchesLegacyNoDataSentinel()
    {
        var catalog = new NebulaCatalog([]);

        Assert.Equal(
            double.MaxValue,
            catalog.FindDistanceToClosest(new GalacticCoordinate(0, 0, 0)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[[1,2]]")]
    [InlineData("[[1,2,\"three\"]]")]
    public void RejectsInvalidCatalogs(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Assert.Throws<InvalidDataException>(() => NebulaCatalog.Load(stream));
    }
}
