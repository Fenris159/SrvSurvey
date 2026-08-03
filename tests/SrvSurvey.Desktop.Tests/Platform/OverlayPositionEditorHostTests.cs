using Avalonia;
using Avalonia.Controls;
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
