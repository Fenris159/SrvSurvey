using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Diagnostics.Replay;

public sealed record JournalHistoryEvent(
    int Index,
    string FileName,
    DateTimeOffset? Timestamp,
    string EventName,
    string? CommanderName,
    string? SystemName,
    string RawJson);

public sealed record JournalHistorySnapshot(
    string JournalDirectory,
    IReadOnlyList<JournalHistoryEvent> Events,
    int TotalEventCount,
    int FileCount,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp)
{
    public bool IsWindowed => TotalEventCount > Events.Count;
}

public sealed class JournalHistoryReader
{
    public const int DefaultMaximumLoadedEvents = 50_000;

    private readonly int maximumLoadedEvents;

    public JournalHistoryReader(
        int maximumLoadedEvents = DefaultMaximumLoadedEvents)
    {
        if (maximumLoadedEvents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLoadedEvents));
        }

        this.maximumLoadedEvents = maximumLoadedEvents;
    }

    public async Task<JournalHistorySnapshot> LoadAsync(
        string journalDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        var fullDirectory = Path.GetFullPath(journalDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return new JournalHistorySnapshot(
                fullDirectory,
                [],
                0,
                0,
                null,
                null);
        }

        var paths = Directory.EnumerateFiles(
                fullDirectory,
                "Journal.*.log",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        var recent = new Queue<JournalHistoryEvent>(maximumLoadedEvents);
        var totalEventCount = 0;
        DateTimeOffset? firstTimestamp = null;
        DateTimeOffset? lastTimestamp = null;
        await foreach (var historyEvent in StreamPathsAsync(
                           paths,
                           cancellationToken))
        {
            totalEventCount++;
            firstTimestamp ??= historyEvent.Timestamp;
            if (historyEvent.Timestamp is not null)
            {
                lastTimestamp = historyEvent.Timestamp;
            }

            if (recent.Count == maximumLoadedEvents)
            {
                _ = recent.Dequeue();
            }

            recent.Enqueue(historyEvent);
        }

        return new JournalHistorySnapshot(
            fullDirectory,
            recent.ToArray(),
            totalEventCount,
            paths.Length,
            firstTimestamp,
            lastTimestamp);
    }

    public async IAsyncEnumerable<JournalHistoryEvent> StreamAsync(
        string journalDirectory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        var fullDirectory = Path.GetFullPath(journalDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            yield break;
        }

        var paths = Directory.EnumerateFiles(
                fullDirectory,
                "Journal.*.log",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        await foreach (var historyEvent in StreamPathsAsync(
                           paths,
                           cancellationToken))
        {
            yield return historyEvent;
        }
    }

    private static async IAsyncEnumerable<JournalHistoryEvent> StreamPathsAsync(
        IReadOnlyList<string> paths,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var eventIndex = 0;
        foreach (var path in paths)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 64 * 1024,
                leaveOpen: false);
            var boundedReader = new ReplaySessionManager.BoundedJournalLineReader(
                reader);
            var line = await boundedReader.ReadLineAsync(
                ReplaySessionManager.MaximumJournalLineCharacters,
                cancellationToken);
            while (line is not null)
            {
                var nextLine = await boundedReader.ReadLineAsync(
                    ReplaySessionManager.MaximumJournalLineCharacters,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    line = nextLine;
                    continue;
                }

                if (eventIndex >= ReplaySessionManager.MaximumJournalEvents)
                {
                    throw new InvalidDataException(
                        "The journal history contains more events than the supported diagnostic limit.");
                }

                JsonDocument? document = null;
                try
                {
                    document = JsonDocument.Parse(
                        line,
                        new JsonDocumentOptions
                        {
                            AllowTrailingCommas = false,
                            CommentHandling = JsonCommentHandling.Disallow,
                            MaxDepth = 64,
                        });
                }
                catch (JsonException) when (nextLine is null)
                {
                    // The live journal may end with a partially written event.
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        $"Journal line {eventIndex + 1:N0} is not valid JSON.",
                        exception);
                }

                if (document is null)
                {
                    break;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !ReplaySessionManager.TryGetString(
                            root,
                            "event",
                            out var eventName))
                    {
                        throw new InvalidDataException(
                            $"Journal line {eventIndex + 1:N0} does not contain an event name.");
                    }

                    DateTimeOffset? timestamp = null;
                    if (ReplaySessionManager.TryGetString(
                            root,
                            "timestamp",
                            out var timestampText)
                        && DateTimeOffset.TryParse(
                            timestampText,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var parsedTimestamp))
                    {
                        timestamp = parsedTimestamp;
                    }

                    yield return new JournalHistoryEvent(
                        eventIndex,
                        Path.GetFileName(path),
                        timestamp,
                        eventName,
                        GetCommanderName(root, eventName),
                        GetSystemName(root),
                        line);
                }

                eventIndex++;
                line = nextLine;
            }
        }
    }

    private static string? GetCommanderName(
        JsonElement root,
        string eventName)
    {
        var propertyName = string.Equals(
            eventName,
            "Commander",
            StringComparison.Ordinal)
                ? "Name"
                : "Commander";
        return ReplaySessionManager.TryGetString(
            root,
            propertyName,
            out var commander)
                ? commander
                : null;
    }

    private static string? GetSystemName(JsonElement root)
    {
        foreach (var propertyName in new[] { "StarSystem", "SystemName" })
        {
            if (ReplaySessionManager.TryGetString(
                root,
                propertyName,
                out var systemName))
            {
                return systemName;
            }
        }

        return null;
    }
}
