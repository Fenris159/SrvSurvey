using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journeys;

public sealed class JourneyJournalHistoryReader
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly string[] journalDirectories;

    public JourneyJournalHistoryReader(string journalDirectory)
        : this([journalDirectory]) { }

    public JourneyJournalHistoryReader(IReadOnlyList<string> journalDirectories)
    {
        ArgumentNullException.ThrowIfNull(journalDirectories);
        var paths = new HashSet<string>(PathComparer);
        foreach (string path in journalDirectories)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            paths.Add(Path.GetFullPath(path));
        }
        if (paths.Count == 0)
        {
            throw new ArgumentException("At least one journal folder is required.", nameof(journalDirectories));
        }
        this.journalDirectories = paths.ToArray();
    }

    public Task<JourneyJournalSystemSearchResult> FindLatestFsdJumpAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        CancellationToken cancellationToken = default
    ) => FindLatestEntryAsync(frontierId, isOdyssey, systemAddress, fsdJumpsOnly: true, cancellationToken);

    public Task<JourneyJournalSystemVisitsResult> FindArrivalsAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        DateOnly? onOrAfter = null,
        CancellationToken cancellationToken = default
    ) =>
        FindMatchingArrivalsAsync(
            frontierId,
            isOdyssey,
            system => system.SystemAddress == systemAddress,
            onOrAfter,
            cancellationToken
        );

    public Task<JourneyJournalSystemVisitsResult> FindArrivalsAsync(
        string frontierId,
        bool isOdyssey,
        string systemName,
        DateOnly? onOrAfter = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        string query = systemName.Trim();
        return FindMatchingArrivalsAsync(
            frontierId,
            isOdyssey,
            system => system.Name.Contains(query, StringComparison.OrdinalIgnoreCase),
            onOrAfter,
            cancellationToken
        );
    }

    private async Task<JourneyJournalSystemVisitsResult> FindMatchingArrivalsAsync(
        string frontierId,
        bool isOdyssey,
        Func<JourneySystemReference, bool> matches,
        DateOnly? onOrAfter,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        EnsureDirectoriesExist();

        var errors = new List<string>();
        var entries = new List<JourneyJournalSystemEntry>();
        foreach (FileInfo file in EnumerateJournalFiles(descending: true, errors))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JournalFileReadResult? read = await TryReadFileAsync(file, onOrAfter, errors, cancellationToken)
                .ConfigureAwait(false);
            if (read is null)
            {
                continue;
            }
            entries.AddRange(MatchingSystems(read, frontierId, isOdyssey, matches, onOrAfter, includeLocations: false));
        }

        return new(
            entries
                .OrderByDescending(entry => entry.Event.Timestamp)
                .ThenBy(entry => entry.JournalFileName, StringComparer.Ordinal)
                .DistinctBy(entry => (entry.System.SystemAddress, entry.Event.Timestamp))
                .ToArray(),
            errors
        );
    }

    private static IEnumerable<JourneyJournalSystemEntry> MatchingSystems(
        JournalFileReadResult read,
        string frontierId,
        bool isOdyssey,
        Func<JourneySystemReference, bool> matches,
        DateOnly? onOrAfter,
        bool includeLocations
    )
    {
        foreach (JournalEventEnvelope entry in CommanderEvents(read, frontierId, isOdyssey))
        {
            if (
                IsSystemEvent(entry, includeLocations)
                && entry.Timestamp is { } timestamp
                && (onOrAfter is null || DateOnly.FromDateTime(timestamp.UtcDateTime) >= onOrAfter)
                && TryGetSystemReference(entry.Payload, out JourneySystemReference? system)
                && matches(system)
            )
            {
                yield return new(read.FileName, entry, system);
            }
        }
    }

    private static bool IsSystemEvent(JournalEventEnvelope entry, bool includeLocations) =>
        entry.EventName is "FSDJump" or "CarrierJump" || (includeLocations && entry.EventName == "Location");

    public Task<JourneyJournalSystemSearchResult> FindCurrentLocationAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        CancellationToken cancellationToken = default
    ) => FindLatestEntryAsync(frontierId, isOdyssey, systemAddress, fsdJumpsOnly: false, cancellationToken);

    private async Task<JourneyJournalSystemSearchResult> FindLatestEntryAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        bool fsdJumpsOnly,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        EnsureDirectoriesExist();
        var errors = new List<string>();
        JourneyJournalSystemEntry? latest = null;
        foreach (FileInfo file in EnumerateJournalFiles(descending: true, errors))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateOnly? minimum = latest?.Event.Timestamp is { } timestamp
                ? DateOnly.FromDateTime(timestamp.UtcDateTime)
                : null;
            JournalFileReadResult? read = await TryReadFileAsync(file, minimum, errors, cancellationToken)
                .ConfigureAwait(false);
            if (read is null)
            {
                continue;
            }
            JourneyJournalSystemEntry? location = MatchingSystems(
                    read,
                    frontierId,
                    isOdyssey,
                    system => system.SystemAddress == systemAddress,
                    null,
                    includeLocations: true
                )
                .Where(entry => !fsdJumpsOnly || entry.Event.EventName == "FSDJump")
                .OrderByDescending(entry => entry.Event.Timestamp)
                .FirstOrDefault();
            if (location is not null && (latest is null || location.Event.Timestamp > latest.Event.Timestamp))
            {
                latest = location;
            }
        }
        return new(latest, errors);
    }

    private static async Task<bool> EndsBeforeAsync(
        FileInfo file,
        DateOnly minimum,
        CancellationToken cancellationToken
    )
    {
        if (
            JournalHistoryAnalyzer.TryGetJournalTimestamp(file.Name, out DateTimeOffset opened)
            && DateOnly.FromDateTime(opened.UtcDateTime) >= minimum
        )
        {
            return false;
        }
        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous
        );
        long offset = Math.Max(0, stream.Length - 16 * 1024);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: offset == 0);
        string[] lines = (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Split('\n');
        // The first line can be a fragment when the read starts partway through the file.
        for (int index = lines.Length - 1; index >= (offset == 0 ? 0 : 1); index--)
        {
            if (
                JournalEventEnvelope.TryParse(lines[index], out JournalEventEnvelope? entry, out _)
                && entry?.Timestamp is { } timestamp
            )
            {
                return DateOnly.FromDateTime(timestamp.UtcDateTime) < minimum;
            }
        }
        // A large, incomplete, or undated final record cannot prove that this journal is old.
        return false;
    }

    private static IEnumerable<JournalEventEnvelope> CommanderEvents(
        JournalFileReadResult read,
        string frontierId,
        bool isOdyssey
    )
    {
        string? owner = null;
        bool odyssey = true;
        JournalEventEnvelope? header = null;
        foreach (JournalEventEnvelope entry in read.Events)
        {
            if (entry.EventName == "Fileheader")
            {
                odyssey = GetBoolean(entry.Payload, "Odyssey") ?? true;
                owner = null;
                header = entry;
                continue;
            }
            if (entry.EventName is "Commander" or "LoadGame")
            {
                owner = GetString(entry.Payload, "FID") ?? owner;
            }
            if (odyssey != isOdyssey || !string.Equals(owner, frontierId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (header is not null)
            {
                yield return header;
                header = null;
            }
            yield return entry;
        }
    }

    public Task<JourneyJournalReadResult> ReadFromAsync(
        string startingJournal,
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default
    ) => ReadFromAsync(startingJournal, frontierId, isOdyssey, null, cancellationToken);

    public async Task<JourneyJournalReadResult> ReadFromAsync(
        string startingJournal,
        string frontierId,
        bool isOdyssey,
        DateTimeOffset? startingTimestamp,
        CancellationToken cancellationToken = default
    )
    {
        ValidateJournalFileName(startingJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        EnsureDirectoriesExist();

        var events = new List<JournalEventEnvelope>();
        var errors = new List<string>();
        var seen = new HashSet<(string Journal, int Index, string Json)>();
        FileInfo[] files = EnumerateJournalFiles(descending: false, errors).ToArray();
        string missingMessage = $"The starting journal was not found for this commander: {startingJournal}";
        FileInfo startingFile =
            files.FirstOrDefault(file => PathComparer.Equals(file.Name, startingJournal))
            ?? throw new FileNotFoundException(missingMessage, startingJournal);
        DateTimeOffset boundary = startingTimestamp ?? JournalOrder(startingFile);
        var minimum = DateOnly.FromDateTime(boundary.UtcDateTime);
        bool foundStartingJournal = false;
        foreach (FileInfo file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JournalFileReadResult? read = await TryReadFileAsync(file, minimum, errors, cancellationToken)
                .ConfigureAwait(false);
            if (read is null)
            {
                continue;
            }
            JournalEventEnvelope[] owned = CommanderEvents(read, frontierId, isOdyssey).ToArray();
            bool isStartingFile = PathComparer.Equals(file.Name, startingJournal);
            foundStartingJournal |= isStartingFile && owned.Length > 0;
            // Count copied journals once, while preserving repeated identical records within one journal.
            // Keep the starting file's preamble for scan context; overlapping files contribute from the time boundary.
            events.AddRange(
                owned.Where(
                    (entry, index) =>
                        (isStartingFile || entry.Timestamp >= boundary)
                        && seen.Add((read.FileName, index, entry.RawJson))
                )
            );
        }
        if (!foundStartingJournal)
        {
            throw new FileNotFoundException(missingMessage, startingJournal);
        }

        return new JourneyJournalReadResult(events.OrderBy(entry => entry.Timestamp).ToArray(), errors);
    }

    private void EnsureDirectoriesExist()
    {
        if (!journalDirectories.Any(Directory.Exists))
        {
            throw new DirectoryNotFoundException(
                $"The journal folders do not exist: {string.Join(", ", journalDirectories)}"
            );
        }
    }

    private IEnumerable<FileInfo> EnumerateJournalFiles(bool descending, List<string> errors)
    {
        var files = new List<FileInfo>();
        foreach (string directory in journalDirectories)
        {
            try
            {
                files.AddRange(
                    new DirectoryInfo(directory).EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
                );
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Could not read journal folder {directory}: {exception.Message}");
            }
        }
        return descending
            ? files
                .OrderByDescending(JournalOrder)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ThenBy(file => file.FullName, PathComparer)
            : files
                .OrderBy(JournalOrder)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ThenBy(file => file.FullName, PathComparer);
    }

    private static DateTime JournalOrder(FileInfo file) =>
        JournalHistoryAnalyzer.TryGetJournalTimestamp(file.Name, out DateTimeOffset timestamp)
            ? timestamp.UtcDateTime
            : file.LastWriteTimeUtc;

    private static async Task<JournalFileReadResult?> TryReadFileAsync(
        FileInfo file,
        DateOnly? onOrAfter,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (
                onOrAfter is { } minimum
                && await EndsBeforeAsync(file, minimum, cancellationToken).ConfigureAwait(false)
            )
            {
                return null;
            }
            JournalFileReadResult read = await ReadFileAsync(file, cancellationToken).ConfigureAwait(false);
            errors.AddRange(read.Errors);
            return read;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not read journal {file.FullName}: {exception.Message}");
            return null;
        }
    }

    private static async Task<JournalFileReadResult> ReadFileAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var events = new List<JournalEventEnvelope>();
        var errors = new List<string>();

        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        int lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (
                !JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? journalEvent, out string? error)
                || journalEvent is null
            )
            {
                errors.Add($"{file.Name}, line {lineNumber}: " + (error ?? "The journal entry could not be parsed."));
                continue;
            }

            events.Add(journalEvent);
        }

        return new JournalFileReadResult(file.Name, events, errors);
    }

    private static void ValidateJournalFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || !fileName.StartsWith("Journal.", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException("The starting journal must be a Journal.*.log file name.", nameof(fileName));
        }
    }

    internal static bool TryGetSystemReference(JsonElement root, out JourneySystemReference systemReference)
    {
        systemReference = null!;
        string? name = GetString(root, "StarSystem");
        long? address = GetInt64(root, "SystemAddress");
        if (
            string.IsNullOrWhiteSpace(name)
            || address is null
            || !root.TryGetProperty("StarPos", out JsonElement position)
            || position.ValueKind != JsonValueKind.Array
            || position.GetArrayLength() != 3
        )
        {
            return false;
        }

        JsonElement[] coordinates = position.EnumerateArray().ToArray();
        if (
            coordinates.Any(coordinate => coordinate.ValueKind != JsonValueKind.Number)
            || !coordinates[0].TryGetDouble(out double x)
            || !coordinates[1].TryGetDouble(out double y)
            || !coordinates[2].TryGetDouble(out double z)
        )
        {
            return false;
        }

        try
        {
            systemReference = new JourneySystemReference(name, address.Value, new GalacticCoordinate(x, y, z));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number)
            ? number
            : null;
    }

    private sealed record JournalFileReadResult(
        string FileName,
        IReadOnlyList<JournalEventEnvelope> Events,
        IReadOnlyList<string> Errors
    );
}

public sealed record JourneyJournalSystemEntry(
    string JournalFileName,
    JournalEventEnvelope Event,
    JourneySystemReference System
);

public sealed record JourneyJournalSystemSearchResult(JourneyJournalSystemEntry? Entry, IReadOnlyList<string> Errors);

public sealed record JourneyJournalSystemVisitsResult(
    IReadOnlyList<JourneyJournalSystemEntry> Entries,
    IReadOnlyList<string> Errors
);

public sealed record JourneyJournalReadResult(IReadOnlyList<JournalEventEnvelope> Events, IReadOnlyList<string> Errors);
