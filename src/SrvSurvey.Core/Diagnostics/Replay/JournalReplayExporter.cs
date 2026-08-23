using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Diagnostics.Replay;

public enum ReplayPrivacyMode
{
    Raw,
    Redacted,
}

public sealed record JournalReplayExportRequest(
    DateTimeOffset? From,
    DateTimeOffset? To,
    ReplayPrivacyMode PrivacyMode,
    string SourceVersion);

public sealed record JournalReplayExportResult(
    string Path,
    int EventCount,
    int BootstrapEventCount,
    ReplayCommander Commander,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp);

public sealed class JournalReplayExporter
{
    public const int CurrentPackageFormatVersion = 1;

    private static readonly JsonSerializerOptions PackageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<JournalReplayExportResult> ExportAsync(
        string journalDirectory,
        string destinationPath,
        JournalReplayExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(request);
        if (request.From is { } from
            && request.To is { } to
            && from > to)
        {
            throw new ArgumentException(
                "The replay export start must not be after its end.",
                nameof(request));
        }

        var history = await new JournalHistoryReader().LoadAsync(
            journalDirectory,
            cancellationToken);
        var events = history.Events.Select(item => new JournalReplayEvent(
            item.Index,
            item.Timestamp,
            item.EventName,
            item.RawJson)).ToArray();
        var selected = events
            .Where(replayEvent => IsWithinRange(replayEvent, request))
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidDataException(
                "No journal events exist in the selected replay range.");
        }

        var firstSelectedIndex = selected[0].Index;
        var bootstrap = SelectBootstrap(events, firstSelectedIndex);
        var included = bootstrap
            .Concat(selected)
            .DistinctBy(replayEvent => replayEvent.Index)
            .OrderBy(replayEvent => replayEvent.Index)
            .ToArray();
        var commander = ReplaySessionManager.ResolveCommander(included);
        var sanitized = included.Select(replayEvent => replayEvent with
        {
            RawJson = RemoveCredentials(replayEvent.RawJson),
        }).ToArray();
        var outputLines = request.PrivacyMode == ReplayPrivacyMode.Redacted
            ? Redact(sanitized, commander)
            : sanitized.Select(replayEvent => replayEvent.RawJson).ToArray();
        var journalBytes = Encoding.UTF8.GetBytes(
            string.Join('\n', outputLines) + '\n');
        var checksum = Convert.ToHexStringLower(SHA256.HashData(journalBytes));
        var package = new JournalReplayPackageManifest(
            CurrentPackageFormatVersion,
            DateTimeOffset.UtcNow,
            request.SourceVersion.Trim(),
            request.From,
            request.To,
            request.PrivacyMode,
            included.Length,
            bootstrap.Length,
            included.FirstOrDefault()?.Timestamp,
            included.LastOrDefault()?.Timestamp,
            request.PrivacyMode == ReplayPrivacyMode.Redacted
                ? new ReplayCommander("Replay Commander", "F000000")
                : commander,
            checksum,
            ["status", "cargo", "shipLocker", "navRoute", "market"]);

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullDestinationPath)
                ?? throw new InvalidDataException(
                    "The replay export destination has no containing directory."));
        await using var output = new FileStream(
            fullDestinationPath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        using (var archive = new ZipArchive(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry(
                "replay-package.json",
                CompressionLevel.Optimal);
            await using (var manifestStream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(
                    manifestStream,
                    package,
                    PackageJson,
                    cancellationToken);
            }

            var journalEntry = archive.CreateEntry(
                "journal.jsonl",
                CompressionLevel.Optimal);
            await using var journalStream = journalEntry.Open();
            await journalStream.WriteAsync(journalBytes, cancellationToken);
        }

        return new JournalReplayExportResult(
            fullDestinationPath,
            included.Length,
            bootstrap.Length,
            package.Commander,
            package.FirstTimestamp,
            package.LastTimestamp);
    }

    internal static JsonSerializerOptions GetPackageJsonOptions() => PackageJson;

    private static bool IsWithinRange(
        JournalReplayEvent replayEvent,
        JournalReplayExportRequest request)
    {
        if (request.From is null && request.To is null)
        {
            return true;
        }

        return replayEvent.Timestamp is { } timestamp
            && (request.From is null || timestamp >= request.From)
            && (request.To is null || timestamp <= request.To);
    }

    private static JournalReplayEvent[] SelectBootstrap(
        IReadOnlyList<JournalReplayEvent> events,
        int firstSelectedIndex)
    {
        var prior = events
            .Where(replayEvent => replayEvent.Index < firstSelectedIndex)
            .ToArray();
        var selected = new List<JournalReplayEvent>();
        AddLatest(selected, prior, "Fileheader");
        AddLatest(selected, prior, "Commander");
        AddLatest(selected, prior, "LoadGame");
        var location = prior.LastOrDefault(replayEvent =>
            replayEvent.EventName is "Location" or "FSDJump" or "CarrierJump");
        if (location is not null)
        {
            selected.Add(location);
        }

        return selected
            .DistinctBy(replayEvent => replayEvent.Index)
            .OrderBy(replayEvent => replayEvent.Index)
            .ToArray();
    }

    private static void AddLatest(
        ICollection<JournalReplayEvent> selected,
        IReadOnlyList<JournalReplayEvent> events,
        string eventName)
    {
        var match = events.LastOrDefault(replayEvent => string.Equals(
            replayEvent.EventName,
            eventName,
            StringComparison.Ordinal));
        if (match is not null)
        {
            selected.Add(match);
        }
    }

    private static string[] Redact(
        IReadOnlyList<JournalReplayEvent> events,
        ReplayCommander commander)
    {
        return events.Select(replayEvent =>
        {
            var redacted = replayEvent.RawJson
                .Replace(commander.Name, "Replay Commander", StringComparison.Ordinal)
                .Replace(commander.FrontierId, "F000000", StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(redacted);
            if (!string.Equals(
                    replayEvent.EventName,
                    "ReceiveText",
                    StringComparison.Ordinal))
            {
                return redacted;
            }

            var properties = document.RootElement
                .EnumerateObject()
                .Where(property => property.Name is not "Message" and not "Message_Localised")
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.Clone(),
                    StringComparer.Ordinal);
            return JsonSerializer.Serialize(properties);
        }).ToArray();
    }

    private static string RemoveCredentials(string rawJson)
    {
        var root = JsonNode.Parse(rawJson)
            ?? throw new InvalidDataException(
                "A journal event could not be read while removing credentials.");
        RemoveCredentialProperties(root);
        return root.ToJsonString();
    }

    private static void RemoveCredentialProperties(JsonNode node)
    {
        if (node is JsonObject objectNode)
        {
            foreach (var propertyName in objectNode
                         .Select(property => property.Key)
                         .ToArray())
            {
                if (IsCredentialProperty(propertyName))
                {
                    _ = objectNode.Remove(propertyName);
                }
                else if (objectNode[propertyName] is { } child)
                {
                    RemoveCredentialProperties(child);
                }
            }

            return;
        }

        if (node is JsonArray arrayNode)
        {
            foreach (var child in arrayNode)
            {
                if (child is not null)
                {
                    RemoveCredentialProperties(child);
                }
            }
        }
    }

    private static bool IsCredentialProperty(string propertyName)
    {
        var normalized = new string(propertyName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal);
    }
}

public sealed record JournalReplayPackageManifest(
    int FormatVersion,
    DateTimeOffset CreatedAt,
    string SourceVersion,
    DateTimeOffset? RequestedFrom,
    DateTimeOffset? RequestedTo,
    ReplayPrivacyMode PrivacyMode,
    int EventCount,
    int BootstrapEventCount,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp,
    ReplayCommander Commander,
    string JournalSha256,
    IReadOnlyList<string> SupportedCompanionTimelines);
