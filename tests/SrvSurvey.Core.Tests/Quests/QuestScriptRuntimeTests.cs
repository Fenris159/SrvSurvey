using System.Text.Json;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Core.Tests.Quests;

public sealed class QuestScriptRuntimeTests
{
    [Fact]
    public async Task ChapterLifecycleMutatesAndPersistsCompleteQuestState()
    {
        var saves = 0;
        RavenQuestState? transitioned = null;
        var progress = CreateProgress(
            """
            counter = 0

            function onStart()
                objective:show("scan", 0, 3)
                quest:set("started", true)
                quest:sendMsg("welcome")
                quest:tag({"Sol", "Achenar"})
                quest:trackLocation("site", 12.5, -42.25, 50)
                quest:keepLast({"Scan"})
                quest:setRoute("route", 2.5, {"1,2", "3,4"})
                return true
            end

            function on_Scan(entry)
                counter = counter + 1
                objective:progress("scan", counter, 3)
                quest:set("lastBody", entry.BodyName)
                return true
            end

            function onMsgRead(id)
                quest:tag("read")
                return true
            end

            function onMsgAction(action, id)
                if action == "go" then
                    objective:complete("scan")
                    quest:complete()
                end
                return true
            end
            """);
        await using var runtime = new QuestScriptRuntime(
            progress,
            saveProgress: (_, _) =>
            {
                saves++;
                return Task.CompletedTask;
            },
            transitionState: (state, _) =>
            {
                transitioned = state;
                return Task.CompletedTask;
            });

        await runtime.InitializeAsync(startFirstChapter: true);

        Assert.NotNull(runtime.Progress.StartTime);
        Assert.True(IsActive(Assert.Single(progress.Chapters)));
        Assert.Equal("visible,0,3", progress.Objectives["scan"]);
        Assert.True(progress.Variables["started"].GetBoolean());
        var message = Assert.Single(progress.Messages);
        Assert.Null(message.From);
        Assert.Null(message.Subject);
        Assert.Null(message.Body);
        Assert.Equal("start", message.Chapter);
        Assert.Contains("Sol", progress.Tags);
        Assert.Equal("12.5,-42.25,50", progress.BodyLocations["site"]);
        Assert.Equal(3, progress.KeptJournalEvents.Count);
        Assert.True(progress.KeptJournalEvents["Scan"].ValueKind is JsonValueKind.Null);
        Assert.Equal(2, Assert.Single(progress.Routes).Waypoints.Count);

        using var journal = JsonDocument.Parse(
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Scan","BodyName":"Test 1"}
            """);
        Assert.True(await runtime.ProcessJournalEntryAsync(journal.RootElement));

        Assert.Equal("visible,1,3", progress.Objectives["scan"]);
        Assert.Equal("Test 1", progress.Variables["lastBody"].GetString());
        Assert.Equal(1, progress.Chapters[0].Variables["counter"].GetDouble());
        Assert.Equal(
            "Test 1",
            progress.KeptJournalEvents["Scan"].GetProperty("BodyName").GetString());

        await runtime.MarkMessageReadAsync("welcome");

        Assert.True(progress.Messages[0].Read);
        Assert.Contains("read", progress.Tags);

        await runtime.ReplyToMessageAsync("welcome", "go");

        Assert.Equal("go", progress.Messages[0].Replied);
        Assert.Equal("complete,1,3", progress.Objectives["scan"]);
        Assert.Equal(RavenQuestState.complete, runtime.TerminalState);
        Assert.NotNull(runtime.Progress.EndTime);
        Assert.Equal(RavenQuestState.complete, transitioned);
        Assert.True(saves >= 4);
    }

    [Fact]
    public async Task NextChapterStopsCurrentAndStartsReplacement()
    {
        var progress = CreateProgress(
            """
            function on_Test(entry)
                quest:nextChapter("second")
                return true
            end
            """,
            new Dictionary<string, string>
            {
                ["second"] =
                    """
                    function onStart()
                        quest:set("secondStarted", true)
                        return true
                    end
                    """,
            });
        await using var runtime = new QuestScriptRuntime(progress);
        await runtime.InitializeAsync(startFirstChapter: true);
        using var journal = JsonDocument.Parse("{\"event\":\"Test\"}");

        await runtime.ProcessJournalEntryAsync(journal.RootElement);

        Assert.NotNull(progress.Chapters.Single(chapter => chapter.Id == "start").EndTime);
        Assert.True(IsActive(
            progress.Chapters.Single(chapter => chapter.Id == "second")));
        Assert.True(progress.Variables["secondStarted"].GetBoolean());
    }

    [Fact]
    public async Task DeveloperCanStartDebugAndStopChapterWithSavedVariables()
    {
        var saves = 0;
        var progress = CreateProgress(
            "function noop() end",
            new Dictionary<string, string>
            {
                ["second"] = "counter = 2",
            });
        await using var runtime = new QuestScriptRuntime(
            progress,
            saveProgress: (_, _) =>
            {
                saves++;
                return Task.CompletedTask;
            });
        await runtime.InitializeAsync();

        await runtime.SetChapterActiveAsync("second", active: true);
        var result = await runtime.RunDebugAsync(
            "second",
            "counter = counter + 3; return counter");
        await runtime.SetChapterActiveAsync("second", active: false);

        var chapter = progress.Chapters.Single(item => item.Id == "second");
        Assert.NotNull(chapter.StartTime);
        Assert.NotNull(chapter.EndTime);
        Assert.Equal(5, chapter.Variables["counter"].GetDouble());
        Assert.Equal(5, result.GetDouble());
        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task DevelopmentStateEditsValidateAndPersistTypedViews()
    {
        var saves = 0;
        var progress = CreateProgress("counter = 1");
        await using var runtime = new QuestScriptRuntime(
            progress,
            saveProgress: (_, _) =>
            {
                saves++;
                return Task.CompletedTask;
            });
        await runtime.InitializeAsync(startFirstChapter: true);
        var initial = await runtime.GetDevelopmentStateAsync();
        var chapter = Assert.Single(initial.Chapters);
        Assert.Equal(1, chapter.Variables["counter"].GetDouble());

        await runtime.UpdateDevelopmentChapterVariablesAsync(
            "start",
            new Dictionary<string, JsonElement>
            {
                ["counter"] = JsonSerializer.SerializeToElement(5),
            });
        await runtime.UpdateDevelopmentObjectivesAsync(
            new Dictionary<string, string>
            {
                ["scan"] = "complete,3,3",
            });
        await runtime.UpdateDevelopmentMessagesAsync(
        [
            new RavenQuestMessage
            {
                Id = "manual",
                Received = DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
                Body = "Test",
            },
        ]);
        var unknown = await Assert.ThrowsAsync<InvalidDataException>(() =>
            runtime.UpdateDevelopmentChapterVariablesAsync(
                "start",
                new Dictionary<string, JsonElement>
                {
                    ["invented"] = JsonSerializer.SerializeToElement(true),
                }));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            runtime.UpdateDevelopmentObjectivesAsync(
                new Dictionary<string, string>
                {
                    ["scan"] = "not-a-state",
                }));

        Assert.Contains("Cannot add", unknown.Message, StringComparison.Ordinal);
        Assert.Equal(5, progress.Chapters[0].Variables["counter"].GetDouble());
        Assert.Equal("complete,3,3", progress.Objectives["scan"]);
        Assert.Equal("manual", Assert.Single(progress.Messages).Id);
        Assert.True(saves >= 4);
    }

    [Fact]
    public async Task DevelopmentPreparationValidatesInactiveChapterScripts()
    {
        var progress = CreateProgress(
            "function noop() end",
            new Dictionary<string, string>
            {
                ["broken"] = "this is not valid lua",
            });
        await using var runtime = new QuestScriptRuntime(progress);
        await runtime.InitializeAsync(startFirstChapter: true);

        var exception = await Assert.ThrowsAsync<QuestScriptException>(() =>
            runtime.PrepareDevelopmentChaptersAsync());

        Assert.Equal("broken", exception.ChapterId);
        Assert.Equal("load", exception.FunctionName);
    }

    [Fact]
    public async Task ImportedChapterVariablesResumeBeforeJournalDispatch()
    {
        var progress = CreateProgress(
            """
            counter = 0
            function on_Test(entry)
                counter = counter + 1
                return true
            end
            """);
        progress = progress with
        {
            StartTime = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        };
        progress.Chapters.Add(new RavenQuestChapterState
        {
            Id = "start",
            StartTime = progress.StartTime,
            Variables = new Dictionary<string, JsonElement>
            {
                ["counter"] = JsonSerializer.SerializeToElement(2),
            },
        });
        await using var runtime = new QuestScriptRuntime(progress);
        await runtime.InitializeAsync();
        using var journal = JsonDocument.Parse("{\"event\":\"Test\"}");

        await runtime.ProcessJournalEntryAsync(journal.RootElement);

        Assert.Equal(3, progress.Chapters[0].Variables["counter"].GetDouble());
    }

    [Fact]
    public async Task RemoteChapterSourceLoadsThroughPortableProvider()
    {
        var progress = CreateProgress(string.Empty);
        progress.Quest!.Chapters.Clear();
        progress = progress with
        {
            StartTime = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        };
        progress.Chapters.Add(new RavenQuestChapterState
        {
            Id = "start",
            StartTime = progress.StartTime,
        });
        var requests = 0;
        await using var runtime = new QuestScriptRuntime(
            progress,
            chapterSourceProvider: (reference, chapter, _) =>
            {
                requests++;
                Assert.Equal("publisher|sample|1", reference.ToString());
                Assert.Equal("start", chapter);
                return Task.FromResult<string?>(
                    """
                    function on_Test(entry)
                        quest:set("remote", entry.Value)
                        return true
                    end
                    """);
            });
        await runtime.InitializeAsync();
        using var journal = JsonDocument.Parse(
            "{\"event\":\"Test\",\"Value\":42}");

        await runtime.ProcessJournalEntryAsync(journal.RootElement);

        Assert.Equal(1, requests);
        Assert.Equal(42, progress.Variables["remote"].GetDouble());
    }

    [Fact]
    public async Task CommanderLibraryUsesPortableContextAndRetainedJournalData()
    {
        var progress = CreateProgress(
            """
            function onStart()
                quest:set("cmdrName", cmdr.name)
                quest:set("reputation", cmdr:getFactionRep("Test Faction"))
                quest:set("stateCount", arrlen(cmdr:getFactionStates("Test Faction", "active")))
                quest:set("statusFlags", cmdr.status.Flags)
                quest:set("near", cmdr:isWithin(12.5, -42.25, 5))
                quest:set("heading", cmdr:headingBetween(0, 20))
                quest:set("station", cmdr.lastDocked.StationName)
                return true
            end
            """);
        progress.KeptJournalEvents["Docked"] = JsonSerializer.SerializeToElement(
            new { @event = "Docked", StationName = "Jameson Memorial" });
        var context = new QuestCommanderContext(
            "Test Cmdr",
            JsonSerializer.SerializeToElement(new { Flags = 123 }),
            new QuestSurfaceContext(12.5, -42.25, 1_000_000, 350),
            new Dictionary<string, QuestFactionSnapshot>
            {
                ["Test Faction"] = new(
                    75,
                    0.42,
                    ["Boom", "Expansion"],
                    [],
                    []),
            });
        await using var runtime = new QuestScriptRuntime(progress, context);

        await runtime.InitializeAsync(startFirstChapter: true);

        Assert.Equal("Test Cmdr", progress.Variables["cmdrName"].GetString());
        Assert.Equal(75, progress.Variables["reputation"].GetDouble());
        Assert.Equal(2, progress.Variables["stateCount"].GetDouble());
        Assert.Equal(123, progress.Variables["statusFlags"].GetDouble());
        Assert.True(progress.Variables["near"].GetBoolean());
        Assert.True(progress.Variables["heading"].GetBoolean());
        Assert.Equal(
            "Jameson Memorial",
            progress.Variables["station"].GetString());
    }

    [Fact]
    public async Task ScriptErrorsExposeQuestChapterAndFunction()
    {
        var progress = CreateProgress(
            """
            function onStart()
                objective:show("missing")
            end
            """);
        await using var runtime = new QuestScriptRuntime(progress);

        var exception = await Assert.ThrowsAsync<QuestScriptException>(() =>
            runtime.InitializeAsync(startFirstChapter: true));

        Assert.Equal("start", exception.ChapterId);
        Assert.Equal("onStart", exception.FunctionName);
        Assert.Contains("publisher|sample|1", exception.Message);
    }

    [Fact]
    public async Task PriorJournalEventsAndHumanoidEmotesMatchLegacyHelpers()
    {
        var progress = CreateProgress(
            """
            function onStart()
                quest:set("priorStation", cmdr.lastDocked.StationName)
            end

            function onEmote(actor, action, target)
                quest:set("emoteActor", actor)
                quest:set("emoteAction", action)
                quest:set("emoteTarget", target)
                return true
            end
            """);
        var prior = new Dictionary<string, JsonElement>
        {
            ["Docked"] = JsonSerializer.SerializeToElement(
                new { @event = "Docked", StationName = "Jameson Memorial" }),
            ["FSDJump"] = JsonSerializer.SerializeToElement(
                new { @event = "FSDJump", StarSystem = "Shinrarta Dezhra" }),
        };
        var context = QuestCommanderContext.Empty with
        {
            PriorJournalEvents = prior,
        };
        await using var runtime = new QuestScriptRuntime(progress, context);
        await runtime.InitializeAsync(startFirstChapter: true);
        using var journal = JsonDocument.Parse(
            """
            {
              "event":"ReceiveText",
              "Message":"$HumanoidEmote_TargetMessage:#player=$cmdr_decorate:#name=Test Cmdr;:#targetedAction=$HumanoidEmote_wave_Action_Targeted;:#target=$npc_name_decorate:#name=Raven;"
            }
            """);

        Assert.True(await runtime.ProcessJournalEntryAsync(journal.RootElement));

        Assert.Equal(
            "Jameson Memorial",
            progress.Variables["priorStation"].GetString());
        Assert.Equal("Test Cmdr", progress.Variables["emoteActor"].GetString());
        Assert.Equal("wave", progress.Variables["emoteAction"].GetString());
        Assert.Equal("Raven", progress.Variables["emoteTarget"].GetString());
        Assert.Equal(2, progress.KeptJournalEvents.Count);
    }

    [Fact]
    public async Task InfiniteScriptHonorsCancellation()
    {
        var progress = CreateProgress("while true do end");
        await using var runtime = new QuestScriptRuntime(progress);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.InitializeAsync(
                startFirstChapter: true,
                cancellation.Token));
    }

    private static RavenCommanderQuest CreateProgress(
        string firstChapter,
        IReadOnlyDictionary<string, string>? additionalChapters = null)
    {
        var chapters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["start"] = firstChapter,
        };
        if (additionalChapters is not null)
        {
            foreach (var pair in additionalChapters)
            {
                chapters[pair.Key] = pair.Value;
            }
        }

        var definition = new RavenQuestDefinition
        {
            Publisher = "publisher",
            Id = "sample",
            Version = 1,
            Title = "Sample",
            FirstChapter = "start",
            Objectives = new Dictionary<string, string>
            {
                ["scan"] = "Scan three things",
            },
            Messages =
            [
                new RavenQuestMessageDefinition
                {
                    Id = "welcome",
                    From = "Raven",
                    Subject = "Hello",
                    Body = "Welcome",
                    Actions = new Dictionary<string, string>
                    {
                        ["go"] = "Proceed",
                    },
                },
            ],
            Chapters = chapters,
        };
        return new RavenCommanderQuest
        {
            Publisher = definition.Publisher,
            Id = definition.Id,
            Version = definition.Version,
            Quest = definition,
        };
    }

    private static bool IsActive(RavenQuestChapterState chapter)
    {
        return chapter.StartTime is not null && chapter.EndTime is null;
    }
}
