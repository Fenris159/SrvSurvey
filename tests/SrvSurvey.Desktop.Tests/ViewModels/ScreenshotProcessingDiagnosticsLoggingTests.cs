using System.Text.Json.Nodes;
using SkiaSharp;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ScreenshotProcessingDiagnosticsLoggingTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-screenshot-diag-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task LogsLoadedSettingsChangesSkippedNativeAndIdleSilence()
    {
        Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "commanders", "F5302231", "cross-platform-ui.json");
        string source = Path.Combine(directory, "bmp");
        string target = Path.Combine(directory, "png");
        Directory.CreateDirectory(source);
        var store = new ScreenshotProcessingSettingsStore(settingsPath);
        store.Save(
            ScreenshotProcessingPreferences.CreateDefaults() with
            {
                Enabled = false,
                ProcessSteamScreenshots = false,
                SourceFolder = source,
                TargetFolder = target,
            },
            useAutomaticSource: false,
            useAutomaticTarget: false
        );

        var model = new ScreenshotProcessingViewModel(
            store,
            new ScreenshotProcessingService(() => 1920, (_, _) => Task.CompletedTask)
        );
        var logs = new List<string>();
        model.UseDiagnosticLog(logs.Add);

        string loaded = Assert.Single(logs);
        Assert.Contains("[Screenshots] loaded;", loaded, StringComparison.Ordinal);
        Assert.Contains($"settings={Path.GetFullPath(settingsPath)}", loaded, StringComparison.Ordinal);
        Assert.Contains("enabled=False; processSteam=False;", loaded, StringComparison.Ordinal);
        Assert.Contains("pendingNative=0; pendingSteam=0", loaded, StringComparison.Ordinal);

        // Idle polls with no events must not spam the log.
        logs.Clear();
        await model.ProcessJournalEventsAsync([], "Blah1945", frontierId: "F5302231");
        Assert.Empty(logs);

        // A native Screenshot journal event while disabled logs a single skipped line.
        JournalEventEnvelope nativeEvent = Parse(
            """{"timestamp":"2026-11-23T19:10:00Z","event":"Screenshot","Filename":"\\ED_Pictures\\Screenshot_0001.bmp","System":"Sol","Body":"Earth"}"""
        );
        await model.ProcessJournalEventsAsync([nativeEvent], "Blah1945", frontierId: "F5302231");
        string skipped = Assert.Single(logs);
        Assert.Contains(
            "[Screenshots] skipped; sourceKind=native; reason=disabled;",
            skipped,
            StringComparison.Ordinal
        );
        Assert.Contains(@"filename=\ED_Pictures\Screenshot_0001.bmp", skipped, StringComparison.Ordinal);

        // Settings changes log the changed property and updated toggle state.
        logs.Clear();
        model.Enabled = true;
        model.SelectSourceFolder(source);
        model.ProcessSteamScreenshots = true;
        Assert.Equal(3, logs.Count);
        Assert.Contains("property=Enabled; enabled=True; processSteam=False;", logs[0], StringComparison.Ordinal);
        Assert.Contains("property=SourceFolder; enabled=True; processSteam=False;", logs[1], StringComparison.Ordinal);
        Assert.Contains(
            "property=ProcessSteamScreenshots; enabled=True; processSteam=True;",
            logs[2],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task LogsNativeAndSteamConversionsWithDistinctSourceKindsAndDeduplicatedWarnings()
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
        string steamJpeg = Path.Combine(steam, "19700101050652_1.jpg");
        Platform.SteamScreenshotProcessingTests.WriteJpeg(steamJpeg);

        string bmpFolder = Path.Combine(directory, "bmp");
        Directory.CreateDirectory(bmpFolder);
        string nativeBmp = Path.Combine(bmpFolder, "native.bmp");
        Platform.ScreenshotProcessingServiceTests.CreateBitmap(nativeBmp, SKColors.Cyan);

        string target = Path.Combine(directory, "converted");
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "ui.json"));
        store.Save(
            ScreenshotProcessingPreferences.CreateDefaults(journals) with
            {
                Enabled = true,
                ProcessSteamScreenshots = true,
                SteamSourceFolder = steam,
                SourceFolder = bmpFolder,
                TargetFolder = target,
                AddBanner = false,
                DeleteOriginal = false,
            },
            useAutomaticSource: false,
            useAutomaticTarget: false
        );

        var processor = new ScreenshotProcessingService(
            () => 1920,
            (_, _) => Task.CompletedTask,
            Path.Combine(directory, "queue")
        );
        var model = new ScreenshotProcessingViewModel(
            store,
            processor,
            journals,
            steamScreenshotSource: new SteamScreenshotSource([], TimeZoneInfo.Utc)
        );
        var logs = new List<string>();
        model.UseDiagnosticLog(logs.Add);
        logs.Clear();

        JournalEventEnvelope nativeEvent = Parse(
            """{"timestamp":"1970-01-01T05:06:51Z","event":"Screenshot","Filename":"native.bmp","System":"Sol","Body":"Earth"}"""
        );
        await model.ProcessJournalEventsAsync([nativeEvent], "Test", frontierId: "F123");
        string nativeConverted = Assert.Single(logs);
        Assert.Contains("[Screenshots] converted; sourceKind=native;", nativeConverted, StringComparison.Ordinal);
        Assert.Contains($"source={nativeBmp}", nativeConverted, StringComparison.Ordinal);
        Assert.Contains("banner=False; sourceDeleted=False;", nativeConverted, StringComparison.Ordinal);

        logs.Clear();
        await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        Assert.Equal(2, logs.Count);
        Assert.Contains("[Screenshots] steam-scan;", logs[0], StringComparison.Ordinal);
        Assert.Contains($"folder={steam}; frontierId=F123; matched=1;", logs[0], StringComparison.Ordinal);
        Assert.Contains("[Screenshots] converted; sourceKind=steam;", logs[1], StringComparison.Ordinal);
        Assert.Contains($"source={steamJpeg}", logs[1], StringComparison.Ordinal);

        // Missing source bitmap logs a standalone warning once and remains quiet on unchanged retries.
        logs.Clear();
        JournalEventEnvelope missingEvent = Parse(
            """{"timestamp":"1970-01-01T05:06:53Z","event":"Screenshot","Filename":"missing.bmp","System":"Sol","Body":"Earth"}"""
        );
        await model.ProcessJournalEventsAsync([missingEvent], "Test", frontierId: "F123");
        Assert.Contains(logs, line => line.StartsWith("[Screenshots] warning;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogsSaveFailureAndConversionWarningWithoutDuplicateWarningEntry()
    {
        Directory.CreateDirectory(directory);
        string blocker = Path.Combine(directory, "blocker");
        await File.WriteAllTextAsync(blocker, "cannot create directory here");

        var unpinned = new ScreenshotProcessingViewModel(
            new ScreenshotProcessingSettingsStore(Path.Combine(blocker, "settings.json")),
            deferOutputInitialization: true
        );
        var failureLogs = new List<string>();
        unpinned.UseDiagnosticLog(failureLogs.Add);
        failureLogs.Clear();

        unpinned.UpdateJournalDirectory(Path.Combine(directory, "journals"));
        unpinned.Enabled = true;

        Assert.Contains(
            failureLogs,
            line => line.Contains("[Screenshots] save-failed; operation=pin-output-folder;", StringComparison.Ordinal)
        );
        Assert.Contains(
            failureLogs,
            line => line.Contains("[Screenshots] save-failed; operation=save-preferences;", StringComparison.Ordinal)
        );

        var warningStore = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "ok-settings.json"));
        warningStore.Save(
            ScreenshotProcessingPreferences.CreateDefaults() with
            {
                Enabled = true,
                TargetFolder = Path.Combine(directory, "out"),
            }
        );
        var stubResult = new ScreenshotProcessingResult(
            [
                new ScreenshotConversion("source.bmp", "output.png", false, "Original retained.", "aerial.png")
                {
                    IncludedBanner = true,
                    IsSteam = false,
                },
            ],
            ["Original retained.", "Separate warning."]
        );
        var model = new ScreenshotProcessingViewModel(warningStore, new StubProcessingService(stubResult));
        var conversionLogs = new List<string>();
        model.UseDiagnosticLog(conversionLogs.Add);
        conversionLogs.Clear();

        JournalEventEnvelope shot = Parse(
            """{"timestamp":"2026-11-23T19:10:00Z","event":"Screenshot","Filename":"source.bmp"}"""
        );
        await model.ProcessJournalEventsAsync([shot], "Test");

        Assert.Equal(2, conversionLogs.Count);
        Assert.Contains("aerial=aerial.png; warning=Original retained.", conversionLogs[0], StringComparison.Ordinal);
        Assert.Equal("[Screenshots] warning; Separate warning.", conversionLogs[1]);
    }

    [Fact]
    public async Task LogsSteamScanSkippedWhenDisabledDuringInFlightScan()
    {
        Directory.CreateDirectory(directory);
        string steam = Path.Combine(directory, "Steam", "userdata", "42", "760", "remote", "359320", "screenshots");
        Directory.CreateDirectory(steam);
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "ui.json"));
        store.Save(
            ScreenshotProcessingPreferences.CreateDefaults() with
            {
                Enabled = true,
                ProcessSteamScreenshots = false,
                SteamSourceFolder = steam,
                TargetFolder = Path.Combine(directory, "out"),
            },
            useAutomaticSource: false,
            useAutomaticTarget: false
        );
        var processor = new CountingProcessingService();
        var model = new ScreenshotProcessingViewModel(store, processor);
        var logs = new List<string>();
        model.UseDiagnosticLog(logs.Add);
        logs.Clear();

        JournalEventEnvelope steamEntry = Parse(
            """{"timestamp":"2026-11-23T19:10:00Z","event":"Screenshot","Filename":"shot.jpg","System":"Sol","SteamScreenshot":true}"""
        );
        ScreenshotProcessingResult skippedBySteamToggle = await model.ProcessSteamCapturesAsync(
            [steamEntry],
            steam,
            "F123",
            hasLiveSession: true,
            CancellationToken.None
        );
        Assert.Empty(skippedBySteamToggle.Conversions);
        Assert.Equal(0, processor.CallCount);
        Assert.Contains(
            "[Screenshots] steam-scan-skipped; reason=disabled-during-scan; matched=1; enabled=True; processSteam=False",
            Assert.Single(logs),
            StringComparison.Ordinal
        );

        logs.Clear();
        model.ProcessSteamScreenshots = true;
        model.Enabled = false;
        logs.Clear();
        ScreenshotProcessingResult skippedByMainToggle = await model.ProcessSteamCapturesAsync(
            [steamEntry],
            steam,
            "F123",
            hasLiveSession: false,
            CancellationToken.None
        );
        Assert.Empty(skippedByMainToggle.Conversions);
        Assert.Equal(0, processor.CallCount);
        Assert.Contains(
            "[Screenshots] steam-scan-skipped; reason=disabled-during-scan; matched=1; enabled=False; processSteam=True",
            Assert.Single(logs),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task MainWindowViewModelLogsLoadedCommanderScreenshotSettingsToApplicationLog()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.1970-01-01T050000.01.log"),
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader"}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            """
        );
        string dataDir = Path.Combine(directory, "app-data");
        var paths = new AppDataPaths(
            Path.Combine(directory, "app-config"),
            dataDir,
            Path.Combine(directory, "app-cache"),
            []
        )
        {
            SettingsFrontierId = "F123",
        };
        new UiSettingsDocumentStore(paths.UiSettingsPath).Update(root =>
            root["Screenshots"] = new JsonObject
            {
                ["Enabled"] = true,
                ["ProcessSteamScreenshots"] = false,
                ["SourceFolder"] = Path.Combine(directory, "bmp"),
                ["TargetFolder"] = Path.Combine(directory, "converted"),
                ["UseAutomaticSourceFolder"] = false,
            }
        );
        var appLog = new ApplicationLogService(dataDir);
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            journals,
            builder => builder.WithAppDataPaths(paths).WithApplicationLogService(appLog)
        );

        Assert.Contains(
            appLog.Entries,
            entry =>
                entry.Contains("[Screenshots] loaded;", StringComparison.Ordinal)
                && entry.Contains(paths.UiSettingsPath, StringComparison.Ordinal)
                && entry.Contains("processSteam=False", StringComparison.Ordinal)
        );
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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class StubProcessingService(ScreenshotProcessingResult result) : IScreenshotProcessingService
    {
        public Task<ScreenshotProcessingResult> ProcessAsync(
            IReadOnlyList<JournalEventEnvelope> journalEvents,
            ScreenshotProcessingPreferences preferences,
            string? commanderName,
            IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
            ScreenshotNavigationContext? navigationContext = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(result);
    }

    private sealed class CountingProcessingService : IScreenshotProcessingService
    {
        public int CallCount { get; private set; }

        public Task<ScreenshotProcessingResult> ProcessAsync(
            IReadOnlyList<JournalEventEnvelope> journalEvents,
            ScreenshotProcessingPreferences preferences,
            string? commanderName,
            IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
            ScreenshotNavigationContext? navigationContext = null,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            return Task.FromResult(ScreenshotProcessingResult.Empty);
        }
    }
}
