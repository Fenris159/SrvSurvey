using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationWishListOverlayTests
{
    private const string Plotter = "PlotColonizationWishList";

    [AvaloniaFact]
    public void LivePanelFollowsWishListGameVisibilityAndSharedSuppression()
    {
        var model = new ColonizationWishListViewModel();
        var registry = new OverlayWindowRegistry();
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationWishListOverlayCoordinator(model, host.Session);
        Assert.False(coordinator.IsVisible);
        ApplyStops(model, 1);
        Assert.True(coordinator.IsVisible);
        ColonizationWishListOverlayWindow window = Assert.IsType<ColonizationWishListOverlayWindow>(
            host.PreparedWindows.Single()
        );
        Assert.Same(model, window.DataContext);
        Assert.False(window.ShowActivated);
        Assert.False(window.ShowInTaskbar);
        Assert.False(window.CanResize);
        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
        Assert.True(window.Topmost);
        Assert.Empty(host.InteractiveWindows);
        Assert.Equal(0, window.Position.X);
        Assert.InRange(window.Position.Y, 400, 540);
        ApplyStops(model, 2);
        using (WriteableBitmap? frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Port 2");
        coordinator.SetSuppressed(true);
        coordinator.SetSuppressed(true);
        Assert.False(coordinator.IsVisible);
        coordinator.SetSuppressed(false);
        Assert.True(coordinator.IsVisible);
        host.GameWindow = host.GameWindow with { IsForeground = false };
        host.Tick();
        Assert.False(coordinator.IsVisible);
        host.GameWindow = HostedOverlayTestHarness.AvailableGameWindow;
        host.Tick();
        Assert.True(coordinator.IsVisible);
        registry.SetUserVisibility(Plotter, false);
        host.Tick();
        Assert.False(registry.ShouldPresent(Plotter));
        registry.SetUserVisibility(Plotter, true);
        host.Tick();
        Assert.True(registry.ShouldPresent(Plotter));
        model.Apply(null, []);
        Assert.False(coordinator.IsVisible);
        ApplyStops(model, 1);
        Assert.True(coordinator.IsVisible);
        coordinator.Dispose();
        coordinator.Dispose();
        coordinator.SetSuppressed(true);
        model.Apply(null, []);
        Assert.False(coordinator.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false, 0, 1)]
    [InlineData(false, 100, 1.5)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 50, 1.5)]
    public void ListGrowsThenScrollsAtHalfMonitorHeightInLiveAndEditorHosts(bool editor, int percent, double dpi)
    {
        OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired(Plotter);
        var model = new ColonizationWishListViewModel();
        Window window;
        ColonizationWishListOverlayPresentation panel;
        int scale = OverlayScaleCatalog.GetIndex(percent);
        if (editor)
        {
            var preview = new OverlayPositionPreviewWindow(definition);
            preview.ConfigureScale(scale, null, dpi);
            panel = Assert.IsType<ColonizationWishListOverlayPresentation>(preview.RuntimePresentation);
            panel.DataContext = model;
            window = preview;
        }
        else
        {
            window = new ColonizationWishListOverlayWindow(model);
            var layout = new LegacyOverlayLayout(
                new Dictionary<string, LegacyOverlayPlacement>
                {
                    [Plotter] = definition.DefaultPlacement with { ScaleIndex = scale },
                },
                null,
                null
            );
            window.SetRenderScaling(dpi);
            OverlayThemeResources.Apply(window, layout, Plotter, new OverlayWindowRegistry());
            window.Show();
            panel = window.GetVisualDescendants().OfType<ColonizationWishListOverlayPresentation>().Single();
        }
        try
        {
            window.SetRenderScaling(dpi);
            ApplyStops(model, 1);
            window.Show();
            Render(window);
            double shortHeight = panel.Bounds.Height;
            ApplyStops(model, 2);
            Render(window);
            Assert.True(panel.Bounds.Height > shortHeight);
            ApplyStops(model, 30);
            Render(window);
            Screen screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary!;
            Point start = panel.TranslatePoint(default, window)!.Value;
            Point end = panel.TranslatePoint(new Point(0, panel.Bounds.Height), window)!.Value;
            Assert.InRange((end.Y - start.Y) * dpi, screen.Bounds.Height / 2d - 2, screen.Bounds.Height / 2d + 2);
            ScrollViewer scroll = panel.FindControl<ScrollViewer>("WishListScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Point target = scroll.TranslatePoint(new Point(30, 30), window)!.Value;
            window.MouseWheel(target, new Vector(0, -3));
            Render(window);
            Assert.True(scroll.Offset.Y > 0);
            Assert.Contains(
                panel.GetVisualDescendants().OfType<Button>(),
                button => button.Command == model.Stations[0].RemoveCommand
            );
            ApplyStops(model, 1);
            Render(window);
            Assert.InRange(panel.Bounds.Height, shortHeight - 1, shortHeight + 1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EditorUsesExistingColonizationCategoryAndSavesGrowthSettingsWithRepresentativePreview()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-wish-overlay-{Guid.NewGuid():N}");
        var registry = new OverlayWindowRegistry();
        var platform = new EditorPlatform();
        var store = new LegacyOverlayLayoutStore(directory);
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var editor = new OverlayInteractionViewModel(
            platform,
            new EditorTracker(),
            store,
            store.Load(),
            registry,
            host
        );
        try
        {
            OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired(Plotter);
            Assert.Contains(OverlaySettingsCategory.Colonization, definition.SettingsCategories);
            Assert.Equal(LegacyHorizontalAnchor.Left, definition.DefaultPlacement.Horizontal);
            Assert.Equal(LegacyVerticalAnchor.Middle, definition.DefaultPlacement.Vertical);
            Assert.Equal(0, definition.DefaultPlacement.HorizontalOffset);
            editor.SelectedCategory = editor.Categories.Single(category =>
                category.Category == OverlayLayoutCategory.CombatAndColonization
            );
            Assert.Equal("Combat & colonization", editor.SelectedCategory.DisplayName);
            Assert.Equal(OverlayLayoutCatalog.GetRequired("PlotBuildCommodities").Category, definition.Category);
            Assert.True(editor.Begin());
            editor.OpenOverlaySettings(Plotter);
            OverlayPositionPreviewWindow preview = host.PreviewWindows.Single(window =>
                window.Definition.Name == Plotter
            );
            Assert.IsType<ColonizationWishListOverlayPresentation>(preview.RuntimePresentation);
            Render(preview);
            Assert.Equal("Colonization wish list", preview.EditorFolderTabLabelControl.Text);
            ColonizationWishListViewModel model = Assert.IsType<ColonizationWishListViewModel>(
                preview.RuntimePresentation.DataContext
            );
            Assert.Equal(["Abraham Lincoln", "Galileo"], model.Stations.Select(station => station.Label));
            Assert.All(model.Stations, station => Assert.Equal("Sol", station.System));
            Assert.Equal("Steel: 100", model.Stations.Single(station => station.Label == "Galileo").Children[0].Label);
            foreach (
                OverlayGrowthDirection direction in new[]
                {
                    OverlayGrowthDirection.Left,
                    OverlayGrowthDirection.Right,
                    OverlayGrowthDirection.Automatic,
                }
            )
            {
                editor.CycleOverlayGrowthCommand.Execute(null);
                Assert.Equal(direction, editor.SelectedOverlayGrowthDirection);
            }
            using (WriteableBitmap? frame = preview.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-wishlist-overlay-editor.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            var live = new ColonizationWishListOverlayWindow(model);
            try
            {
                OverlayThemeResources.Apply(live, LegacyOverlayLayout.Empty, Plotter, new OverlayWindowRegistry());
                live.Show();
                Render(live);
                using WriteableBitmap? frame = live.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-wishlist-sol-live.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            finally
            {
                live.Close();
            }
            OverlayPositionEditorWindow toolbar = Assert.IsType<OverlayPositionEditorWindow>(host.EditorToolbar);
            using (WriteableBitmap? frame = toolbar.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-wishlist-overlay-editor-toolbar.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            editor.CycleOverlayGrowthCommand.Execute(null);
            editor.Save();
            Assert.Equal(OverlayGrowthDirection.Left, store.Load().Placements[Plotter].GrowthDirection);
        }
        finally
        {
            editor.Cancel();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("Medical diagnostic equipment", "Sol")]
    [InlineData("Steel", "Synuefe NL-N C23-4")]
    public void LiveWidthFollowsTheLongestRowAndShrinksWhenItIsRemoved(string commodityName, string systemName)
    {
        var model = new ColonizationWishListViewModel();
        var window = new ColonizationWishListOverlayWindow(model);
        try
        {
            OverlayThemeResources.Apply(window, LegacyOverlayLayout.Empty, Plotter, new OverlayWindowRegistry());
            ApplyStops(model, 1);
            window.Show();
            Render(window);
            double shortWidth = window.Bounds.Width;
            ApplyStops(model, 1, commodityName, systemName);
            Render(window);
            Assert.True(window.Bounds.Width > shortWidth + 30);
            ColonizationWishListOverlayPresentation panel = window
                .GetVisualDescendants()
                .OfType<ColonizationWishListOverlayPresentation>()
                .Single();
            ItemsControl rows = panel.FindControl<ItemsControl>("WishListRows")!;
            Assert.InRange(window.Bounds.Width - rows.Bounds.Width, 11, 13);
            Assert.All(
                panel.GetVisualDescendants().OfType<TextBlock>(),
                text => Assert.True(text.Bounds.Right <= panel.Bounds.Width + 1)
            );
            ApplyStops(model, 1);
            Render(window);
            Assert.InRange(window.Bounds.Width, shortWidth - 1, shortWidth + 1);
            Assert.InRange(window.Bounds.Width - rows.Bounds.Width, 11, 13);
            Assert.True(shortWidth < 160);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InteractiveModeCopiesOnlyTheSystemAndInvokesRemovalsWithoutDragging()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-wish-actions-{Guid.NewGuid():N}");
        var removals = new List<IReadOnlyList<ColonizationWishListItem>>();
        var model = new ColonizationWishListViewModel(removals.Add);
        ApplyStops(model, 1);
        var registry = new OverlayWindowRegistry();
        using var host = new HostedOverlayTestHarness(registry);
        using var overlay = new ColonizationWishListOverlayCoordinator(model, host.Session);
        var store = new LegacyOverlayLayoutStore(directory);
        using var interaction = new OverlayInteractionViewModel(
            new EditorPlatform(),
            new EditorTracker(),
            store,
            store.Load(),
            registry
        );
        try
        {
            Assert.True(interaction.ToggleLiveOverlayInteraction());
            Assert.True(interaction.IsLiveInteractionEnabled);
            ColonizationWishListOverlayWindow window = Assert.IsType<ColonizationWishListOverlayWindow>(
                host.PreparedWindows.Single()
            );
            Render(window);
            ColonizationWishListNode station = model.Stations[0];
            Button[] links = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Where(button => ClipboardCopyBehavior.GetText(button) is string)
                .ToArray();
            Button system = Assert.Single(links);
            Assert.Equal(station.System, Assert.IsType<TextBlock>(system.Content).Text);
            IClipboard clipboard = Assert.IsType<IClipboard>(window.Clipboard, exactMatch: false);
            await clipboard.SetTextAsync("before");
            LeftClick(window, system);
            Assert.Equal(station.System, await clipboard.TryGetTextAsync());
            foreach (ColonizationWishListNode item in new[] { station.Children[0], station })
            {
                Button remove = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => button.Command == item.RemoveCommand);
                LeftClick(window, remove);
            }
            Assert.Equal(2, removals.Count);
            Assert.All(removals, items => Assert.Equal(100, Assert.Single(items).Quantity));
            Assert.False(interaction.HasPendingLivePositions);
            Assert.True(interaction.ToggleLiveOverlayInteraction());
            Assert.False(interaction.IsLiveInteractionEnabled);
        }
        finally
        {
            interaction.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void LeftClick(Window window, Control control)
    {
        Point point = control
            .TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void ApplyStops(
        ColonizationWishListViewModel model,
        int count,
        string displayName = "Steel",
        string systemName = "Sol"
    )
    {
        ColonizationMarketQuote[] quotes = Enumerable
            .Range(1, count)
            .Select(index => new ColonizationMarketQuote(
                systemName,
                $"Port {index}",
                index,
                "Orbis",
                3,
                index * 10,
                250,
                "steel",
                100,
                1000,
                DateTimeOffset.UtcNow,
                "Test"
            ))
            .ToArray();
        model.Apply(
            new ColonizationShoppingState
            {
                Quotes = quotes,
                WishList = quotes
                    .Select(quote => new ColonizationWishListItem(ColonizationShopping.MarketKey(quote), "steel", 100))
                    .ToArray(),
            },
            [new("steel", displayName, count * 100)]
        );
    }

    private sealed class EditorTracker : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot() => HostedOverlayTestHarness.AvailableGameWindow;

        public void Dispose() { }
    }

    private sealed class EditorPlatform : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public OverlayPreparationResult PreparePassiveWindow(Window window) => new(true, true, "Prepared");

        public OverlayInteractionResult SetInteractive(Window window, bool interactive) =>
            new(true, interactive, "Prepared");

        public void BeginMoveDrag(Window window, PointerPressedEventArgs eventArgs) { }

        public void Dispose() { }
    }
}
