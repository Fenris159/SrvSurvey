using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MainWindowJourneyJournalTests : IDisposable
{
    private const string TestFrontierId = "F987654321098765432109876543210";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-journal-folders-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task LateConvertedScreenshotAppearsInTheOriginalJourneySystemAndPortableReport()
    {
        string profile = Path.Combine(directory, "proton", "drive_c", "users", "steamuser");
        string journals = Path.Combine(profile, "Saved Games", "Frontier Developments", "Elite Dangerous");
        string pictures = Path.Combine(profile, "Pictures", "Frontier Developments", "Elite Dangerous");
        Directory.CreateDirectory(journals);
        Directory.CreateDirectory(pictures);
        string journal = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0],"Docked":false}
            """ + "\n"
        );
        var paths = new AppDataPaths(
            Path.Combine(directory, "config"),
            Path.Combine(directory, "data"),
            Path.Combine(directory, "cache"),
            []
        );
        new UiSettingsDocumentStore(paths.UiSettingsPath).Update(root =>
            root["Screenshots"] = new JsonObject { ["Enabled"] = true, ["AddBanner"] = false }
        );
        var processor = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask);
        await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            journals,
            builder => builder.WithAppDataPaths(paths).WithScreenshotProcessingService(processor)
        );
        await model.RefreshAsync();
        model.Notifications.Enabled = true;
        model.Notifications.ShowScreenshot = true;
        await model.Journey.StartNewJourneyAsync();
        model.Journey.NewJourneyName = "Screenshot expedition";
        await model.Journey.BeginJourneyAsync();
        Assert.True(model.Journey.HasActiveJourney, model.Journey.StartStatus);
        await File.AppendAllTextAsync(
            journal,
            """{"timestamp":"1970-01-01T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\late.bmp","System":"Sol","Body":"Earth"}"""
                + "\n"
        );
        await model.RefreshAsync();
        Assert.Empty(model.Journey.ScreenshotFiles);
        await File.AppendAllTextAsync(
            journal,
            """{"timestamp":"1970-01-01T05:07:00Z","event":"FSDJump","StarSystem":"Lave","SystemAddress":43,"StarPos":[10,0,0]}"""
                + "\n"
        );
        await model.RefreshAsync();
        model.Journey.SelectedSystem = model.Journey.VisitedSystems.Single(item => item.Name == "Sol");
        string anotherSource = Path.Combine(directory, "another-source");
        Directory.CreateDirectory(anotherSource);
        model.ScreenshotProcessing.SourceFolder = anotherSource;
        model.ScreenshotProcessing.AddBanner = true;
        model.ScreenshotProcessing.DeleteOriginal = true;
        string original = Path.Combine(pictures, "late.bmp");
        await File.WriteAllBytesAsync(
            original,
            Convert.FromBase64String("Qk06AAAAAAAAADYAAAAoAAAAAQAAAAEAAAABABgAAAAAAAQAAAATCwAAEwsAAAAAAAAAAAAA/wAAAA==")
        );
        var visible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(model.Journey.ScreenshotFiles) && model.Journey.ScreenshotFiles.Count == 1)
            {
                visible.TrySetResult();
            }
        }
        model.Journey.PropertyChanged += OnChanged;
        using var cancellation = new CancellationTokenSource();
        Task monitoring = model.MonitorAsync(TimeSpan.Zero, cancellation.Token);
        try
        {
            try
            {
                await visible.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            { /* The assertion below reports the missing Journey image. */
            }
            string expected = Path.Combine(pictures, "converted", "Sol", "Earth (1970-01-01 050653).png");
            Assert.Equal([expected], model.Journey.ScreenshotFiles);
        }
        finally
        {
            await cancellation.CancelAsync();
            await monitoring;
            model.Journey.PropertyChanged -= OnChanged;
        }
        Assert.Contains(
            model.Notifications.Messages,
            message => message.Text == "Saved 'Earth (1970-01-01 050653).png' with no banner"
        );
        string png = Assert.Single(model.Journey.ScreenshotFiles);
        using (var image = SkiaSharp.SKBitmap.Decode(png))
        {
            Assert.NotNull(image);
            Assert.Equal(SkiaSharp.SKColors.Blue, image.GetPixel(0, 0));
        }
        Assert.True(File.Exists(original));
        Assert.Equal(1, model.Journey.VisitedSystems.Single(item => item.Name == "Sol").Visit.Counts.Screenshots);
        Assert.Equal(0, model.Journey.VisitedSystems.Single(item => item.Name == "Lave").Visit.Counts.Screenshots);
        await model.Journey.ConfirmConcludeAsync();
        JourneyHtmlExport report = Assert.IsType<JourneyHtmlExport>(await model.Journey.CreateHtmlReportAsync());
        Assert.Equal(png, Assert.Single(report.Images).SourcePath);
        Assert.Contains("images/42/0001-preview.png", report.Html, StringComparison.Ordinal);
        string folder = await JourneyReportBundleExporter.SaveAsync(report, Path.Combine(directory, "exports"));
        Assert.Equal(
            await File.ReadAllBytesAsync(png),
            await File.ReadAllBytesAsync(Path.Combine(folder, "images", "42", "0001.png"))
        );
        Assert.True(File.Exists(Path.Combine(folder, "images", "42", "0001-preview.png")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task JourneyStartsFromTheCurrentCommandersJournalsRegardlessOfFolderOrder(
        bool activeFolderFirst,
        bool previousVisit
    )
    {
        string steam = Path.Combine(directory, "steam-journals");
        string epic = Path.Combine(directory, "epic-journals");
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(epic);
        await File.WriteAllTextAsync(
            Path.Combine(steam, "Journal.2026-10-30T160000.01.log"),
            """
            {"timestamp":"2026-10-30T16:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-10-30T16:00:01Z","event":"Commander","Name":"Other","FID":"F999"}
            {"timestamp":"2026-10-30T16:00:02Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-10-30T16:00:03Z","event":"Screenshot"}
            """ + "\n"
        );
        string currentJournal = Path.Combine(epic, "Journal.2026-10-30T170000.01.log");
        await File.WriteAllTextAsync(
            currentJournal,
            $$"""
            {"timestamp":"2026-10-30T17:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-10-30T17:00:01Z","event":"Commander","Name":"David","FID":"{{TestFrontierId}}"}
            {"timestamp":"2026-10-30T17:00:02Z","event":"LoadGame","Commander":"David","FID":"{{TestFrontierId}}","Ship":"CobraMkIII"}
            {"timestamp":"2026-10-30T17:00:03Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-10-30T17:00:04Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0],"Docked":false}
            {"timestamp":"2026-10-30T17:00:05Z","event":"Screenshot"}
            """ + "\n"
        );
        var paths = new AppDataPaths(
            Path.Combine(directory, "config"),
            Path.Combine(directory, "data"),
            Path.Combine(directory, "cache"),
            []
        );
        new JournalSettingsStore(paths.SharedUiSettingsPath).Save(
            activeFolderFirst ? new JournalPreferences(epic, [steam]) : new JournalPreferences(steam, [epic])
        );
        await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths).WithTargetFrontierId(TestFrontierId)
        );
        await model.RefreshAsync();
        Assert.Equal(currentJournal, model.CurrentJournalPath);
        Assert.Equal("Sol", model.Journey.CurrentStartSystem);
        await model.Journey.StartNewJourneyAsync();
        model.Journey.NewJourneyName = "Across the black";
        if (previousVisit)
        {
            model.Journey.UsePreviousStart = true;
            await model.Journey.SelectStartingSystemAsync("Sol", 42);
            Assert.Single(model.Journey.StartVisits);
        }
        Assert.True(model.Journey.BeginJourneyCommand.CanExecute(null));
        await model.Journey.BeginJourneyAsync();
        Assert.True(model.Journey.HasActiveJourney, model.Journey.StartStatus);
        Assert.Equal(TestFrontierId, model.Journey.SelectedJourney!.Document.FrontierId);
        Assert.Equal(1, Assert.Single(model.Journey.VisitedSystems).Visit.Counts.Screenshots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshStartupWithNullOrAbsentSelectedReferenceCanStartAJourney(bool omitActiveReference)
    {
        string journals = Path.Combine(directory, "journals");
        string data = Path.Combine(directory, "data");
        Directory.CreateDirectory(journals);
        Directory.CreateDirectory(data);
        var selectedProfile = new JsonObject
        {
            ["fid"] = TestFrontierId,
            ["commander"] = "Current",
            ["isOdyssey"] = true,
        };
        if (!omitActiveReference)
        {
            selectedProfile["activeJourney"] = null;
        }
        await File.WriteAllTextAsync(Path.Combine(data, TestFrontierId + "-live.json"), selectedProfile.ToJsonString());
        await File.WriteAllTextAsync(
            Path.Combine(data, "F999-live.json"),
            """
            {"fid":"F999","commander":"Other","isOdyssey":true}
            """
        );
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.2026-10-30T160000.01.log"),
            """
            {"timestamp":"2026-10-30T16:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-10-30T16:00:01Z","event":"Commander","Name":"Other","FID":"F999"}
            {"timestamp":"2026-10-30T16:00:02Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """ + "\n"
        );
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.2026-10-30T170000.01.log"),
            $$"""
            {"timestamp":"2026-10-30T17:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-10-30T17:00:01Z","event":"Commander","Name":"Current","FID":"{{TestFrontierId}}"}
            {"timestamp":"2026-10-30T17:00:02Z","event":"LoadGame","Commander":"Current","FID":"{{TestFrontierId}}","Ship":"CobraMkIII"}
            {"timestamp":"2026-10-30T17:00:03Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """ + "\n"
        );
        var paths = new AppDataPaths(Path.Combine(directory, "config"), data, Path.Combine(directory, "cache"), []);
        new JournalSettingsStore(paths.SharedUiSettingsPath).Save(new(journals));
        await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths).WithTargetFrontierId(TestFrontierId)
        );
        await model.RefreshAsync();
        Assert.Empty(model.Journey.Journeys);
        Assert.False(model.Journey.HasActiveJourney);
        await model.Journey.StartNewJourneyAsync();
        model.Journey.NewJourneyName = "Fresh start";
        await model.Journey.BeginJourneyAsync();
        Assert.True(model.Journey.HasActiveJourney, model.Journey.StartStatus);
        Assert.Equal("Fresh start", model.Journey.JourneyName);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
