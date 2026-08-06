using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;

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
    Uri KnownSystemAddresses,
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
        WellKnownUris.PublishedCodexReference,
        WellKnownUris.PublishedRegionalCodexCandidatesCsv,
        WellKnownUris.PublishedKnownSystemAddresses,
        WellKnownUris.PublishedBiologyCriteriaArchive,
        WellKnownUris.PublishedGuardianTemplates,
        WellKnownUris.PublishedGuardianRuins,
        WellKnownUris.PublishedGuardianStructures,
        WellKnownUris.PublishedGuardianSurveyArchive,
        WellKnownUris.PublishedHumanSettlementsArchive,
        WellKnownUris.PublishedGreenGasGiants,
        WellKnownUris.PublishedRavenNicknames);
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
    private const string CodexReferenceFileName = "codexRef.json";
    private const long MaximumDownloadBytes = 32L * 1024 * 1024;
    private const long MaximumExpandedArchiveBytes = 128L * 1024 * 1024;
    private const int MaximumArchiveEntries = 2_048;

    private static readonly string[] GuardianSiteSourceCatalogs =
    [
        "Guardian site index",
        "Guardian published surveys",
    ];

    private static readonly HttpClient SharedClient = CreateSharedClient();
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

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
        var plan = EvaluateUpdatePlan(root, previous, remote);
        var warnings = plan.Warnings;
        var updated = new List<string>();
        if (!plan.HasAnyUpdate)
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
            var stagePublished = await StageCatalogUpdatesAsync(
                    plan,
                    stageRoot,
                    updated,
                    cancellationToken)
                .ConfigureAwait(false);
            var next = BuildNextVersions(previous, remote, plan);
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
            CleanupUpdateDirectories(
                root,
                stageRoot,
                rollbackRoot,
                backupRoot,
                activationCompleted);
        }
    }

    private async Task<string> StageCatalogUpdatesAsync(
        UpdatePlan plan,
        string stageRoot,
        List<string> updated,
        CancellationToken cancellationToken)
    {
        if (plan.UpdateCodex)
        {
            await WriteDownloadAsync(
                    uris.CodexReference,
                    Path.Combine(stageRoot, CodexReferenceFileName),
                    cancellationToken)
                .ConfigureAwait(false);
            updated.Add("Codex reference");
        }

        if (plan.UpdateRegionalCodexCandidates)
        {
            await StageRegionalCodexCandidatesAsync(stageRoot, cancellationToken)
                .ConfigureAwait(false);
            updated.Add("regional Codex candidates");
        }

        var stagePublished = Path.Combine(stageRoot, "pub");
        Directory.CreateDirectory(stagePublished);
        await StagePublishedCatalogsAsync(
                plan,
                stageRoot,
                stagePublished,
                updated,
                cancellationToken)
            .ConfigureAwait(false);
        return stagePublished;
    }

    private async Task StageRegionalCodexCandidatesAsync(
        string stageRoot,
        CancellationToken cancellationToken)
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
    }

    private async Task StagePublishedCatalogsAsync(
        UpdatePlan plan,
        string stageRoot,
        string stagePublished,
        List<string> updated,
        CancellationToken cancellationToken)
    {
        if (plan.UpdateKnownSystemAddresses)
        {
            await WriteDownloadAsync(
                    uris.KnownSystemAddresses,
                    Path.Combine(
                        stagePublished,
                        KnownSystemAddressCatalog.LegacyFileName),
                    cancellationToken)
                .ConfigureAwait(false);
            updated.Add("known system addresses");
        }

        if (plan.UpdateBiology)
        {
            await StageBiologyCriteriaAsync(stageRoot, stagePublished, cancellationToken)
                .ConfigureAwait(false);
            updated.Add("biology criteria");
        }

        if (plan.UpdateGuardianTemplates)
        {
            await WriteDownloadAsync(
                    uris.GuardianTemplates,
                    Path.Combine(stagePublished, "guardianSiteTemplates.json"),
                    cancellationToken)
                .ConfigureAwait(false);
            updated.Add("Guardian site templates");
        }

        if (plan.UpdateGuardian)
        {
            await StageGuardianCatalogsAsync(stagePublished, cancellationToken)
                .ConfigureAwait(false);
            updated.Add("Guardian site indexes and surveys");
        }

        if (plan.UpdateSettlements)
        {
            await StageSettlementsAsync(stageRoot, stagePublished, cancellationToken)
                .ConfigureAwait(false);
            updated.Add("human settlement templates");
        }

        if (plan.UpdateGreenGasGiants)
        {
            await WriteDownloadAsync(
                    uris.GreenGasGiants,
                    Path.Combine(stagePublished, "ggg.json"),
                    cancellationToken)
                .ConfigureAwait(false);
            updated.Add("Green Gas Giant criteria");
        }

        if (plan.UpdateNicknames)
        {
            await StageNicknamesAsync(stagePublished, cancellationToken)
                .ConfigureAwait(false);
            updated.Add("Raven system nicknames");
        }
    }

    private async Task StageBiologyCriteriaAsync(
        string stageRoot,
        string stagePublished,
        CancellationToken cancellationToken)
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
    }

    private async Task StageGuardianCatalogsAsync(
        string stagePublished,
        CancellationToken cancellationToken)
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
    }

    private async Task StageSettlementsAsync(
        string stageRoot,
        string stagePublished,
        CancellationToken cancellationToken)
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
    }

    private async Task StageNicknamesAsync(
        string stagePublished,
        CancellationToken cancellationToken)
    {
        var bytes = await DownloadAsync(uris.RavenNicknames, cancellationToken)
            .ConfigureAwait(false);
        var nicknameMap = ParseNicknameMap(bytes);
        await File.WriteAllTextAsync(
                Path.Combine(stagePublished, "nicknames.json"),
                JsonSerializer.Serialize(
                    nicknameMap,
                    IndentedJson),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static PublishedReferenceVersions BuildNextVersions(
        PublishedReferenceVersions previous,
        PublishedDataIndex remote,
        UpdatePlan plan)
    {
        return new PublishedReferenceVersions(
            plan.UpdateCodex
                ? Math.Max(previous.CodexReference, remote.CodexReferenceVersion)
                : previous.CodexReference,
            plan.UpdateBiology
                ? Math.Max(previous.BiologyCriteria, remote.BiologyCriteriaVersion)
                : previous.BiologyCriteria,
            plan.UpdateBiology
                ? remote.BiologyEngineVersion
                : previous.BiologyEngine,
            plan.UpdateGuardianTemplates
                ? Math.Max(
                    previous.SettlementTemplate,
                    remote.SettlementTemplateVersion)
                : previous.SettlementTemplate,
            plan.UpdateGuardian
                ? Math.Max(previous.Guardian, remote.GuardianVersion)
                : previous.Guardian,
            plan.UpdateSettlements
                ? Math.Max(previous.Settlements, remote.SettlementsVersion)
                : previous.Settlements,
            plan.UpdateNicknames
                ? Math.Max(previous.Nicknames, remote.NicknamesVersion)
                : previous.Nicknames,
            plan.UpdateGreenGasGiants
                ? Math.Max(
                    previous.GreenGasGiants,
                    remote.GreenGasGiantsVersion)
                : previous.GreenGasGiants);
    }

    private static void CleanupUpdateDirectories(
        string root,
        string stageRoot,
        string rollbackRoot,
        string backupRoot,
        bool activationCompleted)
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

    private async Task ActivateAsync(
        string root,
        string stageRoot,
        string rollbackRoot,
        IReadOnlyCollection<string> updated,
        CancellationToken cancellationToken)
    {
        var paths = CreateActivationPaths(root, stageRoot, rollbackRoot);
        Directory.CreateDirectory(rollbackRoot);
        var moved = new ActivationMoveState();
        try
        {
            MoveExistingReferences(paths, moved);
            checkpoint?.Invoke(
                PublishedReferenceUpdateCheckpoint.ExistingReferencesMoved);
            ActivateCandidateReferences(paths, moved);
            checkpoint?.Invoke(PublishedReferenceUpdateCheckpoint.CandidateActivated);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCandidate(root, updated);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch
        {
            RollbackActivation(paths, moved, stageRoot);
            throw;
        }
    }

    private static ActivationPaths CreateActivationPaths(
        string root,
        string stageRoot,
        string rollbackRoot)
    {
        return new ActivationPaths(
            Path.Combine(root, "pub"),
            Path.Combine(root, CodexReferenceFileName),
            Path.Combine(root, RegionalCodexCandidateCatalog.LegacyFileName),
            Path.Combine(stageRoot, "pub"),
            Path.Combine(stageRoot, CodexReferenceFileName),
            Path.Combine(stageRoot, RegionalCodexCandidateCatalog.LegacyFileName),
            Path.Combine(rollbackRoot, "pub"),
            Path.Combine(rollbackRoot, CodexReferenceFileName),
            Path.Combine(rollbackRoot, RegionalCodexCandidateCatalog.LegacyFileName));
    }

    private static void MoveExistingReferences(
        ActivationPaths paths,
        ActivationMoveState moved)
    {
        if (Directory.Exists(paths.LivePublished))
        {
            Directory.Move(paths.LivePublished, paths.RollbackPublished);
            moved.PublishedMoved = true;
        }

        if (File.Exists(paths.LiveCodex))
        {
            File.Move(paths.LiveCodex, paths.RollbackCodex);
            moved.CodexMoved = true;
        }

        if (File.Exists(paths.LiveRegionalCodex))
        {
            File.Move(paths.LiveRegionalCodex, paths.RollbackRegionalCodex);
            moved.RegionalCodexMoved = true;
        }
    }

    private static void ActivateCandidateReferences(
        ActivationPaths paths,
        ActivationMoveState moved)
    {
        Directory.Move(paths.StagePublished, paths.LivePublished);
        moved.CandidatePublishedActivated = true;
        if (File.Exists(paths.StageCodex))
        {
            File.Move(paths.StageCodex, paths.LiveCodex);
            moved.CandidateCodexActivated = true;
        }

        if (File.Exists(paths.StageRegionalCodex))
        {
            File.Move(paths.StageRegionalCodex, paths.LiveRegionalCodex);
            moved.CandidateRegionalCodexActivated = true;
        }
    }

    private static void RollbackActivation(
        ActivationPaths paths,
        ActivationMoveState moved,
        string stageRoot)
    {
        if (moved.CandidatePublishedActivated && Directory.Exists(paths.LivePublished))
        {
            Directory.Move(
                paths.LivePublished,
                Path.Combine(stageRoot, "failed-pub"));
        }

        if (moved.CandidateCodexActivated && File.Exists(paths.LiveCodex))
        {
            File.Move(
                paths.LiveCodex,
                Path.Combine(stageRoot, "failed-codexRef.json"));
        }

        if (moved.CandidateRegionalCodexActivated
            && File.Exists(paths.LiveRegionalCodex))
        {
            File.Move(
                paths.LiveRegionalCodex,
                Path.Combine(stageRoot, "failed-codexNotFound.json"));
        }

        if (moved.PublishedMoved && Directory.Exists(paths.RollbackPublished))
        {
            Directory.Move(paths.RollbackPublished, paths.LivePublished);
        }

        if (moved.CodexMoved && File.Exists(paths.RollbackCodex))
        {
            File.Move(paths.RollbackCodex, paths.LiveCodex);
        }

        if (moved.RegionalCodexMoved && File.Exists(paths.RollbackRegionalCodex))
        {
            File.Move(paths.RollbackRegionalCodex, paths.LiveRegionalCodex);
        }
    }

    private sealed record ActivationPaths(
        string LivePublished,
        string LiveCodex,
        string LiveRegionalCodex,
        string StagePublished,
        string StageCodex,
        string StageRegionalCodex,
        string RollbackPublished,
        string RollbackCodex,
        string RollbackRegionalCodex);

    private sealed class ActivationMoveState
    {
        public bool PublishedMoved { get; set; }
        public bool CodexMoved { get; set; }
        public bool RegionalCodexMoved { get; set; }
        public bool CandidatePublishedActivated { get; set; }
        public bool CandidateCodexActivated { get; set; }
        public bool CandidateRegionalCodexActivated { get; set; }
    }

    private static bool NeedsUpdate(
        int currentVersion,
        int remoteVersion,
        ReferenceCatalogSource source)
    {
        return remoteVersion > currentVersion || !source.IsLocal;
    }

    private UpdatePlan EvaluateUpdatePlan(
        string root,
        PublishedReferenceVersions previous,
        PublishedDataIndex remote)
    {
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
        var knownSystemAddresses = KnownSystemAddressCatalog.Load(root);
        var updateKnownSystemAddresses = !knownSystemAddresses.HasData
            || knownSystemAddresses.Warnings.Count > 0;
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
        return new UpdatePlan(
            updateCodex,
            updateRegionalCodexCandidates,
            updateKnownSystemAddresses,
            updateBiology,
            updateGuardianTemplates,
            updateGuardian,
            updateSettlements,
            updateGreenGasGiants,
            updateNicknames,
            warnings);
    }

    private sealed record UpdatePlan(
        bool UpdateCodex,
        bool UpdateRegionalCodexCandidates,
        bool UpdateKnownSystemAddresses,
        bool UpdateBiology,
        bool UpdateGuardianTemplates,
        bool UpdateGuardian,
        bool UpdateSettlements,
        bool UpdateGreenGasGiants,
        bool UpdateNicknames,
        List<string> Warnings)
    {
        public bool HasAnyUpdate =>
            UpdateCodex
            || UpdateRegionalCodexCandidates
            || UpdateKnownSystemAddresses
            || UpdateBiology
            || UpdateGuardianTemplates
            || UpdateGuardian
            || UpdateSettlements
            || UpdateGreenGasGiants
            || UpdateNicknames;
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
                GuardianSiteSourceCatalogs,
            _ => new[] { name },
        }).ToHashSet(StringComparer.Ordinal);
        foreach (var catalogName in expectedSources)
        {
            if (TryValidateSpecialCatalog(candidateRoot, catalogName))
            {
                continue;
            }

            ValidateLocalCatalogSource(result, catalogName);
        }
    }

    private static bool TryValidateSpecialCatalog(
        string candidateRoot,
        string catalogName)
    {
        if (catalogName == "regional Codex candidates")
        {
            var regional = RegionalCodexCandidateCatalog.Load(candidateRoot);
            EnsureCatalogReady(
                regional.HasData,
                regional.Warnings,
                "The staged regional Codex candidate catalog is empty.");
            return true;
        }

        if (catalogName == "Raven system nicknames")
        {
            var nicknames = SystemNicknameCatalog.Load(candidateRoot);
            EnsureCatalogReady(
                nicknames.RavenCount > 0,
                nicknames.Warnings,
                "The staged Raven nickname catalog is empty.");
            return true;
        }

        if (catalogName == "known system addresses")
        {
            var knownSystems = KnownSystemAddressCatalog.Load(candidateRoot);
            EnsureCatalogReady(
                knownSystems.HasData,
                knownSystems.Warnings,
                "The staged known-system address catalog is empty.");
            return true;
        }

        return false;
    }

    private static void EnsureCatalogReady(
        bool hasData,
        IReadOnlyList<string> warnings,
        string emptyMessage)
    {
        if (hasData && warnings.Count == 0)
        {
            return;
        }

        throw new InvalidDataException(
            warnings.Count > 0 ? warnings[0] : emptyMessage);
    }

    private static void ValidateLocalCatalogSource(
        LegacyReferenceCatalogLoadResult result,
        string catalogName)
    {
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

    private static List<ArchiveEntryPayload> ValidateArchive(
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

            if (TryReadArchiveEntry(
                entry,
                allowedExtensions,
                out var normalized,
                out var payload,
                out var warning))
            {
                expandedLength += payload!.Length;
                if (expandedLength > MaximumExpandedArchiveBytes)
                {
                    throw new InvalidDataException(
                        "The expanded published reference archive exceeds the safety limit.");
                }

                result.Add(new ArchiveEntryPayload(normalized!, payload));
            }
            else if (warning is not null)
            {
                throw new InvalidDataException(warning);
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException(
                "The published reference archive contains no supported files.");
        }

        return result;
    }

    private static bool TryReadArchiveEntry(
        ZipArchiveEntry entry,
        string[] allowedExtensions,
        out string? normalized,
        out byte[]? payload,
        out string? warning)
    {
        normalized = null;
        payload = null;
        warning = null;

        var path = entry.FullName.Replace('\\', '/');
        if (Path.IsPathRooted(path)
            || path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            warning = $"The published reference archive has an unsafe path: {entry.FullName}";
            return false;
        }

        if (string.Equals(path, "readme.md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!allowedExtensions.Contains(
                Path.GetExtension(entry.Name),
                StringComparer.OrdinalIgnoreCase))
        {
            warning = $"The published reference archive has an unexpected file: {entry.FullName}";
            return false;
        }

        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        if (buffer.Length != entry.Length)
        {
            warning = $"The published reference archive entry was incomplete: {entry.FullName}";
            return false;
        }

        normalized = path;
        payload = buffer.ToArray();
        return true;
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
        var sourceCodex = Path.Combine(sourceRoot, CodexReferenceFileName);
        if (File.Exists(sourceCodex))
        {
            await CopyFileVerifiedAsync(
                    sourceCodex,
                    Path.Combine(destinationRoot, CodexReferenceFileName),
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
