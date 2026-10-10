using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayPlacementInteractionTests : IDisposable
{
    private static readonly PixelRect GameBounds = new(100, 200, 1200, 800);

    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-placement-tests-{Guid.NewGuid():N}"
    );

    [AvaloniaFact]
    public void PanelMovePublishesWorkingPlacementToTheActiveLayout()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        LegacyOverlayLayout activeLayout = store.Load();
        LegacyOverlayPlacement original = activeLayout.Placements["PlotJumpInfo"];
        var registry = new OverlayWindowRegistry();
        var window = new Window { Width = 600, Height = 100 };
        registry.Register(window, "PlotJumpInfo");
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform(),
            store,
            activeLayout,
            registry
        );
        var moves = new List<OverlayPlacementMovedEventArgs>();
        interaction.PlacementMoved += (_, eventArgs) => moves.Add(eventArgs);

        Assert.True(interaction.Attach(registry.Snapshot().Single()));
        window.Position = new PixelPoint(420, 310);

        Assert.Equal(new PixelPoint(420, 310), activeLayout.GetPosition("PlotJumpInfo", GameBounds, new(600, 100)));
        OverlayPlacementMovedEventArgs moved = Assert.Single(moves);
        Assert.Equal("PlotJumpInfo", moved.PlotterName);
        Assert.Equal(moved.Placement, Assert.Single(interaction.Changes).Value);
        Assert.Equal(moved.Placement, activeLayout.Placements["PlotJumpInfo"]);
        Assert.Equal(original, interaction.Cancel()["PlotJumpInfo"]);
    }

    [AvaloniaTheory]
    [InlineData("PlotBioSystem")]
    [InlineData("PlotFloatie")]
    [InlineData("PlotGrounded")]
    [InlineData("PlotGuardians")]
    [InlineData("PlotHumanSite")]
    [InlineData("PlotPriorScans")]
    [InlineData("PlotRamTah")]
    [InlineData("PlotStationInfo")]
    [InlineData("PlotSysStatus")]
    public void PanelMoveKeepsDynamicPanelTopEdgeStableAcrossContentHeights(string plotterName)
    {
        LegacyOverlayPlacement original = OverlayLayoutCatalog.GetRequired(plotterName).DefaultPlacement with
        {
            Opacity = 0.7,
        };
        var activeLayout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement> { [plotterName] = original },
            null,
            null
        );
        var registry = new OverlayWindowRegistry();
        var window = new Window { Width = 220, Height = 140 };
        registry.Register(window, plotterName);
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform(),
            new LegacyOverlayLayoutStore(temporaryDirectory),
            activeLayout,
            registry
        );
        Assert.True(interaction.Attach(registry.Snapshot().Single()));
        var liveSize = new PixelSize(220, 140);
        var movedPosition = new PixelPoint(420, 310);

        window.Position = movedPosition;

        LegacyOverlayPlacement placement = interaction.Changes[plotterName];
        Assert.Equal(LegacyVerticalAnchor.Top, placement.Vertical);
        Assert.Equal(movedPosition, activeLayout.GetPosition(plotterName, GameBounds, liveSize));
        Assert.Equal(
            movedPosition.Y,
            activeLayout
                .GetPosition(plotterName, GameBounds, new PixelSize(liveSize.Width, liveSize.Height + 120))!
                .Value.Y
        );
    }

    [AvaloniaFact]
    public void PointerDragMovesThePanelByThePointerDelta()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        LegacyOverlayLayout activeLayout = store.Load();
        var registry = new OverlayWindowRegistry();
        Window window = CreatePanel(registry, "PlotJumpInfo");
        var platform = new RecordingPlatform { UseManagedMoveDrag = true };
        OverlayPlacementInteraction interaction = CreateInteraction(platform, store, activeLayout, registry);
        try
        {
            ShowAt(window, new PixelPoint(400, 300));
            Assert.True(interaction.Attach(registry.Snapshot().Single()));

            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(55, 70), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(55, 70), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, platform.MoveDragStarts);
            Assert.Equal(new PixelPoint(435, 345), window.Position);
            PixelSize size = OverlayWindowMetrics.GetPixelSize(registry.Snapshot().Single());
            Assert.Equal(new PixelPoint(435, 345), activeLayout.GetPosition("PlotJumpInfo", GameBounds, size));
            Assert.Single(interaction.Changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void PlatformCapabilityChoosesManagedOrPlatformDragForMonitorLock(
        bool usesManagedDragForMonitorLock,
        bool lockToMonitor,
        bool expectsPlatformDrag
    )
    {
        var registry = new OverlayWindowRegistry();
        Window window = CreatePanel(registry, "PlotJumpInfo");
        var platform = new RecordingPlatform(usesManagedDragForMonitorLock);
        try
        {
            ShowAt(window, new PixelPoint(400, 300));
            OverlayBehaviorViewModel behavior = CreateBehavior(window, lockToMonitor);
            OverlayPlacementInteraction interaction = CreateInteraction(
                platform,
                new LegacyOverlayLayoutStore(temporaryDirectory),
                new LegacyOverlayLayoutStore(temporaryDirectory).Load(),
                registry,
                behavior
            );
            Assert.True(interaction.Attach(registry.Snapshot().Single()));

            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(30, 45), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(30, 45), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(expectsPlatformDrag ? 1 : 0, platform.MoveDragStarts);
            Assert.Equal(expectsPlatformDrag ? new PixelPoint(400, 300) : new PixelPoint(410, 320), window.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MonitorLockClampsTheDragAndCompletingDragsSavesTheLastAcceptedMove()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        LegacyOverlayLayout activeLayout = store.Load();
        var registry = new OverlayWindowRegistry();
        Window window = CreatePanel(registry, "PlotJumpInfo");
        var platform = new RecordingPlatform(usesManagedDragForMonitorLock: true);
        try
        {
            ShowAt(window, new PixelPoint(400, 300));
            OverlayBehaviorViewModel behavior = CreateBehavior(window, lockToMonitor: true);
            MainWindowMonitor monitor = MainWindowPlacement.DescribeScreens(window.Screens.All)[0];
            OverlayPlacementInteraction interaction = CreateInteraction(
                platform,
                store,
                activeLayout,
                registry,
                behavior
            );
            RegisteredOverlayWindow registered = registry.Snapshot().Single();
            Assert.True(interaction.Attach(registered));
            PixelSize panelSize = OverlayWindowMetrics.GetPixelSize(registered);
            var expected = new PixelPoint(
                monitor.Bounds.Right - panelSize.Width,
                monitor.Bounds.Bottom - panelSize.Height
            );

            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(6020, 6025), RawInputModifiers.LeftMouseButton);
            interaction.CompleteDrags();
            window.MouseMove(new Point(7020, 7025), RawInputModifiers.LeftMouseButton);

            Assert.Equal(0, platform.MoveDragStarts);
            Assert.Equal(expected, window.Position);
            LegacyOverlayLayoutSaveResult result = interaction.Save();
            Assert.Equal(1, result.UpdatedPlacementCount);
            Assert.Equal(expected, store.Load().GetPosition("PlotJumpInfo", GameBounds, panelSize));

            interaction.Detach(window);
            Assert.Null(OverlayDragPolicy.GetOptions(window).ConstrainPosition);
            Assert.Empty(interaction.Panels);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SaveWritesTheStoreAndContinuesTheEditFromTheSavedLayout()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        LegacyOverlayLayout activeLayout = store.Load();
        var registry = new OverlayWindowRegistry();
        var window = new Window { Width = 600, Height = 100 };
        registry.Register(window, "PlotJumpInfo");
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform(),
            store,
            activeLayout,
            registry
        );
        Assert.True(interaction.Attach(registry.Snapshot().Single()));
        window.Position = new PixelPoint(420, 310);

        LegacyOverlayLayoutSaveResult result = interaction.Save();

        Assert.Equal(1, result.UpdatedPlacementCount);
        LegacyOverlayLayout saved = store.Load();
        Assert.Equal(new PixelPoint(420, 310), saved.GetPosition("PlotJumpInfo", GameBounds, new(600, 100)));
        Assert.Equal(saved.Placements["PlotJumpInfo"], activeLayout.Placements["PlotJumpInfo"]);
        Assert.Empty(interaction.Changes);
        Assert.Empty(interaction.Cancel());
        Assert.Equal(saved.Placements["PlotJumpInfo"], activeLayout.Placements["PlotJumpInfo"]);
    }

    [AvaloniaFact]
    public void SaveCompletesPendingDragBeforePersistingAndStopsTheGesture()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        LegacyOverlayLayout activeLayout = store.Load();
        var registry = new OverlayWindowRegistry();
        Window window = CreatePanel(registry, "PlotJumpInfo");
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform { UseManagedMoveDrag = true },
            store,
            activeLayout,
            registry
        );
        try
        {
            ShowAt(window, new PixelPoint(400, 300));
            Assert.True(interaction.Attach(registry.Snapshot().Single()));
            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(60, 85), RawInputModifiers.LeftMouseButton);

            LegacyOverlayLayoutSaveResult result = interaction.Save();
            var expected = new PixelPoint(440, 360);
            PixelSize size = OverlayWindowMetrics.GetPixelSize(registry.Snapshot().Single());

            Assert.Equal(1, result.UpdatedPlacementCount);
            Assert.Equal(expected, window.Position);
            Assert.Equal(expected, store.Load().GetPosition("PlotJumpInfo", GameBounds, size));
            Assert.Empty(interaction.Changes);
            window.MouseMove(new Point(90, 115), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(90, 115), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(expected, window.Position);
            Assert.Empty(interaction.Changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CancelCompletesPendingDragBeforeRestoringAndStopsTheGesture()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        string savedText = File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json"));
        LegacyOverlayLayout activeLayout = store.Load();
        LegacyOverlayPlacement original = activeLayout.Placements["PlotJumpInfo"];
        var registry = new OverlayWindowRegistry();
        Window window = CreatePanel(registry, "PlotJumpInfo");
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform { UseManagedMoveDrag = true },
            store,
            activeLayout,
            registry
        );
        try
        {
            ShowAt(window, new PixelPoint(400, 300));
            Assert.True(interaction.Attach(registry.Snapshot().Single()));
            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(60, 85), RawInputModifiers.LeftMouseButton);

            IReadOnlyDictionary<string, LegacyOverlayPlacement> restored = interaction.Cancel();

            Assert.Equal(original, Assert.Single(restored).Value);
            Assert.Equal(original, activeLayout.Placements["PlotJumpInfo"]);
            Assert.Empty(interaction.Changes);
            window.MouseMove(new Point(90, 115), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(90, 115), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(original, activeLayout.Placements["PlotJumpInfo"]);
            Assert.Empty(interaction.Changes);
            Assert.Equal(savedText, File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json")));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CancelRestoresOriginalPlacementsWithoutWritingTheStore()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        string savedText = File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json"));
        LegacyOverlayLayout activeLayout = store.Load();
        LegacyOverlayPlacement original = activeLayout.Placements["PlotJumpInfo"];
        var registry = new OverlayWindowRegistry();
        var window = new Window { Width = 600, Height = 100 };
        registry.Register(window, "PlotJumpInfo");
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform(),
            store,
            activeLayout,
            registry
        );
        Assert.True(interaction.Attach(registry.Snapshot().Single()));
        window.Position = new PixelPoint(420, 310);
        Assert.NotEqual(original, activeLayout.Placements["PlotJumpInfo"]);

        IReadOnlyDictionary<string, LegacyOverlayPlacement> restored = interaction.Cancel();

        Assert.Equal(original, Assert.Single(restored).Value);
        Assert.Equal(original, activeLayout.Placements["PlotJumpInfo"]);
        Assert.Empty(interaction.Changes);
        Assert.Equal(savedText, File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json")));
    }

    [AvaloniaFact]
    public void RelatedChildWindowCannotOverwriteItsOwnersPlacement()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotGuardians":"right:20, bottom:20"}""");
        LegacyOverlayLayout activeLayout = store.Load();
        var registry = new OverlayWindowRegistry();
        var owner = new Window
        {
            Width = 600,
            Height = 600,
            Position = new PixelPoint(680, 380),
        };
        var child = new Window
        {
            Width = 120,
            Height = 40,
            Position = new PixelPoint(1140, 920),
        };
        registry.Register(owner, "PlotGuardians");
        registry.Register(child, "PlotGuardians", participatesInPlacement: false);
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform(),
            store,
            activeLayout,
            registry
        );
        LegacyOverlayPlacement original = activeLayout.Placements["PlotGuardians"];

        RegisteredOverlayWindow ownerRegistration = registry
            .Snapshot()
            .Single(registered => registered.Window == owner);
        RegisteredOverlayWindow childRegistration = registry
            .Snapshot()
            .Single(registered => registered.Window == child);

        Assert.False(interaction.Attach(childRegistration));
        Assert.True(interaction.Attach(ownerRegistration));
        Assert.False(interaction.Attach(ownerRegistration));
        child.Position = new PixelPoint(-800, -700);

        Assert.Equal(original, activeLayout.Placements["PlotGuardians"]);
        Assert.Same(owner, Assert.Single(interaction.Panels));

        owner.Position = new PixelPoint(510, 330);

        Assert.NotEqual(original, activeLayout.Placements["PlotGuardians"]);
    }

    [AvaloniaFact]
    public void EditedPlacementMovesTheLivePanelThatOwnsIt()
    {
        LegacyOverlayLayoutStore store = CreateStore("""{"PlotJumpInfo":"center:0, top:8"}""");
        LegacyOverlayLayout activeLayout = store.Load();
        var registry = new OverlayWindowRegistry();
        var window = new Window
        {
            Width = 600,
            Height = 100,
            Position = new PixelPoint(400, 208),
        };
        registry.Register(window, "PlotJumpInfo");
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform(),
            store,
            activeLayout,
            registry
        );
        var moves = new List<string>();
        interaction.PlacementMoved += (_, eventArgs) => moves.Add(eventArgs.PlotterName);
        Assert.True(interaction.Attach(registry.Snapshot().Single()));
        LegacyOverlayPlacement edited = activeLayout.Placements["PlotJumpInfo"] with { HorizontalOffset = 50 };

        interaction.SetPlacement("PlotJumpInfo", edited);

        Assert.Equal(activeLayout.GetPosition("PlotJumpInfo", GameBounds, new(600, 100)), window.Position);
        Assert.Equal(["PlotJumpInfo"], moves);
        Assert.Single(interaction.Changes);

        LegacyOverlayPlacement unowned = OverlayLayoutCatalog.GetRequired("PlotFSSInfo").DefaultPlacement with
        {
            HorizontalOffset = 64,
        };
        interaction.SetPlacement("PlotFSSInfo", unowned);

        Assert.Equal(unowned, activeLayout.Placements["PlotFSSInfo"]);
        Assert.Equal(2, interaction.Changes.Count);
    }

    [AvaloniaFact]
    public void ClosedPanelIsDetachedAndReportedDuringADrag()
    {
        var registry = new OverlayWindowRegistry();
        Window window = CreatePanel(registry, "PlotJumpInfo");
        OverlayPlacementInteraction interaction = CreateInteraction(
            new RecordingPlatform { UseManagedMoveDrag = true },
            new LegacyOverlayLayoutStore(temporaryDirectory),
            new LegacyOverlayLayoutStore(temporaryDirectory).Load(),
            registry
        );
        var closed = new List<Window>();
        interaction.PanelClosed += (_, eventArgs) => closed.Add(eventArgs.Window);
        ShowAt(window, new PixelPoint(400, 300));
        Assert.True(interaction.Attach(registry.Snapshot().Single()));

        window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(60, 85), RawInputModifiers.LeftMouseButton);
        window.Close();
        IReadOnlyDictionary<string, LegacyOverlayPlacement> changesAtClose = interaction.Changes;
        window.Position = new PixelPoint(900, 700);

        Assert.Equal([window], closed);
        Assert.Empty(interaction.Panels);
        Assert.Null(OverlayDragPolicy.GetOptions(window).ConstrainPosition);
        Assert.Equal(changesAtClose, interaction.Changes);
    }

    [AvaloniaFact]
    public void PanelDragCanStartFromFssPanelContent()
    {
        var registry = new OverlayWindowRegistry();
        var window = new FssInfoOverlayWindow(
            (SystemSurveyOverlayViewModel)OverlayEditorPreviewCatalog.Create("PlotFSSInfo")
        );
        registry.Register(window, "PlotFSSInfo");
        var platform = new RecordingPlatform();
        window.Show();
        try
        {
            OverlayPlacementInteraction interaction = CreateInteraction(
                platform,
                new LegacyOverlayLayoutStore(temporaryDirectory),
                new LegacyOverlayLayoutStore(temporaryDirectory).Load(),
                registry
            );
            Assert.True(interaction.Attach(registry.Snapshot().Single()));

            window.MouseDown(new Point(20, 50), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.Equal(1, platform.MoveDragStarts);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanelDragRespectsPointerCaptureWhenContentHandlesPointerPress(bool ownsPointer)
    {
        var registry = new OverlayWindowRegistry();
        var content = new Border
        {
            Width = 200,
            Height = 120,
            Background = Avalonia.Media.Brushes.Black,
            Child = new Border { Background = Avalonia.Media.Brushes.Black },
        };
        content.PointerPressed += (_, eventArgs) =>
        {
            if (ownsPointer)
            {
                eventArgs.Pointer.Capture(content);
            }
            eventArgs.Handled = true;
        };
        var window = new Window
        {
            Width = 200,
            Height = 120,
            Content = content,
        };
        registry.Register(window, "PlotFSSInfo");
        var platform = new RecordingPlatform();
        window.Show();
        try
        {
            OverlayPlacementInteraction interaction = CreateInteraction(
                platform,
                new LegacyOverlayLayoutStore(temporaryDirectory),
                new LegacyOverlayLayoutStore(temporaryDirectory).Load(),
                registry
            );
            Assert.True(interaction.Attach(registry.Snapshot().Single()));

            window.MouseDown(new Point(30, 40), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.Equal(ownsPointer ? 0 : 1, platform.MoveDragStarts);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Keeps control clicks intact and sends drag releases at a fixed screen position across frame timings.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PanelControlsReceiveClicksWithoutStartingPanelDrag(bool scrollbar, bool applyMoveBeforeRelease)
    {
        var registry = new OverlayWindowRegistry();
        var button = new Button { Content = "Activate", Height = 36 };
        var scroll = new ScrollBar
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Height = 160,
            Width = 24,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 20,
            Value = 20,
        };
        var window = new Window
        {
            Width = 300,
            Height = 220,
            Content = new StackPanel
            {
                Background = Avalonia.Media.Brushes.Black,
                Children = { scrollbar ? scroll : button },
            },
        };
        registry.Register(window, "PlotFSSInfo");
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        var platform = new RecordingPlatform { UseManagedMoveDrag = true };
        try
        {
            ShowAt(window, new PixelPoint(100, 200));
            OverlayPlacementInteraction interaction = CreateInteraction(
                platform,
                new LegacyOverlayLayoutStore(temporaryDirectory),
                new LegacyOverlayLayoutStore(temporaryDirectory).Load(),
                registry
            );
            Assert.True(interaction.Attach(registry.Snapshot().Single()));
            Control target = scrollbar ? Assert.Single(scroll.GetVisualDescendants().OfType<Thumb>()) : button;
            Point start = Assert.IsType<Point>(
                target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
            );
            window.MouseMove(start, RawInputModifiers.None);
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Point end = scrollbar ? start + new Vector(0, 35) : start;
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(0, platform.MoveDragStarts);
            Assert.Equal(new PixelPoint(100, 200), window.Position);
            if (scrollbar)
            {
                Assert.True(scroll.Value > 20, $"Scrollbar stayed at {scroll.Value}.");
            }
            else
            {
                Assert.Equal(1, clicks);
            }

            var background = new Point(250, 200);
            PixelPoint releasePosition = window.PointToScreen(background + new Vector(20, 30));
            window.MouseDown(background, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(window.PointToClient(releasePosition), RawInputModifiers.LeftMouseButton);
            if (applyMoveBeforeRelease)
            {
                await Task.Delay(30);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(new PixelPoint(120, 230), window.Position);
            }
            // A completed frame changes the release's client coordinates, not its screen position.
            window.MouseUp(window.PointToClient(releasePosition), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, platform.MoveDragStarts);
            Assert.Equal(new PixelPoint(120, 230), window.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MineMapKeepsPanGesturesWhenViewportInteractionIsEnabled(bool canPan)
    {
        var registry = new OverlayWindowRegistry();
        var window = new Window
        {
            Width = 300,
            Height = 220,
            Content = new MineMapControl
            {
                Survey = new MineMapSurvey
                {
                    Center = new(0, 0),
                    PlanetRadiusMeters = 1_000_000,
                    LocationRadiusMeters = 1_000,
                },
                AllowViewportInteraction = canPan,
                ViewportZoom = 2,
            },
        };
        registry.Register(window, "PlotMineMap");
        var platform = new RecordingPlatform { UseManagedMoveDrag = true };
        try
        {
            ShowAt(window, new PixelPoint(100, 200));
            OverlayPlacementInteraction interaction = CreateInteraction(
                platform,
                new LegacyOverlayLayoutStore(temporaryDirectory),
                new LegacyOverlayLayoutStore(temporaryDirectory).Load(),
                registry
            );
            Assert.True(interaction.Attach(registry.Snapshot().Single()));
            var start = new Point(150, 110);

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(start + new Vector(20, 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(20, 30), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(canPan ? 0 : 1, platform.MoveDragStarts);
            Assert.Equal(canPan ? new PixelPoint(100, 200) : new PixelPoint(120, 230), window.Position);
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private LegacyOverlayLayoutStore CreateStore(string plottersJson)
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(Path.Combine(temporaryDirectory, "plotters.json"), plottersJson);
        return new LegacyOverlayLayoutStore(temporaryDirectory);
    }

    private static OverlayPlacementInteraction CreateInteraction(
        IOverlayPlatformService platform,
        LegacyOverlayLayoutStore store,
        LegacyOverlayLayout activeLayout,
        OverlayWindowRegistry registry,
        OverlayBehaviorViewModel? behavior = null
    ) => new(platform, store, activeLayout, registry, GameBounds, () => behavior);

    private OverlayBehaviorViewModel CreateBehavior(Window window, bool lockToMonitor)
    {
        var behavior = new OverlayBehaviorViewModel(
            new OverlayBehaviorSettingsStore(Path.Combine(temporaryDirectory, "ui.json"))
        );
        MainWindowMonitor monitor = MainWindowPlacement.DescribeScreens(window.Screens.All)[0];
        var option = new ApplicationMonitorOption(monitor.Id, monitor.DisplayName);
        behavior.SetAvailableMonitors([option]);
        behavior.SelectedMonitor = option;
        behavior.LockToMonitor = lockToMonitor;
        return behavior;
    }

    private static Window CreatePanel(OverlayWindowRegistry registry, string plotterName)
    {
        var window = new Window
        {
            Width = 300,
            Height = 220,
            Content = new Border { Background = Avalonia.Media.Brushes.Black },
        };
        registry.Register(window, plotterName);
        return window;
    }

    private static void ShowAt(Window window, PixelPoint position)
    {
        window.Show();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        window.Position = position;
    }

    private sealed class RecordingPlatform(bool usesManagedDragForMonitorLock = false) : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.LinuxX11) with
            {
                UsesManagedDragForMonitorLock = usesManagedDragForMonitorLock,
            };

        public int MoveDragStarts { get; private set; }

        public bool UseManagedMoveDrag { get; init; }

        public OverlayPreparationResult PreparePassiveWindow(Window window) => new(true, true, "Prepared");

        public OverlayInteractionResult SetInteractive(Window window, bool interactive) =>
            new(true, interactive, "Prepared");

        public void BeginMoveDrag(Window window, PointerPressedEventArgs eventArgs)
        {
            MoveDragStarts++;
            if (UseManagedMoveDrag)
            {
                ManagedOverlayWindowDragSession.Begin(window, eventArgs);
            }
        }

        public void Dispose() { }
    }
}
