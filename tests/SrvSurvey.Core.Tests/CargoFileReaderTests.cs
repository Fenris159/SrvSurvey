using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class CargoFileReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-cargo-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsyncPortsInventoryAndNormalizesDuplicateEntries()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, CargoFileReader.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "timestamp": "2026-07-24T12:00:00Z",
              "event": "Cargo",
              "Vessel": "SRV",
              "Count": 7,
              "Inventory": [
                {
                  "Name": "ancienttablet",
                  "Name_Localised": "Guardian Tablet",
                  "Count": 1,
                  "Stolen": 0
                },
                {
                  "Name": "AncientTablet",
                  "Count": 2,
                  "Stolen": 1
                },
                {
                  "Name": "ancienturn",
                  "Name_Localised": "Guardian Urn",
                  "Count": 3,
                  "Stolen": 0
                },
                { "Name": "ignored", "Count": 0, "Stolen": 0 },
                { "Name": "", "Count": 4, "Stolen": 0 }
              ]
            }
            """);

        var result = await CargoFileReader.ReadAsync(path);

        Assert.True(result.IsSuccess, result.Error);
        var snapshot = Assert.IsType<CargoSnapshot>(result.Snapshot);
        Assert.Equal("Cargo", snapshot.EventName);
        Assert.Equal("SRV", snapshot.Vessel);
        Assert.Equal(6, snapshot.Count);
        Assert.Equal(3, snapshot.GetCount("ANCIENTTABLET"));
        Assert.Equal(3, snapshot.GetCount("ancienturn"));
        Assert.Equal(0, snapshot.GetCount("ancientorb"));
        Assert.Equal(2, snapshot.Inventory.Count);
        Assert.Equal(1, snapshot.Inventory[0].Stolen);
        Assert.NotNull(result.ContentHash);
    }

    [Fact]
    public async Task ReadAsyncRetriesMalformedPartialWrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, CargoFileReader.FileName);
        await File.WriteAllTextAsync(path, "{\"event\":\"Cargo\"");

        var result = await CargoFileReader.ReadAsync(
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
