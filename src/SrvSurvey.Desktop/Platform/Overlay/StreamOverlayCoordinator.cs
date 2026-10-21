using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
        if (!hostedWindow.IsVisible)
        {
            viewModel.ActiveOverlayNames = [];
        }
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
        target.Width = gameBounds.Width / target.RenderScaling;
        target.Height = gameBounds.Height / target.RenderScaling;
        return gameBounds.Position;
    }

    /// <summary>Projects visible overlay bitmaps into the stream canvas while retaining capture resources for presentation.</summary>
    private void RenderFrames(StreamOverlayWindow target, PixelRect gameBounds)
    {
        var rendered = new List<StreamOverlayRenderedFrame>();
        var panelNames = new List<string>();
        try
        {
            foreach (RegisteredOverlayWindow registered in registry.Snapshot())
            {
                Window source = registered.Window;
                Visual renderSource = registered.RenderSource;
                Rect renderBounds = renderSource.Bounds;
                if (
                    !registered.IsVisible
                    || source.Opacity <= 0
                    || source is StreamOverlayWindow
                    || renderBounds.Width <= 0
                    || renderBounds.Height <= 0
                )
                {
                    continue;
                }

                double sourceScaling = TopLevel.GetTopLevel(renderSource)?.RenderScaling ?? source.RenderScaling;
                var pixelSize = PixelSize.FromSize(renderBounds.Size, sourceScaling);
                StreamOverlayFrame? projection = StreamOverlayProjection.Create(
                    gameBounds,
                    source.Position,
                    pixelSize,
                    target.RenderScaling,
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
                    if (!HasVisiblePixels(bitmap))
                    {
                        bitmap.Dispose();
                        bitmap = null;
                        continue;
                    }
                    rendered.Add(
                        new StreamOverlayRenderedFrame(
                            bitmap,
                            projection,
                            registered.PresentationVisual is null ? 1d : source.Opacity
                        )
                    );
                    bitmap = null;
                    panelNames.Add(OverlayLayoutCatalog.GetRequired(registered.PlotterName).DisplayName);
                }
                catch
                {
                    bitmap?.Dispose();
                }
            }

            target.ReplaceFrames(rendered);
            viewModel.ActiveOverlayNames = panelNames;
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

    private static bool HasVisiblePixels(RenderTargetBitmap bitmap)
    {
        using var pixels = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        bitmap.CopyPixels(buffer);
        int rowLength = checked(buffer.Size.Width * 4);
        byte[] row = ArrayPool<byte>.Shared.Rent(rowLength);
        try
        {
            for (int y = 0; y < buffer.Size.Height; y++)
            {
                Marshal.Copy(buffer.Address + (y * buffer.RowBytes), row, 0, rowLength);
                ReadOnlySpan<byte> bytes = row.AsSpan(0, rowLength);
                for (int alpha = 3; alpha < bytes.Length; alpha += 4)
                {
                    if (bytes[alpha] != 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
        }
    }
}
