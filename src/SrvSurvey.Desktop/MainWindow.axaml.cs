using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly JournalMonitorSession monitorSession = new();
    private Task? closePreparationTask;
    private bool closeReady;
    private TrayIcon? trayIcon;

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
        trayIcon = CreateTrayIcon();
    }

    public ApplicationInputContext InputContext { get; }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        _ = monitorSession.Start(
            RunMonitorAsync,
            exception => Program.ApplicationLog?.Append(
                "Journal monitor stopped unexpectedly: " + exception));
    }

    private async Task RunMonitorAsync(CancellationToken cancellationToken)
    {
        _ = viewModel.ReleaseUpdates.CheckAsync();
        _ = viewModel.ReferenceDataUpdates.RefreshAsync();
        await viewModel.RefreshAsync();
        if (!cancellationToken.IsCancellationRequested)
        {
            _ = viewModel.DesktopBehavior.RequestStartupFocus();
            await viewModel.MonitorAsync(cancellationToken: cancellationToken);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty
            && WindowState == WindowState.Minimized)
        {
            _ = viewModel.DesktopBehavior.RequestMinimizeFocus();
            if (viewModel.DesktopBehavior.MinimizeToTray
                && trayIcon is not null)
            {
                ShowInTaskbar = false;
                Hide();
            }
        }
    }

    private async Task StopMonitorForProfileImportAsync()
    {
        await monitorSession.StopAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs eventArgs)
    {
        if (!closeReady)
        {
            eventArgs.Cancel = true;
            base.OnClosing(eventArgs);
            closePreparationTask ??= PrepareToCloseAsync();
            return;
        }

        base.OnClosing(eventArgs);
    }

    private async Task PrepareToCloseAsync()
    {
        try
        {
            await monitorSession.StopAsync();
        }
        catch (Exception exception)
        {
            Program.ApplicationLog?.Append(
                "Journal monitor shutdown failed: " + exception);
        }
        finally
        {
            closeReady = true;
            Dispatcher.UIThread.Post(Close);
        }
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        InputContext.SetActive(false);
        InputContext.SetTextInputActive(false);
        viewModel.ProfileImportPreparing -= StopMonitorForProfileImportAsync;
        trayIcon?.Dispose();
        trayIcon = null;
        base.OnClosed(eventArgs);
    }

    private TrayIcon? CreateTrayIcon()
    {
        if (Application.Current is not { } application)
        {
            return null;
        }

        try
        {
            var menu = new NativeMenu();
            var showItem = new NativeMenuItem("Show SrvSurvey");
            showItem.Click += (_, _) => RestoreFromTray();
            menu.Items.Add(showItem);

            var settingsItem = new NativeMenuItem("Settings");
            settingsItem.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    viewModel.ShowSettings();
                    RestoreFromTrayCore();
                });
            };
            menu.Items.Add(settingsItem);
            menu.Items.Add(new NativeMenuItemSeparator());

            var closeItem = new NativeMenuItem("Close");
            closeItem.Click += (_, _) => Dispatcher.UIThread.Post(Close);
            menu.Items.Add(closeItem);

            var icon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(
                    new Uri("avares://SrvSurvey.Desktop/Assets/logo.ico"))),
                ToolTipText = "SrvSurvey - click to show",
                Menu = menu,
                IsVisible = true,
            };
            icon.Clicked += (_, _) => RestoreFromTray();
            TrayIcon.SetIcons(application, new TrayIcons { icon });
            return icon;
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or NotSupportedException)
        {
            viewModel.DesktopBehavior.ReportTrayUnavailable(exception.Message);
            return null;
        }
    }

    private void RestoreFromTray()
    {
        Dispatcher.UIThread.Post(RestoreFromTrayCore);
    }

    private void RestoreFromTrayCore()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
    }

    public void RestoreAndActivate()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RestoreFromTrayCore();
            return;
        }

        Dispatcher.UIThread.Post(RestoreFromTrayCore);
    }

    private void OnElementGotFocus(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        InputContext.SetTextInputActive(eventArgs.Source is TextBox);
    }
}
