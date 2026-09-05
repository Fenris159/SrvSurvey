using System.Text.Json;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Search;

public sealed class BoxelSurveyStatsState
{
    private const string SystemAddressPropertyName = "SystemAddress";
    private readonly Dictionary<string, WorkingBoxel> boxels = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> addressToPrefix = [];
    private readonly HashSet<string> dirtyPrefixes = new(StringComparer.Ordinal);
    private string frontierId = string.Empty;
    private string? currentPrefix;
    private bool isOdyssey = true;

    public BoxelSurveyStatsState(BoxelSurveyStatsCatalog? seed = null)
    {
        Reset(seed);
    }

    public int Version { get; private set; }

    public int BoxelCount => boxels.Count;

    public bool TreatNavBeaconAsFullyScanned { get; set; }

    public string FrontierId => frontierId;

    public BoxelSurveyBoxelSnapshot? Current
        => currentPrefix is not null && TryGet(currentPrefix, out var snapshot)
            ? snapshot
            : null;

    public IReadOnlyList<BoxelSurveyIndexEntry> GetIndex()
        => boxels.Values
            .Select(boxel => CreateSnapshot(boxel).ToIndexEntry())
            .OrderBy(entry => entry.Prefix, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> GetDirtyPrefixes()
        => dirtyPrefixes.Order(StringComparer.Ordinal).ToArray();

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        return journalEvent.EventName switch
        {
            "Fileheader" => ApplyOdyssey(journalEvent.Payload),
            "LoadGame" => true, // Expansion ownership does not change galaxy rewards.
            "FSDJump" or "Location" or "CarrierJump" => ApplyJump(journalEvent),
            "Scan" => ApplyScan(journalEvent.Payload),
            "SAAScanComplete" => ApplySaaScanComplete(journalEvent.Payload),
            "FSSDiscoveryScan" => ApplyFssDiscoveryScan(journalEvent.Payload),
            "FSSAllBodiesFound" => ApplyFssAllBodiesFound(journalEvent.Payload),
            "NavBeaconScan" => ApplyNavBeaconScan(journalEvent.Payload),
            _ => false,
        };
    }

    public bool IngestSnapshot(
        SystemScanSnapshot snapshot,
        DateTimeOffset? visitedAt = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return IngestSnapshotCore(snapshot, visitedAt, replaceBodies: false);
    }

    public bool IngestSystemFile(
        SystemScanSnapshot snapshot,
        DateTimeOffset? lastVisited)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return IngestSnapshotCore(
            snapshot,
            lastVisited,
            replaceBodies: false,
            recomputeValues: true);
    }

    public bool ImportDocument(BoxelSurveyBoxelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Prefix))
        {
            return false;
        }

        var working = WorkingBoxel.FromDocument(document);
        boxels[working.Prefix] = working;
        foreach (var address in working.Systems.Values
                     .Select(system => system.SystemAddress)
                     .Where(address => address > 0))
        {
            addressToPrefix[address] = working.Prefix;
        }

        dirtyPrefixes.Remove(working.Prefix);
        return true;
    }

    public bool TryCreateDocument(string prefix, out BoxelSurveyBoxelDocument document)
    {
        document = null!;
        if (!boxels.TryGetValue(prefix, out var boxel) || !boxel.IsHydrated)
        {
            return false;
        }

        document = boxel.ToDocument();
        return true;
    }

    public bool HasLoadedDocument(string prefix)
        => boxels.TryGetValue(prefix, out var boxel) && boxel.IsHydrated;

    public bool ShouldLoadDocument(string prefix)
        => boxels.TryGetValue(prefix, out var boxel) && !boxel.IsHydrated;

    public bool UnloadDocument(string prefix)
    {
        if (!boxels.TryGetValue(prefix, out var boxel) || !boxel.IsHydrated)
        {
            return false;
        }

        if (dirtyPrefixes.Contains(prefix))
        {
            return false;
        }

        var snapshot = CreateSnapshot(boxel);
        foreach (var address in boxel.Systems.Values
                     .Select(system => system.SystemAddress)
                     .Where(address => address > 0))
        {
            addressToPrefix.Remove(address);
        }

        boxels[prefix] = WorkingBoxel.FromIndexEntry(snapshot.ToIndexEntry());
        return true;
    }

    public bool TryGet(string prefix, out BoxelSurveyBoxelSnapshot snapshot)
    {
        snapshot = BoxelSurveyBoxelSnapshot.Empty;
        if (!boxels.TryGetValue(prefix, out var boxel))
        {
            return false;
        }

        snapshot = CreateSnapshot(boxel);
        return true;
    }

    public BoxelSurveyBoxelSnapshot Rollup(IEnumerable<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        var visited = 0;
        var implied = 0;
        var fssComplete = 0;
        var navBeacon = 0;
        var fssBodies = 0;
        double? minHelium = null;
        double? maxHelium = null;
        long scanValue = 0;
        long currentValue = 0;
        long mappedValue = 0;
        var otherTf = 0;
        DateTimeOffset? lastVisited = null;
        var classes = new Dictionary<BoxelPlanetClass, BoxelSurveyClassCounts>();
        var systems = new List<BoxelSurveySystemContribution>();
        string? firstPrefix = null;
        char massCode = BoxelAddress.MinimumMassCode;
        long? boxelId64 = null;

        foreach (var prefix in prefixes.Distinct(StringComparer.Ordinal))
        {
            if (!TryGet(prefix, out var part) || string.IsNullOrWhiteSpace(part.Prefix))
            {
                continue;
            }

            if (firstPrefix is null)
            {
                firstPrefix = part.Prefix;
                massCode = part.MassCode;
                boxelId64 = part.BoxelId64;
            }

            visited += part.Visited;
            implied += part.ImpliedPopulation;
            fssComplete += part.FssCompleteCount;
            navBeacon += part.NavBeaconCount;
            fssBodies += part.FssDiscoveryBodyCountSum;
            scanValue += part.ScanValue;
            currentValue += part.CurrentValue;
            mappedValue += part.MappedPotentialValue;
            otherTf += part.OtherTerraformableCount;
            minHelium = MinNullable(minHelium, part.MinHeliumPercent);
            maxHelium = MaxNullable(maxHelium, part.MaxHeliumPercent);
            lastVisited = Later(lastVisited, part.LastVisited);
            foreach (var pair in part.Classes)
            {
                classes[pair.Key] = classes.TryGetValue(pair.Key, out var existing)
                    ? existing.Add(pair.Value)
                    : pair.Value;
            }

            systems.AddRange(part.Systems);
        }

        if (firstPrefix is null)
        {
            return BoxelSurveyBoxelSnapshot.Empty;
        }

        return new BoxelSurveyBoxelSnapshot(
            firstPrefix,
            massCode,
            boxelId64,
            lastVisited,
            visited,
            implied,
            fssComplete,
            navBeacon,
            fssBodies,
            minHelium,
            maxHelium,
            scanValue,
            currentValue,
            mappedValue,
            otherTf,
            classes,
            systems);
    }

    public BoxelSurveyStatsCatalog CreateSnapshot()
        => new(
            frontierId,
            BoxelSurveyStatsCatalog.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            GetIndex());

    public BoxelSurveyStatsState CreateWorkingCopy()
    {
        var copy = new BoxelSurveyStatsState(CreateSnapshot())
        {
            TreatNavBeaconAsFullyScanned = TreatNavBeaconAsFullyScanned,
            isOdyssey = isOdyssey,
        };
        foreach (var boxel in boxels.Values.Where(boxel => boxel.IsHydrated))
        {
            copy.ImportDocument(boxel.ToDocument());
        }

        copy.dirtyPrefixes.UnionWith(dirtyPrefixes);
        return copy;
    }

    public void ReplaceWith(BoxelSurveyStatsState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var documents = source.boxels.Values
            .Where(boxel => boxel.IsHydrated)
            .Select(boxel => boxel.ToDocument())
            .ToArray();
        Reset(source.CreateSnapshot());
        TreatNavBeaconAsFullyScanned = source.TreatNavBeaconAsFullyScanned;
        isOdyssey = source.isOdyssey;
        foreach (var document in documents)
        {
            ImportDocument(document);
        }

        dirtyPrefixes.UnionWith(source.dirtyPrefixes);
    }

    public void Reset(BoxelSurveyStatsCatalog? seed = null)
    {
        boxels.Clear();
        addressToPrefix.Clear();
        dirtyPrefixes.Clear();
        currentPrefix = null;
        isOdyssey = true;
        frontierId = seed?.FrontierId ?? string.Empty;
        if (seed is not null)
        {
            foreach (var entry in seed.Index)
            {
                if (string.IsNullOrWhiteSpace(entry.Prefix)
                    || boxels.ContainsKey(entry.Prefix))
                {
                    continue;
                }

                boxels[entry.Prefix] = WorkingBoxel.FromIndexEntry(entry);
            }
        }

        Version++;
    }

    public void ClearDirty() => dirtyPrefixes.Clear();

    public void MarkClean(string prefix) => dirtyPrefixes.Remove(prefix);

    internal static bool TryOpenGeneratedBoxel(
        long address,
        string? systemName,
        out BoxelAddress boxel)
    {
        boxel = null!;
        if (address <= 0
            || !BoxelAddress.TryFromSystemAddress(address, systemName, out var decoded)
            || decoded is null
            || !BoxelAddress.TryParse(systemName, out var parsed)
            || parsed is null)
        {
            return false;
        }

        boxel = decoded;
        return true;
    }

    private bool ApplyOdyssey(JsonElement root)
    {
        var odyssey = GetBoolean(root, "Odyssey");
        if (odyssey is null || odyssey.Value == isOdyssey)
        {
            return true;
        }

        isOdyssey = odyssey.Value;
        Version++;
        return true;
    }

    private bool ApplyJump(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        var address = GetInt64(root, SystemAddressPropertyName) ?? 0;
        var name = GetString(root, "StarSystem") ?? GetString(root, "SystemName");
        if (!TryOpenGeneratedBoxel(address, name, out var boxel))
        {
            return false;
        }

        var system = OpenSystem(boxel, address, journalEvent.Timestamp, out var changed);
        currentPrefix = system.Boxel.Prefix;
        if (changed)
        {
            Touch(system.Boxel);
        }

        return true;
    }

    private bool ApplyScan(JsonElement root)
    {
        var address = GetInt64(root, SystemAddressPropertyName) ?? 0;
        if (!TryAttachKnownSystem(address, out var system))
        {
            return false;
        }

        currentPrefix = system.Boxel.Prefix;
        var bodyId = GetInt32(root, "BodyID");
        if (bodyId is null or < 0)
        {
            return true;
        }

        var planetClass = GetString(root, "PlanetClass");
        if (!BoxelPlanetClassifier.TryFromPlanetClass(planetClass, out var classified))
        {
            return true;
        }

        var terraformable = BoxelPlanetClassifier.IsTerraformable(
            GetString(root, "TerraformState"));
        var landable = GetBoolean(root, "Landable") ?? false;
        var atmosphereType = GetString(root, "AtmosphereType");
        var atmospheric = BoxelPlanetClassifier.IsAtmosphericLandable(
            landable,
            atmosphereType);
        var mass = GetDouble(root, "MassEM") ?? 0;
        var wasDiscovered = GetBoolean(root, "WasDiscovered") ?? false;
        var wasMapped = GetBoolean(root, "WasMapped") ?? false;
        var composition = ReadJournalComposition(root);
        BoxelPlanetClassifier.TryGetHeliumPercent(composition, out var helium);
        var heliumPercent = helium > 0 ? helium : (double?)null;
        system.Bodies.TryGetValue(bodyId.Value, out var existing);
        var dssComplete = existing?.DssComplete ?? false;
        var dssEfficiency = existing?.DssEfficiencyBonus ?? false;
        var values = BoxelSurveyValueCalculator.Calculate(
            new BoxelSurveyValueRequest(
                BoxelPlanetClassifier.ToPlanetClassString(classified),
                terraformable,
                mass,
                wasDiscovered,
                wasMapped,
                dssComplete,
                dssEfficiency,
                isOdyssey));
        system.Bodies[bodyId.Value] = new WorkingBody
        {
            BodyId = bodyId.Value,
            Class = classified,
            Terraformable = terraformable,
            Landable = landable,
            Atmospheric = atmospheric,
            MassEm = mass,
            HeliumPercent = heliumPercent,
            ScanValue = values.Scan,
            CurrentValue = values.Current,
            MappedPotentialValue = values.Mapped,
            WasDiscovered = wasDiscovered,
            WasMapped = wasMapped,
            DssComplete = dssComplete,
            DssEfficiencyBonus = dssEfficiency,
        };
        system.Recalculate();
        Touch(system.Boxel);
        return true;
    }

    private bool ApplySaaScanComplete(JsonElement root)
    {
        var address = GetInt64(root, SystemAddressPropertyName) ?? 0;
        if (!TryAttachKnownSystem(address, out var system))
        {
            return false;
        }

        currentPrefix = system.Boxel.Prefix;
        var bodyId = GetInt32(root, "BodyID");
        if (bodyId is null or < 0)
        {
            return true;
        }

        var efficiency = (GetInt32(root, "ProbesUsed") ?? int.MaxValue)
            <= (GetInt32(root, "EfficiencyTarget") ?? -1);
        if (!system.Bodies.TryGetValue(bodyId.Value, out var body))
        {
            body = new WorkingBody { BodyId = bodyId.Value };
            system.Bodies[bodyId.Value] = body;
        }

        body.DssComplete = true;
        body.DssEfficiencyBonus = efficiency;
        if (body.Class != BoxelPlanetClass.Unknown)
        {
            var values = BoxelSurveyValueCalculator.Calculate(
                new BoxelSurveyValueRequest(
                    BoxelPlanetClassifier.ToPlanetClassString(body.Class),
                    body.Terraformable,
                    body.MassEm,
                    body.WasDiscovered,
                    body.WasMapped,
                    DssComplete: true,
                    DssEfficiencyBonus: efficiency,
                    IsOdyssey: isOdyssey));
            body.ScanValue = values.Scan;
            body.CurrentValue = values.Current;
            body.MappedPotentialValue = values.Mapped;
        }

        system.Recalculate();
        Touch(system.Boxel);
        return true;
    }

    private bool ApplyFssDiscoveryScan(JsonElement root)
    {
        var address = GetInt64(root, SystemAddressPropertyName) ?? 0;
        if (!TryAttachKnownSystem(address, out var system))
        {
            return false;
        }

        currentPrefix = system.Boxel.Prefix;
        var bodyCount = GetInt32(root, "BodyCount") ?? 0;
        if (bodyCount > system.FssDiscoveryBodyCount)
        {
            system.FssDiscoveryBodyCount = bodyCount;
            system.Recalculate();
            Touch(system.Boxel);
        }

        return true;
    }

    private bool ApplyFssAllBodiesFound(JsonElement root)
    {
        var address = GetInt64(root, SystemAddressPropertyName) ?? 0;
        if (!TryAttachKnownSystem(address, out var system))
        {
            return false;
        }

        currentPrefix = system.Boxel.Prefix;
        var count = GetInt32(root, "Count") ?? 0;
        var changed = !system.AllBodiesFound;
        system.AllBodiesFound = true;
        if (count > system.FssDiscoveryBodyCount)
        {
            system.FssDiscoveryBodyCount = count;
            changed = true;
        }

        if (changed)
        {
            system.Recalculate();
            Touch(system.Boxel);
        }

        return true;
    }

    private bool ApplyNavBeaconScan(JsonElement root)
    {
        var address = GetInt64(root, SystemAddressPropertyName) ?? 0;
        if (!TryAttachKnownSystem(address, out var system))
        {
            return false;
        }

        currentPrefix = system.Boxel.Prefix;
        if (!system.NavBeaconScanned)
        {
            system.NavBeaconScanned = true;
            system.Recalculate();
            Touch(system.Boxel);
        }

        return true;
    }

    private bool IngestSnapshotCore(
        SystemScanSnapshot snapshot,
        DateTimeOffset? visitedAt,
        bool replaceBodies,
        bool recomputeValues = false)
    {
        if (snapshot.SystemAddress is not > 0
            || !TryOpenGeneratedBoxel(
                snapshot.SystemAddress.Value,
                snapshot.SystemName,
                out var boxel))
        {
            return false;
        }

        var system = OpenSystem(
            boxel,
            snapshot.SystemAddress.Value,
            visitedAt,
            out var changed);
        currentPrefix = system.Boxel.Prefix;
        if (!system.AllBodiesFound && snapshot.AllBodiesFound)
        {
            system.AllBodiesFound = true;
            changed = true;
        }

        if (snapshot.ExpectedBodyCount > system.FssDiscoveryBodyCount)
        {
            system.FssDiscoveryBodyCount = snapshot.ExpectedBodyCount;
            changed = true;
        }

        var snapshotBodies = BuildSnapshotBodies(system, snapshot, recomputeValues);
        if (SynchronizeBodies(system, snapshot, snapshotBodies, replaceBodies))
        {
            changed = true;
        }

        if (changed)
        {
            system.Recalculate();
            Touch(system.Boxel);
        }

        return true;
    }

    private Dictionary<int, WorkingBody> BuildSnapshotBodies(
        WorkingSystem system,
        SystemScanSnapshot snapshot,
        bool recomputeValues)
    {
        var snapshotBodies = new Dictionary<int, WorkingBody>();
        foreach (var source in snapshot.Bodies)
        {
            if (source.BodyId < 0
                || !BoxelPlanetClassifier.TryFromPlanetClass(
                    source.PlanetClass,
                    out var classified))
            {
                continue;
            }

            system.Bodies.TryGetValue(source.BodyId, out var existing);
            BoxelPlanetClassifier.TryGetHeliumPercent(
                source.AtmosphereComposition,
                out var helium);
            var dssEfficiency = !recomputeValues
                && InferDssEfficiency(source, existing);
            var values = recomputeValues
                ? BoxelSurveyValueCalculator.Calculate(
                    new BoxelSurveyValueRequest(
                        BoxelPlanetClassifier.ToPlanetClassString(classified),
                        source.IsTerraformable,
                        source.Mass,
                        source.WasDiscovered,
                        source.WasMapped,
                        source.IsDssComplete,
                        dssEfficiency,
                        isOdyssey))
                : (source.ScanValue, source.CurrentScanValue, source.EstimatedMappedValue);
            snapshotBodies[source.BodyId] = new WorkingBody
            {
                BodyId = source.BodyId,
                Class = classified,
                Terraformable = source.IsTerraformable,
                Landable = source.IsLandable,
                Atmospheric = BoxelPlanetClassifier.IsAtmosphericLandable(
                    source.IsLandable,
                    source.AtmosphereType),
                MassEm = source.Mass,
                HeliumPercent = helium > 0 ? helium : null,
                ScanValue = values.Item1,
                CurrentValue = values.Item2,
                MappedPotentialValue = values.Item3,
                WasDiscovered = source.WasDiscovered,
                WasMapped = source.WasMapped,
                DssComplete = source.IsDssComplete,
                DssEfficiencyBonus = dssEfficiency,
            };
        }

        return snapshotBodies;
    }

    private static bool SynchronizeBodies(
        WorkingSystem system,
        SystemScanSnapshot snapshot,
        Dictionary<int, WorkingBody> snapshotBodies,
        bool replaceBodies)
    {
        var changed = false;
        if (replaceBodies || CanReplaceBodies(system, snapshot, snapshotBodies.Keys))
        {
            foreach (var extraId in system.Bodies.Keys
                         .Where(id => !snapshotBodies.ContainsKey(id))
                         .ToArray())
            {
                system.Bodies.Remove(extraId);
                changed = true;
            }
        }

        foreach (var pair in snapshotBodies)
        {
            if (!system.Bodies.TryGetValue(pair.Key, out var existing)
                || !existing.HasSameFacts(pair.Value))
            {
                changed = true;
                system.Bodies[pair.Key] = pair.Value;
            }
        }

        return changed;
    }

    private static bool InferDssEfficiency(
        SystemScanBodySnapshot source,
        WorkingBody? existing)
    {
        if (existing?.DssComplete == true)
        {
            return existing.DssEfficiencyBonus;
        }

        if (!source.IsDssComplete)
        {
            return false;
        }

        return source.CurrentScanValue == source.EstimatedMappedValue
            && source.EstimatedMappedValue > 0;
    }

    private static bool CanReplaceBodies(
        WorkingSystem system,
        SystemScanSnapshot snapshot,
        IReadOnlyCollection<int> snapshotIds)
    {
        if (!snapshot.AllBodiesFound)
        {
            return false;
        }

        var storedClassified = system.Bodies.Values
            .Where(body => body.Class != BoxelPlanetClass.Unknown)
            .Select(body => body.BodyId)
            .ToHashSet();
        return snapshotIds.Count >= storedClassified.Count
            && storedClassified.IsSubsetOf(snapshotIds);
    }

    private WorkingSystem OpenSystem(
        BoxelAddress boxel,
        long address,
        DateTimeOffset? visitedAt,
        out bool changed)
    {
        var workingBoxel = GetOrCreateBoxel(boxel);
        workingBoxel.IsHydrated = true;
        changed = false;
        if (address > 0)
        {
            addressToPrefix[address] = workingBoxel.Prefix;
        }

        if (!workingBoxel.Systems.TryGetValue(boxel.GeneratedName, out var system))
        {
            if (address > 0
                && workingBoxel.TryGetSystemByAddress(address, out system))
            {
                workingBoxel.Systems.Remove(system.GeneratedName);
            }
            else
            {
                system = new WorkingSystem(workingBoxel)
                {
                    GeneratedName = boxel.GeneratedName,
                    N2 = boxel.N2,
                };
            }

            system.GeneratedName = boxel.GeneratedName;
            system.N2 = boxel.N2;
            workingBoxel.Systems[system.GeneratedName] = system;
            changed = true;
        }

        if (address > 0 && system.SystemAddress != address)
        {
            system.SystemAddress = address;
            changed = true;
        }

        var lastVisited = Later(system.LastVisited, visitedAt);
        if (lastVisited != system.LastVisited)
        {
            system.LastVisited = lastVisited;
            workingBoxel.LastVisited = Later(workingBoxel.LastVisited, visitedAt);
            changed = true;
        }

        return system;
    }

    private bool TryAttachKnownSystem(long systemAddress, out WorkingSystem system)
    {
        system = null!;
        if (systemAddress <= 0)
        {
            return false;
        }

        if (addressToPrefix.TryGetValue(systemAddress, out var prefix)
            && boxels.TryGetValue(prefix, out var boxel)
            && boxel.TryGetSystemByAddress(systemAddress, out system))
        {
            return true;
        }

        foreach (var candidate in boxels.Values)
        {
            if (candidate.TryGetSystemByAddress(systemAddress, out system))
            {
                addressToPrefix[systemAddress] = candidate.Prefix;
                return true;
            }
        }

        return false;
    }

    private WorkingBoxel GetOrCreateBoxel(BoxelAddress boxel)
    {
        if (boxels.TryGetValue(boxel.Prefix, out var existing))
        {
            existing.EnsureIdentity(boxel);
            return existing;
        }

        var created = WorkingBoxel.FromAddress(boxel);
        boxels[created.Prefix] = created;
        return created;
    }

    private BoxelSurveyBoxelSnapshot CreateSnapshot(WorkingBoxel boxel)
    {
        if (!boxel.IsHydrated)
        {
            return boxel.ToIndexSnapshot();
        }

        var systems = new List<BoxelSurveySystemContribution>(boxel.Systems.Count);
        var classes = new Dictionary<BoxelPlanetClass, BoxelSurveyClassCounts>();
        var otherTf = 0;
        var fssComplete = 0;
        var navBeacon = 0;
        var fssBodies = 0;
        long scanValue = 0;
        long currentValue = 0;
        long mappedValue = 0;
        var maxN2 = 0;
        foreach (var system in boxel.Systems.Values
                     .OrderBy(candidate => candidate.N2)
                     .ThenBy(candidate => candidate.GeneratedName, StringComparer.Ordinal))
        {
            systems.Add(system.ToContribution());
            if (system.N2 > maxN2)
            {
                maxN2 = system.N2;
            }

            if (system.AllBodiesFound
                || (TreatNavBeaconAsFullyScanned && system.NavBeaconScanned))
            {
                fssComplete++;
            }

            if (system.NavBeaconScanned)
            {
                navBeacon++;
            }

            fssBodies += system.FssDiscoveryBodyCount;
            scanValue += system.ScanValue;
            currentValue += system.CurrentValue;
            mappedValue += system.MappedPotentialValue;
            AccumulateClassCounts(system, classes, ref otherTf);
        }

        return new BoxelSurveyBoxelSnapshot(
            boxel.Prefix,
            boxel.MassCode,
            boxel.BoxelId64,
            boxel.LastVisited,
            boxel.Systems.Count,
            boxel.Systems.Count == 0 ? 0 : maxN2 + 1,
            fssComplete,
            navBeacon,
            fssBodies,
            boxel.MinHeliumPercent,
            boxel.MaxHeliumPercent,
            scanValue,
            currentValue,
            mappedValue,
            otherTf,
            classes,
            systems);
    }

    private static void AccumulateClassCounts(
        WorkingSystem system,
        Dictionary<BoxelPlanetClass, BoxelSurveyClassCounts> classes,
        ref int otherTf)
    {
        foreach (var body in system.Bodies.Values)
        {
            if (body.Class == BoxelPlanetClass.Unknown)
            {
                continue;
            }

            classes[body.Class] = classes.TryGetValue(body.Class, out var counts)
                ? counts.AddBody(body.Terraformable, body.Landable, body.Atmospheric)
                : BoxelSurveyClassCounts.Zero.AddBody(
                    body.Terraformable,
                    body.Landable,
                    body.Atmospheric);
            if (body.Terraformable
                && !BoxelPlanetClassifier.ShowsTerraformableColumn(body.Class))
            {
                otherTf++;
            }
        }
    }

    private void Touch(WorkingBoxel boxel)
    {
        dirtyPrefixes.Add(boxel.Prefix);
        Version++;
    }

    private static Dictionary<string, double> ReadJournalComposition(JsonElement root)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("AtmosphereComposition", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

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

    private static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        return second is not null && second > first ? second : first;
    }

    private static double? MinNullable(double? first, double? second)
    {
        if (first is null)
        {
            return second;
        }

        return second is null ? first : Math.Min(first.Value, second.Value);
    }

    private static double? MaxNullable(double? first, double? second)
    {
        if (first is null)
        {
            return second;
        }

        return second is null ? first : Math.Max(first.Value, second.Value);
    }

    private static string? GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static bool? GetBoolean(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

    private static double? GetDouble(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : null;

    private static long? GetInt64(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : null;

    private static int? GetInt32(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;

    private sealed class WorkingBoxel
    {
        public string Prefix { get; private set; } = string.Empty;

        public char MassCode { get; private set; } = BoxelAddress.MinimumMassCode;

        public long? BoxelId64 { get; private set; }

        public DateTimeOffset? LastVisited { get; set; }

        public bool IsHydrated { get; set; }

        public BoxelSurveyIndexEntry? IndexSeed { get; private set; }

        public Dictionary<string, WorkingSystem> Systems { get; } = new(StringComparer.Ordinal);

        public double? MinHeliumPercent
            => MinOf(Systems.Values.Select(system => system.MinHeliumPercent));

        public double? MaxHeliumPercent
            => MaxOf(Systems.Values.Select(system => system.MaxHeliumPercent));

        public static WorkingBoxel FromAddress(BoxelAddress boxel)
        {
            var working = new WorkingBoxel();
            working.EnsureIdentity(boxel);
            working.IsHydrated = true;
            return working;
        }

        public static WorkingBoxel FromIndexEntry(BoxelSurveyIndexEntry entry)
            => new()
            {
                Prefix = entry.Prefix,
                MassCode = entry.MassCode,
                BoxelId64 = entry.BoxelId64,
                LastVisited = entry.LastVisited,
                IsHydrated = false,
                IndexSeed = entry,
            };

        public static WorkingBoxel FromDocument(BoxelSurveyBoxelDocument document)
        {
            var identity = ParsePrefix(document.Prefix);
            var working = new WorkingBoxel
            {
                Prefix = document.Prefix,
                MassCode = identity.MassCode,
                BoxelId64 = document.BoxelId64 ?? identity.BoxelId64,
                LastVisited = document.LastVisited,
                IsHydrated = true,
            };
            foreach (var contribution in document.Systems)
            {
                working.Systems[contribution.GeneratedName] = WorkingSystem.FromContribution(
                    working,
                    contribution);
            }

            return working;
        }

        public void EnsureIdentity(BoxelAddress boxel)
        {
            Prefix = boxel.Prefix;
            MassCode = boxel.MassCode;
            if (boxel.WithSystemNumber(0).TryGetSystemAddress(out var id64))
            {
                BoxelId64 = id64;
            }
        }

        public bool TryGetSystemByAddress(long address, out WorkingSystem system)
        {
            foreach (var candidate in Systems.Values)
            {
                if (candidate.SystemAddress == address)
                {
                    system = candidate;
                    return true;
                }
            }

            system = null!;
            return false;
        }

        public BoxelSurveyBoxelDocument ToDocument()
            => new(
                Prefix,
                BoxelId64,
                LastVisited,
                MinHeliumPercent,
                MaxHeliumPercent,
                Systems.Values
                    .OrderBy(system => system.N2)
                    .ThenBy(system => system.GeneratedName, StringComparer.Ordinal)
                    .Select(system => system.ToContribution())
                    .ToArray());

        public BoxelSurveyBoxelSnapshot ToIndexSnapshot()
        {
            var seed = IndexSeed;
            return new BoxelSurveyBoxelSnapshot(
                Prefix,
                MassCode,
                BoxelId64,
                seed?.LastVisited ?? LastVisited,
                seed?.VisitedSystemCount ?? 0,
                seed?.ImpliedPopulation ?? 0,
                seed?.FssCompleteCount ?? 0,
                seed?.NavBeaconCount ?? 0,
                0,
                seed?.MinHeliumPercent,
                seed?.MaxHeliumPercent,
                0,
                seed?.CurrentValue ?? 0,
                seed?.MappedPotentialValue ?? 0,
                0,
                new Dictionary<BoxelPlanetClass, BoxelSurveyClassCounts>(),
                []);
        }

        private static (char MassCode, long? BoxelId64) ParsePrefix(string prefix)
        {
            if (BoxelAddress.TryParse(prefix + "0", out var boxel) && boxel is not null)
            {
                long? id64 = boxel.WithSystemNumber(0).TryGetSystemAddress(out var encoded)
                    ? encoded
                    : null;
                return (boxel.MassCode, id64);
            }

            return (BoxelAddress.MinimumMassCode, null);
        }

        private static double? MinOf(IEnumerable<double?> values)
        {
            double? min = null;
            foreach (var value in values)
            {
                min = MinNullable(min, value);
            }

            return min;
        }

        private static double? MaxOf(IEnumerable<double?> values)
        {
            double? max = null;
            foreach (var value in values)
            {
                max = MaxNullable(max, value);
            }

            return max;
        }
    }

    private sealed class WorkingSystem(WorkingBoxel boxel)
    {
        public WorkingBoxel Boxel { get; } = boxel;

        public string GeneratedName { get; set; } = string.Empty;

        public long SystemAddress { get; set; }

        public int N2 { get; set; }

        public DateTimeOffset? LastVisited { get; set; }

        public int FssDiscoveryBodyCount { get; set; }

        public bool AllBodiesFound { get; set; }

        public bool NavBeaconScanned { get; set; }

        public double? MinHeliumPercent { get; private set; }

        public double? MaxHeliumPercent { get; private set; }

        public long ScanValue { get; private set; }

        public long CurrentValue { get; private set; }

        public long MappedPotentialValue { get; private set; }

        public Dictionary<int, WorkingBody> Bodies { get; } = [];

        public static WorkingSystem FromContribution(
            WorkingBoxel boxel,
            BoxelSurveySystemContribution contribution)
        {
            var system = new WorkingSystem(boxel)
            {
                GeneratedName = contribution.GeneratedName,
                SystemAddress = contribution.SystemAddress,
                N2 = contribution.N2,
                LastVisited = contribution.LastVisited,
                FssDiscoveryBodyCount = contribution.FssDiscoveryBodyCount,
                AllBodiesFound = contribution.AllBodiesFound,
                NavBeaconScanned = contribution.NavBeaconScanned,
            };
            foreach (var body in contribution.Bodies)
            {
                system.Bodies[body.BodyId] = WorkingBody.FromContribution(body);
            }

            system.Recalculate();
            return system;
        }

        public void Recalculate()
        {
            double? minHelium = null;
            double? maxHelium = null;
            long scan = 0;
            long current = 0;
            long mapped = 0;
            foreach (var body in Bodies.Values)
            {
                if (body.Class == BoxelPlanetClass.Unknown)
                {
                    continue;
                }

                scan += body.ScanValue;
                current += body.CurrentValue;
                mapped += body.MappedPotentialValue;
                minHelium = MinNullable(minHelium, body.HeliumPercent);
                maxHelium = MaxNullable(maxHelium, body.HeliumPercent);
            }

            ScanValue = scan;
            CurrentValue = current;
            MappedPotentialValue = mapped;
            MinHeliumPercent = minHelium;
            MaxHeliumPercent = maxHelium;
        }

        public BoxelSurveySystemContribution ToContribution()
            => new(
                GeneratedName,
                SystemAddress,
                N2,
                LastVisited,
                FssDiscoveryBodyCount,
                AllBodiesFound,
                NavBeaconScanned,
                MinHeliumPercent,
                MaxHeliumPercent,
                ScanValue,
                CurrentValue,
                MappedPotentialValue,
                Bodies.Values
                    .OrderBy(body => body.BodyId)
                    .Select(body => body.ToContribution())
                    .ToArray());
    }

    private sealed class WorkingBody
    {
        public int BodyId { get; set; }

        public BoxelPlanetClass Class { get; set; }

        public bool Terraformable { get; set; }

        public bool Landable { get; set; }

        public bool Atmospheric { get; set; }

        public double MassEm { get; set; }

        public double? HeliumPercent { get; set; }

        public int ScanValue { get; set; }

        public int CurrentValue { get; set; }

        public int MappedPotentialValue { get; set; }

        public bool WasDiscovered { get; set; }

        public bool WasMapped { get; set; }

        public bool DssComplete { get; set; }

        public bool DssEfficiencyBonus { get; set; }

        public bool HasSameFacts(WorkingBody other)
            => BodyId == other.BodyId
                && Class == other.Class
                && Terraformable == other.Terraformable
                && Landable == other.Landable
                && Atmospheric == other.Atmospheric
                && ApproximatelyEqual(MassEm, other.MassEm)
                && ApproximatelyEqual(HeliumPercent, other.HeliumPercent)
                && ScanValue == other.ScanValue
                && CurrentValue == other.CurrentValue
                && MappedPotentialValue == other.MappedPotentialValue
                && WasDiscovered == other.WasDiscovered
                && WasMapped == other.WasMapped
                && DssComplete == other.DssComplete
                && DssEfficiencyBonus == other.DssEfficiencyBonus;

        private static bool ApproximatelyEqual(double first, double second)
        {
            if (!double.IsFinite(first) || !double.IsFinite(second))
            {
                return first.CompareTo(second) == 0;
            }

            const double tolerance = 1e-9;
            var scale = Math.Max(1d, Math.Max(Math.Abs(first), Math.Abs(second)));
            return Math.Abs(first - second) <= tolerance * scale;
        }

        private static bool ApproximatelyEqual(double? first, double? second)
            => first is null || second is null
                ? first is null && second is null
                : ApproximatelyEqual(first.Value, second.Value);

        public static WorkingBody FromContribution(BoxelSurveyBodyContribution body)
            => new()
            {
                BodyId = body.BodyId,
                Class = body.Class,
                Terraformable = body.Terraformable,
                Landable = body.Landable,
                Atmospheric = body.Atmospheric,
                MassEm = body.MassEm,
                HeliumPercent = body.HeliumPercent,
                ScanValue = body.ScanValue,
                CurrentValue = body.CurrentValue,
                MappedPotentialValue = body.MappedPotentialValue,
                WasDiscovered = body.WasDiscovered,
                WasMapped = body.WasMapped,
                DssComplete = body.DssComplete,
                DssEfficiencyBonus = body.DssEfficiencyBonus,
            };

        public BoxelSurveyBodyContribution ToContribution()
            => new(
                BodyId,
                Class,
                Terraformable,
                Landable,
                Atmospheric,
                MassEm,
                HeliumPercent,
                ScanValue,
                CurrentValue,
                MappedPotentialValue,
                WasDiscovered,
                WasMapped,
                DssComplete,
                DssEfficiencyBonus);
    }
}
