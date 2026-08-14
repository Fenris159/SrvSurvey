using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Desktop;

public sealed partial class VoxStellarInformationWindow : Window
{
    public VoxStellarInformationWindow()
    {
        InitializeComponent();
    }

    private async void PrivacyPolicy_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await LaunchAsync(WellKnownUris.VoxStellarPrivacyPolicy);
    }

    private async void TermsOfService_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await LaunchAsync(WellKnownUris.VoxStellarTermsOfService);
    }

    private async void PluginSource_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await LaunchAsync(WellKnownUris.VoxStellarPluginSource);
    }

    private async Task LaunchAsync(Uri uri)
    {
        var launcher = Launcher
            ?? throw new InvalidOperationException(
                "The desktop link launcher is not available.");
        await launcher.LaunchUriAsync(uri);
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
