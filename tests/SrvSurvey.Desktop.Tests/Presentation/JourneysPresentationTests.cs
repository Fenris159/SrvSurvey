using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journeys;
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
        using var contents = new MemoryStream();
        await view.ExportHtmlAsync(options =>
        {
            Assert.Equal("html", options.DefaultExtension);
            Assert.EndsWith(".html", options.SuggestedFileName, StringComparison.Ordinal);
            return Task.FromResult<JourneyReportDestination?>(new(contents));
        });
        Assert.Contains("Expedition report", Encoding.UTF8.GetString(contents.ToArray()), StringComparison.Ordinal);
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

    private async Task<MainWindowViewModel> CreateModelAsync()
    {
        MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder =>
                builder.WithAppDataPaths(
                    new(
                        Path.Combine(directory, "config"),
                        Path.Combine(directory, "data"),
                        Path.Combine(directory, "cache"),
                        []
                    )
                )
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
