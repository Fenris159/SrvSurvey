using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.ProfileSync;

/// <summary>A portable JSON value or deletion, with per-machine change counters.</summary>
public sealed record SyncValue(JsonNode? Value, bool Deleted, Dictionary<string, long> Clock)
{
    /// <summary>Returns whether this change contains every change observed by the other value.</summary>
    public bool Includes(SyncValue other) => other.Clock.All(pair => Clock.GetValueOrDefault(pair.Key) >= pair.Value);
}

/// <summary>A versioned backup containing portable values and this machine's separate settings.</summary>
public sealed record ProfileSnapshot(
    int SchemaVersion,
    string DeviceId,
    string DeviceName,
    string Platform,
    DateTimeOffset CreatedUtc,
    Dictionary<string, SyncValue> Portable,
    Dictionary<string, JsonNode?> Machine,
    string MachineFingerprint = ""
)
{
    public const int CurrentSchema = 1;
    public const int MaximumBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Serializes a snapshot for local export or Google Drive.</summary>
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    /// <summary>Validates a downloaded snapshot before it can change local files.</summary>
    public static ProfileSnapshot Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaximumBytes)
        {
            throw new InvalidDataException("The backup exceeds the supported size.");
        }

        ProfileSnapshot snapshot;
        try
        {
            snapshot =
                JsonSerializer.Deserialize<ProfileSnapshot>(bytes)
                ?? throw new InvalidDataException("The backup is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The backup is not valid JSON.", exception);
        }

        if (
            snapshot.SchemaVersion != CurrentSchema
            || string.IsNullOrWhiteSpace(snapshot.DeviceId)
            || string.IsNullOrWhiteSpace(snapshot.DeviceName)
            || snapshot.Platform is not ("Windows" or "Linux" or "Other")
            || !IsValidFingerprint(snapshot.MachineFingerprint)
            || snapshot.Portable is null
            || snapshot.Machine is null
            || snapshot.Portable.Count > 100_000
            || snapshot.Machine.Count > 100_000
            || snapshot.Portable.Any(pair =>
                pair.Value is null
                || pair.Value.Clock is null
                || pair.Value.Clock.Count == 0
                || pair.Value.Clock.Any(counter => string.IsNullOrWhiteSpace(counter.Key) || counter.Value <= 0)
            )
        )
        {
            throw new InvalidDataException("The backup has an unsupported schema or invalid change counters.");
        }

        return snapshot;
    }

    /// <summary>Accepts an optional SHA-256 machine hash without permitting raw OS identifiers.</summary>
    private static bool IsValidFingerprint(string? value) =>
        value is not null && (value.Length == 0 || (value.Length == 64 && value.All(Uri.IsHexDigit)));
}

/// <summary>Two machines independently changed the same portable value.</summary>
public sealed record SyncConflict(string Key, SyncValue Local, SyncValue Remote);

/// <summary>A merge preserves non-conflicting changes and reports choices that need user input.</summary>
public sealed record SyncMergeResult(Dictionary<string, SyncValue> Values, IReadOnlyList<SyncConflict> Conflicts);

/// <summary>Merges portable changes without using wall-clock time to choose a winner.</summary>
public static class ProfileSyncMerge
{
    /// <summary>Records only values that changed since this machine's last successful sync.</summary>
    public static Dictionary<string, SyncValue> Capture(
        string deviceId,
        IReadOnlyDictionary<string, JsonNode?> current,
        IReadOnlyDictionary<string, SyncValue> baseline
    )
    {
        var result = baseline.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (string key in current.Keys.Union(baseline.Keys, StringComparer.Ordinal))
        {
            bool present = current.TryGetValue(key, out JsonNode? value);
            if (
                baseline.TryGetValue(key, out SyncValue? previous)
                && previous.Deleted == !present
                && (!present || JsonNode.DeepEquals(previous.Value, value))
            )
            {
                continue;
            }

            Dictionary<string, long> clock = previous is null ? [] : new(previous.Clock);
            clock[deviceId] = checked(clock.GetValueOrDefault(deviceId) + 1);
            result[key] = new(value?.DeepClone(), !present, clock);
        }

        return result;
    }

    /// <summary>Merges causally ordered values and retains local values for unresolved conflicts.</summary>
    public static SyncMergeResult Merge(
        IReadOnlyDictionary<string, SyncValue> local,
        IReadOnlyDictionary<string, SyncValue> remote
    )
    {
        var merged = local.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var conflicts = new List<SyncConflict>();
        foreach ((string key, SyncValue incoming) in remote)
        {
            if (!merged.TryGetValue(key, out SyncValue? existing) || incoming.Includes(existing))
            {
                if (existing is not null && existing.Includes(incoming) && !Equal(existing, incoming))
                {
                    throw new InvalidDataException("A backup reuses change counters for different values.");
                }
                merged[key] = incoming;
            }
            else if (!existing.Includes(incoming))
            {
                if (Equal(existing, incoming))
                {
                    merged[key] = existing with { Clock = UnionClock(existing, incoming) };
                }
                else
                {
                    conflicts.Add(new(key, existing, incoming));
                }
            }
        }

        return new(merged, conflicts);
    }

    /// <summary>Records a deliberate choice that supersedes both conflicting versions.</summary>
    public static SyncValue Resolve(string deviceId, SyncConflict conflict, bool useRemote)
    {
        Dictionary<string, long> clock = UnionClock(conflict.Local, conflict.Remote);
        clock[deviceId] = checked(clock.GetValueOrDefault(deviceId) + 1);
        return (useRemote ? conflict.Remote : conflict.Local) with { Clock = clock };
    }

    /// <summary>Compares deletion state and JSON content independently of change counters.</summary>
    private static bool Equal(SyncValue first, SyncValue second) =>
        first.Deleted == second.Deleted && JsonNode.DeepEquals(first.Value, second.Value);

    /// <summary>Combines the highest observed counter for each machine.</summary>
    private static Dictionary<string, long> UnionClock(SyncValue first, SyncValue second) =>
        first
            .Clock.Keys.Union(second.Clock.Keys)
            .ToDictionary(
                key => key,
                key => Math.Max(first.Clock.GetValueOrDefault(key), second.Clock.GetValueOrDefault(key))
            );
}
