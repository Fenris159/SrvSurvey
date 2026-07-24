using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class SystemNotesWindowCoordinator : IDisposable
{
    private readonly SystemNotesViewModel viewModel;
    private readonly Window owner;
    private SystemNotesWindow? window;
    private bool isOpening;
    private bool disposed;

    public SystemNotesWindowCoordinator(
        SystemNotesViewModel viewModel,
        Window owner)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.owner = owner
            ?? throw new ArgumentNullException(nameof(owner));
        this.viewModel.SetWindowOpener(ShowOrActivateAsync);
    }

    public bool IsVisible => window is not null;

    public async Task<bool> ShowOrActivateAsync()
    {
        if (disposed || !viewModel.HasCurrentSystem)
        {
            return false;
        }

        if (window is not null)
        {
            window.Activate();
            return true;
        }

        if (isOpening)
        {
            return true;
        }

        isOpening = true;
        try
        {
            if (!await viewModel.LoadCurrentAsync())
            {
                return false;
            }

            var notesWindow = new SystemNotesWindow(viewModel);
            notesWindow.Closed += OnWindowClosed;
            window = notesWindow;
            notesWindow.Show(owner);
            return true;
        }
        finally
        {
            isOpening = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.SetWindowOpener(null);
        var notesWindow = window;
        window = null;
        if (notesWindow is not null)
        {
            notesWindow.Closed -= OnWindowClosed;
            notesWindow.Close();
        }

        viewModel.CloseSession();
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is SystemNotesWindow notesWindow)
        {
            notesWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, notesWindow))
            {
                window = null;
            }
        }

        viewModel.CloseSession();
    }
}
