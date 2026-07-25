using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class HumanSiteOverlayCoordinator : IDisposable
{
    private readonly HumanSiteViewModel humanSite;
    private readonly HumanSiteOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly DispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private HumanSiteOverlayWindow? window;
    private bool isSuppressed;
    private bool disposed;

    public HumanSiteOverlayCoordinator(
        HumanSiteViewModel humanSite,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker)
    {
        this.humanSite = humanSite
            ?? throw new ArgumentNullException(nameof(humanSite));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        viewModel = new HumanSiteOverlayViewModel(
            humanSite,
            platform.Capabilities);
        humanSite.PropertyChanged += OnHumanSitePropertyChanged;
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindow();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => window is not null;

    public void SetSuppressed(bool value)
    {
        if (disposed || isSuppressed == value)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeWindow();
    }

    public bool AdjustZoom(bool zoomIn)
    {
        if (disposed || !IsVisible)
        {
            return false;
        }

        humanSite.AdjustZoom(zoomIn);
        return true;
    }

    public bool ResetZoom()
    {
        if (disposed || !IsVisible)
        {
            return false;
        }

        humanSite.EnableAutomaticZoom();
        return true;
    }

    public bool ToggleHuge()
    {
        if (disposed || !IsVisible)
        {
            return false;
        }

        humanSite.ToggleHuge();
        SynchronizeWindow();
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
        humanSite.PropertyChanged -= OnHumanSitePropertyChanged;
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeWindow();
    }

    private void OnHumanSitePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(HumanSiteViewModel.ShouldShow)
            or nameof(HumanSiteViewModel.IsHuge)
            or nameof(HumanSiteViewModel.PreferredWidth)
            or nameof(HumanSiteViewModel.PreferredHeight))
        {
            SynchronizeWindow();
        }
    }

    private void SynchronizeWindow()
    {
        if (disposed)
        {
            return;
        }

        gameWindow = gameWindowTracker.GetSnapshot();
        if (isSuppressed
            || !humanSite.ShouldShow
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
            SizeAndPositionWindow(window, gameWindow.ClientBounds);
            return;
        }

        var overlay = new HumanSiteOverlayWindow(viewModel);
        overlay.Opened += (_, _) =>
        {
            SizeAndPositionWindow(overlay, gameWindow.ClientBounds);
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
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        window = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SizeAndPositionWindow(Window overlay, PixelRect gameBounds)
    {
        var screen = overlay.Screens.ScreenFromBounds(gameBounds)
            ?? overlay.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var logicalWidth = humanSite.IsHuge
            ? gameBounds.Width * 0.4 / screen.Scaling
            : humanSite.PreferredWidth;
        var logicalHeight = humanSite.IsHuge
            ? gameBounds.Height * 0.9 / screen.Scaling
            : humanSite.PreferredHeight;
        if (Math.Abs(overlay.Width - logicalWidth) > 0.5)
        {
            overlay.Width = logicalWidth;
        }

        if (Math.Abs(overlay.Height - logicalHeight) > 0.5)
        {
            overlay.Height = logicalHeight;
        }

        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(logicalWidth * screen.Scaling)),
            Math.Max(1, (int)Math.Ceiling(logicalHeight * screen.Scaling)));
        var position = OverlayWindowPlacement.MiddleLeft(
            gameBounds,
            pixelSize,
            margin: 8);
        if (overlay.Position != position)
        {
            overlay.Position = position;
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
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
