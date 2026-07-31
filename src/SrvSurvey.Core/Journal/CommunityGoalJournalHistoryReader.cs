using System.Globalization;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Core.Journal;

public interface ICommunityGoalJournalHistoryReader
{
    Task<CommunityGoalJournalHistoryReadResult> ReadAsync(
        string frontierId,
        CancellationToken cancellationToken = default);
}

public sealed record CommunityGoalJournalHistoryReadResult(
    IReadOnlyList<FrontierCommunityGoalSnapshot> Goals,
    string Warning);

public sealed class CommunityGoalJournalHistoryReader(
    string journalDirectory) : ICommunityGoalJournalHistoryReader
{
    private const int MaximumHistoryGoals = 250;
    private readonly string journalDirectory = Path.GetFullPath(
        string.IsNullOrWhiteSpace(journalDirectory)
            ? throw new ArgumentException(
                "A journal directory is required.",
                nameof(journalDirectory))
            : journalDirectory);

    public async Task<CommunityGoalJournalHistoryReadResult> ReadAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (!Directory.Exists(journalDirectory))
        {
            return new CommunityGoalJournalHistoryReadResult(
                [],
                $"Local Community Goal history was not found: {journalDirectory}");
        }

        var normalizedFrontierId = NormalizeFrontierId(frontierId);
        var latest = new Dictionary<string, HistoryGoal>(StringComparer.Ordinal);
        var malformedEntries = 0;
        var unreadableFiles = 0;
        foreach (var file in new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderBy(item => item.LastWriteTimeUtc)
            .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                malformedEntries += await ReadFileAsync(
                        file,
                        normalizedFrontierId,
                        latest,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                unreadableFiles++;
            }
        }

        var goals = latest.Values
            .OrderByDescending(item => item.Timestamp)
            .Take(MaximumHistoryGoals)
            .Select(item => item.Goal)
            .ToArray();
        var warnings = new List<string>();
        if (malformedEntries > 0)
        {
            warnings.Add(
                $"Local Community Goal history ignored {malformedEntries:N0} malformed entr{(malformedEntries == 1 ? "y" : "ies")}.");
        }

        if (unreadableFiles > 0)
        {
            warnings.Add(
                $"Local Community Goal history could not read {unreadableFiles:N0} journal file{(unreadableFiles == 1 ? string.Empty : "s")}.");
        }

        return new CommunityGoalJournalHistoryReadResult(
            goals,
            string.Join(Environment.NewLine, warnings));
    }

    private static async Task<int> ReadFileAsync(
        FileInfo file,
        string targetFrontierId,
        IDictionary<string, HistoryGoal> latest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var malformedEntries = 0;
        string? currentFrontierId = null;
        var pending = new List<JournalEventEnvelope>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
               is { } line)
        {
            if (!LooksRelevant(line))
            {
                continue;
            }

            if (!JournalEventEnvelope.TryParse(
                    line,
                    out var journalEvent,
                    out _)
                || journalEvent is null)
            {
                malformedEntries++;
                continue;
            }

            if (journalEvent.EventName is "Commander" or "LoadGame")
            {
                currentFrontierId = ReadFrontierId(journalEvent.Payload)
                    ?? currentFrontierId;
                if (currentFrontierId is not null && pending.Count > 0)
                {
                    if (MatchesFrontierId(currentFrontierId, targetFrontierId))
                    {
                        foreach (var pendingEvent in pending)
                        {
                            malformedEntries += ApplyCommunityGoalEvent(
                                pendingEvent,
                                latest);
                        }
                    }

                    pending.Clear();
                }

                continue;
            }

            if (!string.Equals(
                    journalEvent.EventName,
                    "CommunityGoal",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (currentFrontierId is null)
            {
                pending.Add(journalEvent);
            }
            else if (MatchesFrontierId(currentFrontierId, targetFrontierId))
            {
                malformedEntries += ApplyCommunityGoalEvent(journalEvent, latest);
            }
        }

        return malformedEntries;
    }

    private static int ApplyCommunityGoalEvent(
        JournalEventEnvelope journalEvent,
        IDictionary<string, HistoryGoal> latest)
    {
        try
        {
            foreach (var parsed in FrontierCapiSnapshotParser.ParseCommunityGoals(
                journalEvent.RawJson))
            {
                var timestamp = journalEvent.Timestamp ?? DateTimeOffset.MinValue;
                var goal = AddJournalTimestamp(parsed, timestamp);
                var key = GoalKey(goal);
                if (!latest.TryGetValue(key, out var prior)
                    || timestamp >= prior.Timestamp)
                {
                    latest[key] = new HistoryGoal(goal, timestamp);
                }
            }

            return 0;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            return 1;
        }
    }

    private static FrontierCommunityGoalSnapshot AddJournalTimestamp(
        FrontierCommunityGoalSnapshot goal,
        DateTimeOffset timestamp)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in goal.DataPoints ?? [])
        {
            data[point.Path] = point.Value;
        }

        data["journal.communityGoalTimestamp"] = timestamp.ToString(
            "O",
            CultureInfo.InvariantCulture);
        return goal with
        {
            DataPoints = data
                .Select(pair => new FrontierDataPointSnapshot(pair.Key, pair.Value))
                .ToArray(),
        };
    }

    private static string GoalKey(FrontierCommunityGoalSnapshot goal)
    {
        if (goal.Id is { } id)
        {
            return $"id:{id.ToString(CultureInfo.InvariantCulture)}";
        }

        return "goal:"
            + NormalizeText(goal.Title)
            + ":"
            + (goal.ExpiresAt?.ToUniversalTime().ToString(
                "O",
                CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string? ReadFrontierId(JsonElement payload)
    {
        if (!payload.TryGetProperty("FID", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var frontierId = value.GetString();
        return string.IsNullOrWhiteSpace(frontierId) ? null : frontierId;
    }

    private static bool MatchesFrontierId(string first, string second) =>
        string.Equals(
            NormalizeFrontierId(first),
            NormalizeFrontierId(second),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFrontierId(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith('F') || normalized.StartsWith('f')
            ? normalized[1..]
            : normalized;
    }

    private static string NormalizeText(string value) =>
        string.Concat(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant));

    private static bool LooksRelevant(string line) =>
        line.Contains("CommunityGoal", StringComparison.Ordinal)
        || line.Contains("\"Commander\"", StringComparison.Ordinal)
        || line.Contains("\"LoadGame\"", StringComparison.Ordinal);

    private sealed record HistoryGoal(
        FrontierCommunityGoalSnapshot Goal,
        DateTimeOffset Timestamp);
}
