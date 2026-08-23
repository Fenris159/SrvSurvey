using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Runtime;

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
        try
        {
            DesktopExternalEffectPolicy.ThrowIfDisabled();
            var launcher = Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            if (!await launcher.LaunchUriAsync(uri))
            {
                throw new InvalidOperationException(
                    "The default browser declined the request.");
            }

            LinkFailureMessage.IsVisible = false;
            LinkFailureMessage.Text = string.Empty;
        }
        catch (Exception exception)
        {
            LinkFailureMessage.Text = "The link could not be opened: "
                + exception.Message;
            LinkFailureMessage.IsVisible = true;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
