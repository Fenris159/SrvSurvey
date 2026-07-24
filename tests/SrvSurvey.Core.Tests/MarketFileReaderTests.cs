using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class MarketFileReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-market-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsyncPortsLegacyMarketFieldsAndCommodityNames()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, MarketFileReader.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "timestamp": "2026-07-24T12:00:00Z",
              "event": "Market",
              "MarketId": 3700123456,
              "StationName": "Raven's Rest",
              "StationType": "FleetCarrier",
              "CarrierDockingAccess": "all",
              "StarSystem": "Facece",
              "Items": [
                {
                  "id": 128049152,
                  "Name": "$Steel_Name;",
                  "Name_Localised": "Steel",
                  "Category": "$MARKET_category_metals;",
                  "Category_Localised": "Metals",
                  "BuyPrice": 4000,
                  "SellPrice": 3000,
                  "MeanPrice": 3500,
                  "StockBracket": 2,
                  "DemandBracket": 1,
                  "Stock": 125,
                  "Demand": 20,
                  "Producer": true,
                  "Consumer": false,
                  "Rare": false,
                  "FutureField": 42
                },
                { "Name": "", "Stock": 999 }
              ]
            }
            """);

        var result = await MarketFileReader.ReadAsync(path);

        Assert.True(result.IsSuccess, result.Error);
        var snapshot = Assert.IsType<MarketSnapshot>(result.Snapshot);
        Assert.Equal("Market", snapshot.EventName);
        Assert.Equal(3700123456, snapshot.MarketId);
        Assert.Equal("Raven's Rest", snapshot.StationName);
        Assert.Equal("FleetCarrier", snapshot.StationType);
        Assert.Equal("all", snapshot.CarrierDockingAccess);
        Assert.Equal("Facece", snapshot.StarSystem);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal("steel", item.Commodity);
        Assert.Equal("Steel", item.LocalizedName);
        Assert.Equal("Metals", item.LocalizedCategory);
        Assert.Equal(125, item.Stock);
        Assert.True(item.Producer);
        Assert.Same(item, snapshot.FindItem("STEEL"));
        Assert.NotNull(result.ContentHash);
    }

    [Fact]
    public async Task ReadAsyncRetriesMalformedPartialWrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, MarketFileReader.FileName);
        await File.WriteAllTextAsync(path, "{\"event\":\"Market\"");

        var result = await MarketFileReader.ReadAsync(
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
