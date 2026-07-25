using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Storage;

public sealed class HumanSiteKnowledgeStore
{
    private readonly LegacySystemDataFileStore fileStore;

    public HumanSiteKnowledgeStore(string dataDirectory)
    {
        fileStore = new LegacySystemDataFileStore(dataDirectory);
    }

    public async Task<HumanSiteKnowledgeLoadResult> LoadAsync(
        HumanSiteKnowledgeContext context,
        long marketId,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        if (marketId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marketId));
        }

        var result = await fileStore.LoadAsync(
                ToFileContext(context),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Root is null)
        {
            return new HumanSiteKnowledgeLoadResult(
                result.Path,
                result.Exists,
                false,
                null,
                result.Error,
                []);
        }

        var warnings = new List<string>();
        var station = FindStation(result.Root, marketId);
        if (station is null)
        {
            return new HumanSiteKnowledgeLoadResult(
                result.Path,
                true,
                false,
                null,
                null,
                warnings);
        }

        var knowledge = ReadKnowledge(station, context, warnings);
        return new HumanSiteKnowledgeLoadResult(
            result.Path,
            true,
            true,
            knowledge,
            knowledge is null
                ? "The saved human settlement entry is incomplete."
                : null,
            warnings);
    }

    public async Task<string> SaveAsync(
        HumanSiteKnowledgeContext context,
        HumanSiteLiveSnapshot site,
        HumanSiteGeometrySource geometrySource = HumanSiteGeometrySource.Unknown,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(site);
        if (site.SystemAddress != context.SystemAddress)
        {
            throw new ArgumentException(
                "The human settlement belongs to a different system.",
                nameof(site));
        }

        return await fileStore.UpdateAsync(
                ToFileContext(context),
                root => Save(root, context, site, geometrySource),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Save(
        JsonObject root,
        HumanSiteKnowledgeContext context,
        HumanSiteLiveSnapshot site,
        HumanSiteGeometrySource geometrySource)
    {
        var stations = GetOrCreateArray(root, "stations");
        var station = stations
            .OfType<JsonObject>()
            .FirstOrDefault(candidate =>
                ReadInt64(candidate, "marketId") == site.MarketId);
        var isNew = station is null;
        if (station is null)
        {
            station = new JsonObject();
            stations.Add(station);
        }

        station["timestamp"] = site.LastUpdated;
        station["name"] = site.Name;
        station["marketId"] = site.MarketId;
        station["systemAddress"] = site.SystemAddress;
        station["bodyId"] = site.BodyId;
        station["stationEconomy"] = site.EconomyToken;
        station["lat"] = site.Location.Latitude;
        station["long"] = site.Location.Longitude;
        if (!string.IsNullOrWhiteSpace(site.StationType))
        {
            station["stationType"] = site.StationType;
        }

        if (context.RadiusMeters > 0)
        {
            station["bodyRadius"] = context.RadiusMeters;
        }

        if (site.AvailablePads.Total > 0)
        {
            station["availblePads"] = new JsonObject
            {
                ["Large"] = site.AvailablePads.Large,
                ["Medium"] = site.AvailablePads.Medium,
                ["Small"] = site.AvailablePads.Small,
            };
        }

        var existingSubType = ReadInt32(station, "subType") ?? 0;
        if (site.SubType > 0 || isNew && existingSubType <= 0)
        {
            station["subType"] = Math.Max(0, site.SubType);
        }

        var existingHeading = ReadDouble(station, "heading");
        if (site.Heading is { } heading && double.IsFinite(heading))
        {
            station["heading"] = heading;
            station["calcMethod"] = geometrySource.ToString();
        }
        else if (isNew && existingHeading is null)
        {
            station["heading"] = -1;
            station["calcMethod"] = HumanSiteGeometrySource.Unknown.ToString();
        }
    }

    private static HumanSiteKnowledge? ReadKnowledge(
        JsonObject station,
        HumanSiteKnowledgeContext context,
        ICollection<string> warnings)
    {
        var marketId = ReadInt64(station, "marketId") ?? 0;
        var systemAddress = ReadInt64(station, "systemAddress")
            ?? context.SystemAddress;
        var bodyId = ReadInt32(station, "bodyId") ?? -1;
        var name = ReadString(station, "name");
        var economyToken = ReadString(station, "stationEconomy");
        var economy = HumanSiteEconomyParser.ParseJournalValue(economyToken);
        var latitude = ReadDouble(station, "lat");
        var longitude = ReadDouble(station, "long");
        if (marketId <= 0
            || systemAddress <= 0
            || bodyId < 0
            || string.IsNullOrWhiteSpace(name)
            || economy == HumanSiteEconomy.Unknown
            || latitude is not >= -90 or > 90
            || longitude is not >= -180 or > 180)
        {
            return null;
        }

        var subType = Math.Max(0, ReadInt32(station, "subType") ?? 0);
        var heading = ReadDouble(station, "heading");
        if (heading is not null
            && (!double.IsFinite(heading.Value) || heading < 0))
        {
            heading = null;
        }
        else if (heading is not null)
        {
            heading = SrvSurvey.Core.Navigation.SurfaceNavigation
                .NormalizeDegrees(heading.Value);
        }

        var pads = ReadLandingPads(station);
        if (subType == 0 && heading is not null)
        {
            warnings.Add(
                $"Settlement {marketId} has a heading but no known subtype.");
        }

        return new HumanSiteKnowledge(
            name,
            marketId,
            systemAddress,
            bodyId,
            economy,
            economyToken ?? string.Empty,
            new HumanSiteSurfaceLocation(latitude.Value, longitude.Value),
            subType,
            heading,
            pads,
            ReadGeometrySource(station));
    }

    private static HumanSiteGeometrySource ReadGeometrySource(
        JsonObject station)
    {
        var value = ReadString(station, "calcMethod");
        return Enum.TryParse<HumanSiteGeometrySource>(
            value,
            ignoreCase: true,
            out var source)
                ? source
                : HumanSiteGeometrySource.Unknown;
    }

    private static HumanSiteLandingPads ReadLandingPads(JsonObject station)
    {
        var pads = GetProperty(station, "availblePads") as JsonObject
            ?? GetProperty(station, "availablePads") as JsonObject;
        return pads is null
            ? HumanSiteLandingPads.Empty
            : new HumanSiteLandingPads(
                Math.Max(0, ReadInt32(pads, "Small") ?? 0),
                Math.Max(0, ReadInt32(pads, "Medium") ?? 0),
                Math.Max(0, ReadInt32(pads, "Large") ?? 0));
    }

    private static JsonObject? FindStation(JsonObject root, long marketId)
    {
        return GetProperty(root, "stations") is JsonArray stations
            ? stations.OfType<JsonObject>().FirstOrDefault(station =>
                ReadInt64(station, "marketId") == marketId)
            : null;
    }

    private static JsonArray GetOrCreateArray(
        JsonObject root,
        string propertyName)
    {
        if (GetProperty(root, propertyName) is JsonArray existing)
        {
            return existing;
        }

        var created = new JsonArray();
        root[propertyName] = created;
        return created;
    }

    private static JsonNode? GetProperty(
        JsonObject root,
        string propertyName)
    {
        foreach (var property in root)
        {
            if (string.Equals(
                property.Key,
                propertyName,
                StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadString(
        JsonObject root,
        string propertyName)
    {
        return GetProperty(root, propertyName) is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static int? ReadInt32(
        JsonObject root,
        string propertyName)
    {
        var value = ReadInt64(root, propertyName);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static long? ReadInt64(
        JsonObject root,
        string propertyName)
    {
        if (GetProperty(root, propertyName) is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
            && long.TryParse(text, out number)
                ? number
                : null;
    }

    private static double? ReadDouble(
        JsonObject root,
        string propertyName)
    {
        if (GetProperty(root, propertyName) is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }

    private static LegacySystemDataFileContext ToFileContext(
        HumanSiteKnowledgeContext context)
    {
        return new LegacySystemDataFileContext(
            context.FrontierId,
            context.CommanderName,
            context.SystemName,
            context.SystemAddress,
            context.StarPosition);
    }

    private static void ValidateContext(HumanSiteKnowledgeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SystemAddress <= 0
            || !double.IsFinite(context.RadiusMeters)
            || context.RadiusMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }
    }
}

public sealed record HumanSiteKnowledgeContext(
    string FrontierId,
    string? CommanderName,
    string SystemName,
    long SystemAddress,
    GalacticCoordinate? StarPosition,
    double RadiusMeters);

public sealed record HumanSiteKnowledgeLoadResult(
    string Path,
    bool FileExists,
    bool SiteExists,
    HumanSiteKnowledge? Knowledge,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Knowledge is not null;
}
