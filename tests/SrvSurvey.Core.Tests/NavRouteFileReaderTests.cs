using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests;

public sealed class NavRouteFileReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-nav-route-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsyncPortsRouteEntriesAndGeneratedBoxelData()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, NavRouteFileReader.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "timestamp": "2026-07-24T12:00:00Z",
              "event": "NavRoute",
              "Route": [
                {
                  "StarSystem": "Praea Euq IL-P c5-2",
                  "SystemAddress": 102,
                  "StarPos": [1.5, 2.5, 3.5],
                  "StarClass": "M"
                },
                {
                  "StarSystem": "Sol",
                  "SystemAddress": 10477373803,
                  "StarPos": [0, 0, 0],
                  "StarClass": "G"
                }
              ]
            }
            """);

        var result = await NavRouteFileReader.ReadAsync(path);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("NavRoute", result.Snapshot?.EventName);
        Assert.Equal(2, result.Snapshot?.Route.Count);
        var entry = result.Snapshot?.Route[0];
        Assert.Equal(new GalacticCoordinate(1.5, 2.5, 3.5), entry?.Position);
        var boxel = Assert.IsType<BoxelSystemObservation>(entry?.ToBoxelObservation());
        Assert.Equal(102, boxel.Boxel.SystemAddress);
        Assert.Null(result.Snapshot?.Route[1].ToBoxelObservation());
        Assert.NotNull(result.ContentHash);
    }

    [Fact]
    public async Task ReadAsyncRetriesMalformedPartialWrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, NavRouteFileReader.FileName);
        await File.WriteAllTextAsync(path, "{\"event\":\"NavRoute\"");

        var result = await NavRouteFileReader.ReadAsync(
            path,
            maximumAttempts: 2,
            retryDelay: TimeSpan.Zero);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Attempts);
        Assert.Contains("after 2 attempts", result.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
