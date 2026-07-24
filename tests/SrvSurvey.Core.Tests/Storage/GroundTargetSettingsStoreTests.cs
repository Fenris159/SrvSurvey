using System.Text.Json.Nodes;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class GroundTargetSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"SrvSurvey-ground-target-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsReturnsInactiveTargetWithoutCreatingFile()
    {
        var store = new GroundTargetSettingsStore(temporaryDirectory);

        var result = store.Load();

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Exists);
        Assert.Equal(GroundTargetSnapshot.Empty, result.Snapshot);
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public async Task LoadAndSaveUseLegacyFieldsAndPreserveUnknownData()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = System.IO.Path.Combine(temporaryDirectory, "settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "targetLatLong": { "lat": 12.5, "long": -45.25, "future": 7 },
              "targetLatLongActive": true,
              "unknownSetting": { "enabled": true }
            }
            """);
        var store = new GroundTargetSettingsStore(temporaryDirectory);

        var result = store.Load();

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Snapshot!.IsActive);
        Assert.Equal(new SurfaceCoordinate(12.5, -45.25), result.Snapshot.Target);

        await store.SaveAsync(
            new GroundTargetSnapshot(false, new SurfaceCoordinate(-1.5, 2.25)));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["unknownSetting"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(7, root["targetLatLong"]!["future"]!.GetValue<int>());
        Assert.Equal(-1.5, root["targetLatLong"]!["lat"]!.GetValue<double>());
        Assert.False(root["targetLatLongActive"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = System.IO.Path.Combine(temporaryDirectory, "settings.json");
        const string malformed = "{\"targetLatLong\":";
        await File.WriteAllTextAsync(path, malformed);
        var store = new GroundTargetSettingsStore(temporaryDirectory);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(GroundTargetSnapshot.Empty));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void InvalidSavedCoordinateIsReportedWithoutThrowing()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            System.IO.Path.Combine(temporaryDirectory, "settings.json"),
            "{\"targetLatLong\":{\"lat\":91,\"long\":0},\"targetLatLongActive\":true}");
        var store = new GroundTargetSettingsStore(temporaryDirectory);

        var result = store.Load();

        Assert.False(result.IsSuccess);
        Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
