using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class SteamScreenshotSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-steam-options-{Guid.NewGuid():N}");

    [Fact]
    public void ManualSteamFolderSurvivesSettingsAndJournalChangesAndCanReturnToAutomaticDetection()
    {
        string root = Path.Combine(directory, "Steam");
        string automatic = Platform.SteamScreenshotSourceTests.CreateAccount(root, "42");
        string manual = Platform.SteamScreenshotSourceTests.CreateAccount(
            Path.Combine(directory, "second-Steam"),
            "99"
        );
        var source = new SteamScreenshotSource([root]);
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(
            ScreenshotProcessingPreferences.CreateDefaults() with
            {
                TargetFolder = Path.Combine(directory, "converted"),
            }
        );
        var model = new ScreenshotProcessingViewModel(store, steamScreenshotSource: source);
        Assert.False(model.ProcessSteamScreenshots);
        Assert.Equal(string.Empty, model.SteamSourceFolder);
        model.DetectSteamSourceFolder();
        Assert.Equal(automatic, model.SteamSourceFolder);
        Assert.False(model.ProcessSteamScreenshots);
        model.ProcessSteamScreenshots = true;
        Assert.Equal(automatic, model.SteamSourceFolder);
        model.SelectSteamSourceFolder(manual);
        model.UpdateJournalDirectory(Path.Combine(directory, "another-journal"));
        model.AddBanner = false;

        var restored = new ScreenshotProcessingViewModel(store, steamScreenshotSource: source);
        Assert.True(restored.ProcessSteamScreenshots);
        Assert.Equal(manual, restored.SteamSourceFolder);
        Assert.Equal(Path.Combine(directory, "converted"), restored.TargetFolder);
        Assert.False(restored.AddBanner);
        restored.DetectSteamSourceFolder();
        Assert.Equal(automatic, restored.SteamSourceFolder);
        Assert.Null(store.Load().SteamSourceFolder);
        Assert.Throws<ArgumentException>(() => restored.SelectSteamSourceFolder("relative"));
    }

    [Fact]
    public async Task NativeAndSteamCapturesAreReportedOnSeparatePollsWithoutLosingEitherImage()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.1970-01-01T050000.01.log"),
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader"}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","Body":"Earth","BodyType":"Planet"}
            {"timestamp":"1970-01-01T05:07:00Z","event":"FuelScoop","Total":10}
            """
        );
        string steam = Platform.SteamScreenshotSourceTests.CreateAccount(Path.Combine(directory, "Steam"), "42");
        Platform.SteamScreenshotProcessingTests.WriteJpeg(Path.Combine(steam, "capture.jpg"));
        await File.WriteAllTextAsync(
            Platform.SteamScreenshotSourceTests.IndexPath(steam),
            "\"screenshots\" { \"359320\" { \"0\" { \"filename\" \"359320/screenshots/capture.jpg\" \"creation\" \"18413\" } } }"
        );
        string source = Path.Combine(directory, "native");
        Directory.CreateDirectory(source);
        Platform.ScreenshotProcessingServiceTests.CreateBitmap(
            Path.Combine(source, "capture.bmp"),
            SkiaSharp.SKColors.Cyan
        );
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(
            ScreenshotProcessingPreferences.CreateDefaults(journals) with
            {
                Enabled = true,
                ProcessSteamScreenshots = true,
                SteamSourceFolder = steam,
                SourceFolder = source,
                TargetFolder = Path.Combine(directory, "converted"),
                AddBanner = false,
            }
        );
        var model = new ScreenshotProcessingViewModel(
            store,
            new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask),
            journals,
            steamScreenshotSource: new SteamScreenshotSource([])
        );
        Assert.True(
            SrvSurvey.Core.Journal.JournalEventEnvelope.TryParse(
                """{"timestamp":"1970-01-01T05:06:53Z","event":"Screenshot","Filename":"capture.bmp","System":"Sol","Body":"Moon"}""",
                out SrvSurvey.Core.Journal.JournalEventEnvelope? capture,
                out _
            )
        );

        ScreenshotProcessingResult native = await model.ProcessJournalEventsAsync(
            [capture!],
            "Test",
            frontierId: "F123"
        );

        string nativePath = Path.Combine(model.TargetFolder, "Sol", "Moon (1970-01-01 050653).png");
        string steamPath = Path.Combine(model.TargetFolder, "Sol", "Earth (1970-01-01 050653).png");
        Assert.Empty(native.Warnings);
        Assert.Equal(nativePath, Assert.Single(native.Conversions).OutputPath);
        Assert.False(File.Exists(steamPath));
        ScreenshotProcessingResult imported = await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        Assert.Empty(imported.Warnings);
        Assert.Equal(steamPath, Assert.Single(imported.Conversions).OutputPath);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(model.TargetFolder, "Sol"), "*.png").Length);
    }

    [Fact]
    public async Task UncheckingProcessSteamScreenshotsPausesPendingSteamCapturesWhileConvertingHighResBitmaps()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.1970-01-01T050000.01.log"),
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader"}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","Body":"Earth","BodyType":"Planet"}
            {"timestamp":"1970-01-01T05:07:00Z","event":"FuelScoop","Total":10}
            """
        );
        string steam = Platform.SteamScreenshotSourceTests.CreateAccount(Path.Combine(directory, "Steam"), "42");
        string firstSteam = Path.Combine(steam, "19700101050651_1.jpg");
        string secondSteam = Path.Combine(steam, "19700101050652_1.jpg");
        Platform.SteamScreenshotProcessingTests.WriteJpeg(firstSteam);
        Platform.SteamScreenshotProcessingTests.WriteJpeg(secondSteam);

        string bmpFolder = Path.Combine(directory, "bmp");
        Directory.CreateDirectory(bmpFolder);
        string highRes = Path.Combine(bmpFolder, "HighResScreenShot_2026-11-21.bmp");
        Platform.ScreenshotProcessingServiceTests.CreateBitmap(highRes, SkiaSharp.SKColors.Cyan);

        string queue = Path.Combine(directory, "queue");
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(
            ScreenshotProcessingPreferences.CreateDefaults(journals) with
            {
                Enabled = true,
                ProcessSteamScreenshots = true,
                SteamSourceFolder = steam,
                SourceFolder = bmpFolder,
                TargetFolder = Path.Combine(directory, "converted"),
                AddBanner = false,
            }
        );
        var processor = new ScreenshotProcessingService(() => 1920, (_, _) => Task.CompletedTask, queue);
        var model = new ScreenshotProcessingViewModel(
            store,
            processor,
            journals,
            steamScreenshotSource: new SteamScreenshotSource([], TimeZoneInfo.Utc)
        );

        // First live tick admits both Steam captures into the persistent queue and converts one.
        ScreenshotProcessingResult firstPass = await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        Assert.Single(firstPass.Conversions);
        Assert.Equal(1, processor.PendingCount);

        // Unchecking ProcessSteamScreenshots must pause the remaining pending Steam capture.
        model.ProcessSteamScreenshots = false;
        Assert.True(
            SrvSurvey.Core.Journal.JournalEventEnvelope.TryParse(
                """{"timestamp":"1970-01-01T05:06:55Z","event":"Screenshot","Filename":"HighResScreenShot_2026-11-21.bmp","Width":3840,"System":"Sol","Body":"Earth"}""",
                out SrvSurvey.Core.Journal.JournalEventEnvelope? highResEvent,
                out _
            )
        );
        ScreenshotProcessingResult nativePass = await model.ProcessJournalEventsAsync(
            [highResEvent!],
            "Test",
            frontierId: "F123"
        );
        Assert.EndsWith(
            "Earth (1970-01-01 050655) (HighRes).png",
            Assert.Single(nativePass.Conversions).OutputPath,
            StringComparison.Ordinal
        );
        ScreenshotProcessingResult idlePass = await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        Assert.Empty(idlePass.Conversions);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(model.TargetFolder, "Sol"), "*.png").Length);

        // Disabling the service pauses everything even if Steam is re-included.
        model.Enabled = false;
        model.ProcessSteamScreenshots = true;
        Assert.Empty((await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123")).Conversions);

        // Re-enabling both resumes the paused Steam capture.
        model.Enabled = true;
        ScreenshotProcessingResult resumed = await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        Assert.Equal(secondSteam, Assert.Single(resumed.Conversions).SourcePath);
        Assert.Equal(0, processor.PendingCount);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(model.TargetFolder, "Sol"), "*.png").Length);
    }

    [Fact]
    public void SteamSourceDescriptionReflectsEnabledAndSteamInclusionStates()
    {
        string root = Path.Combine(directory, "Steam");
        string detected = Platform.SteamScreenshotSourceTests.CreateAccount(root, "42");
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(
            ScreenshotProcessingPreferences.CreateDefaults() with
            {
                TargetFolder = Path.Combine(directory, "converted"),
            }
        );
        var model = new ScreenshotProcessingViewModel(store, steamScreenshotSource: new SteamScreenshotSource([root]));
        var changed = new List<string?>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.Contains("Enable screenshot processing above", model.SteamSourceDescription, StringComparison.Ordinal);

        model.Enabled = false;
        Assert.Empty(changed);

        model.Enabled = true;
        Assert.Contains(nameof(ScreenshotProcessingViewModel.SteamSourceDescription), changed);
        Assert.Contains("Check Process Steam screenshots", model.SteamSourceDescription, StringComparison.Ordinal);

        model.SelectSteamSourceFolder(detected);
        Assert.Contains("Steam screenshot processing is off", model.SteamSourceDescription, StringComparison.Ordinal);

        model.ProcessSteamScreenshots = true;
        Assert.Contains("matched by capture time", model.SteamSourceDescription, StringComparison.Ordinal);

        var undetected = new ScreenshotProcessingViewModel(store, steamScreenshotSource: new SteamScreenshotSource([]));
        undetected.DetectSteamSourceFolder();
        Assert.Contains(
            "No unambiguous Steam screenshot folder",
            undetected.SteamSourceDescription,
            StringComparison.Ordinal
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
