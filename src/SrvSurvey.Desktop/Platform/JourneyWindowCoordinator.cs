using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class JourneyWindowCoordinator : IDisposable
{
    private readonly JourneyWorkspaceViewModel viewModel;
    private readonly Window owner;
    private JourneyWindow? window;
    private bool disposed;

    public JourneyWindowCoordinator(
        JourneyWorkspaceViewModel viewModel,
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

        await viewModel.RefreshAsync();
        var journeyWindow = new JourneyWindow(viewModel);
        journeyWindow.Closed += OnWindowClosed;
        window = journeyWindow;
        journeyWindow.Show(owner);
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
        var journeyWindow = window;
        window = null;
        if (journeyWindow is not null)
        {
            journeyWindow.Closed -= OnWindowClosed;
            journeyWindow.Close();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is JourneyWindow journeyWindow)
        {
            journeyWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, journeyWindow))
            {
                window = null;
            }
        }
    }
}
