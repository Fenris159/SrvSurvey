using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayPresentationSession : IDisposable
{
    private readonly CombinedOverlayPresentationController? combinedController;
    private readonly OverlayPresentationSessionDependencies hostDependencies;
    private readonly HashSet<HostedOverlayWindow> hostedWindows = [];
    private OverlayWindowManagementSession? windowManagementSession;
    private readonly IOverlayPlatformService? ownedPlatform;
    private bool disposed;

    private OverlayPresentationSession(
        OverlayPresentationDecision decision,
        CombinedOverlayPresentationController? combinedController,
        OverlayPresentationSessionDependencies hostDependencies,
        IOverlayPlatformService? ownedPlatform = null
    )
    {
        Decision = decision;
        this.combinedController = combinedController;
        this.hostDependencies = hostDependencies;
        this.ownedPlatform = ownedPlatform;
    }

    public OverlayPresentationDecision Decision { get; }

    internal LegacyOverlayLayout OverlayLayout => hostDependencies.OverlayLayout;

    internal OverlayWindowRegistry WindowRegistry => hostDependencies.WindowRegistry ?? OverlayWindowRegistry.Shared;

    public static OverlayPresentationSession CreateCurrent(
        IGameWindowTracker? gameWindowTracker = null,
        OverlayWindowRegistry? registry = null
    )
    {
        return CreateCurrent(
            gameWindowTracker,
            registry,
            LegacyOverlayLayout.Empty,
            () => false,
            diagnosticSink: null,
            gameWindowTrackerFactory: null
        );
    }

    internal static OverlayPresentationSession CreateCurrent(
        IGameWindowTracker? gameWindowTracker,
        OverlayWindowRegistry? registry,
        LegacyOverlayLayout overlayLayout,
        Func<bool> keepWhenGameLosesFocus,
        Action<OverlayHostDiagnostic>? diagnosticSink,
        Func<IGameWindowTracker>? gameWindowTrackerFactory = null
    )
    {
        ArgumentNullException.ThrowIfNull(overlayLayout);
        ArgumentNullException.ThrowIfNull(keepWhenGameLosesFocus);
        var capabilities = OverlayPlatformCapabilities.DetectCurrent();
        Func<IGameWindowTracker> trackerFactory = gameWindowTrackerFactory ?? GameWindowTracker.CreateCurrent;
        OverlayPresentationDecision decision = OverlayPresentationModeSelector.DetectCurrent(capabilities);
        if (decision.Mode != OverlayPresentationMode.CombinedWindow)
        {
            gameWindowTracker?.Dispose();
            return CreateWithSharedPlatform(
                decision,
                CreateHostDependencies(
                    OverlayPlatformService.CreateCurrent,
                    registry,
                    overlayLayout,
                    keepWhenGameLosesFocus,
                    diagnosticSink,
                    trackerFactory
                )
            );
        }

        IOverlayPlatformService nativePlatform = OverlayPlatformService.CreateCurrent();
        if (nativePlatform is not ICombinedOverlayNativeService)
        {
            gameWindowTracker?.Dispose();
            return CreateWithSharedPlatform(
                new OverlayPresentationDecision(
                    OverlayPresentationMode.MultipleWindows,
                    decision.Reason
                        + " The native combined-host operations were unavailable, so separate windows remain active."
                ),
                CreateHostDependencies(
                    () => nativePlatform,
                    registry,
                    overlayLayout,
                    keepWhenGameLosesFocus,
                    diagnosticSink,
                    trackerFactory
                )
            );
        }

        var controller = new CombinedOverlayPresentationController(
            nativePlatform,
            gameWindowTracker ?? trackerFactory(),
            registry
        );
        return new OverlayPresentationSession(
            decision,
            controller,
            CreateHostDependencies(
                () => new CombinedOverlayPlatformService(controller),
                registry,
                overlayLayout,
                keepWhenGameLosesFocus,
                diagnosticSink,
                trackerFactory
            )
        );
    }

    internal static OverlayPresentationSession CreateForAdapters(
        OverlayPresentationDecision decision,
        OverlayPresentationSessionDependencies dependencies
    )
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(dependencies);
        return new OverlayPresentationSession(decision, null, dependencies);
    }

    /// <summary>Creates the separate-window session with one shared native connection.</summary>
    internal static OverlayPresentationSession CreateWithSharedPlatform(
        OverlayPresentationDecision decision,
        OverlayPresentationSessionDependencies dependencies
    )
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(dependencies);
        IOverlayPlatformService shared = dependencies.CreatePlatform();
        return new OverlayPresentationSession(
            decision,
            null,
            dependencies with
            {
                CreatePlatform = () => new BorrowedOverlayPlatformService(shared),
            },
            shared
        );
    }

    public IOverlayPlatformService CreatePlatformService()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return hostDependencies.CreatePlatform();
    }

    internal IGameWindowTracker CreateGameWindowTracker()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return hostDependencies.CreateGameWindowTracker();
    }

    internal HostedOverlayWindow HostPassiveWindow(PassiveOverlayWindowDefinition definition)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        var hosted = new HostedOverlayWindow(definition, hostDependencies, RemoveHostedWindow);
        hostedWindows.Add(hosted);
        return hosted;
    }

    internal void ConfigureAuxiliaryWindow(Window window, string plotterName, bool applyOpacity = true)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (applyOpacity)
        {
            OverlayThemeResources.ApplyOpacity(window, hostDependencies.OverlayLayout, plotterName);
        }
        (hostDependencies.WindowRegistry ?? OverlayWindowRegistry.Shared).Register(
            window,
            plotterName,
            participatesInPlacement: false
        );
    }

    /// <summary>Classifies separate X11 overlays before mapping and applies their startup bypass preference.</summary>
    internal void ConfigureWindowManagement(bool bypassWindowManagement, Action<string>? log = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Decision.Mode != OverlayPresentationMode.MultipleWindows || windowManagementSession is not null)
        {
            return;
        }

        IOverlayPlatformService platform = ownedPlatform ?? hostDependencies.CreatePlatform();
        if (platform is IOverlayWindowManagement native)
        {
            windowManagementSession = new OverlayWindowManagementSession(
                hostDependencies.WindowRegistry ?? OverlayWindowRegistry.Shared,
                ownedPlatform is null ? native : new BorrowedWindowManagement(native),
                log,
                bypassWindowManagement
            );
            if (bypassWindowManagement)
            {
                log?.Invoke(
                    "Window management bypass is enabled for X11/XWayland live panels and the position editor."
                );
            }
        }
        else
        {
            if (ownedPlatform is null)
            {
                platform.Dispose();
            }
            if (bypassWindowManagement)
            {
                log?.Invoke(
                    "Window management bypass is unavailable on this display backend; using normal management."
                );
            }
        }
    }

    /// <summary>Disposes hosted panels and native presentation resources even if one dependent fails.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Exception? disposalFailure = null;
        try
        {
            foreach (HostedOverlayWindow? hosted in hostedWindows.ToArray())
            {
                try
                {
                    hosted.Dispose();
                }
                catch (Exception exception)
                {
                    disposalFailure ??= exception;
                }
            }
        }
        finally
        {
            hostedWindows.Clear();
            try
            {
                windowManagementSession?.Dispose();
            }
            catch (Exception exception)
            {
                disposalFailure ??= exception;
            }
            try
            {
                combinedController?.Dispose();
            }
            catch (Exception exception)
            {
                disposalFailure ??= exception;
            }
            try
            {
                ownedPlatform?.Dispose();
            }
            catch (Exception exception)
            {
                disposalFailure ??= exception;
            }
        }

        if (disposalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(disposalFailure).Throw();
        }
    }

    private static OverlayPresentationSessionDependencies CreateHostDependencies(
        Func<IOverlayPlatformService> platformFactory,
        OverlayWindowRegistry? registry,
        LegacyOverlayLayout overlayLayout,
        Func<bool> keepWhenGameLosesFocus,
        Action<OverlayHostDiagnostic>? diagnosticSink,
        Func<IGameWindowTracker> gameWindowTrackerFactory
    )
    {
        return new OverlayPresentationSessionDependencies(
            platformFactory,
            () => new OverlayGameWindowTracker(gameWindowTrackerFactory(), keepWhenGameLosesFocus),
            interval => new DispatcherHostedOverlayTimer(interval),
            overlayLayout,
            diagnosticSink,
            registry ?? OverlayWindowRegistry.Shared
        );
    }

    private void RemoveHostedWindow(HostedOverlayWindow hosted)
    {
        hostedWindows.Remove(hosted);
    }

    /// <summary>Forwards window operations while the containing session owns the native connection.</summary>
    private sealed class BorrowedOverlayPlatformService(IOverlayPlatformService platform) : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities => platform.Capabilities;

        public OverlayPreparationResult PreparePassiveWindow(Window window) => platform.PreparePassiveWindow(window);

        public OverlayInteractionResult PrepareInteractiveWindow(Window window) =>
            platform.PrepareInteractiveWindow(window);

        public OverlayInteractionResult SetInteractive(Window window, bool interactive) =>
            platform.SetInteractive(window, interactive);

        public IDisposable? BeginVisibleCursorSession(Window window) => platform.BeginVisibleCursorSession(window);

        public void BeginMoveDrag(Window window, PointerPressedEventArgs eventArgs) =>
            platform.BeginMoveDrag(window, eventArgs);

        public void Dispose() { }
    }

    private sealed class BorrowedWindowManagement(IOverlayWindowManagement native) : IOverlayWindowManagement
    {
        /// <summary>Forwards pre-map classification while the presentation session owns the native connection.</summary>
        public void PrepareOverlayWindow(Window window) => native.PrepareOverlayWindow(window);

        public bool TryBypassWindowManagement(Window window) => native.TryBypassWindowManagement(window);

        public void RaiseUnmanagedWindow(Window window, bool activate = false) =>
            native.RaiseUnmanagedWindow(window, activate);

        public void Dispose() { }
    }

    private sealed class CombinedOverlayPlatformService(CombinedOverlayPresentationController controller)
        : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities => controller.Capabilities;

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            return controller.PreparePassiveWindow(window);
        }

        public OverlayInteractionResult SetInteractive(Window window, bool interactive)
        {
            return controller.SetInteractive(window, interactive);
        }

        public IDisposable? BeginVisibleCursorSession(Window window)
        {
            return controller.BeginVisibleCursorSession(window);
        }

        public void BeginMoveDrag(Window window, PointerPressedEventArgs eventArgs)
        {
            controller.BeginMoveDrag(window, eventArgs);
        }

        public void Dispose()
        {
            // The application-owned session controls the shared host lifetime.
        }
    }
}
