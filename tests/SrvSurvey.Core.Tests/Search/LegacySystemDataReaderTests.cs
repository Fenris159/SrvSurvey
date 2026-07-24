using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class LegacySystemDataReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-system-data-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadReturnsMatchingLegacySystemsAndIsolatesMalformedFiles()
    {
        var systemDirectory = Path.Combine(temporaryDirectory, "systems", "F123");
        Directory.CreateDirectory(systemDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Praea Euq IL-P c5-2_102.json"),
            """
            {
              "name": "Praea Euq IL-P c5-2",
              "address": 102,
              "starPos": [1.5, 2.5, 3.5],
              "lastVisited": "2026-07-20T12:00:00-05:00",
              "fssAllBodies": true
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Praea Euq IL-P c5-0_100.json"),
            """
            {
              "name": "Praea Euq IL-P c5-0",
              "address": 100,
              "starPos": [4, 5, 6],
              "lastVisited": "2026-07-21T12:00:00Z"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "unrelated.json"),
            "{\"name\":\"Sol\",\"address\":10477373803}");
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Praea Euq IL-P c5-malformed.json"),
            "{\"name\":");
        var reader = new LegacySystemDataReader(temporaryDirectory);

        var result = await reader.ReadAsync(
            "F123",
            BoxelAddress.Parse("Praea Euq IL-P c5-0"));

        Assert.Equal(2, result.Systems.Count);
        Assert.Equal("Praea Euq IL-P c5-0", result.Systems[0].Boxel.Name);
        Assert.Equal(100, result.Systems[0].Boxel.SystemAddress);
        Assert.Equal(new GalacticCoordinate(4, 5, 6), result.Systems[0].Position);
        Assert.Equal("Praea Euq IL-P c5-2", result.Systems[1].Boxel.Name);
        Assert.True(result.Systems[1].FssAllBodies);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-20T12:00:00-05:00"),
            result.Systems[1].VisitedAt);
        Assert.Single(result.Errors);
        Assert.Contains("malformed.json", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingProfileReturnsNoSystemsWithoutCreatingFolders()
    {
        var reader = new LegacySystemDataReader(temporaryDirectory);

        var result = await reader.ReadAsync(
            "F123",
            BoxelAddress.Parse("Praea Euq IL-P c5-0"));

        Assert.Empty(result.Systems);
        Assert.Empty(result.Errors);
        Assert.False(Directory.Exists(temporaryDirectory));
    }

    [Fact]
    public async Task FrontierIdCannotEscapeTheSystemsDirectory()
    {
        var reader = new LegacySystemDataReader(temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => reader.ReadAsync(
                "..",
                BoxelAddress.Parse("Praea Euq IL-P c5-0")));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
