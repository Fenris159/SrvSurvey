using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private CancellationTokenSource? monitorCancellation;
    private Task? monitorTask;

    public MainWindow()
        : this(new MainWindowViewModel(configuredJournalDirectory: null))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InputContext = new ApplicationInputContext();
        InitializeComponent();
        DataContext = viewModel;
        Opened += OnOpened;
        viewModel.ProfileImportPreparing += StopMonitorForProfileImportAsync;
        Activated += (_, _) => InputContext.SetActive(true);
        Deactivated += (_, _) => InputContext.SetActive(false);
        AddHandler(
            GotFocusEvent,
            OnElementGotFocus,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public ApplicationInputContext InputContext { get; }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        var cancellation = new CancellationTokenSource();
        monitorCancellation = cancellation;
        await viewModel.RefreshAsync();
        if (!cancellation.IsCancellationRequested)
        {
            monitorTask = viewModel.MonitorAsync(
                cancellationToken: cancellation.Token);
        }
    }

    private async Task StopMonitorForProfileImportAsync()
    {
        var cancellation = monitorCancellation;
        var runningMonitor = monitorTask;
        monitorCancellation = null;
        monitorTask = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (runningMonitor is not null)
        {
            await runningMonitor;
        }

        cancellation.Dispose();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        InputContext.SetActive(false);
        InputContext.SetTextInputActive(false);
        viewModel.ProfileImportPreparing -= StopMonitorForProfileImportAsync;
        monitorCancellation?.Cancel();
        monitorCancellation?.Dispose();
        monitorCancellation = null;
        monitorTask = null;
        base.OnClosed(eventArgs);
    }

    private void OnElementGotFocus(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        InputContext.SetTextInputActive(eventArgs.Source is TextBox);
    }
}
