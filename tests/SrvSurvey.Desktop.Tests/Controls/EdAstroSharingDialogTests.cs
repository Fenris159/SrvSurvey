using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class EdAstroSharingDialogTests
{
    /// <summary>Verifies consent changes only when disclosure is saved and cancel preserves it.</summary>
    [AvaloniaFact]
    public void ConsentChangesOnlyWhenDisclosureIsSavedAndCancelPreservesIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-edastro-dialog-" + Guid.NewGuid().ToString("N"));
        var store = new NetworkPrivacySettingsStore(Path.Combine(directory, "ui.json"));
        var vm = new NetworkPrivacyViewModel(store);
        var dialog = new EdAstroIntegrationDialog(vm);
        try
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            CheckBox toggle = dialog.FindControl<CheckBox>("EdAstroUploadEnabledCheckBox")!;
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true;
            Assert.False(vm.EdAstroUploadEnabled);
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_EDASTRO_SHARING_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                using WriteableBitmap? frame = dialog.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }
            Click(dialog, "Save");
            Assert.True(store.Load().EdAstroUploadEnabled);
            dialog = new EdAstroIntegrationDialog(vm);
            dialog.Show();
            dialog.FindControl<CheckBox>("EdAstroUploadEnabledCheckBox")!.IsChecked = false;
            Click(dialog, "Cancel");
            Assert.True(vm.EdAstroUploadEnabled);
        }
        finally
        {
            dialog.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>Verifies failed save shows error without closing or enabling sharing.</summary>
    [AvaloniaFact]
    public void FailedSaveShowsErrorWithoutClosingOrEnablingSharing()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-edastro-dialog-blocked-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        string blocked = Path.Combine(directory, "blocked");
        File.WriteAllText(blocked, "occupied");
        var vm = new NetworkPrivacyViewModel(new NetworkPrivacySettingsStore(Path.Combine(blocked, "ui.json")));
        var dialog = new EdAstroIntegrationDialog(vm);
        try
        {
            dialog.Show();
            dialog.FindControl<CheckBox>("EdAstroUploadEnabledCheckBox")!.IsChecked = true;
            Click(dialog, "Save");
            Assert.False(vm.EdAstroUploadEnabled);
            Assert.True(dialog.IsVisible);
            Assert.True(dialog.FindControl<TextBlock>("SaveErrorText")!.IsVisible);
        }
        finally
        {
            dialog.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Invokes the named disclosure action through the rendered button.</summary>
    private static void Click(Window dialog, string label) =>
        Assert
            .Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
