using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    [InlineData("PlotMiningMissions")]
    [InlineData("PlotMassacre")]
    public void EditorConstrainsMissionHeightToItsScaledGameViewport(string plotter)
    {
        using var fixture = new MissionFixture(plotter);
        fixture.SetMissionCount(20);
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(plotter));
        try
        {
            (preview.RuntimePresentation!.DataContext as IDisposable)?.Dispose();
            preview.RuntimePresentation.DataContext = fixture.Window.DataContext;
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(50), null, 1);
            preview.ConfigureViewport(480, 480);
            preview.Show();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Assert.InRange(preview.RuntimePresentation.Bounds.Height * 1.5, 100, 464);
        }
        finally
        {
            preview.Close();
        }
    }

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
        Assert.Equal(expanded.Width, wider.Width);
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
    [InlineData("PlotMiningMissions", OverlayGrowthDirection.Left)]
    [InlineData("PlotMiningMissions", OverlayGrowthDirection.Right)]
    [InlineData("PlotMiningMissions", OverlayGrowthDirection.Automatic)]
    [InlineData("PlotMassacre", OverlayGrowthDirection.Left)]
    [InlineData("PlotMassacre", OverlayGrowthDirection.Right)]
    [InlineData("PlotMassacre", OverlayGrowthDirection.Automatic)]
    public void MissionTextExpandsAndContractsWidthWithoutMovingTheGrowthEdge(
        string plotter,
        OverlayGrowthDirection direction
    )
    {
        using var fixture = new MissionFixture(plotter);
        fixture.SetMissionCount(1);
        LegacyOverlayLayout layout = CreateLayout(plotter, direction);
        using var host = new HostedOverlayTestHarness(new OverlayWindowRegistry(), overlayLayout: layout);
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
        int edge = fixture.Window.Position.X + (direction == OverlayGrowthDirection.Left ? initial.Width : 0);

        fixture.AddLongMission("Independent Industrial Workers of the Outer Colonies Cooperative");
        Settle(fixture.Window, host);
        PixelSize expanded = OverlayWindowMetrics.GetPixelSize(new(fixture.Window, plotter));
        Assert.True(expanded.Width > initial.Width + 100, $"{initial} -> {expanded}");
        Assert.Equal(edge, fixture.Window.Position.X + (direction == OverlayGrowthDirection.Left ? expanded.Width : 0));

        fixture.SetMissionCount(1);
        Settle(fixture.Window, host);
        PixelSize contracted = OverlayWindowMetrics.GetPixelSize(new(fixture.Window, plotter));
        Assert.Equal(initial, contracted);
        Assert.Equal(
            edge,
            fixture.Window.Position.X + (direction == OverlayGrowthDirection.Left ? contracted.Width : 0)
        );
    }

    [AvaloniaTheory]
    [InlineData("PlotMiningMissions")]
    [InlineData("PlotMassacre")]
    public void EditorMatchesContentWidthAndIgnoresOldFixedSizes(string plotter)
    {
        using var fixture = new MissionFixture(plotter);
        fixture.SetMissionCount(1);
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(plotter));
        try
        {
            if (preview.RuntimePresentation!.DataContext is IDisposable sample)
            {
                sample.Dispose();
            }
            preview.RuntimePresentation.DataContext = fixture.Window.DataContext;
            OverlayThemeResources.Apply(preview);
            OverlayThemeResources.Apply(
                fixture.Window,
                CreateLayout(plotter, OverlayGrowthDirection.Left),
                plotter,
                new()
            );
            preview.ConfigureSize(new(900, 900));
            preview.Show();
            fixture.Window.Show();
            using WriteableBitmap? initialFrame = preview.CaptureRenderedFrame();
            using WriteableBitmap? liveFrame = fixture.Window.CaptureRenderedFrame();
            double initial = preview.RuntimePresentation.Bounds.Width;
            Assert.Equal(
                OverlayThemeResources.GetPresentationRoot(fixture.Window)!.Bounds.Width,
                initial,
                precision: 1
            );
            Assert.False(preview.FindControl<Thumb>("PanelResizeThumb")!.IsVisible);
            fixture.AddLongMission("Independent Industrial Workers of the Outer Colonies Cooperative");
            using WriteableBitmap? expandedFrame = preview.CaptureRenderedFrame();
            using WriteableBitmap? expandedLive = fixture.Window.CaptureRenderedFrame();
            Assert.True(preview.RuntimePresentation.Bounds.Width > initial + 100);
            Assert.Equal(
                OverlayThemeResources.GetPresentationRoot(fixture.Window)!.Bounds.Width,
                preview.RuntimePresentation.Bounds.Width,
                precision: 1
            );
            expandedFrame!.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-{plotter}-content-width.png"),
                PngBitmapEncoderOptions.Default
            );
            double expandedHeight = preview.RuntimePresentation.Bounds.Height;
            preview.ConfigureViewport(320);
            using WriteableBitmap? wrapped = preview.CaptureRenderedFrame();
            Assert.InRange(preview.RuntimePresentation.Bounds.Width, initial, 304);
            Assert.True(preview.RuntimePresentation.Bounds.Height > expandedHeight);
            preview.ConfigureViewport(1280);
            fixture.SetMissionCount(1);
            preview.ConfigureSize(new(80, 900));
            using WriteableBitmap? contracted = preview.CaptureRenderedFrame();
            Assert.Equal(initial, preview.RuntimePresentation.Bounds.Width);
        }
        finally
        {
            preview.Close();
        }
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
        fixture.AddLongMission(string.Join(" ", Enumerable.Repeat("Independent Outer Colonies Cooperative", 20)));
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
            foreach ((int width, int height) in new[] { (1280, 720), (480, 480), (800, 800) })
            {
                OverlayWindowMetrics.PrepareForPlacement(window, layout, plotter, 1, height, width);
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap? frame = surface.CaptureRenderedFrame();
                PixelSize size = OverlayWindowMetrics.GetPixelSize(new(window, plotter));
                Assert.InRange(size.Height, 1, height - 15);
                Assert.InRange(size.Width, 1, width - 15);
                Assert.True(size.Width > width / 2, $"Long mission text did not use the available width: {size}");
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

        public void AddLongMission(string name)
        {
            if (combat is not null)
            {
                combat.LoadProfile(
                    null,
                    null,
                    true,
                    new CombatSnapshot(
                        Enumerable
                            .Range(1, Math.Max(1, combat.MassacreMissions.Count))
                            .Select(index => new MassacreMissionSnapshot(index, name, name, null, 6, 6))
                            .ToArray()
                    )
                );
                return;
            }
            mining.Feed(
                JsonSerializer.Serialize(
                    new
                    {
                        @event = "MissionAccepted",
                        Name = "Mission_Mining_Wing",
                        MissionID = 100,
                        Commodity = "$lowtemperaturediamond_name;",
                        Count = 1000,
                        DestinationSystem = name,
                        DestinationStation = "Huss Port",
                    }
                )
            );
        }

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
