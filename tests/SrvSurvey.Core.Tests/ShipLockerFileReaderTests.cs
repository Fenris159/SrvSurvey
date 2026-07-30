using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class ShipLockerFileReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-ship-locker-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsyncProjectsAllSectionsAndMergesDuplicateItems()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(
            temporaryDirectory,
            ShipLockerFileReader.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "timestamp":"2026-07-29T12:00:00Z",
              "event":"ShipLocker",
              "Items":[
                {"Name":"healthmonitor","Name_Localised":"Health Monitor","Count":2},
                {"Name":"HealthMonitor","Count":1}
              ],
              "Components":[{"Name":"microelectrode","Name_Localised":"Microelectrode","Count":4}],
              "Consumables":[{"Name":"healthpack","Name_Localised":"Medkit","Count":5}],
              "Data":[{"Name":"manufacturinginstructions","Name_Localised":"Manufacturing Instructions","Count":6}]
            }
            """);

        var result = await ShipLockerFileReader.ReadAsync(path);

        Assert.True(result.IsSuccess, result.Error);
        var snapshot = Assert.IsType<ShipLockerSnapshot>(result.Snapshot);
        Assert.Equal("ShipLocker", snapshot.EventName);
        Assert.Equal(4, snapshot.Items.Count);
        var healthMonitor = snapshot.Items.Single(item =>
            item.Name.Equals("healthmonitor", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Items", healthMonitor.Category);
        Assert.Equal("Health Monitor", healthMonitor.LocalizedName);
        Assert.Equal(3, healthMonitor.Count);
        Assert.Contains(snapshot.Items, item =>
            item.Category == "Data" && item.Count == 6);
        Assert.NotNull(result.ContentHash);
    }

    [Fact]
    public async Task ReadAsyncRetriesMalformedPartialWrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(
            temporaryDirectory,
            ShipLockerFileReader.FileName);
        await File.WriteAllTextAsync(path, "{\"event\":\"ShipLocker\"");

        var result = await ShipLockerFileReader.ReadAsync(
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
