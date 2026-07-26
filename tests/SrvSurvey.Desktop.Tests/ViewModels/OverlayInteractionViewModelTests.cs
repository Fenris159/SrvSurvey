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
    public void ModeCanArmBeforeAnyOverlayOpens()
    {
        var platform = new FakeOverlayPlatform();
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        using var viewModel = new OverlayInteractionViewModel(
            platform,
            new FakeGameWindowTracker(),
            store,
            store.Load(),
            new OverlayWindowRegistry());

        Assert.True(viewModel.Toggle());

        Assert.True(viewModel.IsEditing);
        Assert.Contains("Newly opened overlays", viewModel.StatusMessage);

        Assert.True(viewModel.Toggle());
        Assert.False(viewModel.IsEditing);
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

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            return new OverlayPreparationResult(true, true, "Prepared");
        }

        public OverlayInteractionResult SetInteractive(
            Window window,
            bool interactive)
        {
            return new OverlayInteractionResult(true, interactive, "Prepared");
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGameWindowTracker : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot()
        {
            return new GameWindowSnapshot(
                (nint)1,
                1,
                new PixelRect(100, 200, 1200, 800),
                true,
                true);
        }

        public void Dispose()
        {
        }
    }
}
