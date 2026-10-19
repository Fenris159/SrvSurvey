using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ProfileSync;

/// <summary>Machine-local configuration; authorization and cloud state are never portable preferences.</summary>
internal sealed record ProfileSyncPreferences(bool Automatic = false, string ClientId = "", string ClientSecret = "");

/// <summary>A stable local installation identity with a hashed operating-system machine identifier.</summary>
internal sealed record ProfileSyncDevice(string Id, string Name, string Platform, string MachineHash = "");

/// <summary>Captures allowed profile data and restores it transactionally with a local rollback copy.</summary>
internal sealed class ProfileSyncStore
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    private readonly AppDataPaths paths;
    private readonly Action<string> log;

    /// <summary>Opens the machine-local sync directory and verifies its installation identity.</summary>
    internal ProfileSyncStore(AppDataPaths paths, Func<string>? machineIdentity = null, Action<string>? log = null)
    {
        this.paths = paths;
        this.log = log ?? (message => System.Diagnostics.Trace.TraceWarning(message));
        Directory.CreateDirectory(StateDirectory);
        Device = LoadDevice(machineIdentity ?? ReadMachineIdentity);
    }

    internal string StateDirectory => Path.Combine(paths.ConfigDirectory, "profile-sync");
    internal ProfileSyncDevice Device { get; }
    internal string PendingPath => Path.Combine(StateDirectory, "pending-restore.json");

    /// <summary>Reads publisher configuration and this machine's opt-in to automatic synchronization.</summary>
    internal ProfileSyncPreferences LoadPreferences()
    {
        string path = Path.Combine(StateDirectory, "preferences.json");
        if (!File.Exists(path))
        {
            return new();
        }
        try
        {
            return JsonSerializer.Deserialize<ProfileSyncPreferences>(File.ReadAllBytes(path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>Persists sync options separately from settings that are synchronized.</summary>
    internal void SavePreferences(ProfileSyncPreferences preferences) =>
        WriteAtomic(Path.Combine(StateDirectory, "preferences.json"), JsonSerializer.SerializeToUtf8Bytes(preferences));

    /// <summary>Returns the last local change counters, including deletions.</summary>
    internal ProfileSnapshot? LoadBaseline() =>
        File.Exists(Path.Combine(StateDirectory, "baseline.json"))
            ? ProfileSnapshot.Parse(File.ReadAllBytes(Path.Combine(StateDirectory, "baseline.json")))
            : null;

    /// <summary>Records captured counters only after the snapshot is safely stored locally.</summary>
    internal void SaveBaseline(ProfileSnapshot snapshot) =>
        WriteAtomic(Path.Combine(StateDirectory, "baseline.json"), snapshot.Serialize());

    /// <summary>Reads allowed profile data without including credentials, journals, or queued API reports.</summary>
    internal ProfileSnapshot Capture()
    {
        Dictionary<string, byte[]> layouts = CaptureLayoutContents();
        var portable = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var machine = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        Dictionary<string, SyncValue> baseline = LoadBaseline()?.Portable ?? [];
        var skippedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string file, byte[] bytes) in layouts)
        {
            string relative = Path.GetRelativePath(paths.DataDirectory, file).Replace('\\', '/');
            CaptureContent("data", relative, bytes, portable, machine, skippedFiles);
        }
        foreach ((string area, string root) in Roots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in EnumerateSafeFiles(root, area))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!ProfileSyncPolicy.AllowsFile(area, relative))
                {
                    continue;
                }

                CaptureContent(area, relative, File.ReadAllBytes(file), portable, machine, skippedFiles);
            }
        }
        RetainSkippedValues(portable, baseline, skippedFiles);
        Dictionary<string, SyncValue> captured = ProfileSyncMerge.Capture(Device.Id, portable, baseline);
        CheckValueLimit(captured.Count, "portable", "profile files and retained deletion history");
        return new(
            ProfileSnapshot.CurrentSchema,
            Device.Id,
            Device.Name,
            Device.Platform,
            DateTimeOffset.UtcNow,
            captured,
            machine,
            Device.MachineHash
        );
    }

    /// <summary>Copies layout bytes under short-lived leases so large profile reads cannot block overlay saves.</summary>
    private Dictionary<string, byte[]> CaptureLayoutContents()
    {
        string[] markers = LayoutFiles().ToArray();
        using IDisposable leases = OverlayLayoutFileAccess.AcquireForFiles(markers);
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string marker in markers)
        {
            foreach (string name in OverlayLayoutFileAccess.JournalFileNames)
            {
                string file = Path.Combine(Path.GetDirectoryName(marker)!, name);
                string relative = Path.GetRelativePath(paths.DataDirectory, file).Replace('\\', '/');
                if (
                    File.Exists(file)
                    && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0
                    && ProfileSyncPolicy.AllowsFile("data", relative)
                )
                {
                    contents[file] = File.ReadAllBytes(file);
                }
            }
        }
        return contents;
    }

    /// <summary>Prevents rereading layout files after their coherent snapshot, including files created after the leases ended.</summary>
    private static bool IsLayoutProfileFile(string area, string relative)
    {
        if (
            area != "data"
            || !OverlayLayoutFileAccess.JournalFileNames.Contains(Path.GetFileName(relative), StringComparer.Ordinal)
        )
        {
            return false;
        }
        string[] parts = relative.Split('/');
        return parts.Length == 1 || (parts.Length == 4 && parts[0] == "commanders" && parts[2] == "overlays");
    }

    /// <summary>Parses captured bytes and retains malformed-file baseline entries without extending layout leases.</summary>
    private void CaptureContent(
        string area,
        string relative,
        byte[] bytes,
        Dictionary<string, JsonNode?> portable,
        Dictionary<string, JsonNode?> machine,
        HashSet<string> skippedFiles
    )
    {
        if (bytes.Length > ProfileSnapshot.MaximumBytes)
        {
            throw new InvalidDataException("A profile file exceeds the supported backup size.");
        }
        string file = ResolveTarget(area, relative);
        JsonNode? node;
        try
        {
            node = ReadContent(relative, bytes);
        }
        catch (JsonException)
        {
            skippedFiles.Add(ProfileSyncPolicy.Key(area, relative, ""));
            log("Profile sync skipped malformed JSON file: " + file);
            return;
        }
        ProfileSyncPolicy.RemoveLocalReferencesFromArrays(node);
        foreach ((string pointer, JsonNode? value) in ProfileSyncPolicy.Flatten(node))
        {
            bool? local = ProfileSyncPolicy.IsMachineValue(area, relative, pointer);
            if (local is null)
            {
                continue;
            }
            string key = ProfileSyncPolicy.Key(area, relative, pointer);
            Dictionary<string, JsonNode?> target = local.Value ? machine : portable;
            target[key] = value?.DeepClone();
            CheckValueLimit(target.Count, local.Value ? "machine" : "portable", file);
        }
    }

    /// <summary>Finds real layout folders so backup captures cannot mix files from an in-progress save.</summary>
    private IEnumerable<string> LayoutFiles()
    {
        if (Directory.Exists(paths.DataDirectory))
        {
            yield return Path.Combine(paths.DataDirectory, "plotters.json");
        }
        string commanders = Path.Combine(paths.DataDirectory, "commanders");
        if (!Directory.Exists(commanders) || (File.GetAttributes(commanders) & FileAttributes.ReparsePoint) != 0)
        {
            yield break;
        }
        foreach (string commander in Directory.EnumerateDirectories(commanders))
        {
            string overlays = Path.Combine(commander, "overlays");
            if (
                Directory.Exists(overlays)
                && (File.GetAttributes(commander) & FileAttributes.ReparsePoint) == 0
                && (File.GetAttributes(overlays) & FileAttributes.ReparsePoint) == 0
            )
            {
                yield return Path.Combine(overlays, "plotters.json");
            }
        }
    }

    /// <summary>Retains unreadable files' live values so capture also preserves their existing counters and deletions.</summary>
    private static void RetainSkippedValues(
        Dictionary<string, JsonNode?> portable,
        Dictionary<string, SyncValue> baseline,
        HashSet<string> skippedFiles
    )
    {
        foreach ((string key, SyncValue value) in baseline)
        {
            if (!value.Deleted && skippedFiles.Contains(key[..(key.IndexOf('#') + 1)]))
            {
                portable[key] = value.Value?.DeepClone();
            }
        }
    }

    /// <summary>Reports excessive flattened data at capture time rather than as an invalid saved backup.</summary>
    private static void CheckValueLimit(int count, string scope, string cause)
    {
        if (count > ProfileSnapshot.MaximumValues)
        {
            throw new InvalidDataException(
                $"The {scope} profile value limit of {ProfileSnapshot.MaximumValues} was exceeded by {cause}. Large JSON files in systems or mining-search-cache can exceed this limit."
            );
        }
    }

    /// <summary>Keeps ten local recovery snapshots independently of Google Drive availability.</summary>
    internal string SaveBackup(ProfileSnapshot snapshot)
    {
        string directory = Path.Combine(StateDirectory, "backups");
        string file = Path.Combine(
            directory,
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.json"
        );
        byte[] bytes = snapshot.Serialize();
        _ = ProfileSnapshot.Parse(bytes);
        ValidateScope(snapshot);
        WriteAtomic(file, bytes);
        foreach (
            string old in Directory
                .EnumerateFiles(directory, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(10)
        )
        {
            File.Delete(old);
        }

        return file;
    }

    /// <summary>Stages an explicit restore while in-memory workspaces are still running.</summary>
    internal void StageRestore(
        ProfileSnapshot snapshot,
        bool restoreMachine,
        CancellationToken cancellationToken = default
    )
    {
        ValidateScope(snapshot);
        if (restoreMachine && !IsOriginalMachine(snapshot))
        {
            throw new InvalidOperationException(
                "Machine settings can only be restored on the machine that created the backup."
            );
        }

        ProfileSnapshot staged = (
            restoreMachine
                ? snapshot
                : snapshot with
                {
                    Machine = [],
                    DeviceId = Device.Id,
                    Platform = Device.Platform,
                    MachineFingerprint = Device.MachineHash,
                }
        );
        byte[] bytes = staged.Serialize();
        _ = ProfileSnapshot.Parse(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        WriteAtomic(PendingPath, bytes);
    }

    /// <summary>Turns an older backup into new changes, including deletion of portable values absent from it.</summary>
    internal void StageExplicitRestore(
        ProfileSnapshot snapshot,
        bool restoreMachine,
        CancellationToken cancellationToken = default
    )
    {
        ValidateScope(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ProfileSnapshot current = Capture();
        cancellationToken.ThrowIfCancellationRequested();
        var values = new Dictionary<string, SyncValue>(StringComparer.Ordinal);
        foreach (string key in current.Portable.Keys.Union(snapshot.Portable.Keys, StringComparer.Ordinal))
        {
            SyncValue? previous = current.Portable.GetValueOrDefault(key);
            SyncValue? wanted = snapshot.Portable.GetValueOrDefault(key);
            previous ??= new(null, true, []);
            wanted ??= new(null, true, []);
            values[key] = ProfileSyncMerge.Resolve(Device.Id, new(key, previous, wanted), useRemote: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        SaveBaseline(current);
        StageRestore(snapshot with { Portable = values }, restoreMachine, cancellationToken);
    }

    /// <summary>Applies a queued restore before workspaces and journal producers are constructed.</summary>
    internal bool ApplyPending() => ApplyPending(out _);

    /// <summary>Merges a queued restore with subsequent edits, deferring conflicting changes for an explicit choice.</summary>
    internal bool ApplyPending(out SyncMergeResult? conflicts) => ApplyPending(out conflicts, out _);

    /// <summary>Returns the exact capture used for conflicts so callers can retain it under their startup guard.</summary>
    internal bool ApplyPending(out SyncMergeResult? conflicts, out ProfileSnapshot? conflictSnapshot)
    {
        conflicts = null;
        conflictSnapshot = null;
        if (!File.Exists(PendingPath))
        {
            return false;
        }

        var staged = ProfileSnapshot.Parse(File.ReadAllBytes(PendingPath));
        ValidateScope(staged);
        if (!IsOriginalMachine(staged))
        {
            throw new InvalidOperationException(
                "The backup's hardware settings belong to another machine or operating system."
            );
        }
        Dictionary<string, SyncValue> baseline = LoadBaseline()?.Portable ?? [];
        ProfileSnapshot current = Capture();
        SyncMergeResult merge = MergePending(current.Portable, staged.Portable, baseline);
        if (merge.Conflicts.Count > 0)
        {
            SaveBackup(current);
            conflicts = merge;
            conflictSnapshot = current;
            return false;
        }
        ProfileSnapshot snapshot = staged with
        {
            Portable = merge.Values,
            Machine = MergeStagedMachine(current, staged),
        };
        Apply(snapshot, restoreMachine: true);
        SaveBaseline(snapshot);
        File.Delete(PendingPath);
        return true;
    }

    /// <summary>Preserves machine edits made after staging; the later explicit local edit takes precedence.</summary>
    private Dictionary<string, JsonNode?> MergeStagedMachine(ProfileSnapshot current, ProfileSnapshot staged)
    {
        if (staged.Machine.Count == 0)
        {
            return [];
        }
        Dictionary<string, JsonNode?>? baseline = LoadBaseline()?.Machine;
        if (baseline is null)
        {
            return staged.Machine;
        }
        var machine = new Dictionary<string, JsonNode?>(staged.Machine, StringComparer.Ordinal);
        foreach (string key in current.Machine.Keys.Union(baseline.Keys, StringComparer.Ordinal))
        {
            bool hasCurrent = current.Machine.TryGetValue(key, out JsonNode? value);
            bool hadBaseline = baseline.TryGetValue(key, out JsonNode? previous);
            if (hasCurrent == hadBaseline && JsonNode.DeepEquals(value, previous))
            {
                continue;
            }
            if (hasCurrent)
            {
                machine[key] = value?.DeepClone();
            }
            else
            {
                machine.Remove(key);
            }
            log("Profile restore preserved a machine setting edited after staging: " + key);
        }
        return machine;
    }

    /// <summary>Detects competing edits against the staging baseline, including same-device counter collisions.</summary>
    private SyncMergeResult MergePending(
        Dictionary<string, SyncValue> current,
        Dictionary<string, SyncValue> staged,
        Dictionary<string, SyncValue> baseline
    )
    {
        var local = new Dictionary<string, SyncValue>(current, StringComparer.Ordinal);
        var remote = new Dictionary<string, SyncValue>(staged, StringComparer.Ordinal);
        var conflicts = new List<SyncConflict>();
        foreach ((string key, SyncValue value) in current)
        {
            SyncValue? previous = baseline.GetValueOrDefault(key);
            if (
                !SameContent(value, previous)
                && staged.TryGetValue(key, out SyncValue? incoming)
                && !SameContent(value, incoming)
            )
            {
                var conflict = new SyncConflict(key, value, incoming);
                remote.Remove(key);
                if (SameContent(incoming, previous))
                {
                    local[key] = ProfileSyncMerge.Resolve(Device.Id, conflict, useRemote: false);
                }
                else
                {
                    conflicts.Add(conflict);
                }
            }
        }
        SyncMergeResult merged = ProfileSyncMerge.Merge(local, remote);
        return new(merged.Values, conflicts.Concat(merged.Conflicts).ToArray());
    }

    /// <summary>Compares content against the staging baseline, treating an absent entry as a deletion.</summary>
    private static bool SameContent(SyncValue first, SyncValue? second) =>
        first.Deleted == (second?.Deleted ?? true)
        && (first.Deleted || JsonNode.DeepEquals(first.Value, second?.Value));

    /// <summary>Writes validated JSON changes atomically and rolls all changed files back on failure.</summary>
    internal void Apply(ProfileSnapshot snapshot, bool restoreMachine = false)
    {
        ValidateScope(snapshot);
        if (restoreMachine && !IsOriginalMachine(snapshot))
        {
            throw new InvalidOperationException(
                "The backup's hardware settings belong to another machine or operating system."
            );
        }

        using IDisposable layoutLeases = OverlayLayoutFileAccess.AcquireForFiles(
            snapshot
                .Portable.Keys.Concat(restoreMachine ? snapshot.Machine.Keys : [])
                .Select(key =>
                {
                    (string Area, string Relative, string Pointer) parsed = ProfileSyncPolicy.ParseKey(
                        key,
                        machine: snapshot.Machine.ContainsKey(key)
                    );
                    return ResolveTarget(parsed.Area, parsed.Relative);
                })
        );
        (Dictionary<string, JsonNode?> updates, Dictionary<string, byte[]?> originals) = ReadUpdates(
            snapshot,
            restoreMachine
        );
        if (updates.Count == 0)
        {
            return;
        }
        string journal = PrepareRollback(originals);
        try
        {
            WriteUpdates(updates);
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
            try
            {
                RecoverInterruptedRestore();
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    "Restore and rollback could not complete. Recovery will retry at startup; the rollback copy was retained.",
                    new AggregateException(writeError, recoveryError)
                );
            }
            throw new IOException("Restore failed; the previous profile was restored.", writeError);
        }
        File.Delete(journal);
    }

    /// <summary>Allows same-machine recovery after an app reinstall, falling back to the installation ID when no OS hash exists.</summary>
    private bool IsOriginalMachine(ProfileSnapshot snapshot) =>
        snapshot.Platform == Device.Platform
        && (
            string.IsNullOrEmpty(snapshot.MachineFingerprint)
                ? snapshot.DeviceId == Device.Id
                : string.Equals(snapshot.MachineFingerprint, Device.MachineHash, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Builds only changed files while preserving credentials and other excluded local fields.</summary>
    private (Dictionary<string, JsonNode?> Updates, Dictionary<string, byte[]?> Originals) ReadUpdates(
        ProfileSnapshot snapshot,
        bool restoreMachine
    )
    {
        var updates = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var originals = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        IEnumerable<(string Key, JsonNode? Value, bool Deleted, bool Machine)> changes = snapshot.Portable.Select(
            pair => (pair.Key, pair.Value.Value, pair.Value.Deleted, Machine: false)
        );
        if (restoreMachine)
        {
            changes = changes.Concat(
                snapshot.Machine.Select(pair => (pair.Key, pair.Value, Deleted: false, Machine: true))
            );
        }

        foreach ((string Key, JsonNode? Value, bool Deleted, bool Machine) change in changes)
        {
            (string Area, string Relative, string Pointer) key = ProfileSyncPolicy.ParseKey(change.Key, change.Machine);
            string file = ResolveTarget(key.Area, key.Relative);
            if (!updates.TryGetValue(file, out JsonNode? current))
            {
                byte[]? original = File.Exists(file) ? File.ReadAllBytes(file) : null;
                originals[file] = original;
                current = original is null ? null : ReadContent(key.Relative, original);
            }
            updates[file] = SetPointer(current, key.Pointer, change.Value, change.Deleted);
        }
        foreach (string file in updates.Keys.ToArray())
        {
            string relative = Path.GetRelativePath(paths.DataDirectory, file).Replace('\\', '/');
            JsonNode? previous = originals[file] is { } bytes ? ReadContent(relative, bytes) : null;
            ProfileSyncPolicy.PreserveLocalAttachments(previous, updates[file]);
            if (JsonNode.DeepEquals(previous, updates[file]))
            {
                updates.Remove(file);
                originals.Remove(file);
            }
        }
        return (updates, originals);
    }

    /// <summary>Stores every original file before opening a durable restore transaction.</summary>
    private string PrepareRollback(Dictionary<string, byte[]?> originals)
    {
        string rollback = Path.Combine(StateDirectory, "restore-backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollback);
        foreach ((string file, byte[]? original) in originals)
        {
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file)));
            if (original is not null)
            {
                WriteAtomic(Path.Combine(rollback, key + ".json"), original);
            }
        }
        WriteAtomic(
            Path.Combine(rollback, "manifest.json"),
            JsonSerializer.SerializeToUtf8Bytes(
                originals
                    .Keys.Select(file =>
                    {
                        bool config = !file.StartsWith(
                            paths.DataDirectory + Path.DirectorySeparatorChar,
                            StringComparison.Ordinal
                        );
                        return new RestoreFile(
                            config ? "config" : "data",
                            Path.GetRelativePath(config ? paths.ConfigDirectory : paths.DataDirectory, file)
                                .Replace('\\', '/'),
                            originals[file] is not null
                        );
                    })
                    .ToArray()
            )
        );
        string journal = Path.Combine(StateDirectory, "active-restore.json");
        WriteAtomic(journal, JsonSerializer.SerializeToUtf8Bytes(Path.GetFileName(rollback)));
        return journal;
    }

    /// <summary>Commits each prepared JSON or saved search selection through atomic replacement.</summary>
    private void WriteUpdates(Dictionary<string, JsonNode?> updates)
    {
        foreach ((string file, JsonNode? node) in updates)
        {
            if (node is null)
            {
                File.Delete(file);
            }
            else
            {
                string relative = Path.GetRelativePath(paths.DataDirectory, file).Replace('\\', '/');
                string text = ProfileSyncPolicy.IsSearchSelection(relative)
                    ? node.GetValue<string>()
                    : node.ToJsonString(new() { WriteIndented = true });
                WriteAtomic(file, Encoding.UTF8.GetBytes(text));
            }
        }
    }

    /// <summary>Rolls back an interrupted multi-file restore before any workspace reads its files.</summary>
    internal void RecoverInterruptedRestore()
    {
        string journal = Path.Combine(StateDirectory, "active-restore.json");
        if (!File.Exists(journal))
        {
            return;
        }
        string? id = JsonSerializer.Deserialize<string>(File.ReadAllBytes(journal));
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new InvalidDataException("The restore recovery journal is invalid.");
        }
        string rollback = Path.Combine(StateDirectory, "restore-backups", id);
        RestoreFile[] files =
            JsonSerializer.Deserialize<RestoreFile[]>(File.ReadAllBytes(Path.Combine(rollback, "manifest.json")))
            ?? throw new InvalidDataException("The restore recovery manifest is invalid.");
        foreach (RestoreFile entry in files)
        {
            if (
                entry is null
                || string.IsNullOrWhiteSpace(entry.Area)
                || string.IsNullOrWhiteSpace(entry.Relative)
                || !ProfileSyncPolicy.AllowsFile(entry.Area, entry.Relative)
            )
            {
                throw new InvalidDataException("Restore recovery contains an invalid profile path.");
            }
            string file = ResolveTarget(entry.Area, entry.Relative);
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file)));
            if (entry.HadFile)
            {
                WriteAtomic(file, File.ReadAllBytes(Path.Combine(rollback, key + ".json")));
            }
            else
            {
                File.Delete(file);
            }
        }
        File.Delete(journal);
    }

    /// <summary>Reads search-selection keys as text and all other profile files as JSON.</summary>
    private static JsonNode? ReadContent(string relative, byte[] bytes) =>
        ProfileSyncPolicy.IsSearchSelection(relative)
            ? JsonValue.Create(Encoding.UTF8.GetString(bytes))
            : JsonNode.Parse(bytes, documentOptions: JsonOptions);

    /// <summary>Rejects even well-formed snapshots that contain forbidden file or setting keys.</summary>
    internal static void ValidateScope(ProfileSnapshot snapshot)
    {
        foreach (string key in snapshot.Portable.Keys)
        {
            _ = ProfileSyncPolicy.ParseKey(key, machine: false);
        }

        foreach (string key in snapshot.Machine.Keys)
        {
            _ = ProfileSyncPolicy.ParseKey(key, machine: true);
        }
    }

    /// <summary>Replaces a file through a unique temporary file on the same filesystem.</summary>
    internal static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Updates one JSON property while retaining settings excluded from synchronization.</summary>
    private static JsonNode? SetPointer(JsonNode? root, string pointer, JsonNode? value, bool deleted)
    {
        if (pointer.Length == 0)
        {
            return deleted ? null : value?.DeepClone();
        }

        string[] parts = pointer.Split('/').Skip(1).Select(ProfileSyncPolicy.Unescape).ToArray();
        if (deleted && root is null)
        {
            return null;
        }
        root ??= new JsonObject();
        if (root is not JsonObject obj)
        {
            throw new InvalidDataException("The backup's JSON structure conflicts with the local profile.");
        }

        var parents = new List<(JsonObject Parent, string Property)>();
        foreach (string part in parts[..^1])
        {
            if (obj[part] is null)
            {
                if (deleted)
                {
                    return root;
                }
                obj[part] = new JsonObject();
            }

            parents.Add((obj, part));
            obj = obj[part] as JsonObject ?? throw new InvalidDataException("The backup has incompatible JSON paths.");
        }
        if (deleted)
        {
            obj.Remove(parts[^1]);
            return RemoveEmptyParents((JsonObject)root, obj, parents);
        }
        else
        {
            obj[parts[^1]] = value?.DeepClone();
        }

        return root;
    }

    /// <summary>Removes containers emptied by a deletion so saved documents do not return as empty records.</summary>
    private static JsonObject? RemoveEmptyParents(
        JsonObject root,
        JsonObject current,
        List<(JsonObject Parent, string Property)> parents
    )
    {
        foreach ((JsonObject parent, string property) in parents.AsEnumerable().Reverse())
        {
            if (current.Count != 0)
            {
                break;
            }
            parent.Remove(property);
            current = parent;
        }
        return root.Count == 0 ? null : root;
    }

    /// <summary>Resolves paths only beneath the configured roots and refuses symbolic links.</summary>
    private string ResolveTarget(string area, string relative)
    {
        string root = area == "config" ? paths.ConfigDirectory : paths.DataDirectory;
        string file = Path.GetFullPath(Path.Combine(root, relative));
        if (Directory.Exists(file))
        {
            throw new InvalidDataException("A profile file path is occupied by a directory.");
        }
        string? ancestor = file;
        while (ancestor is not null && ancestor != Path.GetPathRoot(ancestor))
        {
            if (
                (File.Exists(ancestor) || Directory.Exists(ancestor))
                && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0
            )
            {
                throw new InvalidDataException("Backup restore does not follow symbolic links.");
            }

            ancestor = Path.GetDirectoryName(ancestor);
        }
        return file;
    }

    /// <summary>Returns profile roots without following external caches or journal paths.</summary>
    private IEnumerable<(string Area, string Root)> Roots() =>
        [("config", paths.ConfigDirectory), ("data", paths.DataDirectory)];

    /// <summary>Walks only real directories so a linked journal or cache cannot enter a backup.</summary>
    private static IEnumerable<string> EnumerateSafeFiles(string root, string area, string relative = "")
    {
        foreach (string item in Directory.EnumerateFileSystemEntries(root))
        {
            if (IsLayoutProfileFile(area, relative + Path.GetFileName(item)))
            {
                continue;
            }
            FileAttributes attributes = File.GetAttributes(item);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                yield return item;
            }
            else if (
                Path.GetFileName(item)
                    is not ("profile-sync" or "cache" or "logs" or "backups" or "updates" or "legacy-backups")
                && ProfileSyncPolicy.AllowsDirectory(area, (relative + Path.GetFileName(item)).TrimEnd('/'))
            )
            {
                foreach (string file in EnumerateSafeFiles(item, area, relative + Path.GetFileName(item) + "/"))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>Generates a local installation ID, incorporating a hash of the available machine identifier.</summary>
    private ProfileSyncDevice LoadDevice(Func<string> machineIdentity)
    {
        string path = Path.Combine(StateDirectory, "device.json");
        string platform = OperatingSystem.IsWindows() ? "Windows" : "Other";
        if (OperatingSystem.IsLinux())
        {
            platform = "Linux";
        }
        string identity = machineIdentity();
        string fingerprint = string.IsNullOrWhiteSpace(identity)
            ? ""
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{platform}:{identity}")));
        if (File.Exists(path))
        {
            try
            {
                ProfileSyncDevice? existing = JsonSerializer.Deserialize<ProfileSyncDevice>(File.ReadAllBytes(path));
                if (
                    existing is not null
                    && existing.MachineHash == fingerprint
                    && existing.Platform == platform
                    && !string.IsNullOrWhiteSpace(existing.Id)
                )
                {
                    return existing with { Name = Environment.MachineName };
                }
            }
            catch (JsonException)
            { /* A new local identity keeps a malformed identity from preventing startup. */
            }
        }
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{fingerprint}:{Guid.NewGuid():N}")));
        var device = new ProfileSyncDevice(id, Environment.MachineName, platform, fingerprint);
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(device));
        return device;
    }

    /// <summary>Reads an OS machine identifier; raw identifiers are never uploaded.</summary>
    private static string ReadMachineIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            return Registry
                    .GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")
                    ?.ToString()
                ?? "";
        }

        return OperatingSystem.IsLinux() && File.Exists("/etc/machine-id")
            ? File.ReadAllText("/etc/machine-id").Trim()
            : "";
    }
}

/// <summary>One recoverable profile file in a restore transaction.</summary>
internal sealed record RestoreFile(string Area, string Relative, bool HadFile);
