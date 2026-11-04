using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyJournalProcessor
{
    private readonly ExobiologyReferenceCatalog exobiologyCatalog;
    private readonly Dictionary<BodyKey, BodyState> bodies = [];
    private bool isOdyssey;

    public JourneyJournalProcessor(
        JourneyDocument journey,
        ExobiologyReferenceCatalog exobiologyCatalog,
        bool isOdyssey
    )
    {
        ArgumentNullException.ThrowIfNull(journey);
        State = InitializeRecordTracking(journey);
        this.exobiologyCatalog = exobiologyCatalog ?? throw new ArgumentNullException(nameof(exobiologyCatalog));
        this.isOdyssey = isOdyssey;
        InitializeObservationHistory(journey);
    }

    private JourneyDocument state = null!;
    private bool snapshotDirty;
    private JourneyDocument State
    {
        get => state;
        set
        {
            state = value;
            snapshotDirty = true;
        }
    }
    public JourneyDocument Journey => Snapshot();

    public void UpdateJourney(JourneyDocument journey)
    {
        lock (stateLock)
        {
            ArgumentNullException.ThrowIfNull(journey);
            if (
                !string.Equals(journey.FrontierId, State.FrontierId, StringComparison.Ordinal)
                || !string.Equals(journey.FileName, State.FileName, StringComparison.Ordinal)
            )
            {
                throw new ArgumentException(
                    "The replacement document must represent the same journey.",
                    nameof(journey)
                );
            }

            State = InitializeRecordTracking(journey);
            InitializeObservationHistory(journey);
            HasRecordMetadataChanges = false;
        }
    }

    private void PrimeEvent(JournalEventEnvelope journalEvent, bool replaceEqual = true)
    {
        if (journalEvent.EventName == "Fileheader")
        {
            isOdyssey = GetBoolean(journalEvent.Payload, "Odyssey") ?? isOdyssey;
        }
        else if (journalEvent.EventName == "Scan")
        {
            PrimeBody(journalEvent.Payload, CurrentVisit, journalEvent.Timestamp!.Value, replaceEqual);
        }
    }

    private void ApplyEvent(JournalEventEnvelope journalEvent)
    {
        JsonElement root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "Fileheader":
                isOdyssey = GetBoolean(root, "Odyssey") ?? isOdyssey;
                break;

            case "Location":
            case "CarrierJump":
            case "FSDJump":
                ApplyArrival(root, journalEvent.Timestamp!.Value);
                break;

            case "StartJump":
                ApplyDeparture(root, journalEvent.Timestamp!.Value);
                break;

            case "FSSDiscoveryScan":
                ApplyBodyCount(root, "BodyCount", journalEvent.Timestamp!.Value);
                break;

            case "FSSAllBodiesFound":
                ApplyBodyCount(root, "Count", journalEvent.Timestamp!.Value);
                break;

            case "Scan":
                ApplyScan(root, journalEvent.Timestamp!.Value);
                break;

            case "SAAScanComplete":
                ApplyDetailedSurfaceScan(root);
                break;

            case "Touchdown":
                ApplyTouchdown(root);
                break;

            case "FSSBodySignals":
                ApplySurfaceSignals(root);
                break;

            case "FSSSignalDiscovered":
                ApplyFssSignal(root);
                break;

            case "CodexEntry":
                ApplyCodexEntry(root);
                break;

            case "ScanOrganic":
                ApplyOrganicScan(root);
                break;

            case "Screenshot":
                UpdateCurrent(visit =>
                    visit with
                    {
                        Counts = visit.Counts with { Screenshots = checked(visit.Counts.Screenshots + 1) },
                    }
                );
                break;
        }
    }

    private void ApplyArrival(JsonElement root, DateTimeOffset timestamp)
    {
        if (!JourneyJournalHistoryReader.TryGetSystemReference(root, out JourneySystemReference? systemReference))
        {
            return;
        }

        ApplyArrivalAtTime(systemReference, timestamp);
    }

    private void ApplyDeparture(JsonElement root, DateTimeOffset timestamp)
    {
        if (!string.Equals(GetString(root, "JumpType"), "Hyperspace", StringComparison.Ordinal))
        {
            return;
        }

        UpdateCurrent(visit =>
            visit with
            {
                Departed = visit.Departed is { } prior && prior < timestamp ? prior : timestamp,
            }
        );
    }

    private void ApplyBodyCount(JsonElement root, string propertyName, DateTimeOffset timestamp)
    {
        if (GetInt32(root, propertyName) is not { } bodyCount)
        {
            return;
        }

        bool final = propertyName == "Count";
        UpdateCurrent(visit =>
        {
            DateTimeOffset? observed =
                visit.BodyCountObservedAt ?? (visit.Counts.BodyCount > 0 ? State.Watermark : null);
            if (observed > timestamp || (observed == timestamp && visit.BodyCountIsFinal && !final))
            {
                return visit;
            }
            return visit with
            {
                Counts = visit.Counts with { BodyCount = bodyCount },
                BodyCountObservedAt = timestamp,
                BodyCountIsFinal = final,
            };
        });
    }

    private void ApplyScan(JsonElement root, DateTimeOffset timestamp)
    {
        JourneySystemVisit? current = CurrentVisit;
        BodyState? body = PrimeBody(root, current, timestamp);
        if (
            current is null
            || body is null
            || (GetString(root, "PlanetClass") is null && GetString(root, "StarType") is null)
        )
        {
            return;
        }

        HashSet<int> scanned = current.BodiesScanned is null ? [] : [.. current.BodiesScanned];
        if (!scanned.Add(body.Key.BodyId))
        {
            return;
        }

        int reward = CalculateReward(body, isMapped: false, withEfficiencyBonus: false);
        body.ScanReward = reward;
        UpdateCurrent(visit =>
            visit with
            {
                BodiesScanned = scanned,
                Counts = visit.Counts with
                {
                    BodyScans = checked(visit.Counts.BodyScans + 1),
                    Stars = GetString(root, "StarType") is null ? visit.Counts.Stars : checked(visit.Counts.Stars + 1),
                    ExplorationRewards = checked(visit.Counts.ExplorationRewards + reward),
                },
            }
        );
    }

    private void ApplyDetailedSurfaceScan(JsonElement root)
    {
        JourneySystemVisit? current = CurrentVisit;
        if (current is null || GetInt32(root, "BodyID") is not { } bodyId)
        {
            return;
        }

        int rewardDelta = 0;
        var key = new BodyKey(current.StarSystem.SystemAddress, bodyId);
        if (bodies.TryGetValue(key, out BodyState? body))
        {
            int probesUsed = GetInt32(root, "ProbesUsed") ?? int.MaxValue;
            int efficiencyTarget = GetInt32(root, "EfficiencyTarget") ?? -1;
            int mappedReward = CalculateReward(
                body,
                isMapped: true,
                withEfficiencyBonus: probesUsed <= efficiencyTarget
            );
            rewardDelta = Math.Max(0, mappedReward - body.ScanReward);
        }

        UpdateCurrent(visit =>
            visit with
            {
                Counts = visit.Counts with
                {
                    DetailedSurfaceScans = checked(visit.Counts.DetailedSurfaceScans + 1),
                    ExplorationRewards = checked(visit.Counts.ExplorationRewards + rewardDelta),
                },
            }
        );
    }

    private void ApplyTouchdown(JsonElement root)
    {
        JourneySystemVisit? current = CurrentVisit;
        string? bodyName = GetString(root, "Body");
        if (current is null || string.IsNullOrWhiteSpace(bodyName))
        {
            return;
        }

        string starSystemName = GetString(root, "StarSystem") ?? current.StarSystem.Name;
        string shortName = bodyName
            .Replace(starSystemName, string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(shortName))
        {
            shortName = bodyName;
        }

        Dictionary<string, int> landedOn = current.LandedOn is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(current.LandedOn, StringComparer.Ordinal);
        landedOn[shortName] = checked(landedOn.GetValueOrDefault(shortName) + 1);
        UpdateCurrent(visit =>
            visit with
            {
                LandedOn = landedOn,
                Counts = visit.Counts with { Touchdowns = checked(visit.Counts.Touchdowns + 1) },
            }
        );
    }

    private void ApplySurfaceSignals(JsonElement root)
    {
        JourneySystemVisit? current = CurrentVisit;
        if (
            current is null
            || !root.TryGetProperty("Signals", out JsonElement signals)
            || signals.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        Dictionary<string, int> counts = current.SurfaceSignals is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(current.SurfaceSignals, StringComparer.Ordinal);
        foreach (JsonElement signal in signals.EnumerateArray())
        {
            string? type = GetString(signal, "Type");
            int? count = GetInt32(signal, "Count");
            if (string.IsNullOrWhiteSpace(type) || count is null)
            {
                continue;
            }

            string key = type.Replace("$SAA_SignalType_", string.Empty, StringComparison.Ordinal)
                .Replace(";", string.Empty, StringComparison.Ordinal);
            counts[key] = checked(counts.GetValueOrDefault(key) + count.Value);
        }

        UpdateCurrent(visit => visit with { SurfaceSignals = counts });
    }

    private void ApplyFssSignal(JsonElement root)
    {
        JourneySystemVisit? current = CurrentVisit;
        string? signalType = GetString(root, "SignalType");
        if (current is null || string.IsNullOrWhiteSpace(signalType))
        {
            return;
        }

        Dictionary<string, int> signals = current.FssSignals is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(current.FssSignals, StringComparer.Ordinal);
        signals[signalType] = checked(signals.GetValueOrDefault(signalType) + 1);
        UpdateCurrent(visit => visit with { FssSignals = signals });
    }

    private void ApplyCodexEntry(JsonElement root)
    {
        JourneySystemVisit? current = CurrentVisit;
        if (current is null || GetInt64(root, "EntryID") is not { } entryId)
        {
            return;
        }

        HashSet<long> scanned = current.CodexScanned is null ? [] : [.. current.CodexScanned];
        bool firstScanInSystem = scanned.Add(entryId);
        bool isNew = GetBoolean(root, "IsNewEntry") ?? false;
        HashSet<string> newEntries = current.CodexNew is null
            ? []
            : new HashSet<string>(current.CodexNew, StringComparer.Ordinal);
        string? name = GetString(root, "Name_Localised") ?? GetString(root, "Name");
        if (isNew && !string.IsNullOrWhiteSpace(name))
        {
            newEntries.Add(name);
        }

        Dictionary<string, int> subCategories = current.SubCategories is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(current.SubCategories, StringComparer.Ordinal);
        string? subCategory = GetString(root, "SubCategory_Localised");
        if (firstScanInSystem && !string.IsNullOrWhiteSpace(subCategory))
        {
            subCategories[subCategory] = checked(subCategories.GetValueOrDefault(subCategory) + 1);
        }

        UpdateCurrent(visit =>
            visit with
            {
                CodexScanned = scanned,
                CodexNew = newEntries.Count == 0 ? null : newEntries,
                SubCategories = subCategories.Count == 0 ? null : subCategories,
                Counts = visit.Counts with
                {
                    NewCodexEntries = isNew ? checked(visit.Counts.NewCodexEntries + 1) : visit.Counts.NewCodexEntries,
                },
            }
        );
    }

    private void ApplyOrganicScan(JsonElement root)
    {
        if (!string.Equals(GetString(root, "ScanType"), "Analyse", StringComparison.Ordinal))
        {
            return;
        }

        long reward = exobiologyCatalog.FindBySpecies(GetString(root, "Species"))?.Reward ?? 0;
        UpdateCurrent(visit =>
            visit with
            {
                Counts = visit.Counts with
                {
                    Organisms = checked(visit.Counts.Organisms + 1),
                    ExobiologyRewards = checked(visit.Counts.ExobiologyRewards + reward),
                },
            }
        );
    }

    private BodyState? PrimeBody(
        JsonElement root,
        JourneySystemVisit? current,
        DateTimeOffset timestamp,
        bool replaceEqual = true
    )
    {
        int? bodyId = GetInt32(root, "BodyID");
        long? systemAddress = GetInt64(root, "SystemAddress") ?? current?.StarSystem.SystemAddress;
        string? bodyClass = GetString(root, "PlanetClass") ?? GetString(root, "StarType");
        if (bodyId is null || systemAddress is null || string.IsNullOrWhiteSpace(bodyClass))
        {
            return null;
        }

        var key = new BodyKey(systemAddress.Value, bodyId.Value);
        BodyState body = bodies.GetValueOrDefault(key) ?? new BodyState(key);
        if (body.ObservedAt > timestamp || (!replaceEqual && body.ObservedAt == timestamp))
        {
            return body;
        }
        body.ObservedAt = timestamp;
        body.BodyClass = bodyClass;
        body.IsTerraformable = GetString(root, "TerraformState") == "Terraformable";
        double? planetMass = GetDouble(root, "MassEM");
        body.Mass = planetMass is > 0 ? planetMass.Value : GetDouble(root, "StellarMass") ?? 0;
        body.IsFirstDiscoverer = !(GetBoolean(root, "WasDiscovered") ?? false);
        body.IsFirstMapped = !(GetBoolean(root, "WasMapped") ?? false);
        body.ScanReward = CalculateReward(body, isMapped: false, withEfficiencyBonus: false);
        bodies[key] = body;
        return body;
    }

    private int CalculateReward(BodyState body, bool isMapped, bool withEfficiencyBonus)
    {
        return ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = body.BodyClass,
                IsTerraformable = body.IsTerraformable,
                Mass = body.Mass,
                IsFirstDiscoverer = body.IsFirstDiscoverer,
                IsMapped = isMapped,
                IsFirstMapped = body.IsFirstMapped,
                IsOdyssey = isOdyssey,
                WithEfficiencyBonus = withEfficiencyBonus,
            }
        );
    }

    private void UpdateCurrent(Func<JourneySystemVisit, JourneySystemVisit> update)
    {
        var visits = State.VisitedSystems.ToList();
        int index = applyingVisitIndex ?? FindCurrentIndex(visits);
        if (index < 0)
        {
            return;
        }

        visits[index] = update(visits[index]);
        State = State with { VisitedSystems = visits };
    }

    private static int FindCurrentIndex(List<JourneySystemVisit> visits)
    {
        for (int index = visits.Count - 1; index >= 0; index--)
        {
            if (visits[index].Departed is null)
            {
                return index;
            }
        }

        return -1;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number)
            ? number
            : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number)
            ? number
            : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return
            root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double number)
            ? number
            : null;
    }

    private readonly record struct BodyKey(long SystemAddress, int BodyId);

    private sealed record BodyState(BodyKey Key)
    {
        public DateTimeOffset ObservedAt { get; set; }
        public string? BodyClass { get; set; }

        public bool IsTerraformable { get; set; }

        public double Mass { get; set; }

        public bool IsFirstDiscoverer { get; set; }

        public bool IsFirstMapped { get; set; }

        public int ScanReward { get; set; }
    }
}

public sealed record JourneyReplaySummary(JourneyDocument Journey, int ProcessedEventCount, int IgnoredEventCount);
