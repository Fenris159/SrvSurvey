using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ExplorationHistoryPresentationTests
{
    private static readonly string[] NotableLabels = ["ELW", "WW", "AW", "TFC", "GGG", "NSP", "NS", "BH"];
    private static readonly string[] NotableDescriptions =
    [
        "Earth-like world",
        "Water world",
        "Ammonia world",
        "Terraformable world",
        "Green gas giant",
        "Notable stellar phenomena",
        "Neutron star",
        "Black hole",
    ];

    [AvaloniaTheory]
    [InlineData(1120, 900, "monochrome-dark")]
    [InlineData(1600, 900, "monochrome-dark")]
    [InlineData(760, 900, "monochrome-dark")]
    [InlineData(1120, 900, "blue-dark")]
    [InlineData(1120, 900, "blue-light")]
    [InlineData(760, 900, "blue-light")]
    [InlineData(1120, 900, "orange-dark")]
    [InlineData(1120, 900, "green-dark")]
    [InlineData(1120, 900, "green-light")]
    [InlineData(760, 600, "monochrome-dark")]
    [InlineData(760, 600, "blue-light")]
    [InlineData(640, 600, "monochrome-dark")]
    [InlineData(640, 600, "blue-light")]
    public async Task HistoryUsesTheWorkspaceWidthAndKeepsRowsClearOfTheScrollbars(int width, int height, string theme)
    {
        using var fixture = new Fixture(theme);
        await fixture.PrepareAsync();
        ILogSink? previous = Logger.Sink;
        var log = new BindingLogSink();
        Logger.Sink = log;
        var view = new ExplorationView { DataContext = fixture.Main };
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view,
        };
        try
        {
            window.Show();
            fixture.Main.SelectedExplorationTabIndex = 1;
            using WriteableBitmap? first = window.CaptureRenderedFrame();
            Assert.NotNull(first);
            SavePreview(first, $"history-{theme}-{width}-{height}");
            ListBox list = view.GetVisualDescendants()
                .OfType<ListBox>()
                .Single(item => item.Name == "ExplorationHistoryRows");
            Assert.Equal(50, list.ItemCount);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Notable");
            ItemsControl discoveries = list.GetVisualDescendants()
                .OfType<ItemsControl>()
                .First(control => control.Name == "HistoryNotableTags");
            ExplorationNotableTagViewModel neutron = Assert.IsType<ExplorationNotableTagViewModel>(
                Assert.Single(discoveries.Items)
            );
            Assert.Equal("NS", neutron.Label);
            Assert.Equal("Neutron star", neutron.Description);
            Border tag = Assert.Single(
                discoveries.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("discovery-tag")
            );
            Assert.Equal("Neutron star", ToolTip.GetTip(tag));
            Point tagAt = tag.TranslatePoint(default, discoveries)!.Value;
            Assert.True(tagAt.X >= 0 && tagAt.X + tag.Bounds.Width <= discoveries.Bounds.Width);
            Assert.Equal(
                Assert.IsType<ISolidColorBrush>(view.FindResource("RavenTagFillBrush"), exactMatch: false).Color,
                ((ISolidColorBrush)tag.Background!).Color
            );
            Assert.Equal(
                Assert
                    .IsType<ISolidColorBrush>(view.FindResource("RavenAccentForegroundBrush"), exactMatch: false)
                    .Color,
                Assert
                    .IsType<ISolidColorBrush>(
                        Assert.Single(tag.GetVisualDescendants().OfType<TextBlock>()).Foreground,
                        exactMatch: false
                    )
                    .Color
            );
            Assert.InRange(list.Bounds.Width, width - 100, width);
            ScrollViewer scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.False(scroll.AllowAutoHide);
            ScrollContentPresenter viewport = scroll
                .GetVisualDescendants()
                .OfType<ScrollContentPresenter>()
                .Single(p => p.GetVisualAncestors().OfType<ScrollViewer>().First() == scroll);
            Assert.True(viewport.Bounds.Height > 100);
            if (height == 600)
            {
                ScrollViewer controls = view.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .Single(control => control.Name == "HistoryControlsViewport");
                Assert.True(controls.Extent.Height > controls.Viewport.Height);
                controls.ScrollToEnd();
                using WriteableBitmap? filters = window.CaptureRenderedFrame();
                SavePreview(filters!, $"short-filters-{theme}-{width}-{height}");
                ScrollContentPresenter controlsViewport = controls
                    .GetVisualDescendants()
                    .OfType<ScrollContentPresenter>()
                    .Single(p => p.GetVisualAncestors().OfType<ScrollViewer>().First() == controls);
                Button clear = view.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Clear"));
                Point clearAt = clear.TranslatePoint(default, controlsViewport)!.Value;
                Assert.True(clearAt.X >= 0 && clearAt.X + clear.Bounds.Width <= controlsViewport.Bounds.Width);
                Assert.True(clearAt.Y >= 0 && clearAt.Y + clear.Bounds.Height <= controlsViewport.Bounds.Height);
                Button next = view.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Next"));
                Point nextAt = next.TranslatePoint(default, window)!.Value;
                Assert.True(nextAt.Y >= 0 && nextAt.Y + next.Bounds.Height <= window.Bounds.Height);
            }

            foreach (
                TextBlock status in list.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(block => block.Text is "Sold" or "Unsold" or "Lost")
            )
            {
                Point at = status.TranslatePoint(default, viewport)!.Value;
                Assert.True(
                    at.X + status.Bounds.Width <= viewport.Bounds.Width - 8,
                    "A status overlaps the vertical scrollbar."
                );
                string resource = status.Text switch
                {
                    "Sold" => "RavenSuccessBrush",
                    "Lost" => "RavenDangerBrush",
                    _ => "RavenWarningBrush",
                };
                Assert.Equal(
                    ((ISolidColorBrush)Application.Current!.Resources[resource]!).Color,
                    ((ISolidColorBrush)status.Foreground!).Color
                );
            }
            Point titleBefore = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text == "Exploration history" && block.Classes.Contains("page-title"))
                .TranslatePoint(default, window)!
                .Value;
            scroll.ScrollToEnd();
            using WriteableBitmap? lower = window.CaptureRenderedFrame();
            Assert.True(scroll.Offset.Y > 0);
            Point titleAfter = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text == "Exploration history" && block.Classes.Contains("page-title"))
                .TranslatePoint(default, window)!
                .Value;
            Assert.Equal(titleBefore, titleAfter);
            SavePreview(lower!, $"history-scrolled-{theme}-{width}-{height}");
            fixture.Main.ExplorationHistory.SelectedRow = fixture.Main.ExplorationHistory.Rows[0];
            await fixture.Main.ExplorationHistory.PendingDetails;
            using WriteableBitmap? details = window.CaptureRenderedFrame();
            Assert.NotNull(details);
            SavePreview(details, $"details-{theme}-{width}-{height}");
            Assert.Equal("NS", Assert.Single(fixture.Main.ExplorationHistory.Bodies).BodyClass);
            Assert.Empty(log.Messages);
        }
        finally
        {
            window.Close();
            Logger.Sink = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(760, "blue-light", true)]
    [InlineData(1600, "monochrome-dark", false)]
    public async Task NotableDiscoveriesWrapWithinTheirColumnAndExplainEveryAbbreviation(
        int width,
        string theme,
        bool wraps
    )
    {
        using var fixture = new Fixture(theme, withNotables: true);
        await fixture.PrepareAsync();
        var view = new ExplorationView { DataContext = fixture.Main };
        var window = new Window
        {
            Width = width,
            Height = 900,
            Content = view,
        };
        try
        {
            window.Show();
            fixture.Main.SelectedExplorationTabIndex = 1;
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SavePreview(frame, $"notables-{theme}-{width}");
            ItemsControl discoveries = view.GetVisualDescendants()
                .OfType<ItemsControl>()
                .First(control => control.Name == "HistoryNotableTags");
            Assert.Equal(
                NotableLabels,
                discoveries.Items.OfType<ExplorationNotableTagViewModel>().Select(tag => tag.Label)
            );
            Border[] tags = discoveries
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("discovery-tag"))
                .ToArray();
            Assert.Equal(8, tags.Length);
            Assert.Equal(NotableDescriptions, tags.Select(tag => ToolTip.GetTip(tag)));
            var lines = new HashSet<double>();
            foreach (Border tag in tags)
            {
                Point point = tag.TranslatePoint(default, discoveries)!.Value;
                Assert.True(point.X >= 0 && point.X + tag.Bounds.Width <= discoveries.Bounds.Width);
                Assert.True(point.Y >= 0 && point.Y + tag.Bounds.Height <= discoveries.Bounds.Height);
                lines.Add(point.Y);
            }
            Assert.Equal(wraps, lines.Count > 1);
            Grid row = discoveries.GetVisualAncestors().OfType<Grid>().First();
            TextBlock estimate = row
                .Children.OfType<TextBlock>()
                .Single(block => block.Classes.Contains("history-estimate"));
            Button system = row.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("system-link"));
            Point notableAt = discoveries.TranslatePoint(default, row)!.Value;
            Point systemAt = system.TranslatePoint(default, row)!.Value;
            Point estimateAt = estimate.TranslatePoint(default, row)!.Value;
            Assert.True(systemAt.X + system.Bounds.Width <= notableAt.X);
            Assert.True(notableAt.X + discoveries.Bounds.Width <= estimateAt.X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TabSelectionTrackingCopyFiltersAndPagingWorkThroughTheVisibleControls()
    {
        using var fixture = new Fixture("blue-light");
        await fixture.PrepareAsync();
        var view = new ExplorationView { DataContext = fixture.Main };
        var window = new Window
        {
            Width = 1120,
            Height = 900,
            Content = view,
        };
        try
        {
            window.Show();
            using WriteableBitmap? totals = window.CaptureRenderedFrame();
            SavePreview(totals!, "survey-totals-blue-light");
            CheckBox tracking = view.FindControl<CheckBox>("AutoTrackExplorationValue")!;
            tracking.BringIntoView();
            using WriteableBitmap? profile = window.CaptureRenderedFrame();
            SavePreview(profile!, "commander-profile-blue-light");
            Click(window, tracking);
            await fixture.Main.ExplorationHistory.PendingPreferenceSave;
            Assert.True(fixture.Main.ExplorationHistory.AutoResetEstimatedValue);
            Assert.Equal("577,650 CR", fixture.Main.EstimatedExplorationValue);
            TabControl tabs = view.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedIndex = 1;
            using WriteableBitmap? history = window.CaptureRenderedFrame();
            Assert.True(fixture.Main.IsExplorationHistorySelected);
            ExplorationHistoryViewModel model = fixture.Main.ExplorationHistory;
            Button copy = view.GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.Classes.Contains("system-link"));
            Click(window, copy);
            Assert.Equal("Survey system 75", await window.Clipboard!.TryGetTextAsync());
            ComboBox status = view.GetVisualDescendants()
                .OfType<ExplorationHistoryView>()
                .Single()
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .Single();
            status.SelectedItem = ExplorationHistoryViewModel.StatusOptions[2];
            await model.PendingQuery;
            Assert.Equal(25, model.Rows.Count);
            Assert.All(model.Rows, row => Assert.Equal("Sold", row.Status));
            Button clear = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Clear"));
            Click(window, clear);
            await model.PendingQuery;
            Button next = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Next"));
            Click(window, next);
            await model.PendingQuery;
            Assert.Equal("51–75 of 75 entries", model.PageDescription);
            Assert.Equal(25, model.Rows.Count);
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? lastPage = window.CaptureRenderedFrame();
            Assert.False(
                next.IsEffectivelyEnabled,
                $"Next command can execute: {model.NextCommand.CanExecute(null)}; query busy: {model.IsQueryBusy}; {model.PageDescription}"
            );
            Button previousPage = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Previous"));
            Click(window, previousPage);
            await model.PendingQuery;
            Assert.Equal("1–50 of 75 entries", model.PageDescription);
            TextBox search = view.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(box => box.Name == "HistorySystemSearch");
            search.Text = "system 75";
            await model.PendingQuery;
            Assert.Equal("Survey system 75", Assert.Single(model.Rows).SystemName);
            ListBox list = view.GetVisualDescendants()
                .OfType<ListBox>()
                .Single(item => item.Name == "ExplorationHistoryRows");
            list.SelectedIndex = 0;
            await model.PendingDetails;
            Assert.Contains("StellarMass", model.RawJson);
            Button back = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Back to history"));
            Click(window, back);
            Assert.False(model.HasSelection);
            model.FromDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await model.PendingQuery;
            Assert.Empty(model.Rows);
            model.ThroughDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await model.PendingQuery;
            Assert.Equal("The start date must be on or before the end date.", model.StatusMessage);
            Click(window, clear);
            await model.PendingQuery;
            Assert.Null(model.FromDate);
            Assert.Null(model.ThroughDate);
            Assert.Equal(50, model.Rows.Count);
            Assert.Equal("History cataloged from 1 journal file for Survey commander.", model.StatusMessage);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EmptyAndDetachedHistoryRemainSafeDuringCommanderChanges()
    {
        using var fixture = new Fixture("monochrome-dark", false);
        await fixture.PrepareAsync();
        var view = new ExplorationHistoryView { DataContext = fixture.Main.ExplorationHistory };
        var window = new Window
        {
            Width = 760,
            Height = 820,
            Content = view,
        };
        window.Show();
        using WriteableBitmap? empty = window.CaptureRenderedFrame();
        SavePreview(empty!, "empty-history");
        Assert.Empty(fixture.Main.ExplorationHistory.Rows);
        fixture.Main.ExplorationHistory.SetCommander(null, null, true, false);
        Assert.False(fixture.Main.ExplorationHistory.RefreshCommand.CanExecute(null));
        window.Close();
        fixture.Main.ExplorationHistory.SetCommander("F100", "Survey commander", true, true);
        await fixture.Main.ExplorationHistory.PendingSynchronization;
        Assert.Empty(fixture.Main.ExplorationHistory.Rows);
    }

    private static void Click(Window window, Control control)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Point point = control
            .TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SavePreview(WriteableBitmap frame, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_EXPLORATION_DESIGN_OUTPUT");
        if (output is null)
        {
            return;
        }
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class BindingLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == "Binding";

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Log(level, area, source, messageTemplate, []);

        public void Log(
            LogEventLevel level,
            string area,
            object? source,
            string messageTemplate,
            params object?[] propertyValues
        )
        {
            if (IsEnabled(level, area))
            {
                Messages.Add(messageTemplate + " " + string.Join(" | ", propertyValues));
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-history-presentation-" + Guid.NewGuid()
        );
        private readonly RavenThemeService themes;
        private readonly string originalTheme;

        public Fixture(string theme, bool withData = true, bool withNotables = false)
        {
            Directory.CreateDirectory(directory);
            var lines = new List<string>
            {
                """{"timestamp":"2026-10-01T12:00:00Z","event":"Fileheader","Odyssey":true,"gameversion":"4.0.0"}""",
                """{"timestamp":"2026-10-01T12:00:01Z","event":"Commander","FID":"F100","Name":"Survey commander"}""",
            };
            if (withData)
            {
                for (int i = 1; i <= 75; i++)
                {
                    string timestamp = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
                        .AddMinutes(i)
                        .ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                    lines.Add(
                        $$"""{"timestamp":"{{timestamp}}","event":"Location","StarSystem":"Survey system {{i}}","SystemAddress":{{i}},"StarPos":[100,200,-300]}"""
                    );
                    lines.Add(
                        $$"""{"timestamp":"{{timestamp}}","event":"Scan","SystemAddress":{{i}},"BodyID":0,"BodyName":"Survey system {{i}} A","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}"""
                    );
                    if (withNotables && i == 75)
                    {
                        lines.Add(
                            $$"""{"timestamp":"{{timestamp}}","event":"Scan","SystemAddress":75,"BodyID":1,"BodyName":"Survey system 75 1","PlanetClass":"Earthlike body","MassEM":1}"""
                        );
                        lines.Add(
                            $$"""{"timestamp":"{{timestamp}}","event":"Scan","SystemAddress":75,"BodyID":2,"BodyName":"Survey system 75 2","PlanetClass":"Water world","TerraformState":"Terraformable","MassEM":1}"""
                        );
                        lines.Add(
                            $$"""{"timestamp":"{{timestamp}}","event":"Scan","SystemAddress":75,"BodyID":3,"BodyName":"Survey system 75 3","PlanetClass":"Ammonia world","MassEM":1}"""
                        );
                        lines.Add(
                            $$"""{"timestamp":"{{timestamp}}","event":"Scan","SystemAddress":75,"BodyID":4,"BodyName":"Survey system 75 B","StarType":"H","StellarMass":1.4}"""
                        );
                        lines.Add(
                            $$"""{"timestamp":"{{timestamp}}","event":"CodexEntry","Name":"$Codex_Ent_Green_Sudarsky_Class_II_Name;","SystemAddress":75,"System":"Survey system 75"}"""
                        );
                        lines.Add(
                            $$"""{"timestamp":"{{timestamp}}","event":"FSSSignalDiscovered","SystemAddress":75,"SignalName":"$Fixed_Event_Life_Cloud;"}"""
                        );
                    }
                    if (i == 25)
                    {
                        string names = JsonSerializer.Serialize(
                            Enumerable.Range(1, 25).Select(n => "Survey system " + n)
                        );
                        lines.Add(
                            $$"""{"timestamp":"{{timestamp}}","event":"SellExplorationData","Systems":{{names}},"TotalEarnings":500000}"""
                        );
                    }
                    if (i == 50)
                    {
                        lines.Add($$"""{"timestamp":"{{timestamp}}","event":"Died"}""");
                        lines.Add($$"""{"timestamp":"{{timestamp}}","event":"Resurrect","Option":"rebuy","Cost":0}""");
                    }
                }
            }
            File.WriteAllLines(Path.Combine(directory, "Journal.2026-10-01T120000.01.log"), lines);
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
            Application app = Assert.IsType<Application>(Application.Current, exactMatch: false);
            themes = new RavenThemeService(app, new ThemePreferenceStore(Path.Combine(directory, "theme.json")));
            Color color = Assert.IsType<ISolidColorBrush>(app.Resources["RavenWindowBrush"], exactMatch: false).Color;
            originalTheme = RavenThemeCatalog.All.Single(palette => Color.Parse(palette.WindowColor) == color).Key;
            themes.Select(theme == "blue-dark" ? "monochrome-dark" : "blue-dark");
            themes.Select(theme);
        }

        public MainWindowViewModel Main { get; }

        public async Task PrepareAsync()
        {
            await Main.RefreshAsync();
            await Main.ExplorationHistory.PendingSynchronization;
        }

        public void Dispose()
        {
            Main.Dispose();
            themes.Select(originalTheme);
            Directory.Delete(directory, true);
        }
    }
}
