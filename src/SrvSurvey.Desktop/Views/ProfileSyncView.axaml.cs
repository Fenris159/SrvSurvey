using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

/// <summary>Hosts explicit cloud actions and platform file pickers for backup import/export.</summary>
public partial class ProfileSyncView : UserControl
{
    /// <summary>Loads the backup and sync controls.</summary>
    public ProfileSyncView() => InitializeComponent();

    /// <summary>Starts browser-based Google authorization only after a button click.</summary>
    private async void Link_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.LinkAsync();
        }
    }

    /// <summary>Cancels a pending browser authorization.</summary>
    private void CancelLink_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            vm.CancelLink();
        }
    }

    /// <summary>Disconnects this installation.</summary>
    private async void Disconnect_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.DisconnectAsync();
        }
    }

    /// <summary>Creates a recovery snapshot.</summary>
    private async void Backup_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.BackupAsync();
        }
    }

    /// <summary>Checks and uploads portable changes.</summary>
    private async void Sync_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.SynchronizeAsync();
        }
    }

    /// <summary>Retrieves cloud restore history.</summary>
    private async void Refresh_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.RefreshBackupsAsync();
        }
    }

    /// <summary>Stages a cloud backup for restart.</summary>
    private async void Restore_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.RestoreAsync();
        }
    }

    /// <summary>Shows a confirmation before deleting the selected cloud backup.</summary>
    private void DeleteBackup_Click(object? sender, RoutedEventArgs args) =>
        (DataContext as ProfileSyncViewModel)?.BeginDeleteSelectedBackup();

    /// <summary>Shows a confirmation for the listed backups across all machines.</summary>
    private void ClearBackups_Click(object? sender, RoutedEventArgs args) =>
        (DataContext as ProfileSyncViewModel)?.BeginClearBackups();

    /// <summary>Dismisses the confirmation without deleting anything.</summary>
    private void CancelDelete_Click(object? sender, RoutedEventArgs args) =>
        (DataContext as ProfileSyncViewModel)?.CancelDeleteBackups();

    /// <summary>Executes only the cloud deletion choices captured by the confirmation.</summary>
    private async void ConfirmDelete_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.ConfirmDeleteBackupsAsync();
        }
    }

    /// <summary>Saves the selected cloud backup's original bytes through the operating system's picker.</summary>
    private async void Download_Click(object? sender, RoutedEventArgs args)
    {
        if (
            DataContext is not ProfileSyncViewModel vm
            || !vm.CanRestore
            || vm.SelectedBackup is not { } backup
            || TopLevel.GetTopLevel(this) is not { } top
        )
        {
            return;
        }
        await vm.FileOperationAsync(async () =>
        {
            byte[]? bytes = await vm.DownloadBackupAsync(backup);
            if (bytes is null)
            {
                return;
            }
            using IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Download SrvSurvey cloud backup",
                    SuggestedFileName = $"SrvSurvey-cloud-backup-{backup.Backup.CreatedUtc:yyyyMMdd-HHmm}.json",
                    DefaultExtension = "json",
                    FileTypeChoices = [new("SrvSurvey backup") { Patterns = ["*.json"] }],
                }
            );
            if (file is not null)
            {
                await using Stream stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
            }
        });
    }

    /// <summary>Resolves conflicts using local values.</summary>
    private async void KeepLocal_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.KeepLocalAsync();
        }
    }

    /// <summary>Resolves conflicts using cloud values.</summary>
    private async void UseCloud_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.UseCloudAsync();
        }
    }

    /// <summary>Requests a coordinated restart after a restore.</summary>
    private async void Restart_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ProfileSyncViewModel vm)
        {
            await vm.RequestRestartAsync();
        }
    }

    /// <summary>Exports a backup through the operating system's save picker.</summary>
    private async void Export_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfileSyncViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        await vm.FileOperationAsync(async () =>
        {
            IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export SrvSurvey backup",
                    SuggestedFileName = $"SrvSurvey-backup-{DateTime.Now:yyyyMMdd-HHmm}.json",
                    DefaultExtension = "json",
                    FileTypeChoices = [new("SrvSurvey backup") { Patterns = ["*.json"] }],
                }
            );
            if (file is null)
            {
                return;
            }

            byte[] bytes = vm.ExportLocal();
            using (file)
            await using (Stream stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
            }
        });
    }

    /// <summary>Reads a user-selected local backup.</summary>
    private async void Import_Click(object? sender, RoutedEventArgs args) => await ChooseJsonAsync(clientSetup: false);

    /// <summary>Reads a Desktop OAuth setup file supplied by the publisher.</summary>
    private async void ClientSetup_Click(object? sender, RoutedEventArgs args) =>
        await ChooseJsonAsync(clientSetup: true);

    /// <summary>Opens one JSON file, bounds its size, and delegates validation to the view model.</summary>
    private async Task ChooseJsonAsync(bool clientSetup)
    {
        if (DataContext is not ProfileSyncViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        await vm.FileOperationAsync(async () =>
        {
            IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = clientSetup ? "Import Google Desktop client setup" : "Restore SrvSurvey backup",
                    AllowMultiple = false,
                    FileTypeFilter = [new("JSON files") { Patterns = ["*.json"] }],
                }
            );
            if (files.Count == 0)
            {
                return;
            }

            using IStorageFile file = files[0];
            await using Stream stream = await file.OpenReadAsync();
            using var content = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer)) > 0)
            {
                if (content.Length + count > SrvSurvey.Core.ProfileSync.ProfileSnapshot.MaximumBytes)
                {
                    throw new InvalidDataException("The selected file exceeds the supported backup size.");
                }

                await content.WriteAsync(buffer.AsMemory(0, count));
            }
            vm.AcceptFile(content.ToArray(), clientSetup);
        });
    }
}
