using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.ProfileSync;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CloudBackupManagementTests
{
    [AvaloniaFact]
    public async Task CloudDownloadsRemainCancellableBeforeTheFilePickerAndWritePhase()
    {
        using var root = new ProfileSyncTestRoot();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloud = new FakeProfileDrive
        {
            BeforeDownload = token =>
            {
                started.SetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        cloud.Add(root.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        await vm.RefreshBackupsAsync();
        ProfileBackupOption selected = Assert.Single(vm.Backups);
        Task<byte[]?> download = vm.DownloadBackupAsync(selected);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.CanCancelOperation);
        vm.CancelOperation();

        Assert.Null(await download.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(vm.IsBusy);
        Assert.False(vm.RestartRequired);
        Assert.Single(vm.Backups);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShutdownCancelsAndDrainsManualUploadsBeforeItCompletes(bool automatic)
    {
        using var root = new ProfileSyncTestRoot();
        root.Store.SavePreferences(new(Automatic: automatic));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int uploads = 0;
        var cloud = new FakeProfileDrive
        {
            BeforeUpload = token =>
            {
                if (++uploads > 1)
                {
                    return Task.CompletedTask;
                }
                started.SetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        Task manual = vm.BackupAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task shutdown = vm.ShutdownAsync();
        try
        {
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(manual.IsCompleted);
            await manual.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(automatic ? 1 : 0, cloud.Uploaded.Count);
        }
        finally
        {
            vm.CancelOperation();
            await Task.WhenAll(manual, shutdown).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaFact]
    public async Task LocalFileOperationsDoNotAdvertiseCancellationTheyCannotHonor()
    {
        using var root = new ProfileSyncTestRoot();
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, new FakeProfileDrive()));
        var view = new ProfileSyncView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = vm.FileOperationAsync(() => finish.Task);
        try
        {
            Assert.True(vm.IsBusy);
            window.UpdateLayout();
            Button cancel = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Cancel operation"));
            Assert.False(cancel.IsEffectivelyVisible);
        }
        finally
        {
            finish.SetResult();
            await operation;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RelinkingClearsHistoryAndSelectionFromThePreviousAccount()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        cloud.Add(root.Store.Capture());
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));
        await vm.RefreshBackupsAsync();
        vm.SelectedBackup = Assert.Single(vm.Backups);
        cloud.IsLinked = false;

        await vm.LinkAsync();

        Assert.Empty(vm.Backups);
        Assert.Null(vm.SelectedBackup);
        Assert.False(vm.CanClearBackups);
    }

    [AvaloniaFact]
    public async Task CancelingAnUploadRetainsTheLocalRecoverySnapshot()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[]");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloud = new FakeProfileDrive
        {
            BeforeUpload = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        using var vm = new ProfileSyncViewModel(new ProfileSyncService(root.Store, cloud));

        Task backup = vm.BackupAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsBusy);
        vm.CancelOperation();
        await backup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(vm.IsBusy);
        Assert.Empty(cloud.Uploaded);
        Assert.Contains("canceled", vm.StatusText);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root.Store.StateDirectory, "backups"), "*.json"));
    }

    /// <summary>Verifies that selected deletion requires confirmation and keeps the confirmed choice.</summary>
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

    /// <summary>Verifies that cancel or missing confirmation never deletes files.</summary>
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

    /// <summary>Verifies that clear history leaves local state and backups created after confirmation intact.</summary>
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

    /// <summary>Verifies that disconnect dismisses a confirmation without deleting history.</summary>
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

    /// <summary>Verifies that download uses cloud bytes and does not capture or restore current settings.</summary>
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

    /// <summary>Verifies that invalid or mismatched cloud download never reaches file export.</summary>
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

    /// <summary>Verifies that partial deletion reports remaining history and allows an explicit retry.</summary>
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

    /// <summary>Verifies that storage summary includes all computers and identifies unavailable sizes.</summary>
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
