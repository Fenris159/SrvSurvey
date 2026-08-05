using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianCommanderDataReader
{
    private readonly string dataDirectory;
    private readonly GuardianPublishedSiteCatalog publishedSites;

    public GuardianCommanderDataReader(
        string dataDirectory,
        GuardianPublishedSiteCatalog? publishedSites = null)
    {
        this.dataDirectory = GetFullPath(dataDirectory);
        this.publishedSites = publishedSites
            ?? GuardianPublishedSiteCatalog.LoadEmbedded();
    }

    public async Task<GuardianCommanderDataReadResult> ReadAsync(
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default)
    {
        ValidateFrontierId(frontierId);
        var folder = Path.Combine(dataDirectory, "guardian", frontierId);
        if (!isOdyssey)
        {
            folder = Path.Combine(folder, "legacy");
        }

        if (!Directory.Exists(folder))
        {
            return GuardianCommanderDataReadResult.Empty;
        }

        var surveys = new List<GuardianCommanderSiteSurvey>();
        var beacons = new List<GuardianCommanderBeaconVisit>();
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     folder,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filename = Path.GetFileName(path);
            if (filename.EndsWith(
                "-beacon.json",
                StringComparison.OrdinalIgnoreCase))
            {
                var beacon = await ReadBeaconAsync(
                        path,
                        errors,
                        isLegacy: !isOdyssey,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (beacon is not null)
                {
                    beacons.Add(beacon);
                }
            }
            else if (filename.Contains(
                    "-ruins-",
                    StringComparison.OrdinalIgnoreCase)
                || filename.Contains(
                    "-structure-",
                    StringComparison.OrdinalIgnoreCase))
            {
                var survey = await ReadSurveyAsync(
                        path,
                        errors,
                        isLegacy: !isOdyssey,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (survey is not null)
                {
                    surveys.Add(survey);
                }
            }
        }

        return new GuardianCommanderDataReadResult(
            surveys.OrderBy(survey => survey.SystemName)
                .ThenBy(survey => survey.BodyName)
                .ThenBy(survey => survey.Index)
                .ToArray(),
            beacons.OrderBy(beacon => beacon.SystemName)
                .ThenBy(beacon => beacon.BodyName)
                .ToArray(),
            errors);
    }

    private async Task<GuardianCommanderSiteSurvey?> ReadSurveyAsync(
        string path,
        ICollection<string> errors,
        bool isLegacy,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(path, errors, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        using (document)
        {
            try
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        "The Guardian survey root is not an object.");
                }

                return new GuardianCommanderSiteSurvey(
                    path,
                    GetString(root, "name") ?? string.Empty,
                    GetString(root, "nameLocalised") ?? string.Empty,
                    GetString(root, "commander") ?? string.Empty,
                    GetDateTimeOffset(root, "firstVisited")
                        ?? DateTimeOffset.MinValue,
                    GetDateTimeOffset(root, "lastVisited")
                        ?? DateTimeOffset.MinValue,
                    GetString(root, "type") ?? "Unknown",
                    GetInt32(root, "index") ?? 0,
                    GetInt64(root, "systemAddress") ?? 0,
                    GetString(root, "systemName") ?? string.Empty,
                    GetInt32(root, "bodyId") ?? -1,
                    GetString(root, "bodyName") ?? string.Empty,
                    GetString(root, "notes") ?? string.Empty,
                    GetBoolean(root, "legacy") ?? isLegacy,
                    new GuardianSurveyData
                    {
                        SiteType = GetString(root, "type") ?? "Unknown",
                        SiteHeading = GetInt32(root, "siteHeading") ?? -1,
                        RelicTowerHeading = GetInt32(
                                root,
                                "relicTowerHeading")
                            ?? -1,
                        Location = ReadLocation(root),
                        PoiStatuses = ReadPoiStatuses(root),
                        RelicHeadings = ReadRelicHeadings(root),
                        ComponentMaterials = ReadComponentMaterials(root),
                        RawPointsOfInterest = ReadRawPoints(root),
                    },
                    ReadActiveObelisks(root),
                    ReadObeliskGroups(root));
            }
            catch (Exception exception) when (
                exception is JsonException
                    or InvalidDataException
                    or FormatException)
            {
                errors.Add($"Could not read {path}: {exception.Message}");
                return null;
            }
        }
    }

    private static async Task<GuardianCommanderBeaconVisit?> ReadBeaconAsync(
        string path,
        ICollection<string> errors,
        bool isLegacy,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(path, errors, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        using (document)
        {
            try
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        "The Guardian beacon root is not an object.");
                }

                return new GuardianCommanderBeaconVisit(
                    path,
                    GetDateTimeOffset(root, "firstVisited")
                        ?? DateTimeOffset.MinValue,
                    GetDateTimeOffset(root, "lastVisited")
                        ?? DateTimeOffset.MinValue,
                    GetString(root, "systemName") ?? string.Empty,
                    GetInt64(root, "systemAddress") ?? 0,
                    GetString(root, "bodyName") ?? string.Empty,
                    GetInt32(root, "bodyId") ?? -1,
                    GetString(root, "notes") ?? string.Empty,
                    GetBoolean(root, "legacy") ?? isLegacy,
                    ReadScannedLocations(root));
            }
            catch (Exception exception) when (
                exception is JsonException
                    or InvalidDataException
                    or FormatException)
            {
                errors.Add($"Could not read {path}: {exception.Message}");
                return null;
            }
        }
    }

    private static async Task<JsonDocument?> ReadDocumentAsync(
        string path,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            errors.Add($"Could not read {path}: {exception.Message}");
            return null;
        }
    }

    private static IReadOnlyDictionary<string, GuardianPoiStatus>
        ReadPoiStatuses(JsonElement root)
    {
        var statuses = new Dictionary<string, GuardianPoiStatus>(
            StringComparer.Ordinal);
        if (root.TryGetProperty("poiStatus", out var oldStatuses)
            && oldStatuses.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in oldStatuses.EnumerateObject())
            {
                statuses[property.Name] = ParsePoiStatus(property.Value);
            }
        }

        if (statuses.Count == 0)
        {
            AddCompactStatuses(
                root,
                "poiPresent",
                GuardianPoiStatus.Present,
                statuses);
            AddCompactStatuses(
                root,
                "poiAbsent",
                GuardianPoiStatus.Absent,
                statuses);
            AddCompactStatuses(
                root,
                "poiEmpty",
                GuardianPoiStatus.Empty,
                statuses);
        }

        if (statuses.Count == 0
            && root.TryGetProperty("confirmedPOI", out var confirmed)
            && confirmed.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in confirmed.EnumerateObject().Where(property =>
                property.Value.ValueKind is JsonValueKind.True
                    or JsonValueKind.False))
            {
                statuses[property.Name] = property.Value.GetBoolean()
                    ? GuardianPoiStatus.Present
                    : GuardianPoiStatus.Absent;
            }
        }

        return statuses;
    }

    private static GuardianPoiStatus ParsePoiStatus(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            && Enum.IsDefined(typeof(GuardianPoiStatus), number))
        {
            return (GuardianPoiStatus)number;
        }

        if (value.ValueKind == JsonValueKind.String
            && Enum.TryParse<GuardianPoiStatus>(
                value.GetString(),
                ignoreCase: true,
                out var status))
        {
            return status;
        }

        throw new InvalidDataException("A Guardian POI status is invalid.");
    }

    private static void AddCompactStatuses(
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

    private static IReadOnlyDictionary<string, int> ReadRelicHeadings(
        JsonElement root)
    {
        var headings = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!root.TryGetProperty("relicHeadings", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return headings;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Guardian relic headings are not an object.");
        }

        foreach (var property in value.EnumerateObject())
        {
            if (!property.Value.TryGetInt32(out var heading))
            {
                throw new InvalidDataException(
                    $"Guardian relic heading {property.Name} is invalid.");
            }

            headings[property.Name] = heading;
        }

        return headings;
    }

    private static IReadOnlyDictionary<string, GuardianComponentLoadout>
        ReadComponentMaterials(JsonElement root)
    {
        var components = new Dictionary<string, GuardianComponentLoadout>(
            StringComparer.Ordinal);
        if (!root.TryGetProperty("components", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return components;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return components;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String
                && GuardianComponentLoadout.TryParseLegacy(
                    item.GetString(),
                    out var loadout))
            {
                components[loadout.Name] = loadout;
            }
        }

        return components;
    }

    private static IReadOnlyList<GuardianPointOfInterest>? ReadRawPoints(
        JsonElement root)
    {
        if (!root.TryGetProperty("rawPoi", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Guardian raw POIs are not an array.");
        }

        return value.EnumerateArray()
            .Select(GuardianSiteTemplateCatalog.ReadPointOfInterest)
            .ToArray();
    }

    private GuardianObelisk[] ReadActiveObelisks(
        JsonElement root)
    {
        if (!root.TryGetProperty("activeObelisks", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(element => GuardianPublishedSiteCatalog.ParseObelisk(
                    element.GetString()))
                .ToArray();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return value.EnumerateObject()
                .Select(property =>
                {
                    var old = property.Value;
                    return new GuardianObelisk(
                        property.Name,
                        GetString(old, "msg") ?? string.Empty,
                        GetBoolean(old, "scanned") ?? false,
                        publishedSites.FindItemCodesByLog(
                            GetString(old, "msg")));
                })
                .ToArray();
        }

        throw new InvalidDataException(
            "Guardian active obelisks are neither an array nor an object.");
    }

    private static IReadOnlySet<char> ReadObeliskGroups(JsonElement root)
    {
        if (!root.TryGetProperty("obeliskGroups", out var value))
        {
            return new HashSet<char>();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.ToHashSet() ?? new HashSet<char>();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.String
                    && element.GetString() is { Length: > 0 } item
                        ? item[0]
                        : throw new InvalidDataException(
                            "A Guardian obelisk group is invalid."))
                .ToHashSet();
        }

        throw new InvalidDataException(
            "Guardian obelisk groups are neither a string nor an array.");
    }

    private static IReadOnlyDictionary<DateTimeOffset, GuardianSurfaceLocation>
        ReadScannedLocations(JsonElement root)
    {
        var locations = new Dictionary<DateTimeOffset, GuardianSurfaceLocation>();
        if (!root.TryGetProperty("scannedLocations", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return locations;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Guardian beacon scanned locations are not an object.");
        }

        foreach (var property in value.EnumerateObject())
        {
            if (!DateTimeOffset.TryParse(
                    property.Name,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var timestamp)
                || ReadLocationValue(property.Value) is not { } location)
            {
                throw new InvalidDataException(
                    "A Guardian beacon scanned location is invalid.");
            }

            locations[timestamp] = location;
        }

        return locations;
    }

    private static GuardianSurfaceLocation? ReadLocation(JsonElement root)
    {
        return root.TryGetProperty("location", out var value)
            ? ReadLocationValue(value)
            : null;
    }

    private static GuardianSurfaceLocation? ReadLocationValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var latitude = GetDouble(value, "lat");
        var longitude = GetDouble(value, "long");
        return latitude is not null && longitude is not null
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
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var number)
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

    private static DateTimeOffset? GetDateTimeOffset(
        JsonElement root,
        string propertyName)
    {
        return GetString(root, propertyName) is { } value
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp)
                    ? timestamp
                    : null;
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static void ValidateFrontierId(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (frontierId is "." or ".."
            || !string.Equals(
                Path.GetFileName(frontierId),
                frontierId,
                StringComparison.Ordinal)
            || frontierId.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException(
                "The Frontier ID must be a folder name, not a path.",
                nameof(frontierId));
        }
    }
}

public sealed record GuardianCommanderSiteSurvey(
    string Path,
    string Name,
    string LocalizedName,
    string Commander,
    DateTimeOffset FirstVisited,
    DateTimeOffset LastVisited,
    string SiteType,
    int Index,
    long SystemAddress,
    string SystemName,
    int BodyId,
    string BodyName,
    string Notes,
    bool Legacy,
    GuardianSurveyData Survey,
    IReadOnlyList<GuardianObelisk> ActiveObelisks,
    IReadOnlySet<char> ObeliskGroups);

public sealed record GuardianCommanderBeaconVisit(
    string Path,
    DateTimeOffset FirstVisited,
    DateTimeOffset LastVisited,
    string SystemName,
    long SystemAddress,
    string BodyName,
    int BodyId,
    string Notes,
    bool Legacy,
    IReadOnlyDictionary<DateTimeOffset, GuardianSurfaceLocation>
        ScannedLocations);

public sealed record GuardianCommanderDataReadResult(
    IReadOnlyList<GuardianCommanderSiteSurvey> Surveys,
    IReadOnlyList<GuardianCommanderBeaconVisit> Beacons,
    IReadOnlyList<string> Errors)
{
    public static GuardianCommanderDataReadResult Empty { get; } =
        new([], [], []);
}
