using System.Text.Json;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Core.Tests.Quests;

public sealed class QuestProgressMapperTests
{
    [Fact]
    public void KnownLegacyStateMapsWithoutLosingRuntimeValues()
    {
        var definition = new LegacyQuestDefinition(
            "publisher",
            "sample",
            1.5,
            "Sample",
            "Subtitle",
            "Description",
            new HashSet<string> { "exploration" },
            LegacyQuestDuration.Long,
            new HashSet<string> { "TEST" },
            new HashSet<string> { "Cmdr" },
            true,
            new Dictionary<string, string> { ["scan"] = "Scan" },
            new Dictionary<string, string> { ["scan"] = "Scan 3" },
            [
                new LegacyQuestMessageDefinition(
                    "welcome",
                    "Raven",
                    "Hello",
                    "Welcome",
                    new Dictionary<string, string> { ["go"] = "Proceed" },
                    new HashSet<string> { "urgent" }),
            ],
            "start",
            new Dictionary<string, string> { ["start"] = "return true" },
            "dev-sample.json");
        var progress = new LegacyQuestProgress(
            new LegacyQuestReference("publisher", "sample", 1.5),
            definition,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            null,
            false,
            new Dictionary<string, LegacyQuestObjective>
            {
                ["scan"] = new(LegacyQuestObjectiveState.visible, 1, 3),
                ["simple"] = new(LegacyQuestObjectiveState.complete, 0, 0),
            },
            new HashSet<string> { "Sol" },
            new Dictionary<string, LegacyQuestBodyLocation>
            {
                ["site"] = new(12.5, -42.25, 50),
            },
            [
                new LegacyQuestChapter(
                    "start",
                    DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                    null,
                    new Dictionary<string, JsonElement>
                    {
                        ["visits"] = JsonSerializer.SerializeToElement(2),
                    }),
            ],
            [
                new LegacyQuestMessage(
                    "welcome",
                    DateTimeOffset.Parse("2026-07-01T00:01:00Z"),
                    "Raven",
                    "Hello",
                    "Welcome",
                    "start",
                    ["go", "later"],
                    false,
                    null),
            ],
            [
                new LegacyQuestRoute(
                    "route",
                    2.5,
                    [new[] { 1d, 2d }]),
            ],
            new Dictionary<string, JsonElement>
            {
                ["counter"] = JsonSerializer.SerializeToElement(42),
            },
            new Dictionary<string, JsonElement>
            {
                ["Docked"] = JsonSerializer.SerializeToElement(
                    new { @event = "Docked" }),
            });

        var mapped = QuestProgressMapper.FromLegacy(progress);

        Assert.Equal("publisher|sample|1.5", mapped.Reference.ToString());
        Assert.Equal("visible,1,3", mapped.Objectives["scan"]);
        Assert.Equal("complete", mapped.Objectives["simple"]);
        Assert.Equal("12.5,-42.25,50", mapped.BodyLocations["site"]);
        Assert.Equal(42, mapped.Variables["counter"].GetInt32());
        Assert.Equal(2, mapped.Chapters[0].Variables["visits"].GetInt32());
        Assert.Equal(
            ["go", "later"],
            Assert.IsType<string[]>(mapped.Messages[0].Actions));
        Assert.Equal([1d, 2d], mapped.Routes[0].Waypoints[0]);
        Assert.Equal(RavenQuestDuration.Long, mapped.Quest?.Duration);
        Assert.True(mapped.Quest?.Hidden);
        Assert.Equal("Proceed", mapped.Quest?.Messages[0].Actions?["go"]);
        Assert.Equal(
            "Docked",
            mapped.KeptJournalEvents["Docked"]
                .GetProperty("event")
                .GetString());
    }
}
