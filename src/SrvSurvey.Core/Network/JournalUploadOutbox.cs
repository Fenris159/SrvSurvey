using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SrvSurvey.Core.Network;

/// <summary>Retains immutable provider payloads and consumed record identities in a flushed, leased append log.</summary>
internal sealed class JournalUploadOutbox<T> : IDisposable
    where T : class
{
    private const long MaximumFileBytes = 64L * 1024 * 1024;
    private const long CompactAfterBytes = 8L * 1024 * 1024;
    private const int MaximumPending = 4096;
    private const int MaximumReceipts = 65536;
    private readonly string folder;
    private readonly Func<T, string> identity;
    private readonly Func<T, bool> validate;
    private readonly Action<string> log;
    private readonly Lock sync = new();
    private readonly Dictionary<string, T> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> receipts = new();
    private FileStream? lease;
    private string? owner;
    private string? path;
    private bool disposed;
    private bool checkpointPending;
    private string consentEpoch;

    /// <summary>Creates isolated provider storage without opening files until an attributable session is available.</summary>
    public JournalUploadOutbox(
        string directory,
        string provider,
        Func<T, string> identity,
        Func<T, bool> validate,
        Action<string> log
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (provider.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                "A provider storage name may contain only letters, digits and hyphens.",
                nameof(provider)
            );
        }
        folder = Path.Combine(Path.GetFullPath(directory), provider);
        this.identity = identity;
        this.validate = validate;
        this.log = log;
        consentEpoch = string.Empty;
        try
        {
            consentEpoch = ReadConsentEpoch();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report("Journal upload storage is unavailable; application startup can continue without delivery.");
        }
    }

    /// <summary>Reports the persisted provider deadline so reopening cannot bypass a retry cooldown.</summary>
    public DateTimeOffset? NotBefore { get; private set; }

    /// <summary>Reports whether another application instance has revoked this captured consent generation.</summary>
    public bool HasConsent
    {
        get
        {
            lock (sync)
            {
                try
                {
                    return !disposed && consentEpoch == ReadConsentEpoch();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>Accepts an explicit opt-in after a prior revocation without restoring revoked queue entries.</summary>
    public void AcceptConsent()
    {
        lock (sync)
        {
            try
            {
                string current = ReadConsentEpoch();
                if (current != consentEpoch)
                {
                    CloseOwner();
                    consentEpoch = current;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                consentEpoch = Guid.NewGuid().ToString("N");
                CloseOwner();
                Report("Journal upload consent could not open storage; delivery remains paused.");
            }
        }
    }

    /// <summary>Revokes retained reports even before a commander is selected and invalidates other instances' old consent.</summary>
    public void ClearAll()
    {
        try
        {
            ClearRetainedStorage();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lock (sync)
            {
                consentEpoch = Guid.NewGuid().ToString("N");
                CloseOwner();
            }
            Report(
                "Journal uploads stopped, but unavailable storage prevented clearing retained files: "
                    + exception.Message
            );
        }
    }

    /// <summary>Persists provider-wide revocation before removing any unleased retained reports.</summary>
    private void ClearRetainedStorage()
    {
        lock (sync)
        {
            Directory.CreateDirectory(folder);
            string marker = Path.Combine(folder, "consent-epoch");
            string temporary = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string epoch = Guid.NewGuid().ToString("N");
            using (FileStream file = OpenPrivate(temporary, FileMode.CreateNew))
            {
                file.Write(Encoding.UTF8.GetBytes(epoch));
                file.Flush(true);
            }
            File.Move(temporary, marker, true);
            consentEpoch = epoch;
            CloseOwner();
            foreach (string retained in Directory.EnumerateFiles(folder, "*.jsonl"))
            {
                try
                {
                    using var held = new FileStream(
                        retained + ".lease",
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None
                    );
                    File.Delete(retained);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A leased record cannot be removed here; its revoked epoch prevents recovery or further delivery.
                    Report("Journal uploads revoked a queue currently leased by another instance.");
                }
            }
        }
    }

    /// <summary>Reads the provider-wide revocation marker without exposing account identity.</summary>
    private string ReadConsentEpoch()
    {
        string marker = Path.Combine(folder, "consent-epoch");
        return File.Exists(marker) ? File.ReadAllText(marker) : string.Empty;
    }

    /// <summary>Acquires one owner's queue and returns recovered payloads only when its ownership changes.</summary>
    public bool TryOpen(string capturedOwner, out IReadOnlyList<T>? recovered)
    {
        recovered = null;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capturedOwner))).ToLowerInvariant();
        string? error = null;
        bool opened;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!HasConsent)
            {
                return false;
            }
            if (owner == key)
            {
                return true;
            }
            CloseOwner();
            try
            {
                Directory.CreateDirectory(folder);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        folder,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    );
                }
                path = Path.Combine(folder, key + ".jsonl");
                lease = new FileStream(path + ".lease", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                owner = key;
                Load();
                recovered = pending.Values.ToArray();
                opened = true;
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or JsonException
                            or DecoderFallbackException
                )
            {
                CloseOwner();
                error =
                    $"Journal uploads could not open durable storage for {Path.GetFileName(folder)} ({exception.GetType().Name}); delivery is paused.";
                opened = false;
            }
        }
        Report(error);
        return opened;
    }

    /// <summary>Commits new payloads before returning them for network delivery, suppressing already captured record identities.</summary>
    public IReadOnlyList<T> Enqueue(IEnumerable<T> values)
    {
        lock (sync)
        {
            EnsureOwner();
            T[] added = values
                .Where(value =>
                    validate(value) && !seen.Contains(identity(value)) && !pending.ContainsKey(identity(value))
                )
                .DistinctBy(identity, StringComparer.Ordinal)
                .ToArray();
            if (pending.Count + added.Length > MaximumPending)
            {
                throw new IOException("The durable journal upload queue is full; existing reports were retained.");
            }
            if (added.Length != 0)
            {
                Append(new Delta(added, [], [], NotBefore, false));
            }
            return added;
        }
    }

    /// <summary>Durably records final outcomes before removing their pending payloads.</summary>
    public void Complete(IEnumerable<string> identities)
    {
        lock (sync)
        {
            EnsureOwner();
            string[] completed = identities.Where(pending.ContainsKey).Distinct(StringComparer.Ordinal).ToArray();
            if (completed.Length != 0)
            {
                var change = new Delta([], completed, [], NotBefore, false);
                try
                {
                    Append(change);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A confirmed server outcome must not become another HTTP attempt in this process.
                    Apply(change);
                    checkpointPending = true;
                    Report(
                        "Journal upload acknowledgement could not be checkpointed; further sends wait for storage recovery."
                    );
                }
            }
        }
    }

    /// <summary>Persists the retry deadline independently of queue changes.</summary>
    public void DeferUntil(DateTimeOffset? deadline)
    {
        lock (sync)
        {
            EnsureOwner();
            if (NotBefore != deadline)
            {
                Append(new Delta([], [], [], deadline, false));
            }
        }
    }

    /// <summary>Returns immutable retained payloads for a recovered sender without consuming them.</summary>
    public IReadOnlyList<T> ReadPending()
    {
        lock (sync)
        {
            return pending.Values.ToArray();
        }
    }

    /// <summary>Records explicit consent revocation without treating ordinary shutdown as permission to discard reports.</summary>
    public void Clear()
    {
        lock (sync)
        {
            if (lease is not null)
            {
                Append(new Delta([], [], [], null, true));
            }
        }
    }

    /// <summary>Rejects delivery without an exclusive, attributable queue lease.</summary>
    private void EnsureOwner()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lease is null || path is null || !HasConsent)
        {
            throw new IOException("Durable journal upload storage is not owned by this sender.");
        }
    }

    /// <summary>Flushes a complete delta before exposing its state and compacts only after the log has grown.</summary>
    private void Append(Delta delta)
    {
        EnsureOwner();
        if (checkpointPending)
        {
            Compact();
            checkpointPending = false;
        }
        byte[] bytes = Encode(delta);
        long length = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (length > CompactAfterBytes || length + bytes.Length > MaximumFileBytes)
        {
            Compact();
            length = new FileInfo(path!).Length;
        }
        if (length + bytes.Length > MaximumFileBytes)
        {
            throw new IOException(
                "The durable journal upload storage limit was reached; retained reports were not removed."
            );
        }
        try
        {
            using FileStream file = OpenPrivate(path!, FileMode.Append);
            file.Write(bytes);
            file.Flush(true);
        }
        catch
        {
            try
            {
                using FileStream file = OpenPrivate(path!, FileMode.Open);
                file.SetLength(length);
                file.Flush(true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                checkpointPending = true;
            }
            throw;
        }
        Apply(delta);
    }

    /// <summary>Encodes only captured payloads and hashed owner identities, never credentials supplied by a live sender.</summary>
    private byte[] Encode(Delta delta) =>
        Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(new StoredDelta(1, owner!, consentEpoch, delta), Formatting.None) + "\n"
        );

    /// <summary>Replays complete flushed rows and removes only an unfinished last append before accepting further writes.</summary>
    private void Load()
    {
        if (!File.Exists(path))
        {
            return;
        }
        if (new FileInfo(path).Length > MaximumFileBytes)
        {
            throw new IOException("The durable journal upload log exceeds its supported size.");
        }
        byte[] bytes = File.ReadAllBytes(path);
        int committedLength = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        if (committedLength != bytes.Length)
        {
            using FileStream file = OpenPrivate(path, FileMode.Open);
            file.SetLength(committedLength);
            file.Flush(true);
        }
        int offset = 0;
        while (offset < committedLength)
        {
            int begin = offset;
            int end = Array.IndexOf(bytes, (byte)'\n', begin, committedLength - begin);
            offset = end + 1;
            if (end == begin)
            {
                continue;
            }
            ReplayRecord(bytes, begin, end - begin);
        }
        if (pending.Count > MaximumPending)
        {
            throw new IOException("The durable journal upload log contains too many pending reports.");
        }
    }

    /// <summary>Validates and replays one committed row without allowing a malformed row to discard adjacent reports.</summary>
    private void ReplayRecord(byte[] bytes, int offset, int length)
    {
        try
        {
            string line = new UTF8Encoding(false, true).GetString(bytes, offset, length);
            using var text = new StringReader(line);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
            var document = JObject.Load(reader);
            RemoveMalformedAdditions(document);
            StoredDelta record =
                document.ToObject<StoredDelta>()
                ?? throw new JsonSerializationException("A durable upload record was null.");
            if (record.ConsentEpoch != consentEpoch)
            {
                return;
            }
            if (record.Schema != 1 || record.Owner != owner || record.Change is null)
            {
                throw new JsonSerializationException("A durable upload record has an invalid schema or owner.");
            }
            if (record.Change.Added is null || record.Change.Completed is null || record.Change.Receipts is null)
            {
                throw new JsonSerializationException("A durable upload record is missing required fields.");
            }
            if (record.Change.Added.Any(value => value is null || !validate(value)))
            {
                throw new JsonSerializationException("A durable upload payload is malformed.");
            }
            Apply(record.Change);
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            // Keep the damaged evidence and continue recovering independent committed records.
            Report("Journal uploads skipped a malformed retained record in " + path + ".");
        }
    }

    /// <summary>Skips individual malformed payloads while retaining other valid additions in the same committed record.</summary>
    private void RemoveMalformedAdditions(JObject document)
    {
        if (document["Change"] is not JObject change || change["Added"] is not JArray additions)
        {
            return;
        }
        foreach (JToken value in additions.ToArray())
        {
            try
            {
                T? decoded = value.ToObject<T>();
                if (decoded is null || !validate(decoded))
                {
                    value.Remove();
                    Report("Journal uploads skipped a malformed retained payload in " + path + ".");
                }
            }
            catch (JsonException)
            {
                value.Remove();
                Report("Journal uploads skipped a malformed retained payload in " + path + ".");
            }
        }
    }

    /// <summary>Applies a committed delta while retaining a bounded replay ledger after acknowledgement.</summary>
    private void Apply(Delta delta)
    {
        if (delta.Clear)
        {
            pending.Clear();
            seen.Clear();
            receipts.Clear();
        }
        foreach (string receipt in delta.Receipts)
        {
            Remember(receipt);
        }
        foreach (T value in delta.Added)
        {
            string id = identity(value);
            if (!seen.Contains(id))
            {
                pending.TryAdd(id, value);
            }
        }
        foreach (string completed in delta.Completed)
        {
            pending.Remove(completed);
            Remember(completed);
        }
        NotBefore = delta.NotBefore;
    }

    /// <summary>Bounds acknowledged-record history independently of the pending payload dictionary.</summary>
    private void Remember(string id)
    {
        if (seen.Add(id))
        {
            receipts.Enqueue(id);
        }
        while (receipts.Count > MaximumReceipts)
        {
            string expired = receipts.Dequeue();
            seen.Remove(expired);
        }
    }

    /// <summary>Replaces a large append log with one flushed snapshot while keeping the separate ownership lease.</summary>
    private void Compact()
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = Encode(new Delta(pending.Values.ToArray(), [], seen.ToArray(), NotBefore, true));
            if (bytes.Length > MaximumFileBytes)
            {
                throw new IOException("The durable journal upload checkpoint exceeds its storage limit.");
            }
            using (FileStream file = OpenPrivate(temporary, FileMode.CreateNew))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            File.Move(temporary, path!, true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { /* Preserve the original checkpoint error. */
            }
        }
    }

    /// <summary>Creates user-private log files and forces writes through the operating system's cache.</summary>
    private static FileStream OpenPrivate(string filename, FileMode mode)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows() && mode != FileMode.Open)
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        return new FileStream(filename, options);
    }

    /// <summary>Releases one owner's lease without deleting durable payloads or their replay ledger.</summary>
    private void CloseOwner()
    {
        lease?.Dispose();
        lease = null;
        owner = null;
        path = null;
        pending.Clear();
        seen.Clear();
        receipts.Clear();
        NotBefore = null;
        checkpointPending = false;
    }

    /// <summary>Reports storage failures without allowing an application log callback to change delivery behavior.</summary>
    private void Report(string? message)
    {
        if (message is null)
        {
            return;
        }
        try
        {
            log(message);
        }
        catch (Exception)
        { /* Diagnostics cannot authorize or interrupt publication. */
        }
    }

    /// <summary>Closes the leased storage while preserving all captured reports for the next eligible session.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            CloseOwner();
        }
    }

    /// <summary>Captures one transactional queue change and the current provider cooldown.</summary>
    private sealed record Delta(
        T[] Added,
        string[] Completed,
        string[] Receipts,
        DateTimeOffset? NotBefore,
        bool Clear
    );

    /// <summary>Validates the format and owner of every committed queue change before applying it.</summary>
    private sealed record StoredDelta(int Schema, string Owner, string ConsentEpoch, Delta Change);
}
