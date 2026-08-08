using Avalonia.Controls;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class RouteBioOverlayPresentation : UserControl
{
    public RouteBioOverlayPresentation()
    {
        InitializeComponent();
        TargetList.CompletionRequested += OnCompletionRequested;
    }

    private async void OnCompletionRequested(
        object? sender,
        RouteBioCompletionRequestedEventArgs eventArgs)
    {
        if (DataContext is not RouteBioOverlayViewModel viewModel)
        {
            return;
        }

        await viewModel.SetCompletedAsync(
            eventArgs.Target,
            eventArgs.IsCompleted);
    }
}
