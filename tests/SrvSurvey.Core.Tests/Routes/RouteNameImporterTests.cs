using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class RouteNameImporterTests
{
    [Fact]
    public async Task ImportResolvesNamesInOrderAndPreservesUnknownSystems()
    {
        var resolver = new StubResolver(new Dictionary<string, StarSystemReference>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Sol"] = new(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0)),
            ["Colonia"] = new(
                "Colonia",
                2,
                new GalacticCoordinate(-9530.5, -910.28125, 19808.125)),
        });
        var importer = new RouteNameImporter(resolver);
        var progress = new SynchronousProgress<RouteNameImportProgress>();

        var result = await importer.ImportAsync(
            [" Sol ", "Unknown Place", "Colonia"],
            progress);

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
        var names = RouteNameImporter.ParseNames(
            " Sol\r\n\r\nAchenar\n  Colonia  \rSagittarius A* ");

        Assert.Equal(
            ["Sol", "Achenar", "Colonia", "Sagittarius A*"],
            names);
    }

    private sealed class StubResolver(
        IReadOnlyDictionary<string, StarSystemReference> systems)
        : IStarSystemResolver
    {
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            IReadOnlyList<StarSystemReference> result = systems.TryGetValue(
                query,
                out var system)
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
