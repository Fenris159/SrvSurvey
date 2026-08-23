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
    int FileCount,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp);

public sealed class JournalHistoryReader
{
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
                null,
                null);
        }

        var paths = Directory.EnumerateFiles(
                fullDirectory,
                "Journal.*.log",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        List<JournalHistoryEvent> history = [];
        foreach (var path in paths)
        {
            var fileEvents = await ReplaySessionManager.ReadEventsAsync(
                path,
                cancellationToken,
                requireEvents: false,
                allowIncompleteFinalLine: true);
            foreach (var replayEvent in fileEvents)
            {
                using var document = JsonDocument.Parse(replayEvent.RawJson);
                var root = document.RootElement;
                history.Add(new JournalHistoryEvent(
                    history.Count,
                    Path.GetFileName(path),
                    replayEvent.Timestamp,
                    replayEvent.EventName,
                    GetCommanderName(root, replayEvent.EventName),
                    GetSystemName(root),
                    replayEvent.RawJson));
            }
        }

        return new JournalHistorySnapshot(
            fullDirectory,
            history,
            paths.Length,
            history.FirstOrDefault(item => item.Timestamp is not null)?.Timestamp,
            history.LastOrDefault(item => item.Timestamp is not null)?.Timestamp);
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
