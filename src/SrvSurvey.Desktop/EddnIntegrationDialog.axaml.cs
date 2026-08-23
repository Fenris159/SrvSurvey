using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class EddnIntegrationDialog : Window
{
    private NetworkPrivacyViewModel? networkPrivacy;

    public EddnIntegrationDialog()
    {
        InitializeComponent();
    }

    public EddnIntegrationDialog(NetworkPrivacyViewModel networkPrivacy) : this()
    {
        this.networkPrivacy = networkPrivacy
            ?? throw new ArgumentNullException(nameof(networkPrivacy));
        EddnUploadEnabledCheckBox.IsChecked = networkPrivacy.EddnUploadEnabled;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close(false);
    }

    private void Save_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (networkPrivacy is null)
        {
            Close(false);
            return;
        }

        var enabled = EddnUploadEnabledCheckBox.IsChecked == true;
        if (networkPrivacy.TrySetEddnUploadEnabled(enabled))
        {
            Close(true);
            return;
        }

        SaveErrorText.Text = networkPrivacy.StatusMessage;
        SaveErrorText.IsVisible = true;
    }
}
