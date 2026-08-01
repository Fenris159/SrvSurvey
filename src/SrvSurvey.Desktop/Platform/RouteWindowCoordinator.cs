using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class RouteWindowCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel viewModel;
    private readonly Window owner;
    private RouteWindow? window;
    private bool disposed;

    public RouteWindowCoordinator(
        RouteWorkspaceViewModel viewModel,
        Window owner)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        viewModel.SetWindowOpener(ShowOrActivateAsync);
    }

    public bool IsVisible => window is not null;

    public async Task<bool> ShowOrActivateAsync()
    {
        if (disposed || !viewModel.HasProfile)
        {
            return false;
        }

        if (window is not null)
        {
            window.Activate();
            return true;
        }

        viewModel.DismissDialogs();
        await viewModel.RefreshAsync();
        var routeWindow = new RouteWindow(viewModel);
        routeWindow.Closed += OnWindowClosed;
        window = routeWindow;
        routeWindow.Show(owner);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.SetWindowOpener(null);
        var routeWindow = window;
        window = null;
        if (routeWindow is not null)
        {
            routeWindow.Closed -= OnWindowClosed;
            routeWindow.Close();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is RouteWindow routeWindow)
        {
            routeWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, routeWindow))
            {
                window = null;
            }
        }
    }
}
