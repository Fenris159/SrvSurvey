using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class FleetCarrierJumpCountdownCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel route;
    private readonly OverlayDispatcherTimer timer;
    private bool isRunning;
    private bool disposed;

    public FleetCarrierJumpCountdownCoordinator(RouteWorkspaceViewModel route)
    {
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        timer.Tick += OnTimerTick;
        route.PropertyChanged += OnRoutePropertyChanged;
        SynchronizeTimer();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        route.PropertyChanged -= OnRoutePropertyChanged;
        timer.Tick -= OnTimerTick;
        if (isRunning)
        {
            timer.Stop();
            isRunning = false;
        }
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        route.RefreshCarrierJumpCountdown();
    }

    private void OnRoutePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName ==
            nameof(RouteWorkspaceViewModel.HasCarrierJumpCountdown))
        {
            SynchronizeTimer();
        }
    }

    private void SynchronizeTimer()
    {
        var shouldRun = !disposed && route.HasCarrierJumpCountdown;
        if (shouldRun == isRunning)
        {
            return;
        }

        isRunning = shouldRun;
        if (shouldRun)
        {
            timer.Start();
        }
        else
        {
            timer.Stop();
        }
    }
}
