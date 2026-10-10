using System.Text;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.ProfileSync;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CloudBackupManagementTests
{
    [AvaloniaFact]
    public async Task SelectedDeletionRequiresConfirmationAndKeepsTheConfirmedChoice()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        cloud.Add(root.Store.Capture());
        cloud.Add(root.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        await vm.RefreshBackupsAsync();
        Assert.False(vm.CanRestore);
        vm.SelectedBackup = vm.Backups[0];
        string confirmedId = vm.SelectedBackup.Backup.Id;
        vm.BeginDeleteSelectedBackup();
        Assert.True(vm.IsDeleteConfirmationVisible);
        Assert.Contains("Permanently delete the backup", vm.DeleteConfirmationText);
        Assert.False(vm.CanUseCloud);
        Assert.Empty(cloud.Deleted);
        vm.SelectedBackup = vm.Backups[1];
        await vm.ConfirmDeleteBackupsAsync();
        Assert.Equal(confirmedId, Assert.Single(cloud.Deleted));
        Assert.Single(vm.Backups);
        Assert.NotNull(vm.SelectedBackup);
        Assert.False(vm.IsDeleteConfirmationVisible);
        Assert.True(vm.IsLinked);
    }

    [AvaloniaFact]
    public async Task CancelOrMissingConfirmationNeverDeletesFiles()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        cloud.Add(root.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        vm.BeginClearBackups();
        vm.BeginDeleteSelectedBackup();
        Assert.False(vm.IsDeleteConfirmationVisible);
        await vm.RefreshBackupsAsync();
        vm.SelectedBackup = vm.Backups.Single();
        vm.BeginClearBackups();
        Assert.Contains("all machines", vm.DeleteConfirmationText);
        Assert.Contains("Automatic backups", vm.DeleteConfirmationText);
        vm.CancelDeleteBackups();
        await vm.ConfirmDeleteBackupsAsync();
        Assert.Equal("", vm.DeleteConfirmationText);
        Assert.Empty(cloud.Deleted);
        Assert.Single(vm.Backups);
    }

    [AvaloniaFact]
    public async Task ClearHistoryLeavesLocalStateAndBackupsCreatedAfterConfirmationIntact()
    {
        using var root = new ProfileSyncTestRoot();
        using var other = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[{\"Name\":\"Sol\"}]");
        root.Store.SavePreferences(new(Automatic: true));
        ProfileSnapshot local = root.Store.Capture();
        string localFile = root.Store.SaveBackup(local);
        root.Store.SaveBaseline(local);
        byte[] baseline = await File.ReadAllBytesAsync(Path.Combine(root.Store.StateDirectory, "baseline.json"));
        var cloud = new FakeProfileDrive();
        cloud.Add(local);
        cloud.Add(other.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        await vm.RefreshBackupsAsync();
        vm.BeginClearBackups();
        cloud.Add(local);
        await vm.ConfirmDeleteBackupsAsync();
        Assert.Equal(2, cloud.Deleted.Count);
        Assert.Empty(vm.Backups);
        Assert.False(vm.CanClearBackups);
        Assert.True(vm.IsLinked);
        Assert.True(vm.Automatic);
        Assert.True(File.Exists(localFile));
        Assert.Equal(baseline, await File.ReadAllBytesAsync(Path.Combine(root.Store.StateDirectory, "baseline.json")));
        Assert.Equal("[{\"Name\":\"Sol\"}]", await File.ReadAllTextAsync(root.Path("data", "bookmarks.json")));
        await vm.RefreshBackupsAsync();
        Assert.Single(vm.Backups);
    }

    [AvaloniaFact]
    public async Task DisconnectDismissesAConfirmationWithoutDeletingHistory()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        cloud.Add(root.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        await vm.RefreshBackupsAsync();
        vm.BeginClearBackups();
        await vm.DisconnectAsync();
        await vm.ConfirmDeleteBackupsAsync();
        Assert.Empty(cloud.Deleted);
        Assert.False(vm.IsDeleteConfirmationVisible);
        Assert.False(vm.CanClearBackups);
    }

    [AvaloniaFact]
    public async Task DownloadUsesCloudBytesAndDoesNotCaptureOrRestoreCurrentSettings()
    {
        using var root = new ProfileSyncTestRoot();
        using var other = new ProfileSyncTestRoot();
        other.Write("data", "bookmarks.json", "[{\"Name\":\"Achenar\"}]");
        byte[] expected = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(other.Store.Capture().Serialize()));
        var cloud = new FakeProfileDrive { DownloadBytes = expected };
        cloud.Add(other.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        await vm.RefreshBackupsAsync();
        byte[]? actual = await vm.DownloadBackupAsync(vm.Backups.Single());
        Assert.Equal(expected, actual);
        Assert.False(File.Exists(root.Path("data", "bookmarks.json")));
        Assert.False(File.Exists(Path.Combine(root.Store.StateDirectory, "baseline.json")));
        Assert.False(vm.RestartRequired);
        Assert.False(Directory.Exists(Path.Combine(root.Store.StateDirectory, "backups")));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrMismatchedCloudDownloadNeverReachesFileExport(bool malformed)
    {
        using var root = new ProfileSyncTestRoot();
        using var other = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive
        {
            DownloadBytes = malformed ? Encoding.UTF8.GetBytes("bad") : other.Store.Capture().Serialize(),
        };
        cloud.Add(root.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.RefreshBackupsAsync();
        Assert.Null(await service.DownloadBackupAsync(service.Backups.Single()));
        Assert.Contains("could not complete", service.Status);
        Assert.False(service.RestartRequired);
    }

    [AvaloniaFact]
    public async Task PartialDeletionReportsRemainingHistoryAndAllowsAnExplicitRetry()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive { FailDeletionAfter = 1 };
        cloud.Add(root.Store.Capture());
        cloud.Add(root.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        await vm.RefreshBackupsAsync();
        vm.BeginClearBackups();
        await vm.ConfirmDeleteBackupsAsync();
        Assert.Contains("Deleted 1 of 2", vm.StatusText);
        Assert.Contains("offline", vm.StatusText);
        Assert.Single(vm.Backups);
        Assert.True(vm.CanClearBackups);
        cloud.FailDeletionAfter = null;
        await vm.RefreshBackupsAsync();
        vm.BeginClearBackups();
        await vm.ConfirmDeleteBackupsAsync();
        Assert.Empty(vm.Backups);
        Assert.Equal(2, cloud.Deleted.Count);
    }

    [AvaloniaFact]
    public async Task StorageSummaryIncludesAllComputersAndIdentifiesUnavailableSizes()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        cloud.Add(root.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        using var vm = new ProfileSyncViewModel(service);
        Assert.Contains("Refresh backup history", vm.CloudStorageText);
        await vm.RefreshBackupsAsync();
        Assert.StartsWith("1 cloud backups", vm.CloudStorageText);
        Assert.DoesNotContain("at least", vm.CloudStorageText);
        cloud.IncludeSizes = false;
        cloud.Add(root.Store.Capture());
        await vm.RefreshBackupsAsync();
        Assert.Contains("at least", vm.CloudStorageText);
        Assert.Contains("size unavailable for 1", vm.CloudStorageText);
    }
}
