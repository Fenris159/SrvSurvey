using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
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
    public void LiveMovePublishesWorkingPlacementForRuntimeCoordinators()
    {
        var original = new LegacyOverlayPlacement(
            LegacyHorizontalAnchor.Center,
            0,
            LegacyVerticalAnchor.Top,
            8,
            0.7);
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = original,
            },
            null,
            null);
        var session = new OverlayPositionEditSession(active);
        var previewSession = new OverlayPositionEditSession(active);
        var gameBounds = new PixelRect(100, 200, 1200, 800);
        var overlaySize = new PixelSize(600, 100);
        var movedPosition = new PixelPoint(420, 310);

        Assert.True(OverlayInteractionViewModel.MoveLiveOverlay(
            session,
            active,
            "PlotJumpInfo",
            movedPosition,
            overlaySize,
            gameBounds,
            previewSession));

        Assert.Equal(
            movedPosition,
            active.GetPosition("PlotJumpInfo", gameBounds, overlaySize));
        Assert.Equal(original, session.GetOriginalPlacement("PlotJumpInfo"));
        Assert.Single(session.Changes);
        Assert.Equal(
            session.GetPlacement("PlotJumpInfo"),
            previewSession.GetPlacement("PlotJumpInfo"));
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

    [AvaloniaFact]
    public void LiveWindowDragPersistsAndIsReloadedByThePositionEditor()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        File.WriteAllText(
            path,
            "{\"PlotJumpInfo\":\"center:0, top:8\"}");
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var platform = new FakeOverlayPlatform();
        var registry = new OverlayWindowRegistry();
        var host = new FakeEditorHost();
        var gameBounds = new PixelRect(100, 200, 1200, 800);
        var window = new Window
        {
            Width = 600,
            Height = 100,
            Position = new PixelPoint(400, 208),
        };
        registry.Register(window, "PlotJumpInfo");
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                gameBounds,
                IsVisible: true,
                IsForeground: true)),
            store,
            activeLayout,
            registry,
            host);

        Assert.True(viewModel.ToggleLiveOverlayInteraction());
        window.Position = new PixelPoint(420, 310);
        Assert.Contains("Moved live overlay", viewModel.StatusMessage);

        Assert.True(viewModel.ToggleLiveOverlayInteraction());

        Assert.Equal([true, false], platform.InteractiveStates);
        Assert.Equal(
            new PixelPoint(420, 310),
            store.Load().GetPosition(
                "PlotJumpInfo",
                gameBounds,
                new PixelSize(600, 100)));
        Assert.Contains("Saved 1 live overlay position", viewModel.StatusMessage);

        Assert.True(viewModel.Begin());
        Assert.Equal(
            store.Load().Placements["PlotJumpInfo"],
            host.OpenedJumpInfoPlacement);
    }

    [AvaloniaFact]
    public void LiveDragAndOpenEditorStaySynchronizedAndDisposeRestoresChanges()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotJumpInfo\":\"center:0, top:8\"}");
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var registry = new OverlayWindowRegistry();
        var host = new FakeEditorHost();
        var gameBounds = new PixelRect(100, 200, 1200, 800);
        var window = new Window
        {
            Width = double.NaN,
            Height = double.NaN,
            Position = new PixelPoint(400, 208),
        };
        registry.Register(window, "PlotJumpInfo");
        var viewModel = new OverlayInteractionViewModel(
            new FakeOverlayPlatform(),
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                gameBounds,
                IsVisible: true,
                IsForeground: true)),
            store,
            activeLayout,
            registry,
            host);
        var original = activeLayout.Placements["PlotJumpInfo"];

        Assert.True(viewModel.Begin());
        Assert.True(viewModel.ToggleLiveOverlayInteraction());
        Assert.True(host.RuntimeOverlaysVisibleDuringEditing);

        var definition = OverlayLayoutCatalog.GetRequired("PlotJumpInfo");
        host.Move(
            definition.Name,
            new PixelPoint(460, 340),
            definition.PreviewSize,
            gameBounds);

        Assert.NotEqual(original, activeLayout.Placements["PlotJumpInfo"]);

        window.Position = new PixelPoint(500, 360);

        Assert.True(host.PositionRefreshCount > 0);
        Assert.Equal(
            activeLayout.Placements["PlotJumpInfo"],
            host.LastPositionRefreshPlacements["PlotJumpInfo"]);

        viewModel.Dispose();

        Assert.Equal(original, activeLayout.Placements["PlotJumpInfo"]);
        Assert.False(viewModel.IsLiveInteractionEnabled);
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
    public void OpeningEditorReloadsPersistedLayoutInsteadOfStaleMemory()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        File.WriteAllText(
            path,
            "{\"PlotJumpInfo\":\"center:0, top:8\"}");
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

        File.WriteAllText(
            path,
            "{\"PlotJumpInfo\":\"center:125, top:96\"}");

        Assert.True(viewModel.Begin());

        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Center,
                125,
                LegacyVerticalAnchor.Top,
                96,
                null),
            host.OpenedJumpInfoPlacement);
        Assert.Equal(
            host.OpenedJumpInfoPlacement,
            activeLayout.Placements["PlotJumpInfo"]);
    }

    [Fact]
    public void SnapToCenterCommandIsSavedWithTheOtherEditorChanges()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        File.WriteAllText(
            path,
            "{\"PlotJumpInfo\":\"left:315, top:470\","
                + "\"PlotGuardians\":\"right:40, bottom:60\"}");
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
        viewModel.SnapToCenterCommand.Execute(null);

        Assert.Equal(1, host.PositionRefreshCount);
        var centered = host.LastPositionRefreshPlacements["PlotJumpInfo"];
        Assert.Equal(LegacyHorizontalAnchor.Center, centered.Horizontal);
        Assert.Equal(0, centered.HorizontalOffset);
        Assert.Equal(LegacyVerticalAnchor.Top, centered.Vertical);
        Assert.Equal(350, centered.VerticalOffset);
        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Right,
                40,
                LegacyVerticalAnchor.Bottom,
                60,
                null),
            host.LastPositionRefreshPlacements["PlotGuardians"]);
        Assert.Contains("Snapped", viewModel.StatusMessage);

        viewModel.Save();

        var persisted = store.Load();
        Assert.Equal(centered, persisted.Placements["PlotJumpInfo"]);
        Assert.Equal(
            host.LastPositionRefreshPlacements["PlotGuardians"],
            persisted.Placements["PlotGuardians"]);
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
    public void ActiveScaleChangesRefreshOpenPositionPreviews()
    {
        Directory.CreateDirectory(temporaryDirectory);
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

        activeLayout.SetScaleIndex(19);

        Assert.Equal(1, host.ScaleRefreshCount);
        Assert.Equal(19, host.LastScaleIndex);
        Assert.Contains("selected scale", viewModel.StatusMessage);
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

    [Fact]
    public void PerOverlayScaleCanBeCancelledOrSavedIndependently()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var plottersPath = Path.Combine(temporaryDirectory, "plotters.json");
        File.WriteAllText(
            plottersPath,
            "{\"PlotJumpInfo\":\"center:0, top:8\"}");
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
        host.ChangeScale("PlotJumpInfo", 19);

        Assert.Equal(19, host.LastEffectiveScaleIndex["PlotJumpInfo"]);
        Assert.False(File.Exists(Path.Combine(
            temporaryDirectory,
            "overlay-scale-overrides.json")));

        viewModel.Cancel();
        Assert.Null(activeLayout.Placements["PlotJumpInfo"].ScaleIndex);

        Assert.True(viewModel.Begin());
        host.ChangeScale("PlotJumpInfo", 19);
        viewModel.Save();

        Assert.Equal(19, activeLayout.Placements["PlotJumpInfo"].ScaleIndex);
        Assert.Contains(
            "\"PlotJumpInfo\": 19",
            File.ReadAllText(Path.Combine(
                temporaryDirectory,
                "overlay-scale-overrides.json")));
    }

    [Fact]
    public void ToolbarPaneSettingsAreCommittedByTheTopCheckmarkCommand()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotJumpInfo\":\"center:0, top:8\"}");
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
        viewModel.OpenOverlaySettings("PlotJumpInfo");
        Assert.True(viewModel.IsOverlaySettingsOpen);
        viewModel.SelectedOverlayOpacityPercent = 34;
        viewModel.UseGlobalOverlayOpacity = false;
        var scaleOptions = OverlayScaleCatalog.Options
            .Where(option => option.AbsoluteScale is not null)
            .OrderBy(option => option.AbsoluteScale)
            .ToArray();
        viewModel.SelectedOverlayScaleOrdinal = Array.FindIndex(
            scaleOptions,
            option => option.Index == 19);
        viewModel.UseGlobalOverlayScale = false;

        viewModel.SaveCommand.Execute(null);

        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.IsOverlaySettingsOpen);
        Assert.False(host.IsOpen);
        Assert.Equal(0.34, activeLayout.Placements["PlotJumpInfo"].Opacity);
        Assert.Equal(19, activeLayout.Placements["PlotJumpInfo"].ScaleIndex);
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

        public event EventHandler? Closed
        {
            add { }
            remove { }
        }

        public bool IsOpen { get; private set; }

        public OverlayInteractionViewModel? ViewModel { get; private set; }

        public PixelRect? PreferredHostBounds { get; private set; }

        public List<OverlayLayoutCategory> ShownCategories { get; } = [];

        public double LastDefaultOpacityPercent { get; private set; }

        public Dictionary<string, double> LastEffectiveOpacityPercent { get; } =
            new(StringComparer.Ordinal);

        public int ScaleRefreshCount { get; private set; }

        public int LastScaleIndex { get; private set; }

        public int PositionRefreshCount { get; private set; }

        public Dictionary<string, LegacyOverlayPlacement>
            LastPositionRefreshPlacements
        { get; private set; } =
                new Dictionary<string, LegacyOverlayPlacement>(
                    StringComparer.Ordinal);

        public bool RuntimeOverlaysVisibleDuringEditing { get; private set; }

        public LegacyOverlayPlacement? OpenedJumpInfoPlacement { get; private set; }

        public Dictionary<string, int> LastEffectiveScaleIndex { get; } =
            new(StringComparer.Ordinal);

        public bool Open(
            OverlayInteractionViewModel viewModel,
            OverlayPositionEditSession session,
            OverlayLayoutCategory category,
            PixelRect? preferredHostBounds)
        {
            IsOpen = true;
            ViewModel = viewModel;
            PreferredHostBounds = preferredHostBounds;
            OpenedJumpInfoPlacement = session.GetPlacement("PlotJumpInfo");
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

        public void RefreshPreviewScales(OverlayPositionEditSession session)
        {
            ScaleRefreshCount++;
            LastScaleIndex = session.ScaleIndex;
            foreach (var definition in OverlayLayoutCatalog.Supported)
            {
                LastEffectiveScaleIndex[definition.Name] =
                    session.GetScaleIndex(definition.Name);
            }
        }

        public void RefreshPreviewPositions(OverlayPositionEditSession session)
        {
            PositionRefreshCount++;
            LastPositionRefreshPlacements = OverlayLayoutCatalog.Supported
                .ToDictionary(
                    definition => definition.Name,
                    definition => session.GetPlacement(definition.Name),
                    StringComparer.Ordinal);
        }

        public int SnapPreviewsToCenter(OverlayPositionEditSession session)
        {
            var category = ShownCategories[^1];
            var bounds = new PixelRect(100, 200, 1200, 800);
            var definitions = OverlayLayoutCatalog.ForCategory(category);
            foreach (var definition in definitions)
            {
                var size = definition.PreviewSize;
                var center = new PixelPoint(
                    bounds.X + ((bounds.Width - size.Width) / 2),
                    bounds.Y + ((bounds.Height - size.Height) / 2));
                session.MoveWithDefaultAnchors(
                    definition.Name,
                    center,
                    size,
                    bounds);
            }

            RefreshPreviewPositions(session);
            return definitions.Count;
        }

        public void SetRuntimeOverlaysVisibleDuringEditing(bool visible)
        {
            RuntimeOverlaysVisibleDuringEditing = visible;
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
            ViewModel!.OpenOverlaySettings(plotterName);
            if (opacityOverride is null)
            {
                ViewModel.UseGlobalOverlayOpacity = true;
                return;
            }

            ViewModel.SelectedOverlayOpacityPercent =
                opacityOverride.Value * 100d;
            ViewModel.UseGlobalOverlayOpacity = false;
        }

        public void ChangeScale(string plotterName, int? scaleOverride)
        {
            ViewModel!.OpenOverlaySettings(plotterName);
            if (scaleOverride is null)
            {
                ViewModel.UseGlobalOverlayScale = true;
                return;
            }

            var options = OverlayScaleCatalog.Options
                .Where(option => option.AbsoluteScale is not null)
                .OrderBy(option => option.AbsoluteScale)
                .ToArray();
            ViewModel.SelectedOverlayScaleOrdinal = Array.FindIndex(
                options,
                option => option.Index == scaleOverride.Value);
            ViewModel.UseGlobalOverlayScale = false;
        }

        public void Dispose()
        {
        }
    }
}
