using System.Globalization;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

public sealed class SystemScanPersistenceStore
{
    private readonly LegacySystemDataFileStore fileStore;

    public SystemScanPersistenceStore(string dataDirectory)
    {
        fileStore = new LegacySystemDataFileStore(dataDirectory);
    }

    public async Task<SystemScanHistoryLoadResult> LoadAsync(
        string frontierId,
        string? commanderName,
        string systemName,
        long systemAddress,
        GalacticCoordinate? starPosition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);
        var result = await fileStore.LoadAsync(
                new LegacySystemDataFileContext(
                    frontierId,
                    commanderName,
                    systemName,
                    systemAddress,
                    starPosition),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Exists)
        {
            return new SystemScanHistoryLoadResult(
                result.Path,
                false,
                null,
                null);
        }

        if (result.Root is null)
        {
            return new SystemScanHistoryLoadResult(
                result.Path,
                true,
                null,
                result.Error ?? "The legacy system data is malformed.");
        }

        try
        {
            var snapshot = LegacySystemSnapshotParser.Parse(result.Root);
            if (snapshot.SystemAddress != systemAddress)
            {
                throw new InvalidDataException(
                    $"The legacy system file contains address "
                        + $"{snapshot.SystemAddress}, not {systemAddress}.");
            }

            return new SystemScanHistoryLoadResult(
                result.Path,
                true,
                snapshot,
                null);
        }
        catch (InvalidDataException exception)
        {
            return new SystemScanHistoryLoadResult(
                result.Path,
                true,
                null,
                exception.Message);
        }
    }

    public async Task<SystemScanPersistenceResult> SaveAsync(
        SystemScanPersistenceContext context,
        SystemScanSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return await SaveCoreAsync(
                context,
                snapshot,
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SystemScanPersistenceResult>
        SaveFirstFootfallCorrectionAsync(
            SystemScanPersistenceContext context,
            SystemScanSnapshot snapshot,
            int bodyId,
            bool value,
            CancellationToken cancellationToken = default)
    {
        if (!snapshot.Bodies.Any(body => body.BodyId == bodyId))
        {
            throw new ArgumentException(
                "The corrected body is not present in the system snapshot.",
                nameof(bodyId));
        }

        return await SaveCoreAsync(
                context,
                snapshot,
                (bodyId, value),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SystemScanPersistenceResult> SaveCoreAsync(
        SystemScanPersistenceContext context,
        SystemScanSnapshot snapshot,
        (int BodyId, bool Value)? firstFootfallCorrection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.FrontierId);
        if (snapshot.SystemAddress is not { } systemAddress
            || systemAddress <= 0
            || string.IsNullOrWhiteSpace(snapshot.SystemName))
        {
            throw new ArgumentException(
                "A named system snapshot with a positive address is required.",
                nameof(snapshot));
        }

        var fileContext = new LegacySystemDataFileContext(
            context.FrontierId,
            context.CommanderName,
            snapshot.SystemName,
            systemAddress,
            snapshot.StarPosition);
        var mutation = await fileStore.UpdateWithResultAsync(
                fileContext,
                root =>
                {
                    var result = Merge(root, context, snapshot);
                    if (firstFootfallCorrection is { } correction)
                    {
                        ApplyFirstFootfallCorrection(
                            root,
                            correction.BodyId,
                            correction.Value);
                    }

                    return result;
                },
                cancellationToken)
            .ConfigureAwait(false);
        return mutation.Value with { Path = mutation.Path };
    }

    private static void ApplyFirstFootfallCorrection(
        JsonObject root,
        int bodyId,
        bool value)
    {
        if (root["bodies"] is not JsonArray bodies)
        {
            throw new InvalidDataException(
                "The legacy system body collection is malformed and was not overwritten.");
        }

        var body = bodies.OfType<JsonObject>().FirstOrDefault(candidate =>
            ReadInt32(candidate["id"]) == bodyId)
            ?? throw new InvalidDataException(
                "The corrected body could not be represented in the legacy system data.");
        body["firstFootFall"] = value;
    }

    private static SystemScanPersistenceResult Merge(
        JsonObject root,
        SystemScanPersistenceContext context,
        SystemScanSnapshot snapshot)
    {
        var firstVisited = ReadTimestamp(root["firstVisited"]);
        var lastVisited = ReadTimestamp(root["lastVisited"]);
        var isKnownRepeat = firstVisited is not null
            && lastVisited is not null
            && firstVisited != lastVisited;
        var isNewRepeat = firstVisited is not null
            && context.VisitedAt > (lastVisited ?? firstVisited);
        var merged = LegacySystemSnapshotMerger.Merge(
            root,
            snapshot,
            context.CommanderName,
            context.VisitedAt,
            context.VisitedAt);
        var biologicalSignalsRemaining =
            ReadBiologicalSignalsRemaining(merged);

        root.Clear();
        foreach (var pair in merged)
        {
            root[pair.Key] = pair.Value?.DeepClone();
        }

        var isRepeatVisit = isKnownRepeat || isNewRepeat;
        return new SystemScanPersistenceResult(
            string.Empty,
            isRepeatVisit,
            biologicalSignalsRemaining,
            isRepeatVisit && biologicalSignalsRemaining == 0);
    }

    private static int? ReadBiologicalSignalsRemaining(JsonObject root)
    {
        if (root["bodies"] is null)
        {
            return 0;
        }

        if (root["bodies"] is not JsonArray bodies)
        {
            return null;
        }

        var remaining = 0;
        foreach (var bodyNode in bodies)
        {
            var bodyRemaining = ReadBodyBiologicalSignalsRemaining(bodyNode);
            if (bodyRemaining is null)
            {
                return null;
            }

            remaining += bodyRemaining.Value;
        }

        return remaining;
    }

    private static int? ReadBodyBiologicalSignalsRemaining(JsonNode? bodyNode)
    {
        if (bodyNode is not JsonObject body)
        {
            return null;
        }

        var signalCount = ReadInt32(body["bioSignalCount"]);
        if (signalCount is null)
        {
            return body["bioSignalCount"] is not null ? null : 0;
        }

        var analyzedCount = CountAnalyzedOrganisms(body);
        if (analyzedCount is null)
        {
            return null;
        }

        return Math.Max(0, signalCount.Value - analyzedCount.Value);
    }

    private static int? CountAnalyzedOrganisms(JsonObject body)
    {
        if (body["organisms"] is null)
        {
            return 0;
        }

        if (body["organisms"] is not JsonArray organisms)
        {
            return null;
        }

        var analyzedCount = 0;
        foreach (var organismNode in organisms)
        {
            if (!TryCountAnalyzedOrganism(organismNode, ref analyzedCount))
            {
                return null;
            }
        }

        return analyzedCount;
    }

    private static bool TryCountAnalyzedOrganism(
        JsonNode? organismNode,
        ref int analyzedCount)
    {
        if (organismNode is not JsonObject organism)
        {
            return false;
        }

        if (ReadBoolean(organism["analyzed"]) == true)
        {
            analyzedCount++;
        }

        return true;
    }

    private static int? ReadInt32(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var result))
        {
            return result;
        }

        return value.TryGetValue<string>(out var text)
            && int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result)
                    ? result
                    : null;
    }

    private static bool? ReadBoolean(JsonNode? node)
    {
        return node is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonNode? node)
    {
        return node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result)
                    ? result
                    : null;
    }
}

public sealed record SystemScanPersistenceContext(
    string FrontierId,
    string? CommanderName,
    DateTimeOffset VisitedAt);

public sealed record SystemScanPersistenceResult(
    string Path,
    bool IsRepeatVisit,
    int? BiologicalSignalsRemaining,
    bool ShouldSuppressBiologyOverlays);

public sealed record SystemScanHistoryLoadResult(
    string Path,
    bool Exists,
    SystemScanSnapshot? Snapshot,
    string? Error);
