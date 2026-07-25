using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Core.Tests.Quests;

public sealed class QuestJournalCompatibilityTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurveyQuestJournalTests",
        Guid.NewGuid().ToString("N"));

    public QuestJournalCompatibilityTests()
    {
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public async Task AuxiliaryEventUsesCompleteFileWithoutChangingIt()
    {
        var path = Path.Combine(tempDirectory, "Cargo.json");
        var source = """
            {"timestamp":"2026-07-25T00:00:00Z","event":"Cargo","Vessel":"Ship","Inventory":[{"Name":"gold","Count":2}],"future":true}
            """;
        await File.WriteAllTextAsync(path, source);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var journalEvent = Parse(
            """
            {"timestamp":"2026-07-25T00:00:00Z","event":"Cargo"}
            """);

        var result = await QuestJournalPayloadResolver.ResolveAsync(
            tempDirectory,
            journalEvent);

        Assert.True(result.UsedAuxiliaryFile);
        Assert.Null(result.Warning);
        Assert.Equal(2, result.Payload.GetProperty("Inventory")[0]
            .GetProperty("Count").GetInt32());
        Assert.True(result.Payload.GetProperty("future").GetBoolean());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("Cargo", null)]
    [InlineData("Market", "not-json")]
    [InlineData("NavRoute", "[]")]
    public async Task UnavailableAuxiliaryDataFallsBackToJournalEvent(
        string eventName,
        string? auxiliaryContents)
    {
        var path = Path.Combine(tempDirectory, $"{eventName}.json");
        if (auxiliaryContents is not null)
        {
            await File.WriteAllTextAsync(path, auxiliaryContents);
        }

        var journalEvent = Parse(
            $$"""
            {"event":"{{eventName}}","fallback":42}
            """);

        var result = await QuestJournalPayloadResolver.ResolveAsync(
            tempDirectory,
            journalEvent);

        Assert.False(result.UsedAuxiliaryFile);
        Assert.NotNull(result.Warning);
        Assert.Equal(42, result.Payload.GetProperty("fallback").GetInt32());
        if (auxiliaryContents is not null)
        {
            Assert.Equal(
                auxiliaryContents,
                await File.ReadAllTextAsync(path));
        }
    }

    [Fact]
    public async Task OrdinaryEventNeverReadsSameNamedFile()
    {
        await File.WriteAllTextAsync(
            Path.Combine(tempDirectory, "Scan.json"),
            "not-json");
        var journalEvent = Parse("""{"event":"Scan","BodyName":"A 1"}""");

        var result = await QuestJournalPayloadResolver.ResolveAsync(
            tempDirectory,
            journalEvent);

        Assert.False(result.UsedAuxiliaryFile);
        Assert.Null(result.Warning);
        Assert.Equal("A 1", result.Payload.GetProperty("BodyName").GetString());
    }

    [Fact]
    public void TrackerPreservesPriorEventsFactionSemanticsAndSurfaceStatus()
    {
        var tracker = new QuestCommanderContextTracker();
        tracker.Apply(Parse(
            """
            {"event":"Docked","StationName":"Jameson Memorial"}
            """));
        tracker.Apply(Parse(
            """
            {
              "event":"FSDJump",
              "StarSystem":"Shinrarta Dezhra",
              "Factions":[
                {
                  "Name":"Pilots Federation",
                  "FactionState":"Boom",
                  "Influence":0.75,
                  "MyReputation":100,
                  "PendingStates":[{"State":"Expansion"}],
                  "RecoveringStates":[{"State":"PublicHoliday"}]
                },
                {
                  "Name":"Explicit Empty",
                  "FactionState":"None",
                  "ActiveStates":[]
                }
              ]
            }
            """));
        var status = new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 12.5,
            Longitude = -42.25,
            Heading = -5,
            PlanetRadius = 1_000_000,
        };

        var context = tracker.CreateContext("Test Cmdr", status);

        Assert.Equal("Test Cmdr", context.CommanderName);
        Assert.Equal(355, context.Surface?.Heading);
        Assert.Equal(12.5, context.Surface?.Latitude);
        Assert.Equal(
            "Jameson Memorial",
            context.PriorJournalEvents!["Docked"]
                .GetProperty("StationName").GetString());
        Assert.Equal(
            "Shinrarta Dezhra",
            context.PriorJournalEvents["FSDJump"]
                .GetProperty("StarSystem").GetString());
        var faction = context.Factions["Pilots Federation"];
        Assert.Equal(100, faction.Reputation);
        Assert.Equal(0.75, faction.Influence);
        Assert.Equal(["Boom"], faction.ActiveStates);
        Assert.Equal(["Expansion"], faction.PendingStates);
        Assert.Equal(["PublicHoliday"], faction.RecoveringStates);
        Assert.Empty(context.Factions["Explicit Empty"].ActiveStates);
        Assert.Equal(
            (uint)StatusFlags.HasLatLong,
            context.Status?.GetProperty("Flags").GetUInt32());
    }

    [Fact]
    public void FactionlessLocationDoesNotDestroyLastKnownSystemFactions()
    {
        var tracker = new QuestCommanderContextTracker();
        tracker.Apply(Parse(
            """
            {"event":"Location","Factions":[{"Name":"Known","MyReputation":12}]}
            """));
        tracker.Apply(Parse("""{"event":"Location","StarSystem":"Sol"}"""));

        Assert.True(tracker.CreateContext(string.Empty, null)
            .Factions.ContainsKey("Known"));

        tracker.Reset();

        Assert.Empty(tracker.CreateContext(string.Empty, null).Factions);
        Assert.Empty(tracker.CreateContext(string.Empty, null)
            .PriorJournalEvents!);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var result, out var error), error);
        return Assert.IsType<JournalEventEnvelope>(result);
    }
}
