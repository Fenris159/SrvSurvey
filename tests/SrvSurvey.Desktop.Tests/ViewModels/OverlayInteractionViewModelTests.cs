using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayInteractionViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-interaction-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DraggedPlacementPreservesAnchorsAndResolvesToNewPosition()
    {
        var gameBounds = new PixelRect(100, 200, 1200, 800);
        var overlaySize = new PixelSize(300, 120);
        var desiredPosition = new PixelPoint(475, 525);
        var anchors = new[]
        {
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Left,
                0,
                LegacyVerticalAnchor.Top,
                0,
                0.7),
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Center,
                0,
                LegacyVerticalAnchor.Middle,
                0,
                0.7),
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Right,
                0,
                LegacyVerticalAnchor.Bottom,
                0,
                0.7),
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Screen,
                0,
                LegacyVerticalAnchor.Screen,
                0,
                0.7),
        };

        foreach (var original in anchors)
        {
            var placement = OverlayInteractionViewModel.CreatePlacement(
                original,
                desiredPosition,
                overlaySize,
                gameBounds);
            var layout = new LegacyOverlayLayout(
                new Dictionary<string, LegacyOverlayPlacement>
                {
                    ["overlay"] = placement,
                },
                null,
                null);

            Assert.Equal(original.Horizontal, placement.Horizontal);
            Assert.Equal(original.Vertical, placement.Vertical);
            Assert.Equal(original.Opacity, placement.Opacity);
            Assert.Equal(
                desiredPosition,
                layout.GetPosition("overlay", gameBounds, overlaySize));
        }
    }

    [Fact]
    public void ModeCanArmWithoutEliteAndShowsTheSelectedCategory()
    {
        var platform = new FakeOverlayPlatform();
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var host = new FakeEditorHost();
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(GameWindowSnapshot.Unavailable),
            store,
            store.Load(),
            new OverlayWindowRegistry(),
            host);

        Assert.True(viewModel.Toggle());

        Assert.True(viewModel.IsEditing);
        Assert.True(host.IsOpen);
        Assert.Null(host.PreferredHostBounds);
        Assert.Equal(
            OverlayLayoutCategory.ExplorationAndNavigation,
            host.ShownCategories.Single());
        Assert.Contains("Editing Exploration", viewModel.ModeLabel);

        Assert.True(viewModel.Toggle());
        Assert.False(viewModel.IsEditing);
        Assert.False(host.IsOpen);
        Assert.Contains("cancelled", viewModel.StatusMessage);
    }

    [Fact]
    public void LiveShortcutDoesNotOpenFullEditorWhenNoLiveOverlayExists()
    {
        var platform = new FakeOverlayPlatform();
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var registry = new OverlayWindowRegistry();
        var host = new FakeEditorHost();
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                new PixelRect(100, 200, 1200, 800),
                IsVisible: true,
                IsForeground: true)),
            store,
            store.Load(),
            registry,
            host);

        Assert.False(viewModel.ToggleLiveOverlayInteraction());

        Assert.False(viewModel.IsLiveInteractionEnabled);
        Assert.False(viewModel.IsEditing);
        Assert.False(host.IsOpen);
        Assert.Empty(platform.InteractiveStates);
        Assert.Contains("No live overlays", viewModel.StatusMessage);
    }

    [Fact]
    public void CategorySelectionReplacesTheVisiblePreviewGroup()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var host = new FakeEditorHost();
        using var viewModel = new OverlayInteractionViewModel(
            new FakeOverlayPlatform(),
            new FakeGameWindowTracker(GameWindowSnapshot.Unavailable),
            store,
            store.Load(),
            new OverlayWindowRegistry(),
            host);

        Assert.True(viewModel.Begin());
        viewModel.SelectedCategory = viewModel.Categories.Single(category =>
            category.Category == OverlayLayoutCategory.SitesAndQuests);

        Assert.Equal(
            [
                OverlayLayoutCategory.ExplorationAndNavigation,
                OverlayLayoutCategory.SitesAndQuests,
            ],
            host.ShownCategories);
        Assert.Contains("Sites & quests", viewModel.StatusMessage);
    }

    [Fact]
    public void CancelDiscardsMovesAndSaveCommitsThemAtomically()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        const string originalFile = "{\"PlotJumpInfo\":\"center:0, top:8\"}";
        File.WriteAllText(path, originalFile);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var host = new FakeEditorHost();
        using var viewModel = new OverlayInteractionViewModel(
            new FakeOverlayPlatform(),
            new FakeGameWindowTracker(GameWindowSnapshot.Unavailable),
            store,
            activeLayout,
            new OverlayWindowRegistry(),
            host);
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotJumpInfo");
        var bounds = new PixelRect(100, 200, 1200, 800);

        Assert.True(viewModel.Begin());
        host.Move(
            definition.Name,
            new PixelPoint(420, 310),
            definition.PreviewSize,
            bounds);

        Assert.Equal(originalFile, File.ReadAllText(path));
        Assert.Equal(
            new PixelPoint(400, 208),
            activeLayout.GetPosition(
                definition.Name,
                bounds,
                definition.PreviewSize));

        viewModel.Cancel();

        Assert.Equal(originalFile, File.ReadAllText(path));
        Assert.Equal(
            new PixelPoint(400, 208),
            activeLayout.GetPosition(
                definition.Name,
                bounds,
                definition.PreviewSize));

        Assert.True(viewModel.Begin());
        host.Move(
            definition.Name,
            new PixelPoint(420, 310),
            definition.PreviewSize,
            bounds);
        viewModel.Save();

        Assert.False(viewModel.IsEditing);
        Assert.Equal(
            new PixelPoint(420, 310),
            activeLayout.GetPosition(
                definition.Name,
                bounds,
                definition.PreviewSize));
        Assert.Contains("Saved 1 overlay position", viewModel.StatusMessage);
        Assert.Contains("center:20, top:110", File.ReadAllText(path));
    }

    [Fact]
    public void OpacityPreviewCancelAndSaveShareThePositionEditSession()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var plottersPath = Path.Combine(temporaryDirectory, "plotters.json");
        var settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        File.WriteAllText(
            plottersPath,
            "{\"PlotJumpInfo\":\"center:0, top:8\"}");
        File.WriteAllText(settingsPath, "{\"plotterOpacity\":65}");
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var host = new FakeEditorHost();
        using var viewModel = new OverlayInteractionViewModel(
            new FakeOverlayPlatform(),
            new FakeGameWindowTracker(GameWindowSnapshot.Unavailable),
            store,
            activeLayout,
            new OverlayWindowRegistry(),
            host);

        Assert.True(viewModel.Begin());
        Assert.Equal(65, viewModel.GlobalOpacityPercent);
        viewModel.GlobalOpacityPercent = 40;
        host.ChangeOpacity("PlotJumpInfo", 0.8);

        Assert.Equal(40, host.LastDefaultOpacityPercent);
        Assert.Equal(80, host.LastEffectiveOpacityPercent["PlotJumpInfo"]);
        Assert.Equal("{\"plotterOpacity\":65}", File.ReadAllText(settingsPath));
        Assert.DoesNotContain(", 0.8", File.ReadAllText(plottersPath));

        viewModel.Cancel();

        Assert.Equal(0.65, activeLayout.DefaultOpacity);
        Assert.Null(activeLayout.Placements["PlotJumpInfo"].Opacity);

        Assert.True(viewModel.Begin());
        viewModel.GlobalOpacityPercent = 40;
        host.ChangeOpacity("PlotJumpInfo", 0.8);
        viewModel.Save();

        Assert.False(viewModel.IsEditing);
        Assert.Equal(0.4, activeLayout.DefaultOpacity);
        Assert.Equal(0.8, activeLayout.Placements["PlotJumpInfo"].Opacity);
        Assert.Contains(
            "Saved 1 overlay position/opacity override",
            viewModel.StatusMessage);
        Assert.Contains("\"plotterOpacity\": 40", File.ReadAllText(settingsPath));
        Assert.Contains(", 0.8", File.ReadAllText(plottersPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class FakeOverlayPlatform : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public List<bool> InteractiveStates { get; } = [];

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            return new OverlayPreparationResult(true, true, "Prepared");
        }

        public OverlayInteractionResult SetInteractive(
            Window window,
            bool interactive)
        {
            InteractiveStates.Add(interactive);
            return new OverlayInteractionResult(true, interactive, "Prepared");
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGameWindowTracker(GameWindowSnapshot snapshot)
        : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot()
        {
            return snapshot;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeEditorHost : IOverlayPositionEditorHost
    {
        public event EventHandler<OverlayPreviewMovedEventArgs>? PreviewMoved;

        public event EventHandler<OverlayPreviewOpacityChangedEventArgs>?
            PreviewOpacityChanged;

        public event EventHandler? Closed
        {
            add { }
            remove { }
        }

        public bool IsOpen { get; private set; }

        public PixelRect? PreferredHostBounds { get; private set; }

        public List<OverlayLayoutCategory> ShownCategories { get; } = [];

        public double LastDefaultOpacityPercent { get; private set; }

        public Dictionary<string, double> LastEffectiveOpacityPercent { get; } =
            new(StringComparer.Ordinal);

        public bool Open(
            OverlayInteractionViewModel viewModel,
            OverlayPositionEditSession session,
            OverlayLayoutCategory category,
            PixelRect? preferredHostBounds)
        {
            IsOpen = true;
            PreferredHostBounds = preferredHostBounds;
            ShownCategories.Add(category);
            return true;
        }

        public void ShowCategory(
            OverlayPositionEditSession session,
            OverlayLayoutCategory category)
        {
            ShownCategories.Add(category);
        }

        public void RefreshPreviewOpacities(OverlayPositionEditSession session)
        {
            LastDefaultOpacityPercent = session.DefaultOpacity * 100d;
            foreach (var definition in OverlayLayoutCatalog.Supported)
            {
                LastEffectiveOpacityPercent[definition.Name] =
                    session.GetOpacity(definition.Name) * 100d;
            }
        }

        public void Close(bool restoreRuntimeWindows = true)
        {
            IsOpen = false;
        }

        public void Move(
            string plotterName,
            PixelPoint position,
            PixelSize previewSize,
            PixelRect hostBounds)
        {
            PreviewMoved?.Invoke(
                this,
                new OverlayPreviewMovedEventArgs(
                    plotterName,
                    position,
                    previewSize,
                    hostBounds));
        }

        public void ChangeOpacity(string plotterName, double? opacityOverride)
        {
            PreviewOpacityChanged?.Invoke(
                this,
                new OverlayPreviewOpacityChangedEventArgs(
                    plotterName,
                    opacityOverride));
        }

        public void Dispose()
        {
        }
    }
}
