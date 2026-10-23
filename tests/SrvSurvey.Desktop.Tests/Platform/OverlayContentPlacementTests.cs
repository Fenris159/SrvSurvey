using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

/// <summary>Exercises retained growth corners, monitor bounds, and saved geometry across resize and reopen.</summary>
[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayContentPlacementTests
{
    /// <summary>Size changes retain the selected top corner and shrinking returns to the original position.</summary>
    [Theory]
    [InlineData(30, 30)]
    [InlineData(850, 650)]
    [InlineData(450, 250)]
    public void GrowthAndShrinkKeepTheChosenTopCorner(int x, int grownX)
    {
        var placement = new OverlayContentPlacement();
        var monitor = new PixelRect(0, 0, 1000, 800);
        var initial = new PixelPoint(x, 40);
        Assert.Equal(initial, placement.Resolve(initial, new PixelSize(100, 80), monitor, monitor, null));
        Assert.Equal(
            new PixelPoint(grownX, 40),
            placement.Resolve(new PixelPoint(x, 5), new PixelSize(300, 200), monitor, monitor, null)
        );
        Assert.Equal(initial, placement.Resolve(initial, new PixelSize(100, 80), monitor, monitor, null));
    }

    /// <summary>Content crossing the monitor midpoint cannot reverse a latched growth direction.</summary>
    [Fact]
    public void CrossingTheMidpointDuringGrowthDoesNotFlipTheCorner()
    {
        var placement = new OverlayContentPlacement();
        var monitor = new PixelRect(-1200, 300, 1200, 900);
        var initial = new PixelPoint(-800, 330);
        placement.Resolve(initial, new PixelSize(100, 80), monitor, monitor, null);
        Assert.Equal(initial, placement.Resolve(initial, new PixelSize(800, 200), monitor, monitor, null));
        Assert.Equal(initial, placement.Resolve(initial, new PixelSize(100, 80), monitor, monitor, null));
    }

    /// <summary>Raw legacy size-dependent coordinates cannot switch monitors during growth before the retained corner is projected.</summary>
    [Fact]
    public void GrowthKeepsItsOriginalMonitorUntilRepositioned()
    {
        var growth = new OverlayContentPlacement();
        var monitor = new PixelRect(0, 0, 1000, 800);
        var neighbor = new PixelRect(1000, 0, 1000, 800);
        var position = new PixelPoint(850, 40);
        growth.Resolve(position, new PixelSize(100, 80), monitor, monitor, null);
        Assert.Equal(
            new PixelPoint(150, 40),
            growth.Resolve(position, new PixelSize(800, 80), neighbor, monitor, null)
        );
        Assert.Equal(position, growth.Resolve(position, new PixelSize(100, 80), monitor, monitor, null));
    }

    /// <summary>Manual growth preferences take priority over automatic monitor-side selection.</summary>
    [Theory]
    [InlineData(50, OverlayGrowthDirection.Left, 0)]
    [InlineData(800, OverlayGrowthDirection.Right, 800)]
    public void ForcedGrowthWinsOverMonitorHalfAndSurvivesDragging(
        int x,
        OverlayGrowthDirection direction,
        int expectedX
    )
    {
        var growth = new OverlayContentPlacement();
        var bounds = new PixelRect(0, 0, 1000, 800);
        var placement = new LegacyOverlayPlacement(
            LegacyHorizontalAnchor.Left,
            x,
            LegacyVerticalAnchor.Top,
            20,
            1,
            GrowthDirection: direction
        );
        growth.Resolve(new PixelPoint(x, 20), new PixelSize(100, 80), bounds, bounds, placement);
        Assert.Equal(
            new PixelPoint(expectedX, 20),
            growth.Resolve(new PixelPoint(x, 20), new PixelSize(150, 100), bounds, bounds, placement)
        );
        var session = new OverlayPositionEditSession(LegacyOverlayLayout.Empty);
        session.SetPlacement("PlotJumpInfo", placement);
        session.Move("PlotJumpInfo", new PixelPoint(400, 30), new PixelSize(100, 80), bounds);
        Assert.Equal(direction, session.GetPlacement("PlotJumpInfo").GrowthDirection);
        Assert.Equal(
            direction == OverlayGrowthDirection.Left ? LegacyHorizontalAnchor.Right : LegacyHorizontalAnchor.Left,
            session.GetPlacement("PlotJumpInfo").Horizontal
        );
    }

    /// <summary>Only placement or reference-frame changes select a new corner; appearance edits keep it.</summary>
    [Fact]
    public void PositionChangesAndFrameChangesReanchorButAppearanceChangesDoNot()
    {
        var growth = new OverlayContentPlacement();
        var monitor = new PixelRect(0, 0, 1000, 800);
        var placement = new LegacyOverlayPlacement(
            LegacyHorizontalAnchor.Left,
            850,
            LegacyVerticalAnchor.Bottom,
            40,
            1
        );
        growth.Resolve(new PixelPoint(850, 40), new PixelSize(100, 80), monitor, monitor, placement);
        Assert.Equal(
            new PixelPoint(650, 40),
            growth.Resolve(
                new PixelPoint(850, 500),
                new PixelSize(300, 200),
                monitor,
                monitor,
                placement with
                {
                    Opacity = 0.5,
                    ScaleIndex = 1,
                    SizeOverride = new OverlayPanelSize(300, 200),
                }
            )
        );
        placement = placement with
        {
            HorizontalOffset = 40,
            Vertical = LegacyVerticalAnchor.Top,
            PositionReference = new(1000, 800),
        };
        var moved = new PixelPoint(40, 90);
        Assert.Equal(moved, growth.Resolve(moved, new PixelSize(100, 80), monitor, monitor, placement));
        Assert.Equal(moved, growth.Resolve(moved, new PixelSize(500, 200), monitor, monitor, placement));
        var changedFrame = new PixelRect(100, 50, 800, 600);
        Assert.Equal(
            new PixelPoint(200, 60),
            growth.Resolve(new PixelPoint(200, 60), new PixelSize(100, 80), monitor, changedFrame, placement)
        );
        growth.Reset();
        Assert.Equal(
            new PixelPoint(800, 80),
            growth.Resolve(new PixelPoint(800, 80), new PixelSize(100, 80), monitor, changedFrame, placement)
        );
    }

    /// <summary>Exhausted horizontal clearance clamps the panel without losing its preferred origin.</summary>
    [Fact]
    public void GrowthClampsToTheMonitorWhenTheAvailableSpaceIsExhausted()
    {
        var growth = new OverlayContentPlacement();
        var monitor = new PixelRect(-1000, 0, 1000, 800);
        var position = new PixelPoint(-900, 20);
        growth.Resolve(position, new PixelSize(100, 80), monitor, monitor, null);
        Assert.Equal(
            new PixelPoint(-1000, 20),
            growth.Resolve(position, new PixelSize(1000, 80), monitor, monitor, null)
        );
        Assert.Equal(
            new PixelPoint(-1000, 20),
            growth.Resolve(position, new PixelSize(1500, 80), monitor, monitor, null)
        );
        Assert.Equal(position, growth.Resolve(position, new PixelSize(100, 80), monitor, monitor, null));
    }

    /// <summary>Saving expanded geometry preserves the originally selected side and appearance.</summary>
    [Fact]
    public void SavingGeometryKeepsTheOriginalSideAfterGrowingAcrossTheMidpoint()
    {
        var growth = new OverlayContentPlacement();
        var monitor = new PixelRect(0, 0, 1000, 800);
        var appearance = new LegacyOverlayPlacement(
            LegacyHorizontalAnchor.Center,
            0,
            LegacyVerticalAnchor.Middle,
            20,
            0.7
        );
        var origin = new PixelPoint(400, 50);
        growth.Resolve(origin, new PixelSize(100, 80), monitor, monitor, appearance);
        var size = new PixelSize(600, 200);
        LegacyOverlayPlacement saved = growth.CapturePlacement(appearance, size);
        Assert.Equal(LegacyHorizontalAnchor.Left, saved.Horizontal);
        Assert.Equal(LegacyVerticalAnchor.Top, saved.Vertical);
        Assert.Equal(0.7, saved.Opacity);
        Assert.Equal(origin, growth.Resolve(origin, size, monitor, monitor, saved));
        var session = new OverlayPositionEditSession(LegacyOverlayLayout.Empty);
        session.SetPlacement("PlotJumpInfo", saved);
        Assert.Equal(origin, session.GetPosition("PlotJumpInfo", monitor, size));
        Assert.Equal(origin, growth.Resolve(origin, new PixelSize(100, 80), monitor, monitor, saved));
    }

    /// <summary>Reopening retains the saved automatic corner even after content crosses the monitor midpoint.</summary>
    [Fact]
    public void ReopeningKeepsSavedAutomaticGrowthCorner()
    {
        var bounds = new PixelRect(0, 0, 1000, 800);
        var origin = new PixelPoint(400, 50);
        var appearance = new LegacyOverlayPlacement(LegacyHorizontalAnchor.Left, 400, LegacyVerticalAnchor.Top, 50, 1);
        var growth = new OverlayContentPlacement();
        growth.Resolve(origin, new PixelSize(100, 80), bounds, bounds, appearance);
        LegacyOverlayPlacement saved = growth.CapturePlacement(appearance, new PixelSize(600, 200));
        var reopened = new OverlayContentPlacement();
        reopened.Resolve(origin, new PixelSize(600, 200), bounds, bounds, saved);
        Assert.Equal(origin, reopened.Resolve(origin, new PixelSize(100, 80), bounds, bounds, saved));
    }

    /// <summary>Content grows upward when lower clearance runs out, then returns to the original top on shrink.</summary>
    [Fact]
    public void VerticalGrowthRemainsVisibleAndShrinkRestoresTheOrigin()
    {
        var bounds = new PixelRect(-1000, 300, 1000, 800);
        var origin = new PixelPoint(-900, 950);
        var growth = new OverlayContentPlacement();
        growth.Resolve(origin, new PixelSize(100, 100), bounds, bounds, null);
        Assert.Equal(new PixelPoint(-900, 700), growth.Resolve(origin, new PixelSize(100, 400), bounds, bounds, null));
        Assert.Equal(origin, growth.Resolve(origin, new PixelSize(100, 100), bounds, bounds, null));
    }

    /// <summary>Dragging selects growth from the physical monitor rather than a smaller game rectangle.</summary>
    [Fact]
    public void DragUsesTheMonitorMidpointInsteadOfTheWindowedGameMidpoint()
    {
        var session = new OverlayPositionEditSession(LegacyOverlayLayout.Empty);
        var monitor = new PixelRect(0, 0, 2000, 1000);
        var game = new PixelRect(100, 100, 800, 600);
        var position = new PixelPoint(700, 200);
        session.Move("PlotJumpInfo", position, new PixelSize(100, 80), game, monitor);
        Assert.Equal(LegacyHorizontalAnchor.Left, session.GetPlacement("PlotJumpInfo").Horizontal);
        Assert.Equal(position, session.GetPosition("PlotJumpInfo", game, new PixelSize(300, 200)));
        session.Move("PlotJumpInfo", new PixelPoint(1500, 200), new PixelSize(100, 80), game, monitor);
        Assert.Equal(LegacyHorizontalAnchor.Right, session.GetPlacement("PlotJumpInfo").Horizontal);
        Assert.Equal(new PixelPoint(1300, 200), session.GetPosition("PlotJumpInfo", game, new PixelSize(300, 200)));
    }

    /// <summary>Monitor selection follows panel overlap and safely handles absent matching screens.</summary>
    [AvaloniaFact]
    public void MonitorLookupUsesPanelOverlapAndFallsBackForUnavailableScreens()
    {
        var window = new Window();
        Assert.Equal(
            window.Screens.Primary!.Bounds,
            OverlayContentPlacement.GetMonitorBounds(
                window,
                new PixelPoint(10, 10),
                new PixelSize(100, 80),
                new PixelRect(20, 20, 500, 300)
            )
        );
        var fallback = new PixelRect(-10000, 0, 1000, 800);
        Assert.Equal(
            fallback,
            OverlayContentPlacement.GetMonitorBounds(window, fallback.Position, new PixelSize(100, 80), fallback)
        );
    }
}
