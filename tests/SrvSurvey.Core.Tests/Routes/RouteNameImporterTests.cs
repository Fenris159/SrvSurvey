using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class RouteNameImporterTests
{
    [Fact]
    public async Task ImportResolvesNamesInOrderAndPreservesUnknownSystems()
    {
        var resolver = new StubResolver(
            new Dictionary<string, StarSystemReference>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sol"] = new("Sol", 1, new GalacticCoordinate(0, 0, 0)),
                ["Colonia"] = new("Colonia", 2, new GalacticCoordinate(-9530.5, -910.28125, 19808.125)),
            }
        );
        var importer = new RouteNameImporter(resolver);
        var progress = new SynchronousProgress<RouteNameImportProgress>();

        RouteNameImportResult result = await importer.ImportAsync([" Sol ", "Unknown Place", "Colonia"], progress);

        Assert.Equal(3, result.Hops.Count);
        Assert.Equal(2, result.ResolvedCount);
        Assert.Equal(1, result.UnresolvedCount);
        Assert.Equal(["Sol", "Unknown Place", "Colonia"], resolver.Queries);
        Assert.Equal(1, result.Hops[0].SystemAddress);
        Assert.Equal("Unknown Place", result.Hops[1].Name);
        Assert.Null(result.Hops[1].SystemAddress);
        Assert.Null(result.Hops[1].Position);
        Assert.Equal(3, progress.Values.Count);
        Assert.False(progress.Values[1].Resolved);
    }

    [Fact]
    public void ParseNamesHandlesWindowsUnixAndBlankLines()
    {
        IReadOnlyList<string> names = RouteNameImporter.ParseNames(" Sol\r\n\r\nAchenar\n  Colonia  \rSagittarius A* ");

        Assert.Equal(["Sol", "Achenar", "Colonia", "Sagittarius A*"], names);
    }

    [Theory]
    [InlineData("{\n\"name\": \"Sol\",\n\"bodies\": []\n}")]
    [InlineData("[\"Sol\", \"Colonia\"]")]
    [InlineData("Sol\n\"estimated_scan_value\": 303275,\n}")]
    [InlineData("\uFEFF{\"name\":\"Sol\"}")]
    public async Task StructuredInputIsRejectedBeforeResolvingAnyNames(string text)
    {
        var resolver = new StubResolver(new Dictionary<string, StarSystemReference>());
        var importer = new RouteNameImporter(resolver);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            importer.ImportAsync(RouteNameImporter.ParseNames(text))
        );

        Assert.Contains("Spansh URL from clipboard", error.Message, StringComparison.Ordinal);
        Assert.Empty(resolver.Queries);
    }

    [Fact]
    public async Task AutocompleteOnlyResolvesExactNamesIgnoringCase()
    {
        var importer = new RouteNameImporter(
            new AutocompleteResolver([
                new StarSystemReference("Solitude", 99, new GalacticCoordinate(5, 5, 5)),
                new StarSystemReference("Sol", 1, new GalacticCoordinate(0, 0, 0)),
            ])
        );

        RouteNameImportResult result = await importer.ImportAsync(["sOl", "Unknown Place"]);

        Assert.Equal(["Sol", "Unknown Place"], result.Hops.Select(hop => hop.Name));
        Assert.Equal(1, result.Hops[0].SystemAddress);
        Assert.Null(result.Hops[1].SystemAddress);
        Assert.Null(result.Hops[1].Position);
        Assert.Equal(1, result.ResolvedCount);
        Assert.Equal(1, result.UnresolvedCount);
    }

    private sealed class AutocompleteResolver(IReadOnlyList<StarSystemReference> suggestions) : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(suggestions);
        }
    }

    private sealed class StubResolver(IReadOnlyDictionary<string, StarSystemReference> systems) : IStarSystemResolver
    {
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            Queries.Add(query);
            IReadOnlyList<StarSystemReference> result = systems.TryGetValue(query, out StarSystemReference? system)
                ? [system]
                : [];
            return Task.FromResult(result);
        }
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value)
        {
            Values.Add(value);
        }
    }
}
