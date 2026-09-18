using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Journal;

/// <summary>
/// Recovers body-local organic sample confirmation from recent journal files when
/// live monitoring missed the original <c>ScanOrganic</c> events (app off, earlier
/// journal rotation, or a pre-patch session).
/// </summary>
public static class OrganicScanJournalBackfill
{
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(12);
    private static readonly TimeSpan CandidateFileWindow = TimeSpan.FromDays(2);

    private static readonly Regex JournalFileNameTimestamp = new(
        @"^Journal\.(?<stamp>\d{4}-\d{2}-\d{2}T\d{6})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250)
    );

    public static async Task<IReadOnlyList<JournalEventEnvelope>> ReadAsync(
        string journalDirectory,
        long systemAddress,
        TimeSpan? lookback = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);

        string fullDirectory = Path.GetFullPath(journalDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return [];
        }

        TimeSpan window = lookback ?? DefaultLookback;
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback));
        }

        string[] candidates = SelectCandidateFiles(fullDirectory);
        if (candidates.Length == 0)
        {
            return [];
        }

        var matches = new List<JournalEventEnvelope>();
        DateTimeOffset? latestJournalTimestamp = null;
        foreach (string path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latestJournalTimestamp = await ReadFileAsync(
                    path,
                    systemAddress,
                    matches,
                    latestJournalTimestamp,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (matches.Count == 0)
        {
            return [];
        }

        // Anchor the 12h window to the newest timestamp inside the journals we
        // read, not wall-clock/real time. If the available journal span is
        // shorter than the lookback, every in-range ScanOrganic is kept.
        DateTimeOffset? anchor = latestJournalTimestamp;
        if (anchor is null)
        {
            foreach (JournalEventEnvelope item in matches)
            {
                if (item.Timestamp is { } timestamp && (anchor is null || timestamp > anchor))
                {
                    anchor = timestamp;
                }
            }
        }

        if (anchor is null)
        {
            return matches.OrderBy(item => item.RawJson, StringComparer.Ordinal).ToArray();
        }

        DateTimeOffset cutoff = anchor.Value.Ticks > window.Ticks ? anchor.Value - window : DateTimeOffset.MinValue;
        return matches
            .Where(item => item.Timestamp is null || item.Timestamp >= cutoff)
            .OrderBy(item => item.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.RawJson, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SelectCandidateFiles(string journalDirectory)
    {
        FileInfo[] files = Directory
            .EnumerateFiles(journalDirectory, "Journal.*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        if (!TryParseJournalFileStamp(files[0].Name, out DateTimeOffset newestStamp))
        {
            return files.Take(3).Reverse().Select(file => file.FullName).ToArray();
        }

        DateTimeOffset oldestStamp = newestStamp - CandidateFileWindow;
        return files
            .Where(file => !TryParseJournalFileStamp(file.Name, out DateTimeOffset stamp) || stamp >= oldestStamp)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .Select(file => file.FullName)
            .ToArray();
    }

    private static async Task<DateTimeOffset?> ReadFileAsync(
        string path,
        long systemAddress,
        List<JournalEventEnvelope> matches,
        DateTimeOffset? latestJournalTimestamp,
        CancellationToken cancellationToken
    )
    {
        // Shared read: Elite must keep write access. Never open journals
        // exclusively or the game can hang/crash while appending.
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (
                !JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? journalEvent, out _)
                || journalEvent is null
            )
            {
                continue;
            }

            if (
                journalEvent.Timestamp is { } timestamp
                && (latestJournalTimestamp is null || timestamp > latestJournalTimestamp)
            )
            {
                latestJournalTimestamp = timestamp;
            }

            if (
                journalEvent.EventName != "ScanOrganic"
                || !TryGetSystemAddress(journalEvent.Payload, out long address)
                || address != systemAddress
            )
            {
                continue;
            }

            matches.Add(journalEvent);
        }

        return latestJournalTimestamp;
    }

    private static bool TryGetSystemAddress(JsonElement root, out long systemAddress)
    {
        systemAddress = 0;
        if (!root.TryGetProperty("SystemAddress", out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out systemAddress))
        {
            return systemAddress > 0;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out systemAddress)
            && systemAddress > 0;
    }

    private static bool TryParseJournalFileStamp(string fileName, out DateTimeOffset stamp)
    {
        stamp = default;
        Match match = JournalFileNameTimestamp.Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        string raw = match.Groups["stamp"].Value;
        return DateTimeOffset.TryParseExact(
            raw,
            "yyyy-MM-dd'T'HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out stamp
        );
    }
}
