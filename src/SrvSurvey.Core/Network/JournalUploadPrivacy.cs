using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Network;

/// <summary>Applies expedition privacy to delivery without changing provider consent or durable capture.</summary>
public interface IJournalUploadPrivacyAware
{
    void SetUploadPrivacy(JournalUploadPrivacy policy) { }
}

/// <summary>Persists per-commander docking watermarks, releasing only evidence recorded through a live docking.</summary>
public sealed class JournalUploadPrivacy
{
    internal const int ExpeditionMaximumReports = 131072;
    internal const long ExpeditionMaximumBytes = 512L * 1024 * 1024;
    private readonly string path;
    private readonly Lock sync = new();
    private Dictionary<string, DateTimeOffset> dockedThrough = new(StringComparer.OrdinalIgnoreCase);
    private string? commander;
    private string? frontierId;
    private bool live = true;
    private bool enabled;

    public JournalUploadPrivacy(string path, bool enabled)
    {
        this.path = path;
        this.enabled = enabled;
        try
        {
            if (File.Exists(path))
            {
                Dictionary<string, DateTimeOffset>? saved = JsonSerializer.Deserialize<
                    Dictionary<string, DateTimeOffset>
                >(File.ReadAllText(path));
                dockedThrough = new(saved ?? [], StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Missing or unreadable release evidence holds every report until another live docking is durably recorded.
        }
    }

    public event Action<bool>? Changed;

    public string? LastError { get; private set; }

    public bool Enabled
    {
        get
        {
            lock (sync)
            {
                return enabled;
            }
        }
    }

    public void SetEnabled(bool value)
    {
        lock (sync)
        {
            if (enabled == value)
            {
                return;
            }
            enabled = value;
        }
        Changed?.Invoke(value);
    }

    public bool CanSend(string? owner, string? timestamp)
    {
        lock (sync)
        {
            return !enabled
                || (
                    owner is not null
                    && dockedThrough.TryGetValue(owner.Trim(), out DateTimeOffset docked)
                    && DateTimeOffset.TryParse(
                        timestamp,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset at
                    )
                    && at <= docked
                );
        }
    }

    public bool CanSendJournal(string? owner, string json) =>
        !Enabled
        || (
            JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _)
            && CanSend(owner, entry?.Timestamp?.ToString("O", CultureInfo.InvariantCulture))
        );

    /// <summary>Always tracks identities, but bootstrap, replay and unverified sources cannot release a queue.</summary>
    public void Observe(IReadOnlyList<JournalEventEnvelope> events, bool allowRelease)
    {
        foreach (JournalEventEnvelope entry in events)
        {
            if (entry.EventName == "Fileheader")
            {
                commander = null;
                frontierId = null;
                live =
                    !entry.Payload.TryGetProperty("Odyssey", out JsonElement value)
                    || value.ValueKind == JsonValueKind.True;
            }
            else if (entry.EventName is "Commander" or "LoadGame")
            {
                ReadIdentity(entry);
            }
            else if (
                allowRelease
                && live
                && entry.EventName == "Docked"
                && commander is not null
                && frontierId is not null
                && entry.Timestamp is { } at
            )
            {
                try
                {
                    Release(commander, at);
                    LastError = null;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    LastError =
                        "Docking could not be saved. Stealth Mode is retaining uploads until a docking can be saved.";
                }
            }
        }
    }

    private void ReadIdentity(JournalEventEnvelope entry)
    {
        string? name = Text(entry.Payload, entry.EventName == "Commander" ? "Name" : "Commander");
        string? fid = Text(entry.Payload, "FID");
        if (
            (fid is not null && !string.Equals(fid, frontierId, StringComparison.OrdinalIgnoreCase))
            || (name is not null && !string.Equals(name, commander, StringComparison.OrdinalIgnoreCase))
        )
        {
            commander = name;
            frontierId = fid;
        }
    }

    private void Release(string owner, DateTimeOffset at)
    {
        lock (sync)
        {
            if (dockedThrough.TryGetValue(owner, out DateTimeOffset previous) && at <= previous)
            {
                return;
            }
            var updated = new Dictionary<string, DateTimeOffset>(dockedThrough, StringComparer.OrdinalIgnoreCase)
            {
                [owner] = at,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(file, updated);
                    file.Flush(true);
                }
                File.Move(temporary, path, true);
                dockedThrough = updated;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        Changed?.Invoke(false);
    }

    private static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
}
