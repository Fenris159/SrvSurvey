using System.ComponentModel;
using Avalonia.Controls;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Maintains one popout per build, permitting multiple previews while closing their reads on profile, consent, or application changes.</summary>
public sealed class ColonizationProjectPreviewWindowCoordinator : IDisposable
{
    private readonly ColonizationViewModel viewModel;
    private readonly Window owner;
    private readonly Dictionary<string, ColonizationProjectPreviewWindow> windows = new(
        StringComparer.OrdinalIgnoreCase
    );
    private bool disposed;

    /// <summary>Connects project hyperlinks to the native popout lifetime.</summary>
    public ColonizationProjectPreviewWindowCoordinator(ColonizationViewModel viewModel, Window owner)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        viewModel.SetProjectPreviewOpener(ShowOrActivate);
        viewModel.SetCombinedReportOpener(ShowCombinedReport);
        viewModel.PropertyChanged += OnContextChanged;
        owner.Closed += OnOwnerClosed;
    }

    internal IReadOnlyCollection<ColonizationProjectPreviewWindow> Windows => windows.Values;

    /// <summary>Activates an existing build window or immediately opens a new independent preview.</summary>
    public void ShowOrActivate(ColonizationProject project)
    {
        if (disposed || !viewModel.IsEnabled || string.IsNullOrWhiteSpace(project.BuildId))
        {
            return;
        }
        ShowWindow(project.BuildId, () => viewModel.CreateProjectPreview(project.BuildId));
    }

    /// <summary>Maintains one combined report alongside any independent build popouts.</summary>
    internal void ShowCombinedReport()
    {
        if (!disposed && viewModel.IsEnabled && viewModel.HasProjects)
        {
            ShowWindow("combined-report", viewModel.CreateCombinedReport);
        }
    }

    /// <summary>Restores an existing popout or starts its isolated refresh loop with the shared owner.</summary>
    private void ShowWindow(string key, Func<ColonizationProjectPreviewViewModel> createPreview)
    {
        if (windows.TryGetValue(key, out ColonizationProjectPreviewWindow? existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }
            existing.Activate();
            return;
        }
        try
        {
            var window = new ColonizationProjectPreviewWindow(createPreview());
            windows.Add(key, window);
            window.Closed += OnPreviewClosed;
            window.Show(owner);
        }
        catch (InvalidOperationException exception)
        {
            viewModel.ReportLinkFailure(exception.Message);
        }
    }

    /// <summary>Detaches launch and profile handlers and closes every open preview.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        viewModel.SetProjectPreviewOpener(null);
        viewModel.SetCombinedReportOpener(null);
        viewModel.PropertyChanged -= OnContextChanged;
        owner.Closed -= OnOwnerClosed;
        CloseAll();
    }

    /// <summary>Forgets a closed build so its next click starts a fresh live session.</summary>
    private void OnPreviewClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is ColonizationProjectPreviewWindow window)
        {
            window.Closed -= OnPreviewClosed;
            string? key = windows.FirstOrDefault(pair => ReferenceEquals(pair.Value, window)).Key;
            if (key is not null)
            {
                windows.Remove(key);
            }
        }
    }

    /// <summary>Stops public requests when Raven access is disabled or the active commander changes.</summary>
    private void OnContextChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName == nameof(ColonizationViewModel.CommanderName)
            || eventArgs.PropertyName == nameof(ColonizationViewModel.IsEnabled) && !viewModel.IsEnabled
        )
        {
            CloseAll();
        }
    }

    /// <summary>Closes popouts even when the owner closes before runtime shutdown disposes this coordinator.</summary>
    private void OnOwnerClosed(object? sender, EventArgs eventArgs) => Dispose();

    /// <summary>Uses a snapshot of windows because each Close removes its own dictionary entry.</summary>
    private void CloseAll()
    {
        foreach (ColonizationProjectPreviewWindow window in windows.Values.ToArray())
        {
            window.Close();
        }
    }
}
