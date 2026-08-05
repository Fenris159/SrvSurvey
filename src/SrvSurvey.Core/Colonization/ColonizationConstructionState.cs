using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed class ColonizationConstructionState
{
    private ColonizationDockingSnapshot? currentDock;
    private ColonizationConstructionDepotSnapshot? currentDepot;
    private ColonizationContributionSnapshot? lastContribution;
    private ColonizationSystemClaimSnapshot? lastClaim;
    private DateTimeOffset? lastBeaconDeployment;
    private int shipCargoCapacity;
    private string? musicTrack;

    public long Version { get; private set; }

    public ColonizationDockingSnapshot? CurrentDock => currentDock;

    public ColonizationConstructionDepotSnapshot? CurrentDepot => currentDepot;

    public ColonizationContributionSnapshot? LastContribution =>
        lastContribution;

    public ColonizationSystemClaimSnapshot? LastClaim => lastClaim;

    public DateTimeOffset? LastBeaconDeployment => lastBeaconDeployment;

    public int ShipCargoCapacity => shipCargoCapacity;

    public string? MusicTrack => musicTrack;

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var changed = journalEvent.EventName switch
        {
            "Docked" => ApplyDocked(
                journalEvent.Payload,
                journalEvent.Timestamp),
            "Undocked" => ClearDocking(),
            "StartJump" => ClearDocking(),
            "ColonisationConstructionDepot" => ApplyDepot(
                journalEvent.Payload,
                journalEvent.Timestamp),
            "ColonisationContribution" => ApplyContribution(
                journalEvent.Payload,
                journalEvent.Timestamp),
            "ColonisationSystemClaim" => ApplyClaim(
                journalEvent.Payload,
                journalEvent.Timestamp),
            "ColonisationBeaconDeployed" => ApplyBeacon(
                journalEvent.Timestamp),
            "Loadout" => ApplyShipLoadout(journalEvent.Payload),
            "Music" => ApplyMusic(journalEvent.Payload),
            "Died" or "Resurrect" or "Shutdown" => ClearDocking(),
            _ => false,
        };
        if (changed)
        {
            Version++;
        }

        return changed;
    }

    public ColonizationConstructionSnapshot CreateSnapshot()
    {
        return new ColonizationConstructionSnapshot(
            currentDock,
            currentDepot,
            lastContribution,
            lastClaim,
            lastBeaconDeployment,
            shipCargoCapacity,
            musicTrack);
    }

    public static string NormalizeCommodityName(string? journalName)
    {
        if (string.IsNullOrWhiteSpace(journalName))
        {
            return string.Empty;
        }

        var normalized = journalName.Trim();
        if (normalized.StartsWith('$'))
        {
            normalized = normalized[1..];
        }

        if (normalized.EndsWith(
                "_name;",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^6];
        }

        return normalized.ToLowerInvariant();
    }

    private bool ApplyDocked(
        JsonElement root,
        DateTimeOffset? timestamp)
    {
        var marketId = GetInt64(root, "MarketID");
        var systemAddress = GetInt64(root, "SystemAddress");
        var stationName = GetString(root, "StationName_Localised")
            ?? GetString(root, "StationName");
        if (marketId is null
            || systemAddress is null
            || string.IsNullOrWhiteSpace(stationName))
        {
            return false;
        }

        var factionName = root.TryGetProperty("StationFaction", out var faction)
            && faction.ValueKind == JsonValueKind.Object
                ? GetString(faction, "Name")
                : null;
        var services = GetStrings(root, "StationServices");
        var updated = new ColonizationDockingSnapshot(
            marketId.Value,
            systemAddress.Value,
            GetString(root, "StarSystem") ?? string.Empty,
            stationName,
            factionName,
            services,
            timestamp,
            GetString(root, "StationType"));
        var changed = !DockEquals(updated, currentDock)
            || currentDepot is not null;
        currentDock = updated;
        currentDepot = null;
        return changed;
    }

    private bool ClearDocking()
    {
        if (currentDock is null && currentDepot is null)
        {
            return false;
        }

        currentDock = null;
        currentDepot = null;
        return true;
    }

    private bool ApplyDepot(JsonElement root, DateTimeOffset? timestamp)
    {
        var marketId = GetInt64(root, "MarketID");
        if (marketId is null)
        {
            return false;
        }

        var resources = ReadResources(root);
        var updated = new ColonizationConstructionDepotSnapshot(
            timestamp,
            marketId.Value,
            GetDouble(root, "ConstructionProgress") ?? 0,
            GetBoolean(root, "ConstructionComplete") ?? false,
            GetBoolean(root, "ConstructionFailed") ?? false,
            resources);
        if (DepotEquals(updated, currentDepot))
        {
            return false;
        }

        currentDepot = updated;
        return true;
    }

    private bool ApplyContribution(
        JsonElement root,
        DateTimeOffset? timestamp)
    {
        var marketId = GetInt64(root, "MarketID");
        if (marketId is null
            || currentDock is not { IsConstructionSite: true } dock
            || dock.MarketId != marketId)
        {
            return false;
        }

        var contributions = ReadContributions(root);
        if (contributions.Count == 0)
        {
            return false;
        }

        var updated = new ColonizationContributionSnapshot(
            timestamp,
            marketId.Value,
            contributions);
        if (ContributionEquals(updated, lastContribution))
        {
            return false;
        }

        lastContribution = updated;
        return true;
    }

    private bool ApplyClaim(JsonElement root, DateTimeOffset? timestamp)
    {
        var systemAddress = GetInt64(root, "SystemAddress");
        var systemName = GetString(root, "StarSystem");
        if (systemAddress is null || string.IsNullOrWhiteSpace(systemName))
        {
            return false;
        }

        var updated = new ColonizationSystemClaimSnapshot(
            timestamp,
            systemAddress.Value,
            systemName);
        if (updated == lastClaim)
        {
            return false;
        }

        lastClaim = updated;
        return true;
    }

    private bool ApplyBeacon(DateTimeOffset? timestamp)
    {
        if (timestamp == lastBeaconDeployment)
        {
            return false;
        }

        lastBeaconDeployment = timestamp;
        return true;
    }

    private bool ApplyShipLoadout(JsonElement root)
    {
        var capacity = GetInt32(root, "CargoCapacity");
        if (capacity is null || capacity < 0 || capacity == shipCargoCapacity)
        {
            return false;
        }

        shipCargoCapacity = capacity.Value;
        return true;
    }

    private bool ApplyMusic(JsonElement root)
    {
        var updated = GetString(root, nameof(MusicTrack));
        var changed = !string.Equals(
            updated,
            musicTrack,
            StringComparison.Ordinal);
        musicTrack = updated;
        return string.Equals(updated, "MainMenu", StringComparison.Ordinal)
            ? ClearDocking() || changed
            : changed;
    }

    private static IReadOnlyList<ColonizationResourceRequirement>
        ReadResources(JsonElement root)
    {
        if (!root.TryGetProperty("ResourcesRequired", out var resources)
            || resources.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ColonizationResourceRequirement>();
        foreach (var resource in resources.EnumerateArray())
        {
            var name = NormalizeCommodityName(GetString(resource, "Name"));
            var required = GetInt32(resource, "RequiredAmount");
            var provided = GetInt32(resource, "ProvidedAmount");
            if (name.Length == 0 || required is null || provided is null)
            {
                continue;
            }

            result.Add(new ColonizationResourceRequirement(
                name,
                GetString(resource, "Name_Localised") ?? name,
                Math.Max(0, required.Value),
                Math.Max(0, provided.Value),
                Math.Max(0, GetInt32(resource, "Payment") ?? 0)));
        }

        return result;
    }

    private static bool DockEquals(
        ColonizationDockingSnapshot left,
        ColonizationDockingSnapshot? right)
    {
        return right is not null
            && left.MarketId == right.MarketId
            && left.SystemAddress == right.SystemAddress
            && string.Equals(
                left.SystemName,
                right.SystemName,
                StringComparison.Ordinal)
            && string.Equals(
                left.StationName,
                right.StationName,
                StringComparison.Ordinal)
            && string.Equals(
                left.FactionName,
                right.FactionName,
                StringComparison.Ordinal)
            && string.Equals(
                left.StationType,
                right.StationType,
                StringComparison.Ordinal)
            && left.Timestamp == right.Timestamp
            && left.StationServices.SequenceEqual(
                right.StationServices,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool DepotEquals(
        ColonizationConstructionDepotSnapshot left,
        ColonizationConstructionDepotSnapshot? right)
    {
        return right is not null
            && left.Timestamp == right.Timestamp
            && left.MarketId == right.MarketId
            && Math.Abs(left.ReportedProgress - right.ReportedProgress)
                <= 0.0000001d
            && left.IsComplete == right.IsComplete
            && left.IsFailed == right.IsFailed
            && left.Resources.SequenceEqual(right.Resources);
    }

    private static bool ContributionEquals(
        ColonizationContributionSnapshot left,
        ColonizationContributionSnapshot? right)
    {
        return right is not null
            && left.Timestamp == right.Timestamp
            && left.MarketId == right.MarketId
            && left.Commodities.Count == right.Commodities.Count
            && left.Commodities.All(pair =>
                right.Commodities.TryGetValue(pair.Key, out var value)
                && value == pair.Value);
    }

    private static IReadOnlyDictionary<string, int> ReadContributions(
        JsonElement root)
    {
        if (!root.TryGetProperty("Contributions", out var contributions)
            || contributions.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, int>();
        }

        var result = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var contribution in contributions.EnumerateArray())
        {
            var name = NormalizeCommodityName(GetString(contribution, "Name"));
            var amount = GetInt32(contribution, "Amount");
            if (name.Length == 0 || amount is null || amount <= 0)
            {
                continue;
            }

            result[name] = result.GetValueOrDefault(name) + amount.Value;
        }

        return result;
    }

    private static IReadOnlyList<string> GetStrings(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
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
            && double.IsFinite(number)
                ? number
                : null;
    }
}

public sealed record ColonizationConstructionSnapshot(
    ColonizationDockingSnapshot? CurrentDock,
    ColonizationConstructionDepotSnapshot? CurrentDepot,
    ColonizationContributionSnapshot? LastContribution,
    ColonizationSystemClaimSnapshot? LastClaim,
    DateTimeOffset? LastBeaconDeployment,
    int ShipCargoCapacity,
    string? MusicTrack = null)
{
    public bool IsSquadronBankOpen => string.Equals(
            MusicTrack,
            "Squadrons",
            StringComparison.Ordinal)
        && CurrentDock?.StationServices.Contains(
            "squadronBank",
            StringComparer.OrdinalIgnoreCase) == true;
}

public sealed record ColonizationDockingSnapshot(
    long MarketId,
    long SystemAddress,
    string SystemName,
    string StationName,
    string? FactionName,
    IReadOnlyList<string> StationServices,
    DateTimeOffset? Timestamp = null,
    string? StationType = null)
{
    public const string SystemColonisationShip = "System Colonisation Ship";
    public const string ExternalPanelColonisationShip =
        "$EXT_PANEL_ColonisationShip";
    public const string PlanetaryConstructionSite =
        "Planetary Construction Site:";
    public const string OrbitalConstructionSite =
        "Orbital Construction Site:";

    public bool IsPrimaryPortShip => StationName.StartsWith(
            ExternalPanelColonisationShip,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            StationName,
            SystemColonisationShip,
            StringComparison.OrdinalIgnoreCase);

    public bool IsConstructionSite => IsConstructionSiteName(StationName)
        && StationServices.Contains(
            "colonisationcontribution",
            StringComparer.OrdinalIgnoreCase);

    public string DefaultProjectName => IsPrimaryPortShip
        ? "Primary port"
        : StationName
            .Replace(
                ExternalPanelColonisationShip + "; ",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                PlanetaryConstructionSite,
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                OrbitalConstructionSite,
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Trim();

    public static bool IsConstructionSiteName(string? stationName)
    {
        return !string.IsNullOrWhiteSpace(stationName)
            && (stationName.StartsWith(
                    PlanetaryConstructionSite,
                    StringComparison.OrdinalIgnoreCase)
                || stationName.StartsWith(
                    OrbitalConstructionSite,
                    StringComparison.OrdinalIgnoreCase)
                || stationName.StartsWith(
                    ExternalPanelColonisationShip,
                    StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ColonizationConstructionDepotSnapshot(
    DateTimeOffset? Timestamp,
    long MarketId,
    double ReportedProgress,
    bool IsComplete,
    bool IsFailed,
    IReadOnlyList<ColonizationResourceRequirement> Resources)
{
    public long TotalRequired => Resources.Sum(
        resource => (long)resource.RequiredAmount);

    public long TotalProvided => Resources.Sum(
        resource => (long)resource.ProvidedAmount);

    public long TotalRemaining => Resources.Sum(
        resource => (long)resource.RemainingAmount);

    public double? CalculatedProgress => TotalRequired > 0
        ? Math.Clamp(TotalProvided / (double)TotalRequired, 0, 1)
        : null;
}

public sealed record ColonizationResourceRequirement(
    string Name,
    string LocalizedName,
    int RequiredAmount,
    int ProvidedAmount,
    int Payment)
{
    public int RemainingAmount => Math.Max(0, RequiredAmount - ProvidedAmount);
}

public sealed record ColonizationContributionSnapshot(
    DateTimeOffset? Timestamp,
    long MarketId,
    IReadOnlyDictionary<string, int> Commodities)
{
    public long TotalAmount => Commodities.Values.Sum(value => (long)value);
}

public sealed record ColonizationSystemClaimSnapshot(
    DateTimeOffset? Timestamp,
    long SystemAddress,
    string SystemName);
