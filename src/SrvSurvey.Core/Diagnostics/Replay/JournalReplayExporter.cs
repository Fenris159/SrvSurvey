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
    string SourceVersion,
    ReplayPresentationSnapshot? PresentationSnapshot = null);

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
    private const string CommanderJournalName = "Commander";

    private static readonly JsonSerializerOptions PackageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly IReplayPackageWriter packageWriter;
    private readonly Func<
        string,
        CancellationToken,
        IAsyncEnumerable<JournalHistoryEvent>> streamHistory;

    public JournalReplayExporter()
        : this(
            new ZipReplayPackageWriter(),
            JournalHistoryReader.StreamAsync)
    {
    }

    internal JournalReplayExporter(IReplayPackageWriter packageWriter)
        : this(packageWriter, JournalHistoryReader.StreamAsync)
    {
    }

    internal JournalReplayExporter(
        Func<string, CancellationToken, IAsyncEnumerable<JournalHistoryEvent>>
            streamHistory)
        : this(new ZipReplayPackageWriter(), streamHistory)
    {
    }

    internal JournalReplayExporter(
        IReplayPackageWriter packageWriter,
        Func<string, CancellationToken, IAsyncEnumerable<JournalHistoryEvent>>
            streamHistory)
    {
        this.packageWriter = packageWriter
            ?? throw new ArgumentNullException(nameof(packageWriter));
        this.streamHistory = streamHistory
            ?? throw new ArgumentNullException(nameof(streamHistory));
    }

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

        ReplayPresentationSnapshotValidator.Validate(
            request.PresentationSnapshot);
        ReplaySessionManager.ValidateSourceVersion(request.SourceVersion);
        var scan = await ScanAsync(
            journalDirectory,
            request,
            cancellationToken);

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidDataException(
                "The replay export destination has no containing directory.");
        Directory.CreateDirectory(destinationDirectory);
        var journalSpoolPath = Path.Combine(
            destinationDirectory,
            $".journal-export.{Guid.NewGuid():N}.tmp");
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var checksum = await WriteJournalSpoolAsync(
                journalDirectory,
                journalSpoolPath,
                request,
                scan,
                cancellationToken);
            var outputCommander = request.PrivacyMode
                == ReplayPrivacyMode.Redacted
                    ? RedactCommander(scan.Commander, scan.Identities)
                    : scan.Commander;
            var package = new JournalReplayPackageManifest(
                CurrentPackageFormatVersion,
                DateTimeOffset.UtcNow,
                request.SourceVersion.Trim(),
                request.From,
                request.To,
                request.PrivacyMode,
                scan.EventCount,
                scan.Bootstrap.Length,
                scan.FirstTimestamp,
                scan.LastTimestamp,
                outputCommander,
                checksum,
                ["status", "cargo", "shipLocker", "navRoute", "market"],
                request.PresentationSnapshot);
            ReplaySessionManager.ValidatePackageMetadata(package);
            await packageWriter.WriteAsync(
                temporaryPath,
                package,
                journalSpoolPath,
                cancellationToken);
            await ValidateWrittenPackageAsync(
                temporaryPath,
                package,
                cancellationToken);
            File.Delete(journalSpoolPath);
            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
            return new JournalReplayExportResult(
                fullDestinationPath,
                scan.EventCount,
                scan.Bootstrap.Length,
                package.Commander,
                package.FirstTimestamp,
                package.LastTimestamp);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
            TryDeleteTemporaryFile(journalSpoolPath);
        }
    }

    private async Task<ReplayExportScan> ScanAsync(
        string journalDirectory,
        JournalReplayExportRequest request,
        CancellationToken cancellationToken)
    {
        var bootstrapSelector = new ReplayBootstrapSelector();
        var identityBuilder = request.PrivacyMode == ReplayPrivacyMode.Redacted
            ? new IdentityRedactionBuilder()
            : null;
        using var inputHash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        JournalReplayEvent[] bootstrap = [];
        ReplayCommander? commander = null;
        DateTimeOffset? firstTimestamp = null;
        DateTimeOffset? lastTimestamp = null;
        var selectedEventCount = 0;
        long inputByteCount = 0;
        await foreach (var historyEvent in streamHistory(
                           journalDirectory,
                           cancellationToken))
        {
            var replayEvent = new JournalReplayEvent(
                historyEvent.Index,
                historyEvent.Timestamp,
                historyEvent.EventName,
                historyEvent.RawJson);
            if (!IsWithinRange(replayEvent, request))
            {
                if (selectedEventCount == 0)
                {
                    bootstrapSelector.Observe(replayEvent);
                }

                continue;
            }

            if (selectedEventCount == 0)
            {
                bootstrap = bootstrapSelector.Snapshot();
                inputByteCount += ScanBootstrap(
                    bootstrap,
                    identityBuilder,
                    inputHash,
                    ref commander);

                firstTimestamp = bootstrap.Length > 0
                    ? bootstrap[0].Timestamp
                    : replayEvent.Timestamp;
            }

            selectedEventCount++;
            identityBuilder?.Observe(replayEvent);
            commander ??= TryReadCommander(replayEvent);
            AppendRawEvent(inputHash, replayEvent.RawJson);
            lastTimestamp = replayEvent.Timestamp;
            inputByteCount += Encoding.UTF8.GetByteCount(
                replayEvent.RawJson) + 1L;
            ValidateOutputBounds(
                bootstrap.Length + selectedEventCount,
                inputByteCount);
        }

        if (selectedEventCount == 0)
        {
            throw new InvalidDataException(
                "No journal events exist in the selected replay range.");
        }

        if (commander is null)
        {
            throw new InvalidDataException(
                "The replay does not contain a Commander or LoadGame event with both commander name and Frontier ID. Personal profile data will not be used as a fallback.");
        }

        return new ReplayExportScan(
            bootstrap,
            bootstrap.Length + selectedEventCount,
            firstTimestamp,
            lastTimestamp,
            commander,
            identityBuilder?.Build() ?? [],
            Convert.ToHexStringLower(inputHash.GetHashAndReset()));
    }

    private static long ScanBootstrap(
        IReadOnlyList<JournalReplayEvent> bootstrap,
        IdentityRedactionBuilder? identityBuilder,
        IncrementalHash inputHash,
        ref ReplayCommander? commander)
    {
        long inputByteCount = 0;
        foreach (var bootstrapEvent in bootstrap)
        {
            identityBuilder?.Observe(bootstrapEvent);
            commander ??= TryReadCommander(bootstrapEvent);
            AppendRawEvent(inputHash, bootstrapEvent.RawJson);
            inputByteCount += Encoding.UTF8.GetByteCount(
                bootstrapEvent.RawJson) + 1L;
        }

        return inputByteCount;
    }

    private async Task<string> WriteJournalSpoolAsync(
        string journalDirectory,
        string spoolPath,
        JournalReplayExportRequest request,
        ReplayExportScan scan,
        CancellationToken cancellationToken)
    {
        var bootstrapIndices = scan.Bootstrap
            .Select(replayEvent => replayEvent.Index)
            .ToHashSet();
        var locations = new LocationRedactionState();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var inputHash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        await using var output = new FileStream(
            spoolPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        long outputByteCount = 0;
        var outputEventCount = 0;
        await foreach (var historyEvent in streamHistory(
                           journalDirectory,
                           cancellationToken))
        {
            var replayEvent = new JournalReplayEvent(
                historyEvent.Index,
                historyEvent.Timestamp,
                historyEvent.EventName,
                historyEvent.RawJson);
            if (!bootstrapIndices.Contains(replayEvent.Index)
                && !IsWithinRange(replayEvent, request))
            {
                continue;
            }

            AppendRawEvent(inputHash, replayEvent.RawJson);
            var sanitized = replayEvent with
            {
                RawJson = RemoveCredentials(replayEvent.RawJson),
            };
            var outputLine = request.PrivacyMode == ReplayPrivacyMode.Redacted
                ? RedactEvent(sanitized, scan.Identities, locations)
                : sanitized.RawJson;
            var lineBytes = Encoding.UTF8.GetBytes(outputLine);
            outputEventCount++;
            outputByteCount += lineBytes.Length + 1L;
            ValidateOutputBounds(outputEventCount, outputByteCount);
            hash.AppendData(lineBytes);
            hash.AppendData(Newline.Span);
            await output.WriteAsync(lineBytes.AsMemory(), cancellationToken);
            await output.WriteAsync(Newline, cancellationToken);
        }

        if (outputEventCount != scan.EventCount)
        {
            throw new InvalidDataException(
                "The journal history changed while the replay export was being created. Refresh and export again.");
        }

        var inputChecksum = Convert.ToHexStringLower(
            inputHash.GetHashAndReset());
        if (!string.Equals(
                inputChecksum,
                scan.InputSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The journal history changed while the replay export was being created. Refresh and export again.");
        }

        await output.FlushAsync(cancellationToken);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static JsonSerializerOptions GetPackageJsonOptions() => PackageJson;

    private static ReadOnlyMemory<byte> Newline { get; } = new byte[]
    {
        (byte)'\n',
    };

    private static void AppendRawEvent(
        IncrementalHash hash,
        string rawJson)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(rawJson));
        hash.AppendData(Newline.Span);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Preserve the primary export failure or committed result.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the primary export failure or committed result.
        }
    }

    internal static void ValidateOutputBounds(int eventCount, long byteCount)
    {
        if (eventCount > ReplaySessionManager.MaximumJournalEvents)
        {
            throw new InvalidDataException(
                "The replay export contains more events than the supported package limit.");
        }

        if (byteCount > ReplaySessionManager.MaximumJournalBytes)
        {
            throw new InvalidDataException(
                "The replay export is larger than the supported package limit.");
        }
    }

    private static async Task ValidateWrittenPackageAsync(
        string path,
        JournalReplayPackageManifest expected,
        CancellationToken cancellationToken)
    {
        using var archive = await ZipFile.OpenReadAsync(
            path,
            cancellationToken);
        var manifestEntry = archive.GetEntry("replay-package.json")
            ?? throw new InvalidDataException(
                "The completed replay package is missing its manifest.");
        var journalEntry = archive.GetEntry("journal.jsonl")
            ?? throw new InvalidDataException(
                "The completed replay package is missing its journal.");
        if (manifestEntry.Length
            > ReplaySessionManager.MaximumReplayManifestBytes)
        {
            throw new InvalidDataException(
                "The completed replay package manifest is larger than the supported limit.");
        }

        JournalReplayPackageManifest actual;
        await using (var stream = await manifestEntry.OpenAsync(
                         cancellationToken))
        {
            actual = await JsonSerializer
                .DeserializeAsync<JournalReplayPackageManifest>(
                    stream,
                    PackageJson,
                    cancellationToken)
                ?? throw new InvalidDataException(
                    "The completed replay package manifest is empty.");
        }

        ReplaySessionManager.ValidatePackageMetadata(actual);
        await using var journal = await journalEntry.OpenAsync(
            cancellationToken);
        var checksum = Convert.ToHexStringLower(
            await SHA256.HashDataAsync(journal, cancellationToken));
        if (actual.FormatVersion != expected.FormatVersion
            || actual.EventCount != expected.EventCount
            || actual.BootstrapEventCount != expected.BootstrapEventCount
            || actual.PrivacyMode != expected.PrivacyMode
            || !string.Equals(
                actual.SourceVersion,
                expected.SourceVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                actual.Commander.Name,
                expected.Commander.Name,
                StringComparison.Ordinal)
            || !string.Equals(
                actual.Commander.FrontierId,
                expected.Commander.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                actual.JournalSha256,
                expected.JournalSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                checksum,
                expected.JournalSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The completed replay package could not be verified.");
        }
    }

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

    private static ReplayCommander? TryReadCommander(
        JournalReplayEvent replayEvent)
    {
        if (replayEvent.EventName is not CommanderJournalName and not "LoadGame")
        {
            return null;
        }

        using var document = JsonDocument.Parse(replayEvent.RawJson);
        var nameProperty = replayEvent.EventName == CommanderJournalName
            ? "Name"
            : CommanderJournalName;
        return ReplaySessionManager.TryGetString(
                document.RootElement,
                nameProperty,
                out var name)
            && ReplaySessionManager.TryGetString(
                document.RootElement,
                "FID",
                out var frontierId)
                ? new ReplayCommander(name, frontierId)
                : null;
    }

    private static string RedactEvent(
        JournalReplayEvent replayEvent,
        IReadOnlyList<IdentityRedaction> identities,
        LocationRedactionState locations)
    {
        var root = JsonNode.Parse(replayEvent.RawJson)
            ?? throw new InvalidDataException(
                "A journal event could not be read while applying redaction.");
        string? eventSystemIdentity = null;
        if (root is JsonObject eventObject)
        {
            eventSystemIdentity = ResolveSystemIdentity(
                eventObject,
                locations.CurrentSystemIdentity);
            foreach (var propertyName in SensitivePathProperties)
            {
                _ = eventObject.Remove(propertyName);
            }

            if (string.Equals(
                    replayEvent.EventName,
                    "FSDTarget",
                    StringComparison.OrdinalIgnoreCase)
                && eventObject["Name"] is { } destinationName)
            {
                eventObject["Name"] = RedactNode(
                    destinationName,
                    "DestinationSystem",
                    identities,
                    locations,
                    eventSystemIdentity);
            }
        }

        _ = RedactNode(
            root,
            propertyName: null,
            identities,
            locations,
            eventSystemIdentity);
        if (eventSystemIdentity is not null
            && CurrentSystemEvents.Contains(replayEvent.EventName))
        {
            locations.CurrentSystemIdentity = eventSystemIdentity;
        }

        if (replayEvent.EventName is "ReceiveText" or "SendText"
            && root is JsonObject chat)
        {
            _ = chat.Remove("Message");
            _ = chat.Remove("Message_Localised");
        }

        return root.ToJsonString();
    }

    private static ReplayCommander RedactCommander(
        ReplayCommander commander,
        IReadOnlyList<IdentityRedaction> identities)
    {
        var identity = identities.FirstOrDefault(candidate =>
            string.Equals(
                candidate.OriginalName,
                commander.Name,
                StringComparison.Ordinal)
            && string.Equals(
                candidate.OriginalFrontierId,
                commander.FrontierId,
                StringComparison.OrdinalIgnoreCase));
        return identity is null
            ? commander
            : new ReplayCommander(
                identity.ReplacementName,
                identity.ReplacementFrontierId);
    }

    private static JsonNode RedactNode(
        JsonNode node,
        string? propertyName,
        IReadOnlyList<IdentityRedaction> identities,
        LocationRedactionState locations,
        string? systemIdentity)
    {
        var redactedValue = RedactValue(
            node,
            propertyName,
            identities,
            locations,
            systemIdentity);
        if (redactedValue is not null)
        {
            return redactedValue;
        }

        RedactChildren(
            node,
            identities,
            locations,
            systemIdentity);
        return node;
    }

    private static JsonNode? RedactValue(
        JsonNode node,
        string? propertyName,
        IReadOnlyList<IdentityRedaction> identities,
        LocationRedactionState locations,
        string? systemIdentity)
    {
        if (propertyName is not null
            && LocationNameProperties.Contains(propertyName)
            && node is JsonValue locationValue
            && locationValue.TryGetValue<string>(out var locationName))
        {
            if (!locations.Names.TryGetValue(locationName, out var replacement))
            {
                replacement = $"Replay Location {locations.Names.Count + 1:000}";
                locations.Names.Add(locationName, replacement);
            }

            return JsonValue.Create(replacement);
        }

        if (propertyName is not null
            && LocationIdProperties.Contains(propertyName)
            && node is JsonValue)
        {
            var original = node.ToJsonString();
            var key = CreateLocationIdKey(
                propertyName,
                original,
                systemIdentity);
            if (!locations.Ids.TryGetValue(key, out var replacement))
            {
                replacement = 9_000_000_000_000_000L + locations.Ids.Count;
                locations.Ids.Add(key, replacement);
            }

            return JsonValue.Create(replacement);
        }

        if (propertyName is not null
            && LocationCoordinateProperties.Contains(propertyName))
        {
            if (node is JsonArray coordinates)
            {
                return new JsonArray(coordinates.Select(_ =>
                    (JsonNode?)JsonValue.Create(0d)).ToArray());
            }

            return JsonValue.Create(0d);
        }

        if (node is JsonValue value
            && value.TryGetValue<string>(out var text))
        {
            return JsonValue.Create(ReplaceSensitiveText(text, identities));
        }

        return null;
    }

    private static void RedactChildren(
        JsonNode node,
        IReadOnlyList<IdentityRedaction> identities,
        LocationRedactionState locations,
        string? systemIdentity)
    {
        if (node is JsonObject objectNode)
        {
            var objectSystemIdentity = ResolveSystemIdentity(
                objectNode,
                systemIdentity);
            foreach (var childName in objectNode
                         .Select(property => property.Key)
                         .ToArray())
            {
                if (objectNode[childName] is { } child)
                {
                    var redactedChild = RedactNode(
                        child,
                        childName,
                        identities,
                        locations,
                        objectSystemIdentity);
                    if (!ReferenceEquals(child, redactedChild))
                    {
                        objectNode[childName] = redactedChild;
                    }
                }
            }
        }
        else if (node is JsonArray arrayNode)
        {
            for (var index = 0; index < arrayNode.Count; index++)
            {
                if (arrayNode[index] is { } child)
                {
                    var redactedChild = RedactNode(
                        child,
                        propertyName: null,
                        identities,
                        locations,
                        systemIdentity);
                    if (!ReferenceEquals(child, redactedChild))
                    {
                        arrayNode[index] = redactedChild;
                    }
                }
            }
        }
    }

    private static string? ResolveSystemIdentity(
        JsonObject value,
        string? fallback)
    {
        foreach (var propertyName in SystemAddressProperties)
        {
            if (value[propertyName] is { } address
                && address is JsonValue)
            {
                return address.ToJsonString();
            }
        }

        return fallback;
    }

    private static string CreateLocationIdKey(
        string propertyName,
        string original,
        string? systemIdentity)
    {
        if (SystemAddressProperties.Contains(propertyName))
        {
            return "system:" + original;
        }

        if (BodyIdProperties.Contains(propertyName))
        {
            return $"body:{systemIdentity ?? "unknown-system"}:{original}";
        }

        return $"{propertyName.ToUpperInvariant()}:{original}";
    }

    private static string ReplaceSensitiveText(
        string source,
        IReadOnlyList<IdentityRedaction> identities)
    {
        var replacements = identities.SelectMany(identity => new[]
            {
                new SensitiveReplacement(
                    identity.OriginalName,
                    identity.ReplacementName,
                    StringComparison.Ordinal),
                new SensitiveReplacement(
                    identity.OriginalFrontierId,
                    identity.ReplacementFrontierId,
                    StringComparison.OrdinalIgnoreCase),
            })
            .DistinctBy(item => (item.Original, item.Comparison))
            .ToArray();
        StringBuilder? output = null;
        var sourceIndex = 0;
        while (sourceIndex < source.Length)
        {
            SensitiveReplacement? nextReplacement = null;
            var nextIndex = int.MaxValue;
            foreach (var replacement in replacements)
            {
                var match = source.IndexOf(
                    replacement.Original,
                    sourceIndex,
                    replacement.Comparison);
                if (match >= 0
                    && (match < nextIndex
                        || match == nextIndex
                            && (nextReplacement is null
                                || replacement.Original.Length
                                    > nextReplacement.Original.Length)))
                {
                    nextIndex = match;
                    nextReplacement = replacement;
                }
            }

            if (nextReplacement is null)
            {
                break;
            }

            output ??= new StringBuilder(source.Length + 32);
            output.Append(source, sourceIndex, nextIndex - sourceIndex);
            output.Append(nextReplacement.Replacement);
            sourceIndex = nextIndex + nextReplacement.Original.Length;
        }

        if (output is null)
        {
            return source;
        }

        output.Append(source, sourceIndex, source.Length - sourceIndex);
        return output.ToString();
    }

    private static readonly HashSet<string> LocationNameProperties = new(
        [
            "StarSystem",
            "System",
            "SystemName",
            "DestinationSystem",
            "TargetSystem",
            "OriginSystem",
            "HomeSystem",
            "BodyName",
            "Body",
            "StationName",
            "Station",
            "DestinationStation",
            "SettlementName",
            "Settlement",
            "NearestDestination",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> LocationIdProperties = new(
        ["SystemAddress", "DestinationSystemAddress", "BodyID", "Body", "MarketID"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SystemAddressProperties = new(
        ["SystemAddress", "DestinationSystemAddress"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BodyIdProperties = new(
        ["BodyID", "Body"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CurrentSystemEvents = new(
        ["Location", "FSDJump", "CarrierJump"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> LocationCoordinateProperties = new(
        ["StarPos", "Latitude", "Longitude"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SensitivePathProperties = new(
        ["Filename"],
        StringComparer.OrdinalIgnoreCase);

    private sealed record ReplayExportScan(
        JournalReplayEvent[] Bootstrap,
        int EventCount,
        DateTimeOffset? FirstTimestamp,
        DateTimeOffset? LastTimestamp,
        ReplayCommander Commander,
        IReadOnlyList<IdentityRedaction> Identities,
        string InputSha256);

    private sealed record IdentityRedaction(
        string OriginalName,
        string OriginalFrontierId,
        string ReplacementName,
        string ReplacementFrontierId);

    private sealed class IdentityRedactionBuilder
    {
        private const int MaximumIdentityCount = 4096;
        private readonly List<ReplayCommander> commanders = [];

        public void Observe(JournalReplayEvent replayEvent)
        {
            var commander = TryReadCommander(replayEvent);
            if (commander is null
                || commanders.Any(existing => string.Equals(
                        existing.Name,
                        commander.Name,
                        StringComparison.Ordinal)
                    && string.Equals(
                        existing.FrontierId,
                        commander.FrontierId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (commanders.Count >= MaximumIdentityCount)
            {
                throw new InvalidDataException(
                    "The replay export contains too many commander identities to redact safely.");
            }

            commanders.Add(commander);
        }

        public IdentityRedaction[] Build()
        {
            return commanders
                .Select((commander, index) => new IdentityRedaction(
                    commander.Name,
                    commander.FrontierId,
                    index == 0
                        ? "Replay Commander"
                        : $"Replay Commander {index + 1:N0}",
                    $"F{index:000000}"))
                .OrderByDescending(identity => identity.OriginalName.Length)
                .ToArray();
        }
    }

    private sealed record SensitiveReplacement(
        string Original,
        string Replacement,
        StringComparison Comparison);

    private sealed class LocationRedactionState
    {
        public Dictionary<string, string> Names { get; } = new(
            StringComparer.Ordinal);

        public Dictionary<string, long> Ids { get; } = new(
            StringComparer.Ordinal);

        public string? CurrentSystemIdentity { get; set; }
    }

    private sealed class ReplayBootstrapSelector
    {
        private JournalReplayEvent? fileHeader;
        private JournalReplayEvent? commanderEvent;
        private JournalReplayEvent? loadGameEvent;
        private JournalReplayEvent? locationEvent;
        private ReplayCommander? identity;

        public void Observe(JournalReplayEvent replayEvent)
        {
            if (string.Equals(
                    replayEvent.EventName,
                    "Fileheader",
                    StringComparison.Ordinal))
            {
                fileHeader = replayEvent;
            }

            if (TryReadCommander(replayEvent) is { } candidate)
            {
                if (!SameIdentity(identity, candidate))
                {
                    identity = candidate;
                    commanderEvent = null;
                    loadGameEvent = null;
                    locationEvent = null;
                }

                if (string.Equals(
                        replayEvent.EventName,
                        CommanderJournalName,
                        StringComparison.Ordinal))
                {
                    commanderEvent = replayEvent;
                }
                else
                {
                    loadGameEvent = replayEvent;
                }
            }

            if (replayEvent.EventName is "Location" or "FSDJump" or "CarrierJump")
            {
                locationEvent = replayEvent;
            }
        }

        public JournalReplayEvent[] Snapshot()
        {
            return new[]
                {
                    fileHeader,
                    commanderEvent,
                    loadGameEvent,
                    locationEvent,
                }
                .Where(item => item is not null)
                .Cast<JournalReplayEvent>()
                .DistinctBy(item => item.Index)
                .OrderBy(item => item.Index)
                .ToArray();
        }

        private static bool SameIdentity(
            ReplayCommander? first,
            ReplayCommander second)
        {
            return first is not null
                && string.Equals(
                    first.Name,
                    second.Name,
                    StringComparison.Ordinal)
                && string.Equals(
                    first.FrontierId,
                    second.FrontierId,
                    StringComparison.OrdinalIgnoreCase);
        }
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
            foreach (var child in arrayNode.OfType<JsonNode>())
            {
                RemoveCredentialProperties(child);
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
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("authentication", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal);
    }
}

internal interface IReplayPackageWriter
{
    Task WriteAsync(
        string path,
        JournalReplayPackageManifest package,
        string journalPath,
        CancellationToken cancellationToken);
}

internal sealed class ZipReplayPackageWriter : IReplayPackageWriter
{
    public async Task WriteAsync(
        string path,
        JournalReplayPackageManifest package,
        string journalPath,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true);
        var manifestEntry = archive.CreateEntry(
            "replay-package.json",
            CompressionLevel.Optimal);
        await using (var manifestStream = await manifestEntry.OpenAsync(
                         cancellationToken))
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                package,
                JournalReplayExporter.GetPackageJsonOptions(),
                cancellationToken);
        }

        var journalEntry = archive.CreateEntry(
            "journal.jsonl",
            CompressionLevel.Optimal);
        await using var journalStream = await journalEntry.OpenAsync(
            cancellationToken);
        await using var journalInput = new FileStream(
            journalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await journalInput.CopyToAsync(journalStream, cancellationToken);
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
    IReadOnlyList<string> MissingCompanionTimelines,
    ReplayPresentationSnapshot? PresentationSnapshot = null);

public sealed record ReplayPresentationSnapshot(
    int ViewportWidth,
    int ViewportHeight,
    int GlobalScaleIndex,
    double? DefaultOpacity,
    IReadOnlyDictionary<string, bool> OverlayEnablement,
    IReadOnlyDictionary<string, ReplayOverlayPlacement> OverlayPlacements);

public sealed record ReplayOverlayPlacement(
    ReplayHorizontalAnchor Horizontal,
    int HorizontalOffset,
    ReplayVerticalAnchor Vertical,
    int VerticalOffset,
    double? Opacity,
    int? ScaleIndex);

public enum ReplayHorizontalAnchor
{
    Left,
    Center,
    Right,
    Screen,
}

public enum ReplayVerticalAnchor
{
    Top,
    Middle,
    Bottom,
    Screen,
}

internal static class ReplayPresentationSnapshotValidator
{
    public static void Validate(ReplayPresentationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        ValidateSnapshotHeader(snapshot);
        foreach (var entry in snapshot.OverlayEnablement)
        {
            ValidateName(entry.Key);
        }

        foreach (var entry in snapshot.OverlayPlacements)
        {
            ValidateName(entry.Key);
            ValidatePlacement(entry.Value);
        }
    }

    private static void ValidateSnapshotHeader(
        ReplayPresentationSnapshot snapshot)
    {
        if (snapshot.ViewportWidth is < 320 or > 16_384
            || snapshot.ViewportHeight is < 200 or > 16_384
            || snapshot.GlobalScaleIndex is < 0 or > 100
            || snapshot.DefaultOpacity is { } opacity
                && (!double.IsFinite(opacity) || opacity is < 0 or > 1)
            || snapshot.OverlayEnablement is null
            || snapshot.OverlayPlacements is null
            || snapshot.OverlayEnablement.Count > 256
            || snapshot.OverlayPlacements.Count > 256)
        {
            throw new InvalidDataException(
                "The replay overlay presentation snapshot is invalid.");
        }
    }

    private static void ValidatePlacement(ReplayOverlayPlacement? placement)
    {
        if (placement is null
            || !Enum.IsDefined(placement.Horizontal)
            || !Enum.IsDefined(placement.Vertical)
            || Math.Abs((long)placement.HorizontalOffset) > 100_000
            || Math.Abs((long)placement.VerticalOffset) > 100_000
            || placement.Opacity is { } itemOpacity
                && (!double.IsFinite(itemOpacity)
                    || itemOpacity is < 0 or > 1)
            || placement.ScaleIndex is { } scaleIndex
                && scaleIndex is < 0 or > 100)
        {
            throw new InvalidDataException(
                "The replay overlay presentation snapshot contains an invalid placement.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        {
            throw new InvalidDataException(
                "The replay overlay presentation snapshot contains an invalid overlay name.");
        }
    }
}
