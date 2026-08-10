using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayPositionEditorHostTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-editor-host-tests-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public void PerOverlaySettingsAreLaidOutAboveTheToolbar()
    {
        var window = new OverlayPositionEditorWindow();
        try
        {
            var settings = window.FindControl<Border>("OverlaySettingsPanel");
            var toolbar = window.FindControl<Grid>("EditorToolbarPanel");

            Assert.NotNull(settings);
            Assert.NotNull(toolbar);
            Assert.True(Grid.GetRow(settings) < Grid.GetRow(toolbar));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EditorOpensPreviewsSuppressesRuntimeWindowsAndRestoresThem()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotJumpInfo\":\"center:0, top:8\"}");
        var platform = new FakeOverlayPlatform();
        var registry = new OverlayWindowRegistry();
        var runtimeWindow = new Window();
        registry.Register(runtimeWindow, "PlotJumpInfo");
        runtimeWindow.Show();
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                new PixelRect(100, 200, 1200, 800),
                IsVisible: true,
                IsForeground: true)),
            store,
            activeLayout,
            registry,
            host);

        Assert.True(viewModel.Begin());

        Assert.False(runtimeWindow.IsVisible);
        Assert.NotEmpty(platform.InteractiveWindows);
        var session = new OverlayPositionEditSession(activeLayout);
        host.RefreshPreviewOpacities(session);
        host.RefreshPreviewScales(session);
        host.RefreshPreviewPositions(session);
        Assert.Equal(
            OverlayLayoutCatalog.ForCategory(
                OverlayLayoutCategory.ExplorationAndNavigation).Count,
            host.SnapPreviewsToCenter(session));

        host.SetRuntimeOverlaysVisibleDuringEditing(visible: true);
        Assert.True(runtimeWindow.IsVisible);
        host.SetRuntimeOverlaysVisibleDuringEditing(visible: false);
        Assert.False(runtimeWindow.IsVisible);

        viewModel.Cancel();

        Assert.True(runtimeWindow.IsVisible);
        runtimeWindow.Close();
    }

    [AvaloniaFact]
    public void EditorAlignsPreviewBodyWithCurrentLivePanelGeometry()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotBioSystem\":\"left:300, bottom:100\"}");
        var platform = new FakeOverlayPlatform();
        var registry = new OverlayWindowRegistry();
        var runtimeWindow = new Window
        {
            Width = 500,
            Height = 600,
            Position = new PixelPoint(420, 310),
        };
        registry.Register(runtimeWindow, "PlotBioSystem");
        runtimeWindow.Show();
        var runtimeSize = OverlayWindowMetrics.GetPixelSize(
            registry.Snapshot().Single());
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                new PixelRect(100, 200, 1200, 800),
                IsVisible: true,
                IsForeground: true)),
            store,
            activeLayout,
            registry,
            host);
        viewModel.SelectedCategory = viewModel.Categories.Single(candidate =>
            candidate.Category == OverlayLayoutCategory.BiologyAndSurface);

        Assert.True(viewModel.Begin());

        var preview = host.PreviewWindows.Single(candidate =>
            candidate.Definition.Name == "PlotBioSystem");
        Assert.False(runtimeWindow.IsVisible);
        Assert.Equal(
            new PixelPoint(420, 310),
            preview.GetPanelScreenOrigin(preview.RenderScaling));
        Assert.True(preview.Position.Y < 310);
        Assert.NotEqual(
            runtimeSize.Height,
            preview.GetPanelMetrics(preview.RenderScaling).PanelSize.Height);

        OverlayPreviewMovedEventArgs? moved = null;
        host.PreviewMoved += (_, eventArgs) => moved = eventArgs;
        var metrics = preview.GetPanelMetrics(preview.RenderScaling);
        var movedPanelOrigin = new PixelPoint(510, 430);
        preview.Position = new PixelPoint(
            movedPanelOrigin.X - metrics.OriginOffset.X,
            movedPanelOrigin.Y - metrics.OriginOffset.Y);

        Assert.NotNull(moved);
        Assert.Equal(movedPanelOrigin, moved.Position);
        Assert.Equal(runtimeSize, moved.PreviewSize);

        viewModel.Cancel();

        Assert.True(runtimeWindow.IsVisible);
        runtimeWindow.Close();
    }

    [AvaloniaFact]
    public void EditorReanchorsExistingBiologyPositionWithoutMovingItsTopEdge()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotBioSystem\":\"left:300, bottom:100\"}");
        var platform = new FakeOverlayPlatform();
        var registry = new OverlayWindowRegistry();
        var runtimeWindow = new Window
        {
            Width = 500,
            Height = 600,
            Position = new PixelPoint(420, 310),
        };
        registry.Register(runtimeWindow, "PlotBioSystem");
        runtimeWindow.Show();
        var runtimeSize = OverlayWindowMetrics.GetPixelSize(
            registry.Snapshot().Single());
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                new PixelRect(100, 200, 1200, 800),
                IsVisible: true,
                IsForeground: true)),
            store,
            activeLayout,
            registry,
            host);
        viewModel.SelectedCategory = viewModel.Categories.Single(candidate =>
            candidate.Category == OverlayLayoutCategory.BiologyAndSurface);

        Assert.True(viewModel.Begin());

        var preview = host.PreviewWindows.Single(candidate =>
            candidate.Definition.Name == "PlotBioSystem");
        Assert.Equal(
            runtimeWindow.Position,
            preview.GetPanelScreenOrigin(preview.RenderScaling));

        viewModel.Save();

        var persisted = store.Load();
        var placement = persisted.Placements[preview.Definition.Name];
        Assert.Equal(LegacyVerticalAnchor.Top, placement.Vertical);
        Assert.Equal(
            runtimeWindow.Position,
            persisted.GetPosition(
                preview.Definition.Name,
                new PixelRect(100, 200, 1200, 800),
                runtimeSize));
        Assert.Equal(
            runtimeWindow.Position.Y,
            persisted.GetPosition(
                preview.Definition.Name,
                new PixelRect(100, 200, 1200, 800),
                new PixelSize(runtimeSize.Width, runtimeSize.Height + 120))!
                .Value.Y);
        runtimeWindow.Close();
    }

    [AvaloniaFact]
    public void MovingPreviewAcrossDpiBoundarySavesCurrentPanelOrigin()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var platform = new FakeOverlayPlatform();
        var registry = new OverlayWindowRegistry();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        var hostBounds = new PixelRect(100, 200, 1200, 800);
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                hostBounds,
                IsVisible: true,
                IsForeground: true)),
            store,
            activeLayout,
            registry,
            host);
        viewModel.SelectedCategory = viewModel.Categories.Single(candidate =>
            candidate.Category == OverlayLayoutCategory.BiologyAndSurface);

        Assert.True(viewModel.Begin());

        var preview = host.PreviewWindows.Single(candidate =>
            candidate.Definition.Name == "PlotBioSystem");
        preview.SetRenderScaling(2d);
        var currentMetrics = preview.GetPanelMetrics(preview.RenderScaling);
        var openingDisplayMetrics = preview.GetPanelMetrics(1d);
        var movedPanelOrigin = new PixelPoint(510, 430);

        Assert.Equal(2d, preview.RenderScaling);
        Assert.NotEqual(
            openingDisplayMetrics.OriginOffset,
            currentMetrics.OriginOffset);

        preview.Position = new PixelPoint(
            movedPanelOrigin.X - currentMetrics.OriginOffset.X,
            movedPanelOrigin.Y - currentMetrics.OriginOffset.Y);
        viewModel.Save();

        var persisted = store.Load();
        Assert.Equal(
            movedPanelOrigin,
            persisted.GetPosition(
                preview.Definition.Name,
                hostBounds,
                currentMetrics.PanelSize));
        Assert.Equal(
            LegacyVerticalAnchor.Bottom,
            preview.Definition.DefaultPlacement.Vertical);
        Assert.Equal(
            LegacyVerticalAnchor.Top,
            persisted.Placements[preview.Definition.Name].Vertical);
        Assert.Equal(
            movedPanelOrigin.Y,
            persisted.GetPosition(
                preview.Definition.Name,
                hostBounds,
                new PixelSize(
                    currentMetrics.PanelSize.Width,
                    currentMetrics.PanelSize.Height + 120))!.Value.Y);
    }

    [AvaloniaFact]
    public void SavedCompactPanelReopensAtItsNewPosition()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotPulse\":\"left:8, bottom:8\"}");
        var platform = new FakeOverlayPlatform();
        var registry = new OverlayWindowRegistry();
        var hostBounds = new PixelRect(100, 200, 1200, 800);
        var runtimeWindow = new Window
        {
            Width = 32,
            Height = 32,
            Position = new PixelPoint(108, 960),
        };
        registry.Register(runtimeWindow, "PlotPulse");
        runtimeWindow.Show();
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(new GameWindowSnapshot(
                (nint)1,
                42,
                hostBounds,
                IsVisible: true,
                IsForeground: true)),
            store,
            activeLayout,
            registry,
            host);
        viewModel.SelectedCategory = viewModel.Categories.Single(candidate =>
            candidate.Category == OverlayLayoutCategory.StatusAndUtilities);

        Assert.True(viewModel.Begin());
        var preview = host.PreviewWindows.Single(candidate =>
            candidate.Definition.Name == "PlotPulse");
        var metrics = preview.GetPanelMetrics(preview.RenderScaling);
        var movedPanelOrigin = new PixelPoint(510, 430);
        preview.Position = new PixelPoint(
            movedPanelOrigin.X - metrics.OriginOffset.X,
            movedPanelOrigin.Y - metrics.OriginOffset.Y);

        viewModel.Save();

        Assert.Equal(movedPanelOrigin, runtimeWindow.Position);
        Assert.Equal(
            movedPanelOrigin,
            store.Load().GetPosition(
                "PlotPulse",
                hostBounds,
                new PixelSize(32, 32)));

        Assert.True(viewModel.Begin());
        preview = host.PreviewWindows.Single(candidate =>
            candidate.Definition.Name == "PlotPulse");
        Assert.Equal(
            movedPanelOrigin,
            preview.GetPanelScreenOrigin(preview.RenderScaling));

        viewModel.Cancel();
        runtimeWindow.Close();
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class FakeOverlayPlatform
        : IOverlayPlatformService, IOverlayPresentationControl
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public List<Window> InteractiveWindows { get; } = [];

        public List<bool> SuppressionStates { get; } = [];

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            return new OverlayPreparationResult(true, true, "Prepared");
        }

        public OverlayInteractionResult SetInteractive(
            Window window,
            bool interactive)
        {
            if (interactive)
            {
                InteractiveWindows.Add(window);
            }

            return new OverlayInteractionResult(true, interactive, "Prepared");
        }

        public void SetRuntimeOverlaysSuppressed(bool suppressed)
        {
            SuppressionStates.Add(suppressed);
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
}
