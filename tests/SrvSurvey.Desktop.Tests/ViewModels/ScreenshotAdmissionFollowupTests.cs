using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class SteamScreenshotSettingsTests
{
    [Fact]
    public async Task DurableLibraryAdmissionIsBoundedAndEventuallyProcessesTheWholeLibrary()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.1970-01-01T050000.01.log"),
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Location","StarSystem":"Sol","Body":"Earth","BodyType":"Planet"}
            {"timestamp":"1970-01-01T05:07:00Z","event":"FuelScoop","Total":10}
            """
        );
        string folder = Platform.SteamScreenshotSourceTests.CreateAccount(Path.Combine(directory, "Steam"), "42");
        for (int i = 0; i < 12; i++)
        {
            Platform.SteamScreenshotProcessingTests.WriteJpeg(Path.Combine(folder, $"197001010506{i:D2}_1.jpg"));
        }
        string output = Path.Combine(directory, "output");
        string queue = Path.Combine(directory, "queue");
        var settings = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "settings.json"));
        settings.Save(
            ScreenshotProcessingPreferences.CreateDefaults(journals) with
            {
                Enabled = true,
                ProcessSteamScreenshots = true,
                SourceFolder = folder,
                SteamSourceFolder = folder,
                TargetFolder = output,
                AddBanner = false,
            }
        );
        var clock = new SteamReviewClock();
        var processor = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            pendingQueueDirectory: queue
        );
        var model = new ScreenshotProcessingViewModel(
            settings,
            processor,
            journals,
            steamScreenshotSource: new SteamScreenshotSource([], TimeZoneInfo.Utc),
            timeProvider: clock
        );
        await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        Assert.InRange(Directory.GetFiles(queue, "*.json", SearchOption.AllDirectories).Length, 1, 4);
        for (int i = 0; i < 20; i++)
        {
            clock.Advance();
            await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        }
        Assert.Equal(12, Directory.GetFiles(output, "*.png", SearchOption.AllDirectories).Length);
        Assert.Equal(0, processor.PendingCount);
        Assert.Equal(12, Directory.GetFiles(queue, "*.json", SearchOption.AllDirectories).Length);
    }
}

public sealed partial class ScreenshotProcessingViewModelTests
{
    [Fact]
    public void AFailedOutputPinRetriesTheFrozenSessionFolderAfterTheJournalChanges()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string blocked = Path.Combine(temporaryDirectory, "settings-directory");
        File.WriteAllText(blocked, "blocked");
        string Profile(string name) =>
            Path.Combine(
                temporaryDirectory,
                name,
                "drive_c",
                "users",
                "steamuser",
                "Saved Games",
                "Frontier Developments",
                "Elite Dangerous"
            );
        string first = Profile("first");
        string second = Profile("second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(blocked, "settings.json"));
        var model = new ScreenshotProcessingViewModel(store, journalDirectory: first);
        string expected = Path.Combine(
            temporaryDirectory,
            "first",
            "drive_c",
            "users",
            "steamuser",
            "Pictures",
            "Frontier Developments",
            "Elite Dangerous",
            "converted"
        );
        Assert.Equal(expected, model.TargetFolder);
        File.Delete(blocked);
        Directory.CreateDirectory(blocked);
        model.UpdateJournalDirectory(second);
        Assert.Equal(expected, model.TargetFolder);
        Assert.Equal(expected, new ScreenshotProcessingViewModel(store, journalDirectory: second).TargetFolder);
    }
}
