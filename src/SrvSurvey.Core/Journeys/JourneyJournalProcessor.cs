using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

public sealed class JourneyJournalProcessor
{
    private readonly ExobiologyReferenceCatalog exobiologyCatalog;
    private readonly Dictionary<BodyKey, BodyState> bodies = [];
    private bool isOdyssey;

    public JourneyJournalProcessor(
        JourneyDocument journey,
        ExobiologyReferenceCatalog exobiologyCatalog,
        bool isOdyssey)
    {
        Journey = journey ?? throw new ArgumentNullException(nameof(journey));
        this.exobiologyCatalog = exobiologyCatalog
            ?? throw new ArgumentNullException(nameof(exobiologyCatalog));
        this.isOdyssey = isOdyssey;
    }

    public JourneyDocument Journey { get; private set; }

    public void UpdateJourney(JourneyDocument journey)
    {
        ArgumentNullException.ThrowIfNull(journey);
        if (!string.Equals(
                journey.FrontierId,
                Journey.FrontierId,
                StringComparison.Ordinal)
            || !string.Equals(
                journey.FileName,
                Journey.FileName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The replacement document must represent the same journey.",
                nameof(journey));
        }

        Journey = journey;
    }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        if (journalEvent.Timestamp is not { } timestamp
            || timestamp < Journey.Watermark)
        {
            return false;
        }

        ApplyEvent(journalEvent);
        Journey = Journey with { Watermark = timestamp };
        return true;
    }

    public JourneyReplaySummary ApplyCatchUp(
        IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var startingWatermark = Journey.Watermark;
        var processed = 0;
        var ignored = 0;

        foreach (var journalEvent in journalEvents)
        {
            if (journalEvent.Timestamp is not { } timestamp)
            {
                ignored++;
                continue;
            }

            if (timestamp <= startingWatermark)
            {
                PrimeEvent(journalEvent);
                ignored++;
                continue;
            }

            if (Apply(journalEvent))
            {
                processed++;
            }
            else
            {
                ignored++;
            }
        }

        return new JourneyReplaySummary(Journey, processed, ignored);
    }

    private void PrimeEvent(JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName is "Fileheader" or "LoadGame")
        {
            isOdyssey = GetBoolean(journalEvent.Payload, "Odyssey") ?? isOdyssey;
        }
        else if (journalEvent.EventName == "Scan")
        {
            PrimeBody(journalEvent.Payload, Journey.CurrentSystem);
        }
    }

    private void ApplyEvent(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "Fileheader":
            case "LoadGame":
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
                ApplyBodyCount(root, "BodyCount");
                break;

            case "FSSAllBodiesFound":
                ApplyBodyCount(root, "Count");
                break;

            case "Scan":
                ApplyScan(root);
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
                UpdateCurrent(visit => visit with
                {
                    Counts = visit.Counts with
                    {
                        Screenshots = checked(visit.Counts.Screenshots + 1),
                    },
                });
                break;
        }
    }

    private void ApplyArrival(JsonElement root, DateTimeOffset timestamp)
    {
        if (!JourneyJournalHistoryReader.TryGetSystemReference(
                root,
                out var systemReference))
        {
            return;
        }

        var visits = Journey.VisitedSystems.ToList();
        var currentIndex = FindCurrentIndex(visits);
        if (currentIndex >= 0
            && visits[currentIndex].StarSystem.SystemAddress
                == systemReference.SystemAddress)
        {
            return;
        }

        if (currentIndex >= 0)
        {
            visits[currentIndex] = visits[currentIndex] with
            {
                Departed = timestamp,
            };
        }

        visits.Add(new JourneySystemVisit(
            systemReference,
            timestamp,
            null,
            JourneyCounts.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null));
        Journey = Journey with { VisitedSystems = visits };
    }

    private void ApplyDeparture(JsonElement root, DateTimeOffset timestamp)
    {
        if (!string.Equals(
                GetString(root, "JumpType"),
                "Hyperspace",
                StringComparison.Ordinal))
        {
            return;
        }

        UpdateCurrent(visit => visit with { Departed = timestamp });
    }

    private void ApplyBodyCount(JsonElement root, string propertyName)
    {
        if (GetInt32(root, propertyName) is not { } bodyCount)
        {
            return;
        }

        UpdateCurrent(visit => visit with
        {
            Counts = visit.Counts with { BodyCount = bodyCount },
        });
    }

    private void ApplyScan(JsonElement root)
    {
        var current = Journey.CurrentSystem;
        var body = PrimeBody(root, current);
        if (current is null
            || body is null
            || (GetString(root, "PlanetClass") is null
                && GetString(root, "StarType") is null))
        {
            return;
        }

        var scanned = current.BodiesScanned is null
            ? []
            : new HashSet<int>(current.BodiesScanned);
        if (!scanned.Add(body.Key.BodyId))
        {
            return;
        }

        var reward = CalculateReward(
            body,
            isMapped: false,
            withEfficiencyBonus: false);
        body.ScanReward = reward;
        UpdateCurrent(visit => visit with
        {
            BodiesScanned = scanned,
            Counts = visit.Counts with
            {
                BodyScans = checked(visit.Counts.BodyScans + 1),
                Stars = GetString(root, "StarType") is null
                    ? visit.Counts.Stars
                    : checked(visit.Counts.Stars + 1),
                ExplorationRewards = checked(
                    visit.Counts.ExplorationRewards + reward),
            },
        });
    }

    private void ApplyDetailedSurfaceScan(JsonElement root)
    {
        var current = Journey.CurrentSystem;
        if (current is null || GetInt32(root, "BodyID") is not { } bodyId)
        {
            return;
        }

        var rewardDelta = 0;
        var key = new BodyKey(current.StarSystem.SystemAddress, bodyId);
        if (bodies.TryGetValue(key, out var body))
        {
            var probesUsed = GetInt32(root, "ProbesUsed") ?? int.MaxValue;
            var efficiencyTarget = GetInt32(root, "EfficiencyTarget") ?? -1;
            var mappedReward = CalculateReward(
                body,
                isMapped: true,
                withEfficiencyBonus: probesUsed <= efficiencyTarget);
            rewardDelta = Math.Max(0, mappedReward - body.ScanReward);
        }

        UpdateCurrent(visit => visit with
        {
            Counts = visit.Counts with
            {
                DetailedSurfaceScans = checked(
                    visit.Counts.DetailedSurfaceScans + 1),
                ExplorationRewards = checked(
                    visit.Counts.ExplorationRewards + rewardDelta),
            },
        });
    }

    private void ApplyTouchdown(JsonElement root)
    {
        var current = Journey.CurrentSystem;
        var bodyName = GetString(root, "Body");
        if (current is null || string.IsNullOrWhiteSpace(bodyName))
        {
            return;
        }

        var starSystemName = GetString(root, "StarSystem")
            ?? current.StarSystem.Name;
        var shortName = bodyName
            .Replace(starSystemName, string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(shortName))
        {
            shortName = bodyName;
        }

        var landedOn = current.LandedOn is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(
                current.LandedOn,
                StringComparer.Ordinal);
        landedOn[shortName] = checked(landedOn.GetValueOrDefault(shortName) + 1);
        UpdateCurrent(visit => visit with
        {
            LandedOn = landedOn,
            Counts = visit.Counts with
            {
                Touchdowns = checked(visit.Counts.Touchdowns + 1),
            },
        });
    }

    private void ApplySurfaceSignals(JsonElement root)
    {
        var current = Journey.CurrentSystem;
        if (current is null
            || !root.TryGetProperty("Signals", out var signals)
            || signals.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var counts = current.SurfaceSignals is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(
                current.SurfaceSignals,
                StringComparer.Ordinal);
        foreach (var signal in signals.EnumerateArray())
        {
            var type = GetString(signal, "Type");
            var count = GetInt32(signal, "Count");
            if (string.IsNullOrWhiteSpace(type) || count is null)
            {
                continue;
            }

            var key = type
                .Replace("$SAA_SignalType_", string.Empty, StringComparison.Ordinal)
                .Replace(";", string.Empty, StringComparison.Ordinal);
            counts[key] = checked(counts.GetValueOrDefault(key) + count.Value);
        }

        UpdateCurrent(visit => visit with { SurfaceSignals = counts });
    }

    private void ApplyFssSignal(JsonElement root)
    {
        var current = Journey.CurrentSystem;
        var signalType = GetString(root, "SignalType");
        if (current is null || string.IsNullOrWhiteSpace(signalType))
        {
            return;
        }

        var signals = current.FssSignals is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(
                current.FssSignals,
                StringComparer.Ordinal);
        signals[signalType] = checked(signals.GetValueOrDefault(signalType) + 1);
        UpdateCurrent(visit => visit with { FssSignals = signals });
    }

    private void ApplyCodexEntry(JsonElement root)
    {
        var current = Journey.CurrentSystem;
        if (current is null || GetInt64(root, "EntryID") is not { } entryId)
        {
            return;
        }

        var scanned = current.CodexScanned is null
            ? []
            : new HashSet<long>(current.CodexScanned);
        var firstScanInSystem = scanned.Add(entryId);
        var isNew = GetBoolean(root, "IsNewEntry") ?? false;
        var newEntries = current.CodexNew is null
            ? []
            : new HashSet<string>(current.CodexNew, StringComparer.Ordinal);
        var name = GetString(root, "Name_Localised")
            ?? GetString(root, "Name");
        if (isNew && !string.IsNullOrWhiteSpace(name))
        {
            newEntries.Add(name);
        }

        var subCategories = current.SubCategories is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(
                current.SubCategories,
                StringComparer.Ordinal);
        var subCategory = GetString(root, "SubCategory_Localised");
        if (firstScanInSystem && !string.IsNullOrWhiteSpace(subCategory))
        {
            subCategories[subCategory] = checked(
                subCategories.GetValueOrDefault(subCategory) + 1);
        }

        UpdateCurrent(visit => visit with
        {
            CodexScanned = scanned,
            CodexNew = newEntries.Count == 0 ? null : newEntries,
            SubCategories = subCategories.Count == 0 ? null : subCategories,
            Counts = visit.Counts with
            {
                NewCodexEntries = isNew
                    ? checked(visit.Counts.NewCodexEntries + 1)
                    : visit.Counts.NewCodexEntries,
            },
        });
    }

    private void ApplyOrganicScan(JsonElement root)
    {
        if (!string.Equals(
                GetString(root, "ScanType"),
                "Analyse",
                StringComparison.Ordinal))
        {
            return;
        }

        var reward = exobiologyCatalog
            .FindBySpecies(GetString(root, "Species"))?.Reward ?? 0;
        UpdateCurrent(visit => visit with
        {
            Counts = visit.Counts with
            {
                Organisms = checked(visit.Counts.Organisms + 1),
                ExobiologyRewards = checked(
                    visit.Counts.ExobiologyRewards + checked((int)reward)),
            },
        });
    }

    private BodyState? PrimeBody(
        JsonElement root,
        JourneySystemVisit? current)
    {
        var bodyId = GetInt32(root, "BodyID");
        var systemAddress = GetInt64(root, "SystemAddress")
            ?? current?.StarSystem.SystemAddress;
        var bodyClass = GetString(root, "PlanetClass")
            ?? GetString(root, "StarType");
        if (bodyId is null
            || systemAddress is null
            || string.IsNullOrWhiteSpace(bodyClass))
        {
            return null;
        }

        var key = new BodyKey(systemAddress.Value, bodyId.Value);
        var body = bodies.GetValueOrDefault(key) ?? new BodyState(key);
        body.BodyClass = bodyClass;
        body.IsTerraformable = GetString(root, "TerraformState") == "Terraformable";
        var planetMass = GetDouble(root, "MassEM");
        body.Mass = planetMass is > 0
            ? planetMass.Value
            : GetDouble(root, "StellarMass") ?? 0;
        body.IsFirstDiscoverer = !(GetBoolean(root, "WasDiscovered") ?? false);
        body.IsFirstMapped = !(GetBoolean(root, "WasMapped") ?? false);
        body.ScanReward = CalculateReward(
            body,
            isMapped: false,
            withEfficiencyBonus: false);
        bodies[key] = body;
        return body;
    }

    private int CalculateReward(
        BodyState body,
        bool isMapped,
        bool withEfficiencyBonus)
    {
        return ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest(
                body.BodyClass,
                body.IsTerraformable,
                body.Mass,
                body.IsFirstDiscoverer,
                isMapped,
                body.IsFirstMapped,
                isOdyssey,
                withEfficiencyBonus));
    }

    private void UpdateCurrent(
        Func<JourneySystemVisit, JourneySystemVisit> update)
    {
        var visits = Journey.VisitedSystems.ToList();
        var index = FindCurrentIndex(visits);
        if (index < 0)
        {
            return;
        }

        visits[index] = update(visits[index]);
        Journey = Journey with { VisitedSystems = visits };
    }

    private static int FindCurrentIndex(List<JourneySystemVisit> visits)
    {
        for (var index = visits.Count - 1; index >= 0; index--)
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
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
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

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : null;
    }

    private readonly record struct BodyKey(long SystemAddress, int BodyId);

    private sealed class BodyState(BodyKey key)
    {
        public BodyKey Key { get; } = key;

        public string? BodyClass { get; set; }

        public bool IsTerraformable { get; set; }

        public double Mass { get; set; }

        public bool IsFirstDiscoverer { get; set; }

        public bool IsFirstMapped { get; set; }

        public int ScanReward { get; set; }
    }
}

public sealed record JourneyReplaySummary(
    JourneyDocument Journey,
    int ProcessedEventCount,
    int IgnoredEventCount);
