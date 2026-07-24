using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianLiveSiteState(
    GuardianSiteCatalog catalog,
    TimeProvider? timeProvider = null)
{
    private const string RuinsPrefix = "$Ancient:#index=";
    private const string StructurePrefix = "$Ancient_";
    private const string IndexMarker = ":#index=";
    private readonly GuardianSiteCatalog catalog = catalog
        ?? throw new ArgumentNullException(nameof(catalog));
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private string? systemName;
    private long? systemAddress;

    public GuardianLiveSiteSnapshot? CurrentSite { get; private set; }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var root = journalEvent.Payload;

        switch (journalEvent.EventName)
        {
            case "Location":
                ApplyLocation(root, clearCurrentSite: false);
                return true;

            case "FSDJump":
            case "CarrierJump":
            case "SupercruiseExit":
                ApplyLocation(root, clearCurrentSite: true);
                return true;

            case "ApproachSettlement":
                return ApplyApproachSettlement(journalEvent);

            case "StartJump":
            case "SupercruiseEntry":
            case "Shutdown":
                CurrentSite = null;
                return true;

            default:
                return false;
        }
    }

    public GuardianCommanderSiteSurvey CreateOrUpdateSurvey(
        string commanderName,
        bool legacy,
        GuardianCommanderSiteSurvey? existing = null)
    {
        if (CurrentSite is not { } site)
        {
            throw new InvalidOperationException(
                "There is no active Guardian site to save.");
        }

        if (existing is not null && !IsSameSite(site, existing))
        {
            throw new ArgumentException(
                "The existing survey belongs to a different Guardian site.",
                nameof(existing));
        }

        var siteType = existing is not null
            && !IsUnknown(existing.SiteType)
                ? existing.SiteType
                : site.SiteType;
        var survey = existing?.Survey;
        return new GuardianCommanderSiteSurvey(
            existing?.Path ?? string.Empty,
            site.Name,
            string.IsNullOrWhiteSpace(site.LocalizedName)
                ? existing?.LocalizedName ?? string.Empty
                : site.LocalizedName,
            commanderName,
            existing is not null
                && existing.FirstVisited != DateTimeOffset.MinValue
                ? existing.FirstVisited
                : site.FirstVisited,
            existing is not null && existing.LastVisited > site.LastVisited
                ? existing.LastVisited
                : site.LastVisited,
            siteType,
            site.Index,
            site.SystemAddress,
            string.IsNullOrWhiteSpace(site.SystemName)
                ? existing?.SystemName ?? string.Empty
                : site.SystemName,
            site.BodyId,
            site.BodyName,
            existing?.Notes ?? string.Empty,
            legacy,
            new GuardianSurveyData
            {
                SiteType = siteType,
                SiteHeading = survey?.SiteHeading ?? -1,
                RelicTowerHeading = survey?.RelicTowerHeading ?? -1,
                Location = site.Location ?? survey?.Location,
                PoiStatuses = survey?.PoiStatuses
                    ?? new Dictionary<string, GuardianPoiStatus>(
                        StringComparer.Ordinal),
                RelicHeadings = survey?.RelicHeadings
                    ?? new Dictionary<string, int>(StringComparer.Ordinal),
                RawPointsOfInterest = survey?.RawPointsOfInterest,
            },
            existing?.ActiveObelisks ?? [],
            existing?.ObeliskGroups
                ?? new HashSet<char>());
    }

    private void ApplyLocation(JsonElement root, bool clearCurrentSite)
    {
        var nextAddress = GetInt64(root, "SystemAddress");
        if (clearCurrentSite
            || (CurrentSite is not null
                && nextAddress is not null
                && nextAddress != CurrentSite.SystemAddress))
        {
            CurrentSite = null;
        }

        systemName = GetString(root, "StarSystem") ?? systemName;
        systemAddress = nextAddress ?? systemAddress;
    }

    private bool ApplyApproachSettlement(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        var name = GetString(root, "Name");
        if (!TryParseSiteIdentity(name, out var kind, out var index))
        {
            return false;
        }

        var address = GetInt64(root, "SystemAddress");
        var bodyId = GetInt32(root, "BodyID");
        var bodyName = GetString(root, "BodyName");
        if (address is null || bodyId is null || string.IsNullOrWhiteSpace(bodyName))
        {
            return false;
        }

        var reference = FindReference(
            address.Value,
            bodyId.Value,
            bodyName,
            kind,
            index);
        var timestamp = journalEvent.Timestamp ?? timeProvider.GetUtcNow();
        var location = GetLocation(root);
        var next = new GuardianLiveSiteSnapshot(
            name!,
            GetString(root, "Name_Localised") ?? string.Empty,
            kind,
            index,
            reference?.SiteType ?? GetSiteType(name!, kind),
            address.Value,
            reference?.SystemName
                ?? (systemAddress == address ? systemName ?? string.Empty : string.Empty),
            bodyId.Value,
            bodyName,
            location,
            timestamp,
            timestamp,
            reference);

        if (CurrentSite is { } current && IsSameSite(current, next))
        {
            next = next with
            {
                FirstVisited = current.FirstVisited,
                LastVisited = timestamp > current.LastVisited
                    ? timestamp
                    : current.LastVisited,
                LocalizedName = string.IsNullOrWhiteSpace(next.LocalizedName)
                    ? current.LocalizedName
                    : next.LocalizedName,
                Location = next.Location ?? current.Location,
            };
        }

        CurrentSite = next;
        systemAddress = address;
        systemName = string.IsNullOrWhiteSpace(next.SystemName)
            ? systemName
            : next.SystemName;
        return true;
    }

    private GuardianSiteReference? FindReference(
        long address,
        int bodyId,
        string bodyName,
        GuardianSiteKind kind,
        int index)
    {
        var candidates = catalog.FindBySystemAddress(address)
            .Where(reference => reference.Kind == kind && reference.Index == index)
            .ToArray();
        return candidates.FirstOrDefault(reference => reference.BodyId == bodyId)
            ?? candidates.FirstOrDefault(reference => string.Equals(
                RemoveSystemPrefix(bodyName, reference.SystemName),
                reference.BodyName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseSiteIdentity(
        string? name,
        out GuardianSiteKind kind,
        out int index)
    {
        kind = default;
        index = 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        int start;
        if (name.StartsWith(RuinsPrefix, StringComparison.Ordinal))
        {
            kind = GuardianSiteKind.Ruins;
            start = RuinsPrefix.Length;
        }
        else if (name.StartsWith(StructurePrefix, StringComparison.Ordinal))
        {
            kind = GuardianSiteKind.Structure;
            var marker = name.IndexOf(IndexMarker, StringComparison.Ordinal);
            if (marker < StructurePrefix.Length)
            {
                return false;
            }

            start = marker + IndexMarker.Length;
        }
        else
        {
            return false;
        }

        var end = name.IndexOf(';', start);
        if (end <= start)
        {
            return false;
        }

        return int.TryParse(
                name.AsSpan(start, end - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out index)
            && index > 0;
    }

    private static string GetSiteType(string name, GuardianSiteKind kind)
    {
        if (kind == GuardianSiteKind.Ruins)
        {
            return "Unknown";
        }

        var marker = name.IndexOf(IndexMarker, StringComparison.Ordinal);
        var settlement = marker > 0 ? name[..marker] : name;
        return settlement switch
        {
            "$Ancient_Tiny_001" => "Lacrosse",
            "$Ancient_Tiny_002" => "Crossroads",
            "$Ancient_Tiny_003" => "Fistbump",
            "$Ancient_Small_001" => "Hammerbot",
            "$Ancient_Small_002" => "Bear",
            "$Ancient_Small_003" => "Bowl",
            "$Ancient_Small_005" => "Turtle",
            "$Ancient_Medium_001" => "Robolobster",
            "$Ancient_Medium_002" => "Squid",
            "$Ancient_Medium_003" => "Stickyhand",
            _ => "Unknown",
        };
    }

    private static bool IsSameSite(
        GuardianLiveSiteSnapshot left,
        GuardianLiveSiteSnapshot right)
    {
        return left.SystemAddress == right.SystemAddress
            && left.BodyId == right.BodyId
            && left.Kind == right.Kind
            && left.Index == right.Index;
    }

    private static bool IsSameSite(
        GuardianLiveSiteSnapshot site,
        GuardianCommanderSiteSurvey survey)
    {
        var kind = survey.Name.StartsWith(
            RuinsPrefix,
            StringComparison.Ordinal)
                ? GuardianSiteKind.Ruins
                : GuardianSiteKind.Structure;
        return site.SystemAddress == survey.SystemAddress
            && site.BodyId == survey.BodyId
            && site.Kind == kind
            && site.Index == survey.Index;
    }

    private static bool IsUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveSystemPrefix(string bodyName, string referenceSystem)
    {
        return bodyName.StartsWith(referenceSystem, StringComparison.OrdinalIgnoreCase)
            ? bodyName[referenceSystem.Length..].Trim()
            : bodyName;
    }

    private static GuardianSurfaceLocation? GetLocation(JsonElement root)
    {
        var latitude = GetDouble(root, "Latitude");
        var longitude = GetDouble(root, "Longitude");
        return latitude is >= -90 and <= 90
            && longitude is >= -180 and <= 180
                ? new GuardianSurfaceLocation(latitude.Value, longitude.Value)
                : null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetDouble(out var result)
            && double.IsFinite(result)
                ? result
                : null;
    }
}

public sealed record GuardianLiveSiteSnapshot(
    string Name,
    string LocalizedName,
    GuardianSiteKind Kind,
    int Index,
    string SiteType,
    long SystemAddress,
    string SystemName,
    int BodyId,
    string BodyName,
    GuardianSurfaceLocation? Location,
    DateTimeOffset FirstVisited,
    DateTimeOffset LastVisited,
    GuardianSiteReference? Reference)
{
    public bool IsKnownReference => Reference is not null;
}
