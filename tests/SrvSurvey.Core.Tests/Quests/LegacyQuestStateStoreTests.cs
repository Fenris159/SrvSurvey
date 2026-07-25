using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Core.Tests.Quests;

public sealed class LegacyQuestStateStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-legacy-quest-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DevelopmentQuestStateIsLoadedWithoutChangingSourceFiles()
    {
        var questDirectory = Path.Combine(temporaryDirectory, "quests");
        Directory.CreateDirectory(questDirectory);
        var statePath = Path.Combine(questDirectory, "f123.json");
        var definitionPath = Path.Combine(questDirectory, "dev-sample.json");
        var stateBytes = Encoding.UTF8.GetBytes(
            """
            {
              "fid": "F123",
              "cmdr": "Test Cmdr",
              "devRef": "publisher|sample|1.5",
              "devQuest": {
                "objectives": {
                  "scan": "visible,1,3",
                  "bad": "not-a-state"
                },
                "startTime": "2026-07-01T00:00:00Z",
                "paused": false,
                "tags": ["Sol", "Jameson Memorial"],
                "bodyLocations": {"site": "12.5,-42.25,50"},
                "chapters": [
                  {
                    "id": "start",
                    "startTime": "2026-07-01T00:00:00Z",
                    "vars": {"visits": 2}
                  }
                ],
                "msgs": [
                  {
                    "id": "welcome",
                    "received": "2026-07-01T00:01:00Z",
                    "chapter": "start",
                    "read": false,
                    "actions": ["go", "later", "go"]
                  }
                ],
                "routes": [
                  {"id": "route1", "w": 2.5, "wp": [[1, 2], [3, 4]]}
                ],
                "vars": {
                  "counter": 42,
                  "nested": {"future": true}
                },
                "keptLasts": {
                  "Docked": {"event": "Docked", "StationName": "Jameson Memorial"}
                },
                "futureField": "preserved in the source file"
              },
              "futureRoot": true
            }
            """);
        var definitionBytes = Encoding.UTF8.GetBytes(
            """
            {
              "id": "sample",
              "ver": 1.5,
              "publisher": "publisher",
              "title": "Sample Quest",
              "subTitle": "Testing",
              "desc": "Description",
              "tags": ["exploration"],
              "duration": "Long",
              "onlySquadrons": ["TEST"],
              "onlyCmdrs": ["Test Cmdr"],
              "hidden": true,
              "firstChapter": "start",
              "objectives": {"scan": "Scan things"},
              "strings": {"scan": "Scan 3 things"},
              "msgs": [
                {
                  "id": "welcome",
                  "from": "Raven",
                  "subject": "Hello",
                  "body": "Welcome",
                  "actions": {"go": "Proceed", "later": "Wait"},
                  "tags": ["urgent"]
                }
              ],
              "chapters": {"start": "function JournalEntry(entry) end"}
            }
            """);
        File.WriteAllBytes(statePath, stateBytes);
        File.WriteAllBytes(definitionPath, definitionBytes);

        var result = new LegacyQuestStateStore(temporaryDirectory).Load("F123");

        Assert.True(result.Exists);
        Assert.Null(result.Error);
        var state = Assert.IsType<LegacyCommanderQuestState>(result.Data);
        Assert.Equal("F123", state.FrontierId);
        Assert.Equal("Test Cmdr", state.CommanderName);
        var quest = Assert.IsType<LegacyQuestProgress>(state.DevelopmentQuest);
        Assert.Equal(
            new LegacyQuestReference("publisher", "sample", 1.5),
            quest.Reference);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            quest.StartTime);
        Assert.False(quest.Paused);
        Assert.Equal(2, quest.Tags.Count);
        Assert.Equal(
            new LegacyQuestObjective(
                LegacyQuestObjectiveState.visible,
                1,
                3),
            quest.Objectives["scan"]);
        Assert.False(quest.Objectives.ContainsKey("bad"));
        Assert.Equal(
            new LegacyQuestBodyLocation(12.5, -42.25, 50),
            quest.BodyLocations["site"]);
        Assert.Equal(42, quest.Variables["counter"].GetInt32());
        Assert.True(
            quest.Variables["nested"].GetProperty("future").GetBoolean());
        Assert.Equal(
            "Docked",
            quest.KeptJournalEvents["Docked"].GetProperty("event").GetString());
        var chapter = Assert.Single(quest.Chapters);
        Assert.True(chapter.IsActive);
        Assert.Equal(2, chapter.Variables["visits"].GetInt32());
        var message = Assert.Single(quest.Messages);
        Assert.Equal("Raven", message.From);
        Assert.Equal("Hello", message.Subject);
        Assert.Equal("Welcome", message.Body);
        Assert.Equal(["go", "later", "go"], message.Actions);
        Assert.Equal(1, quest.UnreadMessageCount);
        var route = Assert.Single(quest.Routes);
        Assert.Equal(2.5, route.Width);
        Assert.Equal([1d, 2d], route.Waypoints[0]);

        var definition = Assert.IsType<LegacyQuestDefinition>(quest.Definition);
        Assert.Equal("Sample Quest", definition.Title);
        Assert.Equal("Testing", definition.Subtitle);
        Assert.Equal(LegacyQuestDuration.Long, definition.Duration);
        Assert.True(definition.Hidden);
        Assert.Contains("exploration", definition.Tags);
        Assert.Contains("TEST", definition.OnlySquadrons);
        Assert.Contains("Test Cmdr", definition.OnlyCommanders);
        Assert.Equal("start", definition.FirstChapter);
        Assert.Equal(
            "function JournalEntry(entry) end",
            definition.Chapters["start"]);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("objective 'bad'", StringComparison.Ordinal));
        Assert.Equal(stateBytes, File.ReadAllBytes(statePath));
        Assert.Equal(definitionBytes, File.ReadAllBytes(definitionPath));
    }

    [Fact]
    public void MissingStateReturnsAnEmptySnapshotWithoutCreatingAFile()
    {
        var result = new LegacyQuestStateStore(temporaryDirectory).Load("F123");

        Assert.False(result.Exists);
        Assert.Null(result.Error);
        Assert.Equal("F123", result.Data?.FrontierId);
        Assert.False(File.Exists(result.Path));
    }

    [Fact]
    public void MalformedStateIsReportedWithoutChangingTheFile()
    {
        var questDirectory = Path.Combine(temporaryDirectory, "quests");
        Directory.CreateDirectory(questDirectory);
        var path = Path.Combine(questDirectory, "F123.json");
        var malformed = Encoding.UTF8.GetBytes("{\"devQuest\":[");
        File.WriteAllBytes(path, malformed);

        var result = new LegacyQuestStateStore(temporaryDirectory).Load("F123");

        Assert.True(result.Exists);
        Assert.Null(result.Data);
        Assert.NotNull(result.Error);
        Assert.Equal(malformed, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("../F123")]
    [InlineData("..\\F123")]
    public void FrontierIdCannotEscapeTheQuestDirectory(string frontierId)
    {
        Assert.Throws<ArgumentException>(
            () => new LegacyQuestStateStore(temporaryDirectory).Load(frontierId));
    }

    [Fact]
    public void DevelopmentQuestIdCannotEscapeTheQuestDirectory()
    {
        var questDirectory = Path.Combine(temporaryDirectory, "quests");
        Directory.CreateDirectory(questDirectory);
        File.WriteAllText(
            Path.Combine(questDirectory, "F123.json"),
            """
            {
              "fid": "F123",
              "devRef": "publisher|../outside|1",
              "devQuest": {}
            }
            """);

        var result = new LegacyQuestStateStore(temporaryDirectory).Load("F123");

        var quest = Assert.IsType<LegacyQuestProgress>(result.Data?.DevelopmentQuest);
        Assert.Null(quest.Definition);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("path separator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DevelopmentProgressSavesAtomicallyWithVerifiedBackup()
    {
        var questDirectory = Path.Combine(temporaryDirectory, "quests");
        Directory.CreateDirectory(questDirectory);
        var statePath = Path.Combine(questDirectory, "F123.json");
        var definitionPath = Path.Combine(questDirectory, "dev-sample.json");
        var stateBytes = Encoding.UTF8.GetBytes(
            """
            {
              "fid": "F123",
              "cmdr": "Test Cmdr",
              "devRef": "publisher|sample|1.5",
              "devQuest": {
                "objectives": {"scan": "visible,1,3"},
                "tags": ["Sol"],
                "bodyLocations": {"site": "12.5,-42.25,50"},
                "chapters": [
                  {"id":"start","startTime":"2026-07-01T00:00:00Z","vars":{"visits":2},"futureChapter":true}
                ],
                "msgs": [
                  {"id":"welcome","received":"2026-07-01T00:01:00Z","chapter":"start","actions":["go"],"futureMessage":42}
                ],
                "vars": {"counter": 42},
                "keptLasts": {"Docked":{"event":"Docked"}},
                "routes": [
                  {"id":"route","w":2.5,"wp":[[1,2]],"futureRoute":"keep"}
                ],
                "futureQuest": {"keep": true}
              },
              "futureRoot": "keep"
            }
            """);
        var definitionBytes = Encoding.UTF8.GetBytes(
            """
            {
              "id":"sample",
              "ver":1.5,
              "publisher":"publisher",
              "title":"Sample",
              "firstChapter":"start",
              "objectives":{"scan":"Scan"},
              "msgs":[{"id":"welcome","from":"Raven","body":"Welcome","actions":{"go":"Proceed"}}],
              "chapters":{"start":"return true"}
            }
            """);
        File.WriteAllBytes(statePath, stateBytes);
        File.WriteAllBytes(definitionPath, definitionBytes);
        var store = new LegacyQuestStateStore(temporaryDirectory);
        var loaded = store.Load("F123");
        var legacy = Assert.IsType<LegacyQuestProgress>(
            loaded.Data?.DevelopmentQuest);
        var progress = QuestProgressMapper.FromLegacy(legacy);
        progress.Objectives["scan"] = "complete,3,3";
        progress.Variables["counter"] = JsonSerializer.SerializeToElement(99);
        progress.ExtensionData["newFutureQuest"] =
            JsonSerializer.SerializeToElement("round-trip");
        progress.Messages[0] = progress.Messages[0] with
        {
            Read = true,
            Replied = "go",
        };
        progress.Routes[0].Waypoints.Add([3, 4]);

        var saved = await store.SaveDevelopmentQuestAsync(
            "F123",
            "Test Cmdr",
            progress);

        Assert.Equal(statePath, saved.Path);
        var backupPath = Assert.IsType<string>(saved.BackupPath);
        Assert.Equal(stateBytes, File.ReadAllBytes(backupPath));
        Assert.Equal(definitionBytes, File.ReadAllBytes(definitionPath));
        var root = Assert.IsType<JsonObject>(
            JsonNode.Parse(File.ReadAllText(statePath)));
        Assert.Equal("keep", root["futureRoot"]?.GetValue<string>());
        var quest = Assert.IsType<JsonObject>(root["devQuest"]);
        Assert.True(quest["futureQuest"]?["keep"]?.GetValue<bool>());
        Assert.Equal(
            "round-trip",
            quest["newFutureQuest"]?.GetValue<string>());
        Assert.True(quest["chapters"]?[0]?["futureChapter"]?.GetValue<bool>());
        Assert.Equal(42, quest["msgs"]?[0]?["futureMessage"]?.GetValue<int>());
        Assert.Equal("keep", quest["routes"]?[0]?["futureRoute"]?.GetValue<string>());
        Assert.Equal(
            "complete,3,3",
            quest["objectives"]?["scan"]?.GetValue<string>());
        Assert.Equal(99, quest["vars"]?["counter"]?.GetValue<int>());
        Assert.True(quest["msgs"]?[0]?["read"]?.GetValue<bool>());
        Assert.Equal("go", quest["msgs"]?[0]?["replied"]?.GetValue<string>());
        Assert.Equal(2, quest["routes"]?[0]?["wp"]?.AsArray().Count);
        Assert.Empty(Directory.EnumerateFiles(questDirectory, "*.tmp"));

        var reopened = store.Load("F123");
        Assert.Null(reopened.Error);
        Assert.Equal(
            LegacyQuestObjectiveState.complete,
            reopened.Data?.DevelopmentQuest?.Objectives["scan"].State);
    }

    [Fact]
    public async Task MalformedStateIsNeverOverwrittenBySave()
    {
        var questDirectory = Path.Combine(temporaryDirectory, "quests");
        Directory.CreateDirectory(questDirectory);
        var path = Path.Combine(questDirectory, "F123.json");
        var malformed = Encoding.UTF8.GetBytes("{\"devQuest\":[");
        File.WriteAllBytes(path, malformed);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() =>
            new LegacyQuestStateStore(temporaryDirectory)
                .SaveDevelopmentQuestAsync(
                    "F123",
                    "Test Cmdr",
                    CreateProgress()));

        Assert.Equal(malformed, File.ReadAllBytes(path));
        Assert.False(Directory.Exists(
            Path.Combine(questDirectory, "quest-state-backups")));
    }

    [Fact]
    public async Task ClearingDevelopmentQuestPreservesOtherCommanderState()
    {
        var questDirectory = Path.Combine(temporaryDirectory, "quests");
        Directory.CreateDirectory(questDirectory);
        var path = Path.Combine(questDirectory, "F123.json");
        var original = Encoding.UTF8.GetBytes(
            """
            {"fid":"F123","cmdr":"Cmdr","devRef":"publisher|sample|1","devQuest":{},"future":42}
            """);
        File.WriteAllBytes(path, original);

        var saved = await new LegacyQuestStateStore(temporaryDirectory)
            .SaveDevelopmentQuestAsync("F123", "Cmdr", null);

        Assert.Equal(original, File.ReadAllBytes(saved.BackupPath!));
        var root = Assert.IsType<JsonObject>(
            JsonNode.Parse(File.ReadAllText(path)));
        Assert.Null(root["devRef"]);
        Assert.Null(root["devQuest"]);
        Assert.Equal(42, root["future"]?.GetValue<int>());
    }

    [Fact]
    public async Task SaveFrontierIdCannotEscapeTheQuestDirectory()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new LegacyQuestStateStore(temporaryDirectory)
                .SaveDevelopmentQuestAsync(
                    "../F123",
                    "Cmdr",
                    CreateProgress()));
    }

    private static RavenCommanderQuest CreateProgress()
    {
        return new RavenCommanderQuest
        {
            Publisher = "publisher",
            Id = "sample",
            Version = 1,
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
