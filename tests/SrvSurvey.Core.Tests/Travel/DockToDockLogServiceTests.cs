using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Travel;

namespace SrvSurvey.Core.Tests.Travel;

public sealed class DockToDockLogServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-DockToDock-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LiveTripRetainsLegacyColumnsAndEscapesCsvWithoutReplayingHistory()
    {
        var path = Path.Combine(
            temporaryDirectory,
            DockToDockCsvWriter.FileName);
        var service = new DockToDockLogService(path);
        var bootstrap = service.Apply(
        [
            Event("2026-07-25T11:00:00Z", "Loadout",
                "\"Ship\":\"python\",\"ShipName\":\"Raven, One\",\"MaxJumpRange\":31.75"),
            Event("2026-07-25T11:01:00Z", "Location",
                "\"StarSystem\":\"Alpha\",\"SystemAddress\":1,\"BodyType\":\"Planet\",\"BodyID\":4,\"Body\":\"Alpha 4\""),
            Event("2026-07-25T11:02:00Z", "Scan",
                "\"BodyID\":4,\"DistanceFromArrivalLS\":4.5"),
            Event("2026-07-25T11:03:00Z", "Docked",
                "\"StarSystem\":\"Alpha\",\"SystemAddress\":1,\"MarketID\":100,\"StationName\":\"Alpha, Hub\",\"StationType\":\"Orbis\",\"DistFromStarLS\":4.5"),
            Event("2026-07-25T11:04:00Z", "Undocked",
                "\"MarketID\":100,\"StationName\":\"Alpha, Hub\""),
        ],
        null,
        enabled: true,
        isBootstrapRead: true);
        Assert.False(bootstrap.Written);
        Assert.False(service.HasActiveTrip);
        Assert.False(File.Exists(path));

        var cargo = new CargoSnapshot(
            DateTimeOffset.Parse("2026-07-25T11:59:00Z"),
            "Cargo",
            "Ship",
            3,
            [new CargoItem("gold", "Gold", 2, 0),
             new CargoItem("silver", "Silver", 1, 0)]);
        var result = service.Apply(
        [
            Event("2026-07-25T12:00:00Z", "Undocked",
                "\"MarketID\":100,\"StationName\":\"Alpha, Hub\""),
            Event("2026-07-25T12:05:00Z", "StartJump",
                "\"JumpType\":\"Hyperspace\""),
            Event("2026-07-25T12:10:00Z", "FSDJump",
                "\"StarSystem\":\"Beta\",\"SystemAddress\":2,\"JumpDist\":10.5"),
            Event("2026-07-25T12:11:00Z", "Interdicted", string.Empty),
            Event("2026-07-25T12:20:00Z", "FSDJump",
                "\"StarSystem\":\"Gamma\",\"SystemAddress\":3,\"JumpDist\":20.25"),
            Event("2026-07-25T12:30:00Z", "SupercruiseExit",
                "\"StarSystem\":\"Gamma\",\"SystemAddress\":3,\"BodyType\":\"Planet\",\"BodyID\":7,\"Body\":\"Gamma 7\""),
            Event("2026-07-25T12:40:00Z", "Docked",
                "\"StarSystem\":\"Gamma\",\"SystemAddress\":3,\"MarketID\":200,\"StationName\":\"Beta \\\"Port\\\"\",\"StationType\":\"Outpost\",\"DistFromStarLS\":321.5"),
        ],
        cargo,
        enabled: true,
        isBootstrapRead: false);

        Assert.Equal(1, result.WrittenCount);
        Assert.Null(result.Error);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(TimeSpan.FromMinutes(40), entry.Duration);
        Assert.Equal(TimeSpan.FromMinutes(5), entry.EgressDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), entry.IngressDuration);
        Assert.Equal(2, entry.Jumps);
        Assert.Equal(30.75, entry.Distance);
        Assert.Equal("Alpha", entry.StartSystem);
        Assert.Equal(1, entry.StartAddress);
        Assert.Equal(4, entry.StartBodyId);
        Assert.Equal("Alpha 4", entry.StartBodyName);
        Assert.Equal(4.5, entry.StartDistanceFromStarLs);
        Assert.Equal("Orbis", entry.StartStationType);
        Assert.True(entry.WasInterdicted);
        Assert.Equal("Gamma", entry.EndSystem);
        Assert.Equal(7, entry.EndBodyId);
        Assert.Equal("python", entry.ShipType);
        Assert.Equal("Raven, One", entry.ShipName);
        Assert.Equal(31.75, entry.ShipMaximumJump);
        Assert.Equal(2, entry.Cargo["gold"]);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal(30, lines[0].Split(',').Length);
        Assert.Contains("\"Alpha, Hub\"", lines[1]);
        Assert.Contains("\"Beta \"\"Port\"\"\"", lines[1]);
        Assert.Contains("\"Raven, One\"", lines[1]);
        Assert.Contains("\"{\"\"gold\"\":2,\"\"silver\"\":1}\"", lines[1]);
    }

    [Fact]
    public void ExistingIncompatibleOrPartialCsvIsNeverModified()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, DockToDockCsvWriter.FileName);
        const string incompatible = "different,header\r\nexisting,row\r\n";
        File.WriteAllText(path, incompatible);
        var writer = new DockToDockCsvWriter(path);

        var exception = Assert.Throws<InvalidDataException>(
            () => writer.Append(CreateEntry()));

        Assert.Contains("header", exception.Message);
        Assert.Equal(incompatible, File.ReadAllText(path));

        var validHeader = File.ReadLines(WriteValidFile()).First();
        File.WriteAllText(path, validHeader + "\r\npartial");
        exception = Assert.Throws<InvalidDataException>(
            () => writer.Append(CreateEntry()));
        Assert.Contains("incomplete", exception.Message);
        Assert.Equal(validHeader + "\r\npartial", File.ReadAllText(path));
    }

    [Fact]
    public void ClearingAmbiguousCargoPreventsItEnteringNewTrip()
    {
        var path = Path.Combine(
            temporaryDirectory,
            DockToDockCsvWriter.FileName);
        var service = new DockToDockLogService(path);
        var cargo = new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "Ship",
            2,
            [new CargoItem("gold", "Gold", 2, 0)]);
        service.Apply([], cargo, enabled: true, isBootstrapRead: false);

        service.ClearCargo();
        var result = service.Apply(
        [
            Event("2026-07-25T12:00:00Z", "Undocked",
                "\"MarketID\":100,\"StationName\":\"Start\""),
            Event("2026-07-25T12:10:00Z", "Docked",
                "\"MarketID\":200,\"StationName\":\"End\""),
        ],
        null,
        enabled: true,
        isBootstrapRead: false);

        Assert.Empty(Assert.Single(result.Entries).Cargo);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string WriteValidFile()
    {
        var path = Path.Combine(temporaryDirectory, "valid.csv");
        new DockToDockCsvWriter(path).Append(CreateEntry());
        return path;
    }

    private static DockToDockLogEntry CreateEntry()
    {
        var started = DateTimeOffset.Parse("2026-07-25T12:00:00Z");
        return new DockToDockLogEntry(
            started,
            started.AddMinutes(1),
            TimeSpan.FromMinutes(1),
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            0,
            "Alpha",
            1,
            2,
            "Alpha 2",
            3,
            4,
            "Start",
            "Orbis",
            false,
            "Alpha",
            1,
            2,
            "Alpha 2",
            5,
            "End",
            "Outpost",
            3,
            "python",
            "Raven",
            30,
            new Dictionary<string, int>());
    }

    private static JournalEventEnvelope Event(
        string timestamp,
        string eventName,
        string properties)
    {
        var json = "{\"timestamp\":\"" + timestamp + "\",\"event\":\""
            + eventName
            + "\""
            + (string.IsNullOrEmpty(properties) ? string.Empty : "," + properties)
            + "}";
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }
}
