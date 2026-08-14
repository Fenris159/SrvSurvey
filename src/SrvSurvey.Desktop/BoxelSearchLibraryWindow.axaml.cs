using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class BoxelSearchLibraryWindow : Window
{
    private BoxelSearchLibraryViewModel? connectedViewModel;

    public BoxelSearchLibraryWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ConnectViewModel();
        Opened += async (_, _) =>
        {
            ConnectViewModel();
            if (DataContext is BoxelSearchLibraryViewModel viewModel)
            {
                await viewModel.RefreshAsync();
            }
        };
        Closed += (_, _) => DisconnectViewModel();
    }

    private void ConnectViewModel()
    {
        DisconnectViewModel();
        connectedViewModel = DataContext as BoxelSearchLibraryViewModel;
        if (connectedViewModel is not null)
        {
            connectedViewModel.SearchOpened += OnSearchOpened;
        }
    }

    private void DisconnectViewModel()
    {
        if (connectedViewModel is not null)
        {
            connectedViewModel.SearchOpened -= OnSearchOpened;
            connectedViewModel = null;
        }
    }

    private void OnSearchOpened(object? sender, EventArgs eventArgs)
    {
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
