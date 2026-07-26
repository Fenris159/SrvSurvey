using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Updates;

public interface IPublishedReferenceUpdateService
{
    Task<PublishedReferenceUpdateResult> RefreshAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default);
}

public sealed record PublishedReferenceUpdateResult(
    PublishedReferenceVersions PreviousVersions,
    PublishedReferenceVersions ActiveVersions,
    IReadOnlyList<string> UpdatedCatalogs,
    IReadOnlyList<string> Warnings,
    string? BackupDirectory)
{
    public bool RestartRequired => UpdatedCatalogs.Count > 0;
}

public sealed record PublishedReferenceUris(
    Uri CodexReference,
    Uri RegionalCodexCandidatesCsv,
    Uri BiologyCriteriaArchive,
    Uri GuardianTemplates,
    Uri GuardianRuins,
    Uri GuardianStructures,
    Uri GuardianSurveyArchive,
    Uri HumanSettlementsArchive,
    Uri GreenGasGiants,
    Uri RavenNicknames)
{
    public static PublishedReferenceUris Default { get; } = new(
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/refs/heads/main/docs/codexRef.json"),
        new Uri("https://docs.google.com/spreadsheets/d/1TpPZUFd61KUQWy1sV8VhScZiVbRWJ435wTN8xjN0Qv0/gviz/tq?tqx=out:csv&sheet=Individual+Items"),
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/main/data/bio-criteria.zip"),
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/main/SrvSurvey/guardianSiteTemplates.json"),
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/main/SrvSurvey/allRuins.json"),
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/main/SrvSurvey/allStructures.json"),
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/main/data/guardian.zip"),
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/main/data/settlements.zip"),
        new Uri("https://raw.githubusercontent.com/njthomson/SrvSurvey/main/SrvSurvey/ggg.json"),
        new Uri("https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/api/misc/nicknames"));
}

internal enum PublishedReferenceUpdateCheckpoint
{
    BackupVerified,
    ExistingReferencesMoved,
    CandidateActivated,
}

public sealed class PublishedReferenceUpdateService
    : IPublishedReferenceUpdateService
{
    private const long MaximumDownloadBytes = 32L * 1024 * 1024;
    private const long MaximumExpandedArchiveBytes = 128L * 1024 * 1024;
    private const int MaximumArchiveEntries = 2_048;

    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly IPublishedDataIndexClient indexClient;
    private readonly PublishedReferenceVersionStore versionStore;
    private readonly HttpClient client;
    private readonly PublishedReferenceUris uris;
    private readonly TimeProvider timeProvider;
    private readonly Action<PublishedReferenceUpdateCheckpoint>? checkpoint;

    public PublishedReferenceUpdateService(
        IPublishedDataIndexClient? indexClient = null,
        PublishedReferenceVersionStore? versionStore = null,
        HttpClient? client = null,
        PublishedReferenceUris? uris = null,
        TimeProvider? timeProvider = null)
        : this(
            indexClient,
            versionStore,
            client,
            uris,
            timeProvider,
            null)
    {
    }

    internal PublishedReferenceUpdateService(
        IPublishedDataIndexClient? indexClient,
        PublishedReferenceVersionStore? versionStore,
        HttpClient? client,
        PublishedReferenceUris? uris,
        TimeProvider? timeProvider,
        Action<PublishedReferenceUpdateCheckpoint>? checkpoint)
    {
        this.indexClient = indexClient ?? new PublishedDataIndexClient();
        this.versionStore = versionStore ?? new PublishedReferenceVersionStore();
        this.client = client ?? SharedClient;
        this.uris = uris ?? PublishedReferenceUris.Default;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.checkpoint = checkpoint;
    }

    public async Task<PublishedReferenceUpdateResult> RefreshAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(root);
        var previous = versionStore.Load(root);
        var remote = await indexClient.GetAsync(cancellationToken)
            .ConfigureAwait(false);
        var active = LegacyReferenceCatalogLoader.Load(root);
        var sources = active.Sources.ToDictionary(
            source => source.Catalog,
            StringComparer.Ordinal);
        var warnings = new List<string>();
        var updateCodex = NeedsUpdate(
            previous.CodexReference,
            remote.CodexReferenceVersion,
            sources["Codex reference"]);
        var regionalCodexCandidates = RegionalCodexCandidateCatalog.Load(root);
        var updateRegionalCodexCandidates = updateCodex
            || RegionalCodexCandidatesNeedUpdate(
                root,
                regionalCodexCandidates,
                timeProvider.GetUtcNow());
        var updateBiology = NeedsUpdate(
            previous.BiologyCriteria,
            remote.BiologyCriteriaVersion,
            sources["biology criteria"]);
        if (updateBiology
            && remote.BiologyEngineVersion > BiologyCriteriaCatalog.EngineVersion)
        {
            updateBiology = false;
            warnings.Add(
                $"Published biology criteria require engine {remote.BiologyEngineVersion}, "
                + $"but this build supports engine {BiologyCriteriaCatalog.EngineVersion}.");
        }

        var updateGuardianTemplates = NeedsUpdate(
            previous.SettlementTemplate,
            remote.SettlementTemplateVersion,
            sources["Guardian site templates"]);
        var updateGuardian = remote.GuardianVersion > previous.Guardian
            || !sources["Guardian site index"].IsLocal
            || !sources["Guardian published surveys"].IsLocal;
        var updateSettlements = NeedsUpdate(
            previous.Settlements,
            remote.SettlementsVersion,
            sources["human settlement templates"]);
        var updateGreenGasGiants = NeedsUpdate(
            previous.GreenGasGiants,
            remote.GreenGasGiantsVersion,
            sources["Green Gas Giant criteria"]);
        var currentNicknames = SystemNicknameCatalog.Load(root);
        var updateNicknames = remote.NicknamesVersion > previous.Nicknames
            || currentNicknames.RavenCount == 0;
        var updated = new List<string>();
        if (!updateCodex
            && !updateRegionalCodexCandidates
            && !updateBiology
            && !updateGuardianTemplates
            && !updateGuardian
            && !updateSettlements
            && !updateGreenGasGiants
            && !updateNicknames)
        {
            return new PublishedReferenceUpdateResult(
                previous,
                previous,
                updated,
                warnings,
                null);
        }

        var operationId = Guid.NewGuid().ToString("N");
        var stageRoot = Path.Combine(root, $".reference-update-{operationId}");
        var rollbackRoot = Path.Combine(root, $".reference-rollback-{operationId}");
        var backupRoot = Path.Combine(
            root,
            "reference-backups",
            $"{timeProvider.GetUtcNow():yyyyMMddTHHmmssZ}-{operationId}");
        EnsureChild(root, stageRoot);
        EnsureChild(root, rollbackRoot);
        EnsureChild(root, backupRoot);
        Directory.CreateDirectory(stageRoot);
        var activationCompleted = false;

        try
        {
            await CopyCurrentReferencesAsync(root, stageRoot, cancellationToken)
                .ConfigureAwait(false);
            if (updateCodex)
            {
                await WriteDownloadAsync(
                        uris.CodexReference,
                        Path.Combine(stageRoot, "codexRef.json"),
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add("Codex reference");
            }

            if (updateRegionalCodexCandidates)
            {
                var bytes = await DownloadAsync(
                        uris.RegionalCodexCandidatesCsv,
                        cancellationToken)
                    .ConfigureAwait(false);
                var references = LegacyReferenceCatalogLoader.Load(stageRoot)
                    .Exobiology;
                var regional = RegionalCodexCandidateCatalog.ParsePublishedCsv(
                    bytes,
                    references);
                await File.WriteAllTextAsync(
                        Path.Combine(stageRoot, RegionalCodexCandidateCatalog.LegacyFileName),
                        regional.SerializeLegacy(),
                        cancellationToken)
                    .ConfigureAwait(false);
                File.SetLastWriteTimeUtc(
                    Path.Combine(stageRoot, RegionalCodexCandidateCatalog.LegacyFileName),
                    timeProvider.GetUtcNow().UtcDateTime);
                updated.Add("regional Codex candidates");
            }

            var stagePublished = Path.Combine(stageRoot, "pub");
            Directory.CreateDirectory(stagePublished);
            if (updateBiology)
            {
                var bytes = await DownloadAsync(
                        uris.BiologyCriteriaArchive,
                        cancellationToken)
                    .ConfigureAwait(false);
                var destination = Path.Combine(stagePublished, "bio-criteria");
                RecreateDirectory(stageRoot, destination);
                ExtractArchive(bytes, destination, ".json");
                await File.WriteAllBytesAsync(
                        Path.Combine(stagePublished, "bio-criteria.zip"),
                        bytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add("biology criteria");
            }

            if (updateGuardianTemplates)
            {
                await WriteDownloadAsync(
                        uris.GuardianTemplates,
                        Path.Combine(stagePublished, "guardianSiteTemplates.json"),
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add("Guardian site templates");
            }

            if (updateGuardian)
            {
                await WriteDownloadAsync(
                        uris.GuardianRuins,
                        Path.Combine(stagePublished, "allRuins.json"),
                        cancellationToken)
                    .ConfigureAwait(false);
                await WriteDownloadAsync(
                        uris.GuardianStructures,
                        Path.Combine(stagePublished, "allStructures.json"),
                        cancellationToken)
                    .ConfigureAwait(false);
                var bytes = await DownloadAsync(
                        uris.GuardianSurveyArchive,
                        cancellationToken)
                    .ConfigureAwait(false);
                _ = ValidateArchive(bytes, ".json");
                await File.WriteAllBytesAsync(
                        Path.Combine(stagePublished, "guardian.zip"),
                        bytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add("Guardian site indexes and surveys");
            }

            if (updateSettlements)
            {
                var bytes = await DownloadAsync(
                        uris.HumanSettlementsArchive,
                        cancellationToken)
                    .ConfigureAwait(false);
                var destination = Path.Combine(stagePublished, "settlements");
                RecreateDirectory(stageRoot, destination);
                ExtractArchive(bytes, destination, ".json", ".png");
                await File.WriteAllBytesAsync(
                        Path.Combine(stagePublished, "settlements.zip"),
                        bytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add("human settlement templates");
            }

            if (updateGreenGasGiants)
            {
                await WriteDownloadAsync(
                        uris.GreenGasGiants,
                        Path.Combine(stagePublished, "ggg.json"),
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add("Green Gas Giant criteria");
            }

            if (updateNicknames)
            {
                var bytes = await DownloadAsync(uris.RavenNicknames, cancellationToken)
                    .ConfigureAwait(false);
                var nicknameMap = ParseNicknameMap(bytes);
                await File.WriteAllTextAsync(
                        Path.Combine(stagePublished, "nicknames.json"),
                        JsonSerializer.Serialize(
                            nicknameMap,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true,
                            }),
                        cancellationToken)
                    .ConfigureAwait(false);
                updated.Add("Raven system nicknames");
            }

            var next = new PublishedReferenceVersions(
                updateCodex
                    ? Math.Max(previous.CodexReference, remote.CodexReferenceVersion)
                    : previous.CodexReference,
                updateBiology
                    ? Math.Max(previous.BiologyCriteria, remote.BiologyCriteriaVersion)
                    : previous.BiologyCriteria,
                updateBiology
                    ? remote.BiologyEngineVersion
                    : previous.BiologyEngine,
                updateGuardianTemplates
                    ? Math.Max(
                        previous.SettlementTemplate,
                        remote.SettlementTemplateVersion)
                    : previous.SettlementTemplate,
                updateGuardian
                    ? Math.Max(previous.Guardian, remote.GuardianVersion)
                    : previous.Guardian,
                updateSettlements
                    ? Math.Max(previous.Settlements, remote.SettlementsVersion)
                    : previous.Settlements,
                updateNicknames
                    ? Math.Max(previous.Nicknames, remote.NicknamesVersion)
                    : previous.Nicknames,
                updateGreenGasGiants
                    ? Math.Max(
                        previous.GreenGasGiants,
                        remote.GreenGasGiantsVersion)
                    : previous.GreenGasGiants);
            await versionStore.WriteAsync(
                    stagePublished,
                    next,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateCandidate(stageRoot, updated);
            await CopyCurrentReferencesAsync(root, backupRoot, cancellationToken)
                .ConfigureAwait(false);
            checkpoint?.Invoke(PublishedReferenceUpdateCheckpoint.BackupVerified);
            await ActivateAsync(
                    root,
                    stageRoot,
                    rollbackRoot,
                    updated,
                    cancellationToken)
                .ConfigureAwait(false);
            activationCompleted = true;
            var retainedBackup = Directory.EnumerateFileSystemEntries(
                    backupRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Any()
                    ? backupRoot
                    : null;
            return new PublishedReferenceUpdateResult(
                previous,
                next,
                updated,
                warnings,
                retainedBackup);
        }
        finally
        {
            DeleteDirectoryIfExists(root, stageRoot);
            if (activationCompleted
                || !Directory.Exists(rollbackRoot)
                || !Directory.EnumerateFileSystemEntries(
                    rollbackRoot,
                    "*",
                    SearchOption.AllDirectories).Any())
            {
                DeleteDirectoryIfExists(root, rollbackRoot);
            }
            if (Directory.Exists(backupRoot)
                && !Directory.EnumerateFileSystemEntries(
                    backupRoot,
                    "*",
                    SearchOption.AllDirectories).Any())
            {
                DeleteDirectoryIfExists(root, backupRoot);
            }
        }
    }

    private async Task ActivateAsync(
        string root,
        string stageRoot,
        string rollbackRoot,
        IReadOnlyCollection<string> updated,
        CancellationToken cancellationToken)
    {
        var livePublished = Path.Combine(root, "pub");
        var liveCodex = Path.Combine(root, "codexRef.json");
        var liveRegionalCodex = Path.Combine(
            root,
            RegionalCodexCandidateCatalog.LegacyFileName);
        var stagePublished = Path.Combine(stageRoot, "pub");
        var stageCodex = Path.Combine(stageRoot, "codexRef.json");
        var stageRegionalCodex = Path.Combine(
            stageRoot,
            RegionalCodexCandidateCatalog.LegacyFileName);
        var rollbackPublished = Path.Combine(rollbackRoot, "pub");
        var rollbackCodex = Path.Combine(rollbackRoot, "codexRef.json");
        var rollbackRegionalCodex = Path.Combine(
            rollbackRoot,
            RegionalCodexCandidateCatalog.LegacyFileName);
        Directory.CreateDirectory(rollbackRoot);
        var publishedMoved = false;
        var codexMoved = false;
        var regionalCodexMoved = false;
        var candidatePublishedActivated = false;
        var candidateCodexActivated = false;
        var candidateRegionalCodexActivated = false;
        try
        {
            if (Directory.Exists(livePublished))
            {
                Directory.Move(livePublished, rollbackPublished);
                publishedMoved = true;
            }

            if (File.Exists(liveCodex))
            {
                File.Move(liveCodex, rollbackCodex);
                codexMoved = true;
            }

            if (File.Exists(liveRegionalCodex))
            {
                File.Move(liveRegionalCodex, rollbackRegionalCodex);
                regionalCodexMoved = true;
            }

            checkpoint?.Invoke(
                PublishedReferenceUpdateCheckpoint.ExistingReferencesMoved);
            Directory.Move(stagePublished, livePublished);
            candidatePublishedActivated = true;
            if (File.Exists(stageCodex))
            {
                File.Move(stageCodex, liveCodex);
                candidateCodexActivated = true;
            }

            if (File.Exists(stageRegionalCodex))
            {
                File.Move(stageRegionalCodex, liveRegionalCodex);
                candidateRegionalCodexActivated = true;
            }

            checkpoint?.Invoke(PublishedReferenceUpdateCheckpoint.CandidateActivated);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCandidate(root, updated);
        }
        catch
        {
            if (candidatePublishedActivated && Directory.Exists(livePublished))
            {
                Directory.Move(
                    livePublished,
                    Path.Combine(stageRoot, "failed-pub"));
            }

            if (candidateCodexActivated && File.Exists(liveCodex))
            {
                File.Move(
                    liveCodex,
                    Path.Combine(stageRoot, "failed-codexRef.json"));
            }

            if (candidateRegionalCodexActivated
                && File.Exists(liveRegionalCodex))
            {
                File.Move(
                    liveRegionalCodex,
                    Path.Combine(stageRoot, "failed-codexNotFound.json"));
            }

            if (publishedMoved && Directory.Exists(rollbackPublished))
            {
                Directory.Move(rollbackPublished, livePublished);
            }

            if (codexMoved && File.Exists(rollbackCodex))
            {
                File.Move(rollbackCodex, liveCodex);
            }

            if (regionalCodexMoved && File.Exists(rollbackRegionalCodex))
            {
                File.Move(rollbackRegionalCodex, liveRegionalCodex);
            }

            throw;
        }
    }

    private static bool NeedsUpdate(
        int currentVersion,
        int remoteVersion,
        ReferenceCatalogSource source)
    {
        return remoteVersion > currentVersion || !source.IsLocal;
    }

    private static bool RegionalCodexCandidatesNeedUpdate(
        string root,
        RegionalCodexCandidateCatalog catalog,
        DateTimeOffset now)
    {
        var path = Path.Combine(
            root,
            RegionalCodexCandidateCatalog.LegacyFileName);
        if (!catalog.HasData
            || catalog.Warnings.Count > 0
            || !File.Exists(path))
        {
            return true;
        }

        var lastWrite = File.GetLastWriteTimeUtc(path);
        return lastWrite > now.UtcDateTime.AddMinutes(5)
            || now.UtcDateTime - lastWrite >= TimeSpan.FromDays(7);
    }

    private static void ValidateCandidate(
        string candidateRoot,
        IReadOnlyCollection<string> updated)
    {
        var result = LegacyReferenceCatalogLoader.Load(candidateRoot);
        var expectedSources = updated.SelectMany(name => name switch
        {
            "Guardian site indexes and surveys" =>
                new[] { "Guardian site index", "Guardian published surveys" },
            _ => new[] { name },
        }).ToHashSet(StringComparer.Ordinal);
        foreach (var catalogName in expectedSources)
        {
            if (catalogName == "regional Codex candidates")
            {
                var regional = RegionalCodexCandidateCatalog.Load(candidateRoot);
                if (!regional.HasData || regional.Warnings.Count > 0)
                {
                    throw new InvalidDataException(
                        regional.Warnings.FirstOrDefault()
                            ?? "The staged regional Codex candidate catalog is empty.");
                }

                continue;
            }

            if (catalogName == "Raven system nicknames")
            {
                var nicknames = SystemNicknameCatalog.Load(candidateRoot);
                if (nicknames.RavenCount == 0 || nicknames.Warnings.Count > 0)
                {
                    throw new InvalidDataException(
                        nicknames.Warnings.FirstOrDefault()
                            ?? "The staged Raven nickname catalog is empty.");
                }

                continue;
            }

            var source = result.Sources.Single(candidate => string.Equals(
                candidate.Catalog,
                catalogName,
                StringComparison.Ordinal));
            if (!source.IsLocal)
            {
                throw new InvalidDataException(
                    source.Warning
                        ?? $"The staged {catalogName} did not become active.");
            }
        }
    }

    private async Task WriteDownloadAsync(
        Uri uri,
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await DownloadAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken)
            .ConfigureAwait(false);
    }

    private static SortedDictionary<string, string> ParseNicknameMap(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The Raven nickname response is not a JSON array.");
            }

            var result = new SortedDictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !row.TryGetProperty("name", out var nameValue)
                    || nameValue.ValueKind != JsonValueKind.String
                    || !row.TryGetProperty("nickname", out var nicknameValue)
                    || nicknameValue.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        "The Raven nickname response contains an invalid row.");
                }

                var name = nameValue.GetString()?.Trim();
                var nickname = nicknameValue.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(nickname))
                {
                    throw new InvalidDataException(
                        "The Raven nickname response contains a blank name or nickname.");
                }

                result[name] = nickname;
            }

            if (result.Count == 0)
            {
                throw new InvalidDataException(
                    "The Raven nickname response contains no nicknames.");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The Raven nickname response is not valid JSON.",
                exception);
        }
    }

    private async Task<byte[]> DownloadAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            throw new InvalidDataException(
                $"Published reference download exceeded {MaximumDownloadBytes:N0} bytes: {uri}");
        }

        await using var input = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        await using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaximumDownloadBytes)
            {
                throw new InvalidDataException(
                    $"Published reference download exceeded {MaximumDownloadBytes:N0} bytes: {uri}");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (output.Length == 0)
        {
            throw new InvalidDataException(
                $"Published reference download was empty: {uri}");
        }

        return output.ToArray();
    }

    private static IReadOnlyList<ArchiveEntryPayload> ValidateArchive(
        byte[] bytes,
        params string[] allowedExtensions)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException(
                "The published reference archive contains too many entries.");
        }

        var result = new List<ArchiveEntryPayload>();
        long expandedLength = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var normalized = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(normalized)
                || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                throw new InvalidDataException(
                    $"The published reference archive has an unsafe path: {entry.FullName}");
            }

            if (!allowedExtensions.Contains(
                    Path.GetExtension(entry.Name),
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The published reference archive has an unexpected file: {entry.FullName}");
            }

            expandedLength += entry.Length;
            if (expandedLength > MaximumExpandedArchiveBytes)
            {
                throw new InvalidDataException(
                    "The expanded published reference archive exceeds the safety limit.");
            }

            using var entryStream = entry.Open();
            using var payload = new MemoryStream();
            entryStream.CopyTo(payload);
            if (payload.Length != entry.Length)
            {
                throw new InvalidDataException(
                    $"The published reference archive entry was incomplete: {entry.FullName}");
            }

            result.Add(new ArchiveEntryPayload(normalized, payload.ToArray()));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException(
                "The published reference archive contains no supported files.");
        }

        return result;
    }

    private static void ExtractArchive(
        byte[] bytes,
        string destination,
        params string[] allowedExtensions)
    {
        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        foreach (var entry in ValidateArchive(bytes, allowedExtensions))
        {
            var path = Path.GetFullPath(Path.Combine(
                root,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsureChild(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, entry.Bytes);
        }
    }

    private static async Task CopyCurrentReferencesAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationRoot);
        var sourceCodex = Path.Combine(sourceRoot, "codexRef.json");
        if (File.Exists(sourceCodex))
        {
            await CopyFileVerifiedAsync(
                    sourceCodex,
                    Path.Combine(destinationRoot, "codexRef.json"),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var sourceRegionalCodex = Path.Combine(
            sourceRoot,
            RegionalCodexCandidateCatalog.LegacyFileName);
        if (File.Exists(sourceRegionalCodex))
        {
            await CopyFileVerifiedAsync(
                    sourceRegionalCodex,
                    Path.Combine(
                        destinationRoot,
                        RegionalCodexCandidateCatalog.LegacyFileName),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var sourcePublished = Path.Combine(sourceRoot, "pub");
        if (Directory.Exists(sourcePublished))
        {
            await CopyDirectoryVerifiedAsync(
                    sourcePublished,
                    Path.Combine(destinationRoot, "pub"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task CopyDirectoryVerifiedAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(source);
        if ((new DirectoryInfo(sourceRoot).Attributes
                & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Published reference updates do not follow linked directories: {sourceRoot}");
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(
                     sourceRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((new DirectoryInfo(directory).Attributes
                    & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Published reference updates do not follow linked directories: {directory}");
            }

            Directory.CreateDirectory(Path.Combine(
                destination,
                Path.GetRelativePath(sourceRoot, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(
                     sourceRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((new FileInfo(file).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Published reference updates do not follow linked files: {file}");
            }

            await CopyFileVerifiedAsync(
                    file,
                    Path.Combine(destination, Path.GetRelativePath(sourceRoot, file)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task CopyFileVerifiedAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        var sourceHash = await ComputeHashAsync(source, cancellationToken)
            .ConfigureAwait(false);
        var destinationHash = await ComputeHashAsync(destination, cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
        {
            throw new InvalidDataException(
                $"Published reference backup verification failed: {source}");
        }
    }

    private static async Task<byte[]> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void RecreateDirectory(string operationRoot, string path)
    {
        EnsureChild(operationRoot, path);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void DeleteDirectoryIfExists(string root, string path)
    {
        EnsureChild(root, path);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void EnsureChild(string root, string path)
    {
        var resolvedRoot = Path.GetFullPath(root);
        var resolvedPath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(resolvedRoot)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolvedPath.StartsWith(prefix, comparison))
        {
            throw new InvalidDataException(
                $"Published reference path escapes its operation root: {path}");
        }
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }

    private sealed record ArchiveEntryPayload(string RelativePath, byte[] Bytes);
}
