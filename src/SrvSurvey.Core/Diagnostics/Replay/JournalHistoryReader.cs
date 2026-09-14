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
    string RawJson
);

public sealed record JournalHistorySnapshot(
    string JournalDirectory,
    IReadOnlyList<JournalHistoryEvent> Events,
    int TotalEventCount,
    int FileCount,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp
)
{
    public bool IsWindowed => TotalEventCount > Events.Count;
}

public sealed class JournalHistoryReader
{
    public const int DefaultMaximumLoadedEvents = 50_000;
    private static readonly string[] SystemNamePropertyNames = ["StarSystem", "SystemName"];

    private readonly int maximumLoadedEvents;

    public JournalHistoryReader(int maximumLoadedEvents = DefaultMaximumLoadedEvents)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLoadedEvents);

        this.maximumLoadedEvents = maximumLoadedEvents;
    }

    public async Task<JournalHistorySnapshot> LoadAsync(string journalDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        string fullDirectory = Path.GetFullPath(journalDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return new JournalHistorySnapshot(fullDirectory, [], 0, 0, null, null);
        }

        string[] paths = Directory
            .EnumerateFiles(fullDirectory, "Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        var recent = new Queue<JournalHistoryEvent>(maximumLoadedEvents);
        int totalEventCount = 0;
        DateTimeOffset? firstTimestamp = null;
        DateTimeOffset? lastTimestamp = null;
        await foreach (JournalHistoryEvent historyEvent in StreamPathsAsync(paths, cancellationToken))
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
            lastTimestamp
        );
    }

    public static async IAsyncEnumerable<JournalHistoryEvent> StreamAsync(
        string journalDirectory,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        string fullDirectory = Path.GetFullPath(journalDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            yield break;
        }

        string[] paths = Directory
            .EnumerateFiles(fullDirectory, "Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        await foreach (JournalHistoryEvent historyEvent in StreamPathsAsync(paths, cancellationToken))
        {
            yield return historyEvent;
        }
    }

    private static async IAsyncEnumerable<JournalHistoryEvent> StreamPathsAsync(
        IReadOnlyList<string> paths,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        int eventIndex = 0;
        foreach (string path in paths)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true
            );
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 64 * 1024,
                leaveOpen: false
            );
            var boundedReader = new ReplaySessionManager.BoundedJournalLineReader(reader);
            string? line = await boundedReader.ReadLineAsync(
                ReplaySessionManager.MaximumJournalLineCharacters,
                cancellationToken
            );
            while (line is not null)
            {
                string? nextLine = await boundedReader.ReadLineAsync(
                    ReplaySessionManager.MaximumJournalLineCharacters,
                    cancellationToken
                );
                if (string.IsNullOrWhiteSpace(line))
                {
                    line = nextLine;
                    continue;
                }

                if (eventIndex >= ReplaySessionManager.MaximumJournalEvents)
                {
                    throw new InvalidDataException(
                        "The journal history contains more events than the supported diagnostic limit."
                    );
                }

                JournalHistoryEvent? historyEvent = ParseHistoryEvent(
                    line,
                    path,
                    eventIndex,
                    allowIncomplete: nextLine is null
                );
                if (historyEvent is null)
                {
                    break;
                }

                yield return historyEvent;

                eventIndex++;
                line = nextLine;
            }
        }
    }

    private static JournalHistoryEvent? ParseHistoryEvent(
        string line,
        string path,
        int eventIndex,
        bool allowIncomplete
    )
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                line,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                }
            );
        }
        catch (JsonException) when (allowIncomplete)
        {
            // The live journal may end with a partially written event.
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Journal line {eventIndex + 1:N0} is not valid JSON.", exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !ReplaySessionManager.TryGetString(root, "event", out string? eventName)
            )
            {
                throw new InvalidDataException($"Journal line {eventIndex + 1:N0} does not contain an event name.");
            }

            return new JournalHistoryEvent(
                eventIndex,
                Path.GetFileName(path),
                GetTimestamp(root),
                eventName,
                GetCommanderName(root, eventName),
                GetSystemName(root),
                line
            );
        }
    }

    private static DateTimeOffset? GetTimestamp(JsonElement root)
    {
        return
            ReplaySessionManager.TryGetString(root, "timestamp", out string? timestampText)
            && DateTimeOffset.TryParse(
                timestampText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset timestamp
            )
            ? timestamp
            : null;
    }

    private static string? GetCommanderName(JsonElement root, string eventName)
    {
        string propertyName = string.Equals(eventName, "Commander", StringComparison.Ordinal) ? "Name" : "Commander";
        return ReplaySessionManager.TryGetString(root, propertyName, out string? commander) ? commander : null;
    }

    private static string? GetSystemName(JsonElement root)
    {
        foreach (string propertyName in SystemNamePropertyNames)
        {
            if (ReplaySessionManager.TryGetString(root, propertyName, out string? systemName))
            {
                return systemName;
            }
        }

        return null;
    }
}
