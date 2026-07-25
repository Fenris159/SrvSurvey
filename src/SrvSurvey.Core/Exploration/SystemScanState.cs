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
        body.HasRingParent = HasParentType(root, "Ring");
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
        organism.IsCommanderFirst |= GetBoolean(root, "IsNewEntry") ?? false;
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

    private static bool HasParentType(JsonElement root, string parentType)
    {
        if (!root.TryGetProperty("Parents", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var firstParent = parents.EnumerateArray().FirstOrDefault();
        return firstParent.ValueKind == JsonValueKind.Object
            && firstParent.TryGetProperty(parentType, out _);
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

        public string Name { get; set; } = name;

        public SystemBodyKind Kind { get; set; }

        public string? StarClass { get; set; }

        public string? PlanetClass { get; set; }

        public bool IsLandable { get; set; }

        public bool IsTerraformable { get; set; }

        public bool IsScanned { get; set; }

        public bool IsDssComplete { get; set; }

        public bool DssEfficiencyBonus { get; set; }

        public bool WasDiscovered { get; set; }

        public bool WasMapped { get; set; }

        public bool? WasFootfalled { get; set; }

        public bool IsFirstFootfall { get; set; }

        public bool HasRingParent { get; set; }

        public bool? TidalLock { get; set; }

        public double Mass { get; set; }

        public double DistanceFromArrivalLs { get; set; }

        public double RadiusMeters { get; set; }

        public double SurfaceGravity { get; set; }

        public double SurfaceTemperature { get; set; }

        public double SurfacePressure { get; set; }

        public double SemiMajorAxis { get; set; }

        public double AbsoluteMagnitude { get; set; }

        public string? Atmosphere { get; set; }

        public string? AtmosphereType { get; set; }

        public string? Volcanism { get; set; }

        public int BiologicalSignalCount { get; set; }

        public int GeologicalSignalCount { get; set; }

        public long ScanSequence { get; set; }

        public IReadOnlyDictionary<string, double> AtmosphereComposition { get; set; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, double> Materials { get; set; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SystemRingSnapshot> Rings { get; set; } = [];

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

        public string? GenusLocalized { get; set; }

        public string? Species { get; set; }

        public string? SpeciesLocalized { get; set; }

        public string? Variant { get; set; }

        public string? VariantLocalized { get; set; }

        public long? EntryId { get; set; }

        public long? Reward { get; set; }

        public bool IsAnalyzed { get; set; }

        public bool IsCommanderFirst { get; set; }

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
                IsAnalyzed,
                IsCommanderFirst);
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
    bool IsAnalyzed,
    bool IsCommanderFirst);

public sealed record SystemRingSnapshot(
    string Name,
    string? RingClass,
    double InnerRadius,
    double OuterRadius);

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
