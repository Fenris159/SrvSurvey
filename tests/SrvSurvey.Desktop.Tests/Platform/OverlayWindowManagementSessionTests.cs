using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayWindowManagementSessionTests
{
    /// <summary>Checks that normal presentation and the combined gamescope host never enable bypass.</summary>
    [AvaloniaTheory]
    [InlineData(false, OverlayPresentationMode.MultipleWindows)]
    [InlineData(true, OverlayPresentationMode.CombinedWindow)]
    public void DisabledOrCombinedPresentationKeepsDesktopManagement(bool enabled, OverlayPresentationMode mode)
    {
        var registry = new OverlayWindowRegistry();
        int nativeCreations = 0;
        using OverlayPresentationSession presentation = CreatePresentation(
            registry,
            mode,
            () =>
            {
                nativeCreations++;
                return new FakeWindowManagement();
            }
        );
        presentation.ConfigureWindowManagement(enabled);
        var window = new Window();
        try
        {
            registry.Register(window, "PlotJumpInfo");
            window.Show();
            Assert.Equal(0, nativeCreations);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Verifies that a session owns one bypass backend and leaves unsupported platforms managed.</summary>
    [AvaloniaFact]
    public void StartupConfigurationIsIdempotentAndUnsupportedPlatformsFallBack()
    {
        var registry = new OverlayWindowRegistry();
        var native = new FakeWindowManagement();
        using OverlayPresentationSession presentation = CreatePresentation(
            registry,
            OverlayPresentationMode.MultipleWindows,
            () => native
        );
        var messages = new List<string>();
        presentation.ConfigureWindowManagement(true, messages.Add);
        presentation.ConfigureWindowManagement(true, messages.Add);
        var window = new Window();
        try
        {
            registry.Register(window, "PlotJumpInfo");
            Assert.Equal([window], native.Prepared);
            Assert.Single(messages);
            presentation.Dispose();
            Assert.Equal(1, native.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => presentation.ConfigureWindowManagement(true));
        }
        finally
        {
            window.Close();
        }

        using OverlayPresentationSession unsupported = CreatePresentation(
            registry,
            OverlayPresentationMode.MultipleWindows,
            () => new PortableOverlayPlatformService(OverlayPlatformCapabilities.ForHost(OverlayHostKind.LinuxWayland))
        );
        unsupported.ConfigureWindowManagement(true, messages.Add);
        Assert.Contains(messages, message => message.Contains("unavailable", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void SharedNativePlatformPreservesBypassCursorAndDragUntilSessionDisposal()
    {
        var registry = new OverlayWindowRegistry();
        var native = new FakeWindowManagement();
        using var presentation = OverlayPresentationSession.CreateWithSharedPlatform(
            new OverlayPresentationDecision(OverlayPresentationMode.MultipleWindows, "Shared native test"),
            new OverlayPresentationSessionDependencies(
                () => native,
                () => new UnavailableGameWindowTracker(),
                _ => throw new InvalidOperationException("No timer is needed."),
                LegacyOverlayLayout.Empty,
                WindowRegistry: registry
            )
        );
        presentation.ConfigureWindowManagement(true);
        var window = new Window { Width = 100, Height = 100 };
        try
        {
            using (IOverlayPlatformService lease = presentation.CreatePlatformService())
            {
                registry.Register(window, "PlotJumpInfo");
                Assert.Equal([window], native.Prepared);
                Assert.True(lease.PrepareInteractiveWindow(window).IsInteractive);
                Assert.Null(lease.BeginVisibleCursorSession(window));
                Assert.Same(window, native.CursorWindow);
                window.PointerPressed += (_, args) => lease.BeginMoveDrag(window, args);
                window.Show();
                window.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.LeftMouseButton);
                Assert.Equal(1, native.DragCalls);
                Assert.Contains(window, native.Raised);
            }
            Assert.Equal(0, native.DisposeCount);
            presentation.Dispose();
            Assert.Equal(1, native.DisposeCount);
            int raises = native.Raised.Count;
            window.Position = new PixelPoint(10, 20);
            Assert.Equal(raises, native.Raised.Count);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Creates a presentation with an isolated live-window registry and injected native backend.</summary>
    private static OverlayPresentationSession CreatePresentation(
        OverlayWindowRegistry registry,
        OverlayPresentationMode mode,
        Func<IOverlayPlatformService> platformFactory
    )
    {
        return OverlayPresentationSession.CreateForAdapters(
            new OverlayPresentationDecision(mode, "Bypass test"),
            new OverlayPresentationSessionDependencies(
                platformFactory,
                () => throw new InvalidOperationException("No game tracker is needed."),
                _ => throw new InvalidOperationException("No timer is needed."),
                LegacyOverlayLayout.Empty,
                WindowRegistry: registry
            )
        );
    }

    /// <summary>Verifies pre-map setup and direct dragging while ordinary application windows stay managed.</summary>
    [AvaloniaFact]
    public void LivePanelsBypassAndRaiseWithoutChangingDragPlacementOrOrdinaryWindows()
    {
        var registry = new OverlayWindowRegistry();
        var native = new FakeWindowManagement();
        using var session = new OverlayWindowManagementSession(registry, native);
        var live = new Window
        {
            Width = 200,
            Height = 100,
            ShowActivated = false,
        };
        var ordinaryWindow = new Window();
        try
        {
            OverlayThemeResources.Apply(ordinaryWindow);
            ordinaryWindow.Show();
            registry.Register(live, "PlotJumpInfo");
            registry.Register(live, "PlotJumpInfo");
            Assert.Equal([live], native.Prepared);
            Assert.False(native.PreparedWhileVisible);
            live.Position = new PixelPoint(100, 200);
            Assert.Empty(native.Raised);
            live.Show();
            Assert.Contains(live, native.Raised);
            Assert.DoesNotContain(ordinaryWindow, native.Prepared);
            Assert.DoesNotContain(ordinaryWindow, native.Raised);
            Assert.Empty(native.Activated);

            live.PointerPressed += (_, args) => ManagedOverlayWindowDragSession.Begin(live, args);
            live.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            live.MouseMove(new Point(35, 40), RawInputModifiers.LeftMouseButton);
            live.MouseUp(new Point(35, 40), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(new PixelPoint(125, 230), live.Position);
            Assert.True(native.Raised.Count >= 2);
            int raises = native.Raised.Count;
            live.Hide();
            live.Position = new PixelPoint(200, 300);
            Assert.Equal(raises, native.Raised.Count);
            live.Show();
            Assert.True(native.Raised.Count > raises);
        }
        finally
        {
            live.Close();
            ordinaryWindow.Close();
        }
    }

    /// <summary>Checks the shared editor policy, activation, category replacement, and persisted drag positions.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositionEditorSharesTheStartupPolicyAndKeepsDragAndSaveWorking(bool enabled)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-bypass-editor-{Guid.NewGuid():N}");
        var registry = new OverlayWindowRegistry();
        var native = new FakeWindowManagement();
        using OverlayPresentationSession presentation = CreatePresentation(
            registry,
            OverlayPresentationMode.MultipleWindows,
            () => native
        );
        presentation.ConfigureWindowManagement(enabled);
        var store = new LegacyOverlayLayoutStore(directory);
        var host = new AvaloniaOverlayPositionEditorHost(native, registry);
        using var viewModel = new OverlayInteractionViewModel(
            native,
            new UnavailableGameWindowTracker(),
            store,
            store.Load(),
            registry,
            host
        );
        try
        {
            Assert.True(viewModel.Begin());
            Window toolbar = Assert.IsType<OverlayPositionEditorWindow>(host.EditorToolbar);
            registry.PrepareWindow(toolbar);
            OverlayPositionPreviewWindow preview = host.PreviewWindows.Single(window =>
                window.Definition.Name == "PlotJumpInfo"
            );
            Assert.Empty(registry.Snapshot());
            Assert.False(native.PreparedWhileVisible);
            if (enabled)
            {
                Assert.Contains(toolbar, native.Prepared);
                Assert.All(host.PreviewWindows, window => Assert.Contains(window, native.Prepared));
                Assert.Equal([toolbar], native.Activated);
            }
            else
            {
                Assert.Empty(native.Prepared);
                Assert.Empty(native.Activated);
            }

            PixelPoint initial = preview.GetPanelScreenOrigin(preview.RenderScaling);
            preview.MouseDown(new Point(12, 12), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            preview.MouseMove(new Point(42, 32), RawInputModifiers.LeftMouseButton);
            preview.MouseUp(new Point(42, 32), MouseButton.Left, RawInputModifiers.None);
            PixelPoint saved = preview.GetPanelScreenOrigin(preview.RenderScaling);
            Assert.Equal(new PixelPoint(initial.X + 30, initial.Y + 20), saved);
            viewModel.Save();
            Assert.False(viewModel.IsEditing);
            Assert.StartsWith("Saved", viewModel.StatusMessage);
            Assert.True(viewModel.Begin());
            preview = host.PreviewWindows.Single(window => window.Definition.Name == "PlotJumpInfo");
            Assert.Equal(saved, preview.GetPanelScreenOrigin(preview.RenderScaling));
            viewModel.SelectedCategory = viewModel.Categories[1];
            Assert.All(host.PreviewWindows, window => Assert.True(window.IsVisible));
            if (enabled)
            {
                Assert.All(host.PreviewWindows, window => Assert.Contains(window, native.Prepared));
                Assert.Equal(native.Prepared.Count, native.Prepared.Distinct().Count());
                Assert.Equal(2, native.Activated.Count);
            }
            Assert.Empty(registry.Snapshot());
        }
        finally
        {
            viewModel.Cancel();
            viewModel.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Checks that failed pre-map configuration leaves a panel usable through normal presentation.</summary>
    [AvaloniaFact]
    public void FailedPreparationKeepsManagedWindowAndReportsFallback()
    {
        var registry = new OverlayWindowRegistry();
        var native = new FakeWindowManagement { CanPrepare = false };
        var messages = new List<string>();
        using var session = new OverlayWindowManagementSession(registry, native, messages.Add);
        var window = new Window();
        try
        {
            registry.Register(window, "PlotJumpInfo");
            window.Show();
            Assert.True(window.IsVisible);
            window.Position = new PixelPoint(100, 200);
            Assert.Empty(native.Raised);
            Assert.Contains("using normal management", Assert.Single(messages));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Ensures native resources and observers are released before new windows can be registered.</summary>
    [AvaloniaFact]
    public void DisposalDetachesVisibleWindowsAndStopsFuturePreparation()
    {
        var registry = new OverlayWindowRegistry();
        var native = new FakeWindowManagement();
        var session = new OverlayWindowManagementSession(registry, native);
        var first = new Window();
        var second = new Window();
        try
        {
            registry.Register(first, "PlotJumpInfo");
            first.Show();
            session.Dispose();
            session.Dispose();
            Assert.Equal(1, native.DisposeCount);
            native.Raised.Clear();
            first.Position = new PixelPoint(20, 30);
            first.Hide();
            first.Show();
            registry.Register(second, "PlotFSS");
            second.Show();
            Assert.Equal([first], native.Prepared);
            Assert.Empty(native.Raised);
        }
        finally
        {
            first.Close();
            second.Close();
            session.Dispose();
        }
    }

    /// <summary>Records native requests without depending on a desktop compositor.</summary>
    private sealed class FakeWindowManagement : IOverlayWindowManagement, IOverlayPlatformService
    {
        /// <summary>Controls whether pre-map native configuration succeeds.</summary>
        internal bool CanPrepare { get; init; } = true;

        /// <summary>Records windows submitted for native bypass configuration.</summary>
        internal List<Window> Prepared { get; } = [];

        /// <summary>Records shown or moved windows submitted for raising.</summary>
        internal List<Window> Raised { get; } = [];

        /// <summary>Records windows explicitly given keyboard focus.</summary>
        internal List<Window> Activated { get; } = [];

        /// <summary>Detects attempts to change native management after a window becomes visible.</summary>
        internal bool PreparedWhileVisible { get; private set; }

        /// <summary>Counts releases of the native backend owned by the presentation session.</summary>
        internal int DisposeCount { get; private set; }

        /// <summary>Reports a desktop host that supports separate XWayland overlay windows.</summary>
        public OverlayPlatformCapabilities Capabilities =>
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.LinuxXWayland);

        /// <summary>Keeps the ordinary passive preparation contract available alongside bypass.</summary>
        public OverlayPreparationResult PreparePassiveWindow(Window window) => new(true, true, "Prepared");

        /// <summary>Models interaction remaining available for an unmanaged panel.</summary>
        public OverlayInteractionResult SetInteractive(Window window, bool interactive) =>
            new(true, interactive, "Prepared");

        /// <summary>Checks that the session prepares windows before showing them.</summary>
        public bool TryBypassWindowManagement(Window window)
        {
            Prepared.Add(window);
            PreparedWhileVisible |= window.IsVisible;
            return CanPrepare;
        }

        /// <summary>Records direct raises and the editor's explicit activation separately.</summary>
        public void RaiseUnmanagedWindow(Window window, bool activate = false)
        {
            Raised.Add(window);
            if (activate)
            {
                Activated.Add(window);
            }
        }

        /// <summary>Records native lifetime ownership.</summary>
        internal Window? CursorWindow { get; private set; }
        internal int DragCalls { get; private set; }

        public IDisposable? BeginVisibleCursorSession(Window window)
        {
            CursorWindow = window;
            return null;
        }

        public void BeginMoveDrag(Window window, PointerPressedEventArgs eventArgs) => DragCalls++;

        public void Dispose() => DisposeCount++;
    }

    /// <summary>Allows editor preview testing without a running game process.</summary>
    private sealed class UnavailableGameWindowTracker : IGameWindowTracker
    {
        /// <summary>Reports no game window so the editor uses the connected display.</summary>
        public GameWindowSnapshot GetSnapshot() => GameWindowSnapshot.Unavailable;

        /// <summary>Releases no resources because this tracker owns no native handles.</summary>
        public void Dispose() { }
    }
}
