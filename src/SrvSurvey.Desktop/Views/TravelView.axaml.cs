using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class TravelView : UserControl
{
    public TravelView()
    {
        InitializeComponent();
    }

    private async void PasteTarget_Click(object? sender, RoutedEventArgs eventArgs)
    {
        string? text = null;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                text = await clipboard.TryGetTextAsync();
            }
        }
        catch (Exception)
        {
            // The view model reports the unavailable clipboard as invalid input.
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.GroundTarget.ApplyPastedTextAsync(text);
        }
    }
}
