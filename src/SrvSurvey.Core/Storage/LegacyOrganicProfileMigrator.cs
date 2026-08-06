using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Storage;

public sealed class LegacyOrganicProfileMigrator
{
    private static readonly JsonObject EmptyOrganisms = [];

    private const string BioScansProperty = "bioScans";
    private const string AnalyzedProperty = "analyzed";
    private const string BioSignalCountProperty = "bioSignalCount";
    private const string BodyIdProperty = "bodyId";
    private const string BodyNameProperty = "bodyName";
    private const string BodiesProperty = "bodies";
    private const string CommanderProperty = "commander";
    private const string FirstFootFallProperty = "firstFootFall";
    private const string EntryIdProperty = "entryId";
    private const string FidProperty = "fid";
    private const string GenusProperty = "genus";
    private const string IdProperty = "id";
    private const string LocationLatitudeProperty = "lat";
    private const string LocationLongitudeProperty = "long";
    private const string LandableBodyProperty = "LandableBody";
    private const string LocationProperty = "location";
    private const string NameProperty = "name";
    private const string TypeProperty = "type";
    private const string LastTouchdownProperty = "lastTouchdown";
    private const string MigratedNonSystemDataOrganicsProperty = "migratedNonSystemDataOrganics";
    private const string MigratedScannedOrganicsInEntryIdProperty = "migratedScannedOrganicsInEntryId";
    private const string OdysseYPlatform = "odyssey";
    private const string OrganicsProperty = "organisms";
    private const string OrganicRewardsProperty = "organicRewards";
    private const string ScanStatusProperty = "status";
    private const string SystemNameProperty = "systemName";
    private const string RewardProperty = "reward";
    private const string ScannedBioEntryIdsProperty = "scannedBioEntryIds";
    private const string ScannedOrganicsProperty = "scannedOrganics";
    private const string SpeciesProperty = "species";
    private const string SystemAddressProperty = "systemAddress";
    private const string BodyProperty = "body";
    private const string GenusLocalizedProperty = "genusLocalized";
    private const string SpeciesLocalizedProperty = "speciesLocalized";
    private const string VariantLocalizedProperty = "variantLocalized";
    private const string UnknownType = "Unknown";
    private const string VariantProperty = "variant";
    private const string FirstVisitedProperty = "firstVisited";
    private const string LastVisitedProperty = "lastVisited";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string dataDirectory;
    private readonly ExobiologyReferenceCatalog catalog;
    private readonly LegacySystemDataFileStore systemFileStore;

    public LegacyOrganicProfileMigrator(
        string dataDirectory,
        ExobiologyReferenceCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.catalog = catalog ?? ExobiologyReferenceCatalog.LoadEmbedded();
        systemFileStore = new LegacySystemDataFileStore(this.dataDirectory);
    }

    public async Task<LegacyOrganicProfileMigrationResult> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(dataDirectory))
        {
            return LegacyOrganicProfileMigrationResult.NotRequired;
        }

        var errors = new List<string>();
        var migratedProfilePaths = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        var profilePaths = GetProfilePaths();
        var profilesByFrontierId = await MigrateCommanderProfilesAsync(
                profilePaths,
                errors,
                migratedProfilePaths,
                cancellationToken)
            .ConfigureAwait(false);
        var (migratedBodies, migratedScans, migratedOrganisms) =
            await MigrateOrganicBodiesAsync(
                    profilesByFrontierId,
                    errors,
                    migratedProfilePaths,
                    cancellationToken)
                .ConfigureAwait(false);

        return migratedProfilePaths.Count == 0
            && migratedBodies == 0
            && migratedScans == 0
            && migratedOrganisms == 0
            && errors.Count == 0
                ? LegacyOrganicProfileMigrationResult.NotRequired
                : new LegacyOrganicProfileMigrationResult(
                    migratedProfilePaths.Count,
                    migratedBodies,
                    migratedScans,
                    migratedOrganisms,
                    errors);
    }

    private string[] GetProfilePaths()
    {
        return Directory.EnumerateFiles(
                dataDirectory,
                "F*-*.json",
                SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(
                    "-live.json",
                    StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(
                    "-legacy.json",
                    StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<Dictionary<string, List<CommanderProfile>>> MigrateCommanderProfilesAsync(
        string[] profilePaths,
        List<string> errors,
        HashSet<string> migratedProfilePaths,
        CancellationToken cancellationToken)
    {
        var profilesByFrontierId = new Dictionary<
            string,
            List<CommanderProfile>>(StringComparer.OrdinalIgnoreCase);

        foreach (var profilePath in profilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureRegularFile(profilePath);
                var root = await ReadObjectAsync(profilePath, cancellationToken)
                    .ConfigureAwait(false);
                ValidateProfile(root);
                var frontierId = GetString(root, FidProperty)
                    ?? GetFrontierIdFromProfilePath(profilePath);
                if (string.IsNullOrWhiteSpace(frontierId))
                {
                    errors.Add(
                        $"{Path.GetFileName(profilePath)} has no Frontier ID; "
                            + "legacy organic claims were left unchanged.");
                    continue;
                }

                var changed = MigrateCommanderClaims(root);
                if (changed)
                {
                    await WriteObjectAsync(profilePath, root, cancellationToken)
                        .ConfigureAwait(false);
                    migratedProfilePaths.Add(profilePath);
                }

                if (!profilesByFrontierId.TryGetValue(
                        frontierId,
                        out var profiles))
                {
                    profiles = [];
                    profilesByFrontierId[frontierId] = profiles;
                }

                profiles.Add(new CommanderProfile(frontierId, profilePath, root));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or InvalidDataException)
            {
                errors.Add(
                    $"{Path.GetFileName(profilePath)} was not migrated: "
                        + exception.Message);
            }
        }

        return profilesByFrontierId;
    }

    private async Task<(int migratedBodies, int migratedScans, int migratedOrganisms)> MigrateOrganicBodiesAsync(
        IReadOnlyDictionary<string, List<CommanderProfile>> profilesByFrontierId,
        List<string> errors,
        HashSet<string> migratedProfilePaths,
        CancellationToken cancellationToken)
    {
        var organicRoot = Path.Combine(dataDirectory, "organic");
        if (!Directory.Exists(organicRoot))
        {
            return (0, 0, 0);
        }

        if (IsReparsePoint(organicRoot))
        {
            errors.Add(
                "The legacy organic directory is a symbolic link or junction; "
                    + "its files were preserved without conversion.");
            return (0, 0, 0);
        }

        var migratedBodies = 0;
        var migratedScans = 0;
        var migratedOrganisms = 0;

        foreach (var frontierDirectory in Directory.EnumerateDirectories(
                     organicRoot)
                     .Order(StringComparer.Ordinal))
        {
            var frontierId = Path.GetFileName(frontierDirectory);
            var migration = await MigrateFrontierDirectoryAsync(
                    frontierDirectory,
                    frontierId,
                    profilesByFrontierId.GetValueOrDefault(frontierId)
                        ?? [],
                    errors,
                    migratedProfilePaths,
                    cancellationToken)
                .ConfigureAwait(false);
            migratedBodies += migration.migratedBodies;
            migratedScans += migration.migratedScans;
            migratedOrganisms += migration.migratedOrganisms;
        }

        return (migratedBodies, migratedScans, migratedOrganisms);
    }

    private async Task<(int migratedBodies, int migratedScans, int migratedOrganisms)> MigrateFrontierDirectoryAsync(
        string frontierDirectory,
        string frontierId,
        IReadOnlyList<CommanderProfile> commanderProfiles,
        List<string> errors,
        HashSet<string> migratedProfilePaths,
        CancellationToken cancellationToken)
    {
        if (IsReparsePoint(frontierDirectory))
        {
            errors.Add(
                $"organic/{frontierId} is a symbolic link or junction; "
                    + "its files were preserved without conversion.");
            return (0, 0, 0);
        }

        if (AreCommanderProfilesAlreadyMigrated(commanderProfiles))
        {
            return (0, 0, 0);
        }

        var bodyErrorsBefore = errors.Count;
        var totals = await MigrateBodyFilesAsync(
                frontierDirectory,
                frontierId,
                commanderProfiles,
                errors,
                cancellationToken)
            .ConfigureAwait(false);
        if (commanderProfiles.Count > 0 && errors.Count == bodyErrorsBefore)
        {
            await MarkCommanderProfilesMigratedAsync(
                    commanderProfiles,
                    migratedProfilePaths,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return totals;
    }

    private static bool AreCommanderProfilesAlreadyMigrated(
        IReadOnlyList<CommanderProfile> commanderProfiles)
    {
        return commanderProfiles.Count > 0
            && commanderProfiles.All(profile => GetBoolean(
                profile.Root,
                MigratedNonSystemDataOrganicsProperty) == true);
    }

    private async Task<(int migratedBodies, int migratedScans, int migratedOrganisms)> MigrateBodyFilesAsync(
        string frontierDirectory,
        string frontierId,
        IReadOnlyList<CommanderProfile> commanderProfiles,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var migratedBodies = 0;
        var migratedScans = 0;
        var migratedOrganisms = 0;
        foreach (var bodyPath in Directory.EnumerateFiles(
                     frontierDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly)
                 .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await TryMigrateBodyFileAsync(
                    bodyPath,
                    frontierId,
                    commanderProfiles,
                    errors,
                    cancellationToken)
                .ConfigureAwait(false);
            migratedBodies += result.migratedBodies;
            migratedScans += result.migratedScans;
            migratedOrganisms += result.migratedOrganisms;
        }

        return (migratedBodies, migratedScans, migratedOrganisms);
    }

    private async Task<(int migratedBodies, int migratedScans, int migratedOrganisms)> TryMigrateBodyFileAsync(
        string bodyPath,
        string frontierId,
        IReadOnlyList<CommanderProfile> commanderProfiles,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureRegularFile(bodyPath);
            var source = await ReadObjectAsync(bodyPath, cancellationToken)
                .ConfigureAwait(false);
            var migration = await MigrateBodyAsync(
                    frontierId,
                    source,
                    commanderProfiles,
                    cancellationToken)
                .ConfigureAwait(false);
            return (
                migration.Changed ? 1 : 0,
                migration.ScanCount,
                migration.OrganismCount);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException
                or ArgumentException)
        {
            errors.Add(
                $"organic/{frontierId}/{Path.GetFileName(bodyPath)} "
                    + "was preserved but not converted: "
                    + exception.Message);
            return (0, 0, 0);
        }
    }

    private static async Task MarkCommanderProfilesMigratedAsync(
        IReadOnlyList<CommanderProfile> commanderProfiles,
        HashSet<string> migratedProfilePaths,
        CancellationToken cancellationToken)
    {
        foreach (var profile in commanderProfiles)
        {
            if (GetBoolean(profile.Root, MigratedNonSystemDataOrganicsProperty) == true)
            {
                continue;
            }

            profile.Root[MigratedNonSystemDataOrganicsProperty] = true;
            await WriteObjectAsync(profile.Path, profile.Root, cancellationToken)
                .ConfigureAwait(false);
            migratedProfilePaths.Add(profile.Path);
        }
    }

    private bool MigrateCommanderClaims(JsonObject root)
    {
        var entries = ReadClaimEntries(root);
        var scannedOrganics = RequireOptionalArray(root, ScannedOrganicsProperty);
        if (entries.Count == 0 && scannedOrganics is not { Count: > 0 })
        {
            return false;
        }

        var migrated = BuildMigratedClaims(entries, scannedOrganics);
        if (ClaimsAlreadyMigrated(root, entries, migrated))
        {
            return false;
        }

        root[ScannedBioEntryIdsProperty] = new JsonArray(
            migrated.Select(value => JsonValue.Create(value)).ToArray());
        root[OrganicRewardsProperty] = CalculateClaimRewards(migrated);
        root[MigratedScannedOrganicsInEntryIdProperty] = true;
        return true;
    }

    private static JsonArray? RequireOptionalArray(JsonObject root, string propertyName)
    {
        if (root[propertyName] is { } node && node is not JsonArray)
        {
            throw new InvalidDataException(
                $"The legacy {propertyName} value is not an array.");
        }

        return root[propertyName] as JsonArray;
    }

    private List<string> BuildMigratedClaims(
        List<string> entries,
        JsonArray? scannedOrganics)
    {
        var migrated = entries
            .Select(entry => NormalizeClaim(entry)
                ?? throw new InvalidDataException(
                    $"The legacy organic claim '{entry}' could not be converted safely."))
            .ToList();
        MergeScannedOrganicsClaims(migrated, scannedOrganics);
        migrated = migrated
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (!migrated.All(IsNormalizedClaim))
        {
            throw new InvalidDataException(
                "The converted legacy organic claims did not pass validation.");
        }

        return migrated;
    }

    private void MergeScannedOrganicsClaims(
        List<string> migrated,
        JsonArray? scannedOrganics)
    {
        if (scannedOrganics is null)
        {
            return;
        }

        foreach (var claim in CollectScannedOrganicsClaims(scannedOrganics)
            .Where(claim => !migrated.Any(existing => IsSameLegacyClaim(existing, claim))))
        {
            migrated.Add(claim);
        }
    }

    private static bool ClaimsAlreadyMigrated(
        JsonObject root,
        List<string> entries,
        List<string> migrated)
    {
        var current = entries
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return current.SequenceEqual(migrated, StringComparer.Ordinal)
            && GetBoolean(root, MigratedScannedOrganicsInEntryIdProperty) == true;
    }

    private static List<string> ReadClaimEntries(JsonObject root)
    {
        if (root[ScannedBioEntryIdsProperty] is { } claimNode
            && claimNode is not JsonArray)
        {
            throw new InvalidDataException(
                "The legacy scannedBioEntryIds value is not an array.");
        }

        var entries = new List<string>();
        if (root[ScannedBioEntryIdsProperty] is not JsonArray existing)
        {
            return entries;
        }

        foreach (var node in existing)
        {
            if (node is not JsonValue value
                || !value.TryGetValue<string>(out var text)
                || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidDataException(
                    "A legacy scannedBioEntryIds entry is not a non-empty string.");
            }

            entries.Add(text);
        }

        return entries;
    }

    private List<string> CollectScannedOrganicsClaims(JsonArray scannedOrganisms)
    {
        var claims = new List<string>();
        foreach (var node in scannedOrganisms)
        {
            if (node is not JsonObject scan)
            {
                throw new InvalidDataException(
                    "A legacy scannedOrganics entry is not an object.");
            }

            var claim = CreateLegacyClaim(scan)
                ?? throw new InvalidDataException(
                    "A legacy scannedOrganics entry is incomplete or has an unknown species.");
            claims.Add(claim);
        }

        return claims;
    }

    private static void ValidateProfile(JsonObject root)
    {
        EnsureOptionalString(root, FidProperty);
        EnsureOptionalString(root, CommanderProperty);
        EnsureOptionalInt64(root, OrganicRewardsProperty);
        EnsureOptionalBoolean(root, MigratedScannedOrganicsInEntryIdProperty);
        EnsureOptionalBoolean(root, MigratedNonSystemDataOrganicsProperty);
    }

    private string? NormalizeClaim(string value)
    {
        var parts = value.Split('_', StringSplitOptions.TrimEntries);
        if (!TryParseClaimIdentity(parts, out var entryId))
        {
            return null;
        }

        return parts.Length switch
        {
            3 => ExpandShortClaim(value, entryId),
            4 => ExpandRewardOnlyClaim(value, parts),
            5 => NormalizeFullClaim(parts),
            _ => null,
        };
    }

    private static bool TryParseClaimIdentity(string[] parts, out long entryId)
    {
        entryId = 0;
        return parts.Length is >= 3 and <= 5
            && long.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var systemAddress)
            && systemAddress > 0
            && int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bodyId)
            && bodyId >= 0
            && long.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out entryId)
            && entryId > 0;
    }

    private string? ExpandShortClaim(string value, long entryId)
    {
        var reference = FindByEntryIdOrPrefix(entryId.ToString(CultureInfo.InvariantCulture));
        return reference is null
            ? null
            : $"{value}_{reference.Reward}_{bool.FalseString}";
    }

    private static string? ExpandRewardOnlyClaim(string value, string[] parts)
    {
        if (!long.TryParse(
                parts[3],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var reward)
            || reward < 0)
        {
            return null;
        }

        return $"{value}_{bool.FalseString}";
    }

    private static string? NormalizeFullClaim(string[] parts)
    {
        if (!long.TryParse(
                parts[3],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var reward)
            || reward < 0)
        {
            return null;
        }

        return bool.TryParse(parts[4], out var firstFootfall)
            && (!firstFootfall || reward <= long.MaxValue / 5)
                ? $"{parts[0]}_{parts[1]}_{parts[2]}_{reward}_{firstFootfall}"
                : null;
    }

    private string? CreateLegacyClaim(JsonObject scan)
    {
        var systemAddress = GetInt64(scan, SystemAddressProperty);
        var bodyId = GetInt32(scan, BodyIdProperty);
        var species = GetString(scan, SpeciesProperty);
        var reward = GetInt64(scan, RewardProperty);
        var reference = catalog.FindBySpecies(species);
        if (systemAddress is not > 0
            || bodyId is null or < 0
            || reference is null
            || reward is null or < 0)
        {
            return null;
        }

        var entryId = string.Equals(
                reference.Platform,
                OdysseYPlatform,
                StringComparison.OrdinalIgnoreCase)
            ? reference.EntryIdPrefix + "00"
            : reference.EntryId.ToString(CultureInfo.InvariantCulture);
        return $"{systemAddress}_{bodyId}_{entryId}_{reward}_{bool.FalseString}";
    }

    private static bool IsSameLegacyClaim(string existing, string candidate)
    {
        var existingParts = existing.Split('_');
        var candidateParts = candidate.Split('_');
        return existingParts.Length >= 4
            && candidateParts.Length >= 4
            && existingParts[0] == candidateParts[0]
            && existingParts[1] == candidateParts[1]
            && existingParts[2].StartsWith(
                candidateParts[2][..Math.Min(5, candidateParts[2].Length)],
                StringComparison.Ordinal)
            && existingParts[3] == candidateParts[3];
    }

    private async Task<BodyMigrationResult> MigrateBodyAsync(
        string frontierId,
        JsonObject source,
        IReadOnlyList<CommanderProfile> commanderProfiles,
        CancellationToken cancellationToken)
    {
        var systemName = GetString(source, SystemNameProperty);
        var bodyName = GetString(source, BodyNameProperty);
        var systemAddress = GetInt64(source, SystemAddressProperty);
        var bodyId = GetInt32(source, BodyIdProperty);
        if (string.IsNullOrWhiteSpace(systemName)
            || string.IsNullOrWhiteSpace(bodyName)
            || systemAddress is not > 0
            || bodyId is null or < 0)
        {
            throw new InvalidDataException(
                "The legacy body identity is incomplete.");
        }

        var scanCount = 0;
        var organismCount = 0;
        EnsureSafeSystemTarget(frontierId);
        var changed = await systemFileStore.UpdateWithResultAsync(
                new LegacySystemDataFileContext(
                    frontierId,
                    GetString(source, CommanderProperty)
                        ?? commanderProfiles
                            .Select(profile => GetString(
                                profile.Root,
                                CommanderProperty))
                            .FirstOrDefault(name =>
                                !string.IsNullOrWhiteSpace(name)),
                    systemName,
                    systemAddress.Value,
                    null),
                root => MergeBody(
                    root,
                    source,
                    bodyName,
                    bodyId.Value,
                    commanderProfiles,
                    ref scanCount,
                    ref organismCount),
                cancellationToken)
            .ConfigureAwait(false);
        return new BodyMigrationResult(
            changed.Value,
            scanCount,
            organismCount);
    }

    private bool MergeBody(
        JsonObject root,
        JsonObject source,
        string bodyName,
        int bodyId,
        IReadOnlyList<CommanderProfile> commanderProfiles,
        ref int scanCount,
        ref int organismCount)
    {
        var changed = MergeVisitDates(root, source);
        var bodies = GetOrCreateArray(root, BodiesProperty, ref changed);
        ValidateBodyCollection(bodies);

        var body = GetOrCreateBody(
            bodies,
            bodyName,
            bodyId,
            ref changed);
        changed |= MergeLastTouchdown(body, source);
        changed |= MergeBioScans(
            body,
            source,
            commanderProfiles,
            ref scanCount);
        changed |= MergeOrganisms(body, source, ref organismCount);
        foreach (var commanderProfile in commanderProfiles)
        {
            RepairCommanderClaimsForBody(
                commanderProfile.Root,
                body,
                GetInt64(source, SystemAddressProperty)!.Value,
                bodyId);
        }

        return changed;
    }

    private static void ValidateBodyCollection(JsonArray bodies)
    {
        if (bodies.Any(node => node is not JsonObject))
        {
            throw new InvalidDataException(
                "The target system bodies array contains a non-object entry.");
        }
    }

    private static JsonObject GetOrCreateBody(
        JsonArray bodies,
        string bodyName,
        int bodyId,
        ref bool changed)
    {
        var body = FindBody(bodies, bodyName, bodyId);
        if (body is null)
        {
            body = new JsonObject
            {
                [NameProperty] = bodyName,
                [IdProperty] = bodyId,
                [TypeProperty] = LandableBodyProperty,
            };
            bodies.Add(body);
            changed = true;
            return body;
        }

        changed |= SetIfMissing(body, NameProperty, bodyName);
        changed |= SetIfMissing(body, IdProperty, bodyId);
        changed |= EnsureBodyType(body);
        return body;
    }

    private static JsonObject? FindBody(
        JsonArray bodies,
        string bodyName,
        int bodyId)
    {
        return bodies.OfType<JsonObject>().FirstOrDefault(candidate =>
                GetInt32(candidate, IdProperty) == bodyId)
            ?? bodies.OfType<JsonObject>().FirstOrDefault(candidate =>
                string.Equals(
                    GetString(candidate, NameProperty),
                    bodyName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool EnsureBodyType(JsonObject body)
    {
        if (body[TypeProperty] is not null
            && GetString(body, TypeProperty) is null)
        {
            throw new InvalidDataException(
                "The target system body type is not a string.");
        }

        if (body[TypeProperty] is null
            || string.IsNullOrWhiteSpace(GetString(body, TypeProperty))
            || string.Equals(
                GetString(body, TypeProperty),
                UnknownType,
                StringComparison.OrdinalIgnoreCase))
        {
            body[TypeProperty] = LandableBodyProperty;
            return true;
        }

        return false;
    }

    private static bool MergeLastTouchdown(
        JsonObject body,
        JsonObject source)
    {
        if (source[LastTouchdownProperty] is { } touchdownNode
            && touchdownNode is not JsonObject)
        {
            throw new InvalidDataException(
                "The legacy lastTouchdown value is not an object.");
        }

        if (body[LastTouchdownProperty] is null
            && source[LastTouchdownProperty] is JsonObject touchdown)
        {
            body[LastTouchdownProperty] = touchdown.DeepClone();
            return true;
        }

        return false;
    }

    private static void RepairCommanderClaimsForBody(
        JsonObject? commanderProfile,
        JsonObject body,
        long systemAddress,
        int bodyId)
    {
        if (commanderProfile?[ScannedBioEntryIdsProperty] is not JsonArray claimArray)
        {
            return;
        }

        var exactEntryIds = CollectExactEntryIds(body);
        var prefix = $"{systemAddress}_{bodyId}_";
        var firstFootfall = GetBoolean(body, FirstFootFallProperty) == true;
        var claims = claimArray
            .Select(node => node!.GetValue<string>())
            .ToArray();
        var changed = false;
        for (var index = 0; index < claims.Length; index++)
        {
            if (TryRepairClaim(claims, index, prefix, exactEntryIds, firstFootfall))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        commanderProfile[ScannedBioEntryIdsProperty] = new JsonArray(
            claims.Select(value => JsonValue.Create(value)).ToArray());
        commanderProfile[OrganicRewardsProperty] = CalculateClaimRewards(claims);
    }

    private static long[] CollectExactEntryIds(JsonObject body)
    {
        return body[OrganicsProperty] is JsonArray organisms
            ? organisms.OfType<JsonObject>()
                .Select(organism => GetInt64(organism, EntryIdProperty))
                .Where(entryId => entryId is > 0 && !IsWeakEntryId(entryId))
                .Select(entryId => entryId!.Value)
                .Distinct()
                .ToArray()
            : [];
    }

    private static bool TryRepairClaim(
        string[] claims,
        int index,
        string prefix,
        long[] exactEntryIds,
        bool firstFootfall)
    {
        if (!claims[index].StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = claims[index].Split('_');
        if (parts.Length != 5)
        {
            return false;
        }

        RepairWeakEntryId(parts, exactEntryIds);
        RepairFirstFootfall(parts, firstFootfall);
        var repaired = string.Join('_', parts);
        if (string.Equals(repaired, claims[index], StringComparison.Ordinal))
        {
            return false;
        }

        claims[index] = repaired;
        return true;
    }

    private static void RepairWeakEntryId(string[] parts, long[] exactEntryIds)
    {
        if (!long.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var claimedEntryId)
            || !IsWeakEntryId(claimedEntryId)
            || parts[2].Length < 5)
        {
            return;
        }

        var entryPrefix = parts[2][..5];
        var exactEntryId = exactEntryIds.FirstOrDefault(candidate =>
            candidate.ToString(CultureInfo.InvariantCulture).StartsWith(
                entryPrefix,
                StringComparison.Ordinal));
        if (exactEntryId > 0)
        {
            parts[2] = exactEntryId.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void RepairFirstFootfall(string[] parts, bool firstFootfall)
    {
        if (firstFootfall
            && bool.TryParse(parts[4], out var claimedFirstFootfall)
            && !claimedFirstFootfall)
        {
            parts[4] = bool.TrueString;
        }
    }

    private bool MergeBioScans(
        JsonObject body,
        JsonObject source,
        IReadOnlyList<CommanderProfile> commanderProfiles,
        ref int scanCount)
    {
        if (source[BioScansProperty] is { } scansNode
            && scansNode is not JsonArray)
        {
            throw new InvalidDataException(
                "The legacy bioScans value is not an array.");
        }

        if (source[BioScansProperty] is not JsonArray sourceScans)
        {
            return false;
        }

        var changed = false;
        var targetScans = GetOrCreateArray(body, BioScansProperty, ref changed);
        if (targetScans.Any(node => node is not JsonObject))
        {
            throw new InvalidDataException(
                "The target bioScans array contains a non-object entry.");
        }

        foreach (var node in sourceScans)
        {
            if (node is not JsonObject sourceScan)
            {
                throw new InvalidDataException(
                    "A legacy bioScans entry is not an object.");
            }

            changed |= MergeBioScan(
                targetScans,
                sourceScan,
                source,
                commanderProfiles,
                ref scanCount);
        }

        return changed;
    }

    private bool MergeBioScan(
        JsonArray targetScans,
        JsonObject sourceScan,
        JsonObject source,
        IReadOnlyList<CommanderProfile> commanderProfiles,
        ref int scanCount)
    {
        ValidateLegacyBioScan(sourceScan);

        var normalized = sourceScan.DeepClone().AsObject();
        RepairBioScanEntryId(normalized, source, commanderProfiles);
        var existing = targetScans.OfType<JsonObject>().FirstOrDefault(scan =>
            IsSameBioScan(scan, normalized));
        if (existing is null)
        {
            targetScans.Add(normalized);
            scanCount++;
            return true;
        }

        if (!IsWeakEntryId(GetInt64(existing, EntryIdProperty))
            || IsWeakEntryId(GetInt64(normalized, EntryIdProperty)))
        {
            return false;
        }

        existing[EntryIdProperty] = normalized[EntryIdProperty]?.DeepClone();
        return true;
    }

    private bool MergeOrganisms(
        JsonObject body,
        JsonObject source,
        ref int organismCount)
    {
        if (source[OrganicsProperty] is { } organismsNode
            && organismsNode is not JsonObject)
        {
            throw new InvalidDataException(
                "The legacy organisms value is not an object.");
        }

        if (source[OrganicsProperty] is not JsonObject sourceOrganisms)
        {
            return false;
        }

        var changed = false;
        var target = GetOrCreateArray(body, OrganicsProperty, ref changed);
        EnsureOrganismArray(target);
        foreach (var pair in sourceOrganisms)
        {
            changed |= MergeOrganismEntry(
                pair.Key,
                pair.Value,
                target,
                ref organismCount);
        }

        changed |= UpdateBioSignalCount(body, target.Count);
        return changed;
    }

    private static void EnsureOrganismArray(JsonArray target)
    {
        if (target.Any(node => node is not JsonObject))
        {
            throw new InvalidDataException(
                "The target organisms array contains a non-object entry.");
        }
    }

    private bool MergeOrganismEntry(
        string key,
        JsonNode? value,
        JsonArray target,
        ref int organismCount)
    {
        if (value is not JsonObject sourceOrganism)
        {
            throw new InvalidDataException(
                $"The legacy organism '{key}' is not an object.");
        }

        ValidateLegacyOrganism(sourceOrganism);
        var (reference, genus) = ResolveOrganismMetadata(sourceOrganism);
        if (string.IsNullOrWhiteSpace(genus))
        {
            throw new InvalidDataException(
                $"The legacy organism '{key}' has no recoverable genus.");
        }

        var existing = FindExistingOrganism(target, sourceOrganism, genus);
        var changed = false;
        if (existing is null)
        {
            existing = new JsonObject { [GenusProperty] = genus };
            target.Add(existing);
            organismCount++;
            changed = true;
        }

        var organismChanged = FillOrganism(existing, sourceOrganism, reference);
        return changed || organismChanged;
    }

    private static bool UpdateBioSignalCount(JsonObject body, int organismCount)
    {
        if (body[BioSignalCountProperty] is not null
            && GetInt32(body, BioSignalCountProperty) is null)
        {
            throw new InvalidDataException(
                "The target bioSignalCount value is not an integer.");
        }

        var currentSignalCount = GetInt32(body, BioSignalCountProperty) ?? 0;
        if (organismCount <= currentSignalCount)
        {
            return false;
        }

        body[BioSignalCountProperty] = organismCount;
        return true;
    }

    private (ExobiologyReference? Reference, string? Genus) ResolveOrganismMetadata(
        JsonObject sourceOrganism)
    {
        var variant = GetString(sourceOrganism, VariantProperty);
        var species = GetString(sourceOrganism, SpeciesProperty);
        var reference = catalog.FindByVariant(variant)
            ?? catalog.FindBySpecies(species);
        var genus = GetString(sourceOrganism, GenusProperty)
            ?? (reference is null
                ? null
                : ExobiologyReferenceCatalog.GetGenusName(
                    reference.SpeciesName));
        return (reference, genus);
    }

    private static JsonObject? FindExistingOrganism(
        JsonArray target,
        JsonObject sourceOrganism,
        string? genus)
    {
        var variant = GetString(sourceOrganism, VariantProperty);
        if (!string.IsNullOrWhiteSpace(variant))
        {
            var existingByVariant = target.OfType<JsonObject>().FirstOrDefault(organism =>
                string.Equals(
                    GetString(organism, VariantProperty),
                    variant,
                    StringComparison.Ordinal));
            if (existingByVariant is not null)
            {
                return existingByVariant;
            }
        }

        return target.OfType<JsonObject>().FirstOrDefault(organism =>
            string.Equals(GetString(organism, GenusProperty), genus, StringComparison.Ordinal));
    }

    private static void ValidateLegacyBioScan(JsonObject scan)
    {
        foreach (var propertyName in new[]
                 {
                     GenusProperty,
                     SpeciesProperty,
                     ScanStatusProperty,
                     BodyProperty,
                 })
        {
            EnsureOptionalString(scan, propertyName);
        }

        EnsureOptionalInt64(scan, EntryIdProperty);
        if (scan["radius"] is not null && GetDouble(scan["radius"]) is null)
        {
            throw new InvalidDataException(
                "A legacy bioScans radius is not numeric.");
        }

        if (scan[LocationProperty] is not JsonObject location
            || GetDouble(location[LocationLatitudeProperty] ?? location["Lat"]) is null
            || GetDouble(location[LocationLongitudeProperty] ?? location["Long"]) is null)
        {
            throw new InvalidDataException(
                "A legacy bioScans location is incomplete or malformed.");
        }
    }

    private static void ValidateLegacyOrganism(JsonObject organism)
    {
        foreach (var propertyName in new[]
                 {
                     GenusProperty,
                     GenusLocalizedProperty,
                     SpeciesProperty,
                     SpeciesLocalizedProperty,
                     VariantProperty,
                     VariantLocalizedProperty,
                 })
        {
            EnsureOptionalString(organism, propertyName);
        }

        EnsureOptionalInt64(organism, RewardProperty);
        EnsureOptionalBoolean(organism, AnalyzedProperty);
    }

    private static bool FillOrganism(
        JsonObject target,
        JsonObject source,
        ExobiologyReference? reference)
    {
        var changed = false;
        EnsureOptionalInt64(target, EntryIdProperty);
        EnsureOptionalInt64(target, RewardProperty);
        EnsureOptionalBoolean(target, AnalyzedProperty);
        foreach (var property in source.Where(property =>
            target[property.Key] is null && property.Value is not null))
        {
            target[property.Key] = property.Value!.DeepClone();
            changed = true;
        }

        changed |= FillMissingReferenceData(target, reference);

        if (GetBoolean(source, AnalyzedProperty) == true
            && GetBoolean(target, AnalyzedProperty) != true)
        {
            target[AnalyzedProperty] = true;
            changed = true;
        }

        return changed;
    }

    private static bool FillMissingReferenceData(
        JsonObject target,
        ExobiologyReference? reference)
    {
        if (reference is null)
        {
            return false;
        }

        var changed = SetIfMissing(target, SpeciesProperty, reference.SpeciesName);
        changed |= SetIfMissing(target, VariantProperty, reference.VariantName);
        changed |= SetIfMissing(target, VariantLocalizedProperty, reference.DisplayName);
        if ((GetInt64(target, EntryIdProperty) ?? 0) <= 0)
        {
            target[EntryIdProperty] = reference.EntryId;
            changed = true;
        }

        if ((GetInt64(target, RewardProperty) ?? 0) <= 0)
        {
            target[RewardProperty] = reference.Reward;
            changed = true;
        }

        return changed;
    }

    private void RepairBioScanEntryId(
        JsonObject scan,
        JsonObject bodySource,
        IReadOnlyList<CommanderProfile> commanderProfiles)
    {
        if (!IsWeakEntryId(GetInt64(scan, EntryIdProperty)))
        {
            return;
        }

        var species = GetString(scan, SpeciesProperty);
        var organisms = bodySource[OrganicsProperty] as JsonObject
            ?? EmptyOrganisms;
        JsonObject? organism = null;
        foreach (var pair in organisms)
        {
            if (pair.Value is JsonObject candidate
                && string.Equals(
                    GetString(candidate, SpeciesProperty),
                    species,
                    StringComparison.Ordinal))
            {
                organism = candidate;
                break;
            }
        }
        var reference = catalog.FindByVariant(GetString(organism, VariantProperty));
        if (reference is not null)
        {
            scan[EntryIdProperty] = reference.EntryId;
            return;
        }

        var systemAddress = GetInt64(bodySource, SystemAddressProperty);
        var bodyId = GetInt32(bodySource, BodyIdProperty);
        var speciesReference = catalog.FindBySpecies(species);
        if (systemAddress is not null
            && bodyId is not null)
        {
            var prefix = speciesReference?.EntryIdPrefix;
            var claim = string.IsNullOrWhiteSpace(prefix)
                ? null
                : commanderProfiles
                    .Select(profile => profile.Root[ScannedBioEntryIdsProperty])
                    .OfType<JsonArray>()
                    .SelectMany(claims => claims.OfType<JsonValue>())
                    .Select(value => value.TryGetValue<string>(out var text)
                        ? text
                        : null)
                    .FirstOrDefault(value => value is not null
                        && value.StartsWith(
                            $"{systemAddress}_{bodyId}_{prefix}",
                            StringComparison.Ordinal));
            if (claim is not null)
            {
                var claimParts = claim.Split('_');
                if (claimParts.Length > 2
                    && long.TryParse(claimParts[2], out var claimedEntryId))
                {
                    scan[EntryIdProperty] = claimedEntryId;
                    return;
                }
            }
        }

        if (speciesReference is not null)
        {
            scan[EntryIdProperty] = string.Equals(
                    speciesReference.Platform,
                    OdysseYPlatform,
                    StringComparison.OrdinalIgnoreCase)
                ? long.Parse(
                    speciesReference.EntryIdPrefix + "00",
                    CultureInfo.InvariantCulture)
                : speciesReference.EntryId;
        }
    }

    private ExobiologyReference? FindByEntryIdOrPrefix(string entryId)
    {
        if (!long.TryParse(
                entryId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var numeric))
        {
            return null;
        }

        var prefix = entryId.Length >= 5 ? entryId[..5] : entryId;
        return catalog.FindByEntryId(numeric)
            ?? catalog.Entries.FirstOrDefault(reference =>
                reference.EntryId.ToString(CultureInfo.InvariantCulture)
                    .StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool MergeVisitDates(JsonObject target, JsonObject source)
    {
        var changed = false;
        var sourceFirst = GetDateTimeOffset(source, FirstVisitedProperty);
        if (source[FirstVisitedProperty] is not null && sourceFirst is null)
        {
            throw new InvalidDataException(
                "The legacy firstVisited value is not a valid timestamp.");
        }

        var targetFirst = GetDateTimeOffset(target, FirstVisitedProperty);
        if (target[FirstVisitedProperty] is not null && targetFirst is null)
        {
            throw new InvalidDataException(
                "The target firstVisited value is not a valid timestamp.");
        }

        if (sourceFirst is not null
            && (targetFirst is null || sourceFirst < targetFirst))
        {
            target[FirstVisitedProperty] = sourceFirst.Value.ToString("O");
            changed = true;
        }

        var sourceLast = GetDateTimeOffset(source, LastVisitedProperty);
        if (source[LastVisitedProperty] is not null && sourceLast is null)
        {
            throw new InvalidDataException(
                "The legacy lastVisited value is not a valid timestamp.");
        }

        var targetLast = GetDateTimeOffset(target, LastVisitedProperty);
        if (target[LastVisitedProperty] is not null && targetLast is null)
        {
            throw new InvalidDataException(
                "The target lastVisited value is not a valid timestamp.");
        }

        if (sourceLast is not null
            && (targetLast is null || sourceLast > targetLast))
        {
            target[LastVisitedProperty] = sourceLast.Value.ToString("O");
            changed = true;
        }

        return changed;
    }

    private static bool IsSameBioScan(JsonObject first, JsonObject second)
    {
        if (!string.Equals(
                GetString(first, SpeciesProperty),
                GetString(second, SpeciesProperty),
                StringComparison.Ordinal))
        {
            return false;
        }

        var firstLatitude = GetDouble(
            first[LocationProperty]?[LocationLatitudeProperty]);
        var firstLongitude = GetDouble(
            first[LocationProperty]?[LocationLongitudeProperty]);
        var secondLatitude = GetDouble(
            second[LocationProperty]?[LocationLatitudeProperty]);
        var secondLongitude = GetDouble(
            second[LocationProperty]?[LocationLongitudeProperty]);
        return firstLatitude is not null
            && firstLongitude is not null
            && secondLatitude is not null
            && secondLongitude is not null
                ? Math.Abs(firstLatitude.Value - secondLatitude.Value)
                        <= 0.0000001d
                    && Math.Abs(firstLongitude.Value - secondLongitude.Value)
                        <= 0.0000001d
                : JsonNode.DeepEquals(first, second);
    }

    private static bool IsWeakEntryId(long? entryId)
    {
        var text = entryId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return entryId is not > 0
            || text.Length < 7
            || text.EndsWith("00", StringComparison.Ordinal);
    }

    private static long CalculateClaimRewards(IEnumerable<string> claims)
    {
        var total = 0L;
        foreach (var claim in claims)
        {
            var parts = claim.Split('_');
            if (parts.Length != 5
                || !long.TryParse(
                    parts[3],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var reward)
                || reward < 0
                || !bool.TryParse(parts[4], out var firstFootfall)
                || firstFootfall && reward > long.MaxValue / 5)
            {
                throw new InvalidDataException(
                    $"The organic reward in claim '{claim}' is invalid.");
            }

            var claimReward = firstFootfall ? reward * 5 : reward;
            if (claimReward > long.MaxValue - total)
            {
                throw new InvalidDataException(
                    "The total legacy organic reward exceeds the supported range.");
            }

            total += claimReward;
        }

        return total;
    }

    private static bool IsNormalizedClaim(string claim)
    {
        var parts = claim.Split('_', StringSplitOptions.TrimEntries);
        return parts.Length == 5
            && long.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var systemAddress)
            && systemAddress > 0
            && int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bodyId)
            && bodyId >= 0
            && long.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var entryId)
            && entryId > 0
            && long.TryParse(
                parts[3],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var reward)
            && reward >= 0
            && bool.TryParse(parts[4], out var firstFootfall)
            && (!firstFootfall || reward <= long.MaxValue / 5);
    }

    private static JsonArray GetOrCreateArray(
        JsonObject owner,
        string propertyName,
        ref bool changed)
    {
        if (owner[propertyName] is JsonArray array)
        {
            return array;
        }

        if (owner[propertyName] is not null)
        {
            throw new InvalidDataException(
                $"The legacy '{propertyName}' value is malformed and was not overwritten.");
        }

        changed = true;
        array = [];
        owner[propertyName] = array;
        return array;
    }

    private static bool SetIfMissing<T>(
        JsonObject owner,
        string propertyName,
        T? value)
    {
        if (owner[propertyName] is not null || value is null)
        {
            return false;
        }

        owner[propertyName] = JsonValue.Create(value);
        return true;
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
        return await JsonNode.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException(
                $"{path} does not contain a JSON object.");
    }

    private void EnsureSafeSystemTarget(string frontierId)
    {
        var systemsDirectory = Path.Combine(dataDirectory, "systems");
        if (Directory.Exists(systemsDirectory)
            && IsReparsePoint(systemsDirectory))
        {
            throw new InvalidDataException(
                "The systems directory is a symbolic link or junction.");
        }

        var frontierDirectory = Path.Combine(systemsDirectory, frontierId);
        if (!Directory.Exists(frontierDirectory))
        {
            return;
        }

        if (IsReparsePoint(frontierDirectory))
        {
            throw new InvalidDataException(
                $"The systems/{frontierId} directory is a symbolic link or junction.");
        }

        foreach (var path in Directory.EnumerateFiles(
                     frontierDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            EnsureRegularFile(path);
        }
    }

    private static void EnsureRegularFile(string path)
    {
        if (IsReparsePoint(path))
        {
            throw new InvalidDataException(
                $"{path} is a symbolic link or junction.");
        }
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static async Task WriteObjectAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        root,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? GetFrontierIdFromProfilePath(string path)
    {
        var fileName = Path.GetFileName(path);
        var separator = fileName.IndexOf('-', StringComparison.Ordinal);
        return separator > 0 ? fileName[..separator] : null;
    }

    private static string? GetString(JsonObject? owner, string propertyName)
    {
        return owner?[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static void EnsureOptionalString(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is not null
            && GetString(owner, propertyName) is null)
        {
            throw new InvalidDataException(
                $"The legacy {propertyName} value is not a string.");
        }
    }

    private static void EnsureOptionalBoolean(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is not null
            && GetBoolean(owner, propertyName) is null)
        {
            throw new InvalidDataException(
                $"The legacy {propertyName} value is not a Boolean.");
        }
    }

    private static void EnsureOptionalInt64(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is not null
            && GetInt64(owner, propertyName) is null)
        {
            throw new InvalidDataException(
                $"The legacy {propertyName} value is not an integer.");
        }
    }

    private static bool? GetBoolean(JsonObject owner, string propertyName)
    {
        return owner[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonObject owner, string propertyName)
    {
        return owner[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonObject owner, string propertyName)
    {
        return owner[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonNode? node)
    {
        return node is JsonValue value
            && value.TryGetValue<double>(out var result)
                ? result
                : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonObject owner,
        string propertyName)
    {
        return GetString(owner, propertyName) is { } text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result)
                    ? result
                    : null;
    }

    private sealed record CommanderProfile(string FrontierId, string Path, JsonObject Root);

    private sealed record BodyMigrationResult(
        bool Changed,
        int ScanCount,
        int OrganismCount);
}

public sealed record LegacyOrganicProfileMigrationResult(
    int MigratedProfileCount,
    int MigratedBodyCount,
    int MigratedScanCount,
    int MigratedOrganismCount,
    IReadOnlyList<string> Errors)
{
    public bool Migrated => MigratedProfileCount > 0
        || MigratedBodyCount > 0
        || MigratedScanCount > 0
        || MigratedOrganismCount > 0;

    public static LegacyOrganicProfileMigrationResult NotRequired { get; } =
        new(0, 0, 0, 0, []);
}
