using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ScreenshotProcessingViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-screenshot-vm-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void PreferencesAndShortcutTogglePersist()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string source = Path.Combine(temporaryDirectory, "source");
        Directory.CreateDirectory(source);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var store = new ScreenshotProcessingSettingsStore(path);
        var viewModel = new ScreenshotProcessingViewModel(store, new StubProcessor(ScreenshotProcessingResult.Empty));

        viewModel.Enabled = true;
        viewModel.SourceFolder = source;
        viewModel.TargetFolder = Path.Combine(temporaryDirectory, "target");
        viewModel.DeleteOriginal = true;
        viewModel.AerialAltitudeAlpha = 1_100;
        viewModel.AerialAltitudeBeta = 1_500;
        viewModel.AerialAltitudeGamma = 6_000;
        Assert.True(viewModel.ToggleBanner());

        ScreenshotProcessingPreferences saved = store.Load();
        Assert.True(saved.Enabled);
        Assert.Equal(source, saved.SourceFolder);
        Assert.Equal(Path.Combine(temporaryDirectory, "target"), saved.TargetFolder);
        Assert.True(saved.DeleteOriginal);
        Assert.False(saved.AddBanner);
        Assert.Equal(1_100, saved.AerialAltitudeAlpha);
        Assert.Equal(1_500, saved.AerialAltitudeBeta);
        Assert.Equal(5_000, saved.AerialAltitudeGamma);
        Assert.Contains("disabled", viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutomaticSourceFollowsTheJournalWhileOutputStaysFixedAcrossRestarts(bool overrideSource)
    {
        string first = Path.Combine(temporaryDirectory, "first", "drive_c", "users", "steamuser");
        string second = Path.Combine(temporaryDirectory, "second", "drive_c", "users", "steamuser");
        string firstJournal = Path.Combine(first, "Saved Games", "Frontier Developments", "Elite Dangerous");
        string secondJournal = Path.Combine(second, "Saved Games", "Frontier Developments", "Elite Dangerous");
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(temporaryDirectory, "settings.json"));
        var model = new ScreenshotProcessingViewModel(store, journalDirectory: firstJournal);
        model.UpdateJournalDirectory(secondJournal);

        Assert.Equal(Path.Combine(second, "Pictures", "Frontier Developments", "Elite Dangerous"), model.SourceFolder);
        Assert.Equal(
            Path.Combine(first, "Pictures", "Frontier Developments", "Elite Dangerous", "converted"),
            model.TargetFolder
        );

        string custom = Path.Combine(first, "Pictures", "Frontier Developments", "Elite Dangerous");
        if (overrideSource)
        {
            model.SourceFolder = custom;
        }
        else
        {
            model.TargetFolder = Path.Combine(temporaryDirectory, "legacy-output");
        }
        model.UpdateJournalDirectory(firstJournal);
        model.AddBanner = false;
        model.UpdateJournalDirectory(secondJournal);
        string expectedSource = overrideSource
            ? custom
            : Path.Combine(second, "Pictures", "Frontier Developments", "Elite Dangerous");
        string expectedTarget = overrideSource
            ? Path.Combine(first, "Pictures", "Frontier Developments", "Elite Dangerous", "converted")
            : Path.Combine(temporaryDirectory, "legacy-output");
        Assert.Equal(expectedSource, model.SourceFolder);
        Assert.Equal(expectedTarget, model.TargetFolder);
        var reopened = new ScreenshotProcessingViewModel(store, journalDirectory: secondJournal);
        Assert.Equal(expectedSource, reopened.SourceFolder);
        Assert.Equal(expectedTarget, reopened.TargetFolder);
    }

    [Fact]
    public void ExplicitlyPickingTheDisplayedSourceKeepsItManualAcrossProfilesAndRestart()
    {
        string first = Path.Combine(
            temporaryDirectory,
            "first",
            "drive_c",
            "users",
            "steamuser",
            "Saved Games",
            "Frontier Developments",
            "Elite Dangerous"
        );
        string second = Path.Combine(
            temporaryDirectory,
            "second",
            "drive_c",
            "users",
            "steamuser",
            "Saved Games",
            "Frontier Developments",
            "Elite Dangerous"
        );
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(temporaryDirectory, "settings.json"));
        var model = new ScreenshotProcessingViewModel(store, journalDirectory: first);
        string selected = model.SourceFolder;
        model.SelectSourceFolder(selected);
        model.UpdateJournalDirectory(second);
        Assert.Equal(selected, model.SourceFolder);
        Assert.Equal(selected, new ScreenshotProcessingViewModel(store, journalDirectory: second).SourceFolder);
    }

    [Fact]
    public void OutputPinFailureRetainsAnAbsoluteDestinationAndReportsTheError()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string blocker = Path.Combine(temporaryDirectory, "not-a-directory");
        File.WriteAllText(blocker, "block settings creation");
        var model = new ScreenshotProcessingViewModel(
            new ScreenshotProcessingSettingsStore(Path.Combine(blocker, "settings.json"))
        );
        Assert.True(Path.IsPathFullyQualified(model.TargetFolder));
        Assert.Contains("could not be saved", model.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("block settings creation", File.ReadAllText(blocker));
    }

    [Fact]
    public async Task ProcessingResultIsReportedWithWarnings()
    {
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
        var result = new ScreenshotProcessingResult(
            [new ScreenshotConversion("source.bmp", "target.png", false, null)],
            ["The original was retained."]
        );
        var processor = new StubProcessor(result);
        var viewModel = new ScreenshotProcessingViewModel(store, processor);
        JournalEventEnvelope journalEvent = Parse(
            """
            {"timestamp":"2026-07-25T12:00:00Z","event":"Screenshot","Filename":"\\ED_Pictures\\source.bmp"}
            """
        );

        await viewModel.ProcessJournalEventsAsync([journalEvent], "Commander Test");

        Assert.Equal("Commander Test", processor.CommanderName);
        Assert.Same(journalEvent, Assert.Single(processor.Events));
        Assert.Contains("target.png", viewModel.StatusMessage);
        Assert.Contains("original was retained", viewModel.StatusMessage);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? journalEvent, out string? error),
            error
        );
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubProcessor(ScreenshotProcessingResult result) : IScreenshotProcessingService
    {
        public IReadOnlyList<JournalEventEnvelope> Events { get; private set; } = [];

        public string? CommanderName { get; private set; }

        public Task<ScreenshotProcessingResult> ProcessAsync(
            IReadOnlyList<JournalEventEnvelope> journalEvents,
            ScreenshotProcessingPreferences preferences,
            string? commanderName,
            IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
            ScreenshotNavigationContext? navigationContext = null,
            CancellationToken cancellationToken = default
        )
        {
            Events = journalEvents;
            CommanderName = commanderName;
            return Task.FromResult(result);
        }
    }
}
