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
        string? BodyName,
        CaptureCoverage Coverage
    );

    private sealed class CaptureCoverage
    {
        public DateTimeOffset Through { get; set; } = DateTimeOffset.MinValue;
        public string? LiveJournalPath { get; set; }
        public string? FrontierId { get; set; }
    }

    private sealed record PhotoBody(string Name, bool IsPlanet);

    private sealed record PhotoJournal(IReadOnlyList<JournalEventEnvelope> Events, DateTimeOffset Through);

    private sealed record JournalFile(string Path, long Length, DateTime Modified);

    private sealed record CachedSeries(JournalFile[] Files, IReadOnlyList<CaptureLocation> Locations);

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
        PhotoBody? captureBody = null;
        var coverage = new CaptureCoverage();
        JournalFilePart? previousPart = null;
        foreach (string path in paths)
        {
            PhotoJournal journal = await ReadJournalEventsAsync(path, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<JournalEventEnvelope> entries = journal.Events;
            var part = JournalFilePart.Read(path, entries.FirstOrDefault(entry => entry.EventName == FileheaderEvent));
            bool continuation =
                part is not null
                && previousPart is not null
                && part.Series == previousPart.Series
                && part.Number == previousPart.Number + 1;
            if (!continuation)
            {
                session = new();
                captureBody = null;
                coverage = new();
            }
            coverage.LiveJournalPath = null;
            coverage.Through = journal.Through > coverage.Through ? journal.Through : coverage.Through;
            captureBody = AppendCaptureLocations(
                entries,
                session,
                captureBody,
                coverage,
                result,
                continuation,
                cancellationToken
            );
            if (!session.IsShutdown && !session.IsAtMainMenu)
            {
                coverage.LiveJournalPath = ResolvePhysicalCapture(path);
                coverage.FrontierId = session.FrontierId;
            }
            previousPart = part;
        }
        return result;
    }

    private static PhotoBody? AppendCaptureLocations(
        IReadOnlyList<JournalEventEnvelope> entries,
        JournalSessionState session,
        PhotoBody? captureBody,
        CaptureCoverage coverage,
        List<CaptureLocation> result,
        bool continuation,
        CancellationToken cancellationToken
    )
    {
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
                    captureBody?.Name,
                    coverage
                )
            );
        }
        return captureBody;
    }

    private static async Task<PhotoJournal> ReadJournalEventsAsync(string path, CancellationToken cancellationToken)
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
        DateTimeOffset through = DateTimeOffset.MinValue;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (
                !JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? entry, out _) || entry?.Timestamp is null
            )
            {
                continue;
            }
            if (entry.Timestamp.Value > through)
            {
                through = entry.Timestamp.Value;
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
                    or "Undocked"
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
        return new(result, through);
    }

    private static PhotoBody? ResolveCaptureBody(
        JournalEventEnvelope entry,
        JournalSessionState session,
        PhotoBody? previous
    )
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
            "Location" or "FSDJump" or "CarrierJump" or "SupercruiseExit" => ReadPhotoBody(entry),
            "ApproachBody" => CaptureText(entry, "Body") is { } planet ? new(planet, true) : previous,
            "Docked" => ReadPhotoBody(entry)
                ?? (previous is { IsPlanet: true } ? previous : null)
                ?? (CaptureText(entry, "StationName") is { } station ? new(station, false) : previous),
            "Undocked" => previous is { IsPlanet: true } ? previous : null,
            "Screenshot"
                when string.Equals(
                    CaptureText(entry, "System"),
                    session.SystemName,
                    StringComparison.OrdinalIgnoreCase
                ) => ResolveScreenshotBody(entry, previous),
            _ => previous,
        };
    }

    private static PhotoBody? ResolveScreenshotBody(JournalEventEnvelope entry, PhotoBody? previous) =>
        CaptureText(entry, "Body") is { } body ? new(body, previous?.Name == body && previous.IsPlanet) : previous;

    private static PhotoBody? ReadPhotoBody(JournalEventEnvelope entry) =>
        (CaptureText(entry, "Body") ?? CaptureText(entry, "BodyName")) is { } name
            ? new(name, string.Equals(CaptureText(entry, "BodyType"), "Planet", StringComparison.OrdinalIgnoreCase))
            : null;

    private static string? CaptureText(JournalEventEnvelope entry, string name) =>
        entry.Payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static CaptureLocation? FindLocation(
        IReadOnlyList<CaptureLocation> locations,
        DateTimeOffset at,
        SteamScreenshotLiveSession? liveSession
    )
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
        if (match < 0)
        {
            return null;
        }
        CaptureLocation location = locations[match];
        CaptureCoverage coverage = location.Coverage;
        bool covered =
            at <= coverage.Through
            || (
                liveSession is not null
                && at <= liveSession.Through
                && liveSession.FrontierId == coverage.FrontierId
                && coverage.LiveJournalPath is not null
                && PathComparer.Equals(coverage.LiveJournalPath, ResolvePhysicalCapture(liveSession.JournalPath))
            );
        return covered ? location : null;
    }
}
