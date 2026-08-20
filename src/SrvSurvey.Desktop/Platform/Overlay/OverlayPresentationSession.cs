using Avalonia.Controls;
using Avalonia.Input;
using System.Runtime.ExceptionServices;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayPresentationSession : IDisposable
{
    private readonly CombinedOverlayPresentationController? combinedController;
    private readonly OverlayPresentationSessionDependencies hostDependencies;
    private readonly HashSet<HostedOverlayWindow> hostedWindows = [];
    private bool disposed;

    private OverlayPresentationSession(
        OverlayPresentationDecision decision,
        CombinedOverlayPresentationController? combinedController,
        OverlayPresentationSessionDependencies hostDependencies)
    {
        Decision = decision;
        this.combinedController = combinedController;
        this.hostDependencies = hostDependencies;
    }

    public OverlayPresentationDecision Decision { get; }

    public static OverlayPresentationSession CreateCurrent(
        IGameWindowTracker? gameWindowTracker = null,
        OverlayWindowRegistry? registry = null)
    {
        return CreateCurrent(
            gameWindowTracker,
            registry,
            LegacyOverlayLayout.Empty,
            () => false,
            diagnosticSink: null);
    }

    internal static OverlayPresentationSession CreateCurrent(
        IGameWindowTracker? gameWindowTracker,
        OverlayWindowRegistry? registry,
        LegacyOverlayLayout overlayLayout,
        Func<bool> keepWhenGameLosesFocus,
        Action<OverlayHostDiagnostic>? diagnosticSink)
    {
        ArgumentNullException.ThrowIfNull(overlayLayout);
        ArgumentNullException.ThrowIfNull(keepWhenGameLosesFocus);
        var capabilities = OverlayPlatformCapabilities.DetectCurrent();
        var decision = OverlayPresentationModeSelector.DetectCurrent(
            capabilities);
        if (decision.Mode != OverlayPresentationMode.CombinedWindow)
        {
            gameWindowTracker?.Dispose();
            return new OverlayPresentationSession(
                decision,
                null,
                CreateHostDependencies(
                    OverlayPlatformService.CreateCurrent,
                    registry,
                    overlayLayout,
                    keepWhenGameLosesFocus,
                    diagnosticSink));
        }

        var nativePlatform = OverlayPlatformService.CreateCurrent();
        if (nativePlatform is not ICombinedOverlayNativeService)
        {
            nativePlatform.Dispose();
            gameWindowTracker?.Dispose();
            return new OverlayPresentationSession(
                new OverlayPresentationDecision(
                    OverlayPresentationMode.MultipleWindows,
                    decision.Reason
                        + " The native combined-host operations were unavailable, so separate windows remain active."),
                null,
                CreateHostDependencies(
                    OverlayPlatformService.CreateCurrent,
                    registry,
                    overlayLayout,
                    keepWhenGameLosesFocus,
                    diagnosticSink));
        }

        var controller = new CombinedOverlayPresentationController(
            nativePlatform,
            gameWindowTracker ?? GameWindowTracker.CreateCurrent(),
            registry);
        return new OverlayPresentationSession(
            decision,
            controller,
            CreateHostDependencies(
                () => new CombinedOverlayPlatformService(controller),
                registry,
                overlayLayout,
                keepWhenGameLosesFocus,
                diagnosticSink));
    }

    internal static OverlayPresentationSession CreateForAdapters(
        OverlayPresentationDecision decision,
        OverlayPresentationSessionDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(dependencies);
        return new OverlayPresentationSession(decision, null, dependencies);
    }

    public IOverlayPlatformService CreatePlatformService()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return combinedController is null
            ? OverlayPlatformService.CreateCurrent()
            : new CombinedOverlayPlatformService(combinedController);
    }

    internal HostedOverlayWindow HostPassiveWindow(
        PassiveOverlayWindowDefinition definition)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        var hosted = new HostedOverlayWindow(
            definition,
            hostDependencies,
            RemoveHostedWindow);
        hostedWindows.Add(hosted);
        return hosted;
    }

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
            foreach (var hosted in hostedWindows.ToArray())
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
                combinedController?.Dispose();
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
        Action<OverlayHostDiagnostic>? diagnosticSink)
    {
        return new OverlayPresentationSessionDependencies(
            platformFactory,
            () => new OverlayGameWindowTracker(
                GameWindowTracker.CreateCurrent(),
                keepWhenGameLosesFocus),
            interval => new DispatcherHostedOverlayTimer(interval),
            overlayLayout,
            diagnosticSink,
            registry ?? OverlayWindowRegistry.Shared);
    }

    private void RemoveHostedWindow(HostedOverlayWindow hosted)
    {
        hostedWindows.Remove(hosted);
    }

    private sealed class CombinedOverlayPlatformService(
        CombinedOverlayPresentationController controller)
        : IOverlayPlatformService, IOverlayPresentationControl
    {
        public OverlayPlatformCapabilities Capabilities =>
            controller.Capabilities;

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            return controller.PreparePassiveWindow(window);
        }

        public OverlayInteractionResult SetInteractive(
            Window window,
            bool interactive)
        {
            return controller.SetInteractive(window, interactive);
        }

        public IDisposable? BeginVisibleCursorSession(Window window)
        {
            return controller.BeginVisibleCursorSession(window);
        }

        public void BeginMoveDrag(
            Window window,
            PointerPressedEventArgs eventArgs)
        {
            controller.BeginMoveDrag(window, eventArgs);
        }

        public void SetRuntimeOverlaysSuppressed(bool suppressed)
        {
            controller.SetRuntimeOverlaysSuppressed(suppressed);
        }

        public void Dispose()
        {
            // The application-owned session controls the shared host lifetime.
        }
    }
}
