using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly Dictionary<OverlaySettingsCategory, OverlayCategorySettingsWindow>
        overlaySettingsWindows = [];
    private readonly JournalMonitorSession monitorSession = new();
    private IReadOnlyList<MainWindowMonitor> applicationMonitors = [];
    private PixelPoint? lastNormalPosition;
    private Task? closePreparationTask;
    private bool closeReady;
    private bool applicationWindowPositionSaved;
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
        viewModel.DesktopBehavior.ApplicationWindowPreferencesChanged +=
            OnApplicationWindowPreferencesChanged;
        Screens.Changed += OnScreensChanged;
        PositionChanged += OnPositionChanged;
        RefreshApplicationMonitors();
        ApplyApplicationWindowPreferences(
            viewModel.DesktopBehavior.LastApplicationWindowPosition);
        viewModel.ReleaseUpdates.SetDiagnosticsNavigator(
            NavigateToReleaseUpdates);
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

    private void OpenCategoryOverlaySettings_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (sender is not Button { CommandParameter: string navigationKey }
            || !OverlaySettingsCategoryCatalog.TryGet(
                navigationKey,
                out var definition))
        {
            return;
        }

        if (overlaySettingsWindows.TryGetValue(
                definition.Category,
                out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new OverlayCategorySettingsWindow(definition, viewModel);
        overlaySettingsWindows.Add(definition.Category, window);
        window.Closed += (_, _) =>
            overlaySettingsWindows.Remove(definition.Category);
        window.Show(this);
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        if (WindowState == WindowState.Normal)
        {
            lastNormalPosition = Position;
        }

        _ = monitorSession.Start(
            RunMonitorAsync,
            exception => Program.ApplicationLog?.Append(
                "Journal monitor stopped unexpectedly: " + exception));
    }

    private void NavigateToReleaseUpdates()
    {
        viewModel.ShowDiagnostics();
        DiagnosticsPage.ScrollToApplicationUpdates();
    }

    private void OnScreensChanged(object? sender, EventArgs eventArgs)
    {
        var currentPosition = GetCurrentApplicationWindowPosition();
        RefreshApplicationMonitors();
        ApplyApplicationWindowPreferences(currentPosition);
    }

    private void OnApplicationWindowPreferencesChanged(
        object? sender,
        EventArgs eventArgs)
    {
        ApplyApplicationWindowPreferences(lastPosition: null);
    }

    private void OnPositionChanged(
        object? sender,
        PixelPointEventArgs eventArgs)
    {
        if (WindowState == WindowState.Normal)
        {
            lastNormalPosition = eventArgs.Point;
        }
    }

    private void RefreshApplicationMonitors()
    {
        applicationMonitors = MainWindowPlacement.DescribeScreens(Screens.All);
        viewModel.DesktopBehavior.SetAvailableMonitors(
            applicationMonitors.Select(monitor =>
                new ApplicationMonitorOption(
                    monitor.Id,
                    monitor.DisplayName)));
    }

    private void ApplyApplicationWindowPreferences(
        ApplicationWindowPosition? lastPosition)
    {
        var automaticMonitorId = IsVisible
            ? Screens.ScreenFromWindow(this)?.DisplayName
            : null;
        var placement = MainWindowPlacement.Resolve(
            applicationMonitors,
            viewModel.DesktopBehavior.PreferredMonitorId,
            viewModel.DesktopBehavior.ApplicationWindowScalePercent,
            automaticMonitorId,
            lastPosition);
        Width = placement.Width;
        Height = placement.Height;
        MinWidth = placement.MinimumWidth;
        MinHeight = placement.MinimumHeight;
        ApplicationScaleContainer.LayoutTransform = new ScaleTransform(
            placement.ApplicationScale,
            placement.ApplicationScale);
        if (placement.Position is { } position)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
            lastNormalPosition = position;
        }
    }

    private ApplicationWindowPosition? GetCurrentApplicationWindowPosition()
    {
        var position = WindowState == WindowState.Normal
            ? Position
            : lastNormalPosition;
        if (position is not { } point)
        {
            return null;
        }

        var monitor = applicationMonitors.FirstOrDefault(candidate =>
            point.X >= candidate.Bounds.X
            && point.X < candidate.Bounds.X + candidate.Bounds.Width
            && point.Y >= candidate.Bounds.Y
            && point.Y < candidate.Bounds.Y + candidate.Bounds.Height);
        return monitor is null
            ? null
            : new ApplicationWindowPosition(point.X, point.Y, monitor.Id);
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!applicationWindowPositionSaved
            && GetCurrentApplicationWindowPosition() is { } position)
        {
            applicationWindowPositionSaved = true;
            viewModel.DesktopBehavior.RememberApplicationWindowPosition(position);
        }

        if (!closeReady)
        {
            e.Cancel = true;
            base.OnClosing(e);
            closePreparationTask ??= PrepareToCloseAsync();
            return;
        }

        base.OnClosing(e);
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

        try
        {
            await viewModel.DisposeAsync();
        }
        catch (Exception exception)
        {
            Program.ApplicationLog?.Append(
                "Application service shutdown failed: " + exception);
        }
        finally
        {
            closeReady = true;
            Dispatcher.UIThread.Post(Close);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var window in overlaySettingsWindows.Values.ToArray())
        {
            window.Close();
        }

        overlaySettingsWindows.Clear();
        InputContext.SetActive(false);
        InputContext.SetTextInputActive(false);
        viewModel.ProfileImportPreparing -= StopMonitorForProfileImportAsync;
        viewModel.DesktopBehavior.ApplicationWindowPreferencesChanged -=
            OnApplicationWindowPreferencesChanged;
        Screens.Changed -= OnScreensChanged;
        PositionChanged -= OnPositionChanged;
        viewModel.ReleaseUpdates.SetDiagnosticsNavigator(null);
        trayIcon?.Dispose();
        trayIcon = null;
        base.OnClosed(e);
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
                    WellKnownUris.DesktopLogoAsset)),
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
