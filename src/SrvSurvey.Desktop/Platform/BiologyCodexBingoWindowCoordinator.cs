using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class BiologyCodexBingoWindowCoordinator : IDisposable
{
    private readonly BiologyCodexBingoViewModel viewModel;
    private readonly Window owner;
    private readonly Func<CodexBingoNearestRequest, Task> nearestSearchHandler;
    private BiologyCodexBingoWindow? window;
    private bool disposed;

    public BiologyCodexBingoWindowCoordinator(
        BiologyCodexBingoViewModel viewModel,
        Window owner,
        Func<CodexBingoNearestRequest, Task> nearestSearchHandler)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.nearestSearchHandler = nearestSearchHandler
            ?? throw new ArgumentNullException(nameof(nearestSearchHandler));
        viewModel.SetWindowOpener(ShowOrActivateAsync);
        viewModel.SetNearestSearchHandler(OpenNearestSearchAsync);
    }

    public bool IsVisible => window is not null;

    public async Task<bool> ShowOrActivateAsync()
    {
        if (disposed)
        {
            return false;
        }

        if (window is not null)
        {
            window.Activate();
            return true;
        }

        await viewModel.EnsureInitializedAsync();
        if (disposed)
        {
            return false;
        }

        var bingoWindow = new BiologyCodexBingoWindow(viewModel);
        bingoWindow.Closed += OnWindowClosed;
        window = bingoWindow;
        bingoWindow.Show(owner);
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
        viewModel.SetNearestSearchHandler(null);
        var bingoWindow = window;
        window = null;
        if (bingoWindow is not null)
        {
            bingoWindow.Closed -= OnWindowClosed;
            bingoWindow.Close();
        }
    }

    private async Task OpenNearestSearchAsync(CodexBingoNearestRequest request)
    {
        await nearestSearchHandler(request);
        window?.Close();
        owner.Activate();
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is BiologyCodexBingoWindow bingoWindow)
        {
            bingoWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, bingoWindow))
            {
                window = null;
            }
        }
    }
}
