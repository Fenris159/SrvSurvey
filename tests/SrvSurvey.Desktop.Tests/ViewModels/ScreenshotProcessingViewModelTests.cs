using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ScreenshotProcessingViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-screenshot-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void PreferencesAndShortcutTogglePersist()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var source = Path.Combine(temporaryDirectory, "source");
        Directory.CreateDirectory(source);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var store = new ScreenshotProcessingSettingsStore(path);
        var viewModel = new ScreenshotProcessingViewModel(
            store,
            new StubProcessor(ScreenshotProcessingResult.Empty));

        viewModel.Enabled = true;
        viewModel.SourceFolder = source;
        viewModel.TargetFolder = Path.Combine(temporaryDirectory, "target");
        viewModel.DeleteOriginal = true;
        Assert.True(viewModel.ToggleBanner());

        var saved = store.Load();
        Assert.True(saved.Enabled);
        Assert.Equal(source, saved.SourceFolder);
        Assert.Equal(
            Path.Combine(temporaryDirectory, "target"),
            saved.TargetFolder);
        Assert.True(saved.DeleteOriginal);
        Assert.False(saved.AddBanner);
        Assert.Contains("disabled", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ProcessingResultIsReportedWithWarnings()
    {
        var store = new ScreenshotProcessingSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
        var result = new ScreenshotProcessingResult(
            [new ScreenshotConversion(
                "source.bmp",
                "target.png",
                false,
                null)],
            ["The original was retained."]);
        var processor = new StubProcessor(result);
        var viewModel = new ScreenshotProcessingViewModel(store, processor);
        var journalEvent = Parse(
            """
            {"timestamp":"2026-07-25T12:00:00Z","event":"Screenshot","Filename":"\\ED_Pictures\\source.bmp"}
            """);

        await viewModel.ProcessJournalEventsAsync(
            [journalEvent],
            "Commander Test");

        Assert.Equal("Commander Test", processor.CommanderName);
        Assert.Same(journalEvent, Assert.Single(processor.Events));
        Assert.Contains("target.png", viewModel.StatusMessage);
        Assert.Contains("original was retained", viewModel.StatusMessage);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error), error);
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubProcessor(ScreenshotProcessingResult result)
        : IScreenshotProcessingService
    {
        public IReadOnlyList<JournalEventEnvelope> Events { get; private set; } = [];

        public string? CommanderName { get; private set; }

        public Task<ScreenshotProcessingResult> ProcessAsync(
            IReadOnlyList<JournalEventEnvelope> journalEvents,
            ScreenshotProcessingPreferences preferences,
            string? commanderName,
            CancellationToken cancellationToken = default)
        {
            Events = journalEvents;
            CommanderName = commanderName;
            return Task.FromResult(result);
        }
    }
}
