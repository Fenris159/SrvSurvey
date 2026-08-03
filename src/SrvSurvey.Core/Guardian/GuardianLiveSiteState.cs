using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianLiveSiteState
{
    private const string RuinsPrefix = "$Ancient:#index=";
    private const string StructurePrefix = "$Ancient_";
    private const string IndexMarker = ":#index=";
    private readonly TimeProvider timeProvider;
    private GuardianSiteReference[] recoveryReferences;
    private string? systemName;
    private long? systemAddress;
    private GuardianLiveSiteSnapshot? approachedSite;

    public GuardianLiveSiteState(
        GuardianSiteCatalog catalog,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        recoveryReferences = GetSurfaceSites(catalog.Sites);
    }

    public GuardianLiveSiteSnapshot? CurrentSite { get; private set; }

    public void SetRecoveryReferences(
        IEnumerable<GuardianSiteReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        recoveryReferences = GetSurfaceSites(references);
    }

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
                ApplyLocation(root, clearCurrentSite: true);
                return true;

            case "SupercruiseExit":
                ApplyLocation(root, clearCurrentSite: false);
                return true;

            case "ApproachSettlement":
                return ApplyApproachSettlement(journalEvent);

            case "StartJump":
            case "SupercruiseEntry":
            case "Died":
            case "Resurrect":
            case "Shutdown":
                ClearSite();
                return true;

            case "Music" when string.Equals(
                GetString(root, "MusicTrack"),
                "MainMenu",
                StringComparison.Ordinal):
                ClearSite();
                return true;

            default:
                return false;
        }
    }

    public bool SynchronizeProximity(EliteStatus status, bool retainDuringGlide)
    {
        ArgumentNullException.ThrowIfNull(status);
        var previous = CurrentSite;
        if (retainDuringGlide && approachedSite is not null)
        {
            CurrentSite = approachedSite;
            return !Equals(previous, CurrentSite);
        }

        var radius = (double)status.PlanetRadius;
        if (!status.HasLatitudeLongitude
            || !double.IsFinite(radius)
            || radius <= 0
            || status.Altitude > 4_000)
        {
            CurrentSite = status.Altitude > 4_000 ? null : CurrentSite;
            return !Equals(previous, CurrentSite);
        }

        var system = approachedSite?.SystemAddress ?? systemAddress;
        var bodyName = status.BodyName ?? approachedSite?.BodyName;
        if (system is null || string.IsNullOrWhiteSpace(bodyName))
        {
            return false;
        }

        var here = new SurfaceCoordinate(status.Latitude, status.Longitude);
        var candidates = recoveryReferences
            .Where(reference => reference.SystemAddress == system
                && MatchesBody(reference, bodyName)
                && reference.Latitude is not null
                && reference.Longitude is not null)
            .Select(CreateRecoveredSnapshot)
            .ToList();
        if (approachedSite is { Location: not null } approached
            && approached.SystemAddress == system
            && MatchesBody(approached, bodyName))
        {
            var recoveredIndex = candidates.FindIndex(candidate =>
                IsSameSite(candidate, approached));
            if (recoveredIndex >= 0)
            {
                candidates[recoveredIndex] = approached;
            }
            else
            {
                candidates.Add(approached);
            }
        }

        var nearest = candidates
            .Select(candidate => new
            {
                Site = candidate,
                Distance = GetDistance(candidate, here, radius),
            })
            .Where(candidate => candidate.Distance is not null)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Site)
            .FirstOrDefault();
        if (nearest is not null)
        {
            approachedSite = nearest;
        }

        CurrentSite = nearest;
        return !Equals(previous, CurrentSite);
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
                ComponentMaterials = survey?.ComponentMaterials
                    ?? new Dictionary<string, GuardianComponentLoadout>(
                        StringComparer.Ordinal),
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
            || (approachedSite is not null
                && nextAddress is not null
                && nextAddress != approachedSite.SystemAddress))
        {
            ClearSite();
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
            var hadSite = approachedSite is not null;
            ClearSite();
            return hadSite;
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

        if (approachedSite is { } current && IsSameSite(current, next))
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

        approachedSite = next;
        CurrentSite = next;
        systemAddress = address;
        systemName = string.IsNullOrWhiteSpace(next.SystemName)
            ? systemName
            : next.SystemName;
        return true;
    }

    private void ClearSite()
    {
        approachedSite = null;
        CurrentSite = null;
    }

    private GuardianSiteReference? FindReference(
        long address,
        int bodyId,
        string bodyName,
        GuardianSiteKind kind,
        int index)
    {
        var candidates = recoveryReferences
            .Where(reference => reference.SystemAddress == address)
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

    private GuardianLiveSiteSnapshot CreateRecoveredSnapshot(
        GuardianSiteReference reference)
    {
        var observedAt = timeProvider.GetUtcNow();
        return new GuardianLiveSiteSnapshot(
            GetSettlementName(reference),
            reference.Kind == GuardianSiteKind.Ruins
                ? $"Ancient Ruins ({reference.Index})"
                : $"Guardian Structure ({reference.Index})",
            reference.Kind,
            reference.Index,
            reference.SiteType,
            reference.SystemAddress,
            reference.SystemName,
            reference.BodyId,
            reference.FullBodyName,
            reference.Latitude is double latitude
                && reference.Longitude is double longitude
                    ? new GuardianSurfaceLocation(latitude, longitude)
                    : null,
            observedAt,
            observedAt,
            reference);
    }

    private static string GetSettlementName(GuardianSiteReference reference)
    {
        if (reference.Kind == GuardianSiteKind.Ruins)
        {
            return $"$Ancient:#index={reference.Index};";
        }

        var settlement = reference.SiteType.ToLowerInvariant() switch
        {
            "lacrosse" => "$Ancient_Tiny_001",
            "crossroads" => "$Ancient_Tiny_002",
            "fistbump" => "$Ancient_Tiny_003",
            "hammerbot" => "$Ancient_Small_001",
            "bear" => "$Ancient_Small_002",
            "bowl" => "$Ancient_Small_003",
            "turtle" => "$Ancient_Small_005",
            "robolobster" => "$Ancient_Medium_001",
            "squid" => "$Ancient_Medium_002",
            "stickyhand" => "$Ancient_Medium_003",
            _ => "$Ancient_Unknown",
        };
        return $"{settlement}:#index={reference.Index};";
    }

    private static double? GetDistance(
        GuardianLiveSiteSnapshot site,
        SurfaceCoordinate here,
        double radius)
    {
        return site.Location is { } location
            ? SurfaceNavigation.GetDistance(
                here,
                new SurfaceCoordinate(location.Latitude, location.Longitude),
                radius)
            : null;
    }

    private static bool MatchesBody(
        GuardianSiteReference reference,
        string bodyName)
    {
        return string.Equals(
                reference.FullBodyName,
                bodyName,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                reference.BodyName,
                RemoveSystemPrefix(bodyName, reference.SystemName),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesBody(
        GuardianLiveSiteSnapshot site,
        string bodyName)
    {
        return string.Equals(
                site.BodyName,
                bodyName,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                RemoveSystemPrefix(site.BodyName, site.SystemName),
                RemoveSystemPrefix(bodyName, site.SystemName),
                StringComparison.OrdinalIgnoreCase);
    }

    private static GuardianSiteReference[] GetSurfaceSites(
        IEnumerable<GuardianSiteReference> references)
    {
        return references
            .Where(reference => reference.Kind is GuardianSiteKind.Ruins
                or GuardianSiteKind.Structure)
            .ToArray();
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
