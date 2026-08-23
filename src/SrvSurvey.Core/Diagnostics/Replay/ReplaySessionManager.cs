using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Diagnostics.Replay;

public sealed class ReplaySessionManager
{
    public const int CurrentFormatVersion = 1;

    private const long MaximumJournalBytes = 256L * 1024L * 1024L;
    private const int MaximumJournalEvents = 2_000_000;
    private const int MaximumJournalLineCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

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

        if (sourceInfo.Length > MaximumJournalBytes)
        {
            throw new InvalidDataException(
                "The journal selected for replay is larger than the supported limit.");
        }

        var sessionId = Guid.NewGuid();
        var sessionDirectory = Path.Combine(
            Path.GetFullPath(managedRoot),
            $"replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{sessionId:N}");
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
        var playbackJournalPath = Path.Combine(
            playbackDirectory,
            "Journal.9999-12-31T235959.01.log");
        IReadOnlyList<JournalReplayEvent> events;
        ReplayCommander commander;
        string sourceSha256;
        JournalReplayPackageManifest? package = null;
        try
        {
            if (string.Equals(
                    Path.GetExtension(fullSourcePath),
                    ".srvreplay",
                    StringComparison.OrdinalIgnoreCase))
            {
                package = await ExtractPackageAsync(
                    fullSourcePath,
                    sourceJournalPath,
                    cancellationToken);
            }
            else
            {
                await CopyFileAsync(
                    fullSourcePath,
                    sourceJournalPath,
                    cancellationToken);
            }

            events = await ReadEventsAsync(
                sourceJournalPath,
                cancellationToken);
            commander = ResolveCommander(events);
            sourceSha256 = await ComputeSha256Async(
                sourceJournalPath,
                cancellationToken);
            ValidatePackage(package, events, commander, sourceSha256);
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
            DateTimeOffset.UtcNow,
            Path.GetFileName(fullSourcePath),
            sourceSha256,
            events.Count,
            events.FirstOrDefault()?.Timestamp,
            events.LastOrDefault()?.Timestamp,
            package?.SourceVersion ?? "Unpackaged Elite journal",
            package?.PrivacyMode ?? ReplayPrivacyMode.Raw,
            commander,
            new DiagnosticReplaySessionPaths(
                "source/journal.jsonl",
                "playback/Journal.9999-12-31T235959.01.log",
                "config",
                "data",
                "cache",
                "logs"));
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
            playbackJournalPath,
            configDirectory,
            dataDirectory,
            cacheDirectory,
            logsDirectory,
            manifest.SourceVersion,
            manifest.PrivacyMode,
            commander,
            events);
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
        var line = await reader.ReadLineAsync(cancellationToken);
        while (line is not null)
        {
            var nextLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                line = nextLine;
                continue;
            }

            if (line.Length > MaximumJournalLineCharacters)
            {
                throw new InvalidDataException(
                    $"Journal line {events.Count + 1:N0} is larger than the supported limit.");
            }

            if (events.Count >= MaximumJournalEvents)
            {
                throw new InvalidDataException(
                    "The journal contains more events than the supported limit.");
            }

            try
            {
                using var document = JsonDocument.Parse(
                    line,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 64,
                    });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !TryGetString(root, "event", out var eventName))
                {
                    throw new InvalidDataException(
                        $"Journal line {events.Count + 1:N0} does not contain an event name.");
                }

                DateTimeOffset? timestamp = null;
                if (TryGetString(root, "timestamp", out var timestampText)
                    && DateTimeOffset.TryParse(
                        timestampText,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }

                events.Add(new JournalReplayEvent(
                    events.Count,
                    timestamp,
                    eventName,
                    line));
            }
            catch (JsonException) when (
                allowIncompleteFinalLine && nextLine is null)
            {
                break;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Journal line {events.Count + 1:N0} is not valid JSON.",
                    exception);
            }

            line = nextLine;
        }

        if (requireEvents && events.Count == 0)
        {
            throw new InvalidDataException(
                "The journal selected for replay contains no events.");
        }

        return events;
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
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task<JournalReplayPackageManifest> ExtractPackageAsync(
        string packagePath,
        string journalDestination,
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
        if (manifests.Length != 1 || journals.Length != 1)
        {
            throw new InvalidDataException(
                "A replay package must contain exactly one manifest and one journal.");
        }

        if (manifests[0].Length > 1024 * 1024)
        {
            throw new InvalidDataException(
                "The replay package manifest is larger than the supported limit.");
        }

        if (journals[0].Length > MaximumJournalBytes)
        {
            throw new InvalidDataException(
                "The replay package journal is larger than the supported limit.");
        }

        JournalReplayPackageManifest package;
        try
        {
            await using var manifestStream = manifests[0].Open();
            package = await JsonSerializer
                .DeserializeAsync<JournalReplayPackageManifest>(
                    manifestStream,
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

        if (package.FormatVersion
            != JournalReplayExporter.CurrentPackageFormatVersion)
        {
            throw new InvalidDataException(
                $"Replay package format {package.FormatVersion} is not supported by this build.");
        }

        await using var source = journals[0].Open();
        await using var destination = new FileStream(
            journalDestination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
        return package;
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        var normalized = entry.FullName.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
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

        if (string.IsNullOrWhiteSpace(package.SourceVersion)
            || !Enum.IsDefined(package.PrivacyMode))
        {
            throw new InvalidDataException(
                "The replay package source metadata is invalid.");
        }

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
    string RawJson);

public sealed record DiagnosticReplaySession(
    string ManifestPath,
    string SessionDirectory,
    string SourceJournalPath,
    string PlaybackJournalPath,
    string ConfigDirectory,
    string DataDirectory,
    string CacheDirectory,
    string LogsDirectory,
    string SourceVersion,
    ReplayPrivacyMode PrivacyMode,
    ReplayCommander Commander,
    IReadOnlyList<JournalReplayEvent> Events)
{
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
    }

    public static async Task<DiagnosticReplaySession> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
        {
            throw new FileNotFoundException(
                "The diagnostic replay manifest does not exist.",
                fullManifestPath);
        }

        var manifestInfo = new FileInfo(fullManifestPath);
        if (manifestInfo.Length > 1024 * 1024)
        {
            throw new InvalidDataException(
                "The diagnostic replay manifest is larger than the supported limit.");
        }

        DiagnosticReplayManifest manifest;
        try
        {
            await using var stream = manifestInfo.OpenRead();
            manifest = await JsonSerializer.DeserializeAsync<DiagnosticReplayManifest>(
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

        if (manifest.FormatVersion != ReplaySessionManager.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Replay format {manifest.FormatVersion} is not supported by this SrvSurvey build.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SourceVersion)
            || !Enum.IsDefined(manifest.PrivacyMode))
        {
            throw new InvalidDataException(
                "The diagnostic replay source metadata is invalid.");
        }

        var sessionDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidDataException(
                "The diagnostic replay manifest has no containing session directory.");
        var sourceJournalPath = ResolveContainedPath(
            sessionDirectory,
            manifest.Paths.SourceJournal);
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

        if (!File.Exists(sourceJournalPath))
        {
            throw new InvalidDataException(
                "The diagnostic replay source journal is missing.");
        }

        var actualChecksum = await ReplaySessionManager.ComputeSha256Async(
            sourceJournalPath,
            cancellationToken);
        if (!string.Equals(
                actualChecksum,
                manifest.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The diagnostic replay source checksum does not match the manifest.");
        }

        var events = await ReplaySessionManager.ReadEventsAsync(
            sourceJournalPath,
            cancellationToken);
        if (events.Count != manifest.EventCount)
        {
            throw new InvalidDataException(
                "The diagnostic replay event count does not match the manifest.");
        }

        var commander = ReplaySessionManager.ResolveCommander(events);
        if (!string.Equals(
                commander.FrontierId,
                manifest.Commander.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                commander.Name,
                manifest.Commander.Name,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The diagnostic replay commander does not match the manifest.");
        }

        EnsureDirectory(configDirectory);
        EnsureDirectory(dataDirectory);
        EnsureDirectory(cacheDirectory);
        EnsureDirectory(logsDirectory);
        EnsureDirectory(Path.GetDirectoryName(playbackJournalPath)!);
        if (!File.Exists(playbackJournalPath))
        {
            await File.WriteAllTextAsync(
                playbackJournalPath,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }

        return new DiagnosticReplaySession(
            fullManifestPath,
            sessionDirectory,
            sourceJournalPath,
            playbackJournalPath,
            configDirectory,
            dataDirectory,
            cacheDirectory,
            logsDirectory,
            manifest.SourceVersion,
            manifest.PrivacyMode,
            commander,
            events);
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

        return candidate;
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

        return candidate;
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
    int EventCount,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp,
    string SourceVersion,
    ReplayPrivacyMode PrivacyMode,
    ReplayCommander Commander,
    DiagnosticReplaySessionPaths Paths);

public sealed record DiagnosticReplaySessionPaths(
    string SourceJournal,
    string PlaybackJournal,
    string ConfigDirectory,
    string DataDirectory,
    string CacheDirectory,
    string LogsDirectory);
