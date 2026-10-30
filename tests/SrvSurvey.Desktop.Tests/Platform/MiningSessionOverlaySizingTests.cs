using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningSessionOverlaySizingTests
{
    [AvaloniaTheory]
    [InlineData(OverlayGrowthDirection.Left)]
    [InlineData(OverlayGrowthDirection.Right)]
    [InlineData(OverlayGrowthDirection.Automatic)]
    public void SessionFitsTheViewportAndShrinksWithoutChangingItsGrowthEdge(OverlayGrowthDirection direction)
    {
        const string plotter = "PlotMiningSession";
        using var fixture = new MiningMissionFixture();
        fixture.Feed($$"""{"timestamp":"{{fixture.Now:O}}","event":"LaunchDrone","Type":"Prospector"}""");
        var registry = new OverlayWindowRegistry();
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [plotter] = new(
                    LegacyHorizontalAnchor.Left,
                    400,
                    LegacyVerticalAnchor.Top,
                    20,
                    null,
                    GrowthDirection: direction,
                    SizeOverride: new(340, 900)
                ),
            },
            null,
            null
        );
        using var host = new HostedOverlayTestHarness(registry, overlayLayout: layout);
        host.GameWindow = host.GameWindow with { ClientBounds = new PixelRect(0, 0, 1280, 720) };
        using var coordinator = new MiningActivityOverlayCoordinator(
            fixture.Workspace,
            new FiregroupsWorkspaceViewModel(fixture.Directory),
            host.Session
        );
        MiningSessionOverlayWindow window = Assert.Single(host.PreparedWindows.OfType<MiningSessionOverlayWindow>());
        Settle(window, host);
        PixelSize initial = OverlayWindowMetrics.GetPixelSize(new(window, plotter));
        int edge = window.Position.X + (direction == OverlayGrowthDirection.Left ? initial.Width : 0);
        AddMinerals(fixture);
        Settle(window, host);
        PixelSize expanded = OverlayWindowMetrics.GetPixelSize(new(window, plotter));
        Assert.InRange(expanded.Height, initial.Height + 100, 720);
        ScrollViewer scroller = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Settle(window, host);
        Assert.True(scroller.Offset.Y > 0);

        layout.SetPlacement(plotter, layout.Placements[plotter] with { SizeOverride = new(440, 900) });
        Settle(window, host);
        PixelSize wider = OverlayWindowMetrics.GetPixelSize(new(window, plotter));
        Assert.Equal(edge, window.Position.X + (direction == OverlayGrowthDirection.Left ? wider.Width : 0));
        Assert.Equal(440, wider.Width);
        fixture.Workspace.Current!.Prospects.RemoveRange(1, 19);
        fixture.Workspace.Tick();
        Settle(window, host);
        PixelSize shrunk = OverlayWindowMetrics.GetPixelSize(new(window, plotter));
        Assert.True(shrunk.Height < expanded.Height - 100);
        Assert.Equal(edge, window.Position.X + (direction == OverlayGrowthDirection.Left ? shrunk.Width : 0));
        Assert.Equal(0, scroller.Offset.Y);
        layout.SetPlacement(plotter, layout.Placements[plotter] with { SizeOverride = new(340, 900) });
        Settle(window, host);
        PixelSize restored = OverlayWindowMetrics.GetPixelSize(new(window, plotter));
        Assert.Equal(initial.Width, restored.Width);
        Assert.Equal(edge, window.Position.X + (direction == OverlayGrowthDirection.Left ? restored.Width : 0));
    }

    [AvaloniaTheory]
    [InlineData(false, 1.0, -25)]
    [InlineData(false, 1.5, 50)]
    [InlineData(true, 1.0, 0)]
    [InlineData(true, 1.5, 50)]
    public void SessionViewportFollowsDisplayScaleOverlayScaleAndCombinedReparenting(
        bool combined,
        double scaling,
        int percent
    )
    {
        const string plotter = "PlotMiningSession";
        using var fixture = new MiningMissionFixture();
        fixture.Feed($$"""{"timestamp":"{{fixture.Now:O}}","event":"LaunchDrone","Type":"Prospector"}""");
        AddMinerals(fixture);
        using var model = new MiningSessionOverlayViewModel(fixture.Workspace);
        var window = new MiningSessionOverlayWindow(model);
        var host = new CombinedOverlayWindow { Width = 1280 / scaling, Height = 720 / scaling };
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [plotter] = new(
                    LegacyHorizontalAnchor.Right,
                    8,
                    LegacyVerticalAnchor.Top,
                    8,
                    null,
                    ScaleIndex: OverlayScaleCatalog.GetIndex(percent),
                    SizeOverride: new(340, 900)
                ),
            },
            null,
            null
        );
        try
        {
            window.SetRenderScaling(combined ? 1 : scaling);
            host.SetRenderScaling(scaling);
            OverlayThemeResources.Apply(window, layout, plotter, new OverlayWindowRegistry());
            window.Show();
            Window surface = window;
            if (combined)
            {
                using WriteableBitmap? initialFrame = window.CaptureRenderedFrame();
                Control content = Assert.IsType<Control>(window.Content, exactMatch: false);
                window.Content = new Border { Width = 340, Height = 200 };
                using WriteableBitmap? detachedFrame = window.CaptureRenderedFrame();
                host.Add(new ContentControl { Content = content, DataContext = model });
                OverlayThemeResources.Apply(host);
                host.Show();
                surface = host;
            }
            foreach (int height in new[] { 720, 480, 800 })
            {
                OverlayWindowMetrics.PrepareForPlacement(window, layout, plotter, 1, height);
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap? frame = surface.CaptureRenderedFrame();
                PixelSize size = OverlayWindowMetrics.GetPixelSize(new(window, plotter));
                Assert.InRange(size.Height, 1, height - 15);
                Control panel = Assert.IsType<MiningSessionOverlayPresentation>(
                    OverlayThemeResources.GetPresentationRoot(window)
                );
                ScrollViewer scroller = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(scroller.Viewport.Height > 0);
                if (height == 480)
                {
                    Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
                }
            }
        }
        finally
        {
            host.Close();
            window.Close();
        }
    }

    private static void AddMinerals(MiningMissionFixture fixture)
    {
        fixture.Workspace.SessionYieldLimit = 100;
        for (int index = 0; index < 20; index++)
        {
            fixture.Feed(
                $$"""{"timestamp":"{{fixture.Now.AddSeconds(index + 1):O}}","event":"ProspectedAsteroid","Materials":[{"Name":"Mineral {{index}}","Proportion":30}],"Remaining":100}"""
            );
        }
    }

    private static void Settle(Window window, HostedOverlayTestHarness host)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        host.Tick();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
