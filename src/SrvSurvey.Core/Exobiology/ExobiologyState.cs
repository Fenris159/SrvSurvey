using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Exobiology;

public sealed class ExobiologyState
{
    public const string RadicoidaUnicaSpecies =
        "$Codex_Ent_Ingensradices_Unicus_Name;";

    private readonly ExobiologyReferenceCatalog catalog;
    private readonly Dictionary<BodyKey, BodyState> bodies = [];
    private readonly HashSet<string> scannedBioEntryIds = new(StringComparer.Ordinal);
    private long currentSystemPopulation;
    private string? currentBodyName;
    private SurfaceLocation currentLocation = new(0, 0);
    private BodyKey? currentBodyKey;
    private double currentPlanetRadius;

    public ExobiologyState(
        ExobiologyReferenceCatalog catalog,
        ExobiologySnapshot? seed = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Reset(seed);
    }

    public long OrganicRewards { get; private set; }

    public string? LastOrganicScan { get; private set; }

    public BioSampleSnapshot? ScanOne { get; private set; }

    public BioSampleSnapshot? ScanTwo { get; private set; }

    public int CountRadicoidaUnica { get; private set; }

    public int Version { get; private set; }

    public int UnclaimedScanCount => scannedBioEntryIds.Count;

    public bool? CurrentBodyFirstFootfall => currentBodyKey is not null
        && bodies.TryGetValue(currentBodyKey.Value, out var body)
            ? body.FirstFootfall
            : null;

    public string? ActiveSpeciesDisplayName { get; private set; }

    public double? NearestActiveSampleDistance { get; private set; }

    public double? RequiredSampleDistance => (ScanTwo ?? ScanOne)?.Radius;

    public double? RemainingSampleDistance => RequiredSampleDistance is not null
        && NearestActiveSampleDistance is not null
            ? Math.Max(0, RequiredSampleDistance.Value - NearestActiveSampleDistance.Value)
            : null;

    public void UpdateStatus(EliteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.HasLatitudeLongitude)
        {
            currentLocation = new SurfaceLocation(status.Latitude, status.Longitude);
        }

        currentBodyName = status.BodyName ?? currentBodyName;
        currentPlanetRadius = (double)status.PlanetRadius;
        UpdateSampleDistance();
    }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "Location":
            case "FSDJump":
            case "CarrierJump":
                currentSystemPopulation = GetInt64(root, "Population")
                    ?? currentSystemPopulation;
                currentBodyName = GetString(root, "Body") ?? currentBodyName;
                return true;

            case "ApproachBody":
                currentBodyName = GetString(root, "Body") ?? currentBodyName;
                return true;

            case "Scan":
                ApplyBodyScan(root);
                return true;

            case "Disembark":
                ApplyDisembark(root);
                return true;

            case "ScanOrganic":
                return ApplyOrganicScan(root);

            case "SellOrganicData":
                ApplySale(root);
                return true;

            case "Died":
                ClearAfterDeath();
                return true;

            default:
                return false;
        }
    }

    public ExobiologySnapshot CreateSnapshot()
    {
        return new ExobiologySnapshot(
            LastOrganicScan,
            ScanOne,
            ScanTwo,
            OrganicRewards,
            scannedBioEntryIds.Order(StringComparer.Ordinal).ToArray(),
            CountRadicoidaUnica);
    }

    public void Reset(ExobiologySnapshot? seed = null)
    {
        seed ??= ExobiologySnapshot.Empty;
        LastOrganicScan = seed.LastOrganicScan;
        ScanOne = seed.ScanOne;
        ScanTwo = seed.ScanTwo;
        OrganicRewards = seed.OrganicRewards;
        CountRadicoidaUnica = seed.CountRadicoidaUnica;
        ActiveSpeciesDisplayName = catalog.FindBySpecies(
            seed.ScanTwo?.Species ?? seed.ScanOne?.Species)?.DisplayName;
        UpdateSampleDistance();
        scannedBioEntryIds.Clear();
        scannedBioEntryIds.UnionWith(seed.ScannedBioEntryIds);
        bodies.Clear();
        Version++;
    }

    public void ClearUnclaimedRewards()
    {
        if (OrganicRewards == 0 && scannedBioEntryIds.Count == 0)
        {
            return;
        }

        OrganicRewards = 0;
        scannedBioEntryIds.Clear();
        Version++;
    }

    public void SetFirstFootfall(long systemAddress, int bodyId, bool value)
    {
        var key = new BodyKey(systemAddress, bodyId);
        var body = bodies.GetValueOrDefault(key) ?? new BodyState();
        body.FirstFootfall = value;
        bodies[key] = body;

        var prefix = $"{systemAddress}_{bodyId}_";
        var changed = false;
        foreach (var entry in scannedBioEntryIds
                     .Where(entry => entry.StartsWith(prefix, StringComparison.Ordinal))
                     .ToArray())
        {
            if (!ScannedBioEntry.TryParse(entry, out var parsed)
                || parsed.FirstFootfall == value)
            {
                continue;
            }

            scannedBioEntryIds.Remove(entry);
            scannedBioEntryIds.Add((parsed with { FirstFootfall = value }).ToString());
            changed = true;
        }

        if (changed)
        {
            RecalculateRewards();
            Version++;
        }
    }

    private void ApplyBodyScan(JsonElement root)
    {
        var key = GetBodyKey(root);
        if (key is null)
        {
            return;
        }

        var body = bodies.GetValueOrDefault(key.Value) ?? new BodyState();
        if (GetBoolean(root, "WasFootfalled") is bool wasFootfalled)
        {
            body.WasFootfalled = wasFootfalled;
            if (wasFootfalled)
            {
                body.FirstFootfall = false;
            }
        }

        bodies[key.Value] = body;
        currentBodyKey = key;
        currentBodyName = GetString(root, "BodyName") ?? currentBodyName;
    }

    private void ApplyDisembark(JsonElement root)
    {
        if (!(GetBoolean(root, "OnPlanet") ?? false)
            || (GetBoolean(root, "OnStation") ?? false))
        {
            return;
        }

        var key = GetBodyKey(root);
        if (key is null)
        {
            return;
        }

        var body = bodies.GetValueOrDefault(key.Value) ?? new BodyState();
        if (currentSystemPopulation == 0 && body.WasFootfalled == false)
        {
            body.FirstFootfall = true;
        }

        bodies[key.Value] = body;
        currentBodyKey = key;
    }

    private bool ApplyOrganicScan(JsonElement root)
    {
        var variant = GetString(root, "Variant");
        var species = GetString(root, "Species");
        var reference = catalog.FindByVariant(variant)
            ?? catalog.FindBySpecies(species);
        var systemAddress = GetInt64(root, "SystemAddress");
        var bodyId = GetInt32(root, "Body");
        var scanType = GetString(root, "ScanType");
        if (reference is null
            || systemAddress is null
            || bodyId is null
            || string.IsNullOrWhiteSpace(species)
            || string.IsNullOrWhiteSpace(scanType))
        {
            return false;
        }

        var activeHash = $"{systemAddress}|{bodyId}|{species}";
        if (LastOrganicScan is not null
            && !string.Equals(LastOrganicScan, activeHash, StringComparison.Ordinal))
        {
            ScanOne = null;
            ScanTwo = null;
        }

        LastOrganicScan = activeHash;
        currentBodyKey = new BodyKey(systemAddress.Value, bodyId.Value);
        ActiveSpeciesDisplayName = GetString(root, "Variant_Localised")
            ?? GetString(root, "Species_Localised")
            ?? reference.DisplayName;
        var genus = GetString(root, "Genus") ?? string.Empty;
        var sample = new BioSampleSnapshot(
            currentLocation,
            GetGenusRange(genus),
            genus,
            species,
            "Active",
            reference.EntryId,
            currentBodyName);

        if (scanType == "Log")
        {
            ScanOne = sample;
            ScanTwo = null;
        }
        else if (ScanOne is not null && ScanTwo is null)
        {
            ScanTwo = sample;
        }
        else if (ScanOne is null && scanType == "Sample")
        {
            ScanOne = sample;
        }
        else if (scanType == "Analyse")
        {
            LastOrganicScan = null;
            ScanOne = null;
            ScanTwo = null;
            ActiveSpeciesDisplayName = null;
        }

        if (scanType == "Analyse")
        {
            if (species == RadicoidaUnicaSpecies)
            {
                CountRadicoidaUnica++;
            }

            var body = bodies.GetValueOrDefault(
                new BodyKey(systemAddress.Value, bodyId.Value));
            var entry = new ScannedBioEntry(
                systemAddress.Value,
                bodyId.Value,
                reference.EntryId,
                reference.Reward,
                body?.FirstFootfall ?? false);
            var prefix = entry.ToString()[..entry.ToString().LastIndexOf('_')];
            var prior = scannedBioEntryIds.FirstOrDefault(candidate =>
                candidate.StartsWith(prefix, StringComparison.Ordinal));
            if (prior is not null)
            {
                scannedBioEntryIds.Remove(prior);
            }

            scannedBioEntryIds.Add(entry.ToString());
            RecalculateRewards();
        }

        UpdateSampleDistance();
        Version++;
        return true;
    }

    private void ApplySale(JsonElement root)
    {
        if (!root.TryGetProperty("BioData", out var bioData)
            || bioData.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var changed = false;
        foreach (var item in bioData.EnumerateArray())
        {
            var species = GetString(item, "Species");
            if (species == RadicoidaUnicaSpecies)
            {
                CountRadicoidaUnica = 0;
                changed = true;
            }

            var reference = catalog.FindBySpecies(species);
            var value = GetInt64(item, "Value");
            if (reference is null || value is null)
            {
                continue;
            }

            var rewardText = value.Value.ToString(CultureInfo.InvariantCulture);
            var match = scannedBioEntryIds.FirstOrDefault(candidate =>
                candidate.Contains(reference.EntryIdPrefix, StringComparison.Ordinal)
                && candidate.Contains(rewardText, StringComparison.Ordinal));
            if (match is not null)
            {
                scannedBioEntryIds.Remove(match);
                changed = true;
            }
        }

        if (changed)
        {
            RecalculateRewards();
            Version++;
        }
    }

    private void ClearAfterDeath()
    {
        LastOrganicScan = null;
        ScanOne = null;
        ScanTwo = null;
        ActiveSpeciesDisplayName = null;
        OrganicRewards = 0;
        scannedBioEntryIds.Clear();
        UpdateSampleDistance();
        Version++;
    }

    private void UpdateSampleDistance()
    {
        if (currentPlanetRadius <= 0)
        {
            NearestActiveSampleDistance = null;
            return;
        }

        var activeSamples = new[] { ScanOne, ScanTwo }
            .Where(sample => sample is not null)
            .Cast<BioSampleSnapshot>()
            .ToArray();
        try
        {
            NearestActiveSampleDistance = activeSamples.Length == 0
                ? null
                : activeSamples.Min(sample => SurfaceNavigation.GetDistance(
                    new SurfaceCoordinate(
                        sample.Location.Latitude,
                        sample.Location.Longitude),
                    new SurfaceCoordinate(
                        currentLocation.Latitude,
                        currentLocation.Longitude),
                    currentPlanetRadius));
        }
        catch (ArgumentOutOfRangeException)
        {
            NearestActiveSampleDistance = null;
        }
    }

    private void RecalculateRewards()
    {
        OrganicRewards = scannedBioEntryIds.Sum(entry =>
        {
            if (!ScannedBioEntry.TryParse(entry, out var parsed))
            {
                return 0;
            }

            return parsed.FirstFootfall ? parsed.Reward * 5 : parsed.Reward;
        });
    }

    private static BodyKey? GetBodyKey(JsonElement root)
    {
        var systemAddress = GetInt64(root, "SystemAddress");
        var bodyId = GetInt32(root, "BodyID");
        return systemAddress is null || bodyId is null
            ? null
            : new BodyKey(systemAddress.Value, bodyId.Value);
    }

    private static int GetGenusRange(string genus)
    {
        return genus switch
        {
            "$Codex_Ent_Fumerolas_Genus_Name;" => 100,
            "$Codex_Ent_Aleoids_Genus_Name;"
                or "$Codex_Ent_Clypeus_Genus_Name;"
                or "$Codex_Ent_Conchas_Genus_Name;"
                or "$Codex_Ent_Shrubs_Genus_Name;"
                or "$Codex_Ent_Recepta_Genus_Name;" => 150,
            "$Codex_Ent_Tussocks_Genus_Name;" => 200,
            "$Codex_Ent_Cactoid_Genus_Name;"
                or "$Codex_Ent_Fungoids_Genus_Name;" => 300,
            "$Codex_Ent_Bacterial_Genus_Name;"
                or "$Codex_Ent_Fonticulus_Genus_Name;"
                or "$Codex_Ent_Stratum_Genus_Name;" => 500,
            "$Codex_Ent_Osseus_Genus_Name;"
                or "$Codex_Ent_Tubus_Genus_Name;" => 800,
            "$Codex_Ent_Electricae_Genus_Name;" => 1000,
            "$Codex_Ent_Vents_Name;"
                or "$Codex_Ent_Sphere_Name;"
                or "$Codex_Ent_Cone_Name;"
                or "$Codex_Ent_Brancae_Name;"
                or "$Codex_Ent_Ground_Struct_Ice_Name;"
                or "$Codex_Ent_Tube_Name;" => 100,
            "$Codex_Ent_Barnacles_Name;"
                or "$Codex_Ent_Thargoid_Coral_Name;"
                or "$Codex_Ent_Thargoid_Tower_Name;" => 85,
            "$Codex_Ent_Ingensradices_Genus_Name;" => 15,
            _ => 50,
        };
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

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private readonly record struct BodyKey(long SystemAddress, int BodyId);

    private sealed class BodyState
    {
        public bool? WasFootfalled { get; set; }

        public bool FirstFootfall { get; set; }
    }

    private sealed record ScannedBioEntry(
        long SystemAddress,
        int BodyId,
        long EntryId,
        long Reward,
        bool FirstFootfall)
    {
        public override string ToString()
        {
            return $"{SystemAddress}_{BodyId}_{EntryId}_{Reward}_{FirstFootfall}";
        }

        public static bool TryParse(string value, out ScannedBioEntry result)
        {
            result = null!;
            var parts = value.Split('_', StringSplitOptions.TrimEntries);
            if (parts.Length < 5
                || !long.TryParse(parts[0], out var systemAddress)
                || !int.TryParse(parts[1], out var bodyId)
                || !long.TryParse(parts[2], out var entryId)
                || !long.TryParse(parts[3], out var reward)
                || !bool.TryParse(parts[4], out var firstFootfall))
            {
                return false;
            }

            result = new ScannedBioEntry(
                systemAddress,
                bodyId,
                entryId,
                reward,
                firstFootfall);
            return true;
        }
    }
}

public sealed record SurfaceLocation(double Latitude, double Longitude);

public sealed record BioSampleSnapshot(
    SurfaceLocation Location,
    float Radius,
    string Genus,
    string Species,
    string Status,
    long EntryId,
    string? Body);

public sealed record ExobiologySnapshot(
    string? LastOrganicScan,
    BioSampleSnapshot? ScanOne,
    BioSampleSnapshot? ScanTwo,
    long OrganicRewards,
    IReadOnlyList<string> ScannedBioEntryIds,
    int CountRadicoidaUnica)
{
    public static ExobiologySnapshot Empty { get; } =
        new(null, null, null, 0, [], 0);
}
