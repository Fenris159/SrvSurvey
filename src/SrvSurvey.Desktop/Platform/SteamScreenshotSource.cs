using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Finds Elite's Steam captures and attributes them using the journal session at capture time.</summary>
public sealed partial class SteamScreenshotSource(
    IEnumerable<string>? steamRoots = null,
    TimeZoneInfo? localTimeZone = null
)
{
    private const string EliteAppId = "359320";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal async Task<SteamScreenshotBatch> ReadAsync(
        string folder,
        string journalDirectory,
        string frontierId,
        CancellationToken cancellationToken
    )
    {
        var warnings = new List<string>();
        if (!Directory.Exists(folder) || !Directory.Exists(journalDirectory))
        {
            return new([], []);
        }
        if (
            !new DirectoryInfo(folder).Name.Equals("screenshots", StringComparison.OrdinalIgnoreCase)
            || new DirectoryInfo(folder).Parent?.Name != EliteAppId
        )
        {
            return new(
                [],
                ["Choose Elite's Steam 760/remote/359320/screenshots folder; other games are not imported."]
            );
        }
        folder = ResolvePhysicalFolder(folder);
        Dictionary<string, DateTimeOffset> captures = ReadCaptureTimes(folder, warnings);
        if (captures.Count == 0)
        {
            return new([], warnings);
        }
        IReadOnlyList<CaptureLocation> locations = await ReadLocationsAsync(journalDirectory, cancellationToken)
            .ConfigureAwait(false);
        var matched = new List<SteamScreenshotCapture>();
        foreach (
            (string path, DateTimeOffset timestamp) in captures
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => pair.Key, PathComparer)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureLocation? location = FindLocation(locations, timestamp);
            if (
                location is null
                || location.FrontierId != frontierId
                || location.SystemName is null
                || location.CommanderName is null
            )
            {
                continue;
            }
            var payload = new Dictionary<string, object?>
            {
                ["event"] = "Screenshot",
                ["timestamp"] = timestamp,
                ["Filename"] = path,
                ["System"] = location.SystemName,
                ["SteamScreenshot"] = true,
            };
            if (location.BodyName is not null)
            {
                payload["Body"] = location.BodyName;
            }
            string json = JsonSerializer.Serialize(payload);
            if (JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _) && entry is not null)
            {
                matched.Add(new(entry, location.CommanderName));
            }
        }
        return new(matched, warnings);
    }

    private Dictionary<string, DateTimeOffset> ReadCaptureTimes(string folder, List<string> warnings)
    {
        var captures = new Dictionary<string, DateTimeOffset>(PathComparer);
        ReadIndexedCaptureTimes(folder, warnings, captures);
        foreach (string path in Directory.EnumerateFiles(folder).Where(IsJpeg))
        {
            if (!captures.ContainsKey(path) && new FileInfo(path).LinkTarget is null && TryFilenameTime(path) is { } at)
            {
                captures.TryAdd(path, at);
            }
        }
        return captures;
    }

    private static void ReadIndexedCaptureTimes(
        string folder,
        List<string> warnings,
        Dictionary<string, DateTimeOffset> captures
    )
    {
        DirectoryInfo? remote = new DirectoryInfo(folder).Parent?.Parent;
        string? index = remote?.Parent is { } parent ? Path.Combine(parent.FullName, "screenshots.vdf") : null;
        try
        {
            if (index is not null && File.Exists(index))
            {
                SteamKeyValues? game = SteamKeyValues
                    .Read(index)
                    .Children.GetValueOrDefault("screenshots")
                    ?.Children.GetValueOrDefault(EliteAppId);
                foreach (SteamKeyValues item in game?.Children.Values.AsEnumerable() ?? [])
                {
                    AddIndexedCapture(item, remote!.FullName, folder, captures);
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            warnings.Add(
                "The Steam screenshot index could not be read; capture filenames will be used where possible: "
                    + exception.Message
            );
        }
    }

    private static void AddIndexedCapture(
        SteamKeyValues item,
        string remote,
        string folder,
        Dictionary<string, DateTimeOffset> captures
    )
    {
        string? filename = item.GetValue("filename");
        if (
            filename is null
            || !long.TryParse(item.GetValue("creation"), CultureInfo.InvariantCulture, out long seconds)
        )
        {
            return;
        }
        string path = Path.GetFullPath(Path.Combine(remote, filename.Replace('\\', Path.DirectorySeparatorChar)));
        if (
            !PathComparer.Equals(Path.GetDirectoryName(path), Path.GetFullPath(folder))
            || !IsJpeg(path)
            || !File.Exists(path)
            || new FileInfo(path).LinkTarget is not null
        )
        {
            return;
        }
        try
        {
            captures.TryAdd(path, DateTimeOffset.FromUnixTimeSeconds(seconds));
        }
        catch (ArgumentOutOfRangeException)
        {
            // Leave captures with unusable metadata untouched unless their filename supplies a valid time.
        }
    }

    private DateTimeOffset? TryFilenameTime(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (
            name.Length <= 15
            || name[14] != '_'
            || !DateTime.TryParseExact(
                name[..14],
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime local
            )
        )
        {
            return null;
        }
        TimeZoneInfo zone = localTimeZone ?? TimeZoneInfo.Local;
        return zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)
            ? null
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    private static string ResolvePhysicalFolder(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full)!;
        string resolved = root;
        foreach (
            string component in full[root.Length..]
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var part = new DirectoryInfo(Path.Combine(resolved, component));
            resolved = part.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? part.FullName;
        }
        return resolved;
    }

    private static bool IsJpeg(string path) =>
        Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
}

internal sealed record SteamScreenshotCapture(JournalEventEnvelope Entry, string CommanderName);

internal sealed record SteamScreenshotBatch(
    IReadOnlyList<SteamScreenshotCapture> Captures,
    IReadOnlyList<string> Warnings
);
