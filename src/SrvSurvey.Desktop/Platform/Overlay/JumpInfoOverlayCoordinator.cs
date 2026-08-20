using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class JumpInfoOverlayCoordinator : IDisposable
{
    private const string PlotterName = "PlotJumpInfo";

    private readonly JumpInfoViewModel jumpInfo;
    private readonly JumpInfoOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayDispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private JumpInfoOverlayWindow? window;
    private bool isSuppressed;
    private bool isSynchronizing;
    private bool disposed;

    public JumpInfoOverlayCoordinator(
        JumpInfoViewModel jumpInfo,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayout? overlayLayout = null,
        SystemNicknameViewModel? systemNicknames = null)
    {
        this.jumpInfo = jumpInfo
            ?? throw new ArgumentNullException(nameof(jumpInfo));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.overlayLayout = overlayLayout ?? LegacyOverlayLayout.Empty;
        viewModel = new JumpInfoOverlayViewModel(
            jumpInfo,
            platform.Capabilities,
            systemNicknames);
        jumpInfo.PropertyChanged += OnJumpInfoPropertyChanged;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindow();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => window is not null;

    public bool IsSuppressed => isSuppressed;

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeWindow();
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
        jumpInfo.PropertyChanged -= OnJumpInfoPropertyChanged;
        viewModel.Dispose();
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        jumpInfo.AdvanceTimedTransitions();
        SynchronizeWindow();
    }

    private void OnJumpInfoPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(JumpInfoViewModel.ShouldShow))
        {
            SynchronizeWindow();
        }
    }

    private void SynchronizeWindow()
    {
        if (disposed || isSynchronizing)
        {
            return;
        }

        isSynchronizing = true;
        try
        {
            gameWindow = gameWindowTracker.GetSnapshot();
            if (isSuppressed
                || !jumpInfo.ShouldShow
                || !platform.Capabilities.SupportsPassiveOverlay
                || !platform.Capabilities.SupportsClickThrough
                || !platform.Capabilities.SupportsGameWindowTracking
                || !gameWindow.IsAvailable
                || !gameWindow.IsVisible
                || !gameWindow.IsForeground)
            {
                CloseWindow();
                return;
            }

            if (window is not null)
            {
                PositionWindow(window, gameWindow.ClientBounds);
                return;
            }

            jumpInfo.BeginOverlayPresentation();
            if (!jumpInfo.ShouldShow)
            {
                jumpInfo.EndOverlayPresentation();
                return;
            }

            JumpInfoOverlayWindow? overlay = null;
            var presentationCompleted = false;
            var presentationEnded = false;
            try
            {
                overlay = new JumpInfoOverlayWindow(viewModel);
                OverlayThemeResources.Apply(
                    overlay,
                    overlayLayout,
                    PlotterName);
                overlay.Opened += (_, _) =>
                {
                    PositionWindow(overlay, gameWindow.ClientBounds);
                    var preparation = platform.PreparePassiveWindow(overlay);
                    viewModel.ApplyPreparation(preparation);
                    if (!preparation.IsClickThrough)
                    {
                        isSuppressed = true;
                        CloseWindow();
                    }
                };
                overlay.Closed += (_, _) =>
                {
                    if (ReferenceEquals(window, overlay))
                    {
                        window = null;
                        jumpInfo.EndOverlayPresentation();
                        presentationEnded = true;
                        VisibilityChanged?.Invoke(this, EventArgs.Empty);
                    }
                };
                window = overlay;
                overlay.Show();
                presentationCompleted = true;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                if (!presentationCompleted && !presentationEnded)
                {
                    if (ReferenceEquals(window, overlay))
                    {
                        window = null;
                    }

                    jumpInfo.EndOverlayPresentation();
                }
            }
        }
        finally
        {
            isSynchronizing = false;
        }
    }

    private void PositionWindow(Window window, PixelRect gameBounds)
    {
        OverlayThemeResources.ApplyOpacity(
            window,
            overlayLayout,
            PlotterName);
        var screen = window.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.PrepareForPlacement(
            window, overlayLayout, PlotterName, screen.Scaling);
        var position = overlayLayout.GetPosition(PlotterName, gameBounds, size)
            ?? OverlayWindowPlacement.TopCenter(gameBounds, size);
        if (window.Position != position)
        {
            window.Position = position;
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
        jumpInfo.EndOverlayPresentation();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
