using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class BiologyPredictionsWindowCoordinator : IDisposable
{
    private readonly BiologyPredictionsViewModel viewModel;
    private readonly Window owner;
    private BiologyPredictionsWindow? window;
    private bool disposed;

    public BiologyPredictionsWindowCoordinator(
        BiologyPredictionsViewModel viewModel,
        Window owner)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        viewModel.SetWindowOpener(ShowOrActivateAsync);
    }

    public bool IsVisible => window is not null;

    public Task<bool> ShowOrActivateAsync()
    {
        if (disposed || !viewModel.HasSystem)
        {
            return Task.FromResult(false);
        }

        if (window is not null)
        {
            window.Activate();
            return Task.FromResult(true);
        }

        var predictionsWindow = new BiologyPredictionsWindow(viewModel);
        predictionsWindow.Closed += OnWindowClosed;
        window = predictionsWindow;
        predictionsWindow.Show(owner);
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.SetWindowOpener(null);
        var predictionsWindow = window;
        window = null;
        if (predictionsWindow is not null)
        {
            predictionsWindow.Closed -= OnWindowClosed;
            predictionsWindow.Close();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is BiologyPredictionsWindow predictionsWindow)
        {
            predictionsWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, predictionsWindow))
            {
                window = null;
            }
        }
    }
}
