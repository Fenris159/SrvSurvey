using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CodexBingoPresentationTests
{
    [AvaloniaTheory]
    [InlineData(980, "monochrome-dark")]
    [InlineData(1260, "monochrome-dark")]
    [InlineData(980, "blue-light")]
    [InlineData(1260, "blue-light")]
    [InlineData(1260, "blue-dark")]
    [InlineData(1260, "orange-dark")]
    [InlineData(1260, "green-light")]
    [InlineData(1260, "green-dark")]
    public async Task ExpandedHierarchyKeepsCompletionReadoutsInsideTheScrollViewport(int width, string theme)
    {
        using var fixture = new BingoFixture(theme);
        await fixture.InitializeAsync();
        var window = new BiologyCodexBingoWindow(fixture.Model) { Width = width, Height = width == 980 ? 680 : 820 };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SavePreview(frame, $"hierarchy-{theme}-{width}");
            TreeView tree = Assert.Single(window.GetVisualDescendants().OfType<TreeView>());
            ScrollContentPresenter viewport = Assert.Single(
                tree.GetVisualDescendants().OfType<ScrollContentPresenter>()
            );
            ScrollViewer scroll = Assert.Single(tree.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Equal(0, scroll.Offset.X);
            Assert.True(fixture.Model.RootNodes[0].CompletionPercent > 0);
            AssertReadoutFits(tree, viewport, fixture.Model.RootNodes[0]);
            AssertReadoutFits(tree, viewport, fixture.Model.SelectedNode!);
            AssertSelectedTextReadable(tree, fixture.Model.SelectedNode!);
            foreach (string value in new[] { "2 found", "Undiscovered" })
            {
                TextBlock text = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == value);
                Border badge = Assert.IsType<Border>(text.Parent);
                AssertReadable(text, Assert.IsType<ISolidColorBrush>(badge.Background, exactMatch: false).Color);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(980, "monochrome-dark")]
    [InlineData(1260, "monochrome-dark")]
    [InlineData(980, "blue-light")]
    public async Task EntryLocationAndManualReviewStayReachableWithoutMovingGlobalTools(int width, string theme)
    {
        using var fixture = new BingoFixture(theme);
        await fixture.InitializeAsync();
        BiologyCodexBingoViewModel model = fixture.Model;
        var window = new BiologyCodexBingoWindow(model) { Width = width, Height = width == 980 ? 680 : 820 };
        try
        {
            window.Show();
            TreeView tree = window.FindControl<TreeView>("BingoHierarchy")!;
            TextBlock title = window.FindControl<TextBlock>("BingoSelectedTitle")!;
            tree.SelectedItem = null;
            Assert.Equal("Select a Codex category or entry", title.Text);
            tree.SelectedItem = model.RootNodes[0];
            Assert.Equal("The Codex", title.Text);
            tree.SelectedItem = model.RootNodes[0].Find("entry:2310101");
            Assert.Equal("Aleoida Arcus - Yellow", title.Text);
            Assert.Same(tree.SelectedItem, model.SelectedNode);
            Assert.True(model.SelectedIsJournalVerified);
            Assert.Equal("Synuefai CX-V c18-6 4 a", model.DiscoveryBody);
            Button location = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == model.OpenLocationCommand);
            Assert.True(location.IsEffectivelyEnabled);
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            Assert.NotNull(initial);
            SavePreview(initial, $"discovered-{theme}-{width}");
            ScrollViewer scroll = window.FindControl<ScrollViewer>("BingoDetailsScroll")!;
            ScrollContentPresenter viewport = Assert.Single(
                scroll.GetVisualDescendants().OfType<ScrollContentPresenter>()
            );
            Control tools = window.FindControl<Border>("BingoCanonnTools")!;
            Point toolsTop = tools.TranslatePoint(default, window)!.Value;
            location.BringIntoView();
            using WriteableBitmap? locationFrame = window.CaptureRenderedFrame();
            Assert.NotNull(locationFrame);
            AssertButtonFits(location, viewport);

            tree.SelectedItem = model.RootNodes[0].Find("entry:2202001");
            Button manual = window.FindControl<Button>("BingoManualAction")!;
            using WriteableBitmap? selectionFrame = window.CaptureRenderedFrame();
            Assert.NotNull(selectionFrame);
            manual.BringIntoView();
            using WriteableBitmap? manualFrame = window.CaptureRenderedFrame();
            Assert.NotNull(manualFrame);
            Assert.True(manual.IsEffectivelyEnabled);
            AssertButtonFits(manual, viewport);
            Click(window, manual);
            Assert.True(model.IsManualConfirmationPending);
            Control confirmation = window.FindControl<Border>("BingoManualConfirmation")!;
            using WriteableBitmap? layout = window.CaptureRenderedFrame();
            Assert.NotNull(layout);
            Button cancel = confirmation
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == model.CancelManualCommand);
            confirmation.BringIntoView();
            using WriteableBitmap? review = window.CaptureRenderedFrame();
            Assert.NotNull(review);
            Assert.True(confirmation.IsEffectivelyVisible);
            foreach (Button button in confirmation.GetVisualDescendants().OfType<Button>())
            {
                AssertButtonFits(button, viewport);
            }
            Assert.Equal(toolsTop.Y, tools.TranslatePoint(default, window)!.Value.Y, 1);
            Assert.InRange(toolsTop.Y + tools.Bounds.Height, tools.Bounds.Height, window.ClientSize.Height);
            SavePreview(review, $"manual-review-{theme}-{width}");
            Click(window, cancel);
            Assert.False(model.IsManualConfirmationPending);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, Button button)
    {
        Point center = button
            .TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }

    private static void AssertButtonFits(Button button, ScrollContentPresenter viewport)
    {
        Point top = button.TranslatePoint(default, viewport)!.Value;
        Assert.InRange(top.X, 0, viewport.Bounds.Width - button.Bounds.Width - 8);
        Assert.InRange(top.Y, 0, viewport.Bounds.Height - button.Bounds.Height);
    }

    private static void AssertSelectedTextReadable(TreeView tree, CodexBingoTreeNodeViewModel node)
    {
        TreeViewItem item = tree.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .Single(row => ReferenceEquals(row.DataContext, node));
        Border background = item.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == "PART_LayoutRoot");
        Color fill = Assert.IsType<ISolidColorBrush>(background.Background, exactMatch: false).Color;
        foreach (TextBlock text in item.GetVisualDescendants().OfType<TextBlock>())
        {
            AssertReadable(text, fill);
        }
    }

    private static void AssertReadable(TextBlock text, Color fill)
    {
        Color ink = Assert.IsType<ISolidColorBrush>(text.Foreground, exactMatch: false).Color;
        double lighter = Math.Max(Luminance(ink), Luminance(fill));
        double darker = Math.Min(Luminance(ink), Luminance(fill));
        Assert.True((lighter + 0.05) / (darker + 0.05) >= 4.5, $"'{text.Text}' has insufficient contrast.");
    }

    [AvaloniaTheory]
    [InlineData("monochrome-dark")]
    [InlineData("blue-light")]
    public async Task LongImportWarningsKeepTheHierarchyUsableAtMinimumSize(string theme)
    {
        string warnings = string.Join(
            " ",
            Enumerable.Range(1, 80).Select(index => $"Warning {index}: journal reference is unavailable.")
        );
        using var fixture = new BingoFixture(theme, warnings);
        await fixture.InitializeAsync();
        await fixture.Model.ImportCanonnAsync();
        Assert.Contains("Warning 80:", fixture.Model.StatusMessage);
        var window = new BiologyCodexBingoWindow(fixture.Model) { Width = 980, Height = 680 };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            TreeView tree = Assert.Single(window.GetVisualDescendants().OfType<TreeView>());
            ScrollViewer hierarchy = Assert.Single(tree.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(hierarchy.Viewport.Height > 100);
            ScrollViewer status = window.FindControl<ScrollViewer>("BingoStatusViewport")!;
            Assert.True(status.Extent.Height > status.Viewport.Height);
            status.ScrollToEnd();
            using WriteableBitmap? scrolled = window.CaptureRenderedFrame();
            TextBlock text = window.FindControl<TextBlock>("BingoStatusText")!;
            Assert.Equal(fixture.Model.StatusMessage, text.Text);
            ScrollContentPresenter viewport = status.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
            Point end = text.TranslatePoint(new Point(0, text.Bounds.Height), viewport)!.Value;
            Assert.InRange(end.Y, 0, viewport.Bounds.Height + 1);
        }
        finally
        {
            window.Close();
        }
    }

    private static double Luminance(Color color) =>
        0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);

    private static double LinearChannel(byte value)
    {
        double channel = value / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static void AssertReadoutFits(
        TreeView tree,
        ScrollContentPresenter viewport,
        CodexBingoTreeNodeViewModel node
    )
    {
        TextBlock readout = Assert.Single(
            tree.GetVisualDescendants().OfType<TextBlock>(),
            text => ReferenceEquals(text.DataContext, node) && text.Text == node.CompletionText
        );
        Point right = readout.TranslatePoint(new Point(readout.Bounds.Width, 0), viewport)!.Value;
        ScrollBar scrollbar = tree.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(bar => bar.Orientation == Orientation.Vertical && bar.IsVisible);
        double clearEdge = Math.Min(viewport.Bounds.Width, scrollbar.TranslatePoint(default, viewport)!.Value.X) - 8;
        Assert.True(
            right.X <= clearEdge,
            $"{node.Name} completion ends at {right.X:N1} in a {viewport.Bounds.Width:N1}px viewport."
        );
        ProgressBar progress = Assert.Single(Assert.IsType<Grid>(readout.Parent).Children.OfType<ProgressBar>());
        Assert.Equal(node.CompletionPercent, progress.Value);
        Point progressRight = progress.TranslatePoint(new Point(progress.Bounds.Width, 0), viewport)!.Value;
        Assert.True(progressRight.X <= clearEdge, $"{node.Name} progress runs into the scrollbar gutter.");
    }

    private static void SavePreview(WriteableBitmap frame, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_BINGO_DESIGN_OUTPUT");
        if (output is null)
        {
            return;
        }
        Directory.CreateDirectory(output);
        using FileStream file = File.Create(Path.Combine(output, name + ".png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    private sealed class BingoFixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-bingo-design-" + Guid.NewGuid()
        );
        private readonly RavenThemeService themes;
        private readonly string originalTheme;
        private readonly CommanderCodexStore store;

        public BingoFixture(string theme, string? challengeError = null)
        {
            Application application = Assert.IsType<Application>(Application.Current, exactMatch: false);
            themes = new RavenThemeService(
                application,
                new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
            );
            Color originalFill = Assert
                .IsType<ISolidColorBrush>(application.Resources["RavenWindowBrush"], exactMatch: false)
                .Color;
            originalTheme = RavenThemeCatalog
                .All.Single(palette => Color.Parse(palette.WindowColor) == originalFill)
                .Key;
            themes.Select(theme == "blue-dark" ? "monochrome-dark" : "blue-dark");
            themes.Select(theme);
            store = new CommanderCodexStore(Path.Combine(directory, "codex"));
            var catalog = ExobiologyReferenceCatalog.LoadEmbedded();
            Model = new BiologyCodexBingoViewModel(
                store,
                catalog,
                new CanonnCodexChallengeImporter(new ChallengeClient(challengeError), store, catalog),
                new CommanderCodexJournalImporter(Path.Combine(directory, "journals"), store),
                new LocationClient()
            );
            Model.SetPlatformServices(_ => Task.CompletedTask, _ => Task.FromResult(true));
            Model.SetNearestSearchHandler(_ => Task.CompletedTask);
        }

        public BiologyCodexBingoViewModel Model { get; }

        public async Task InitializeAsync()
        {
            await store.TrackAsync(
                new CommanderCodexTrackRequest
                {
                    FrontierId = "F123",
                    CommanderName = "Fenris Nihilus",
                    EntryId = 2310101,
                    Timestamp = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero),
                    SystemAddress = 42,
                    BodyId = 3,
                }
            );
            await store.SetManualDiscoveryAsync(
                "F123",
                "Fenris Nihilus",
                2208101,
                true,
                new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)
            );
            await Model.UpdateContextAsync("F123", "Fenris Nihilus", "Sol", new GalacticCoordinate(0, 0, 0));
            CodexBingoTreeNodeViewModel root = Model.RootNodes[0];
            Model.SelectedNode = root.Find("entry:2202001");
            Assert.NotNull(Model.SelectedNode);
            ExpandPath(root, "entry:2202001");
        }

        public void Dispose()
        {
            Model.Dispose();
            themes.Select(originalTheme);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        private static void ExpandPath(CodexBingoTreeNodeViewModel node, string key)
        {
            if (node.Find(key) is null)
            {
                return;
            }
            node.IsExpanded = true;
            foreach (CodexBingoTreeNodeViewModel child in node.Children)
            {
                ExpandPath(child, key);
            }
        }
    }

    private sealed class ChallengeClient(string? error) : ICanonnCodexChallengeClient
    {
        public Task<CanonnCodexChallengeLoadResult> GetAsync(
            string commanderName,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new CanonnCodexChallengeLoadResult([], error));
    }

    private sealed class LocationClient : ICodexDiscoveryLocationClient
    {
        public Task<CodexDiscoveryLocationLoadResult> GetAsync(
            long systemAddress,
            int bodyId,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new CodexDiscoveryLocationLoadResult(
                    new CodexDiscoveryLocation(
                        systemAddress,
                        bodyId,
                        "Synuefai CX-V c18-6",
                        "Synuefai CX-V c18-6 4 a",
                        new GalacticRegion(18, "Inner Orion Spur"),
                        new GalacticCoordinate(0, 0, 0),
                        new Uri("https://spansh.co.uk/body/123456789")
                    ),
                    null
                )
            );
    }
}
