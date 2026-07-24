using System.Text.Json.Nodes;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-store-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadsCompactAndLegacyStarReferences()
    {
        var directory = CreateJourneyDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(directory, "20260701_120000.json"),
            """
            {
              "fid": "F123",
              "commander": "Drew",
              "name": "Across the black",
              "description": "A test journey",
              "startingJournal": "Journal.2026-07-01T120000.01.log",
              "startTime": "2026-07-01T11:59:59.990Z",
              "watermark": "2026-07-01T12:10:00Z",
              "visitedSystems": [
                {
                  "starRef": "Sol|10477373803|0|0|0",
                  "arrived": "2026-07-01T12:00:00Z",
                  "departed": "2026-07-01T12:05:00Z",
                  "count": { "bodyScans": 3, "notes": 1 }
                },
                {
                  "starRef": {
                    "name": "Alpha Centauri",
                    "id64": 123,
                    "x": 3.03125,
                    "y": -0.09375,
                    "z": 3.15625
                  },
                  "arrived": "2026-07-01T12:10:00Z",
                  "count": { "bodyCount": 2, "bodyScans": 2 }
                }
              ]
            }
            """);
        var store = new JourneyStore(temporaryDirectory);

        var result = await store.LoadAsync("F123", "20260701_120000");

        Assert.True(result.IsSuccess, result.Error);
        var journey = Assert.IsType<JourneyDocument>(result.Journey);
        Assert.Equal("Across the black", journey.Name);
        Assert.Equal(2, journey.VisitedSystems.Count);
        Assert.Equal(10477373803, journey.VisitedSystems[0].StarSystem.SystemAddress);
        Assert.Equal("Alpha Centauri", journey.VisitedSystems[1].StarSystem.Name);
        Assert.Equal(3.03125, journey.VisitedSystems[1].StarSystem.Position.X);
        Assert.True(journey.VisitedSystems[1].HasCompletedFss);
        Assert.Equal(journey.VisitedSystems[1], journey.CurrentSystem);
    }

    [Fact]
    public async Task SavePreservesUnknownJourneyVisitAndCountFields()
    {
        var directory = CreateJourneyDirectory();
        var path = Path.Combine(directory, "20260701_120000.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid": "F123",
              "commander": "Drew",
              "name": "Before",
              "description": "Before description",
              "startingJournal": "Journal.test.log",
              "startTime": "2026-07-01T12:00:00Z",
              "watermark": "2026-07-01T12:00:00Z",
              "futureJourney": { "enabled": true },
              "visitedSystems": [{
                "starRef": "Sol|10477373803|0|0|0",
                "arrived": "2026-07-01T12:00:00Z",
                "count": { "notes": 1, "futureCount": 7 },
                "futureVisit": "kept"
              }]
            }
            """);
        var store = new JourneyStore(temporaryDirectory);
        var loaded = await store.LoadAsync("F123", "20260701_120000");
        var journey = Assert.IsType<JourneyDocument>(loaded.Journey);
        var visit = journey.VisitedSystems[0];
        var updated = journey with
        {
            Name = "After",
            Description = "After description",
            VisitedSystems =
            [
                visit with
                {
                    Counts = visit.Counts with { Notes = 2, Screenshots = 3 },
                },
            ],
        };

        await store.SaveAsync(updated);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal("After", root["name"]!.GetValue<string>());
        Assert.True(root["futureJourney"]!["enabled"]!.GetValue<bool>());
        var savedVisit = root["visitedSystems"]![0]!;
        Assert.Equal("kept", savedVisit["futureVisit"]!.GetValue<string>());
        Assert.Equal(7, savedVisit["count"]!["futureCount"]!.GetValue<int>());
        Assert.Equal(2, savedVisit["count"]!["notes"]!.GetValue<int>());
        Assert.Equal(3, savedVisit["count"]!["screenshots"]!.GetValue<int>());
    }

    [Fact]
    public async Task CreateUsesLegacyTimestampFileAndInitialWatermark()
    {
        var store = new JourneyStore(temporaryDirectory);
        var timestamp = DateTimeOffset.Parse("2026-07-24T12:34:56Z");

        var journey = await store.CreateAsync(
            new JourneyCreationRequest(
                "F123",
                "Drew",
                "Fresh journey",
                string.Empty,
                "Journal.2026-07-24T123456.01.log",
                timestamp));

        Assert.Equal("20260724_123456", journey.FileName);
        Assert.Equal(timestamp.AddMilliseconds(-10), journey.StartTime);
        Assert.Equal(timestamp.AddMilliseconds(-10), journey.Watermark);
        Assert.True(File.Exists(journey.FilePath));
        var loaded = await store.LoadAsync("F123", journey.FileName);
        Assert.Equal("Fresh journey", loaded.Journey?.Name);
        Assert.Empty(loaded.Journey!.VisitedSystems);
    }

    [Fact]
    public async Task IncrementNoteCountUpdatesLastVisitToSystem()
    {
        var directory = CreateJourneyDirectory();
        var path = Path.Combine(directory, "20260701_120000.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "startTime": "2026-07-01T12:00:00Z",
              "visitedSystems": [
                {
                  "starRef": "Sol|42|0|0|0",
                  "arrived": "2026-07-01T12:00:00Z",
                  "count": { "notes": 1 }
                },
                {
                  "starRef": "Elsewhere|43|1|0|0",
                  "arrived": "2026-07-01T13:00:00Z",
                  "count": {}
                },
                {
                  "starRef": "Sol|42|0|0|0",
                  "arrived": "2026-07-01T14:00:00Z",
                  "count": { "notes": 2 }
                }
              ]
            }
            """);
        var store = new JourneyStore(temporaryDirectory);

        var updated = await store.IncrementNoteCountAsync(
            "F123",
            "20260701_120000",
            42);

        Assert.True(updated);
        var loaded = await store.LoadAsync("F123", "20260701_120000");
        Assert.Equal(1, loaded.Journey!.VisitedSystems[0].Counts.Notes);
        Assert.Equal(3, loaded.Journey.VisitedSystems[2].Counts.Notes);
    }

    [Fact]
    public async Task CatalogSkipsMalformedFilesAndReportsThem()
    {
        var directory = CreateJourneyDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(directory, "good.json"),
            "{\"name\":\"Good\",\"startTime\":\"2026-07-01T12:00:00Z\"}");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "bad.json"),
            "{\"name\":");
        var store = new JourneyStore(temporaryDirectory);

        var result = await store.LoadAllAsync("F123");

        Assert.Single(result.Journeys);
        Assert.Equal("Good", result.Journeys[0].Name);
        Assert.Single(result.Errors);
        Assert.Contains("bad.json", result.Errors[0]);
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedJourney()
    {
        var directory = CreateJourneyDirectory();
        var path = Path.Combine(directory, "bad.json");
        const string malformed = "{\"name\":";
        await File.WriteAllTextAsync(path, malformed);
        var store = new JourneyStore(temporaryDirectory);
        var journey = new JourneyDocument(
            "bad",
            path,
            "F123",
            "Drew",
            "Do not save",
            string.Empty,
            "Journal.test.log",
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            []);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(journey));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FileAndFrontierNamesCannotEscapeDataDirectory()
    {
        var store = new JourneyStore(temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("../outside", "test"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("F123", "../outside"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("F123", "../outside.json"));
    }

    private string CreateJourneyDirectory()
    {
        var path = Path.Combine(temporaryDirectory, "journey", "F123");
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
