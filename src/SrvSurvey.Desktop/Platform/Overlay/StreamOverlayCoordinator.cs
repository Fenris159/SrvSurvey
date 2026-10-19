using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class StreamOverlayCoordinator : IDisposable
{
    private readonly StreamOverlayViewModel viewModel;
    private readonly OverlayWindowRegistry registry;
    private readonly HostedOverlayWindow hostedWindow;
    private readonly TimeProvider timeProvider;
    private long? unavailableSince;
    private PixelRect captureBounds;
    private nint captureHandle;
    private bool disposed;

    public StreamOverlayCoordinator(StreamOverlayViewModel viewModel, OverlayPresentationSession presentationSession)
        : this(viewModel, presentationSession, TimeProvider.System) { }

    internal StreamOverlayCoordinator(
        StreamOverlayViewModel viewModel,
        OverlayPresentationSession presentationSession,
        TimeProvider timeProvider
    )
    {
        this.timeProvider = timeProvider;
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        registry = presentationSession.WindowRegistry;
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "SrvSurveyWindowOne",
                _ => new StreamOverlayWindow(),
                (gameBounds, _) => gameBounds.Position
            )
            {
                Tick = OnTick,
                Placement = PlaceOverGame,
                ObservePreparation = ObservePreparation,
                RequiresLayoutCatalog = false,
                ApplyLayoutTheme = false,
                RetryPassivePreparationOnPoll = true,
                IsGameWindowEligible = IsGameWindowEligible,
            }
        );
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        registry.Changed += OnRegistryChanged;
        SynchronizeIntent();
    }

    public bool Toggle()
    {
        if (disposed)
        {
            return false;
        }

        viewModel.Toggle();
        SynchronizeIntent();
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        registry.Changed -= OnRegistryChanged;
        hostedWindow.Dispose();
    }

    private void OnTick()
    {
        SynchronizeIntent();
        if (hostedWindow.CurrentWindow is StreamOverlayWindow window)
        {
            RenderFrames(window, captureBounds);
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizeIntent();
        if (hostedWindow.CurrentWindow is StreamOverlayWindow window)
        {
            RenderFrames(window, captureBounds);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(StreamOverlayViewModel.Enabled))
        {
            SynchronizeIntent();
        }
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        hostedWindow.Reconcile(viewModel.Enabled);
        if (
            !viewModel.Enabled
            || hostedWindow.IsVisible
            || hostedWindow.Health == OverlayHostHealth.PassivePreparationFailed
        )
        {
            return;
        }

        OverlayPlatformCapabilities capabilities = hostedWindow.Capabilities;
        if (
            !capabilities.SupportsPassiveOverlay
            || !capabilities.SupportsClickThrough
            || !capabilities.SupportsGameWindowTracking
        )
        {
            viewModel.StatusMessage = capabilities.StatusText;
            return;
        }

        viewModel.StatusMessage = "Waiting for the Elite window before composing overlays.";
    }

    private void ObservePreparation(OverlayPreparationResult preparation)
    {
        if (!preparation.IsClickThrough)
        {
            viewModel.StatusMessage = preparation.Status;
        }
    }

    /// <summary>Keeps the capture handle across minimization without accepting iconic or empty geometry.</summary>
    private bool IsGameWindowEligible(GameWindowSnapshot snapshot)
    {
        if (snapshot.NativeHandle == nint.Zero)
        {
            unavailableSince ??= timeProvider.GetTimestamp();
            if (timeProvider.GetElapsedTime(unavailableSince.Value) < TimeSpan.FromSeconds(2))
            {
                return captureHandle != nint.Zero && captureBounds.Width > 0 && captureBounds.Height > 0;
            }
            captureHandle = nint.Zero;
            captureBounds = default;
            return false;
        }
        unavailableSince = null;
        if (captureHandle != snapshot.NativeHandle)
        {
            captureHandle = snapshot.NativeHandle;
            captureBounds = default;
        }
        if (snapshot.IsAvailable && snapshot.IsVisible)
        {
            captureBounds = snapshot.ClientBounds;
        }
        return captureHandle != nint.Zero && captureBounds.Width > 0 && captureBounds.Height > 0;
    }

    private PixelPoint PlaceOverGame(HostedOverlayPlacement placement)
    {
        PixelRect gameBounds = captureBounds;
        Window target = placement.Window;
        Screen screen = target.Screens.ScreenFromBounds(gameBounds) ?? placement.Screen;
        target.Width = gameBounds.Width / screen.Scaling;
        target.Height = gameBounds.Height / screen.Scaling;
        return gameBounds.Position;
    }

    /// <summary>Projects visible overlay bitmaps into the stream canvas while retaining capture resources for presentation.</summary>
    private void RenderFrames(StreamOverlayWindow target, PixelRect gameBounds)
    {
        Screen? screen = target.Screens.ScreenFromBounds(gameBounds) ?? target.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var rendered = new List<StreamOverlayRenderedFrame>();
        try
        {
            foreach (RegisteredOverlayWindow registered in registry.Snapshot())
            {
                Window source = registered.Window;
                Visual renderSource = registered.RenderSource;
                Rect renderBounds = renderSource.Bounds;
                if (!registered.IsVisible || renderBounds.Width <= 0 || renderBounds.Height <= 0)
                {
                    continue;
                }

                double sourceScaling = source.RenderScaling;
                var pixelSize = PixelSize.FromSize(renderBounds.Size, sourceScaling);
                StreamOverlayFrame? projection = StreamOverlayProjection.Create(
                    gameBounds,
                    source.Position,
                    pixelSize,
                    screen.Scaling,
                    keepInsideCanvas: true
                );
                if (projection is null)
                {
                    continue;
                }

                RenderTargetBitmap? bitmap = null;
                try
                {
                    bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * sourceScaling, 96 * sourceScaling));
                    bitmap.Render(renderSource);
                    rendered.Add(
                        new StreamOverlayRenderedFrame(
                            bitmap,
                            projection,
                            registered.PresentationVisual is null ? 1d : source.Opacity
                        )
                    );
                    bitmap = null;
                }
                catch
                {
                    bitmap?.Dispose();
                }
            }

            target.ReplaceFrames(rendered);
            viewModel.StatusMessage = $"Compositing {rendered.Count:N0} live overlays into SrvSurveyWindowOne.";
        }
        catch (Exception exception)
        {
            foreach (StreamOverlayRenderedFrame frame in rendered)
            {
                frame.Bitmap.Dispose();
            }

            viewModel.StatusMessage = $"The joined stream overlay could not update: {exception.Message}";
        }
    }
}
