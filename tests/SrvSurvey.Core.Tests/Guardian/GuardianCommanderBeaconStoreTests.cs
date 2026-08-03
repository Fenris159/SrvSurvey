using System.Text.Json.Nodes;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianCommanderBeaconStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-beacon-store-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SavePreservesUnknownFieldsAndRoundTripsWithCommanderReader()
    {
        var store = new GuardianCommanderBeaconStore(temporaryDirectory);
        var path = store.GetBeaconPath("F123", true, "Test System");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"futureOption":42}""");
        var scannedAt = DateTimeOffset.Parse("2026-08-03T10:15:00Z");
        var beacon = new GuardianCommanderBeaconVisit(
            string.Empty,
            scannedAt,
            scannedAt,
            "Test System",
            42,
            "Test System A 1",
            7,
            "keep",
            false,
            new Dictionary<DateTimeOffset, GuardianSurfaceLocation>
            {
                [scannedAt] = new GuardianSurfaceLocation(1.25, -2.5),
            });

        Assert.Equal(path, await store.SaveAsync("F123", true, beacon));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(42, json["futureOption"]!.GetValue<int>());
        Assert.NotNull(json["scannedLocations"]);

        var result = await new GuardianCommanderDataReader(temporaryDirectory)
            .ReadAsync("F123", isOdyssey: true);
        var saved = Assert.Single(result.Beacons);
        Assert.Equal("Test System", saved.SystemName);
        Assert.Equal(
            new GuardianSurfaceLocation(1.25, -2.5),
            Assert.Single(saved.ScannedLocations).Value);
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedBeacon()
    {
        var store = new GuardianCommanderBeaconStore(temporaryDirectory);
        var path = store.GetBeaconPath("F123", true, "Test System");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{bad-json");
        var beacon = new GuardianCommanderBeaconVisit(
            string.Empty,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Test System",
            42,
            "A 1",
            7,
            string.Empty,
            false,
            new Dictionary<DateTimeOffset, GuardianSurfaceLocation>());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync("F123", true, beacon));
        Assert.Equal("{bad-json", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
