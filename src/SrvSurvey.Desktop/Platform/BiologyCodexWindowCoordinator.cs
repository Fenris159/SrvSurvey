using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class BiologyCodexWindowCoordinator : IDisposable
{
    private readonly BiologyCodexViewModel viewModel;
    private readonly Window owner;
    private readonly CodexImageCache imageCache;
    private BiologyCodexWindow? window;
    private bool disposed;

    public BiologyCodexWindowCoordinator(
        BiologyCodexViewModel viewModel,
        Window owner,
        string imageCacheDirectory)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        imageCache = new CodexImageCache(imageCacheDirectory);
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

        var codexWindow = new BiologyCodexWindow(viewModel, imageCache);
        codexWindow.Closed += OnWindowClosed;
        window = codexWindow;
        codexWindow.Show(owner);
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
        var codexWindow = window;
        window = null;
        if (codexWindow is not null)
        {
            codexWindow.Closed -= OnWindowClosed;
            codexWindow.Close();
        }

        imageCache.Dispose();
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is BiologyCodexWindow codexWindow)
        {
            codexWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, codexWindow))
            {
                window = null;
            }
        }
    }
}
