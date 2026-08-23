using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Diagnostics.Replay;

public sealed class ReplaySessionManager
{
    public const int CurrentFormatVersion = 2;

    internal const long MaximumJournalBytes = 256L * 1024L * 1024L;
    internal const long MaximumReplayPackageBytes =
        (MaximumJournalBytes * 2) + 2L * 1024L * 1024L;
    internal const int MaximumJournalEvents = 2_000_000;
    internal const int MaximumJournalLineCharacters = 4 * 1024 * 1024;
    internal const int MaximumReplayManifestBytes = 1024 * 1024;
    internal const int MaximumSourceVersionCharacters = 4096;
    private const int MaximumCommanderIdentityCharacters = 1024;
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly IReadOnlyList<string> AllCompanionTimelineNames =
        Enum.GetValues<ReplayInputKind>()
            .Where(kind => kind != ReplayInputKind.Journal)
            .Select(kind => kind.ToString())
            .ToArray();
    private readonly Func<Guid> createSessionId;
    private readonly Func<DateTimeOffset> getUtcNow;

    public ReplaySessionManager()
        : this(Guid.NewGuid, () => DateTimeOffset.UtcNow)
    {
    }

    internal ReplaySessionManager(
        Func<Guid> createSessionId,
        Func<DateTimeOffset> getUtcNow)
    {
        this.createSessionId = createSessionId
            ?? throw new ArgumentNullException(nameof(createSessionId));
        this.getUtcNow = getUtcNow
            ?? throw new ArgumentNullException(nameof(getUtcNow));
    }

    public async Task<DiagnosticReplaySession> ImportAsync(
        string sourcePath,
        string managedRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceInfo = new FileInfo(fullSourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException(
                "The journal selected for replay does not exist.",
                fullSourcePath);
        }

        var isPackage = string.Equals(
            Path.GetExtension(fullSourcePath),
            ".srvreplay",
            StringComparison.OrdinalIgnoreCase);
        var maximumSourceBytes = isPackage
            ? MaximumReplayPackageBytes
            : MaximumJournalBytes;
        if (sourceInfo.Length > maximumSourceBytes)
        {
            throw new InvalidDataException(
                "The journal selected for replay is larger than the supported limit.");
        }

        var sessionId = createSessionId();
        var createdAt = getUtcNow();
        var sessionDirectory = Path.Combine(
            Path.GetFullPath(managedRoot),
            $"replay-{createdAt:yyyyMMdd-HHmmss}-{sessionId:N}");
        var sourceDirectory = Path.Combine(sessionDirectory, "source");
        var playbackDirectory = Path.Combine(sessionDirectory, "playback");
        var configDirectory = Path.Combine(sessionDirectory, "config");
        var dataDirectory = Path.Combine(sessionDirectory, "data");
        var cacheDirectory = Path.Combine(sessionDirectory, "cache");
        var logsDirectory = Path.Combine(sessionDirectory, "logs");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(playbackDirectory);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(logsDirectory);

        var sourceJournalPath = Path.Combine(sourceDirectory, "journal.jsonl");
        var sourceCompanionPath = Path.Combine(
            sourceDirectory,
            "companions.jsonl");
        var playbackJournalPath = Path.Combine(
            playbackDirectory,
            "Journal.9999-12-31T235959.01.log");
        IReadOnlyList<JournalReplayEvent> events;
        IReadOnlyList<CompanionTimelineEntry> companionEntries;
        ReplayCommander commander;
        string sourceSha256;
        JournalReplayPackageManifest? package = null;
        try
        {
            if (isPackage)
            {
                package = await ExtractPackageAsync(
                    fullSourcePath,
                    sourceJournalPath,
                    sourceCompanionPath,
                    cancellationToken);
            }
            else
            {
                await CopyFileAsync(
                    fullSourcePath,
                    sourceJournalPath,
                    cancellationToken);
                await File.WriteAllTextAsync(
                    sourceCompanionPath,
                    string.Empty,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
            }

            events = await ReadEventsAsync(
                sourceJournalPath,
                cancellationToken);
            companionEntries = await ReadCompanionsAsync(
                sourceCompanionPath,
                cancellationToken);
            commander = ResolveCommander(events);
            sourceSha256 = await ComputeSha256Async(
                sourceJournalPath,
                cancellationToken);
            ValidatePackage(package, events, commander, sourceSha256);
            var companionSha256 = await ComputeSha256Async(
                sourceCompanionPath,
                cancellationToken);
            ValidateCompanionPackage(
                package,
                companionEntries,
                companionSha256);
            events = MergeTimeline(
                events,
                package?.BootstrapEventCount ?? 0,
                companionEntries,
                package?.CompanionBootstrapEventCount ?? 0);
            await File.WriteAllTextAsync(
                playbackJournalPath,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
        catch
        {
            TryDeleteSession(sessionDirectory);
            throw;
        }

        var manifest = new DiagnosticReplayManifest(
            CurrentFormatVersion,
            sessionId,
            createdAt,
            Path.GetFileName(fullSourcePath),
            sourceSha256,
            await ComputeSha256Async(sourceCompanionPath, cancellationToken),
            events.Count,
            package?.EventCount ?? events.Count,
            companionEntries.Count,
            (package?.BootstrapEventCount ?? 0)
                + (package?.CompanionBootstrapEventCount ?? 0),
            package?.BootstrapEventCount ?? 0,
            package?.CompanionBootstrapEventCount ?? 0,
            package?.CompanionFirstTimestamp,
            package?.CompanionLastTimestamp,
            events.Count > 0 ? events[0].Timestamp : null,
            events.Count > 0 ? events[^1].Timestamp : null,
            package?.SourceVersion ?? "Unpackaged Elite journal",
            package?.PrivacyMode ?? ReplayPrivacyMode.Raw,
            commander,
            new DiagnosticReplaySessionPaths(
                "source/journal.jsonl",
                "source/companions.jsonl",
                "playback/Journal.9999-12-31T235959.01.log",
                "config",
                "data",
                "cache",
                "logs"),
            package?.MissingCompanionTimelines ?? AllCompanionTimelineNames,
            package?.PresentationSnapshot);
        var manifestPath = Path.Combine(sessionDirectory, "replay-session.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJson),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return new DiagnosticReplaySession(
            manifestPath,
            sessionDirectory,
            sourceJournalPath,
            sourceCompanionPath,
            playbackJournalPath,
            configDirectory,
            dataDirectory,
            cacheDirectory,
            logsDirectory,
            manifest.SourceVersion,
            manifest.PrivacyMode,
            commander,
            events,
            manifest.BootstrapInputCount,
            manifest.MissingCompanionTimelines,
            manifest.CompanionFirstTimestamp,
            manifest.CompanionLastTimestamp,
            manifest.PresentationSnapshot);
    }

    internal static string ValidateSourceVersion(string? sourceVersion)
    {
        if (string.IsNullOrWhiteSpace(sourceVersion)
            || sourceVersion.Length > MaximumSourceVersionCharacters)
        {
            throw new InvalidDataException(
                "The replay package source version is invalid or larger than the supported limit.");
        }

        return sourceVersion.Trim();
    }

    internal static void ValidatePackageMetadata(
        JournalReplayPackageManifest package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.FormatVersion
            != JournalReplayExporter.CurrentPackageFormatVersion)
        {
            throw new InvalidDataException(
                $"Replay package format {package.FormatVersion} is not supported by this build.");
        }

        _ = ValidateSourceVersion(package.SourceVersion);
        ReplayPresentationSnapshotValidator.Validate(
            package.PresentationSnapshot);
        if (!Enum.IsDefined(package.PrivacyMode)
            || package.Commander is null
            || string.IsNullOrWhiteSpace(package.Commander.Name)
            || string.IsNullOrWhiteSpace(package.Commander.FrontierId)
            || package.Commander.Name.Length > MaximumCommanderIdentityCharacters
            || package.Commander.FrontierId.Length
                > MaximumCommanderIdentityCharacters
            || package.EventCount is <= 0 or > MaximumJournalEvents
            || package.BootstrapEventCount is < 0
                || package.BootstrapEventCount > package.EventCount
            || package.JournalSha256 is null
            || package.JournalSha256.Length != 64
            || package.JournalSha256.Any(character => !Uri.IsHexDigit(character))
            || package.CompanionEventCount is < 0 or > MaximumJournalEvents
            || package.CompanionBootstrapEventCount is < 0
            || package.CompanionBootstrapEventCount
                > package.CompanionEventCount
            || package.CompanionSha256 is null
            || package.CompanionSha256.Length != 64
            || package.CompanionSha256.Any(character => !Uri.IsHexDigit(character))
            || package.EventCount + package.CompanionEventCount
                > MaximumJournalEvents
            || package.MissingCompanionTimelines is null
            || package.MissingCompanionTimelines.Count > 64
            || package.MissingCompanionTimelines.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Length > 128))
        {
            throw new InvalidDataException(
                "The replay package source metadata or commander is invalid.");
        }
    }

    internal static async Task<IReadOnlyList<JournalReplayEvent>> ReadEventsAsync(
        string path,
        CancellationToken cancellationToken,
        bool requireEvents = true,
        bool allowIncompleteFinalLine = false)
    {
        List<JournalReplayEvent> events = [];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        var boundedReader = new BoundedJournalLineReader(reader);
        var line = await boundedReader.ReadLineAsync(
            MaximumJournalLineCharacters,
            cancellationToken);
        while (line is not null)
        {
            var nextLine = await boundedReader.ReadLineAsync(
                MaximumJournalLineCharacters,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                line = nextLine;
                continue;
            }

            if (events.Count >= MaximumJournalEvents)
            {
                throw new InvalidDataException(
                    "The journal contains more events than the supported limit.");
            }

            var replayEvent = ParseReplayEvent(
                line,
                events.Count,
                allowIncompleteFinalLine && nextLine is null);
            if (replayEvent is null)
            {
                break;
            }

            events.Add(replayEvent);
            line = nextLine;
        }

        if (requireEvents && events.Count == 0)
        {
            throw new InvalidDataException(
                "The journal selected for replay contains no events.");
        }

        return events;
    }

    internal static async Task<IReadOnlyList<CompanionTimelineEntry>>
        ReadCompanionsAsync(
            string path,
            CancellationToken cancellationToken)
    {
        var entries = new List<CompanionTimelineEntry>();
        await foreach (var entry in CompanionTimelineStore.StreamFileAsync(
                           path,
                           cancellationToken))
        {
            if (entries.Count >= MaximumJournalEvents)
            {
                throw new InvalidDataException(
                    "The companion timeline contains more snapshots than the supported limit.");
            }

            entries.Add(entry);
        }

        return entries;
    }

    internal static IReadOnlyList<JournalReplayEvent> MergeTimeline(
        IReadOnlyList<JournalReplayEvent> journalEvents,
        int journalBootstrapCount,
        IReadOnlyList<CompanionTimelineEntry> companionEntries,
        int companionBootstrapCount)
    {
        var journal = CreateJournalTimelineItems(journalEvents);
        var companions = companionEntries.Select((item, sourceIndex) =>
            new TimelineItem(
                new JournalReplayEvent(
                    0,
                    item.Timestamp,
                    item.Kind.ToString(),
                    item.RawJson,
                    item.Kind),
                item.Timestamp,
                KindOrder: 1,
                sourceIndex));
        var bootstrap = journal.Take(journalBootstrapCount)
            .Concat(companions.Take(companionBootstrapCount))
            .OrderBy(item => item.SortTimestamp)
            .ThenBy(item => item.KindOrder)
            .ThenBy(item => item.SourceIndex);
        var selected = journal.Skip(journalBootstrapCount)
            .Concat(companions.Skip(companionBootstrapCount))
            .OrderBy(item => item.SortTimestamp)
            .ThenBy(item => item.KindOrder)
            .ThenBy(item => item.SourceIndex);
        return bootstrap.Concat(selected)
            .Select((item, index) => item.Event with { Index = index })
            .ToArray();
    }

    private static IReadOnlyList<TimelineItem> CreateJournalTimelineItems(
        IReadOnlyList<JournalReplayEvent> journalEvents)
    {
        var items = new List<TimelineItem>(journalEvents.Count);
        var sortTimestamp = DateTimeOffset.MinValue;
        for (var sourceIndex = 0; sourceIndex < journalEvents.Count; sourceIndex++)
        {
            var item = journalEvents[sourceIndex];
            if (item.Timestamp is { } timestamp && timestamp > sortTimestamp)
            {
                sortTimestamp = timestamp;
            }

            items.Add(new TimelineItem(
                item with { Kind = ReplayInputKind.Journal },
                sortTimestamp,
                KindOrder: 0,
                sourceIndex));
        }

        return items;
    }

    private sealed record TimelineItem(
        JournalReplayEvent Event,
        DateTimeOffset SortTimestamp,
        int KindOrder,
        int SourceIndex);

    private static JournalReplayEvent? ParseReplayEvent(
        string line,
        int eventIndex,
        bool allowIncomplete)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                line,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
        }
        catch (JsonException) when (allowIncomplete)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Journal line {eventIndex + 1:N0} is not valid JSON.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "event", out var eventName))
            {
                throw new InvalidDataException(
                    $"Journal line {eventIndex + 1:N0} does not contain an event name.");
            }

            return new JournalReplayEvent(
                eventIndex,
                ParseTimestamp(root),
                eventName,
                line);
        }
    }

    private static DateTimeOffset? ParseTimestamp(JsonElement root)
    {
        return TryGetString(root, "timestamp", out var timestampText)
            && DateTimeOffset.TryParse(
                timestampText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var timestamp)
                ? timestamp
                : null;
    }

    internal sealed class BoundedJournalLineReader(TextReader reader)
    {
        private readonly char[] buffer = new char[64 * 1024];
        private int offset;
        private int count;

        public async Task<string?> ReadLineAsync(
            int maximumCharacters,
            CancellationToken cancellationToken)
        {
            var line = new StringBuilder(256);
            var characterCount = 0;
            while (true)
            {
                if (offset >= count
                    && !await FillBufferAsync(cancellationToken))
                {
                    return characterCount == 0 ? null : line.ToString();
                }

                var newline = Array.IndexOf(
                    buffer,
                    '\n',
                    offset,
                    count - offset);
                var segmentEnd = newline >= 0 ? newline : count;
                var segmentLength = segmentEnd - offset;
                ValidateLineLength(
                    characterCount,
                    segmentLength,
                    maximumCharacters);
                line.Append(buffer, offset, segmentLength);
                characterCount += segmentLength;
                offset = newline >= 0 ? newline + 1 : count;
                if (newline < 0)
                {
                    continue;
                }

                TrimCarriageReturn(line);
                return line.ToString();
            }
        }

        private static void TrimCarriageReturn(StringBuilder line)
        {
            if (line.Length > 0 && line[^1] == '\r')
            {
                line.Length--;
            }
        }

        private async Task<bool> FillBufferAsync(
            CancellationToken cancellationToken)
        {
            count = await reader.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);
            offset = 0;
            return count > 0;
        }

        private static void ValidateLineLength(
            int characterCount,
            int segmentLength,
            int maximumCharacters)
        {
            if (characterCount + segmentLength > maximumCharacters)
            {
                throw new InvalidDataException(
                    "A journal line is larger than the supported limit.");
            }
        }

    }

    internal static ReplayCommander ResolveCommander(
        IReadOnlyList<JournalReplayEvent> events)
    {
        foreach (var replayEvent in events)
        {
            if (!string.Equals(
                    replayEvent.EventName,
                    "Commander",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    replayEvent.EventName,
                    "LoadGame",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var document = JsonDocument.Parse(replayEvent.RawJson);
            var root = document.RootElement;
            var nameProperty = string.Equals(
                replayEvent.EventName,
                "LoadGame",
                StringComparison.OrdinalIgnoreCase)
                    ? "Commander"
                    : "Name";
            if (TryGetString(root, nameProperty, out var commanderName)
                && TryGetString(root, "FID", out var frontierId))
            {
                return new ReplayCommander(commanderName, frontierId);
            }
        }

        throw new InvalidDataException(
            "The replay does not contain a Commander or LoadGame event with both commander name and Frontier ID. Personal profile data will not be used as a fallback.");
    }

    internal static JsonSerializerOptions GetManifestJsonOptions() => ManifestJson;

    internal static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    internal static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await CopyBoundedAsync(
            source,
            destination,
            MaximumJournalBytes,
            cancellationToken);
        await destination.FlushAsync(cancellationToken);
        if (destination.Length > MaximumJournalBytes)
        {
            throw new InvalidDataException(
                "The journal selected for replay is larger than the supported limit.");
        }
    }

    internal static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);

        var buffer = new byte[64 * 1024];
        long written = 0;
        while (true)
        {
            var remaining = maximumBytes - written;
            var readLength = remaining > 0
                ? (int)Math.Min(buffer.Length, remaining)
                : 1;
            var read = await source.ReadAsync(
                buffer.AsMemory(0, readLength),
                cancellationToken);
            if (read == 0)
            {
                return;
            }

            if (written + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "The journal selected for replay is larger than the supported limit.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
            written += read;
        }
    }

    private static async Task<JournalReplayPackageManifest> ExtractPackageAsync(
        string packagePath,
        string journalDestination,
        string companionDestination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(
            input,
            ZipArchiveMode.Read,
            leaveOpen: false);
        if (archive.Entries.Count > 64)
        {
            throw new InvalidDataException(
                "The replay package contains too many archive entries.");
        }

        foreach (var entry in archive.Entries)
        {
            ValidateArchiveEntry(entry);
        }

        var manifests = archive.Entries
            .Where(entry => string.Equals(
                entry.FullName,
                "replay-package.json",
                StringComparison.Ordinal))
            .ToArray();
        var journals = archive.Entries
            .Where(entry => string.Equals(
                entry.FullName,
                "journal.jsonl",
                StringComparison.Ordinal))
            .ToArray();
        var companions = archive.Entries
            .Where(entry => string.Equals(
                entry.FullName,
                "companions.jsonl",
                StringComparison.Ordinal))
            .ToArray();
        if (manifests.Length != 1
            || journals.Length != 1
            || companions.Length != 1)
        {
            throw new InvalidDataException(
                "A replay package must contain exactly one manifest, journal, and companion timeline.");
        }

        if (manifests[0].Length > MaximumReplayManifestBytes)
        {
            throw new InvalidDataException(
                "The replay package manifest is larger than the supported limit.");
        }

        if (journals[0].Length > MaximumJournalBytes)
        {
            throw new InvalidDataException(
                "The replay package journal is larger than the supported limit.");
        }

        if (companions[0].Length > MaximumJournalBytes)
        {
            throw new InvalidDataException(
                "The replay package companion timeline is larger than the supported limit.");
        }

        JournalReplayPackageManifest package;
        try
        {
            await using var manifestStream = await manifests[0].OpenAsync(
                cancellationToken);
            await using var boundedManifest = new MemoryStream();
            await CopyBoundedAsync(
                manifestStream,
                boundedManifest,
                MaximumReplayManifestBytes,
                cancellationToken);
            boundedManifest.Position = 0;
            package = await JsonSerializer
                .DeserializeAsync<JournalReplayPackageManifest>(
                    boundedManifest,
                    JournalReplayExporter.GetPackageJsonOptions(),
                    cancellationToken)
                ?? throw new InvalidDataException(
                    "The replay package manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The replay package manifest is not valid JSON.",
                exception);
        }

        ValidatePackageMetadata(package);

        await ExtractArchiveEntryAsync(
            journals[0],
            journalDestination,
            cancellationToken);
        await ExtractArchiveEntryAsync(
            companions[0],
            companionDestination,
            cancellationToken);

        return package;
    }

    private static async Task ExtractArchiveEntryAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = await entry.OpenAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await CopyBoundedAsync(
            source,
            destination,
            MaximumJournalBytes,
            cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        var normalized = entry.FullName.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith('/')
            || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidDataException(
                "A replay package entry escapes the package root.");
        }

        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixFileType == 0xA000)
        {
            throw new InvalidDataException(
                "Replay packages may not contain symbolic links.");
        }
    }

    private static void ValidatePackage(
        JournalReplayPackageManifest? package,
        IReadOnlyList<JournalReplayEvent> events,
        ReplayCommander commander,
        string checksum)
    {
        if (package is null)
        {
            return;
        }

        ValidatePackageMetadata(package);

        if (!string.Equals(
                package.JournalSha256,
                checksum,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The replay package journal checksum does not match its manifest.");
        }

        if (package.EventCount != events.Count)
        {
            throw new InvalidDataException(
                "The replay package event count does not match its manifest.");
        }

        if (!string.Equals(
                package.Commander.FrontierId,
                commander.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                package.Commander.Name,
                commander.Name,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The replay package commander does not match its journal.");
        }
    }

    private static void ValidateCompanionPackage(
        JournalReplayPackageManifest? package,
        IReadOnlyList<CompanionTimelineEntry> entries,
        string checksum)
    {
        if (package is null)
        {
            return;
        }

        if (!string.Equals(
                package.CompanionSha256,
                checksum,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The replay package companion checksum does not match its manifest.");
        }

        if (package.CompanionEventCount != entries.Count)
        {
            throw new InvalidDataException(
                "The replay package companion count does not match its manifest.");
        }
    }

    private static void TryDeleteSession(string sessionDirectory)
    {
        try
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Import validation retains its original failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Import validation retains its original failure.
        }
    }
}

public sealed record ReplayCommander(string Name, string FrontierId);

public sealed record JournalReplayEvent(
    int Index,
    DateTimeOffset? Timestamp,
    string EventName,
    string RawJson,
    ReplayInputKind Kind = ReplayInputKind.Journal);

public sealed record DiagnosticReplaySession(
    string ManifestPath,
    string SessionDirectory,
    string SourceJournalPath,
    string SourceCompanionPath,
    string PlaybackJournalPath,
    string ConfigDirectory,
    string DataDirectory,
    string CacheDirectory,
    string LogsDirectory,
    string SourceVersion,
    ReplayPrivacyMode PrivacyMode,
    ReplayCommander Commander,
    IReadOnlyList<JournalReplayEvent> Events,
    int BootstrapInputCount,
    IReadOnlyList<string> MissingCompanionTimelines,
    DateTimeOffset? CompanionFirstTimestamp,
    DateTimeOffset? CompanionLastTimestamp,
    ReplayPresentationSnapshot? PresentationSnapshot = null)
{
    private static readonly string[] CompanionFileNames =
        Enum.GetValues<ReplayInputKind>()
            .Where(kind => kind != ReplayInputKind.Journal)
            .Select(CompanionTimelineFileNames.Resolve)
            .ToArray();

    public async Task ResetRuntimeAsync(CancellationToken cancellationToken)
    {
        ClearContainedDirectory(ConfigDirectory, cancellationToken);
        ClearContainedDirectory(DataDirectory, cancellationToken);
        ClearContainedDirectory(CacheDirectory, cancellationToken);
        await File.WriteAllTextAsync(
            EnsureContainedPath(PlaybackJournalPath),
            string.Empty,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        foreach (var fileName in CompanionFileNames)
        {
            File.Delete(EnsureContainedPath(Path.Combine(
                Path.GetDirectoryName(PlaybackJournalPath)!,
                fileName)));
        }
    }

    public static Task<DiagnosticReplaySession> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        return LoadAsync(
            manifestPath,
            ReplaySessionManager.MaximumJournalBytes,
            cancellationToken);
    }

    internal static async Task<DiagnosticReplaySession> LoadAsync(
        string manifestPath,
        long maximumJournalBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumJournalBytes);
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
        {
            throw new FileNotFoundException(
                "The diagnostic replay manifest does not exist.",
                fullManifestPath);
        }

        var manifestInfo = new FileInfo(fullManifestPath);
        var manifest = await ReadManifestAsync(manifestInfo, cancellationToken);
        ValidateLoadedManifest(manifest);
        ValidateVersionTwoPathSchema(manifest.Paths);
        ReplayPresentationSnapshotValidator.Validate(
            manifest.PresentationSnapshot);

        var sessionDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidDataException(
                "The diagnostic replay manifest has no containing session directory.");
        var sourceJournalPath = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.SourceJournal);
        var sourceCompanionPath = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.SourceCompanions);
        var playbackJournalPath = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.PlaybackJournal);
        var configDirectory = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.ConfigDirectory);
        var dataDirectory = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.DataDirectory);
        var cacheDirectory = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.CacheDirectory);
        var logsDirectory = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.LogsDirectory);

        await ValidateSourceFileAsync(
            sourceJournalPath,
            maximumJournalBytes,
            manifest.SourceSha256,
            "The diagnostic replay source journal is missing.",
            "The diagnostic replay source journal is larger than the supported limit.",
            "The diagnostic replay source checksum does not match the manifest.",
            cancellationToken);
        await ValidateSourceFileAsync(
            sourceCompanionPath,
            maximumJournalBytes,
            manifest.SourceCompanionSha256,
            "The diagnostic replay source companion timeline is missing.",
            "The diagnostic replay companion timeline is larger than the supported limit.",
            "The diagnostic replay companion checksum does not match the manifest.",
            cancellationToken);

        var journalEvents = await ReplaySessionManager.ReadEventsAsync(
            sourceJournalPath,
            cancellationToken);
        var companionEntries = await ReplaySessionManager.ReadCompanionsAsync(
            sourceCompanionPath,
            cancellationToken);
        ValidateSourceCounts(manifest, journalEvents, companionEntries);

        var events = ReplaySessionManager.MergeTimeline(
            journalEvents,
            manifest.JournalBootstrapEventCount,
            companionEntries,
            manifest.CompanionBootstrapEventCount);
        ValidateTimelineCount(manifest, events);

        var commander = ReplaySessionManager.ResolveCommander(journalEvents);
        ValidateCommander(manifest.Commander, commander);

        EnsureDirectory(configDirectory);
        EnsureDirectory(dataDirectory);
        EnsureDirectory(cacheDirectory);
        EnsureDirectory(logsDirectory);
        EnsureDirectory(Path.GetDirectoryName(playbackJournalPath)!);
        await EnsurePlaybackJournalAsync(
            playbackJournalPath,
            cancellationToken);

        return new DiagnosticReplaySession(
            fullManifestPath,
            sessionDirectory,
            sourceJournalPath,
            sourceCompanionPath,
            playbackJournalPath,
            configDirectory,
            dataDirectory,
            cacheDirectory,
            logsDirectory,
            manifest.SourceVersion,
            manifest.PrivacyMode,
            commander,
            events,
            manifest.BootstrapInputCount,
            manifest.MissingCompanionTimelines,
            manifest.CompanionFirstTimestamp,
            manifest.CompanionLastTimestamp,
            manifest.PresentationSnapshot);
    }

    private static async Task<DiagnosticReplayManifest> ReadManifestAsync(
        FileInfo manifestInfo,
        CancellationToken cancellationToken)
    {
        if (manifestInfo.Length > ReplaySessionManager.MaximumReplayManifestBytes)
        {
            throw new InvalidDataException(
                "The diagnostic replay manifest is larger than the supported limit.");
        }

        try
        {
            await using var stream = manifestInfo.OpenRead();
            return await JsonSerializer.DeserializeAsync<DiagnosticReplayManifest>(
                    stream,
                    ReplaySessionManager.GetManifestJsonOptions(),
                    cancellationToken)
                ?? throw new InvalidDataException(
                    "The diagnostic replay manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The diagnostic replay manifest is not valid JSON.",
                exception);
        }
    }

    private static void ValidateLoadedManifest(DiagnosticReplayManifest manifest)
    {
        if (manifest.FormatVersion != ReplaySessionManager.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Replay format {manifest.FormatVersion} is not supported by this SrvSurvey build.");
        }

        _ = ReplaySessionManager.ValidateSourceVersion(manifest.SourceVersion);
        if (!Enum.IsDefined(manifest.PrivacyMode)
            || manifest.Commander is null
            || string.IsNullOrWhiteSpace(manifest.Commander.Name)
            || string.IsNullOrWhiteSpace(manifest.Commander.FrontierId)
            || manifest.Paths is null
            || manifest.MissingCompanionTimelines is null
            || manifest.EventCount < 1
            || manifest.JournalEventCount < 1
            || manifest.CompanionEventCount < 0
            || manifest.JournalEventCount + manifest.CompanionEventCount
                != manifest.EventCount
            || manifest.JournalBootstrapEventCount < 0
            || manifest.CompanionBootstrapEventCount < 0
            || manifest.JournalBootstrapEventCount
                > manifest.JournalEventCount
            || manifest.CompanionBootstrapEventCount
                > manifest.CompanionEventCount
            || manifest.BootstrapInputCount
                != manifest.JournalBootstrapEventCount
                    + manifest.CompanionBootstrapEventCount)
        {
            throw new InvalidDataException(
                "The diagnostic replay manifest is missing required source, commander, or path metadata.");
        }
    }

    private static async Task ValidateSourceFileAsync(
        string path,
        long maximumBytes,
        string expectedChecksum,
        string missingMessage,
        string tooLargeMessage,
        string checksumMessage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException(missingMessage);
        }

        if (new FileInfo(path).Length > maximumBytes)
        {
            throw new InvalidDataException(tooLargeMessage);
        }

        var actualChecksum = await ReplaySessionManager.ComputeSha256Async(
            path,
            cancellationToken);
        if (!string.Equals(
                actualChecksum,
                expectedChecksum,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(checksumMessage);
        }
    }

    private static void ValidateSourceCounts(
        DiagnosticReplayManifest manifest,
        IReadOnlyCollection<JournalReplayEvent> journalEvents,
        IReadOnlyCollection<CompanionTimelineEntry> companionEntries)
    {
        if (journalEvents.Count != manifest.JournalEventCount
            || companionEntries.Count != manifest.CompanionEventCount)
        {
            throw new InvalidDataException(
                "The diagnostic replay event count does not match the manifest.");
        }
    }

    private static void ValidateTimelineCount(
        DiagnosticReplayManifest manifest,
        IReadOnlyCollection<JournalReplayEvent> events)
    {
        if (events.Count != manifest.EventCount)
        {
            throw new InvalidDataException(
                "The diagnostic replay timeline count does not match the manifest.");
        }
    }

    private static void ValidateCommander(
        ReplayCommander expected,
        ReplayCommander actual)
    {
        if (!string.Equals(
                actual.FrontierId,
                expected.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                actual.Name,
                expected.Name,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The diagnostic replay commander does not match the manifest.");
        }
    }

    private static async Task EnsurePlaybackJournalAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(
                path,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
    }

    private static string ResolveContainedPath(
        string sessionDirectory,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                "Replay session paths must be relative to the session directory.");
        }

        var fullSessionDirectory = Path.GetFullPath(sessionDirectory);
        var candidate = Path.GetFullPath(Path.Combine(
            fullSessionDirectory,
            relativePath));
        var relative = Path.GetRelativePath(fullSessionDirectory, candidate);
        if (relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException(
                "A replay session path escapes the managed session directory.");
        }

        RejectReparsePoints(fullSessionDirectory, candidate);
        return candidate;
    }

    private static void ValidateVersionTwoPathSchema(
        DiagnosticReplaySessionPaths paths)
    {
        var valid = string.Equals(
                paths.SourceJournal,
                "source/journal.jsonl",
                StringComparison.Ordinal)
            && string.Equals(
                paths.SourceCompanions,
                "source/companions.jsonl",
                StringComparison.Ordinal)
            && string.Equals(
                paths.PlaybackJournal,
                "playback/Journal.9999-12-31T235959.01.log",
                StringComparison.Ordinal)
            && string.Equals(paths.ConfigDirectory, "config", StringComparison.Ordinal)
            && string.Equals(paths.DataDirectory, "data", StringComparison.Ordinal)
            && string.Equals(paths.CacheDirectory, "cache", StringComparison.Ordinal)
            && string.Equals(paths.LogsDirectory, "logs", StringComparison.Ordinal);
        if (!valid)
        {
            throw new InvalidDataException(
                "Replay format 2 paths do not match the required managed path schema.");
        }
    }

    private string EnsureContainedPath(string path)
    {
        var sessionRoot = Path.GetFullPath(SessionDirectory);
        var candidate = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(sessionRoot, candidate);
        if (relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException(
                "A replay runtime path escapes the managed session directory.");
        }

        RejectReparsePoints(sessionRoot, candidate);
        return candidate;
    }

    private static void RejectReparsePoints(
        string sessionRoot,
        string candidate)
    {
        RejectReparsePointIfPresent(sessionRoot);
        var relative = Path.GetRelativePath(sessionRoot, candidate);
        var current = sessionRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                break;
            }

            RejectReparsePointIfPresent(current);
        }
    }

    private static void RejectReparsePointIfPresent(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Replay session paths may not contain a symbolic link or reparse point.");
        }
    }

    private void ClearContainedDirectory(
        string path,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(EnsureContainedPath(path));
        directory.Create();
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is DirectoryInfo childDirectory)
            {
                var isReparsePoint = (childDirectory.Attributes
                    & FileAttributes.ReparsePoint) != 0;
                childDirectory.Delete(recursive: !isReparsePoint);
            }
            else
            {
                entry.Delete();
            }
        }
    }

    private static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }
}

public sealed record DiagnosticReplayManifest(
    int FormatVersion,
    Guid SessionId,
    DateTimeOffset CreatedAt,
    string ImportedFileName,
    string SourceSha256,
    string SourceCompanionSha256,
    int EventCount,
    int JournalEventCount,
    int CompanionEventCount,
    int BootstrapInputCount,
    int JournalBootstrapEventCount,
    int CompanionBootstrapEventCount,
    DateTimeOffset? CompanionFirstTimestamp,
    DateTimeOffset? CompanionLastTimestamp,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp,
    string SourceVersion,
    ReplayPrivacyMode PrivacyMode,
    ReplayCommander Commander,
    DiagnosticReplaySessionPaths Paths,
    IReadOnlyList<string> MissingCompanionTimelines,
    ReplayPresentationSnapshot? PresentationSnapshot = null);

public sealed record DiagnosticReplaySessionPaths(
    string SourceJournal,
    string SourceCompanions,
    string PlaybackJournal,
    string ConfigDirectory,
    string DataDirectory,
    string CacheDirectory,
    string LogsDirectory);
