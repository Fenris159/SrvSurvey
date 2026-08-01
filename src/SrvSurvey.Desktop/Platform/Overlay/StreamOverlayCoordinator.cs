using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class StreamOverlayCoordinator : IDisposable
{
    private readonly StreamOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly OverlayWindowRegistry registry;
    private readonly OverlayDispatcherTimer timer;
    private StreamOverlayWindow? window;
    private bool disposed;

    public StreamOverlayCoordinator(
        StreamOverlayViewModel viewModel,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        OverlayWindowRegistry? registry = null)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        this.registry.Changed += OnRegistryChanged;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        Synchronize();
    }

    public bool Toggle()
    {
        if (disposed)
        {
            return false;
        }

        viewModel.Toggle();
        Synchronize();
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        registry.Changed -= OnRegistryChanged;
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(StreamOverlayViewModel.Enabled))
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        if (disposed || !viewModel.Enabled)
        {
            CloseWindow();
            return;
        }

        if (!platform.Capabilities.SupportsPassiveOverlay
            || !platform.Capabilities.SupportsClickThrough
            || !platform.Capabilities.SupportsGameWindowTracking)
        {
            CloseWindow();
            viewModel.StatusMessage = platform.Capabilities.StatusText;
            return;
        }

        var gameWindow = gameWindowTracker.GetSnapshot();
        if (!gameWindow.IsAvailable || !gameWindow.IsVisible)
        {
            CloseWindow();
            viewModel.StatusMessage =
                "Waiting for the Elite window before composing overlays.";
            return;
        }

        EnsureWindow(gameWindow.ClientBounds);
        if (window is null)
        {
            return;
        }

        PositionWindow(window, gameWindow.ClientBounds);
        RenderFrames(window, gameWindow.ClientBounds);
    }

    private void EnsureWindow(PixelRect gameBounds)
    {
        if (window is not null)
        {
            return;
        }

        var overlay = new StreamOverlayWindow();
        overlay.Opened += (_, _) =>
        {
            PositionWindow(overlay, gameBounds);
            var preparation = platform.PreparePassiveWindow(overlay);
            if (!preparation.IsClickThrough)
            {
                viewModel.StatusMessage = preparation.Status;
                CloseWindow();
            }
        };
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, overlay))
            {
                window = null;
            }
        };
        window = overlay;
        overlay.Show();
    }

    private static void PositionWindow(Window target, PixelRect gameBounds)
    {
        var screen = target.Screens.ScreenFromBounds(gameBounds)
            ?? target.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        target.Position = gameBounds.Position;
        target.Width = gameBounds.Width / screen.Scaling;
        target.Height = gameBounds.Height / screen.Scaling;
    }

    private void RenderFrames(StreamOverlayWindow target, PixelRect gameBounds)
    {
        var screen = target.Screens.ScreenFromBounds(gameBounds)
            ?? target.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var rendered = new List<StreamOverlayRenderedFrame>();
        try
        {
            foreach (var registered in registry.Snapshot())
            {
                var source = registered.Window;
                var renderSource = registered.RenderSource;
                var renderBounds = renderSource.Bounds;
                if (!registered.IsVisible
                    || renderBounds.Width <= 0
                    || renderBounds.Height <= 0)
                {
                    continue;
                }

                var sourceScaling = source.RenderScaling;
                var pixelSize = PixelSize.FromSize(
                    renderBounds.Size,
                    sourceScaling);
                var projection = StreamOverlayProjection.Create(
                    gameBounds,
                    source.Position,
                    pixelSize,
                    screen.Scaling);
                if (projection is null)
                {
                    continue;
                }

                RenderTargetBitmap? bitmap = null;
                try
                {
                    bitmap = new RenderTargetBitmap(
                        pixelSize,
                        new Vector(96 * sourceScaling, 96 * sourceScaling));
                    bitmap.Render(renderSource);
                    rendered.Add(new StreamOverlayRenderedFrame(
                        bitmap,
                        projection,
                        registered.PresentationVisual is null
                            ? 1d
                            : source.Opacity));
                    bitmap = null;
                }
                catch
                {
                    bitmap?.Dispose();
                }
            }

            target.ReplaceFrames(rendered);
            viewModel.StatusMessage =
                $"Compositing {rendered.Count:N0} live overlays into SrvSurveyWindowOne.";
        }
        catch (Exception exception)
        {
            foreach (var frame in rendered)
            {
                frame.Bitmap.Dispose();
            }

            viewModel.StatusMessage =
                $"The joined stream overlay could not update: {exception.Message}";
        }
    }

    private void CloseWindow()
    {
        var overlay = window;
        if (overlay is null)
        {
            return;
        }

        window = null;
        overlay.Close();
    }
}
