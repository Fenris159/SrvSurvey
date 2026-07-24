using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JourneyWorkspaceViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-view-model-tests-{Guid.NewGuid():N}");

    private string DataDirectory => Path.Combine(temporaryDirectory, "data");

    private string JournalDirectory => Path.Combine(temporaryDirectory, "journals");

    [Fact]
    public async Task BeginSaveTrackAndConcludeCoversJourneyWorkspaceFlow()
    {
        await WriteJournalAsync(
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """);
        var viewModel = CreateViewModel();

        Assert.True(await viewModel.UpdateContextAsync(
            "F123", "Drew", true, "Sol", 42));
        await viewModel.StartNewJourneyAsync();
        viewModel.NewJourneyName = "Across the black";
        viewModel.NewJourneyDescription = "A long expedition";
        await viewModel.FindStartAsync();
        await viewModel.BeginJourneyAsync();

        Assert.True(viewModel.HasActiveJourney);
        Assert.Single(viewModel.Journeys);
        Assert.Equal("Across the black", viewModel.JourneyName);
        Assert.Single(viewModel.VisitedSystems);
        Assert.Contains("Last arrived in Sol", viewModel.StartStatus);

        viewModel.SelectedSystemNotes = "Remember this system";
        viewModel.JourneyDescription = "Updated description";
        await viewModel.SaveAsync();

        Assert.False(viewModel.IsDirty);
        Assert.Equal("Updated description", viewModel.JourneyDescription);
        Assert.Equal(1, viewModel.SelectedSystem!.Visit.Counts.Notes);
        var note = await new SystemNoteStore(DataDirectory)
            .LoadAsync("F123", "Sol", 42);
        Assert.Equal("Remember this system", note.Notes);

        await viewModel.RefreshAsync();

        Assert.Equal("Active journey: Across the black.", viewModel.StatusMessage);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse("""{"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}"""),
        ]);
        Assert.Contains(
            viewModel.QuickStatistics,
            statistic => statistic.Label == "Screenshots"
                && statistic.Value == "1");

        await viewModel.ConfirmConcludeAsync();

        Assert.False(viewModel.HasActiveJourney);
        Assert.Equal("COMPLETE", viewModel.SelectedJourney!.State);
        Assert.Contains("concluded", viewModel.JourneyByline);
    }

    [Fact]
    public async Task PriorSystemSearchFindsJournalStartAndCanBeCancelled()
    {
        await WriteJournalAsync(
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Achenar","SystemAddress":99,"StarPos":[1,2,3]}
            """);
        var viewModel = CreateViewModel(
        [
            new StarSystemReference(
                "Achenar",
                99,
                new GalacticCoordinate(1, 2, 3)),
        ]);
        await viewModel.UpdateContextAsync(
            "F123", "Drew", true, "Sol", 42);
        await viewModel.StartNewJourneyAsync();
        viewModel.UseCurrentStart = false;
        viewModel.StartSystemQuery = "Achenar";

        await viewModel.SearchSystemsAsync();
        await viewModel.FindStartAsync();

        Assert.Single(viewModel.StartSystemResults);
        Assert.Equal(99, viewModel.SelectedStartSystem!.SystemAddress);
        Assert.Contains("Last arrived in Achenar", viewModel.StartStatus);
        viewModel.CancelNewJourneyCommand.Execute(null);
        Assert.False(viewModel.IsCreating);
    }

    [Fact]
    public async Task PreferencesPersistAndReformatGalacticTime()
    {
        await WriteJournalAsync(
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """);
        var viewModel = CreateViewModel();
        await viewModel.UpdateContextAsync(
            "F123", "Drew", true, "Sol", 42);
        await viewModel.StartNewJourneyAsync();
        viewModel.NewJourneyName = "Journey";
        await viewModel.FindStartAsync();
        await viewModel.BeginJourneyAsync();

        await viewModel.SetPreferencesAsync(true, true);

        Assert.True(viewModel.AlwaysOnTop);
        Assert.True(viewModel.UseGalacticTime);
        Assert.Contains("UTC", viewModel.JourneyByline);
        var settings = new SystemNotesSettingsStore(DataDirectory).Load();
        Assert.True(settings.Snapshot?.JourneyAlwaysOnTop);
        Assert.True(settings.Snapshot?.JourneyUseGalacticTime);
    }

    private JourneyWorkspaceViewModel CreateViewModel(
        IReadOnlyList<StarSystemReference>? searchResults = null)
    {
        var noteStore = new SystemNoteStore(DataDirectory);
        var settingsStore = new SystemNotesSettingsStore(DataDirectory);
        return new JourneyWorkspaceViewModel(
            new JourneyService(
                new JourneyStore(DataDirectory),
                new JourneyJournalHistoryReader(JournalDirectory),
                new CommanderProfileStore(DataDirectory),
                new ExobiologyReferenceCatalog([])),
            new StubSystemResolver(searchResults ?? []),
            noteStore,
            settingsStore);
    }

    private async Task WriteJournalAsync(string content)
    {
        Directory.CreateDirectory(JournalDirectory);
        var path = Path.Combine(
            JournalDirectory,
            "Journal.2026-07-01T000000.01.log");
        await File.WriteAllTextAsync(path, content);
        File.SetLastWriteTimeUtc(
            path,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubSystemResolver(
        IReadOnlyList<StarSystemReference> results) : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(results);
        }
    }
}
