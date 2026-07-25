using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class GuardianView : UserControl
{
    public GuardianView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectClipboard();
        DetachedFromVisualTree += (_, _) => DisconnectClipboard();
        DataContextChanged += (_, _) => ConnectClipboard();
    }

    private void ConnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Guardian.SetClipboardWriter(WriteClipboardAsync);
        }
    }

    private void DisconnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Guardian.SetClipboardWriter(null);
        }
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }

    private async void CopySystem_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopySystemNameAsync();
        }
    }

    private async void CopyAddress_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopySystemAddressAsync();
        }
    }

    private async void CopyPosition_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopyGalacticPositionAsync();
        }
    }

    private async void CopySurface_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopySurfaceLocationAsync();
        }
    }

    private async void OpenRuinsGuide_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenGuideAsync(
            "https://canonn.science/codex/ram-tahs-mission/",
            "mission 1");
    }

    private async void OpenLogsGuide_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenGuideAsync(
            "https://canonn.science/codex/ram-tah-decrypting-the-guardian-logs/",
            "mission 2");
    }

    private async void CopyShareBundle_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopyShareArchivePathAsync();
        }
    }

    private async void OpenShareFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || viewModel.Guardian.ShareArchivePath is not string archivePath)
        {
            return;
        }

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop launcher is not available.");
            var directory = new DirectoryInfo(Path.GetDirectoryName(archivePath)!);
            var launched = await launcher.LaunchDirectoryInfoAsync(directory);
            viewModel.Guardian.ReportShareLaunch(launched
                ? "Opened the Guardian survey bundle folder."
                : "The Guardian survey bundle folder could not be opened.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            viewModel.Guardian.ReportShareLaunch(
                "The Guardian survey bundle folder could not be opened: "
                + exception.Message);
        }
    }

    private async void OpenShareDiscord_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            var launched = await launcher.LaunchUriAsync(
                new Uri("https://discord.gg/9PhBwwDAbV"));
            viewModel.Guardian.ReportShareLaunch(launched
                ? "Opened the Guardian survey Discord invite."
                : "The Guardian survey Discord invite could not be opened.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException)
        {
            viewModel.Guardian.ReportShareLaunch(
                "The Guardian survey Discord invite could not be opened: "
                + exception.Message);
        }
    }

    private async Task OpenGuideAsync(string address, string label)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            await launcher.LaunchUriAsync(new Uri(address));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UriFormatException
                or NotSupportedException)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.RamTah.ReportGuideLaunchFailure(
                    $"The {label} guide could not be opened: {exception.Message}");
            }
        }
    }
}
