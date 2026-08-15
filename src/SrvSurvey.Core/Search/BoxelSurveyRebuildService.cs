using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Search;

public sealed class BoxelSurveyRebuildService
{
    private readonly string dataDirectory;
    private readonly string journalDirectory;

    public BoxelSurveyRebuildService(string dataDirectory, string journalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.journalDirectory = Path.GetFullPath(journalDirectory);
    }

    public async Task<BoxelSurveyRebuildResult> RebuildAsync(
        string frontierId,
        BoxelSurveyStatsState state,
        string? currentJournalPath = null,
        IProgress<BoxelSurveyRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentNullException.ThrowIfNull(state);

        var warnings = new List<string>();
        var systemFiles = await IngestSystemFilesAsync(
                frontierId,
                state,
                progress,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);
        var journals = await ReplayJournalsAsync(
                frontierId,
                state,
                currentJournalPath,
                progress,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);
        return new BoxelSurveyRebuildResult(
            systemFiles,
            journals.Processed,
            journals.Skipped,
            journals.Malformed,
            warnings);
    }

    private async Task<int> IngestSystemFilesAsync(
        string frontierId,
        BoxelSurveyStatsState state,
        IProgress<BoxelSurveyRebuildProgress>? progress,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        BoxelSurveyStatsStore.ValidateFileName(frontierId, nameof(frontierId));
        var directory = Path.Combine(dataDirectory, "systems", frontierId);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        var ingested = 0;
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];
            progress?.Report(new BoxelSurveyRebuildProgress(
                "System files",
                index + 1,
                files.Length,
                Path.GetFileName(path)));
            try
            {
                var root = await ReadObjectAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                var snapshot = LegacySystemSnapshotParser.Parse(root);
                var lastVisited = GetDateTimeOffset(root, "lastVisited");
                if (state.IngestSystemFile(snapshot, lastVisited))
                {
                    ingested++;
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException)
            {
                warnings.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        return ingested;
    }

    private async Task<(int Processed, int Skipped, int Malformed)> ReplayJournalsAsync(
        string frontierId,
        BoxelSurveyStatsState state,
        string? currentJournalPath,
        IProgress<BoxelSurveyRebuildProgress>? progress,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(journalDirectory))
        {
            return (0, 0, 0);
        }

        var currentFullPath = string.IsNullOrWhiteSpace(currentJournalPath)
            ? null
            : Path.GetFullPath(currentJournalPath);
        var files = Directory.GetFiles(
                journalDirectory,
                "Journal.*.log",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var processed = 0;
        var skipped = 0;
        var malformed = 0;
        var scan = new SystemScanState();
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];
            progress?.Report(new BoxelSurveyRebuildProgress(
                "Journals",
                index + 1,
                files.Length,
                Path.GetFileName(path)));
            if (currentFullPath is not null
                && string.Equals(
                    Path.GetFullPath(path),
                    currentFullPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            JournalFileReplay replay;
            try
            {
                replay = await ReplayJournalAsync(
                        path,
                        frontierId,
                        scan,
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{Path.GetFileName(path)}: {exception.Message}");
                skipped++;
                continue;
            }

            malformed += replay.MalformedLineCount;
            if (!replay.MatchesCommander)
            {
                skipped++;
                continue;
            }

            processed++;
        }

        IngestCurrent(scan, state);
        return (processed, skipped, malformed);
    }

    private static void ReplayEvent(
        JournalEventEnvelope journalEvent,
        SystemScanState scan,
        BoxelSurveyStatsState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsSystemChange(journalEvent))
        {
            IngestCurrent(scan, state);
        }

        scan.Apply(journalEvent);
        state.Apply(journalEvent);
    }

    private static void IngestCurrent(SystemScanState scan, BoxelSurveyStatsState state)
    {
        var snapshot = scan.CreateSnapshot();
        if (snapshot.SystemAddress is > 0)
        {
            state.IngestSnapshot(snapshot);
        }
    }

    private static bool IsSystemChange(JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName is not ("FSDJump" or "Location" or "CarrierJump"))
        {
            return false;
        }

        return journalEvent.Payload.TryGetProperty("SystemAddress", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var address)
            && address > 0;
    }

    private static async Task<JournalFileReplay> ReplayJournalAsync(
        string path,
        string frontierId,
        SystemScanState scan,
        BoxelSurveyStatsState state,
        CancellationToken cancellationToken)
    {
        var replay = new JournalReplayContext(frontierId);
        var malformed = 0;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
               is { } line)
        {
            var journalEvent = ParseJournalLine(line, ref malformed);
            if (journalEvent is null)
            {
                continue;
            }

            ReplayJournalEvent(
                journalEvent,
                scan,
                state,
                replay,
                cancellationToken);
        }

        return new JournalFileReplay(replay.MatchesCommander, malformed);
    }

    private static JournalEventEnvelope? ParseJournalLine(string line, ref int malformed)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        if (JournalEventEnvelope.TryParse(line, out var journalEvent, out _)
            && journalEvent is not null)
        {
            return journalEvent;
        }

        malformed++;
        return null;
    }

    private static void ReplayJournalEvent(
        JournalEventEnvelope journalEvent,
        SystemScanState scan,
        BoxelSurveyStatsState state,
        JournalReplayContext replay,
        CancellationToken cancellationToken)
    {
        if (journalEvent.EventName == "Fileheader" && !replay.MatchesCommander)
        {
            replay.Context.Clear();
            replay.Context.Add(journalEvent);
        }

        if (journalEvent.EventName is "Commander" or "LoadGame"
            && GetString(journalEvent.Payload, "FID") is { } eventFrontierId)
        {
            replay.IncludeEvents = string.Equals(
                eventFrontierId,
                replay.FrontierId,
                StringComparison.OrdinalIgnoreCase);
            if (replay.IncludeEvents && !replay.MatchesCommander)
            {
                foreach (var contextEvent in replay.Context)
                {
                    ReplayEvent(contextEvent, scan, state, cancellationToken);
                }

                replay.Context.Clear();
                replay.MatchesCommander = true;
            }
        }

        if (replay.IncludeEvents)
        {
            ReplayEvent(journalEvent, scan, state, cancellationToken);
        }
    }

    private static async Task<JsonObject> ReadObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException("The system file did not contain a JSON object.");
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<DateTimeOffset>(out var stamp))
        {
            return stamp;
        }

        return value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : null;
    }

    private static string? GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private sealed record JournalFileReplay(
        bool MatchesCommander,
        int MalformedLineCount);

    private sealed class JournalReplayContext(string frontierId)
    {
        public string FrontierId { get; } = frontierId;

        public List<JournalEventEnvelope> Context { get; } = [];

        public bool MatchesCommander { get; set; }

        public bool IncludeEvents { get; set; }
    }
}

public sealed record BoxelSurveyRebuildProgress(
    string Stage,
    int Processed,
    int Total,
    string? CurrentFile);

public sealed record BoxelSurveyRebuildResult(
    int SystemFilesIngested,
    int JournalFilesProcessed,
    int JournalFilesSkipped,
    int MalformedLines,
    IReadOnlyList<string> Warnings);
