using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Exobiology;

public sealed class SurfaceSurveyJournalTracker
{
    private const string OrganicCodexCategory =
        "$Codex_SubCategory_Organic_Structures;";
    private const string FixedLifeCloud = "$Fixed_Event_Life_Cloud;";
    private const string FixedLifeRing = "$Fixed_Event_Life_Ring;";
    private const double TrackerRemovalDistanceMeters = 150;

    private readonly SystemSurfaceStore store;
    private readonly ExobiologyReferenceCatalog catalog;
    private string? lastOrganicScan;
    private BioSampleSnapshot? scanOne;
    private BioSampleSnapshot? scanTwo;
    private EliteStatus? status;

    public SurfaceSurveyJournalTracker(
        SystemSurfaceStore store,
        ExobiologyReferenceCatalog catalog)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public SurfaceCoordinate? ShipLocation { get; private set; }

    public SurfaceCoordinate? SrvLocation { get; private set; }

    public int Version { get; private set; }

    public void Reset(ExobiologySnapshot? seed = null)
    {
        seed ??= ExobiologySnapshot.Empty;
        lastOrganicScan = seed.LastOrganicScan;
        scanOne = seed.ScanOne;
        scanTwo = seed.ScanTwo;
        ShipLocation = null;
        SrvLocation = null;
        Version++;
    }

    public async Task<SurfaceSurveyJournalUpdateResult> ApplyAsync(
        SurfaceSurveySessionContext session,
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? nextStatus,
        SurfaceSurveyTrackingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(journalEvents);
        options ??= SurfaceSurveyTrackingOptions.Default;
        if (nextStatus is not null)
        {
            status = nextStatus;
        }

        var mutationCount = 0;
        var warnings = new List<string>();
        foreach (var journalEvent in journalEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                mutationCount += journalEvent.EventName switch
                {
                    "Touchdown" => await ApplyTouchdownAsync(
                        session,
                        journalEvent.Payload,
                        cancellationToken).ConfigureAwait(false),
                    "Disembark" => ApplyDisembark(journalEvent.Payload),
                    "Embark" => ApplyEmbark(journalEvent.Payload),
                    "LeaveBody" => ApplyLeaveBody(),
                    "CodexEntry" => await ApplyCodexEntryAsync(
                        session,
                        journalEvent.Payload,
                        options,
                        cancellationToken).ConfigureAwait(false),
                    "ScanOrganic" => await ApplyOrganicScanAsync(
                        session,
                        journalEvent.Payload,
                        options,
                        warnings,
                        cancellationToken).ConfigureAwait(false),
                    _ => 0,
                };
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or InvalidOperationException)
            {
                warnings.Add(
                    $"{journalEvent.EventName} surface history was not saved: "
                        + exception.Message);
            }
        }

        if (mutationCount > 0)
        {
            Version++;
        }

        return new SurfaceSurveyJournalUpdateResult(
            mutationCount,
            warnings);
    }

    private async Task<int> ApplyTouchdownAsync(
        SurfaceSurveySessionContext session,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        if (GetCoordinate(root) is not { } location
            || CreateBodyContext(session, root) is not { } context)
        {
            return 0;
        }

        ShipLocation = location;
        await store.SetLastTouchdownAsync(
                context,
                location,
                cancellationToken)
            .ConfigureAwait(false);
        return 1;
    }

    private int ApplyDisembark(JsonElement root)
    {
        if (!(GetBoolean(root, "SRV") ?? false)
            || GetCurrentCoordinate() is not { } location)
        {
            return 0;
        }

        SrvLocation = location;
        return 1;
    }

    private int ApplyEmbark(JsonElement root)
    {
        if (!(GetBoolean(root, "SRV") ?? false) || SrvLocation is null)
        {
            return 0;
        }

        SrvLocation = null;
        return 1;
    }

    private int ApplyLeaveBody()
    {
        if (ShipLocation is null && SrvLocation is null)
        {
            return 0;
        }

        ShipLocation = null;
        SrvLocation = null;
        return 1;
    }

    private async Task<int> ApplyCodexEntryAsync(
        SurfaceSurveySessionContext session,
        JsonElement root,
        SurfaceSurveyTrackingOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.AutoTrackCompositionScans
            || !string.Equals(
                GetString(root, "SubCategory"),
                OrganicCodexCategory,
                StringComparison.Ordinal)
            || GetString(root, "NearestDestination") is FixedLifeCloud
                or FixedLifeRing
            || GetInt64(root, "EntryID") is not { } entryId
            || catalog.FindByEntryId(entryId) is not { } reference
            || !reference.IsBiology
            || CreateBodyContext(session, root) is not { } context
            || (GetCoordinate(root) ?? GetCurrentCoordinate()) is not { } location)
        {
            return 0;
        }

        if (options.SkipAnalyzedCompositionScans
            && options.AnalyzedSpecies?.Contains(reference.SpeciesName) == true)
        {
            return 0;
        }

        var result = await store.AddBookmarkAsync(
                context,
                ExobiologyReferenceCatalog.GetGenusName(reference.SpeciesName),
                location,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.Mutation == SurfaceBookmarkMutation.Added ? 1 : 0;
    }

    private async Task<int> ApplyOrganicScanAsync(
        SurfaceSurveySessionContext session,
        JsonElement root,
        SurfaceSurveyTrackingOptions options,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var scanType = GetString(root, "ScanType");
        var genus = GetString(root, "Genus");
        var species = GetString(root, "Species");
        var reference = catalog.FindByVariant(GetString(root, "Variant"))
            ?? catalog.FindBySpecies(species);
        var context = CreateBodyContext(session, root);
        var location = GetCurrentCoordinate();
        if (string.IsNullOrWhiteSpace(scanType)
            || string.IsNullOrWhiteSpace(genus)
            || string.IsNullOrWhiteSpace(species)
            || reference is null
            || context is null
            || location is null)
        {
            warnings.Add(
                "A ScanOrganic event lacked body, location, or Codex reference "
                    + "context and was not added to surface history.");
            return 0;
        }

        var mutations = 0;
        var activeHash = $"{context.SystemAddress}|{context.BodyId}|{species}";
        if (lastOrganicScan is not null
            && !string.Equals(lastOrganicScan, activeHash, StringComparison.Ordinal))
        {
            mutations += await SaveAbandonedSamplesAsBookmarksAsync(
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
            scanOne = null;
            scanTwo = null;
        }

        lastOrganicScan = activeHash;
        var sample = new BioSampleSnapshot(
            new SurfaceLocation(location.Value.Latitude, location.Value.Longitude),
            ExobiologyReferenceCatalog.GetSampleDistanceMeters(genus),
            genus,
            species,
            "Active",
            reference.EntryId,
            context.BodyName);

        if (options.AutoRemoveTrackerOnSampling)
        {
            var removal = await store.RemoveBookmarkAsync(
                    context,
                    genus,
                    location.Value,
                    maximumDistanceMeters: TrackerRemovalDistanceMeters,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (removal.Mutation == SurfaceBookmarkMutation.Removed)
            {
                mutations++;
            }
        }

        if (scanType == "Log")
        {
            scanOne = sample;
            scanTwo = null;
        }
        else if (scanOne is not null && scanTwo is null && scanType == "Sample")
        {
            scanTwo = sample;
        }
        else if (scanOne is null && scanType == "Sample")
        {
            scanOne = sample;
        }
        else if (scanType == "Analyse")
        {
            var completed = new[] { sample, scanOne, scanTwo }
                .Where(candidate => candidate is not null)
                .Cast<BioSampleSnapshot>()
                .Select(candidate => ToSurfaceScan(candidate, context.BodyName))
                .ToArray();
            await store.AppendBioScansAsync(
                    context,
                    completed,
                    cancellationToken)
                .ConfigureAwait(false);
            mutations += completed.Length;
            if (options.AutoRemoveTrackerOnFinalSample)
            {
                await store.RemoveBookmarkGroupAsync(
                        context,
                        genus,
                        cancellationToken)
                    .ConfigureAwait(false);
                mutations++;
            }

            lastOrganicScan = null;
            scanOne = null;
            scanTwo = null;
        }

        return mutations;
    }

    private async Task<int> SaveAbandonedSamplesAsBookmarksAsync(
        SystemSurfaceContext context,
        CancellationToken cancellationToken)
    {
        var mutations = 0;
        foreach (var sample in new[] { scanOne, scanTwo })
        {
            if (sample is null
                || string.IsNullOrWhiteSpace(sample.Genus)
                || !string.Equals(
                    sample.Body,
                    context.BodyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var result = await store.AddBookmarkAsync(
                    context,
                    sample.Genus,
                    new SurfaceCoordinate(
                        sample.Location.Latitude,
                        sample.Location.Longitude),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (result.Mutation == SurfaceBookmarkMutation.Added)
            {
                mutations++;
            }
        }

        return mutations;
    }

    private static SurfaceBioScan ToSurfaceScan(
        BioSampleSnapshot sample,
        string bodyName)
    {
        return new SurfaceBioScan(
            new SurfaceCoordinate(
                sample.Location.Latitude,
                sample.Location.Longitude),
            sample.Radius,
            sample.Genus,
            sample.Species,
            "Complete",
            sample.EntryId,
            sample.Body ?? bodyName);
    }

    private SystemSurfaceContext? CreateBodyContext(
        SurfaceSurveySessionContext session,
        JsonElement root)
    {
        var bodyId = GetInt32(root, "BodyID")
            ?? GetInt32(root, "Body");
        var bodyName = GetString(root, "Body")
            ?? status?.BodyName;
        var systemAddress = GetInt64(root, "SystemAddress")
            ?? session.SystemAddress;
        var systemName = GetString(root, "StarSystem")
            ?? session.SystemName;
        if (bodyId is null
            || string.IsNullOrWhiteSpace(bodyName)
            || string.IsNullOrWhiteSpace(systemName)
            || systemAddress <= 0)
        {
            return null;
        }

        var radius = status?.PlanetRadius is > 0
            ? (double)status.PlanetRadius
            : 0;
        return new SystemSurfaceContext(
            session.FrontierId,
            session.CommanderName,
            systemName,
            systemAddress,
            session.StarPosition,
            bodyId.Value,
            bodyName,
            radius);
    }

    private SurfaceCoordinate? GetCurrentCoordinate()
    {
        if (status?.HasLatitudeLongitude != true)
        {
            return null;
        }

        try
        {
            return new SurfaceCoordinate(status.Latitude, status.Longitude);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static SurfaceCoordinate? GetCoordinate(JsonElement root)
    {
        var latitude = GetDouble(root, "Latitude");
        var longitude = GetDouble(root, "Longitude");
        if (latitude is null || longitude is null)
        {
            return null;
        }

        try
        {
            return new SurfaceCoordinate(latitude.Value, longitude.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : null;
    }
}

public sealed record SurfaceSurveySessionContext(
    string FrontierId,
    string? CommanderName,
    string SystemName,
    long SystemAddress,
    GalacticCoordinate? StarPosition);

public sealed record SurfaceSurveyTrackingOptions(
    bool AutoRemoveTrackerOnSampling,
    bool AutoRemoveTrackerOnFinalSample,
    bool AutoTrackCompositionScans = true,
    bool SkipAnalyzedCompositionScans = true,
    IReadOnlySet<string>? AnalyzedSpecies = null)
{
    public static SurfaceSurveyTrackingOptions Default { get; } = new(
        AutoRemoveTrackerOnSampling: true,
        AutoRemoveTrackerOnFinalSample: false);
}

public sealed record SurfaceSurveyJournalUpdateResult(
    int MutationCount,
    IReadOnlyList<string> Warnings);
