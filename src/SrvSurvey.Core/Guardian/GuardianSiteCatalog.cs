using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteCatalog
{
    private const string RuinsResourceName =
        "SrvSurvey.Core.Resources.allRuins.json";
    private const string StructuresResourceName =
        "SrvSurvey.Core.Resources.allStructures.json";
    private const string BeaconsResourceName =
        "SrvSurvey.Core.Resources.allBeacons.json";

    private readonly GuardianSiteReference[] sites;
    private readonly Dictionary<long, GuardianSiteReference[]> bySystemAddress;

    public GuardianSiteCatalog(IEnumerable<GuardianSiteReference> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        this.sites = sites.ToArray();
        bySystemAddress = this.sites
            .GroupBy(site => site.SystemAddress)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    public IReadOnlyList<GuardianSiteReference> Sites => sites;

    public int Count => sites.Length;

    public IReadOnlyList<GuardianSiteReference> FindBySystemAddress(
        long systemAddress)
    {
        return bySystemAddress.GetValueOrDefault(systemAddress) ?? [];
    }

    public IReadOnlyList<GuardianSiteMatch> Search(
        GuardianSiteQuery? query = null)
    {
        query ??= new GuardianSiteQuery();
        IEnumerable<GuardianSiteReference> filtered = sites;

        if (query.Kinds is { Count: > 0 })
        {
            filtered = filtered.Where(site => query.Kinds.Contains(site.Kind));
        }

        if (query.SiteTypes is { Count: > 0 })
        {
            filtered = filtered.Where(
                site => query.SiteTypes.Any(
                    siteType => string.Equals(
                        siteType,
                        site.SiteType,
                        StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            filtered = filtered.Where(site => MatchesText(site, text));
        }

        var matches = filtered
            .Select(site => new GuardianSiteMatch(
                site,
                query.Origin is GalacticCoordinate origin
                    ? origin.DistanceTo(site.Position)
                    : null));
        matches = Sort(matches, query.SortBy);
        if (query.Descending)
        {
            matches = matches.Reverse();
        }

        return matches.ToArray();
    }

    public static GuardianSiteCatalog LoadEmbedded()
    {
        var assembly = typeof(GuardianSiteCatalog).Assembly;
        using var ruins = assembly.GetManifestResourceStream(RuinsResourceName)
            ?? throw MissingResource(RuinsResourceName);
        using var structures = assembly.GetManifestResourceStream(
                StructuresResourceName)
            ?? throw MissingResource(StructuresResourceName);
        using var beacons = assembly.GetManifestResourceStream(BeaconsResourceName)
            ?? throw MissingResource(BeaconsResourceName);
        return Load(ruins, structures, beacons);
    }

    public static GuardianSiteCatalog LoadPublishedDirectory(
        string publishedDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedDataDirectory);
        var directory = Path.GetFullPath(publishedDataDirectory);
        using var ruins = File.OpenRead(Path.Combine(directory, "allRuins.json"));
        using var structures = File.OpenRead(
            Path.Combine(directory, "allStructures.json"));
        var assembly = typeof(GuardianSiteCatalog).Assembly;
        using var beacons = assembly.GetManifestResourceStream(BeaconsResourceName)
            ?? throw MissingResource(BeaconsResourceName);
        return Load(ruins, structures, beacons);
    }

    public static GuardianSiteCatalog Load(
        Stream ruins,
        Stream structures,
        Stream beacons)
    {
        ArgumentNullException.ThrowIfNull(ruins);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(beacons);

        return new GuardianSiteCatalog(
        [
            .. ReadSites(ruins, GuardianSiteKind.Ruins),
            .. ReadSites(structures, GuardianSiteKind.Structure),
            .. ReadSites(beacons, GuardianSiteKind.Beacon),
        ]);
    }

    private static IEnumerable<GuardianSiteMatch> Sort(
        IEnumerable<GuardianSiteMatch> matches,
        GuardianSiteSort sortBy)
    {
        return sortBy switch
        {
            GuardianSiteSort.SiteId => matches
                .OrderBy(match => match.Site.SiteId)
                .ThenBy(match => match.Site.SystemName),
            GuardianSiteSort.System => matches
                .OrderBy(match => match.Site.SystemName)
                .ThenBy(match => match.Site.BodyName),
            GuardianSiteSort.Body => matches
                .OrderBy(match => match.Site.BodyName)
                .ThenBy(match => match.Site.SystemName),
            GuardianSiteSort.Arrival => matches
                .OrderBy(match => match.Site.DistanceToArrival)
                .ThenBy(match => match.Site.SystemName),
            GuardianSiteSort.Type => matches
                .OrderBy(match => match.Site.SiteType)
                .ThenBy(match => match.Site.SystemName),
            GuardianSiteSort.Survey => matches
                .OrderBy(match => match.Site.SurveyProgress)
                .ThenBy(match => match.Site.SystemName),
            _ => matches
                .OrderBy(match => match.Distance ?? double.MaxValue)
                .ThenBy(match => match.Site.SystemName),
        };
    }

    private static bool MatchesText(GuardianSiteReference site, string text)
    {
        return site.SystemName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || site.BodyName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || site.SiteType.Contains(text, StringComparison.OrdinalIgnoreCase)
            || site.DisplayId.Contains(text, StringComparison.OrdinalIgnoreCase)
            || site.SystemAddress.ToString(CultureInfo.InvariantCulture)
                .Contains(text, StringComparison.OrdinalIgnoreCase)
            || site.RelatedStructure?.Contains(
                text,
                StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IReadOnlyList<GuardianSiteReference> ReadSites(
        Stream stream,
        GuardianSiteKind kind)
    {
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"The Guardian {kind} reference is not a JSON array.");
        }

        var result = new List<GuardianSiteReference>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"The Guardian {kind} reference contains a non-object entry.");
            }

            var siteType = kind == GuardianSiteKind.Beacon
                ? "Beacon"
                : GetRequiredString(element, "siteType");
            var position = GetPosition(element);
            var surveyProgress = GetInt32(element, "surveyProgress") ?? 0;
            if (surveyProgress is < 0 or > 100)
            {
                throw new InvalidDataException(
                    $"Guardian survey progress {surveyProgress} is outside 0-100.");
            }

            result.Add(new GuardianSiteReference(
                GetInt32(element, "siteID") ?? 0,
                kind,
                GetRequiredString(element, "systemName"),
                GetRequiredInt64(element, "systemAddress"),
                GetRequiredString(element, "bodyName"),
                GetInt32(element, "bodyId") ?? -1,
                siteType,
                GetIndex(element, kind),
                GetRequiredDouble(element, "distanceToArrival"),
                position,
                GetFiniteDouble(element, "latitude"),
                GetFiniteDouble(element, "longitude"),
                GetInt32(element, "siteHeading") ?? -1,
                GetInt32(element, "relicTowerHeading") ?? -1,
                surveyProgress,
                GetDateTimeOffset(element, "lastUpdated"),
                GetString(element, "relatedStructure"),
                GetFiniteDouble(element, "relatedStructureDist")));
        }

        return result;
    }

    private static int GetIndex(JsonElement element, GuardianSiteKind kind)
    {
        var index = GetInt32(element, "idx") ?? 0;
        return kind switch
        {
            GuardianSiteKind.Ruins when index <= 0 => throw new InvalidDataException(
                "A Guardian ruin is missing its positive site index."),
            GuardianSiteKind.Structure when index <= 0 => 1,
            _ => index,
        };
    }

    private static GalacticCoordinate GetPosition(JsonElement element)
    {
        if (!element.TryGetProperty("starPos", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "A Guardian reference is missing its galactic position.");
        }

        var coordinates = value.EnumerateArray().ToArray();
        if (coordinates.Length != 3)
        {
            throw new InvalidDataException(
                "A Guardian galactic position must contain three coordinates.");
        }

        return new GalacticCoordinate(
            GetRequiredDouble(coordinates[0]),
            GetRequiredDouble(coordinates[1]),
            GetRequiredDouble(coordinates[2]));
    }

    private static string GetRequiredString(
        JsonElement element,
        string propertyName)
    {
        return GetString(element, propertyName) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException(
                $"A Guardian reference is missing {propertyName}.");
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long GetRequiredInt64(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var result)
                ? result
                : throw new InvalidDataException(
                    $"A Guardian reference is missing numeric {propertyName}.");
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static double GetRequiredDouble(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? GetRequiredDouble(value)
            : throw new InvalidDataException(
                $"A Guardian reference is missing numeric {propertyName}.");
    }

    private static double GetRequiredDouble(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var result)
            && double.IsFinite(result))
        {
            return result;
        }

        throw new InvalidDataException(
            "A Guardian reference contains an invalid numeric value.");
    }

    private static double? GetFiniteDouble(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && double.IsFinite(number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number)
            && double.IsFinite(number))
        {
            return number;
        }

        return null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonElement element,
        string propertyName)
    {
        return GetString(element, propertyName) is { } value
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result)
                    ? result
                    : null;
    }

    private static InvalidOperationException MissingResource(string name)
    {
        return new InvalidOperationException(
            $"The embedded Guardian reference {name} is missing.");
    }
}

public enum GuardianSiteKind
{
    Beacon,
    Ruins,
    Structure,
}

public enum GuardianSiteSort
{
    Distance,
    SiteId,
    System,
    Body,
    Arrival,
    Type,
    Survey,
}

public sealed record GuardianSiteReference(
    int SiteId,
    GuardianSiteKind Kind,
    string SystemName,
    long SystemAddress,
    string BodyName,
    int BodyId,
    string SiteType,
    int Index,
    double DistanceToArrival,
    GalacticCoordinate Position,
    double? Latitude,
    double? Longitude,
    int SiteHeading,
    int RelicTowerHeading,
    int SurveyProgress,
    DateTimeOffset? LastUpdated,
    string? RelatedStructure,
    double? RelatedStructureDistance,
    bool IsCommanderOnly = false)
{
    public bool IsSurveyComplete => SurveyProgress == 100;

    public string DisplayId => Kind switch
    {
        GuardianSiteKind.Ruins => IsCommanderOnly ? "GR LOCAL" : $"GR {SiteId}",
        GuardianSiteKind.Structure => IsCommanderOnly ? "GS LOCAL" : $"GS {SiteId}",
        _ => IsCommanderOnly ? "GB LOCAL" : "GB",
    };

    public string FullBodyName => $"{SystemName} {BodyName}";
}

public sealed record GuardianSiteMatch(
    GuardianSiteReference Site,
    double? Distance);

public sealed record GuardianSiteQuery(
    string? Text = null,
    IReadOnlySet<GuardianSiteKind>? Kinds = null,
    IReadOnlySet<string>? SiteTypes = null,
    GalacticCoordinate? Origin = null,
    GuardianSiteSort SortBy = GuardianSiteSort.Distance,
    bool Descending = false);
