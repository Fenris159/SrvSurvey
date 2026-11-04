using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Platform;

public sealed partial class SteamScreenshotSource
{
    private sealed record CaptureLocation(
        DateTimeOffset At,
        string? FrontierId,
        string? CommanderName,
        string? SystemName,
        string? BodyName
    );

    private sealed record CachedJournal(long Length, DateTime Modified, IReadOnlyList<CaptureLocation> Locations);

    private readonly Dictionary<string, CachedJournal> journalCache = new(PathComparer);

    private async Task<IReadOnlyList<CaptureLocation>> ReadLocationsAsync(
        string directory,
        CancellationToken cancellationToken
    )
    {
        var result = new List<CaptureLocation>();
        string[] paths = Directory.GetFiles(directory, "Journal.*.log");
        foreach (string obsolete in journalCache.Keys.Except(paths, PathComparer).ToArray())
        {
            journalCache.Remove(obsolete);
        }
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (
                !journalCache.TryGetValue(path, out CachedJournal? cached)
                || cached.Length != info.Length
                || cached.Modified != info.LastWriteTimeUtc
            )
            {
                IReadOnlyList<CaptureLocation> locations = await ReadJournalLocationsAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                cached = new(info.Length, info.LastWriteTimeUtc, locations);
                journalCache[path] = cached;
            }
            result.AddRange(cached.Locations);
        }
        return result.OrderBy(location => location.At).ToArray();
    }

    private static async Task<IReadOnlyList<CaptureLocation>> ReadJournalLocationsAsync(
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
        var session = new JournalSessionState();
        string? captureBody = null;
        var result = new List<CaptureLocation>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (
                !JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? entry, out _)
                || entry?.Timestamp is not { } at
            )
            {
                continue;
            }
            if (entry.EventName == "Fileheader")
            {
                session = new();
            }
            if (
                entry.EventName
                is not (
                    "Fileheader"
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
            session.Apply(entry);
            captureBody = ResolveCaptureBody(entry, session, captureBody);
            result.Add(
                new(
                    at,
                    session.FrontierId,
                    session.CommanderName,
                    session.IsShutdown || session.IsAtMainMenu ? null : session.SystemName,
                    captureBody
                )
            );
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
            "Fileheader"
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
            "Docked" => CaptureText(entry, "Body") ?? session.BodyName ?? CaptureText(entry, "StationName") ?? previous,
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
