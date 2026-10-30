using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class JourneysPresentationTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-presentation-{Guid.NewGuid():N}"
    );

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JourneyScreenshotsUseTheCurrentConverterDestination(bool hasLegacyDestination)
    {
        using MainWindowViewModel main = await CreateModelAsync();
        Assert.Equal("Sagittarius A*", main.Journey.SelectedSystemName);
        if (hasLegacyDestination)
        {
            string legacyRoot = Path.Combine(directory, "old-screenshots");
            string legacyFolder = Path.Combine(legacyRoot, SystemNoteStore.MakeSafeFileName("Sagittarius A*"));
            Directory.CreateDirectory(legacyFolder);
            await File.WriteAllTextAsync(Path.Combine(legacyFolder, "old.png"), "old image");
            await File.WriteAllTextAsync(
                Path.Combine(main.AppDataPaths.OverlaySettingsDirectory, "settings.json"),
                System.Text.Json.JsonSerializer.Serialize(new { screenshotTargetFolder = legacyRoot })
            );
        }
        string target = Path.Combine(directory, "current-screenshots");
        string screenshot = await CreateScreenshotAsync(target);
        main.ScreenshotProcessing.TargetFolder = target;
        await main.Journey.RefreshAsync();
        Assert.Equal([screenshot], main.Journey.ScreenshotFiles);
        Assert.True(main.Journey.HasImagesDirectory);
        Assert.True(main.Journey.HasScreenshots);
        var view = new JourneysView { DataContext = main.Journey };
        var window = new Window
        {
            Content = view,
            Width = 900,
            Height = 800,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ListBox screenshots = view.FindControl<ListBox>("ScreenshotListBox")!;
            Assert.True(screenshots.IsEffectivelyVisible);
            Assert.Equal([screenshot], screenshots.Items.Cast<string>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AStartingArrivalCanBeChosenInTheRenderedJourneyForm()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.2026-07-01T000000.01.log"),
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:07:00Z","event":"FSDJump","StarSystem":"Alpha","SystemAddress":43,"StarPos":[1,0,0]}
            {"timestamp":"2026-07-02T00:05:00Z","event":"CarrierJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-02T00:07:00Z","event":"FSDJump","StarSystem":"Alpha","SystemAddress":43,"StarPos":[1,0,0]}
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        using MainWindowViewModel main = await CreateModelAsync(journals);
        await main.Journey.StartNewJourneyAsync();
        main.Journey.NewJourneyName = "The earlier visit";
        await main.Journey.FindStartAsync();
        var view = new JourneysView { DataContext = main.Journey };
        var window = new Window
        {
            Content = view,
            Width = 1100,
            Height = 1000,
        };
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ListBox arrivals = view.FindControl<ListBox>("JourneyStartVisitList")!;
            Button begin = view.FindControl<Button>("BeginJourneyButton")!;
            Assert.True(arrivals.IsEffectivelyVisible);
            Assert.Equal(3, arrivals.ItemCount);
            Assert.Equal(-1, arrivals.SelectedIndex);
            Assert.False(begin.IsEffectivelyEnabled);
            arrivals.SelectedIndex = 2;
            Assert.Equal("2026-07-01 00:05:00 UTC", main.Journey.SelectedStartVisit!.Arrived);
            Assert.True(begin.IsEffectivelyEnabled);
            begin.BringIntoView();
            using (WriteableBitmap? selected = window.CaptureRenderedFrame())
            {
                Assert.NotNull(selected);
                string? output = Environment.GetEnvironmentVariable("SRVSURVEY_START_VISITS_RENDER_OUTPUT");
                if (output is not null)
                {
                    selected.Save(output, PngBitmapEncoderOptions.Default);
                }
            }
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Journey.PropertyChanged += (_, args) =>
            {
                if (
                    args.PropertyName == nameof(main.Journey.IsBusy)
                    && !main.Journey.IsBusy
                    && main.Journey.HasActiveJourney
                )
                {
                    completed.TrySetResult();
                }
            };
            Point center = begin
                .TranslatePoint(new Point(begin.Bounds.Width / 2, begin.Bounds.Height / 2), window)!
                .Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(main.Journey.IsCreating);
            Assert.Equal(5, main.Journey.VisitedSystems.Count);
            Assert.Equal(1, main.Journey.VisitedSystems[^1].Visit.Arrived.Day);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ChangingTheScreenshotDestinationRefreshesFilesWithoutDiscardingJourneyEdits()
    {
        using MainWindowViewModel main = await CreateModelAsync();
        string firstTarget = Path.Combine(directory, "first-screenshots");
        await CreateScreenshotAsync(firstTarget);
        main.ScreenshotProcessing.TargetFolder = firstTarget;
        await main.Journey.RefreshAsync();
        main.Journey.SelectedSystemNotes = "Unsaved system notes";
        main.Journey.JourneyDescription = "Unsaved expedition description";
        string nextTarget = Path.Combine(directory, "next-screenshots");
        string nextScreenshot = await CreateScreenshotAsync(nextTarget);
        main.ScreenshotProcessing.TargetFolder = nextTarget;
        Assert.Equal([nextScreenshot], main.Journey.ScreenshotFiles);
        Assert.Equal("Unsaved system notes", main.Journey.SelectedSystemNotes);
        Assert.Equal("Unsaved expedition description", main.Journey.JourneyDescription);
        Assert.True(main.Journey.IsDirty);
        string? openedDirectory = null;
        main.Journey.SetDirectoryLauncher(folder =>
        {
            openedDirectory = folder.FullName;
            return Task.FromResult(true);
        });
        main.Journey.OpenImagesCommand.Execute(null);
        Assert.Equal(Path.GetDirectoryName(nextScreenshot), openedDirectory);
        main.ScreenshotProcessing.TargetFolder = Path.Combine(directory, "missing-screenshots");
        Assert.Empty(main.Journey.ScreenshotFiles);
        Assert.False(main.Journey.HasImagesDirectory);
        Assert.False(main.Journey.OpenImagesCommand.CanExecute(null));
        main.ScreenshotProcessing.TargetFolder = string.Empty;
        Assert.Empty(main.Journey.ScreenshotFiles);
        main.ScreenshotProcessing.TargetFolder = "relative-screenshots";
        Assert.Empty(main.Journey.ScreenshotFiles);
        Assert.Equal("Unsaved system notes", main.Journey.SelectedSystemNotes);
    }

    private static async Task<string> CreateScreenshotAsync(string target)
    {
        string folder = ScreenshotProcessingService.GetSystemFolderPath(target, "Sagittarius A*");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Sagittarius A (2026-10-29 140000).png");
        using var bitmap = new SKBitmap(800, 450);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, data.ToArray());
        return path;
    }

    [AvaloniaFact]
    public async Task AConvertedScreenshotAppearsWithoutReselectingTheSystemOrDiscardingNotes()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        string journalPath = Path.Combine(journals, "Journal.2026-10-29T120000.01.log");
        await File.WriteAllTextAsync(
            journalPath,
            """
            {"timestamp":"2026-10-29T12:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-10-29T12:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-10-29T12:00:02Z","event":"Location","StarSystem":"Sagittarius A*","SystemAddress":44,"StarPos":[10000,80,12000]}

            """
        );
        string target = Path.Combine(directory, "converted-screenshots");
        var processor = new SavingScreenshotProcessor(() => CreateScreenshotAsync(target));
        using MainWindowViewModel main = await CreateModelAsync(journals, processor);
        main.ScreenshotProcessing.TargetFolder = target;
        main.ScreenshotProcessing.Enabled = true;
        await main.RefreshAsync();
        Assert.Empty(main.Journey.ScreenshotFiles);
        JourneySystemItemViewModel? selected = main.Journey.SelectedSystem;
        main.Journey.SelectedSystemNotes = "Keep these unsaved notes";
        await File.AppendAllTextAsync(
            journalPath,
            """
            {"timestamp":"2026-10-29T12:00:03Z","event":"Screenshot","Filename":"new.bmp","System":"Sagittarius A*","Body":"Sagittarius A*"}

            """
        );
        await main.RefreshAsync();
        Assert.Single(main.Journey.ScreenshotFiles);
        Assert.True(File.Exists(main.Journey.ScreenshotFiles[0]));
        Assert.Same(selected, main.Journey.SelectedSystem);
        Assert.Equal("Keep these unsaved notes", main.Journey.SelectedSystemNotes);
        Assert.True(main.Journey.IsDirty);
    }

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(900)]
    [InlineData(1280)]
    public async Task EmbeddedFlightLogRendersAcrossWidthsAndAllApplicationThemes(int width)
    {
        using MainWindowViewModel main = await CreateModelAsync();
        var view = new JourneysView { DataContext = main.Journey };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1100,
        };
        try
        {
            window.Show();
            foreach (RavenThemeDefinition theme in RavenThemeCatalog.All)
            {
                ApplyTheme(window, theme);
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Contains(
                    view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "Journeys"
                );
                Assert.True(view.FindControl<Button>("ExportJourneyHtmlButton")!.IsEffectivelyVisible);
                Assert.Equal(3, view.FindControl<ListBox>("JourneyVisitList")!.ItemCount);
                Assert.Equal(1, view.FindControl<ListBox>("JourneyLibraryList")!.ItemCount);
                Assert.Contains(
                    view.FindControl<ListBox>("JourneyVisitList")!.GetVisualDescendants().OfType<TextBlock>(),
                    text =>
                        text.IsEffectivelyVisible
                        && text.Text is not null
                        && text.Text.Contains("2 screenshots", StringComparison.Ordinal)
                        && text.Text.Contains("1 note", StringComparison.Ordinal)
                        && text.Text.Contains("2 landings", StringComparison.Ordinal)
                );
                Grid readout = view.FindControl<Grid>("JourneyReadout")!;
                Assert.InRange(readout.Bounds.Width, 1, width);
                Assert.Equal(width < 900 ? 1 : 0, Grid.GetRow(readout));
                string? output = Environment.GetEnvironmentVariable("SRVSURVEY_JOURNEYS_RENDER_OUTPUT");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    frame.Save(
                        Path.Combine(output, $"journeys-{theme.Key}-{width}.png"),
                        PngBitmapEncoderOptions.Default
                    );
                }
            }
            TabControl tabs = view.FindControl<TabControl>("JourneyDetailTabs")!;
            tabs.SelectedIndex = 1;
            using (WriteableBitmap? statistics = window.CaptureRenderedFrame())
            {
                Assert.NotNull(statistics);
                Assert.Contains(
                    view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "Biology base value"
                );
            }
            tabs.SelectedIndex = 2;
            using (WriteableBitmap? details = window.CaptureRenderedFrame())
            {
                Assert.NotNull(details);
            }

            CheckBox utc = view.FindControl<CheckBox>("GalacticTimeCheckBox")!;
            utc.IsChecked = true;
            await view.SavePreferencesAsync();
            Assert.True(main.Journey.UseGalacticTime);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TravelShortcutNavigatesToJourneysAndOpensTheNavigationGroup()
    {
        using MainWindowViewModel main = await CreateModelAsync();
        main.SelectedNavigation = main.NavigationItems.Single(item => item.Key == "travel");
        main.Journey.OpenWorkspaceCommand.Execute(null);
        Assert.True(main.IsJourneysSelected);
        Assert.False(main.IsTravelSelected);
        Assert.True(main.IsNavigationNavigationExpanded);
        Assert.Contains(
            main.NavigationWorkspaceItems,
            item => item.Key == "journeys" && item.Label == "Journeys" && !item.HasOverlaySettings
        );
    }

    [AvaloniaFact]
    public async Task ArchiveAndDeleteControlsKeepHistoryOutOfTheMainList()
    {
        using MainWindowViewModel main = await CreateModelAsync();
        var view = new JourneysView { DataContext = main.Journey };
        var window = new Window
        {
            Content = view,
            Width = 1100,
            Height = 1000,
        };
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            await main.Journey.ArchiveSelectedAsync();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            Assert.Equal(0, view.FindControl<ListBox>("JourneyLibraryList")!.ItemCount);
            Button archive = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, main.Journey.ArchiveLabel));
            archive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            Assert.True(main.Journey.ShowArchived);
            Assert.Equal(1, view.FindControl<ListBox>("JourneyLibraryList")!.ItemCount);
            main.Journey.RequestDeleteCommand.Execute(null);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == "Delete this journey?"
            );
            main.Journey.CancelDeleteCommand.Execute(null);
            await main.Journey.ArchiveSelectedAsync();
            Button current = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, main.Journey.JourneysLabel));
            current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(main.Journey.ShowArchived);
            string journeyPath = main.Journey.SelectedJourney!.Document.FilePath;
            main.Journey.RequestDeleteCommand.Execute(null);
            using (WriteableBitmap? confirmation = window.CaptureRenderedFrame())
            {
                Assert.NotNull(confirmation);
            }
            Button confirm = view.FindControl<Button>("ConfirmDeleteJourneyButton")!;
            Assert.Same(main.Journey.ConfirmDeleteCommand, confirm.Command);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Journey.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(main.Journey.IsBusy) && !main.Journey.IsBusy)
                {
                    completed.TrySetResult();
                }
            };
            Point center = confirm
                .TranslatePoint(new Point(confirm.Bounds.Width / 2, confirm.Bounds.Height / 2), window)!
                .Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using (WriteableBitmap? deleted = window.CaptureRenderedFrame())
            {
                Assert.NotNull(deleted);
            }
            Assert.False(File.Exists(journeyPath));
            Assert.Equal(0, view.FindControl<ListBox>("JourneyLibraryList")!.ItemCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HtmlExportWritesAPortableFileAndHandlesCancelAndStorageErrors()
    {
        using MainWindowViewModel main = await CreateModelAsync();
        var view = new JourneysView { DataContext = main.Journey };
        await view.ExportHtmlAsync(options =>
        {
            Assert.False(options.AllowMultiple);
            return Task.FromResult<JourneyReportDestination?>(new(directory));
        });
        string path = Directory.GetFiles(directory, "index.html", SearchOption.AllDirectories).Single();
        Assert.Contains("Expedition report", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!, "images")));
        Assert.Contains("report exported", main.Journey.StatusMessage, StringComparison.Ordinal);
        await view.ExportHtmlAsync(_ => Task.FromResult<JourneyReportDestination?>(null));
        await view.ExportHtmlAsync(_ => throw new IOException("Storage unavailable"));
        Assert.Contains("could not be saved", main.Journey.StatusMessage, StringComparison.Ordinal);
        main.Journey.JourneyName = "Unsaved";
        await view.ExportHtmlAsync(_ =>
            throw new InvalidOperationException("Picker should not open for unsaved edits")
        );
        Assert.True(main.Journey.IsDirty);
    }

    [AvaloniaFact]
    public async Task HtmlExportCapturesSystemImagesAndBundlesThemWithRelativeLinks()
    {
        using MainWindowViewModel main = await CreateModelAsync();
        string sourceRoot = Path.Combine(directory, "originals");
        string original = await CreateScreenshotAsync(sourceRoot);
        main.ScreenshotProcessing.TargetFolder = sourceRoot;
        JourneyDocument document = main.Journey.SelectedJourney!.Document;
        await new JourneyStore(main.AppDataPaths.DataDirectory).SaveAsync(
            document with
            {
                VisitedSystems =
                [
                    .. document.VisitedSystems,
                    document.VisitedSystems[^1] with
                    {
                        Arrived = document.StartTime.AddHours(3),
                    },
                ],
            }
        );
        await main.Journey.RefreshAsync();
        JourneyHtmlExport? report = await main.Journey.CreateHtmlReportAsync();
        Assert.NotNull(report);
        JourneyReportAsset image = Assert.Single(report.Images);
        Assert.Equal(original, image.SourcePath);
        Assert.DoesNotContain(sourceRoot, report.Html, StringComparison.Ordinal);
        Assert.Contains(image.Image.ImagePath, report.Html, StringComparison.Ordinal);
        Assert.Equal(2, report.Html.Split("class=\"screenshot-tile\"", StringSplitOptions.None).Length - 1);
        var view = new JourneysView { DataContext = main.Journey };
        string exports = Path.Combine(directory, "portable-exports");
        await view.ExportHtmlAsync(_ => Task.FromResult<JourneyReportDestination?>(new(exports)));
        string folder = Assert.Single(Directory.GetDirectories(exports));
        Assert.True(File.Exists(Path.Combine(folder, "index.html")));
        Assert.True(File.Exists(Path.Combine(folder, image.Image.ImagePath)));
        Assert.True(File.Exists(Path.Combine(folder, image.Image.PreviewPath)));
        Assert.True(File.Exists(original));
        string blocked = Path.Combine(directory, "blocked-export");
        await File.WriteAllTextAsync(blocked, "A file occupies this destination");
        await view.ExportHtmlAsync(_ => Task.FromResult<JourneyReportDestination?>(new(blocked)));
        Assert.Contains("could not be saved", main.Journey.StatusMessage, StringComparison.Ordinal);
        Assert.False(main.Journey.IsBusy);
        Assert.True(main.Journey.CanExportReport);
        Assert.True(File.Exists(Path.Combine(folder, "index.html")));
    }

    [AvaloniaFact]
    public async Task JourneyTopmostPreferenceAppliesOnlyWhileItsWorkspaceIsSelected()
    {
        using MainWindowViewModel main = await CreateModelAsync();
        var window = new MainWindow { DataContext = main };
        try
        {
            window.Show();
            main.SelectedNavigation = main.NavigationItems.Single(item => item.Key == "journeys");
            await main.Journey.SetPreferencesAsync(true, false);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.True(window.Topmost);
            main.SelectedNavigation = main.NavigationItems.Single(item => item.Key == "travel");
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.False(window.Topmost);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private async Task<MainWindowViewModel> CreateModelAsync(
        string? journals = null,
        IScreenshotProcessingService? processor = null
    )
    {
        MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            journals,
            builder =>
            {
                builder.WithAppDataPaths(
                    new(
                        Path.Combine(directory, "config"),
                        Path.Combine(directory, "data"),
                        Path.Combine(directory, "cache"),
                        []
                    )
                );
                if (processor is not null)
                {
                    builder.WithScreenshotProcessingService(processor);
                }
            }
        );
        var store = new JourneyStore(main.AppDataPaths.DataDirectory);
        DateTimeOffset at = new(2026, 10, 29, 12, 0, 15, TimeSpan.Zero);
        JourneyDocument journey = await store.CreateAsync(
            new(
                "F123",
                "Drew",
                "The long way home",
                "A survey through the core, with time to explore.",
                "Journal.test.log",
                at
            )
        );
        JourneySystemVisit Visit(string name, long address, int index) =>
            new(
                new(name, address, new(index * 5000, index * 40, index * 6000)),
                at.AddHours(index),
                index == 2 ? null : at.AddHours(index + 1),
                new(12, 4, 2, 3, 2, 14, 2, 1, 2_000_000_000, 13_000_000, 2),
                new Dictionary<string, int> { [name + " 1"] = 2 },
                new HashSet<long> { 123 },
                null,
                null,
                new Dictionary<string, int> { ["Biology"] = 3 },
                new Dictionary<string, int> { ["Biological"] = 3 },
                null
            );
        journey = journey with
        {
            EndTime = at.AddHours(3),
            VisitedSystems = [Visit("Sol", 42, 0), Visit("Stuemeae FG-Y d7561", 43, 1), Visit("Sagittarius A*", 44, 2)],
        };
        await store.SaveAsync(journey);
        await main.Journey.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        return main;
    }

    private sealed class SavingScreenshotProcessor(Func<Task<string>> saveScreenshot) : IScreenshotProcessingService
    {
        public async Task<ScreenshotProcessingResult> ProcessAsync(
            IReadOnlyList<JournalEventEnvelope> journalEvents,
            ScreenshotProcessingPreferences preferences,
            string? commanderName,
            IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>? guardianContexts = null,
            ScreenshotNavigationContext? navigationContext = null,
            CancellationToken cancellationToken = default
        )
        {
            if (!journalEvents.Any(entry => entry.EventName == "Screenshot"))
            {
                return ScreenshotProcessingResult.Empty;
            }
            string path = await saveScreenshot();
            return new([new("new.bmp", path, false, null)], []);
        }
    }

    private static void ApplyTheme(Window window, RavenThemeDefinition theme)
    {
        window.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        foreach (
            KeyValuePair<string, string> entry in new Dictionary<string, string>
            {
                ["RavenWindowBrush"] = theme.WindowColor,
                ["RavenSurfaceBrush"] = theme.SurfaceColor,
                ["RavenRaisedSurfaceBrush"] = theme.RaisedSurfaceColor,
                ["RavenHighestSurfaceBrush"] = theme.HighestSurfaceColor,
                ["RavenStrongBorderBrush"] = theme.StrongBorderColor,
                ["RavenSidebarBrush"] = theme.SidebarColor,
                ["RavenAccentBrush"] = theme.AccentColor,
                ["RavenAccentMutedBrush"] = theme.AccentMutedColor,
                ["RavenSelectedMutedTextBrush"] = theme.MutedTextColor,
                ["RavenTextBrush"] = theme.TextColor,
                ["RavenMutedTextBrush"] = theme.MutedTextColor,
                ["RavenBorderBrush"] = theme.BorderColor,
                ["RavenControlAccentBrush"] = theme.ControlAccentColor,
                ["RavenSecondaryFillBrush"] = theme.SecondaryFillColor,
                ["RavenAccentForegroundBrush"] = theme.AccentForegroundColor,
            }
        )
        {
            window.Resources[entry.Key] = new SolidColorBrush(Color.Parse(entry.Value));
        }

        window.Background = new SolidColorBrush(Color.Parse(theme.WindowColor));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
