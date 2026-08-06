using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed class ColonizationSystemSiteJournalTracker
{
    private static readonly IReadOnlyDictionary<string, string>
        SignalBuildTypes = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Installation"] = "installation?",
            ["Outpost"] = "outpost?",
            ["StationCoriolis"] = "no_truss?",
            ["StationBernalSphere"] = "ocellus",
            ["StationONeilOrbis"] = "orbis?",
            ["StationAsteroid"] = "asteroid",
            ["StationDodec"] = "dodec?",
        };

    private readonly HashSet<int> knownBodyNumbers;
    private readonly HashSet<int> scannedBodyNumbers = [];
    private readonly HashSet<string> orbitalSignalNames = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> nextIdentifierValue;

    public ColonizationSystemSiteJournalTracker(
        long systemAddress,
        string systemName,
        IEnumerable<int>? knownBodyNumbers = null,
        Func<long>? nextIdentifierValue = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        SystemAddress = systemAddress;
        SystemName = systemName.Trim();
        this.knownBodyNumbers = knownBodyNumbers?.ToHashSet() ?? [];
        this.nextIdentifierValue = nextIdentifierValue
            ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public long SystemAddress { get; }

    public string SystemName { get; }

    public int? ExpectedBodyCount { get; private set; }

    public int ScannedBodyCount => scannedBodyNumbers.Count;

    public bool HasDiscoveryScan { get; private set; }

    public bool HasAllBodiesFound { get; private set; }

    public bool HasNavBeaconScan { get; private set; }

    public bool IsBodyScanComplete => HasAllBodiesFound
        || (ExpectedBodyCount is > 0
            && ScannedBodyCount >= ExpectedBodyCount.Value);

    public int ApplyJournalEvents(
        IList<ColonizationSystemSite> sites,
        IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(journalEvents);
        return journalEvents.Count(journalEvent =>
            ApplyJournalEvent(sites, journalEvent));
    }

    public bool ApplyJournalEvent(
        IList<ColonizationSystemSite> sites,
        JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(journalEvent);
        var root = journalEvent.Payload;
        if (!MatchesSystem(root))
        {
            return false;
        }

        switch (journalEvent.EventName)
        {
            case "FSSDiscoveryScan":
                HasDiscoveryScan = true;
                ExpectedBodyCount = GetInt32(root, "BodyCount")
                    ?? ExpectedBodyCount;
                return false;
            case "FSSAllBodiesFound":
                HasAllBodiesFound = true;
                ExpectedBodyCount = GetInt32(root, "Count")
                    ?? ExpectedBodyCount;
                return false;
            case "NavBeaconScan":
                HasNavBeaconScan = true;
                return false;
            case "Scan":
            case "ScanBaryCentre":
                if (GetInt32(root, "BodyID") is { } bodyNumber)
                {
                    scannedBodyNumbers.Add(bodyNumber);
                }

                return false;
            case "FSSSignalDiscovered":
                return ApplySignal(sites, root);
            case "ApproachSettlement":
                return ApplyApproachSettlement(sites, root);
            case "Docked":
                return ApplyDocked(sites, root);
            default:
                return false;
        }
    }

    public bool ApplyStatusDestination(
        IList<ColonizationSystemSite> sites,
        EliteStatus? status,
        bool captureUnknownSurfaceSite)
    {
        ArgumentNullException.ThrowIfNull(sites);
        var destination = status?.Destination;
        if (destination is null
            || destination.System != SystemAddress
            || !IsUsableName(destination.Name)
            || !IsKnownBody(destination.Body))
        {
            return false;
        }

        var name = destination.Name!.Trim();
        var index = FindSiteIndex(sites, name);
        if (index >= 0)
        {
            var site = sites[index];
            var updated = site with
            {
                BodyNumber = destination.Body,
                Status = ColonizationSystemSiteStatus.Complete,
            };
            if (KnownFieldsEqual(site, updated))
            {
                return false;
            }

            sites[index] = updated;
            return true;
        }

        if (!captureUnknownSurfaceSite
            || name.StartsWith(SystemName, StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                "Construction site",
                StringComparison.OrdinalIgnoreCase)
            || orbitalSignalNames.Contains(name))
        {
            return false;
        }

        sites.Insert(0, CreateSite(
            sites,
            name,
            destination.Body,
            "settlement?"));
        return true;
    }

    private bool ApplySignal(
        IList<ColonizationSystemSite> sites,
        JsonElement root)
    {
        var name = GetString(root, "SignalName")?.Trim();
        var type = GetString(root, "SignalType")?.Trim();
        if (!IsUsableName(name)
            || HasNonEmptyString(root, "SignalName_Localised")
            || string.Equals(type, "FleetCarrier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "SquadronCarrier", StringComparison.OrdinalIgnoreCase)
            || name!.Contains(
                "Construction Site",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        orbitalSignalNames.Add(name);
        if (FindSiteIndex(sites, name) >= 0)
        {
            return false;
        }

        var buildType = type is not null
            ? SignalBuildTypes.GetValueOrDefault(type)
            : null;
        sites.Insert(0, CreateSite(sites, name, -1, buildType));
        return true;
    }

    private bool ApplyApproachSettlement(
        IList<ColonizationSystemSite> sites,
        JsonElement root)
    {
        var name = GetString(root, "Name")
            ?? GetString(root, "SettlementName");
        var index = FindSiteIndex(sites, name);
        if (index < 0)
        {
            return false;
        }

        var current = sites[index];
        var bodyNumber = GetInt32(root, "BodyID");
        var marketId = GetInt64(root, "MarketID");
        var updated = current with
        {
            BodyNumber = bodyNumber is { } body && IsKnownBody(body)
                ? body
                : current.BodyNumber,
            MarketId = marketId is > 0 ? marketId : current.MarketId,
            Status = ColonizationSystemSiteStatus.Complete,
        };
        if (KnownFieldsEqual(current, updated))
        {
            return false;
        }

        sites[index] = updated;
        return true;
    }

    private static bool ApplyDocked(
        IList<ColonizationSystemSite> sites,
        JsonElement root)
    {
        var index = FindSiteIndex(sites, GetString(root, "StationName"));
        if (index < 0)
        {
            return false;
        }

        var current = sites[index];
        var marketId = GetInt64(root, "MarketID");
        var buildType = InferDockedBuildType(root, current.BuildType);
        var updated = current with
        {
            MarketId = marketId is > 0 ? marketId : current.MarketId,
            BuildType = buildType,
            Status = ColonizationSystemSiteStatus.Complete,
        };
        if (KnownFieldsEqual(current, updated))
        {
            return false;
        }

        sites[index] = updated;
        return true;
    }

    private static string? InferDockedBuildType(
        JsonElement root,
        string? currentBuildType)
    {
        var stationType = GetString(root, "StationType");
        if (string.Equals(
                stationType,
                "CraterPort",
                StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(currentBuildType)
                ? "aphrodite?"
                : currentBuildType;
        }

        if (!string.Equals(
                stationType,
                "Outpost",
                StringComparison.OrdinalIgnoreCase))
        {
            return currentBuildType;
        }

        if (root.TryGetProperty("LandingPads", out var landingPads)
            && landingPads.ValueKind == JsonValueKind.Object)
        {
            var small = GetInt32(landingPads, "Small");
            var medium = GetInt32(landingPads, "Medium");
            if (small == 3 && medium == 1)
            {
                return "plutus";
            }

            if (small == 4 && medium == 1)
            {
                return "vesta";
            }
        }

        if (CountSignificantEconomies(root) != 1)
        {
            return currentBuildType;
        }

        return GetString(root, "StationEconomy") switch
        {
            "$economy_HighTech;" => "prometheus",
            "$economy_Industrial;" => "vulcan",
            "$economy_Military;" => "nemesis",
            "$economy_Service;" => "dysnomia",
            _ => currentBuildType,
        };
    }

    private static int CountSignificantEconomies(JsonElement root)
    {
        if (!root.TryGetProperty("StationEconomies", out var economies)
            || economies.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return economies.EnumerateArray().Count(economy =>
            GetDouble(economy, "Proportion") is >= 1);
    }

    private ColonizationSystemSite CreateSite(
        IEnumerable<ColonizationSystemSite> sites,
        string name,
        int bodyNumber,
        string? buildType)
    {
        string id;
        do
        {
            id = $"y{nextIdentifierValue()}";
        }
        while (sites.Any(site => string.Equals(
            site.Id,
            id,
            StringComparison.Ordinal)));

        return new ColonizationSystemSite
        {
            Id = id,
            Name = name,
            BodyNumber = bodyNumber,
            BuildType = buildType,
            Status = ColonizationSystemSiteStatus.Complete,
        };
    }

    private bool MatchesSystem(JsonElement root)
    {
        return GetInt64(root, nameof(SystemAddress)) == SystemAddress;
    }

    private bool IsKnownBody(int bodyNumber)
    {
        return bodyNumber >= 0
            && (knownBodyNumbers.Count == 0
                || knownBodyNumbers.Contains(bodyNumber));
    }

    private static int FindSiteIndex(
        IList<ColonizationSystemSite> sites,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return -1;
        }

        for (var index = 0; index < sites.Count; index++)
        {
            if (string.Equals(
                    sites[index].Name,
                    name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsUsableName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && !name.TrimStart().StartsWith('$');
    }

    private static bool HasNonEmptyString(JsonElement root, string propertyName)
    {
        return !string.IsNullOrWhiteSpace(GetString(root, propertyName));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out var value)
                ? value
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetInt64(out var value)
                ? value
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetDouble(out var value)
                ? value
                : null;
    }

    private static bool KnownFieldsEqual(
        ColonizationSystemSite left,
        ColonizationSystemSite right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && left.BodyNumber == right.BodyNumber
            && string.Equals(
                left.BuildType,
                right.BuildType,
                StringComparison.Ordinal)
            && string.Equals(
                left.BuildId,
                right.BuildId,
                StringComparison.Ordinal)
            && left.MarketId == right.MarketId
            && left.Status == right.Status;
    }
}
