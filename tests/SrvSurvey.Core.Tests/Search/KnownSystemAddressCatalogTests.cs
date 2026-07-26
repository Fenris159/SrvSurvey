using System.Text;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class KnownSystemAddressCatalogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-known-systems-{Guid.NewGuid():N}");

    [Fact]
    public void ImportedCatalogResolvesScalarAndArrayEntriesWithoutMutation()
    {
        var published = Path.Combine(temporaryDirectory, "pub");
        Directory.CreateDirectory(published);
        var path = Path.Combine(
            published,
            KnownSystemAddressCatalog.LegacyFileName);
        const string source = """
            # source comment
            known_systems = {
              "sol": 10477373803,
              "v782 persei": [5579933946338, 8053700858322],
              "ambiguous": {1: vector3.Vector3(0,0,0)},
            }

            known_missing = [
            ]
            """;
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var before = File.ReadAllBytes(path);

        var catalog = KnownSystemAddressCatalog.Load(temporaryDirectory);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryResolve(" Sol ", out var sol));
        Assert.Equal(10477373803, sol);
        Assert.True(catalog.TryResolve("V782 PERSEI", out var persei));
        Assert.Equal(5579933946338, persei);
        Assert.False(catalog.TryResolve("ambiguous", out _));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(catalog.Warnings);
    }

    [Theory]
    [InlineData("known_systems = {\n  \"sol\": 10477373803,\n}")]
    [InlineData("known_systems = {\n  \"sol\": 10477373803,\n}\nknown_missing = [")]
    [InlineData("known_missing = [\n]")]
    [InlineData("known_systems = {\n}\nknown_missing = [\n]")]
    public void IncompleteCatalogIsPreservedAndFailsClosed(string source)
    {
        var published = Path.Combine(temporaryDirectory, "pub");
        Directory.CreateDirectory(published);
        var path = Path.Combine(
            published,
            KnownSystemAddressCatalog.LegacyFileName);
        File.WriteAllText(path, source);

        var catalog = KnownSystemAddressCatalog.Load(temporaryDirectory);

        Assert.False(catalog.HasData);
        Assert.False(catalog.TryResolve("sol", out _));
        Assert.Single(catalog.Warnings);
        Assert.Equal(source, File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
