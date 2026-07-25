using System.Text;
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

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
