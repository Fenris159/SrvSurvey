using Avalonia.Controls;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class RouteBioOverlayPresentation : UserControl, IOverlayPanelSizeAware
{
    public RouteBioOverlayPresentation()
    {
        InitializeComponent();
        TargetList.CompletionRequested += OnCompletionRequested;
    }

    void IOverlayPanelSizeAware.SetPanelSizeOverrideActive(bool active)
    {
        TargetList.ExpandToAvailableHeight = active;
    }

    private async void OnCompletionRequested(object? sender, RouteBioCompletionRequestedEventArgs eventArgs)
    {
        if (DataContext is not RouteBioOverlayViewModel viewModel)
        {
            return;
        }

        await viewModel.SetCompletedAsync(eventArgs.Target, eventArgs.IsCompleted);
    }
}
