using System.Text;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Core.Tests.Quests;

public sealed class QuestRuntimeCoordinatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-quest-coordinator-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task BootstrapHydratesRemoteQuestWithoutDispatchingHistory()
    {
        var client = new FakeRavenQuestClient
        {
            ActiveQuests = [CreateRemoteProgress(quest: null)],
            Definition = CreateDefinition(
                "function on_Scan(entry) quest:set('body', entry.BodyName); return true end"),
        };
        await using var coordinator = CreateCoordinator(client);
        var events = new[]
        {
            Parse("""{"event":"Scan","BodyName":"Bootstrap body"}"""),
        };

        var bootstrap = await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            events,
            isBootstrap: true);

        var quest = Assert.Single(bootstrap.Quests);
        Assert.Equal("Remote Quest", quest.Title);
        Assert.Equal(0, bootstrap.ProcessedEventCount);
        Assert.Equal(1, client.LoadCount);
        Assert.Equal(1, client.DefinitionCount);
        Assert.Equal(0, client.SaveCount);

        var live = await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            [Parse("""{"event":"Scan","BodyName":"Live body"}""")],
            isBootstrap: false);

        Assert.Equal(1, live.ProcessedEventCount);
        Assert.Equal(1, client.SaveCount);
        Assert.Equal(
            "Live body",
            client.LastSaved!.Variables["body"].GetString());
        Assert.Equal(1, client.LoadCount);
    }

    [Fact]
    public async Task DevelopmentQuestOverridesRemoteAndSavesWithVerifiedBackup()
    {
        WriteDevelopmentQuest();
        var statePath = Path.Combine(temporaryDirectory, "quests", "F123.json");
        var originalBytes = await File.ReadAllBytesAsync(statePath);
        var client = new FakeRavenQuestClient
        {
            ActiveQuests = [CreateRemoteProgress(CreateDefinition(""))],
        };
        await using var coordinator = CreateCoordinator(client);

        var bootstrap = await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            [],
            isBootstrap: true);

        var development = Assert.Single(bootstrap.Quests);
        Assert.True(development.IsDevelopment);
        Assert.Equal("Development Quest", development.Title);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(statePath));

        await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            [Parse("""{"event":"Scan","BodyName":"A 1"}""")],
            isBootstrap: false);

        Assert.Equal(0, client.SaveCount);
        var backup = Assert.Single(Directory.GetFiles(
            Path.Combine(
                temporaryDirectory,
                "quests",
                "quest-state-backups")));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(backup));
        Assert.Contains(
            "A 1",
            await File.ReadAllTextAsync(statePath),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledCoordinatorDoesNotReadLocalOrRemoteQuestState()
    {
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "quests"));
        var statePath = Path.Combine(temporaryDirectory, "quests", "F123.json");
        var malformed = Encoding.UTF8.GetBytes("{not-json");
        await File.WriteAllBytesAsync(statePath, malformed);
        var client = new FakeRavenQuestClient();
        await using var coordinator = CreateCoordinator(client);

        var result = await coordinator.ApplyUpdateAsync(
            Configuration(enabled: false),
            temporaryDirectory,
            [Parse("""{"event":"Scan"}""")],
            isBootstrap: false);

        Assert.Empty(result.Quests);
        Assert.Empty(result.Warnings);
        Assert.Equal(0, result.ProcessedEventCount);
        Assert.Equal(0, client.LoadCount);
        Assert.Equal(malformed, await File.ReadAllBytesAsync(statePath));
    }

    [Fact]
    public async Task BrokenQuestIsIsolatedFromOtherRuntime()
    {
        var broken = CreateRemoteProgress(CreateDefinition("this is not lua"))
            with
        {
            Id = "broken",
            Quest = CreateDefinition("this is not lua") with
            {
                Id = "broken",
            },
        };
        var client = new FakeRavenQuestClient
        {
            ActiveQuests =
            [
                broken,
                CreateRemoteProgress(CreateDefinition("function noop() end")),
            ],
        };
        await using var coordinator = CreateCoordinator(client);

        var result = await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            [],
            isBootstrap: true);

        Assert.Single(result.Quests);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("broken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuxiliaryFilePayloadIsDispatchedAndRemainsUnchanged()
    {
        var cargoPath = Path.Combine(temporaryDirectory, "Cargo.json");
        Directory.CreateDirectory(temporaryDirectory);
        var cargoBytes = Encoding.UTF8.GetBytes(
            """{"event":"Cargo","Inventory":[{"Name":"gold"}]}""");
        await File.WriteAllBytesAsync(cargoPath, cargoBytes);
        var definition = CreateDefinition(
            "function on_Cargo(entry) quest:set('cargo', entry.Inventory[1].Name); return true end");
        var client = new FakeRavenQuestClient
        {
            ActiveQuests = [CreateRemoteProgress(definition)],
        };
        await using var coordinator = CreateCoordinator(client);

        await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            [Parse("""{"event":"Cargo"}""")],
            isBootstrap: false);

        Assert.Equal("gold", client.LastSaved?.Variables["cargo"].GetString());
        Assert.Equal(cargoBytes, await File.ReadAllBytesAsync(cargoPath));
    }

    [Fact]
    public async Task CatalogActivationAndCommanderLifecycleUseLiveCoordinator()
    {
        var definition = CreateDefinition(
            "function onStart() quest:set('started', true); return true end");
        var client = new FakeRavenQuestClient
        {
            PublishedQuests = [definition],
            CommanderStatuses =
            [
                new RavenCommanderQuestStatus
                {
                    Publisher = "Raven",
                    Id = "older",
                    Version = 1,
                    State = RavenQuestState.complete,
                },
            ],
            ActivatedDefinition = definition,
        };
        await using var coordinator = CreateCoordinator(client);
        await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            [],
            isBootstrap: true);

        Assert.Single(await coordinator.GetPublishedQuestsAsync());
        Assert.Single(await coordinator.GetCommanderQuestStatusesAsync());

        await coordinator.ActivateQuestAsync("Raven", "sample");

        Assert.Single(coordinator.Snapshot);
        Assert.Equal(1, client.ActivateCount);
        Assert.Equal(1, client.SaveCount);
        Assert.True(client.LastSaved!.Variables["started"].GetBoolean());

        await coordinator.PauseQuestAsync(definition.Reference);

        Assert.Empty(coordinator.Snapshot);
        Assert.Contains(RavenQuestState.paused, client.StateChanges);

        client.ActiveQuests = [CreateRemoteProgress(definition)];
        await coordinator.ResumeQuestAsync(definition.Reference);

        Assert.Single(coordinator.Snapshot);
        Assert.Contains(RavenQuestState.active, client.StateChanges);

        await coordinator.RemoveQuestAsync(definition.Reference);

        Assert.Empty(coordinator.Snapshot);
        Assert.Equal(1, client.DeleteCount);
    }

    [Fact]
    public async Task RemovingDevelopmentQuestClearsItThroughVerifiedSave()
    {
        WriteDevelopmentQuest();
        var statePath = Path.Combine(temporaryDirectory, "quests", "F123.json");
        var sourceBytes = await File.ReadAllBytesAsync(statePath);
        await using var coordinator = CreateCoordinator(
            new FakeRavenQuestClient());
        await coordinator.ApplyUpdateAsync(
            Configuration(),
            temporaryDirectory,
            [],
            isBootstrap: true);
        var reference = Assert.Single(coordinator.Snapshot).Reference;

        await coordinator.RemoveQuestAsync(reference);

        Assert.Empty(coordinator.Snapshot);
        var saved = await File.ReadAllTextAsync(statePath);
        Assert.DoesNotContain("devRef", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("devQuest", saved, StringComparison.Ordinal);
        Assert.Contains("futureRoot", saved, StringComparison.Ordinal);
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(
            temporaryDirectory,
            "quests",
            "quest-state-backups")));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(backup));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private QuestRuntimeCoordinator CreateCoordinator(
        FakeRavenQuestClient client)
    {
        return new QuestRuntimeCoordinator(
            new LegacyQuestStateStore(temporaryDirectory),
            client);
    }

    private static QuestRuntimeConfiguration Configuration(bool enabled = true)
    {
        return new QuestRuntimeConfiguration(
            enabled,
            "F123",
            "Test Cmdr",
            "secret",
            null);
    }

    private void WriteDevelopmentQuest()
    {
        var directory = Path.Combine(temporaryDirectory, "quests");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "F123.json"),
            """
            {
              "fid":"F123",
              "cmdr":"Test Cmdr",
              "devRef":"Raven|sample|1",
              "devQuest":{
                "startTime":"2026-07-01T00:00:00Z",
                "chapters":[{"id":"start","startTime":"2026-07-01T00:00:00Z"}],
                "vars":{},
                "future":"preserve"
              },
              "futureRoot":true
            }
            """);
        File.WriteAllText(
            Path.Combine(directory, "dev-sample.json"),
            """
            {
              "publisher":"Raven",
              "id":"sample",
              "ver":1,
              "title":"Development Quest",
              "firstChapter":"start",
              "objectives":{},
              "strings":{},
              "msgs":[],
              "chapters":{
                "start":"function on_Scan(entry) quest:set('body', entry.BodyName); return true end"
              }
            }
            """);
    }

    private static RavenCommanderQuest CreateRemoteProgress(
        RavenQuestDefinition? quest)
    {
        return new RavenCommanderQuest
        {
            Publisher = "Raven",
            Id = "sample",
            Version = 1,
            Quest = quest,
            StartTime = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            Chapters =
            [
                new RavenQuestChapterState
                {
                    Id = "start",
                    StartTime = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                },
            ],
        };
    }

    private static RavenQuestDefinition CreateDefinition(string source)
    {
        return new RavenQuestDefinition
        {
            Publisher = "Raven",
            Id = "sample",
            Version = 1,
            Title = "Remote Quest",
            FirstChapter = "start",
            Chapters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["start"] = source,
            },
        };
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var result, out var error), error);
        return Assert.IsType<JournalEventEnvelope>(result);
    }

    private sealed class FakeRavenQuestClient : IRavenQuestClient
    {
        public IReadOnlyList<RavenCommanderQuest> ActiveQuests { get; set; } = [];

        public IReadOnlyList<RavenQuestDefinition> PublishedQuests { get; init; } = [];

        public IReadOnlyList<RavenCommanderQuestStatus> CommanderStatuses
        {
            get;
            init;
        } = [];

        public RavenQuestDefinition? Definition { get; init; }

        public RavenQuestDefinition? ActivatedDefinition { get; init; }

        public int LoadCount { get; private set; }

        public int DefinitionCount { get; private set; }

        public int SaveCount { get; private set; }

        public int ActivateCount { get; private set; }

        public int DeleteCount { get; private set; }

        public List<RavenQuestState> StateChanges { get; } = [];

        public RavenCommanderQuest? LastSaved { get; private set; }

        public Task<IReadOnlyList<RavenQuestDefinition>> GetPublishedQuestsAsync(
            string? apiKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PublishedQuests);

        public Task<RavenQuestDefinition?> GetQuestAsync(
            RavenQuestReference reference,
            string? apiKey = null,
            CancellationToken cancellationToken = default)
        {
            DefinitionCount++;
            return Task.FromResult(Definition);
        }

        public Task<string> PublishQuestAsync(
            RavenQuestDefinition quest,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("OK");

        public Task SaveCommanderQuestAsync(
            RavenCommanderQuest quest,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastSaved = quest;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RavenCommanderQuest>> LoadCommanderQuestsAsync(
            RavenQuestState state,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(ActiveQuests);
        }

        public Task<IReadOnlyList<RavenCommanderQuestStatus>>
            GetCommanderQuestStatusesAsync(
                string apiKey,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(CommanderStatuses);

        public Task<RavenQuestDefinition> ActivateQuestAsync(
            string publisher,
            string id,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            ActivateCount++;
            return Task.FromResult(
                ActivatedDefinition
                    ?? throw new InvalidOperationException(
                        "No activation definition was configured."));
        }

        public Task<bool> DeleteQuestAsync(
            string publisher,
            string id,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            return Task.FromResult(true);
        }

        public Task<bool> SetQuestStateAsync(
            string publisher,
            string id,
            RavenQuestState state,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            StateChanges.Add(state);
            return Task.FromResult(true);
        }

        public Task<string?> GetQuestChapterAsync(
            RavenQuestReference reference,
            string chapterId,
            string? apiKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
