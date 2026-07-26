using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exploration;

public sealed class SystemScanState
{
    private const string BiologicalSignal = "$SAA_SignalType_Biological;";
    private const string GeologicalSignal = "$SAA_SignalType_Geological;";
    private const string GeologicalCodexCategory =
        "$Codex_SubCategory_Geology_and_Anomalies;";
    private const string OrganicCodexCategory =
        "$Codex_SubCategory_Organic_Structures;";
    private static readonly Lazy<ExobiologyReferenceCatalog> DefaultBioCatalog =
        new(ExobiologyReferenceCatalog.LoadEmbedded);

    private readonly Dictionary<int, BodyState> bodies = [];
    private readonly Dictionary<string, SignalState> signals =
        new(StringComparer.Ordinal);
    private long scanSequence;
    private bool isOdyssey = true;
    private readonly ExobiologyReferenceCatalog bioCatalog;

    public SystemScanState(ExobiologyReferenceCatalog? bioCatalog = null)
    {
        this.bioCatalog = bioCatalog ?? DefaultBioCatalog.Value;
    }

    public string? SystemName { get; private set; }

    public long? SystemAddress { get; private set; }

    public GalacticCoordinate? StarPosition { get; private set; }

    public long Population { get; private set; }

    public int ExpectedBodyCount { get; private set; }

    public bool HasDiscoveryScan { get; private set; }

    public bool AllBodiesFound { get; private set; }

    public int RawNonBodySignalCount { get; private set; }

    public int? CurrentBodyId { get; private set; }

    public int? LastDetailedBodyId { get; private set; }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var root = journalEvent.Payload;

        switch (journalEvent.EventName)
        {
            case "Fileheader":
            case "LoadGame":
                isOdyssey = GetBoolean(root, "Odyssey") ?? isOdyssey;
                return true;

            case "Location":
            case "FSDJump":
            case "CarrierJump":
                ApplySystemLocation(root);
                return true;

            case "FSSDiscoveryScan":
                ApplyDiscoveryScan(root);
                return true;

            case "FSSAllBodiesFound":
                ApplyAllBodiesFound(root);
                return true;

            case "Scan":
                ApplyScan(root);
                return true;

            case "ScanBaryCentre":
                ApplyBarycentreScan(root);
                return true;

            case "SAAScanComplete":
                ApplyDssComplete(root);
                return true;

            case "FSSBodySignals":
            case "SAASignalsFound":
                ApplyBodySignals(root);
                return true;

            case "ScanOrganic":
                ApplyOrganicScan(root);
                return true;

            case "CodexEntry":
                ApplyCodexEntry(root);
                return true;

            case "FSSSignalDiscovered":
                ApplySignalDiscovered(root);
                return true;

            case "ApproachBody":
            case "Touchdown":
            case "SupercruiseExit":
            case "Disembark":
                ApplyBodyContext(root, journalEvent.EventName);
                return true;

            case "StartJump" when string.Equals(
                GetString(root, "JumpType"),
                "Hyperspace",
                StringComparison.Ordinal):
                CurrentBodyId = null;
                return true;

            case "Died":
            case "Resurrect":
                CurrentBodyId = null;
                return true;

            case "Music" when string.Equals(
                GetString(root, "MusicTrack"),
                "MainMenu",
                StringComparison.Ordinal):
                CurrentBodyId = null;
                return true;

            default:
                return false;
        }
    }

    public SystemScanSnapshot CreateSnapshot()
    {
        if (SystemAddress is null)
        {
            return SystemScanSnapshot.Empty;
        }

        var bodySnapshots = bodies.Values
            .OrderBy(body => body.BodyId)
            .Select(body => body.CreateSnapshot(isOdyssey, SystemName))
            .ToArray();
        var fssBodyCount = bodySnapshots.Count(body => body.CountsTowardFss);
        var nonBodySignalCount = Math.Max(
            0,
            RawNonBodySignalCount
                - bodySnapshots.Count(body => body.Kind == SystemBodyKind.Asteroid)
                - signals.Values.Count(signal => signal.IsRoutineStation));

        return new SystemScanSnapshot(
            SystemName,
            SystemAddress,
            StarPosition,
            Population,
            ExpectedBodyCount,
            HasDiscoveryScan,
            AllBodiesFound,
            fssBodyCount,
            bodySnapshots.Count(body => body.IsScanned),
            bodySnapshots.Count(body => body.IsDssComplete),
            bodySnapshots.Sum(body => (long)body.CurrentScanValue),
            RawNonBodySignalCount,
            nonBodySignalCount,
            CurrentBodyId,
            LastDetailedBodyId,
            bodySnapshots);
    }

    public bool MergeKnownData(
        SystemScanSnapshot known,
        bool includeBiologicalData = true)
    {
        ArgumentNullException.ThrowIfNull(known);
        if (SystemAddress is null
            || known.SystemAddress != SystemAddress
            || string.IsNullOrWhiteSpace(known.SystemName))
        {
            return false;
        }

        var changed = false;
        if (string.IsNullOrWhiteSpace(SystemName))
        {
            SystemName = known.SystemName;
            changed = true;
        }

        if (StarPosition is null && known.StarPosition is not null)
        {
            StarPosition = known.StarPosition;
            changed = true;
        }

        if (known.ExpectedBodyCount > ExpectedBodyCount)
        {
            ExpectedBodyCount = known.ExpectedBodyCount;
            changed = true;
        }

        if (!HasDiscoveryScan && known.HasDiscoveryScan)
        {
            HasDiscoveryScan = true;
            changed = true;
        }

        if (!AllBodiesFound && known.AllBodiesFound)
        {
            AllBodiesFound = true;
            changed = true;
        }

        foreach (var source in known.Bodies)
        {
            if (source.BodyId < 0 || string.IsNullOrWhiteSpace(source.Name))
            {
                continue;
            }

            if (!bodies.TryGetValue(source.BodyId, out var target))
            {
                target = new BodyState(source.BodyId, source.Name);
                bodies.Add(source.BodyId, target);
                changed = true;
            }
            else if (target.Name == $"Body {source.BodyId}")
            {
                target.Name = source.Name;
                changed = true;
            }

            changed |= MergeBody(target, source, includeBiologicalData);
        }

        return changed;
    }

    public bool SetCurrentBodyFirstFootfall(bool value)
    {
        return CurrentBodyId is { } bodyId
            && SetBodyFirstFootfall(bodyId, value);
    }

    public bool SetBodyFirstFootfall(int bodyId, bool value)
    {
        if (!bodies.TryGetValue(bodyId, out var body))
        {
            return false;
        }

        body.IsFirstFootfall = value;
        return true;
    }

    private void ApplySystemLocation(JsonElement root)
    {
        var address = GetInt64(root, "SystemAddress");
        var name = GetString(root, "StarSystem")
            ?? GetString(root, "SystemName");
        if (address is not null)
        {
            SetSystem(address.Value, name);
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            SystemName = name;
        }

        Population = GetInt64(root, "Population") ?? Population;
        StarPosition = GetGalacticCoordinate(root, "StarPos")
            ?? StarPosition;
        var bodyId = GetInt32(root, "BodyID");
        if (bodyId is not null && GetString(root, "BodyType") == "Planet")
        {
            CurrentBodyId = bodyId;
        }
        else
        {
            CurrentBodyId = null;
        }
    }

    private void ApplyDiscoveryScan(JsonElement root)
    {
        if (!EnsureSystem(root))
        {
            return;
        }

        HasDiscoveryScan = true;
        ExpectedBodyCount = Math.Max(
            ExpectedBodyCount,
            GetInt32(root, "BodyCount") ?? 0);
        RawNonBodySignalCount = Math.Max(
            RawNonBodySignalCount,
            GetInt32(root, "NonBodyCount") ?? 0);
    }

    private void ApplyAllBodiesFound(JsonElement root)
    {
        if (!EnsureSystem(root))
        {
            return;
        }

        AllBodiesFound = true;
        ExpectedBodyCount = Math.Max(
            ExpectedBodyCount,
            GetInt32(root, "Count") ?? 0);
    }

    private void ApplyScan(JsonElement root)
    {
        if (!EnsureSystem(root))
        {
            return;
        }

        var bodyId = GetInt32(root, "BodyID");
        if (bodyId is null)
        {
            return;
        }

        var body = GetOrCreateBody(
            bodyId.Value,
            GetString(root, "BodyName"));
        body.IsScanned = true;
        body.Name = GetString(root, "BodyName") ?? body.Name;
        body.StarClass = GetString(root, "StarType");
        body.PlanetClass = GetString(root, "PlanetClass");
        body.IsLandable = GetBoolean(root, "Landable") ?? false;
        body.Kind = GetBodyKind(
            body.Name,
            body.StarClass,
            body.PlanetClass,
            body.IsLandable);
        body.IsTerraformable = GetString(root, "TerraformState") == "Terraformable";
        var planetMass = GetDouble(root, "MassEM");
        body.Mass = planetMass is > 0
            ? planetMass.Value
            : GetDouble(root, "StellarMass") ?? 0;
        body.DistanceFromArrivalLs =
            GetDouble(root, "DistanceFromArrivalLS") ?? 0;
        body.RadiusMeters = GetDouble(root, "Radius") ?? 0;
        body.SurfaceGravity = GetDouble(root, "SurfaceGravity") ?? 0;
        body.SurfaceTemperature = GetDouble(root, "SurfaceTemperature") ?? 0;
        body.SurfacePressure = GetDouble(root, "SurfacePressure") ?? 0;
        body.SemiMajorAxis = GetDouble(root, "SemiMajorAxis") ?? 0;
        body.AbsoluteMagnitude = GetDouble(root, "AbsoluteMagnitude") ?? 0;
        body.TidalLock = GetBoolean(root, "TidalLock");
        body.Atmosphere = GetString(root, "Atmosphere");
        body.AtmosphereType = GetString(root, "AtmosphereType");
        body.Volcanism = GetString(root, "Volcanism");
        body.WasDiscovered = GetBoolean(root, "WasDiscovered") ?? false;
        body.WasMapped = GetBoolean(root, "WasMapped") ?? false;
        body.WasFootfalled = GetBoolean(root, "WasFootfalled");
        var parents = ReadParents(root);
        if (parents is not null)
        {
            body.Parents = parents;
            body.HasRingParent = parents.FirstOrDefault()?.Kind
                == SystemBodyParentKind.Ring;
        }

        body.AtmosphereComposition = ReadComposition(root, "AtmosphereComposition");
        body.Materials = ReadComposition(root, "Materials");
        body.Rings = ReadRings(root);
        body.ScanSequence = ++scanSequence;

        var isDetailedPlanet = GetString(root, "ScanType") == "Detailed"
            && body.Kind is SystemBodyKind.GasGiant
                or SystemBodyKind.Planet
                or SystemBodyKind.LandablePlanet;
        if (isDetailedPlanet && !body.HasRingParent)
        {
            LastDetailedBodyId = body.BodyId;
        }
    }

    private void ApplyBarycentreScan(JsonElement root)
    {
        if (!EnsureSystem(root))
        {
            return;
        }

        var bodyId = GetInt32(root, "BodyID");
        if (bodyId is null)
        {
            return;
        }

        var body = GetOrCreateBody(bodyId.Value, $"barycentre {bodyId.Value}");
        body.IsScanned = true;
        body.Kind = SystemBodyKind.Barycentre;
        body.SemiMajorAxis = GetDouble(root, "SemiMajorAxis") ?? 0;
        body.Parents = ReadParents(root) ?? body.Parents;
    }

    private void ApplyDssComplete(JsonElement root)
    {
        if (!TryGetBody(root, "BodyID", "BodyName", out var body))
        {
            return;
        }

        body.IsDssComplete = true;
        body.DssEfficiencyBonus =
            (GetInt32(root, "ProbesUsed") ?? int.MaxValue)
            <= (GetInt32(root, "EfficiencyTarget") ?? -1);
    }

    private void ApplyBodySignals(JsonElement root)
    {
        if (!TryGetBody(root, "BodyID", "BodyName", out var body))
        {
            return;
        }

        body.BiologicalSignalCount = Math.Max(
            body.BiologicalSignalCount,
            GetSignalCount(root, BiologicalSignal));
        body.GeologicalSignalCount = Math.Max(
            body.GeologicalSignalCount,
            GetSignalCount(root, GeologicalSignal));

        if (root.TryGetProperty("Genuses", out var genuses)
            && genuses.ValueKind == JsonValueKind.Array)
        {
            foreach (var genus in genuses.EnumerateArray())
            {
                var name = GetString(genus, "Genus");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var organism = body.GetOrCreateOrganism(name);
                    organism.GenusLocalized = GetString(
                            genus,
                            "Genus_Localised")
                        ?? organism.GenusLocalized;
                }
            }

            body.BiologicalSignalCount = Math.Max(
                body.BiologicalSignalCount,
                body.Organisms.Count);
        }
    }

    private void ApplyOrganicScan(JsonElement root)
    {
        if (!TryGetBody(root, "Body", null, out var body))
        {
            return;
        }

        var reference = bioCatalog.FindByVariant(GetString(root, "Variant"))
            ?? bioCatalog.FindBySpecies(GetString(root, "Species"));
        var genus = GetString(root, "Genus")
            ?? (reference is null
                ? null
                : ExobiologyReferenceCatalog.GetGenusName(
                    reference.SpeciesName));
        if (string.IsNullOrWhiteSpace(genus))
        {
            return;
        }

        var organism = body.GetOrCreateOrganism(genus);
        organism.GenusLocalized = GetString(root, "Genus_Localised")
            ?? organism.GenusLocalized;
        organism.Species = GetString(root, "Species") ?? organism.Species;
        organism.SpeciesLocalized = GetString(root, "Species_Localised")
            ?? organism.SpeciesLocalized;
        organism.Variant = GetString(root, "Variant") ?? organism.Variant;
        organism.VariantLocalized = GetString(root, "Variant_Localised")
            ?? organism.VariantLocalized;
        if (reference is not null)
        {
            organism.EntryId = reference.EntryId;
            organism.Reward = reference.Reward;
        }

        organism.IsScanned = true;
        organism.IsAnalyzed |= GetString(root, "ScanType") == "Analyse";
        body.BiologicalSignalCount = Math.Max(
            body.BiologicalSignalCount,
            body.Organisms.Count);
    }

    private void ApplyCodexEntry(JsonElement root)
    {
        if (!TryGetBody(root, "BodyID", null, out var body))
        {
            return;
        }

        var category = GetString(root, "SubCategory");
        if (category == GeologicalCodexCategory)
        {
            var name = GetString(root, "Name_Localised")
                ?? GetString(root, "Name")
                ?? GetInt64(root, "EntryID")?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                body.AnalyzedGeologicalSignals.Add(name);
            }

            return;
        }

        if (category != OrganicCodexCategory
            || GetInt64(root, "EntryID") is not { } entryId
            || bioCatalog.FindByEntryId(entryId) is not { } reference)
        {
            return;
        }

        var genus = ExobiologyReferenceCatalog.GetGenusName(
            reference.SpeciesName);
        var organism = body.GetOrCreateOrganism(genus);
        organism.Species = reference.SpeciesName;
        organism.Variant = reference.VariantName;
        organism.VariantLocalized = GetString(root, "Name_Localised")
            ?? organism.VariantLocalized
            ?? reference.DisplayName;
        organism.EntryId = reference.EntryId;
        organism.Reward = reference.Reward;
        organism.IsRegionalFirst |= GetBoolean(root, "IsNewEntry") ?? false;
    }

    private void ApplySignalDiscovered(JsonElement root)
    {
        if (!EnsureSystem(root))
        {
            return;
        }

        var name = GetString(root, "SignalName");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        signals[name] = new SignalState(
            GetString(root, "SignalType"),
            GetBoolean(root, "IsStation") ?? false);
    }

    private void ApplyBodyContext(JsonElement root, string eventName)
    {
        if (!TryGetBody(root, "BodyID", "Body", out var body))
        {
            return;
        }

        CurrentBodyId = body.BodyId;
        if (eventName == "Disembark"
            && (GetBoolean(root, "OnPlanet") ?? false)
            && !(GetBoolean(root, "OnStation") ?? false)
            && Population == 0
            && body.WasFootfalled == false)
        {
            body.IsFirstFootfall = true;
        }
    }

    private bool TryGetBody(
        JsonElement root,
        string bodyIdProperty,
        string? bodyNameProperty,
        out BodyState body)
    {
        body = null!;
        if (!EnsureSystem(root))
        {
            return false;
        }

        var bodyId = GetInt32(root, bodyIdProperty);
        if (bodyId is null)
        {
            return false;
        }

        body = GetOrCreateBody(
            bodyId.Value,
            bodyNameProperty is null ? null : GetString(root, bodyNameProperty));
        return true;
    }

    private bool EnsureSystem(JsonElement root)
    {
        var address = GetInt64(root, "SystemAddress");
        if (address is null)
        {
            return SystemAddress is not null;
        }

        if (SystemAddress is null)
        {
            SetSystem(
                address.Value,
                GetString(root, "SystemName")
                    ?? GetString(root, "StarSystem"));
        }

        return SystemAddress == address;
    }

    private void SetSystem(long address, string? name)
    {
        if (SystemAddress != address)
        {
            SystemAddress = address;
            SystemName = name;
            StarPosition = null;
            Population = 0;
            ExpectedBodyCount = 0;
            HasDiscoveryScan = false;
            AllBodiesFound = false;
            RawNonBodySignalCount = 0;
            CurrentBodyId = null;
            LastDetailedBodyId = null;
            bodies.Clear();
            signals.Clear();
            scanSequence = 0;
            return;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            SystemName = name;
        }
    }

    private static bool MergeBody(
        BodyState target,
        SystemScanBodySnapshot source,
        bool includeBiologicalData)
    {
        var changed = false;
        var hasLiveScan = target.IsScanned;
        if (target.Kind == SystemBodyKind.Unknown
            && source.Kind != SystemBodyKind.Unknown)
        {
            target.Kind = source.Kind;
            changed = true;
        }
        else if (target.Kind == SystemBodyKind.Planet
            && source.Kind == SystemBodyKind.LandablePlanet)
        {
            target.Kind = SystemBodyKind.LandablePlanet;
            changed = true;
        }

        changed |= SetIfMissing(ref target.StarClass, source.StarClass);
        changed |= SetIfMissing(ref target.PlanetClass, source.PlanetClass);
        changed |= SetTrue(ref target.IsLandable, source.IsLandable);
        changed |= SetTrue(ref target.IsTerraformable, source.IsTerraformable);
        changed |= SetTrue(ref target.IsScanned, source.IsScanned);
        changed |= SetTrue(ref target.IsDssComplete, source.IsDssComplete);
        if (!hasLiveScan)
        {
            changed |= SetTrue(ref target.WasDiscovered, source.WasDiscovered);
            changed |= SetTrue(ref target.WasMapped, source.WasMapped);
        }
        if (target.WasFootfalled is null && source.WasFootfalled is not null)
        {
            target.WasFootfalled = source.WasFootfalled;
            changed = true;
        }
        else if (target.WasFootfalled == false && source.WasFootfalled == true)
        {
            target.WasFootfalled = true;
            changed = true;
        }

        changed |= SetTrue(ref target.IsFirstFootfall, source.IsFirstFootfall);
        changed |= SetTrue(ref target.HasRingParent, source.HasRingParent);
        if (target.TidalLock is null && source.TidalLock is not null)
        {
            target.TidalLock = source.TidalLock;
            changed = true;
        }

        changed |= SetIfZero(ref target.Mass, source.Mass);
        changed |= SetIfZero(
            ref target.DistanceFromArrivalLs,
            source.DistanceFromArrivalLs);
        changed |= SetIfZero(ref target.RadiusMeters, source.RadiusMeters);
        changed |= SetIfZero(ref target.SurfaceGravity, source.SurfaceGravity);
        changed |= SetIfZero(
            ref target.SurfaceTemperature,
            source.SurfaceTemperature);
        changed |= SetIfZero(ref target.SurfacePressure, source.SurfacePressure);
        changed |= SetIfZero(ref target.SemiMajorAxis, source.SemiMajorAxis);
        changed |= SetIfZero(
            ref target.AbsoluteMagnitude,
            source.AbsoluteMagnitude);
        changed |= SetIfMissing(
            ref target.Atmosphere,
            source.Atmosphere,
            allowEmpty: true);
        changed |= SetIfMissing(
            ref target.AtmosphereType,
            source.AtmosphereType);
        changed |= SetIfMissing(
            ref target.Volcanism,
            source.Volcanism,
            allowEmpty: true);
        changed |= SetMaximum(
            ref target.BiologicalSignalCount,
            source.BiologicalSignalCount);
        changed |= SetMaximum(
            ref target.GeologicalSignalCount,
            source.GeologicalSignalCount);
        changed |= MergeDictionary(
            ref target.AtmosphereComposition,
            source.AtmosphereComposition);
        changed |= MergeDictionary(ref target.Materials, source.Materials);
        changed |= MergeRings(target, source.Rings);
        if (target.Parents.Count == 0 && source.Parents.Count > 0)
        {
            target.Parents = source.Parents.ToArray();
            target.HasRingParent = source.Parents.FirstOrDefault()?.Kind
                == SystemBodyParentKind.Ring;
            changed = true;
        }

        foreach (var signal in source.AnalyzedGeologicalSignals)
        {
            changed |= target.AnalyzedGeologicalSignals.Add(signal);
        }

        if (!includeBiologicalData)
        {
            return changed;
        }

        foreach (var sourceOrganism in source.Organisms)
        {
            if (string.IsNullOrWhiteSpace(sourceOrganism.Genus))
            {
                continue;
            }

            var existed = target.Organisms.TryGetValue(
                sourceOrganism.Genus,
                out var organism);
            organism ??= target.GetOrCreateOrganism(sourceOrganism.Genus);
            changed |= !existed;
            changed |= SetIfMissing(
                ref organism.GenusLocalized,
                sourceOrganism.GenusLocalized);
            changed |= SetIfMissing(
                ref organism.Species,
                sourceOrganism.Species);
            changed |= SetIfMissing(
                ref organism.SpeciesLocalized,
                sourceOrganism.SpeciesLocalized);
            changed |= SetIfMissing(
                ref organism.Variant,
                sourceOrganism.Variant);
            changed |= SetIfMissing(
                ref organism.VariantLocalized,
                sourceOrganism.VariantLocalized);
            if (organism.EntryId is null && sourceOrganism.EntryId is > 0)
            {
                organism.EntryId = sourceOrganism.EntryId;
                changed = true;
            }

            if (organism.Reward is null && sourceOrganism.Reward is >= 0)
            {
                organism.Reward = sourceOrganism.Reward;
                changed = true;
            }

            changed |= SetTrue(ref organism.IsScanned, sourceOrganism.IsScanned);
            changed |= SetTrue(
                ref organism.IsAnalyzed,
                sourceOrganism.IsAnalyzed);
            changed |= SetTrue(
                ref organism.IsRegionalFirst,
                sourceOrganism.IsRegionalFirst);
        }

        changed |= SetMaximum(
            ref target.BiologicalSignalCount,
            target.Organisms.Count);
        return changed;
    }

    private static bool MergeDictionary(
        ref IReadOnlyDictionary<string, double> target,
        IReadOnlyDictionary<string, double> source)
    {
        if (source.Count == 0)
        {
            return false;
        }

        var merged = new Dictionary<string, double>(
            target,
            StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var pair in source)
        {
            if (!merged.ContainsKey(pair.Key))
            {
                merged[pair.Key] = pair.Value;
                changed = true;
            }
        }

        if (changed)
        {
            target = merged;
        }

        return changed;
    }

    private static bool MergeRings(
        BodyState target,
        IReadOnlyList<SystemRingSnapshot> source)
    {
        if (source.Count == 0)
        {
            return false;
        }

        var rings = target.Rings.ToList();
        var changed = false;
        foreach (var ring in source)
        {
            var index = rings.FindIndex(existing => string.Equals(
                existing.Name,
                ring.Name,
                StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                rings.Add(ring);
                changed = true;
                continue;
            }

            var existing = rings[index];
            var merged = existing with
            {
                RingClass = existing.RingClass ?? ring.RingClass,
                InnerRadius = existing.InnerRadius == 0
                    ? ring.InnerRadius
                    : existing.InnerRadius,
                OuterRadius = existing.OuterRadius == 0
                    ? ring.OuterRadius
                    : existing.OuterRadius,
            };
            if (merged != existing)
            {
                rings[index] = merged;
                changed = true;
            }
        }

        if (changed)
        {
            target.Rings = rings;
        }

        return changed;
    }

    private static bool SetIfMissing(
        ref string? target,
        string? source,
        bool allowEmpty = false)
    {
        if (target is not null
            || source is null
            || !allowEmpty && string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        target = source;
        return true;
    }

    private static bool SetIfZero(ref long target, long source)
    {
        if (target != 0 || source == 0)
        {
            return false;
        }

        target = source;
        return true;
    }

    private static bool SetIfZero(ref double target, double source)
    {
        if (target != 0 || source == 0 || !double.IsFinite(source))
        {
            return false;
        }

        target = source;
        return true;
    }

    private static bool SetMaximum(ref int target, int source)
    {
        if (source <= target)
        {
            return false;
        }

        target = source;
        return true;
    }

    private static bool SetTrue(ref bool target, bool source)
    {
        if (target || !source)
        {
            return false;
        }

        target = true;
        return true;
    }

    private BodyState GetOrCreateBody(int bodyId, string? name)
    {
        if (!bodies.TryGetValue(bodyId, out var body))
        {
            body = new BodyState(bodyId, name ?? $"Body {bodyId}");
            bodies[bodyId] = body;
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            body.Name = name;
        }

        return body;
    }

    private static SystemBodyKind GetBodyKind(
        string name,
        string? starClass,
        string? planetClass,
        bool isLandable)
    {
        if (isLandable)
        {
            return SystemBodyKind.LandablePlanet;
        }

        if (!string.IsNullOrWhiteSpace(starClass))
        {
            return SystemBodyKind.Star;
        }

        if (name.Contains("cluster", StringComparison.OrdinalIgnoreCase))
        {
            return SystemBodyKind.Asteroid;
        }

        if (name.EndsWith("Ring", StringComparison.OrdinalIgnoreCase))
        {
            return SystemBodyKind.Ring;
        }

        if (string.IsNullOrWhiteSpace(planetClass))
        {
            return SystemBodyKind.Barycentre;
        }

        return planetClass.Contains("giant", StringComparison.OrdinalIgnoreCase)
            ? SystemBodyKind.GasGiant
            : SystemBodyKind.Planet;
    }

    private static int GetSignalCount(JsonElement root, string type)
    {
        if (!root.TryGetProperty("Signals", out var signalsElement)
            || signalsElement.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        foreach (var signal in signalsElement.EnumerateArray())
        {
            if (GetString(signal, "Type") == type)
            {
                return GetInt32(signal, "Count") ?? 0;
            }
        }

        return 0;
    }

    private static IReadOnlyDictionary<string, double> ReadComposition(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values.EnumerateArray())
        {
            var name = GetString(value, "Name");
            var percent = GetDouble(value, "Percent");
            if (!string.IsNullOrWhiteSpace(name) && percent is not null)
            {
                result[name] = percent.Value;
            }
        }

        return result;
    }

    private static IReadOnlyList<SystemRingSnapshot> ReadRings(JsonElement root)
    {
        if (!root.TryGetProperty("Rings", out var rings)
            || rings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rings.EnumerateArray()
            .Select(ring => new SystemRingSnapshot(
                GetString(ring, "Name") ?? "Ring",
                GetString(ring, "RingClass"),
                GetDouble(ring, "InnerRad") ?? 0,
                GetDouble(ring, "OuterRad") ?? 0))
            .ToArray();
    }

    private static IReadOnlyList<SystemBodyParentSnapshot>? ReadParents(
        JsonElement root)
    {
        if (!root.TryGetProperty("Parents", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<SystemBodyParentSnapshot>();
        foreach (var parent in parents.EnumerateArray())
        {
            if (parent.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var entry = parent.EnumerateObject().FirstOrDefault();
            if (entry.Value.ValueKind != JsonValueKind.Number
                || !entry.Value.TryGetInt32(out var bodyId)
                || !Enum.TryParse<SystemBodyParentKind>(
                    entry.Name,
                    ignoreCase: false,
                    out var kind))
            {
                continue;
            }

            result.Add(new SystemBodyParentSnapshot(kind, bodyId));
        }

        return result;
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

    private static GalacticCoordinate? GetGalacticCoordinate(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() < 3)
        {
            return null;
        }

        var coordinates = value.EnumerateArray().Take(3).ToArray();
        if (coordinates.Any(coordinate =>
                coordinate.ValueKind != JsonValueKind.Number
                || !coordinate.TryGetDouble(out var number)
                || !double.IsFinite(number)))
        {
            return null;
        }

        return new GalacticCoordinate(
            coordinates[0].GetDouble(),
            coordinates[1].GetDouble(),
            coordinates[2].GetDouble());
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
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

    private sealed class BodyState(int bodyId, string name)
    {
        public int BodyId { get; } = bodyId;

        public string Name = name;

        public SystemBodyKind Kind;

        public string? StarClass;

        public string? PlanetClass;

        public bool IsLandable;

        public bool IsTerraformable;

        public bool IsScanned;

        public bool IsDssComplete;

        public bool DssEfficiencyBonus;

        public bool WasDiscovered;

        public bool WasMapped;

        public bool? WasFootfalled;

        public bool IsFirstFootfall;

        public bool HasRingParent;

        public bool? TidalLock;

        public double Mass;

        public double DistanceFromArrivalLs;

        public double RadiusMeters;

        public double SurfaceGravity;

        public double SurfaceTemperature;

        public double SurfacePressure;

        public double SemiMajorAxis;

        public double AbsoluteMagnitude;

        public string? Atmosphere;

        public string? AtmosphereType;

        public string? Volcanism;

        public int BiologicalSignalCount;

        public int GeologicalSignalCount;

        public long ScanSequence;

        public IReadOnlyDictionary<string, double> AtmosphereComposition =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, double> Materials =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SystemRingSnapshot> Rings = [];

        public IReadOnlyList<SystemBodyParentSnapshot> Parents = [];

        public Dictionary<string, OrganismState> Organisms { get; } =
            new(StringComparer.Ordinal);

        public HashSet<string> AnalyzedGeologicalSignals { get; } =
            new(StringComparer.Ordinal);

        public SystemScanBodySnapshot CreateSnapshot(
            bool isOdyssey,
            string? systemName)
        {
            var bodyClass = Kind == SystemBodyKind.Star ? StarClass : PlanetClass;
            var scanValue = IsScanned
                ? ExplorationValueCalculator.Calculate(
                    bodyClass,
                    IsTerraformable,
                    Mass,
                    !WasDiscovered,
                    false,
                    !WasMapped,
                    isOdyssey,
                    withEfficiencyBonus: false)
                : 0;
            var mappedValue = IsScanned && Kind != SystemBodyKind.Star
                ? ExplorationValueCalculator.Calculate(
                    bodyClass,
                    IsTerraformable,
                    Mass,
                    !WasDiscovered,
                    true,
                    !WasMapped,
                    isOdyssey,
                    withEfficiencyBonus: true)
                : scanValue;
            var currentValue = IsDssComplete
                ? ExplorationValueCalculator.Calculate(
                    bodyClass,
                    IsTerraformable,
                    Mass,
                    !WasDiscovered,
                    true,
                    !WasMapped,
                    isOdyssey,
                    DssEfficiencyBonus)
                : scanValue;

            return new SystemScanBodySnapshot(
                BodyId,
                Name,
                GetShortName(Name, systemName),
                Kind,
                StarClass,
                PlanetClass,
                IsLandable,
                IsTerraformable,
                IsScanned,
                IsDssComplete,
                WasDiscovered,
                WasMapped,
                WasFootfalled,
                IsFirstFootfall,
                HasRingParent,
                TidalLock,
                Mass,
                DistanceFromArrivalLs,
                RadiusMeters,
                SurfaceGravity,
                SurfaceTemperature,
                SurfacePressure,
                SemiMajorAxis,
                AbsoluteMagnitude,
                Atmosphere,
                AtmosphereType,
                Volcanism,
                BiologicalSignalCount,
                Math.Min(
                    BiologicalSignalCount,
                    Organisms.Values.Count(organism => organism.IsAnalyzed)),
                GeologicalSignalCount,
                Math.Min(GeologicalSignalCount, AnalyzedGeologicalSignals.Count),
                scanValue,
                mappedValue,
                currentValue,
                ScanSequence,
                new Dictionary<string, double>(AtmosphereComposition),
                new Dictionary<string, double>(Materials),
                Rings.ToArray(),
                Parents.ToArray(),
                Organisms.Values
                    .OrderBy(organism => organism.Genus, StringComparer.Ordinal)
                    .Select(organism => organism.CreateSnapshot())
                    .ToArray(),
                AnalyzedGeologicalSignals
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
        }

        public OrganismState GetOrCreateOrganism(string genus)
        {
            if (!Organisms.TryGetValue(genus, out var organism))
            {
                organism = new OrganismState(genus);
                Organisms.Add(genus, organism);
            }

            return organism;
        }

        private static string GetShortName(string bodyName, string? systemName)
        {
            var shortName = !string.IsNullOrWhiteSpace(systemName)
                && bodyName.StartsWith(systemName, StringComparison.Ordinal)
                    ? bodyName[systemName.Length..]
                    : bodyName;
            return shortName.Replace(" ", string.Empty, StringComparison.Ordinal);
        }
    }

    private sealed class OrganismState(string genus)
    {
        public string Genus { get; } = genus;

        public string? GenusLocalized;

        public string? Species;

        public string? SpeciesLocalized;

        public string? Variant;

        public string? VariantLocalized;

        public long? EntryId;

        public long? Reward;

        public bool IsScanned;

        public bool IsAnalyzed;

        public bool IsRegionalFirst;

        public SystemOrganismSnapshot CreateSnapshot()
        {
            return new SystemOrganismSnapshot(
                Genus,
                GenusLocalized,
                Species,
                SpeciesLocalized,
                Variant,
                VariantLocalized,
                EntryId,
                Reward,
                IsScanned,
                IsAnalyzed,
                IsRegionalFirst);
        }
    }

    private sealed record SignalState(string? SignalType, bool IsStation)
    {
        public bool IsRoutineStation => IsStation
            || SignalType is "Outpost" or "NavBeacon";
    }
}

public sealed record SystemScanSnapshot(
    string? SystemName,
    long? SystemAddress,
    GalacticCoordinate? StarPosition,
    long Population,
    int ExpectedBodyCount,
    bool HasDiscoveryScan,
    bool AllBodiesFound,
    int FssBodyCount,
    int ScannedBodyCount,
    int DssCompletedBodyCount,
    long CurrentScanValue,
    int RawNonBodySignalCount,
    int NonBodySignalCount,
    int? CurrentBodyId,
    int? LastDetailedBodyId,
    IReadOnlyList<SystemScanBodySnapshot> Bodies)
{
    public static SystemScanSnapshot Empty { get; } = new(
        null,
        null,
        null,
        0,
        0,
        false,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        null,
        null,
        []);

    public bool IsFssComplete => ExpectedBodyCount > 0
        && FssBodyCount >= ExpectedBodyCount;

    public int BiologicalSignalsRemaining => Bodies.Sum(
        body => Math.Max(
            0,
            body.BiologicalSignalCount - body.AnalyzedBiologicalSignalCount));
}

public sealed record SystemScanBodySnapshot(
    int BodyId,
    string Name,
    string ShortName,
    SystemBodyKind Kind,
    string? StarClass,
    string? PlanetClass,
    bool IsLandable,
    bool IsTerraformable,
    bool IsScanned,
    bool IsDssComplete,
    bool WasDiscovered,
    bool WasMapped,
    bool? WasFootfalled,
    bool IsFirstFootfall,
    bool HasRingParent,
    bool? TidalLock,
    double Mass,
    double DistanceFromArrivalLs,
    double RadiusMeters,
    double SurfaceGravity,
    double SurfaceTemperature,
    double SurfacePressure,
    double SemiMajorAxis,
    double AbsoluteMagnitude,
    string? Atmosphere,
    string? AtmosphereType,
    string? Volcanism,
    int BiologicalSignalCount,
    int AnalyzedBiologicalSignalCount,
    int GeologicalSignalCount,
    int AnalyzedGeologicalSignalCount,
    int ScanValue,
    int EstimatedMappedValue,
    int CurrentScanValue,
    long ScanSequence,
    IReadOnlyDictionary<string, double> AtmosphereComposition,
    IReadOnlyDictionary<string, double> Materials,
    IReadOnlyList<SystemRingSnapshot> Rings,
    IReadOnlyList<SystemBodyParentSnapshot> Parents,
    IReadOnlyList<SystemOrganismSnapshot> Organisms,
    IReadOnlyList<string> AnalyzedGeologicalSignals)
{
    public bool CountsTowardFss => Kind is SystemBodyKind.Star
        or SystemBodyKind.GasGiant
        or SystemBodyKind.Planet
        or SystemBodyKind.LandablePlanet;

    public bool IsMappable => Kind is SystemBodyKind.GasGiant
        or SystemBodyKind.Planet
        or SystemBodyKind.LandablePlanet;

    public bool IsEarthLike => PlanetClass?.StartsWith(
        "Earth",
        StringComparison.Ordinal) == true;
}

public sealed record SystemOrganismSnapshot(
    string Genus,
    string? GenusLocalized,
    string? Species,
    string? SpeciesLocalized,
    string? Variant,
    string? VariantLocalized,
    long? EntryId,
    long? Reward,
    bool IsScanned,
    bool IsAnalyzed,
    bool IsRegionalFirst);

public sealed record SystemRingSnapshot(
    string Name,
    string? RingClass,
    double InnerRadius,
    double OuterRadius);

public sealed record SystemBodyParentSnapshot(
    SystemBodyParentKind Kind,
    int BodyId);

public enum SystemBodyParentKind
{
    Null,
    Star,
    Planet,
    Ring,
    Asteroid,
}

public enum SystemBodyKind
{
    Unknown,
    Star,
    GasGiant,
    Planet,
    LandablePlanet,
    Asteroid,
    Ring,
    Barycentre,
}
