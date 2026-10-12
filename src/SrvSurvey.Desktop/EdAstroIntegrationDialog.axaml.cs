using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

/// <summary>Reviews EDAstro disclosure and saves opt-in consent after an explicit Save.</summary>
public sealed partial class EdAstroIntegrationDialog : Window
{
    private NetworkPrivacyViewModel? networkPrivacy;

    /// <summary>Creates the disclosure window without changing consent.</summary>
    public EdAstroIntegrationDialog()
    {
        InitializeComponent();
    }

    /// <summary>Initializes the checkbox from the persisted sharing preference.</summary>
    public EdAstroIntegrationDialog(NetworkPrivacyViewModel networkPrivacy)
        : this()
    {
        this.networkPrivacy = networkPrivacy ?? throw new ArgumentNullException(nameof(networkPrivacy));
        EdAstroUploadEnabledCheckBox.IsChecked = networkPrivacy.EdAstroUploadEnabled;
    }

    /// <summary>Closes the disclosure window without saving the edited consent checkbox.</summary>
    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close(false);
    }

    /// <summary>Saves explicit consent and keeps the window open with an error if the transition fails.</summary>
    private void Save_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (networkPrivacy is null)
        {
            Close(false);
            return;
        }

        bool enabled = EdAstroUploadEnabledCheckBox.IsChecked == true;
        if (networkPrivacy.TrySetEdAstroUploadEnabled(enabled))
        {
            Close(true);
            return;
        }

        SaveErrorText.Text = networkPrivacy.StatusMessage;
        SaveErrorText.IsVisible = true;
    }
}
