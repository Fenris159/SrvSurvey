using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow()
        : this(new MainWindowViewModel(configuredJournalDirectory: null))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        await viewModel.RefreshAsync();
    }
}
