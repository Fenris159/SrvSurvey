using System.Text;
using System.Text.Json;
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        EnsureDirectoryExists();

        var errors = new List<string>();
        foreach (var file in EnumerateJournalFiles(descending: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await ReadFileAsync(file, cancellationToken)
                .ConfigureAwait(false);
            errors.AddRange(read.Errors);
            if (!MatchesCommander(read, frontierId, isOdyssey))
            {
                continue;
            }

            for (var index = read.Events.Count - 1; index >= 0; index--)
            {
                var journalEvent = read.Events[index];
                if (journalEvent.EventName != "FSDJump"
                    || GetInt64(journalEvent.Payload, "SystemAddress")
                        != systemAddress
                    || !TryGetSystemReference(
                        journalEvent.Payload,
                        out var systemReference))
                {
                    continue;
                }

                return new JourneyJournalSystemSearchResult(
                    new JourneyJournalSystemEntry(
                        file.Name,
                        journalEvent,
                        systemReference),
                    errors);
            }
        }

        return new JourneyJournalSystemSearchResult(null, errors);
    }

    public async Task<JourneyJournalReadResult> ReadFromAsync(
        string startingJournal,
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default)
    {
        ValidateJournalFileName(startingJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        EnsureDirectoryExists();

        var files = EnumerateJournalFiles(descending: false).ToArray();
        var startIndex = Array.FindIndex(
            files,
            file => string.Equals(
                file.Name,
                startingJournal,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal));
        if (startIndex < 0)
        {
            throw new FileNotFoundException(
                $"The starting journal was not found: {startingJournal}",
                Path.Combine(journalDirectory, startingJournal));
        }

        var events = new List<JournalEventEnvelope>();
        var errors = new List<string>();
        for (var index = startIndex; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var read = await ReadFileAsync(file, cancellationToken)
                .ConfigureAwait(false);
            errors.AddRange(read.Errors);
            if (MatchesCommander(read, frontierId, isOdyssey))
            {
                events.AddRange(read.Events);
            }
        }

        return new JourneyJournalReadResult(events, errors);
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(journalDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The journal folder does not exist: {journalDirectory}");
        }
    }

    private IEnumerable<FileInfo> EnumerateJournalFiles(bool descending)
    {
        var files = new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly);
        return descending
            ? files
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            : files
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal);
    }

    private static async Task<JournalFileReadResult> ReadFileAsync(
        FileInfo file,
        CancellationToken cancellationToken)
    {
        var events = new List<JournalEventEnvelope>();
        var errors = new List<string>();
        string? frontierId = null;
        var isOdyssey = true;

        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
               is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!JournalEventEnvelope.TryParse(
                    line,
                    out var journalEvent,
                    out var error)
                || journalEvent is null)
            {
                errors.Add(
                    $"{file.Name}, line {lineNumber}: "
                        + (error ?? "The journal entry could not be parsed."));
                continue;
            }

            events.Add(journalEvent);
            if (journalEvent.EventName == "Fileheader")
            {
                isOdyssey = GetBoolean(journalEvent.Payload, "Odyssey") ?? true;
            }
            else if (journalEvent.EventName is "Commander" or "LoadGame")
            {
                frontierId = GetString(journalEvent.Payload, "FID") ?? frontierId;
            }
        }

        return new JournalFileReadResult(
            file.Name,
            frontierId,
            isOdyssey,
            events,
            errors);
    }

    private static bool MatchesCommander(
        JournalFileReadResult read,
        string frontierId,
        bool isOdyssey)
    {
        return read.IsOdyssey == isOdyssey
            && string.Equals(
                read.FrontierId,
                frontierId,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateJournalFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!string.Equals(
                fileName,
                Path.GetFileName(fileName),
                StringComparison.Ordinal)
            || !fileName.StartsWith("Journal.", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The starting journal must be a Journal.*.log file name.",
                nameof(fileName));
        }
    }

    internal static bool TryGetSystemReference(
        JsonElement root,
        out JourneySystemReference systemReference)
    {
        systemReference = null!;
        var name = GetString(root, "StarSystem");
        var address = GetInt64(root, "SystemAddress");
        if (string.IsNullOrWhiteSpace(name)
            || address is null
            || !root.TryGetProperty("StarPos", out var position)
            || position.ValueKind != JsonValueKind.Array
            || position.GetArrayLength() != 3)
        {
            return false;
        }

        var coordinates = position.EnumerateArray().ToArray();
        if (!coordinates[0].TryGetDouble(out var x)
            || !coordinates[1].TryGetDouble(out var y)
            || !coordinates[2].TryGetDouble(out var z))
        {
            return false;
        }

        try
        {
            systemReference = new JourneySystemReference(
                name,
                address.Value,
                new GalacticCoordinate(x, y, z));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private sealed record JournalFileReadResult(
        string FileName,
        string? FrontierId,
        bool IsOdyssey,
        IReadOnlyList<JournalEventEnvelope> Events,
        IReadOnlyList<string> Errors);
}

public sealed record JourneyJournalSystemEntry(
    string JournalFileName,
    JournalEventEnvelope Event,
    JourneySystemReference System);

public sealed record JourneyJournalSystemSearchResult(
    JourneyJournalSystemEntry? Entry,
    IReadOnlyList<string> Errors);

public sealed record JourneyJournalReadResult(
    IReadOnlyList<JournalEventEnvelope> Events,
    IReadOnlyList<string> Errors);
