using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiagnosticsDesignPresentationTests
{
    [AvaloniaTheory]
    [InlineData(1120, "monochrome-dark")]
    [InlineData(1600, "monochrome-dark")]
    [InlineData(760, "monochrome-dark")]
    [InlineData(1120, "blue-dark")]
    [InlineData(1120, "blue-light")]
    [InlineData(760, "blue-light")]
    [InlineData(1120, "orange-dark")]
    [InlineData(1120, "green-dark")]
    [InlineData(1120, "green-light")]
    public async Task DiagnosticToolsKeepHeadingsVisibleAndControlsClearOfScrollbars(int width, string theme)
    {
        using var fixture = new DiagnosticsFixture(theme);
        await fixture.PrepareAsync();
        var errors = new List<string>();
        foreach (DiagnosticsWorkspaceTab tab in TargetTabs)
        {
            fixture.Main.SelectedDiagnosticsTab = tab;
            var view = new DiagnosticsView { DataContext = fixture.Main };
            var window = new Window
            {
                Width = width,
                Height = 820,
                Content = view,
            };
            try
            {
                window.Show();
                using WriteableBitmap? first = window.CaptureRenderedFrame();
                Assert.NotNull(first);
                SavePreview(first, $"{tab}-{theme}-{width}");
                TextBlock title = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(block => block.Classes.Contains("page-title"));
                Point titleTop = title.TranslatePoint(default, window)!.Value;
                Border firstPanel = view.GetVisualDescendants()
                    .OfType<Border>()
                    .First(border => border.Classes.Contains("card") && border.IsEffectivelyVisible);
                Point panelLeft = firstPanel.TranslatePoint(default, window)!.Value;
                if (Math.Abs(panelLeft.X - titleTop.X) > 1)
                {
                    errors.Add($"{tab}: content panels are not aligned with the workspace heading.");
                }
                ScrollViewer scroll = view.FindControl<ScrollViewer>("DiagnosticsPageScroller")!;
                ScrollContentPresenter viewport = scroll
                    .GetVisualDescendants()
                    .OfType<ScrollContentPresenter>()
                    .Single(presenter => presenter.GetVisualAncestors().OfType<ScrollViewer>().First() == scroll);
                foreach (
                    Expander expander in view.GetVisualDescendants()
                        .OfType<Expander>()
                        .Where(expander => expander.IsEffectivelyVisible)
                )
                {
                    expander.IsExpanded = true;
                }
                using WriteableBitmap? expanded = window.CaptureRenderedFrame();
                foreach (
                    ResponsiveColumnsPanel columns in view.GetVisualDescendants()
                        .OfType<ResponsiveColumnsPanel>()
                        .Where(panel => panel.IsEffectivelyVisible)
                )
                {
                    foreach (Control region in columns.Children.Where(child => child.IsVisible))
                    {
                        if (
                            region.Bounds.Right > columns.Bounds.Width + 1
                            || region.Bounds.Bottom > columns.Bounds.Height + 1
                        )
                        {
                            errors.Add(
                                $"{tab}: a section extends beyond its layout region and overlaps adjacent content."
                            );
                        }
                    }
                }
                foreach (Control control in view.GetVisualDescendants().OfType<Control>().Where(IsFormControl))
                {
                    if (
                        !control.IsEffectivelyVisible
                        || control.Bounds.Width == 0
                        || control.GetVisualAncestors().OfType<ScrollBar>().Any()
                    )
                    {
                        continue;
                    }
                    Point left = control.TranslatePoint(default, viewport)!.Value;
                    if (left.X < 0 || left.X + control.Bounds.Width > viewport.Bounds.Width - 8)
                    {
                        errors.Add($"{tab}: {control.Name ?? control.GetType().Name} overlaps the page gutter.");
                    }
                }

                scroll.Offset = new Vector(0, Math.Min(350, scroll.Extent.Height - scroll.Viewport.Height));
                using WriteableBitmap? lower = window.CaptureRenderedFrame();
                Assert.NotNull(lower);
                SavePreview(lower, $"{tab}-lower-{theme}-{width}");
                Point after = title.TranslatePoint(default, window)!.Value;
                if (Math.Abs(after.Y - titleTop.Y) > 1 || after.Y < 0)
                {
                    errors.Add($"{tab}: scrolling hides the workspace heading.");
                }
                if (tab == DiagnosticsWorkspaceTab.Inspector)
                {
                    ListBox list = view.FindControl<ListBox>("JournalInspectorEventList")!;
                    Assert.Equal(70, list.ItemCount);
                }
                scroll.ScrollToEnd();
                using WriteableBitmap? bottom = window.CaptureRenderedFrame();
                Assert.NotNull(bottom);
                SavePreview(bottom, $"{tab}-bottom-{theme}-{width}");
            }
            finally
            {
                window.Close();
            }
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [AvaloniaFact]
    public async Task HistorySearchAndInspectorFieldSelectionDriveTheDisplayedDetailsAndCopiedHandler()
    {
        using var fixture = new DiagnosticsFixture("monochrome-dark");
        await fixture.PrepareAsync();
        fixture.Main.SelectedDiagnosticsTab = DiagnosticsWorkspaceTab.History;
        var view = new DiagnosticsView { DataContext = fixture.Main };
        var window = new Window
        {
            Width = 1120,
            Height = 900,
            Content = view,
        };
        try
        {
            window.Show();
            using WriteableBitmap? first = window.CaptureRenderedFrame();
            TextBox search = view.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(box => box.PlaceholderText == "Search event, commander, system, file, or JSON");
            search.Text = "FSDJump";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(fixture.Main.JournalHistory.Events);
            ListBox history = view.FindControl<ListBox>("JournalHistoryEventList")!;
            using WriteableBitmap? filtered = window.CaptureRenderedFrame();
            Click(window, history.GetVisualDescendants().OfType<ListBoxItem>().Single());
            Assert.Equal("FSDJump", fixture.Main.JournalHistory.SelectedEvent!.EventName);
            Assert.Contains("Galactic Survey Outpost", fixture.Main.JournalHistory.SelectedEventRawJson);
            CheckBox redact = view.GetVisualDescendants()
                .OfType<CheckBox>()
                .Single(box =>
                    Equals(
                        box.Content,
                        "Redact commander identities, chat, locations, coordinates, and screenshot paths"
                    )
                );
            Assert.True(redact.IsChecked);

            fixture.Main.SelectedDiagnosticsTab = DiagnosticsWorkspaceTab.Inspector;
            using WriteableBitmap? inspector = window.CaptureRenderedFrame();
            Assert.Equal(0, view.FindControl<ScrollViewer>("DiagnosticsPageScroller")!.Offset.Y);
            CheckBox field = view.GetVisualDescendants()
                .OfType<CheckBox>()
                .Single(box =>
                    box.DataContext is JournalInspectorPropertyViewModel property && property.Path == "BodyName"
                );
            Click(window, field);
            Assert.Contains("entry.BodyName == \"Survey Body 69\"", fixture.Main.JournalInspector.CodeText);
            ListBox recent = view.FindControl<ListBox>("JournalInspectorEventList")!;
            Point wheel = recent.TranslatePoint(new Point(80, 80), window)!.Value;
            window.MouseWheel(wheel, new Vector(0, -3));
            using WriteableBitmap? scrolled = window.CaptureRenderedFrame();
            ScrollViewer recentScroll = recent.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.InRange(recentScroll.Offset.Y, 1, recentScroll.Extent.Height - recentScroll.Viewport.Height);
            Assert.Equal(0, view.FindControl<ScrollViewer>("DiagnosticsPageScroller")!.Offset.Y);
            Button copy = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Copy handler"));
            copy.BringIntoView();
            using WriteableBitmap? handler = window.CaptureRenderedFrame();
            await window.Clipboard!.SetTextAsync("Clipboard before handler copy");
            Click(window, copy);
            Assert.Equal(fixture.Main.JournalInspector.CodeText, await window.Clipboard!.TryGetTextAsync());
            Assert.False(fixture.Main.JournalInspector.ReplayConfirmed);
            Assert.False(fixture.Main.JournalInspector.ReplayCommand.CanExecute(null));

            ScrollViewer page = view.FindControl<ScrollViewer>("DiagnosticsPageScroller")!;
            page.Offset = new Vector(0, 300);
            using WriteableBitmap? beforeTabChange = window.CaptureRenderedFrame();
            Assert.InRange(page.Offset.Y, 1, 300);
            fixture.Main.SelectedDiagnosticsTab = DiagnosticsWorkspaceTab.Processing;
            using WriteableBitmap? processing = window.CaptureRenderedFrame();
            Assert.Equal(0, view.FindControl<ScrollViewer>("DiagnosticsPageScroller")!.Offset.Y);
            Assert.False(fixture.Main.JournalPostProcessor.RebuildCodexCommand.CanExecute(null));
            CheckBox confirm = view.GetVisualDescendants()
                .OfType<CheckBox>()
                .Single(box => Equals(box.Content, "Confirm an all-history Commander Codex merge"));
            confirm.BringIntoView();
            using WriteableBitmap? confirmation = window.CaptureRenderedFrame();
            Click(window, confirm);
            Assert.True(fixture.Main.JournalPostProcessor.CodexRebuildConfirmed);
            Assert.True(fixture.Main.JournalPostProcessor.RebuildCodexCommand.CanExecute(null));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("monochrome-dark")]
    [InlineData("blue-light")]
    public async Task EmptyArchiveGivesDirectionWhileReconstructionAndReplayStayGuarded(string theme)
    {
        using var fixture = new DiagnosticsFixture(theme, false);
        await fixture.Main.JournalHistory.RefreshAsync();
        var view = new DiagnosticsView { DataContext = fixture.Main };
        var window = new Window
        {
            Width = 760,
            Height = 820,
            Content = view,
        };
        try
        {
            window.Show();
            foreach (DiagnosticsWorkspaceTab tab in TargetTabs)
            {
                fixture.Main.SelectedDiagnosticsTab = tab;
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                SavePreview(frame, $"empty-{tab}-{theme}");
                Assert.False(fixture.Main.JournalHistory.HasEvents);
                Assert.False(fixture.Main.JournalPostProcessor.RebuildCodexCommand.CanExecute(null));
                Assert.False(fixture.Main.JournalInspector.ReplayCommand.CanExecute(null));
                if (tab == DiagnosticsWorkspaceTab.History)
                {
                    Assert.Contains(
                        view.GetVisualDescendants().OfType<TextBlock>(),
                        block =>
                            block.IsEffectivelyVisible
                            && block.Text
                                == "No journal events are available. Check the journal folder in Source, then refresh this archive."
                    );
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static readonly DiagnosticsWorkspaceTab[] TargetTabs =
    [
        DiagnosticsWorkspaceTab.History,
        DiagnosticsWorkspaceTab.Processing,
        DiagnosticsWorkspaceTab.Inspector,
    ];

    private static bool IsFormControl(Control control) =>
        control
            is Button
                or CheckBox
                or ComboBox
                or TextBox
                or CalendarDatePicker
                or TimePicker
                or DatePicker
                or ListBox;

    private static void Click(Window window, Control control)
    {
        Point point = control
            .TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SavePreview(WriteableBitmap frame, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_DIAGNOSTICS_DESIGN_OUTPUT");
        if (output is null)
        {
            return;
        }
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class DiagnosticsFixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-diagnostics-design-" + Guid.NewGuid()
        );
        private readonly RavenThemeService themes;
        private readonly string originalTheme;
        private readonly JournalEventEnvelope[] events;

        public DiagnosticsFixture(string theme, bool hasData = true)
        {
            Directory.CreateDirectory(directory);
            string[] lines =
            [
                "{\"timestamp\":\"2026-10-01T12:00:00Z\",\"event\":\"Commander\",\"Name\":\"Diagnostic Commander\",\"FID\":\"F12345\"}",
                "{\"timestamp\":\"2026-10-01T12:00:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Galactic Survey Outpost\",\"SystemAddress\":123,\"StarPos\":[0,0,0]}",
                "{\"timestamp\":\"2026-10-01T12:00:02Z\",\"event\":\"Scan\",\"SystemAddress\":123,\"BodyName\":\"Survey star\",\"BodyID\":0,\"StarType\":\"G\",\"StellarMass\":1}",
                "{\"timestamp\":\"2026-10-01T12:00:03Z\",\"event\":\"Scan\",\"SystemAddress\":123,\"BodyName\":\"Survey gas giant\",\"BodyID\":1,\"PlanetClass\":\"Sudarsky class III gas giant\",\"SurfaceTemperature\":310}",
                .. Enumerable
                    .Range(0, 70)
                    .Select(index =>
                        $$$"""{"timestamp":"2026-10-01T12:01:00Z","event":"Scan","BodyName":"Survey Body {{{index}}}","Nested":{"ScientificObservationWithAnExtendedPropertyName":"Detailed observation retained for inspection and handler generation","Flags":{"GeologicalSignal":true}},"MassEM":1.25}"""
                    ),
            ];
            if (hasData)
            {
                File.WriteAllLines(Path.Combine(directory, "Journal.2026-10-01T120000.01.log"), lines);
            }
            events = lines
                .Skip(4)
                .Select(line =>
                {
                    Assert.True(JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? parsed, out _));
                    return parsed!;
                })
                .ToArray();
            string dataDirectory = Path.Combine(directory, "data");
            Directory.CreateDirectory(dataDirectory);
            if (hasData)
            {
                File.WriteAllText(
                    Path.Combine(dataDirectory, "F12345-live.json"),
                    "{\"fid\":\"F12345\",\"commander\":\"Diagnostic Commander\"}"
                );
                string systems = Path.Combine(dataDirectory, "systems", "F12345");
                Directory.CreateDirectory(systems);
                File.WriteAllText(
                    Path.Combine(systems, "Survey_123.json"),
                    "{\"bodies\":[{\"atmosphereComposition\":{\"CarbonDioxide\":100},\"organisms\":[{\"speciesLocalized\":\"Aleoida Arcus\"}]}]}"
                );
            }
            Main = MainWindowViewModelTestBuilder.Create(
                directory,
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
            Main.JournalHistory.RangeFrom = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            Main.JournalHistory.RangeTo = new DateTimeOffset(2026, 10, 1, 12, 5, 0, TimeSpan.Zero);
            Main.JournalPostProcessor.StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Application application = Assert.IsType<Application>(Application.Current, exactMatch: false);
            themes = new RavenThemeService(
                application,
                new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
            );
            Color fill = Assert
                .IsType<ISolidColorBrush>(application.Resources["RavenWindowBrush"], exactMatch: false)
                .Color;
            originalTheme = RavenThemeCatalog.All.Single(palette => Color.Parse(palette.WindowColor) == fill).Key;
            themes.Select(theme == "blue-dark" ? "monochrome-dark" : "blue-dark");
            themes.Select(theme);
        }

        public MainWindowViewModel Main { get; }

        public async Task PrepareAsync()
        {
            await Main.JournalHistory.RefreshAsync();
            Main.JournalHistory.SelectedEvent = Main.JournalHistory.Events.Single(item => item.EventName == "FSDJump");
            Main.JournalInspector.ApplyUpdate(
                events,
                new EliteStatus
                {
                    Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
                    Latitude = 12.5,
                    Longitude = -40.25,
                }
            );
            await Main.JournalPostProcessor.RefreshCommandersAsync();
            Main.JournalPostProcessor.StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await Main.JournalPostProcessor.AnalyzeAsync();
            Assert.Equal(
                "1",
                Main.JournalPostProcessor.Statistics.Single(statistic => statistic.Name == "Jumps").Value
            );
            Assert.Equal(1, Main.JournalPostProcessor.HistoricalGreenGasGiantCandidateCount);
            await Main.JournalPostProcessor.AnalyzeSystemsAsync();
            Assert.Equal("Aleoida Arcus", Assert.Single(Main.JournalPostProcessor.SystemSpecies).Name);
        }

        public void Dispose()
        {
            Main.Dispose();
            themes.Select(originalTheme);
            Directory.Delete(directory, true);
        }
    }
}
