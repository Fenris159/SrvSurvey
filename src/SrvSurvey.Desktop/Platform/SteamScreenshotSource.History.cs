using System.Globalization;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Platform;

public sealed partial class SteamScreenshotSource
{
    private const string FileheaderEvent = "Fileheader";

    private sealed record CaptureLocation(
        DateTimeOffset At,
        string? FrontierId,
        string? CommanderName,
        string? SystemName,
        string? BodyName
    );

    private sealed record JournalFile(string Path, long Length, DateTime Modified);

    private sealed record CachedSeries(JournalFile[] Files, IReadOnlyList<CaptureLocation> Locations);

    private sealed record JournalPart(string Series, int Number);

    private readonly Dictionary<string, CachedSeries> historyCache = new(PathComparer);

    private async Task<IReadOnlyList<CaptureLocation>> ReadLocationsAsync(
        string directory,
        CancellationToken cancellationToken
    )
    {
        var result = new List<CaptureLocation>();
        IGrouping<string, string>[] groups = Directory
            .GetFiles(directory, "Journal.*.log")
            .Order(StringComparer.Ordinal)
            .GroupBy(GetJournalSeries)
            .ToArray();
        foreach (string obsolete in historyCache.Keys.Except(groups.Select(group => group.Key), PathComparer).ToArray())
        {
            historyCache.Remove(obsolete);
        }
        foreach (IGrouping<string, string> group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JournalFile[] files = group
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new JournalFile(info.FullName, info.Length, info.LastWriteTimeUtc);
                })
                .ToArray();
            // A later part depends on earlier ownership. Cache the series together,
            // without retaining raw journal payloads or replaying unchanged history.
            if (!historyCache.TryGetValue(group.Key, out CachedSeries? cached) || !cached.Files.SequenceEqual(files))
            {
                IReadOnlyList<CaptureLocation> locations = await ReadSeriesLocationsAsync(group, cancellationToken)
                    .ConfigureAwait(false);
                cached = new(files, locations);
                historyCache[group.Key] = cached;
            }
            result.AddRange(cached.Locations);
        }
        return result.OrderBy(location => location.At).ToArray();
    }

    private static string GetJournalSeries(string path)
    {
        string[] parts = Path.GetFileName(path).Split('.');
        return parts.Length == 4 ? parts[1] : path;
    }

    private static async Task<IReadOnlyList<CaptureLocation>> ReadSeriesLocationsAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken
    )
    {
        var result = new List<CaptureLocation>();
        var session = new JournalSessionState();
        string? captureBody = null;
        JournalPart? previousPart = null;
        foreach (string path in paths)
        {
            IReadOnlyList<JournalEventEnvelope> entries = await ReadJournalEventsAsync(path, cancellationToken)
                .ConfigureAwait(false);
            JournalPart? part = ReadJournalPart(path, entries);
            bool continuation =
                part is not null
                && previousPart is not null
                && part.Series == previousPart.Series
                && part.Number == previousPart.Number + 1;
            if (!continuation)
            {
                session = new();
                captureBody = null;
            }
            foreach (JournalEventEnvelope entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A continuation header describes the same running game session.
                // Re-applying it would put that session back into the main menu.
                if (entry.EventName == FileheaderEvent && continuation)
                {
                    continue;
                }
                session.Apply(entry);
                captureBody = ResolveCaptureBody(entry, session, captureBody);
                result.Add(
                    new(
                        entry.Timestamp!.Value,
                        session.FrontierId,
                        session.CommanderName,
                        session.IsShutdown || session.IsAtMainMenu ? null : session.SystemName,
                        captureBody
                    )
                );
            }
            previousPart = part;
        }
        return result;
    }

    private static JournalPart? ReadJournalPart(string path, IReadOnlyList<JournalEventEnvelope> entries)
    {
        string[] parts = Path.GetFileName(path).Split('.');
        if (
            parts.Length != 4
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            || number < 1
        )
        {
            return null;
        }
        JournalEventEnvelope? header = entries.FirstOrDefault(entry => entry.EventName == FileheaderEvent);
        if (header is null)
        {
            return null;
        }
        if (
            (
                header.Payload.TryGetProperty("part", out JsonElement declared)
                || header.Payload.TryGetProperty("Part", out declared)
            ) && (declared.ValueKind != JsonValueKind.Number || !declared.TryGetInt32(out int part) || part != number)
        )
        {
            return null;
        }
        return new(parts[1], number);
    }

    private static async Task<IReadOnlyList<JournalEventEnvelope>> ReadJournalEventsAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            65536,
            useAsync: true
        );
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = new List<JournalEventEnvelope>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (
                !JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? entry, out _) || entry?.Timestamp is null
            )
            {
                continue;
            }
            if (
                entry.EventName
                is not (
                    FileheaderEvent
                    or "Commander"
                    or "LoadGame"
                    or "Location"
                    or "FSDJump"
                    or "CarrierJump"
                    or "SupercruiseExit"
                    or "ApproachBody"
                    or "Docked"
                    or "StartJump"
                    or "Music"
                    or "Shutdown"
                    or "Died"
                    or "Resurrect"
                    or "Screenshot"
                    or "LeaveBody"
                    or "SupercruiseEntry"
                )
            )
            {
                continue;
            }
            result.Add(entry);
        }
        return result;
    }

    private static string? ResolveCaptureBody(JournalEventEnvelope entry, JournalSessionState session, string? previous)
    {
        if (session.IsShutdown || session.IsAtMainMenu)
        {
            return null;
        }
        // Navigation's BodyName is deliberately planet-only. A photo's body
        // also includes stations, stars, and other explicitly named locations.
        return entry.EventName switch
        {
            FileheaderEvent
            or "Commander"
            or "LoadGame"
            or "StartJump"
            or "LeaveBody"
            or "SupercruiseEntry"
            or "Died"
            or "Resurrect" => null,
            "Location" or "FSDJump" or "CarrierJump" or "SupercruiseExit" => CaptureText(entry, "Body")
                ?? CaptureText(entry, "BodyName"),
            "ApproachBody" => CaptureText(entry, "Body") ?? previous,
            "Docked" => CaptureText(entry, "Body") ?? previous ?? CaptureText(entry, "StationName"),
            "Screenshot"
                when string.Equals(
                    CaptureText(entry, "System"),
                    session.SystemName,
                    StringComparison.OrdinalIgnoreCase
                ) => CaptureText(entry, "Body") ?? previous,
            _ => previous,
        };
    }

    private static string? CaptureText(JournalEventEnvelope entry, string name) =>
        entry.Payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static CaptureLocation? FindLocation(IReadOnlyList<CaptureLocation> locations, DateTimeOffset at)
    {
        int low = 0;
        int high = locations.Count - 1;
        int match = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (locations[middle].At <= at)
            {
                match = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return match < 0 ? null : locations[match];
    }
}
