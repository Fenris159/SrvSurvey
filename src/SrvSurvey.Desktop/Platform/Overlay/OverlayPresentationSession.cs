using Avalonia.Controls;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayPresentationSession : IDisposable
{
    private readonly CombinedOverlayPresentationController? combinedController;
    private bool disposed;

    private OverlayPresentationSession(
        OverlayPresentationDecision decision,
        CombinedOverlayPresentationController? combinedController)
    {
        Decision = decision;
        this.combinedController = combinedController;
    }

    public OverlayPresentationDecision Decision { get; }

    public static OverlayPresentationSession CreateCurrent(
        IGameWindowTracker? gameWindowTracker = null,
        OverlayWindowRegistry? registry = null)
    {
        var capabilities = OverlayPlatformCapabilities.DetectCurrent();
        var decision = OverlayPresentationModeSelector.DetectCurrent(
            capabilities);
        if (decision.Mode != OverlayPresentationMode.CombinedWindow)
        {
            gameWindowTracker?.Dispose();
            return new OverlayPresentationSession(decision, null);
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
                null);
        }

        var controller = new CombinedOverlayPresentationController(
            nativePlatform,
            gameWindowTracker ?? GameWindowTracker.CreateCurrent(),
            registry);
        return new OverlayPresentationSession(decision, controller);
    }

    public IOverlayPlatformService CreatePlatformService()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return combinedController is null
            ? OverlayPlatformService.CreateCurrent()
            : new CombinedOverlayPlatformService(combinedController);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        combinedController?.Dispose();
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
