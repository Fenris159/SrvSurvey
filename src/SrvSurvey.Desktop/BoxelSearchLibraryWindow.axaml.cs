using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            connectedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void DisconnectViewModel()
    {
        if (connectedViewModel is not null)
        {
            connectedViewModel.SearchOpened -= OnSearchOpened;
            connectedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            connectedViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(BoxelSearchLibraryViewModel.IsDialogVisible)
            || sender is not BoxelSearchLibraryViewModel viewModel
            || !viewModel.IsDialogVisible)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.IsRenameVisible)
            {
                RenameTextBox.Focus();
                RenameTextBox.SelectAll();
            }
            else if (viewModel.IsNotesVisible)
            {
                NotesTextBox.Focus();
            }
        });
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
