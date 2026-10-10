using Avalonia.Threading;
using SrvSurvey.Desktop.ProfileSync;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Presents Google Drive linking, backup history, conflict resolution, and local import/export.</summary>
public sealed class ProfileSyncViewModel : WorkspaceObservable, IDisposable
{
    private readonly ProfileSyncService service;
    private bool isBusy;
    private bool disposed;
    private CancellationTokenSource? authorization;
    private bool automatic;
    private bool restoreMachineSettings;
    private ProfileBackupOption? selectedBackup;
    private string? error;
    private DriveBackup[]? pendingDeletion;
    private bool clearingAll;
    private static readonly string[] RefreshProperties =
    [
        nameof(IsLinked),
        nameof(IsConfigured),
        nameof(NeedsClientSetup),
        nameof(CanLink),
        nameof(CanUseCloud),
        nameof(CanRestore),
        nameof(CanClearBackups),
        nameof(IsDeleteConfirmationVisible),
        nameof(DeleteConfirmationText),
        nameof(CloudStorageText),
        nameof(RestartRequired),
        nameof(HasConflicts),
        nameof(StatusText),
        nameof(ConflictDescription),
        nameof(Backups),
        nameof(SelectedBackup),
        nameof(Automatic),
    ];

    /// <summary>Subscribes to sync status without starting authorization or changing user data.</summary>
    internal ProfileSyncViewModel(ProfileSyncService service)
    {
        this.service = service;
        automatic = service.Store.LoadPreferences().Automatic;
        service.Changed += OnServiceChanged;
        Refresh();
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                Refresh();
            }
        }
    }
    public bool IsLinking => authorization is not null;
    public bool IsLinked => service.IsLinked;
    public bool IsConfigured => service.IsConfigured;
    public bool NeedsClientSetup => !IsConfigured;
    public bool CanLink => IsConfigured && !IsLinked && !IsBusy;
    public bool CanUseCloud => IsLinked && !IsBusy && !IsDeleteConfirmationVisible;
    public bool CanRestore => CanUseCloud && SelectedBackup is not null;
    public bool CanClearBackups => CanUseCloud && Backups.Count > 0;
    public bool IsDeleteConfirmationVisible => pendingDeletion is not null;
    public string DeleteConfirmationText
    {
        get
        {
            if (pendingDeletion is null)
            {
                return "";
            }
            return clearingAll
                ? $"Permanently delete these {pendingDeletion.Length} SrvSurvey cloud backups from all machines? This cannot be undone. Local data and Google linking stay intact. Automatic backups on any linked machine can create new cloud files on shutdown."
                : $"Permanently delete the backup from {pendingDeletion[0].CreatedUtc.ToLocalTime():g}? This cannot be undone. Local data and Google linking stay intact.";
        }
    }
    public string CloudStorageText
    {
        get
        {
            if (!service.HasBackupHistory)
            {
                return "Refresh backup history to see SrvSurvey's cloud storage use.";
            }
            decimal bytes = Backups.Sum(option => (decimal)(option.Backup.SizeBytes ?? 0));
            int unknown = Backups.Count(option => option.Backup.SizeBytes is null);
            return unknown == 0
                ? $"{Backups.Count} cloud backups · {bytes / 1048576:N2} MiB used by SrvSurvey"
                : $"{Backups.Count} cloud backups · at least {bytes / 1048576:N2} MiB used by SrvSurvey; size unavailable for {unknown} backups";
        }
    }
    public bool RestartRequired => service.RestartRequired;
    public bool HasConflicts => service.Conflicts.Count > 0;
    public string DeviceDescription => $"{service.Store.Device.Name} ({service.Store.Device.Platform})";
    public string StatusText => error ?? service.Status;
    public string ConflictDescription =>
        string.Join("\n", service.Conflicts.Select(conflict => Uri.UnescapeDataString(conflict.Key)).Distinct());
    public IReadOnlyList<ProfileBackupOption> Backups { get; private set; } = [];
    public ProfileBackupOption? SelectedBackup
    {
        get => selectedBackup;
        set
        {
            if (Set(ref selectedBackup, value))
            {
                Changed(nameof(CanRestore));
            }
        }
    }
    public bool RestoreMachineSettings
    {
        get => restoreMachineSettings;
        set => Set(ref restoreMachineSettings, value);
    }
    public bool Automatic
    {
        get => automatic;
        set
        {
            try
            {
                service.Store.SavePreferences(service.Store.LoadPreferences() with { Automatic = value });
                Set(ref automatic, value);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = "Could not save the sync setting. " + exception.Message;
                Changed(nameof(StatusText));
            }
        }
    }

    /// <summary>Links Google Drive through an explicit browser action.</summary>
    public Task LinkAsync() =>
        RunAsync(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            authorization = cancellation;
            Changed(nameof(IsLinking));
            try
            {
                await service.LinkAsync(cancellation.Token);
                automatic = service.Store.LoadPreferences().Automatic;
            }
            finally
            {
                authorization = null;
                Changed(nameof(IsLinking));
            }
        });

    /// <summary>Stops waiting for browser authorization without changing an existing linked account.</summary>
    public void CancelLink() => authorization?.Cancel();

    /// <summary>Disconnects this machine without deleting backup history.</summary>
    public Task DisconnectAsync() =>
        RunAsync(() =>
        {
            CancelDeleteBackups();
            service.Disconnect();
            automatic = false;
            return Task.CompletedTask;
        });

    /// <summary>Creates a local backup and uploads it when linked.</summary>
    public Task BackupAsync() =>
        RunAsync(() =>
        {
            BeforeCapture?.Invoke();
            return service.BackupAsync(CancellationToken.None);
        });

    /// <summary>Checks cloud changes and stages a merged profile for restart.</summary>
    public Task SynchronizeAsync() =>
        RunAsync(() =>
        {
            BeforeCapture?.Invoke();
            return service.SynchronizeAsync(CancellationToken.None);
        });

    /// <summary>Lists retained cloud snapshots for an explicit restore.</summary>
    public Task RefreshBackupsAsync() => RunAsync(() => service.RefreshBackupsAsync(CancellationToken.None));

    /// <summary>Retrieves the cloud choice captured before opening a save picker.</summary>
    internal Task<byte[]?> DownloadBackupAsync(ProfileBackupOption backup) =>
        service.DownloadBackupAsync(backup.Backup, CancellationToken.None);

    /// <summary>Requests explicit confirmation for the current cloud choice without deleting it.</summary>
    public void BeginDeleteSelectedBackup()
    {
        if (CanRestore && SelectedBackup is { } backup)
        {
            pendingDeletion = [backup.Backup];
            clearingAll = false;
            Refresh();
        }
    }

    /// <summary>Captures the listed backups across all machines for a bounded clear-all confirmation.</summary>
    public void BeginClearBackups()
    {
        if (CanClearBackups)
        {
            pendingDeletion = Backups.Select(option => option.Backup).ToArray();
            clearingAll = true;
            Refresh();
        }
    }

    /// <summary>Dismisses deletion without changing cloud files.</summary>
    public void CancelDeleteBackups()
    {
        pendingDeletion = null;
        Refresh();
    }

    /// <summary>Deletes exactly the confirmed choices and updates the visible history.</summary>
    public Task ConfirmDeleteBackupsAsync()
    {
        if (IsBusy || !IsLinked || pendingDeletion is not { } targets)
        {
            return Task.CompletedTask;
        }
        CancelDeleteBackups();
        return RunAsync(() => service.DeleteBackupsAsync(targets, CancellationToken.None));
    }

    /// <summary>Stages the selected cloud backup and requested machine scope.</summary>
    public Task RestoreAsync() =>
        RunAsync(() =>
            SelectedBackup is { } backup
                ? service.RestoreAsync(backup.Backup, RestoreMachineSettings, CancellationToken.None)
                : throw new InvalidOperationException("Select a backup to restore.")
        );

    /// <summary>Uses cloud values for conflicts while preserving independent local changes.</summary>
    public Task UseCloudAsync() =>
        RunAsync(() => service.ResolveConflictsAsync(useCloud: true, CancellationToken.None));

    /// <summary>Keeps local values for conflicts while preserving independent cloud changes.</summary>
    public Task KeepLocalAsync() =>
        RunAsync(() => service.ResolveConflictsAsync(useCloud: false, CancellationToken.None));

    /// <summary>Imports Desktop OAuth client configuration for development or custom distributions.</summary>
    public Task ImportClientAsync(byte[] bytes) =>
        RunAsync(() =>
        {
            service.ImportClient(bytes);
            return Task.CompletedTask;
        });

    /// <summary>Stages a local backup for restoration at the next application startup.</summary>
    public Task RestoreLocalAsync(byte[] bytes) =>
        RunAsync(() =>
        {
            service.RestoreLocal(bytes, RestoreMachineSettings);
            return Task.CompletedTask;
        });

    /// <summary>Exports a portable backup with a separate machine-specific section.</summary>
    public byte[] ExportLocal()
    {
        BeforeCapture?.Invoke();
        return service.ExportLocal();
    }

    /// <summary>Flushes visible workspace selections before snapshot capture.</summary>
    internal Action? BeforeCapture { get; set; }

    /// <summary>Notifies the runtime that a staged profile should be loaded.</summary>
    public event Func<Task>? RestartRequested;

    /// <summary>Requests the existing coordinated application restart.</summary>
    public Task RequestRestartAsync() =>
        RunAsync(async () =>
        {
            if (RestartRequested is not null)
            {
                await RestartRequested.Invoke();
            }
        });

    /// <summary>Contains file picker and storage errors in the normal status area.</summary>
    public Task FileOperationAsync(Func<Task> action) => RunAsync(action);

    /// <summary>Accepts a bounded file selected by the user.</summary>
    public void AcceptFile(byte[] bytes, bool clientSetup)
    {
        if (clientSetup)
        {
            service.ImportClient(bytes);
        }
        else
        {
            service.RestoreLocal(bytes, RestoreMachineSettings);
        }
    }

    /// <summary>Allows the runtime to back up after all workspace producers have stopped.</summary>
    internal Task ShutdownAsync() => service.ShutdownAsync(CancellationToken.None);

    /// <summary>Prevents overlapping UI operations and contains actionable failures.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        error = null;
        try
        {
            await action();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or HttpRequestException
                        or OperationCanceledException
                        or System.Text.Json.JsonException
                        or System.ComponentModel.Win32Exception
            )
        {
            error = "The operation could not complete. " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    /// <summary>Marshals cloud status updates back to Avalonia's UI thread.</summary>
    private void OnServiceChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    /// <summary>Refreshes operation state and backup choices after an action or cloud update.</summary>
    private void Refresh()
    {
        if (disposed)
        {
            return;
        }
        if (!Backups.Select(option => option.Backup).SequenceEqual(service.Backups))
        {
            Backups = service.Backups.Select(backup => new ProfileBackupOption(backup)).ToArray();
            selectedBackup = Backups.FirstOrDefault(option => option.Backup.Id == selectedBackup?.Backup.Id);
        }
        foreach (string name in RefreshProperties)
        {
            Changed(name);
        }
    }

    /// <summary>Removes notifications and releases this machine's sync resources.</summary>
    public void Dispose()
    {
        disposed = true;
        authorization?.Cancel();
        service.Changed -= OnServiceChanged;
        service.Dispose();
    }
}

/// <summary>A display label for a Google Drive backup; the underlying ID is kept out of UI bindings.</summary>
public sealed class ProfileBackupOption
{
    /// <summary>Formats one backup with its creation time and originating computer.</summary>
    internal ProfileBackupOption(DriveBackup backup)
    {
        Backup = backup;
    }

    internal DriveBackup Backup { get; }
    public string Label =>
        $"{Backup.CreatedUtc.ToLocalTime():g} · {(string.IsNullOrWhiteSpace(Backup.DeviceName) ? Backup.DeviceId[..Math.Min(12, Backup.DeviceId.Length)] : Backup.DeviceName)}";
}
