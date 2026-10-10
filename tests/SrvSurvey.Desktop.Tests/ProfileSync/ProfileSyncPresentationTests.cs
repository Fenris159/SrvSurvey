using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ProfileSync;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ProfileSyncPresentationTests
{
    [AvaloniaTheory]
    [InlineData("blue-dark", false)]
    [InlineData("blue-light", false)]
    [InlineData("blue-dark", true)]
    [InlineData("blue-light", true)]
    public async Task BackupControlsFitAndRevealCloudManagementOrLocalRecovery(string palette, bool cloudHistory)
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive { IsConfigured = cloudHistory, IsLinked = cloudHistory };
        if (cloudHistory)
        {
            cloud.Add(root.Store.Capture());
        }
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        if (cloudHistory)
        {
            await vm.RefreshBackupsAsync();
            vm.SelectedBackup = vm.Backups.Single();
        }
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(root.Paths.ConfigDirectory, "preview-theme.json"))
        );
        string previous = theme.Current.Key;
        theme.Select(palette);
        theme.ApplyCurrent();
        var view = new ProfileSyncView { DataContext = vm };
        var card = new Border
        {
            Child = view,
            Padding = new Thickness(22),
            Margin = new Thickness(20),
        };
        card.Classes.Add("card");
        var window = new Window
        {
            Content = new ScrollViewer { Content = card },
            Width = 1000,
            Height = cloudHistory ? 1100 : 850,
        };
        try
        {
            window.Show();
            Button link = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Link Google Drive"));
            Assert.True(link.IsEffectivelyVisible);
            Assert.False(link.IsEffectivelyEnabled);
            Expander local = view.GetVisualDescendants()
                .OfType<Expander>()
                .Single(expander => Equals(expander.Header, "Local backup and restore"));
            local.IsExpanded = !cloudHistory;
            if (cloudHistory)
            {
                view.GetVisualDescendants()
                    .OfType<Expander>()
                    .Single(expander => Equals(expander.Header, "Restore a Google Drive backup"))
                    .IsExpanded = true;
                window.UpdateLayout();
                Button delete = FindButton(view, "Delete selected backup");
                Assert.True(delete.IsEffectivelyEnabled);
                delete.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent));
                Assert.True(vm.IsDeleteConfirmationVisible);
                Assert.False(delete.IsEffectivelyEnabled);
                FindButton(view, "Cancel deletion").RaiseEvent(new(Avalonia.Controls.Button.ClickEvent));
                Assert.False(vm.IsDeleteConfirmationVisible);
                FindButton(view, "Clear all SrvSurvey cloud backups")
                    .RaiseEvent(new(Avalonia.Controls.Button.ClickEvent));
                Assert.True(vm.IsDeleteConfirmationVisible);
            }
            window.UpdateLayout();
            Assert.True(
                FindButton(view, cloudHistory ? "Download selected backup" : "Export backup file").IsEffectivelyVisible
            );
            window.CaptureRenderedFrame()?.Dispose();
            string directory = Path.Combine(Path.GetTempPath(), "srv-profile-sync-preview");
            Directory.CreateDirectory(directory);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            using FileStream output = File.Create(
                Path.Combine(directory, (cloudHistory ? "cloud-" : "") + palette + ".png")
            );
            frame?.Save(output, PngBitmapEncoderOptions.Default);
            if (cloudHistory)
            {
                FindButton(view, "Permanently delete").RaiseEvent(new(Avalonia.Controls.Button.ClickEvent));
                Assert.Single(cloud.Deleted);
                Assert.Empty(vm.Backups);
                Assert.False(vm.IsDeleteConfirmationVisible);
            }
        }
        finally
        {
            window.Close();
            theme.Select(previous);
            theme.ApplyCurrent();
        }
    }

    private static Button FindButton(ProfileSyncView view, string text) =>
        view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text));
}
