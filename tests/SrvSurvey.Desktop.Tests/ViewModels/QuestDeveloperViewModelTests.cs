using SrvSurvey.Core.Quests;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class QuestDeveloperViewModelTests : IAsyncLifetime
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "srv-survey-quest-developer-vm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRavenQuestClient client = new();
    private QuestRuntimeCoordinator? coordinator;

    [Fact]
    public async Task ImportEditDebugPublishAndRemoveWorkflowIsReachable()
    {
        var source = Path.Combine(temporaryDirectory, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "quest.json"),
            """
            {
              "publisher":"Raven",
              "id":"developer",
              "ver":1,
              "title":"Developer Quest",
              "firstChapter":"start",
              "objectives":{"scan":"Scan"},
              "chapters":{}
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(source, "start.lua"),
            "counter = 1");
        var sourceBytes = Directory.GetFiles(source)
            .ToDictionary(path => path, File.ReadAllBytes);
        using var viewModel = new QuestDeveloperViewModel(coordinator!);

        await viewModel.ImportFolderAsync(source);

        Assert.True(viewModel.HasDevelopmentQuest);
        Assert.Equal("Developer Quest", viewModel.Title);
        Assert.Equal("1", viewModel.VersionLabel);
        Assert.Equal(3, viewModel.Views.Count);
        Assert.Equal(source, viewModel.SourceDirectory);
        viewModel.SelectedView = viewModel.Views.Single(view =>
            view.Kind == QuestDevelopmentViewKind.Chapter);
        Assert.True(viewModel.IsSelectedChapterActive);
        Assert.Contains("\"counter\": 1", viewModel.EditorJson);

        viewModel.EditorJson = """{"counter":4}""";
        await viewModel.ApplyEditorAsync();
        viewModel.DebugCode = "counter = counter + 1; return counter";
        await viewModel.RunDebugAsync();

        Assert.Equal("5", viewModel.DebugResult);
        Assert.Contains("\"counter\": 5", viewModel.EditorJson);
        viewModel.PublishConfirmed = false;
        await viewModel.PublishAsync();
        Assert.Equal(0, client.PublishCount);
        viewModel.PublishConfirmed = true;
        await viewModel.PublishAsync();
        Assert.Equal(1, client.PublishCount);
        Assert.False(viewModel.PublishConfirmed);

        await viewModel.RemoveAsync();
        Assert.Equal("Confirm removal", viewModel.RemoveButtonText);
        await viewModel.RemoveAsync();
        Assert.False(viewModel.HasDevelopmentQuest);
        Assert.Empty(coordinator!.Snapshot);
        foreach (var pair in sourceBytes)
        {
            Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
        }
    }

    [Fact]
    public async Task InvalidEditorDoesNotChangeSavedChapterVariables()
    {
        var source = Path.Combine(temporaryDirectory, "source-invalid");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "quest.json"),
            """
            {
              "publisher":"Raven",
              "id":"developer",
              "ver":1,
              "title":"Developer Quest",
              "firstChapter":"start",
              "chapters":{}
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(source, "start.lua"),
            "counter = 1");
        using var viewModel = new QuestDeveloperViewModel(coordinator!);
        await viewModel.ImportFolderAsync(source);
        viewModel.SelectedView = viewModel.Views.Single(view =>
            view.Kind == QuestDevelopmentViewKind.Chapter);

        viewModel.EditorJson = """{"invented":true}""";
        await viewModel.ApplyEditorAsync();

        Assert.Contains("Cannot add", viewModel.StatusMessage);
        await viewModel.RefreshStateAsync();
        Assert.Contains("\"counter\": 1", viewModel.EditorJson);
    }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(temporaryDirectory);
        coordinator = new QuestRuntimeCoordinator(
            new LegacyQuestStateStore(temporaryDirectory),
            client);
        await coordinator.ApplyUpdateAsync(
            new QuestRuntimeConfiguration(
                true,
                "F123",
                "Test Cmdr",
                "secret",
                null),
            temporaryDirectory,
            [],
            isBootstrap: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (coordinator is not null)
        {
            await coordinator.DisposeAsync();
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class FakeRavenQuestClient : IRavenQuestClient
    {
        public int PublishCount { get; private set; }

        public Task<IReadOnlyList<RavenQuestDefinition>> GetPublishedQuestsAsync(
            string? apiKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RavenQuestDefinition>>([]);

        public Task<RavenQuestDefinition?> GetQuestAsync(
            RavenQuestReference reference,
            string? apiKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RavenQuestDefinition?>(null);

        public Task<string> PublishQuestAsync(
            RavenQuestDefinition quest,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.FromResult("OK");
        }

        public Task SaveCommanderQuestAsync(
            RavenCommanderQuest quest,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RavenCommanderQuest>> LoadCommanderQuestsAsync(
            RavenQuestState state,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RavenCommanderQuest>>([]);

        public Task<IReadOnlyList<RavenCommanderQuestStatus>>
            GetCommanderQuestStatusesAsync(
                string apiKey,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RavenCommanderQuestStatus>>([]);

        public Task<RavenQuestDefinition> ActivateQuestAsync(
            string publisher,
            string id,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteQuestAsync(
            string publisher,
            string id,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> SetQuestStateAsync(
            string publisher,
            string id,
            RavenQuestState state,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<string?> GetQuestChapterAsync(
            RavenQuestReference reference,
            string chapterId,
            string? apiKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
