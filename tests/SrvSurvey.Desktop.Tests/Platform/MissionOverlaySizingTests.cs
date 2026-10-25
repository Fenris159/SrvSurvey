using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MissionOverlaySizingTests
{
    [AvaloniaTheory]
    [InlineData("PlotMiningMissions", OverlayGrowthDirection.Left)]
    [InlineData("PlotMiningMissions", OverlayGrowthDirection.Right)]
    [InlineData("PlotMiningMissions", OverlayGrowthDirection.Automatic)]
    [InlineData("PlotMassacre", OverlayGrowthDirection.Left)]
    [InlineData("PlotMassacre", OverlayGrowthDirection.Right)]
    [InlineData("PlotMassacre", OverlayGrowthDirection.Automatic)]
    public void MissionListsExpandAndContractAtTheSavedGrowthEdge(string plotter, OverlayGrowthDirection direction)
    {
        using var fixture = new MissionFixture(plotter);
        fixture.SetMissionCount(1);
        LegacyOverlayLayout layout = CreateLayout(plotter, direction);
        var registry = new OverlayWindowRegistry();
        using var host = new HostedOverlayTestHarness(registry, overlayLayout: layout);
        host.GameWindow = host.GameWindow with { ClientBounds = new PixelRect(0, 0, 1280, 720) };
        using HostedOverlayWindow hosted = host.Session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                plotter,
                _ => fixture.Window,
                (bounds, size) => OverlayWindowPlacement.MiddleLeft(bounds, size)
            )
        );
        hosted.Reconcile(true);
        Settle(fixture.Window, host);
        PixelSize initial = OverlayWindowMetrics.GetPixelSize(new(fixture.Window, plotter));
        Assert.InRange(initial.Height, 1, 400);
        int edge = fixture.Window.Position.X + (direction == OverlayGrowthDirection.Left ? initial.Width : 0);

        fixture.SetMissionCount(20);
        Settle(fixture.Window, host);
        PixelSize expanded = OverlayWindowMetrics.GetPixelSize(new(fixture.Window, plotter));
        Assert.InRange(expanded.Height, initial.Height + 100, 720);
        layout.SetPlacement(plotter, layout.Placements[plotter] with { SizeOverride = new(440, 900) });
        Settle(fixture.Window, host);
        PixelSize wider = OverlayWindowMetrics.GetPixelSize(new(fixture.Window, plotter));
        Assert.Equal(edge, fixture.Window.Position.X + (direction == OverlayGrowthDirection.Left ? wider.Width : 0));

        fixture.SetMissionCount(1);
        Settle(fixture.Window, host);
        PixelSize shrunk = OverlayWindowMetrics.GetPixelSize(new(fixture.Window, plotter));
        Assert.True(shrunk.Height < expanded.Height - 100);
        Assert.Equal(edge, fixture.Window.Position.X + (direction == OverlayGrowthDirection.Left ? shrunk.Width : 0));
        layout.SetPlacement(plotter, layout.Placements[plotter] with { SizeOverride = new(360, 900) });
        Settle(fixture.Window, host);
        Assert.Equal(initial, OverlayWindowMetrics.GetPixelSize(new(fixture.Window, plotter)));
    }

    [AvaloniaTheory]
    [InlineData("PlotMiningMissions", false, 1.0, 0)]
    [InlineData("PlotMiningMissions", false, 1.5, 50)]
    [InlineData("PlotMiningMissions", true, 1.0, 0)]
    [InlineData("PlotMiningMissions", true, 1.5, 50)]
    [InlineData("PlotMassacre", false, 1.0, 0)]
    [InlineData("PlotMassacre", false, 1.5, 50)]
    [InlineData("PlotMassacre", true, 1.0, 0)]
    [InlineData("PlotMassacre", true, 1.5, 50)]
    public void MissionListsFitChangingViewportsAndScaledSeparateOrCombinedHosts(
        string plotter,
        bool combined,
        double scaling,
        int percent
    )
    {
        using var fixture = new MissionFixture(plotter);
        fixture.SetMissionCount(20);
        Window window = fixture.Window;
        var host = new CombinedOverlayWindow { Width = 1280 / scaling, Height = 720 / scaling };
        LegacyOverlayLayout layout = CreateLayout(plotter, OverlayGrowthDirection.Automatic, percent);
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
                window.Content = new Border { Width = 360, Height = 200 };
                using WriteableBitmap? detachedFrame = window.CaptureRenderedFrame();
                host.Add(new ContentControl { Content = content, DataContext = window.DataContext });
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
                Control panel = Assert.IsAssignableFrom<Control>(OverlayThemeResources.GetPresentationRoot(window));
                ScrollViewer scroller = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(scroller.Viewport.Height > 0);
                if (height == 480)
                {
                    Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
                    scroller.Offset = new Vector(0, scroller.Extent.Height);
                    using WriteableBitmap? scrolledFrame = surface.CaptureRenderedFrame();
                    Assert.True(scroller.Offset.Y > 0);
                }
            }
        }
        finally
        {
            host.Close();
            window.Close();
        }
    }

    private static LegacyOverlayLayout CreateLayout(
        string plotter,
        OverlayGrowthDirection direction,
        int percent = 0
    ) =>
        new(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [plotter] = new(
                    LegacyHorizontalAnchor.Left,
                    400,
                    LegacyVerticalAnchor.Top,
                    20,
                    null,
                    ScaleIndex: OverlayScaleCatalog.GetIndex(percent),
                    SizeOverride: new(360, 900),
                    GrowthDirection: direction
                ),
            },
            null,
            null
        );

    private static void Settle(Window window, HostedOverlayTestHarness host)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        host.Tick();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class MissionFixture : IDisposable
    {
        private readonly MiningMissionFixture mining = new();
        private readonly MiningMissionsOverlayViewModel? miningModel;
        private readonly CombatViewModel? combat;

        public MissionFixture(string plotter)
        {
            if (plotter == "PlotMiningMissions")
            {
                miningModel = new(mining.Workspace);
                Window = new MiningMissionsOverlayWindow(miningModel);
            }
            else
            {
                combat = new(
                    new CombatSettingsStore(Path.Combine(mining.Directory, "ui.json")),
                    new CommanderProfileStore(mining.Directory)
                );
                Window = new MassacreMissionsOverlayWindow(
                    new CombatOverlayViewModel(combat, OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows))
                );
            }
        }

        public Window Window { get; }

        public void SetMissionCount(int count)
        {
            if (combat is not null)
            {
                combat.LoadProfile(
                    null,
                    null,
                    true,
                    new CombatSnapshot(
                        Enumerable
                            .Range(1, count)
                            .Select(index => new MassacreMissionSnapshot(
                                index,
                                "Mission giver",
                                "Target faction " + index,
                                null,
                                6,
                                6
                            ))
                            .ToArray()
                    )
                );
                return;
            }
            for (int index = 1; index <= count; index++)
            {
                mining.Accept(index);
            }
            foreach (MiningMissionCardViewModel card in mining.Workspace.MissionCards)
            {
                card.IsTracked = card.Id <= count;
            }
        }

        public void Dispose()
        {
            Window.Close();
            miningModel?.Dispose();
            mining.Dispose();
        }
    }
}
