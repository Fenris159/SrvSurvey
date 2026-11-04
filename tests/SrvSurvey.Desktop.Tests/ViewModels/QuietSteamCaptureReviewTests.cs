using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MainWindowSteamScreenshotTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task QuietLiveCapturesWorkWithNestedGamescopeAndVerifiedContinuations(
        bool continuation,
        int switchableWindows
    )
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        string first = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        await File.WriteAllTextAsync(
            first,
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader","part":1,"Odyssey":true}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0],"Body":"Earth","BodyType":"Planet"}
            """ + "\n"
        );
        File.SetLastWriteTimeUtc(first, new DateTime(1970, 1, 1, 5, 0, 0, DateTimeKind.Utc));
        if (continuation)
        {
            string second = Path.Combine(journals, "Journal.1970-01-01T050000.02.log");
            await File.WriteAllTextAsync(
                second,
                """
                {"timestamp":"1970-01-01T05:01:00Z","event":"Fileheader","part":2,"Odyssey":true}
                {"timestamp":"1970-01-01T05:01:01Z","event":"FuelScoop","Total":10}
                """ + "\n"
            );
            File.SetLastWriteTimeUtc(second, new DateTime(1970, 1, 1, 5, 1, 0, DateTimeKind.Utc));
        }
        var paths = new AppDataPaths(
            Path.Combine(directory, "config"),
            Path.Combine(directory, "data"),
            Path.Combine(directory, "cache"),
            []
        );
        string steam = Platform.SteamScreenshotSourceTests.CreateAccount(Path.Combine(directory, "Steam"), "42");
        string jpeg = Path.Combine(steam, "capture.jpg");
        Platform.SteamScreenshotProcessingTests.WriteJpeg(jpeg);
        await File.WriteAllTextAsync(
            Platform.SteamScreenshotSourceTests.IndexPath(steam),
            "\"screenshots\" { \"359320\" { \"0\" { \"filename\" \"359320/screenshots/capture.jpg\" \"creation\" \"18413\" } } }"
        );
        new UiSettingsDocumentStore(paths.UiSettingsPath).Update(root =>
            root["Screenshots"] = new JsonObject
            {
                ["Enabled"] = true,
                ["SteamSourceFolder"] = steam,
                ["AddBanner"] = false,
                ["DeleteOriginal"] = true,
                ["TargetFolder"] = Path.Combine(directory, "converted"),
            }
        );
        var processor = new ScreenshotProcessingService(
            () => 320,
            (_, _) => Task.CompletedTask,
            paths.ScreenshotQueueDirectory
        );
        await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            journals,
            builder =>
                builder
                    .WithAppDataPaths(paths)
                    .WithTargetFrontierId("F123")
                    .WithScreenshotTimeProvider(new QuietCaptureClock())
                    .WithGameWindowSwitcher(new QuietWindowSwitcher(switchableWindows))
                    .WithScreenshotProcessingService(processor)
        );
        await model.RefreshAsync();
        model.ScreenshotProcessing.ProcessSteamScreenshots = true;
        await model.RefreshAsync();
        string png = Path.Combine(directory, "converted", "Sol", "Earth (1970-01-01 050653).png");
        Assert.True(File.Exists(png), model.ScreenshotProcessing.StatusMessage);
        Assert.False(File.Exists(jpeg));
    }

    private sealed class QuietCaptureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(1970, 1, 1, 7, 0, 0, TimeSpan.Zero);
    }

    private sealed class QuietWindowSwitcher(int count) : IGameWindowSwitcher
    {
        public int GetAvailableWindowCount() => count;

        public bool TryActivateCurrent() => false;

        public bool TryActivateNext() => false;

        public void Dispose() { }
    }
}
