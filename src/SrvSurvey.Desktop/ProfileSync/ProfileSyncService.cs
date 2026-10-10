using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;

namespace SrvSurvey.Desktop.ProfileSync;

/// <summary>Coordinates backup, causal merging, staged restores, and machine-local authorization.</summary>
internal sealed class ProfileSyncService : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IGoogleDriveBackupClient drive;
    private readonly Action<string> log;
    private FileStream? liveLease;
    private SyncMergeResult? pendingMerge;
    private ProfileSnapshot? pendingSnapshot;
    private bool disposed;

    /// <summary>Coordinates one machine's profile with the supplied or default Google Drive client.</summary>
    internal ProfileSyncService(
        ProfileSyncStore store,
        IGoogleDriveBackupClient? drive = null,
        Action<string>? log = null
    )
    {
        Store = store;
        this.drive = drive ?? new GoogleDriveBackupClient(store);
        this.log = log ?? (_ => { });
    }

    internal ProfileSyncStore Store { get; }
    internal bool IsConfigured => drive.IsConfigured;
    internal bool IsLinked => drive.IsLinked;
    internal bool RestartRequired => File.Exists(Store.PendingPath);
    internal string Status { get; private set; } =
        "Google Drive is not linked. Local backup and restore are available.";
    internal IReadOnlyList<SyncConflict> Conflicts => pendingMerge?.Conflicts ?? [];
    internal IReadOnlyList<DriveBackup> Backups { get; private set; } = [];
    internal bool HasBackupHistory { get; private set; }
    internal event Action? Changed;

    /// <summary>Applies downloads before workspaces load, or safely defers when another instance is active.</summary>
    internal Task StartupAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                bool another = HasOtherLiveInstance();
                string livePath = Path.Combine(
                    Store.StateDirectory,
                    $"live-{Environment.ProcessId}-{Guid.NewGuid():N}.lock"
                );
                liveLease ??= new FileStream(
                    livePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    1,
                    FileOptions.DeleteOnClose
                );
                if (another)
                {
                    SetStatus(
                        "Cloud restore is deferred while another SrvSurvey instance is open. Close all instances before restarting to apply it."
                    );
                    return;
                }
                Store.RecoverInterruptedRestore();
                bool restored = Store.ApplyPending();
                if (!Store.LoadPreferences().Automatic || !IsLinked)
                {
                    if (restored)
                    {
                        SetStatus("The selected backup was restored before loading your workspaces.");
                    }
                    else if (IsLinked)
                    {
                        SetStatus("Google Drive is linked. Automatic sync is off; use Backup now or Sync now.");
                    }

                    return;
                }
                ProfileSnapshot local = Store.Capture();
                SyncMergeResult merge = await MergeCloudAsync(local, token).ConfigureAwait(false);
                if (merge.Conflicts.Count > 0)
                {
                    Store.SaveBackup(local);
                    Store.SaveBaseline(local);
                    pendingMerge = merge;
                    pendingSnapshot = local;
                    SetStatus(
                        $"{merge.Conflicts.Count} conflicting changes need a choice in Data & migration. Local data is unchanged."
                    );
                    return;
                }
                ProfileSnapshot merged = local with { Portable = merge.Values };
                Store.Apply(merged);
                Store.SaveBaseline(merged);
                SetStatus("Startup sync complete. Portable preferences and workspace data are up to date.");
            },
            cancellationToken
        );

    /// <summary>Connects this machine only; automatic sync is enabled after explicit linking.</summary>
    internal async Task LinkAsync(CancellationToken cancellationToken = default)
    {
        await drive.LinkAsync(cancellationToken).ConfigureAwait(false);
        Store.SavePreferences(Store.LoadPreferences() with { Automatic = true });
        SetStatus("Google Drive linked. Use Sync now to check other machines, or Backup now to save this profile.");
    }

    /// <summary>Disables synchronization locally without deleting cloud backups or other machine links.</summary>
    internal void Disconnect()
    {
        drive.Disconnect();
        Backups = [];
        HasBackupHistory = false;
        Store.SavePreferences(Store.LoadPreferences() with { Automatic = false });
        SetStatus("Google Drive disconnected on this machine. Cloud backups were retained.");
    }

    /// <summary>Imports a publisher Desktop OAuth client JSON without changing user data.</summary>
    internal void ImportClient(byte[] bytes)
    {
        var root = JsonNode.Parse(bytes) as JsonObject;
        if (
            root?["installed"] is not JsonObject installed
            || installed["client_id"]?.GetValue<string>() is not { Length: > 0 } id
            || !id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException("Choose a Google Cloud OAuth credentials file for a Desktop app.");
        }

        if (IsLinked)
        {
            throw new InvalidOperationException("Disconnect Google Drive before replacing its application setup.");
        }

        Store.SavePreferences(
            Store.LoadPreferences() with
            {
                ClientId = id,
                ClientSecret = installed["client_secret"]?.GetValue<string>() ?? "",
            }
        );
        SetStatus("Google Desktop client setup loaded. Link Google Drive to continue.");
    }

    /// <summary>Saves a local recovery copy and uploads it when this machine is linked.</summary>
    internal Task BackupAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                if (RestartRequired)
                {
                    throw new InvalidOperationException("Restart to apply the staged restore before backing up again.");
                }
                ProfileSnapshot snapshot = Store.Capture();
                string file = Store.SaveBackup(snapshot);
                Store.SaveBaseline(snapshot);
                if (IsLinked)
                {
                    await drive.UploadAsync(snapshot, token).ConfigureAwait(false);
                    SetStatus("Backup saved locally and to Google Drive. Ten recent backups are retained per machine.");
                }
                else
                {
                    SetStatus("Local backup saved: " + file);
                }
            },
            cancellationToken
        );

    /// <summary>Uploads local changes, checks every machine's latest backup, and stages remote changes for restart.</summary>
    internal Task SynchronizeAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                if (!IsLinked)
                {
                    throw new InvalidOperationException("Link Google Drive before synchronizing.");
                }

                if (RestartRequired)
                {
                    throw new InvalidOperationException(
                        "Restart to apply the staged restore before synchronizing again."
                    );
                }

                ProfileSnapshot local = Store.Capture();
                Store.SaveBackup(local);
                Store.SaveBaseline(local);
                SyncMergeResult merge = await MergeCloudAsync(local, token).ConfigureAwait(false);
                await drive.UploadAsync(local, token).ConfigureAwait(false);
                pendingMerge = merge;
                pendingSnapshot = local;
                if (merge.Conflicts.Count > 0)
                {
                    SetStatus(
                        $"Local changes are backed up. Choose which version to keep for {merge.Conflicts.Count} conflicting changes."
                    );
                }
                else if (Equivalent(local.Portable, merge.Values))
                {
                    SetStatus("Sync complete. This machine matches the cloud profile.");
                }
                else
                {
                    Store.StageRestore(local with { Portable = merge.Values }, restoreMachine: false);
                    SetStatus("Cloud changes are ready. Restart SrvSurvey to load the synchronized profile.");
                }
            },
            cancellationToken
        );

    /// <summary>Resolves reported conflicts explicitly, preserving causality for every competing machine.</summary>
    internal Task ResolveConflictsAsync(bool useCloud, CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                if (pendingMerge is null || pendingSnapshot is null || pendingMerge.Conflicts.Count == 0)
                {
                    throw new InvalidOperationException("There are no pending sync conflicts.");
                }

                ProfileSnapshot fresh = Store.Capture();
                var values = new Dictionary<string, SyncValue>(pendingMerge.Values, StringComparer.Ordinal);
                foreach ((string key, SyncValue value) in fresh.Portable)
                {
                    if (
                        !pendingSnapshot.Portable.TryGetValue(key, out SyncValue? old)
                        || old.Deleted != value.Deleted
                        || !JsonNode.DeepEquals(old.Value, value.Value)
                    )
                    {
                        values[key] = value;
                    }
                }
                Store.SaveBackup(fresh);
                Store.SaveBaseline(fresh);
                foreach (SyncConflict conflict in pendingMerge.Conflicts)
                {
                    SyncConflict current = conflict with { Local = values[conflict.Key] };
                    values[conflict.Key] = ProfileSyncMerge.Resolve(Store.Device.Id, current, useCloud);
                }
                ProfileSnapshot resolved = fresh with { Portable = values };
                Store.StageRestore(resolved, restoreMachine: false);
                await drive.UploadAsync(resolved, token).ConfigureAwait(false);
                pendingMerge = null;
                SetStatus("Conflict choices are backed up. Restart SrvSurvey to apply the merged profile.");
            },
            cancellationToken
        );

    /// <summary>Refreshes the restore history without changing local files.</summary>
    internal Task RefreshBackupsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Backups = (await drive.ListAsync(token).ConfigureAwait(false))
                    .OrderByDescending(backup => backup.CreatedUtc)
                    .ToArray();
                HasBackupHistory = true;
                SetStatus($"{Backups.Count} Google Drive backups are available to restore.");
            },
            cancellationToken
        );

    /// <summary>Stages a selected cloud snapshot; only the original machine can restore hardware settings.</summary>
    internal Task RestoreAsync(
        DriveBackup backup,
        bool restoreMachine,
        CancellationToken cancellationToken = default
    ) =>
        RunAsync(
            async token =>
            {
                ProfileSnapshot snapshot = await drive.DownloadAsync(backup.Id, token).ConfigureAwait(false);
                Store.SaveBackup(Store.Capture());
                Store.StageExplicitRestore(snapshot, restoreMachine);
                SetStatus("Backup selected. Restart SrvSurvey to restore it; a local rollback copy has been saved.");
            },
            cancellationToken
        );

    /// <summary>Exports the selected cloud file without capturing or restoring local settings.</summary>
    internal async Task<byte[]?> DownloadBackupAsync(DriveBackup backup, CancellationToken cancellationToken = default)
    {
        byte[]? result = null;
        await RunAsync(
                async token =>
                {
                    byte[] bytes = await drive.DownloadBytesAsync(backup.Id, token).ConfigureAwait(false);
                    var snapshot = ProfileSnapshot.Parse(bytes);
                    if (snapshot.DeviceId != backup.DeviceId)
                    {
                        throw new InvalidDataException("Cloud backup identity does not match its metadata.");
                    }
                    ProfileSyncStore.ValidateScope(snapshot);
                    result = bytes;
                    SetStatus("The selected cloud backup was downloaded. Local settings are unchanged.");
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>Deletes only the confirmed history entries, leaving local data and authorization intact.</summary>
    internal async Task DeleteBackupsAsync(
        IReadOnlyList<DriveBackup> backups,
        CancellationToken cancellationToken = default
    )
    {
        DriveBackup[] targets = backups.DistinctBy(backup => backup.Id).ToArray();
        int deleted = 0;
        await RunAsync(
                async token =>
                {
                    foreach (string id in targets.Select(backup => backup.Id))
                    {
                        await drive.DeleteAsync(id, token).ConfigureAwait(false);
                        deleted++;
                        Backups = Backups.Where(item => item.Id != id).ToArray();
                    }
                    SetStatus(
                        $"Deleted {deleted} cloud backups. Local data and Google linking were retained. Automatic backups can create new cloud files."
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        if (deleted < targets.Length)
        {
            SetStatus(
                $"Deleted {deleted} of {targets.Length} cloud backups. Refresh history before retrying. " + Status
            );
        }
    }

    /// <summary>Stages a local export after validation, retaining a recovery backup first.</summary>
    internal void RestoreLocal(byte[] bytes, bool restoreMachine)
    {
        var snapshot = ProfileSnapshot.Parse(bytes);
        ProfileSyncStore.ValidateScope(snapshot);
        Store.SaveBackup(Store.Capture());
        Store.StageExplicitRestore(snapshot, restoreMachine);
        SetStatus("Local backup selected. Restart SrvSurvey to restore it.");
    }

    /// <summary>Exports the same portable and machine-scoped format used by cloud backups.</summary>
    internal byte[] ExportLocal()
    {
        ProfileSnapshot snapshot = Store.Capture();
        Store.SaveBackup(snapshot);
        Store.SaveBaseline(snapshot);
        return snapshot.Serialize();
    }

    /// <summary>Backs up after producers stop; a pending restore must not be overwritten by stale in-memory settings.</summary>
    internal async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!Store.LoadPreferences().Automatic || RestartRequired)
        {
            return;
        }

        await BackupAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Downloads the latest immutable snapshot for each machine and merges independent changes.</summary>
    private async Task<SyncMergeResult> MergeCloudAsync(ProfileSnapshot local, CancellationToken cancellationToken)
    {
        IReadOnlyList<DriveBackup> backups = await drive.ListAsync(cancellationToken).ConfigureAwait(false);
        Backups = backups.OrderByDescending(backup => backup.CreatedUtc).ToArray();
        HasBackupHistory = true;
        var values = new Dictionary<string, SyncValue>(local.Portable, StringComparer.Ordinal);
        var conflicts = new List<SyncConflict>();
        foreach (
            DriveBackup backup in backups
                .GroupBy(backup => backup.DeviceId)
                .Select(group => group.OrderByDescending(backup => backup.CreatedUtc).First())
                .OrderBy(backup => backup.CreatedUtc)
        )
        {
            ProfileSnapshot remote = await drive.DownloadAsync(backup.Id, cancellationToken).ConfigureAwait(false);
            if (remote.DeviceId != backup.DeviceId)
            {
                throw new InvalidDataException("Cloud backup identity does not match its metadata.");
            }

            ProfileSyncStore.ValidateScope(remote);
            SyncMergeResult merged = ProfileSyncMerge.Merge(values, remote.Portable);
            values = merged.Values;
            conflicts.AddRange(merged.Conflicts);
        }
        return new(values, conflicts.Where(conflict => !values[conflict.Key].Includes(conflict.Remote)).ToArray());
    }

    /// <summary>Serializes local operations across instances and bounds background network work.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            string path = Path.Combine(Store.StateDirectory, "operation.lock");
            try
            {
                await using FileStream lease = await AcquireLeaseAsync(path, deadline.Token).ConfigureAwait(false);
                await action(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or InvalidDataException
                            or UnauthorizedAccessException
                            or HttpRequestException
                            or OperationCanceledException
                            or InvalidOperationException
                            or JsonException
                )
            {
                SetStatus("Backup/sync could not complete. Local data was retained. " + exception.Message);
            }
        }
        finally
        {
            gate.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>Acquires a file lease rather than relying on process-local locks for multiple commanders.</summary>
    private static async Task<FileStream> AcquireLeaseAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Detects live profile readers so startup restore cannot race another commander's workspaces.</summary>
    private bool HasOtherLiveInstance()
    {
        foreach (string file in Directory.EnumerateFiles(Store.StateDirectory, "live-*.lock"))
        {
            try
            {
                using var probe = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Compares content without treating counter-only updates as a restart requirement.</summary>
    private static bool Equivalent(Dictionary<string, SyncValue> first, Dictionary<string, SyncValue> second) =>
        first.Count == second.Count
        && first.All(pair =>
            second.TryGetValue(pair.Key, out SyncValue? other)
            && pair.Value.Deleted == other.Deleted
            && JsonNode.DeepEquals(pair.Value.Value, other.Value)
        );

    /// <summary>Publishes a user-facing status and a token-free application log message.</summary>
    private void SetStatus(string status)
    {
        Status = status;
        log("Profile sync: " + status);
        Changed?.Invoke();
    }

    /// <summary>Releases this live-session marker and cloud client.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        liveLease?.Dispose();
        drive.Dispose();
        gate.Dispose();
    }
}
