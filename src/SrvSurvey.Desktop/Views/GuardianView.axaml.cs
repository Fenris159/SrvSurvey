using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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
}
