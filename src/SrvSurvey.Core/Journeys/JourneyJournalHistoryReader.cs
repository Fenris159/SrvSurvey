using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journeys;

public sealed class JourneyJournalHistoryReader
{
    private readonly string journalDirectory;

    public JourneyJournalHistoryReader(string journalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        this.journalDirectory = Path.GetFullPath(journalDirectory);
    }

    public async Task<JourneyJournalSystemSearchResult> FindLatestFsdJumpAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        CancellationToken cancellationToken = default
    )
    {
        JourneyJournalSystemVisitsResult visits = await FindArrivalsAsync(
                frontierId,
                isOdyssey,
                systemAddress,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return new(visits.Entries.FirstOrDefault(entry => entry.Event.EventName == "FSDJump"), visits.Errors);
    }

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
        EnsureDirectoryExists();

        var errors = new List<string>();
        var entries = new List<JourneyJournalSystemEntry>();
        foreach (FileInfo file in EnumerateJournalFiles(descending: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                onOrAfter is { } minimum
                && await EndsBeforeAsync(file, minimum, cancellationToken).ConfigureAwait(false)
            )
            {
                continue;
            }
            JournalFileReadResult read = await ReadFileAsync(file, cancellationToken).ConfigureAwait(false);
            errors.AddRange(read.Errors);
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

    public async Task<JourneyJournalSystemSearchResult> FindCurrentLocationAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        EnsureDirectoryExists();
        var errors = new List<string>();
        foreach (FileInfo file in EnumerateJournalFiles(descending: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JournalFileReadResult read = await ReadFileAsync(file, cancellationToken).ConfigureAwait(false);
            errors.AddRange(read.Errors);
            JourneyJournalSystemEntry? location = MatchingSystems(
                    read,
                    frontierId,
                    isOdyssey,
                    system => system.SystemAddress == systemAddress,
                    null,
                    includeLocations: true
                )
                .OrderByDescending(entry => entry.Event.Timestamp)
                .FirstOrDefault();
            if (location is not null)
            {
                return new(location, errors);
            }
        }
        return new(null, errors);
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

    public async Task<JourneyJournalReadResult> ReadFromAsync(
        string startingJournal,
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default
    )
    {
        ValidateJournalFileName(startingJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        EnsureDirectoryExists();

        FileInfo[] files = EnumerateJournalFiles(descending: false).ToArray();
        int startIndex = Array.FindIndex(
            files,
            file =>
                string.Equals(
                    file.Name,
                    startingJournal,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                )
        );
        if (startIndex < 0)
        {
            throw new FileNotFoundException(
                $"The starting journal was not found: {startingJournal}",
                Path.Combine(journalDirectory, startingJournal)
            );
        }

        var events = new List<JournalEventEnvelope>();
        var errors = new List<string>();
        for (int index = startIndex; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file = files[index];
            JournalFileReadResult read = await ReadFileAsync(file, cancellationToken).ConfigureAwait(false);
            errors.AddRange(read.Errors);
            events.AddRange(CommanderEvents(read, frontierId, isOdyssey));
        }

        return new JourneyJournalReadResult(events, errors);
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(journalDirectory))
        {
            throw new DirectoryNotFoundException($"The journal folder does not exist: {journalDirectory}");
        }
    }

    private IEnumerable<FileInfo> EnumerateJournalFiles(bool descending)
    {
        IEnumerable<FileInfo> files = new DirectoryInfo(journalDirectory).EnumerateFiles(
            "Journal.*.log",
            SearchOption.TopDirectoryOnly
        );
        return descending
            ? files.OrderByDescending(JournalOrder).ThenByDescending(file => file.Name, StringComparer.Ordinal)
            : files.OrderBy(JournalOrder).ThenBy(file => file.Name, StringComparer.Ordinal);
    }

    private static DateTime JournalOrder(FileInfo file) =>
        JournalHistoryAnalyzer.TryGetJournalTimestamp(file.Name, out DateTimeOffset timestamp)
            ? timestamp.UtcDateTime
            : file.LastWriteTimeUtc;

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
