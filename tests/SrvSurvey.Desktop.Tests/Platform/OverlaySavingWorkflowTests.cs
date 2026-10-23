using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ProfileSync;
using SrvSurvey.Desktop.ViewModels;
using Xunit;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlaySavingWorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "overlay-audit-" + Guid.NewGuid());

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    public OverlaySavingWorkflowTests() => Directory.CreateDirectory(root);

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    public void Dispose() => Directory.Delete(root, true);

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    private string PathFor(string name) => Path.Combine(root, name);

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    private LegacyOverlayLayoutStore Store() => new(root);

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    private static LegacyOverlayPlacement Placement(int offset = 20, double? opacity = .8) =>
        new(LegacyHorizontalAnchor.Center, offset, LegacyVerticalAnchor.Top, 30, opacity);

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    private static OverlayInteractionViewModel Editor(
        LegacyOverlayLayoutStore store,
        LegacyOverlayLayout active,
        FakeHost host
    ) => new(new Platform(), new Tracker(), store, active, new OverlayWindowRegistry(), host);

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    private static void Drag(FakeHost host) =>
        host.Move(new("PlotJumpInfo", new(450, 300), new(300, 100), new(0, 0, 1920, 1080)));

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [Fact]
    public void CalibrationFailureReportsCommittedPositionsAndKeepsCalibrationForRetry()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement() });
        LegacyOverlayLayout active = store.Load();
        LegacyOverlayPlacement original = active.Placements["PlotJumpInfo"];
        var host = new FakeHost();
        using OverlayInteractionViewModel vm = Editor(store, active, host);
        vm.MiningDetection = new MiningDetectionViewModel(new SurfaceMiningSettingsStore(PathFor("ui.json")));
        Assert.True(vm.Begin());
        Drag(host);
        vm.MiningDetection.UpdateCalibration(vm.MiningDetection.Settings with { X = .3 });
        Assert.True(vm.MiningDetection.HasCalibrationChanges);
        Directory.CreateDirectory(PathFor("ui.json"));
        vm.Save();
        Assert.Contains("positions saved", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("calibration", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(store.Load().Placements["PlotJumpInfo"], active.Placements["PlotJumpInfo"]);
        Assert.NotEqual(original, active.Placements["PlotJumpInfo"]);
        Assert.True(vm.IsEditing);
        Assert.True(vm.MiningDetection.HasCalibrationChanges);
        Directory.Delete(PathFor("ui.json"));
        vm.Save();
        Assert.False(vm.IsEditing);
        Assert.Equal(.3, new SurfaceMiningSettingsStore(PathFor("ui.json")).LoadDetection().X);
    }

    [Theory]
    [InlineData("{", typeof(JsonException))]
    [InlineData("{\"PlotJumpInfo\":\"center:notanumber,top:30\"}", typeof(FormatException))]
    [InlineData("{\"PlotJumpInfo\":\"center:99999999999999999999,top:30\"}", typeof(OverflowException))]
    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    public void MalformedLayoutAfterOpeningMustStayInNormalSaveErrorHandler(string corrupt, Type expected)
    {
        LegacyOverlayLayoutStore store = Store();
        var host = new FakeHost();
        using OverlayInteractionViewModel vm = Editor(store, store.Load(), host);
        Assert.True(vm.Begin());
        Drag(host);
        File.WriteAllText(PathFor("plotters.json"), corrupt);
        Exception? escaped = Record.Exception(vm.Save);
        _ = expected;
        // Desired behavior: return a status, keep the editor usable, do not throw to the UI dispatcher.
        Assert.Null(escaped);
    }

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [Fact]
    public void PositionOnlyEditMustPreserveOpacitySavedByAnotherWriterDuringEdit()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement() });
        var host = new FakeHost();
        using OverlayInteractionViewModel vm = Editor(store, store.Load(), host);
        Assert.True(vm.Begin());
        LegacyOverlayPlacement latest = store.Load().Placements["PlotJumpInfo"] with { Opacity = .25 };
        new LegacyOverlayLayoutStore(root).Save(
            new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = latest }
        );
        Drag(host);
        vm.Save();
        Assert.False(vm.IsEditing);
        Assert.Equal(.25, store.Load().Placements["PlotJumpInfo"].Opacity);
    }

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [Fact]
    public void OpacitySaveMustNotRevertUntouchedPanelWithPositionReference()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = Placement() with { PositionReference = new(1920, 1080) },
            }
        );
        var vm = new OverlayLayoutSettingsViewModel(store, store.Load());
        // Another position-editor save updates this panel's opacity, while the opacity settings VM is open.
        store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = store.Load().Placements["PlotJumpInfo"] with { Opacity = .25 },
            }
        );
        OverlayPlacementEditorViewModel other = vm.Overlays.First(x => x.Name != "PlotJumpInfo");
        other.UseCustomOpacity = true;
        other.CustomOpacityPercent = 60;
        vm.SaveCommand.Execute(null);
        Assert.Equal(.25, store.Load().Placements["PlotJumpInfo"].Opacity);
    }

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [Fact]
    public void UnchangedPositionReferenceMustNotMarkOpacitySettingsDirty()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = Placement() with { PositionReference = new(1920, 1080) },
            }
        );
        var vm = new OverlayLayoutSettingsViewModel(store, store.Load());
        Assert.False(vm.IsDirty);
    }

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [Fact]
    public void PositionSavedAfterStagingMachineRestoreMustNotDisappearSilentlyOnRestart()
    {
        var paths = new AppDataPaths(PathFor("config"), PathFor("data"), PathFor("cache"), []);
        var sync = new ProfileSyncStore(paths, () => "audit-machine");
        var store = new LegacyOverlayLayoutStore(paths.DataDirectory);
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement(20) });
        ProfileSnapshot backup = sync.Capture();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement(100) });
        sync.StageExplicitRestore(backup, restoreMachine: true);
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement(450) });
        bool applied = sync.ApplyPending(out SyncMergeResult? conflicts);
        Assert.True(applied);
        Assert.Null(conflicts);
        Assert.Equal(450, store.Load().Placements["PlotJumpInfo"].HorizontalOffset);
    }

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [AvaloniaFact]
    public void ResizeThenDragWithLivePanelMustSaveTheReleasedPanelOrigin()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement() });
        var platform = new Platform();
        var registry = new OverlayWindowRegistry();
        var live = new Window
        {
            Width = 300,
            Height = 100,
            Position = new PixelPoint(800, 30),
        };
        registry.Register(live, "PlotJumpInfo");
        live.Show();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var vm = new OverlayInteractionViewModel(platform, new Tracker(), store, store.Load(), registry, host);
        try
        {
            Assert.True(vm.Begin());
            OverlayPositionPreviewWindow preview = host.PreviewWindows.Single(x => x.Definition.Name == "PlotJumpInfo");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Controls.Primitives.Thumb thumb = preview.FindControl<Avalonia.Controls.Primitives.Thumb>(
                "PanelResizeThumb"
            )!;
            Point point = thumb.TranslatePoint(new Point(11, 11), preview)!.Value;
            preview.MouseMove(point, Avalonia.Input.RawInputModifiers.None);
            preview.MouseDown(point, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.LeftMouseButton);
            preview.MouseMove(point + new Vector(120, 40), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            preview.MouseUp(
                point + new Vector(120, 40),
                Avalonia.Input.MouseButton.Left,
                Avalonia.Input.RawInputModifiers.None
            );
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            PixelSize resizedSize = preview.GetPanelMetrics(preview.RenderScaling).PanelSize;
            Assert.NotEqual(new PixelSize(300, 100), resizedSize);
            preview.MouseDown(
                new Point(20, 25),
                Avalonia.Input.MouseButton.Left,
                Avalonia.Input.RawInputModifiers.LeftMouseButton
            );
            preview.MouseMove(new Point(120, 75), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            preview.MouseUp(new Point(120, 75), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            PixelPoint releasedOrigin = preview.GetPanelScreenOrigin(preview.RenderScaling);
            vm.Save();
            Assert.False(vm.IsEditing);
            LegacyOverlayLayout saved = store.Load();
            Assert.NotNull(saved.GetSizeOverride("PlotJumpInfo"));
            PixelPoint? resolved = saved.GetPosition("PlotJumpInfo", new(0, 0, 1920, 1080), resizedSize);
            live.Close();
            Assert.True(vm.Begin());
            OverlayPositionPreviewWindow reopened = host.PreviewWindows.Single(x =>
                x.Definition.Name == "PlotJumpInfo"
            );
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(resolved, reopened.GetPanelScreenOrigin(reopened.RenderScaling));
            Assert.Equal(releasedOrigin, reopened.GetPanelScreenOrigin(reopened.RenderScaling));
        }
        finally
        {
            live.Close();
        }
    }

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [AvaloniaFact]
    public void FailedLiveSaveMustKeepMovedDraftForRetryAfterTheFileIsUnlocked()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement() });
        LegacyOverlayLayout active = store.Load();
        var registry = new OverlayWindowRegistry();
        var host = new FakeHost();
        var live = new Window
        {
            Width = 300,
            Height = 100,
            Position = new PixelPoint(800, 30),
        };
        registry.Register(live, "PlotJumpInfo");
        live.Show();
        using var vm = new OverlayInteractionViewModel(new Platform(), new Tracker(), store, active, registry, host);
        try
        {
            Assert.True(vm.ToggleLiveOverlayInteraction());
            live.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            live.MouseMove(new Point(-180, 295), RawInputModifiers.LeftMouseButton);
            live.MouseUp(new Point(-180, 295), MouseButton.Left, RawInputModifiers.None);
            using (
                var locked = new FileStream(
                    PathFor("plotters.json"),
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None
                )
            )
            {
                Assert.True(vm.ToggleLiveOverlayInteraction());
                Assert.Contains("could not be saved", vm.StatusMessage);
            }
            Assert.True(vm.ToggleLiveOverlayInteraction());
            Assert.True(vm.ToggleLiveOverlayInteraction());
            // Desired retry behavior: a transient error must not throw away all moved positions.
            Assert.Equal(
                new PixelPoint(600, 300),
                store.Load().GetPosition("PlotJumpInfo", new(0, 0, 1920, 1080), new(300, 100))
            );
        }
        finally
        {
            live.Close();
        }
    }

    /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
    [AvaloniaFact]
    public void MovingEliteDuringLiveInteractionMustNotAccumulatePositionChangesWithoutDragging()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement() });
        LegacyOverlayLayout active = store.Load();
        var platform = new Platform();
        var registry = new OverlayWindowRegistry();
        var tracker = new Tracker();
        var timer = new ManualTimer();
        using var hosted = new HostedOverlayWindow(
            new PassiveOverlayWindowDefinition(
                "PlotJumpInfo",
                _ => new Window { Width = 300, Height = 100 },
                (bounds, _) => bounds.Position
            ),
            new OverlayPresentationSessionDependencies(
                () => platform,
                () => tracker,
                _ => timer,
                active,
                WindowRegistry: registry
            ),
            _ => { }
        );
        hosted.Reconcile(true);
        Window live = hosted.CurrentWindow!;
        Assert.NotNull(live);
        using var vm = new OverlayInteractionViewModel(platform, tracker, store, active, registry, new FakeHost());
        Assert.True(vm.ToggleLiveOverlayInteraction());
        int originalOffset = active.Placements["PlotJumpInfo"].HorizontalOffset;
        tracker.Snapshot = tracker.Snapshot with { ClientBounds = new PixelRect(100, 0, 1920, 1080) };
        timer.Pulse();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        PixelPoint followedOnce = live.Position;
        timer.Pulse();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        PixelPoint followedTwice = live.Position;
        vm.ToggleLiveOverlayInteraction();
        // A geometry update should move the panel once with the game, without becoming a user position edit.
        int savedOffset = store.Load().Placements["PlotJumpInfo"].HorizontalOffset;
        Assert.True(
            followedOnce == followedTwice && savedOffset == originalOffset,
            $"No pointer input: first poll={followedOnce}; second poll={followedTwice}; original horizontal offset={originalOffset}; saved horizontal offset={savedOffset}"
        );
    }

    /// <summary>Unwinds a partially opened native editor and permits a subsequent opening.</summary>
    [AvaloniaFact]
    public void EditorOpeningFailureClearsStateAndCanBeRetried()
    {
        LegacyOverlayLayoutStore store = Store();
        var platform = new Platform();
        var registry = new OverlayWindowRegistry();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        bool fail = true;
        registry.WindowPreparing += _ =>
        {
            if (fail)
            {
                fail = false;
                throw new IOException("injected preparation failure");
            }
        };
        using var vm = new OverlayInteractionViewModel(platform, new Tracker(), store, store.Load(), registry, host);
        vm.MiningDetection = new MiningDetectionViewModel(null);
        Assert.False(vm.Begin());
        Assert.False(vm.IsEditing);
        Assert.Null(host.EditorToolbar);
        Assert.Contains("injected preparation failure", vm.StatusMessage);
        Assert.True(vm.Begin());
        vm.Cancel();
    }

    /// <summary>Rebuilds the reference frame without losing a user's draft when Elite moves or changes resolution.</summary>
    [AvaloniaFact]
    public void EditorRebasesChangedGameBoundsAndKeepsDraft()
    {
        LegacyOverlayLayoutStore store = Store();
        var tracker = new Tracker();
        var platform = new Platform();
        var registry = new OverlayWindowRegistry();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var vm = new OverlayInteractionViewModel(platform, tracker, store, store.Load(), registry, host);
        Assert.True(vm.Begin());
        vm.GlobalOpacityPercent = 70;
        tracker.Snapshot = tracker.Snapshot with { ClientBounds = new PixelRect(100, 200, 1280, 720) };
        host.UpdateReferenceFrame();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsEditing);
        Assert.Equal(70, vm.GlobalOpacityPercent);
        OverlayPositionPreviewWindow preview = host.PreviewWindows.Single(x => x.Definition.Name == "PlotJumpInfo");
        preview.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
        preview.MouseMove(new Point(100, 75), RawInputModifiers.LeftMouseButton);
        preview.MouseUp(new Point(100, 75), MouseButton.Left, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        PixelPoint position = preview.GetPanelScreenOrigin(preview.RenderScaling);
        PixelSize size = preview.GetPanelMetrics(preview.RenderScaling).PanelSize;
        vm.Save();
        Assert.Equal(position, store.Load().GetPosition("PlotJumpInfo", tracker.Snapshot.ClientBounds, size));
        Assert.Equal(
            new OverlayPositionReference(1280, 720),
            store.Load().Placements["PlotJumpInfo"].PositionReference
        );
    }

    /// <summary>Closing an unsaved live session still discards it; moving does not autosave.</summary>
    [AvaloniaFact]
    public void LiveDragOnlySavesWhenInteractionEnds()
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement() });
        LegacyOverlayPlacement original = store.Load().Placements["PlotJumpInfo"];
        var registry = new OverlayWindowRegistry();
        var live = new Window
        {
            Width = 300,
            Height = 100,
            Position = new PixelPoint(800, 30),
        };
        registry.Register(live, "PlotJumpInfo");
        live.Show();
        var vm = new OverlayInteractionViewModel(
            new Platform(),
            new Tracker(),
            store,
            store.Load(),
            registry,
            new FakeHost()
        );
        try
        {
            Assert.True(vm.ToggleLiveOverlayInteraction());
            live.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            live.MouseMove(new Point(100, 75), RawInputModifiers.LeftMouseButton);
            live.MouseUp(new Point(100, 75), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(original, store.Load().Placements["PlotJumpInfo"]);
            vm.Dispose();
            Assert.Equal(original, store.Load().Placements["PlotJumpInfo"]);
        }
        finally
        {
            vm.Dispose();
            live.Close();
        }
    }

    /// <summary>Geometry changes preserve the corner until horizontal overflow requires a monitor clamp.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppearanceGeometryChangeKeepsTheCornerWithinItsMonitorAndReopensAtTheSavedPosition(bool typography)
    {
        LegacyOverlayLayoutStore store = Store();
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Placement() });
        var platform = new Platform();
        var registry = new OverlayWindowRegistry();
        var live = new Window
        {
            Width = 300,
            Height = 100,
            Position = new PixelPoint(500, 200),
        };
        registry.Register(live, "PlotJumpInfo");
        live.Show();
        var host = new AvaloniaOverlayPositionEditorHost(platform, registry);
        using var vm = new OverlayInteractionViewModel(platform, new Tracker(), store, store.Load(), registry, host);
        try
        {
            Assert.True(vm.Begin());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            OverlayPositionPreviewWindow preview = host.PreviewWindows.Single(x => x.Definition.Name == "PlotJumpInfo");
            PixelPoint origin = preview.GetPanelScreenOrigin(preview.RenderScaling);
            vm.OpenOverlaySettings("PlotJumpInfo");
            if (typography)
            {
                vm.TypographyRoles.Single(role => role.Role == OverlayTypographyRole.Title).Percent = 150;
            }
            else
            {
                vm.UseGlobalOverlayScale = false;
                vm.SelectedOverlayScalePercent = 150;
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            PixelPoint changedOrigin = preview.GetPanelScreenOrigin(preview.RenderScaling);
            PixelSize changedSize = preview.GetPanelMetrics(preview.RenderScaling).PanelSize;
            PixelRect monitor = preview.Screens.Primary!.Bounds;
            Assert.Equal(
                new PixelPoint(
                    Math.Clamp(origin.X, monitor.X, Math.Max(monitor.X, monitor.Right - changedSize.Width)),
                    origin.Y
                ),
                changedOrigin
            );
            vm.Save();
            LegacyOverlayPlacement committed = store.Load().Placements["PlotJumpInfo"];
            live.Close();
            Assert.True(vm.Begin());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            preview = host.PreviewWindows.Single(x => x.Definition.Name == "PlotJumpInfo");
            Assert.True(
                changedOrigin == preview.GetPanelScreenOrigin(preview.RenderScaling),
                $"original={origin}; after change={changedOrigin}; changed size={changedSize}; saved={committed}; reopened={preview.GetPanelScreenOrigin(preview.RenderScaling)}; reopened size={preview.GetPanelMetrics(preview.RenderScaling).PanelSize}"
            );
            vm.Cancel();
        }
        finally
        {
            live.Close();
        }
    }

    private sealed class ManualTimer : IHostedOverlayTimer
    {
        public event EventHandler? Tick;

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Start() { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Stop() { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Dispose() { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Pulse() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Platform : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities => OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

        public OverlayPreparationResult PreparePassiveWindow(Window window) => new(true, true, "ready");

        public OverlayInteractionResult SetInteractive(Window window, bool interactive) =>
            new(true, interactive, "ready");

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void BeginMoveDrag(Window window, PointerPressedEventArgs eventArgs) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Dispose() { }
    }

    private sealed class Tracker : IGameWindowTracker
    {
        public GameWindowSnapshot Snapshot { get; set; } = new((nint)1, 1, new(0, 0, 1920, 1080), true, true);

        public GameWindowSnapshot GetSnapshot() => Snapshot;

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Dispose() { }
    }

    private sealed class FakeHost : IOverlayPositionEditorHost
    {
        public event EventHandler<OverlayPreviewMovedEventArgs>? PreviewMoved;
        public event EventHandler<OverlayPreviewSizeChangedEventArgs>? PreviewSizeChanged
        {
            add
            { /* This fake host does not raise this event. */
            }
            remove
            { /* This fake host has no event subscriptions to remove. */
            }
        }
        public event EventHandler? Closed
        {
            add
            { /* This fake host does not raise this event. */
            }
            remove
            { /* This fake host has no event subscriptions to remove. */
            }
        }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Move(OverlayPreviewMovedEventArgs e) => PreviewMoved?.Invoke(this, e);

        public bool Open(
            OverlayInteractionViewModel viewModel,
            OverlayPositionEditSession session,
            OverlayLayoutCategory category,
            PixelRect? preferredHostBounds
        ) => true;

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void ShowCategory(OverlayPositionEditSession session, OverlayLayoutCategory category) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void RefreshPreviewOpacities(OverlayPositionEditSession session) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void RefreshPreviewScales(OverlayPositionEditSession session) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void RefreshPreviewTypography(OverlayPositionEditSession session) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void RefreshPreviewSizes(OverlayPositionEditSession session) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void RefreshPreviewPositions(OverlayPositionEditSession session) { }

        public int SnapPreviewsToCenter(OverlayPositionEditSession session) => 0;

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void SetRuntimeOverlaysVisibleDuringEditing(bool visible) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Close(bool restoreRuntimeWindows = true) { }

        /// <summary>Exercises or supports an isolated overlay saving regression scenario.</summary>
        public void Dispose() { }
    }
}
