using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteTemplateCatalog
{
    private const string EmbeddedResourceName =
        "SrvSurvey.Core.Resources.guardianSiteTemplates.json";

    private readonly Dictionary<string, GuardianSiteTemplate> templates;

    public GuardianSiteTemplateCatalog(
        IEnumerable<GuardianSiteTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        this.templates = templates.ToDictionary(
            template => template.SiteType,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<GuardianSiteTemplate> Templates =>
        templates.Values;

    public GuardianSiteTemplate? Find(string? siteType)
    {
        return siteType is not null
            ? templates.GetValueOrDefault(siteType)
            : null;
    }

    public static GuardianSiteTemplateCatalog LoadEmbedded()
    {
        var assembly = typeof(GuardianSiteTemplateCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded Guardian templates {EmbeddedResourceName} are missing.");
        return Load(stream);
    }

    public static GuardianSiteTemplateCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The Guardian template reference is not a JSON object.");
        }

        var templates = new List<GuardianSiteTemplate>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value;
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Guardian template {property.Name} is not an object.");
            }

            var points = ReadPoints(value, "poi");
            templates.Add(new GuardianSiteTemplate(
                property.Name,
                GetString(value, "name") ?? property.Name,
                GetString(value, "backgroundImage") ?? string.Empty,
                ReadPoint(value, "imageOffset") ?? new GuardianMapPoint(0, 0),
                GetDouble(value, "scaleFactor") ?? 1,
                points,
                ReadPoints(value, "destructablePanels"),
                ReadNamedPoints(value, "obeliskGroupNameLocations")));
        }

        return new GuardianSiteTemplateCatalog(templates);
    }

    private static IReadOnlyList<GuardianPointOfInterest> ReadPoints(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Guardian template {propertyName} is not an array.");
        }

        return value.EnumerateArray().Select(ReadPointOfInterest).ToArray();
    }

    internal static GuardianPointOfInterest ReadPointOfInterest(
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "A Guardian point of interest is not an object.");
        }

        var name = GetString(value, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException(
                "A Guardian point of interest is missing its name.");
        }

        var type = ReadPoiType(value);
        return new GuardianPointOfInterest(
            name,
            type,
            GetDouble(value, "angle") ?? 0,
            GetDouble(value, "dist") ?? 0,
            GetDouble(value, "rot") ?? 0);
    }

    private static GuardianPoiType ReadPoiType(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var value))
        {
            return GuardianPoiType.Unknown;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            && Enum.IsDefined(typeof(GuardianPoiType), number))
        {
            return (GuardianPoiType)number;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString();
            if (string.Equals(
                name,
                "brokeObelisk",
                StringComparison.OrdinalIgnoreCase))
            {
                return GuardianPoiType.BrokenObelisk;
            }

            if (string.Equals(
                name,
                "destructablePanel",
                StringComparison.OrdinalIgnoreCase))
            {
                return GuardianPoiType.DestructiblePanel;
            }

            if (Enum.TryParse<GuardianPoiType>(
                name,
                ignoreCase: true,
                out var type))
            {
                return type;
            }
        }

        throw new InvalidDataException(
            "A Guardian point of interest has an unknown type.");
    }

    private static IReadOnlyDictionary<string, GuardianMapPoint> ReadNamedPoints(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new Dictionary<string, GuardianMapPoint>();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Guardian template {propertyName} is not an object.");
        }

        return value.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ParsePoint(property.Value)
                ?? throw new InvalidDataException(
                    $"Guardian map point {property.Name} is invalid."));
    }

    private static GuardianMapPoint? ReadPoint(
        JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            ? ParsePoint(value)
            : null;
    }

    private static GuardianMapPoint? ParsePoint(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var parts = value.GetString()?.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts?.Length == 2
                && double.TryParse(
                    parts[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var x)
                && double.TryParse(
                    parts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var y))
            {
                return new GuardianMapPoint(x, y);
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var x = GetDouble(value, "X") ?? GetDouble(value, "x");
            var y = GetDouble(value, "Y") ?? GetDouble(value, "y");
            if (x is not null && y is not null)
            {
                return new GuardianMapPoint(x.Value, y.Value);
            }
        }

        return null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
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
}

public sealed record GuardianSiteTemplate(
    string SiteType,
    string Name,
    string BackgroundImage,
    GuardianMapPoint ImageOffset,
    double ScaleFactor,
    IReadOnlyList<GuardianPointOfInterest> PointsOfInterest,
    IReadOnlyList<GuardianPointOfInterest> DestructiblePanels,
    IReadOnlyDictionary<string, GuardianMapPoint> ObeliskGroupNameLocations)
{
    public IReadOnlyList<GuardianPointOfInterest> SurveyPoints { get; } =
        PointsOfInterest
            .Where(point => point.Type is not GuardianPoiType.Obelisk
                and not GuardianPoiType.BrokenObelisk)
            .ToArray();

    public IReadOnlyList<GuardianPointOfInterest> RelicTowers { get; } =
        PointsOfInterest
            .Where(point => point.Type == GuardianPoiType.Relic)
            .ToArray();
}

public sealed record GuardianPointOfInterest(
    string Name,
    GuardianPoiType Type,
    double Angle,
    double Distance,
    double Rotation);

public readonly record struct GuardianMapPoint(double X, double Y);

public enum GuardianPoiType
{
    Unknown = 0,
    Relic,
    Orb,
    Casket,
    Tablet,
    Totem,
    Urn,
    EmptyPuddle,
    Component,
    Pylon,
    Obelisk,
    BrokenObelisk,
    DestructiblePanel,
}
