using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>Recovers commander-scoped local body records from at most 100 journal files, like upstream's backfill.</summary>
public static class ColonizationBodyJournalHistoryReader
{
    public static async Task<IReadOnlyList<JournalEventEnvelope>> ReadAsync(
        string journalDirectory,
        long systemAddress,
        string frontierId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(journalDirectory) || string.IsNullOrWhiteSpace(frontierId))
        {
            return [];
        }

        FileInfo[] files = new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Take(100)
            .Reverse()
            .ToArray();
        var result = new List<JournalEventEnvelope>();
        var identityPrefix = new List<JournalEventEnvelope>();
        JournalFilePart? previousPart = null;
        string? previousPath = null;
        foreach (FileInfo file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<JournalEventEnvelope> entries = await ReadFileAsync(file, cancellationToken).ConfigureAwait(false);
            var part = JournalFilePart.Read(file.FullName, entries.Count > 0 ? entries[0] : null);
            bool continuation =
                part?.PreviousPath == previousPath
                && previousPart is not null
                && part?.Number == previousPart.Number + 1;
            IEnumerable<JournalEventEnvelope> attributedEntries = continuation
                ? identityPrefix.Concat(entries.Skip(1))
                : entries;
            result.AddRange(
                JournalCommanderEvents
                    .ForCommander(attributedEntries, frontierId, null)
                    .Where(entry => MatchesBodyEvent(entry, systemAddress))
            );
            identityPrefix = attributedEntries
                .Where(entry => entry.EventName is "Fileheader" or "Commander" or "LoadGame")
                .ToList();
            previousPart = part;
            previousPath = file.FullName;
        }
        return SelectRecentBodiesWithBackfill(result);
    }

    private static List<JournalEventEnvelope> SelectRecentBodiesWithBackfill(List<JournalEventEnvelope> entries)
    {
        int jumpCount = 0;
        int start = 0;
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            if (entries[index].EventName == "FSDJump" && ++jumpCount > 10)
            {
                start = index + 1;
                break;
            }
        }
        var recentIds = entries
            .Skip(start)
            .Where(entry => entry.EventName is "Scan" or "ScanBaryCentre")
            .Select(BodyId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();
        return entries
            .Where(
                (entry, index) =>
                    index >= start
                    || entry.EventName == "FSSAllBodiesFound"
                    || BodyId(entry) is { } id && recentIds.Contains(id)
            )
            .ToList();
    }

    private static int? BodyId(JournalEventEnvelope entry) =>
        entry.Payload.TryGetProperty("BodyID", out JsonElement id)
        && id.ValueKind == JsonValueKind.Number
        && id.TryGetInt32(out int value)
            ? value
            : null;

    private static bool MatchesBodyEvent(JournalEventEnvelope entry, long systemAddress) =>
        entry.EventName == "FSDJump"
        || entry.EventName
            is "Scan"
                or "ScanBaryCentre"
                or "FSSAllBodiesFound"
                or "FSSDiscoveryScan"
                or "FSSBodySignals"
                or "SAASignalsFound"
            && entry.Payload.TryGetProperty("SystemAddress", out JsonElement address)
            && address.ValueKind == JsonValueKind.Number
            && address.TryGetInt64(out long value)
            && value == systemAddress;

    private static async Task<List<JournalEventEnvelope>> ReadFileAsync(
        FileInfo file,
        CancellationToken cancellationToken
    )
    {
        if (file.Length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException("A body-history journal exceeded the 64 MiB reading limit.");
        }
        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var entries = new List<JournalEventEnvelope>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? entry, out _))
            {
                entries.Add(entry!);
            }
        }
        return entries;
    }
}
