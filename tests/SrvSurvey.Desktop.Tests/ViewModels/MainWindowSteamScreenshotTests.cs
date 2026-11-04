using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MainWindowSteamScreenshotTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-journey-steam-{Guid.NewGuid():N}");

    [Fact]
    public async Task SteamCaptureWithoutAScreenshotJournalEventAppearsInItsOriginalJourneySystemAndPortableExport()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        string journal = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0],"Docked":false,"Body":"Earth","BodyType":"Planet"}
            """ + "\n"
        );
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
                ["SourceFolder"] = Path.Combine(directory, "missing-bmp-folder"),
                ["UseAutomaticSourceFolder"] = false,
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
            builder => builder.WithAppDataPaths(paths).WithScreenshotProcessingService(processor)
        );
        await model.RefreshAsync();
        await model.Journey.StartNewJourneyAsync();
        model.Journey.NewJourneyName = "Steam expedition";
        await model.Journey.BeginJourneyAsync();
        Assert.True(model.Journey.HasActiveJourney, model.Journey.StartStatus);
        await File.AppendAllTextAsync(
            journal,
            """{"timestamp":"1970-01-01T05:07:00Z","event":"FSDJump","StarSystem":"Lave","SystemAddress":43,"StarPos":[10,0,0]}"""
                + "\n"
        );
        await model.RefreshAsync();
        model.Journey.SelectedSystem = model.Journey.VisitedSystems.Single(item => item.Name == "Sol");
        Assert.Empty(model.Journey.ScreenshotFiles);
        Assert.True(File.Exists(jpeg));

        model.ScreenshotProcessing.ProcessSteamScreenshots = true;
        await model.RefreshAsync();

        string png = Assert.Single(model.Journey.ScreenshotFiles);
        Assert.Equal(
            Path.Combine(model.ScreenshotProcessing.TargetFolder, "Sol", "Earth (1970-01-01 050653).png"),
            png
        );
        Assert.False(File.Exists(jpeg));
        model.Journey.SelectedSystem = model.Journey.VisitedSystems.Single(item => item.Name == "Lave");
        Assert.False(Directory.Exists(Path.Combine(model.ScreenshotProcessing.TargetFolder, "Lave")));
        await model.Journey.ConfirmConcludeAsync();
        JourneyHtmlExport report = Assert.IsType<JourneyHtmlExport>(await model.Journey.CreateHtmlReportAsync());
        Assert.Equal(png, Assert.Single(report.Images).SourcePath);
        string exported = await JourneyReportBundleExporter.SaveAsync(report, Path.Combine(directory, "exports"));
        Assert.Equal(
            await File.ReadAllBytesAsync(png),
            await File.ReadAllBytesAsync(Path.Combine(exported, "images", "42", "0001.png"))
        );
        Assert.True(File.Exists(Path.Combine(exported, "images", "42", "0001-preview.png")));
        Assert.Contains("images/42/0001-preview.png", report.Html, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
