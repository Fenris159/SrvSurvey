using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianPublishedSiteCatalog
{
    private const string EmbeddedResourceName =
        "SrvSurvey.Core.Resources.guardian.zip";

    private readonly GuardianPublishedSite[] allSites;
    private readonly Dictionary<string, GuardianPublishedSite> sites;
    private readonly Dictionary<string, string[]> itemCodesByLog;

    public GuardianPublishedSiteCatalog(
        IEnumerable<GuardianPublishedSite> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        allSites = sites.ToArray();
        this.sites = allSites.ToDictionary(
            site => GetIdentity(site.Kind, site.FullBodyName, site.Index),
            StringComparer.OrdinalIgnoreCase);
        itemCodesByLog = allSites
            .SelectMany(site => site.ActiveObelisks)
            .Where(obelisk => !string.IsNullOrWhiteSpace(obelisk.LogCode))
            .GroupBy(obelisk => obelisk.LogCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(obelisk => obelisk.ItemCodes)
                    .FirstOrDefault(itemCodes => itemCodes.Count > 0) is { } itemCodes
                        ? itemCodes.ToArray()
                        : [],
                StringComparer.OrdinalIgnoreCase);
    }

    public int Count => allSites.Length;

    public GuardianPublishedSite? Find(GuardianSiteReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return sites.GetValueOrDefault(GetIdentity(
            reference.Kind,
            reference.FullBodyName,
            reference.Index));
    }

    public GuardianPublishedSite? Find(
        GuardianSiteKind kind,
        int siteId)
    {
        var matches = allSites
            .Where(site => site.Kind == kind && site.SiteId == siteId)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public GuardianPublishedSite? Find(
        GuardianSiteKind kind,
        string fullBodyName,
        int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullBodyName);
        return sites.GetValueOrDefault(GetIdentity(kind, fullBodyName, index));
    }

    public IReadOnlyList<string> FindItemCodesByLog(string? logCode)
    {
        return !string.IsNullOrWhiteSpace(logCode)
            && itemCodesByLog.TryGetValue(logCode, out var itemCodes)
                ? itemCodes
                : [];
    }

    public static GuardianPublishedSiteCatalog LoadEmbedded()
    {
        var assembly = typeof(GuardianPublishedSiteCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded Guardian surveys {EmbeddedResourceName} are missing.");
        return LoadZip(stream);
    }

    public static GuardianPublishedSiteCatalog LoadZip(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sites = new List<GuardianPublishedSite>();
        foreach (var entry in archive.Entries.Where(
            entry => entry.FullName.EndsWith(
                ".json",
                StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            sites.Add(Read(entryStream, entry.FullName));
        }

        return new GuardianPublishedSiteCatalog(sites);
    }

    public static GuardianPublishedSite Read(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Guardian published survey {sourceName} is not an object.");
        }

        var identifier = GetRequiredString(root, "sid");
        var kind = identifier.StartsWith("GR", StringComparison.OrdinalIgnoreCase)
            ? GuardianSiteKind.Ruins
            : (identifier.StartsWith("GS", StringComparison.OrdinalIgnoreCase)) switch
            {
                true => GuardianSiteKind.Structure,
                false => throw new InvalidDataException(
                                                                      $"Guardian survey {sourceName} has unknown ID {identifier}.")
            };
        if (!int.TryParse(
            identifier.AsSpan(2),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var siteId))
        {
            throw new InvalidDataException(
                $"Guardian survey {sourceName} has invalid ID {identifier}.");
        }

        return new GuardianPublishedSite(
            siteId,
            kind,
            GetFullBodyName(sourceName, kind),
            GetRequiredString(root, "t"),
            GetInt32(root, "idx") ?? 1,
            GetInt32(root, "sh") ?? -1,
            GetInt32(root, "rh") ?? -1,
            ReadLocation(root),
            ReadStatuses(root),
            ReadRelicHeadings(root),
            ReadObelisks(root),
            GetString(root, "og") ?? string.Empty,
            sourceName);
    }

    private static string GetFullBodyName(
        string sourceName,
        GuardianSiteKind kind)
    {
        var filename = Path.GetFileNameWithoutExtension(sourceName);
        var marker = kind == GuardianSiteKind.Ruins
            ? "-ruins-"
            : "-structure-";
        var markerIndex = filename.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            throw new InvalidDataException(
                $"Guardian survey filename {sourceName} has no {marker} marker.");
        }

        return filename[..markerIndex];
    }

    private static string GetIdentity(
        GuardianSiteKind kind,
        string fullBodyName,
        int index)
    {
        return $"{kind}|{fullBodyName}|{index}";
    }

    private static GuardianSurfaceLocation? ReadLocation(JsonElement root)
    {
        if (!root.TryGetProperty("ll", out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var latitude = GetDouble(value, "lat");
        var longitude = GetDouble(value, "long");
        return latitude is not null && longitude is not null
            ? new GuardianSurfaceLocation(latitude.Value, longitude.Value)
            : null;
    }

    private static Dictionary<string, GuardianPoiStatus> ReadStatuses(
        JsonElement root)
    {
        var statuses = new Dictionary<string, GuardianPoiStatus>(
            StringComparer.Ordinal);
        AddStatuses(root, "pp", GuardianPoiStatus.Present, statuses);
        AddStatuses(root, "pa", GuardianPoiStatus.Absent, statuses);
        AddStatuses(root, "pe", GuardianPoiStatus.Empty, statuses);
        return statuses;
    }

    private static void AddStatuses(
        JsonElement root,
        string propertyName,
        GuardianPoiStatus status,
        Dictionary<string, GuardianPoiStatus> statuses)
    {
        var encoded = GetString(root, propertyName);
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return;
        }

        foreach (var name in encoded.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            statuses[name] = status;
        }
    }

    private static Dictionary<string, int> ReadRelicHeadings(
        JsonElement root)
    {
        var headings = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var encoded = GetString(root, "rth");
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return headings;
        }

        foreach (var pair in encoded.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(
                ':',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !int.TryParse(
                    parts[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var heading))
            {
                throw new InvalidDataException(
                    $"A Guardian relic heading is invalid: {pair}.");
            }

            headings[parts[0]] = heading;
        }

        return headings;
    }

    private static GuardianObelisk[] ReadObelisks(JsonElement root)
    {
        if (!root.TryGetProperty("ao", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Guardian active obelisks are not an array.");
        }

        return value.EnumerateArray()
            .Select(element => ParseObelisk(element.GetString()))
            .ToArray();
    }

    internal static GuardianObelisk ParseObelisk(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidDataException("A Guardian obelisk entry is empty.");
        }

        var parts = encoded.Split('-');
        if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new InvalidDataException(
                $"A Guardian obelisk entry is invalid: {encoded}.");
        }

        var scanned = parts[0].EndsWith('!');
        var name = scanned ? parts[0][..^1] : parts[0];
        var items = parts[1].Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new GuardianObelisk(
            name,
            parts[2],
            scanned,
            items);
    }

    private static string GetRequiredString(
        JsonElement root,
        string propertyName)
    {
        return GetString(root, propertyName) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException(
                $"A Guardian published survey is missing {propertyName}.");
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
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetDouble(out var number)
            && double.IsFinite(number)
                ? number
                : null;
    }
}

public sealed record GuardianPublishedSite(
    int SiteId,
    GuardianSiteKind Kind,
    string FullBodyName,
    string SiteType,
    int Index,
    int SiteHeading,
    int RelicTowerHeading,
    GuardianSurfaceLocation? Location,
    IReadOnlyDictionary<string, GuardianPoiStatus> PoiStatuses,
    IReadOnlyDictionary<string, int> RelicHeadings,
    IReadOnlyList<GuardianObelisk> ActiveObelisks,
    string ObeliskGroups,
    string SourceName);

public sealed record GuardianObelisk(
    string Name,
    string LogCode,
    bool Scanned,
    IReadOnlyList<string> ItemCodes);

public readonly record struct GuardianSurfaceLocation(
    double Latitude,
    double Longitude);

public enum GuardianPoiStatus
{
    Unknown,
    Present,
    Absent,
    Empty,
}
