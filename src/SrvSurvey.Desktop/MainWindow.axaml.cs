using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private CancellationTokenSource? monitorCancellation;

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
        var cancellation = new CancellationTokenSource();
        monitorCancellation = cancellation;
        await viewModel.RefreshAsync();
        if (!cancellation.IsCancellationRequested)
        {
            _ = viewModel.MonitorAsync(cancellationToken: cancellation.Token);
        }
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        monitorCancellation?.Cancel();
        monitorCancellation?.Dispose();
        monitorCancellation = null;
        base.OnClosed(eventArgs);
    }
}
