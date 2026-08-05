using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteTemplateCatalog
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string ResourceName =
        "SrvSurvey.Core.Resources.humanSiteTemplates.json";

    private readonly HumanSiteTemplate[] templates;
    private readonly FrozenDictionary<HumanSiteTemplateKey, HumanSiteTemplate>
        byKey;

    public HumanSiteTemplateCatalog(IEnumerable<HumanSiteTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        this.templates = templates.ToArray();
        Validate(this.templates);
        byKey = this.templates.ToFrozenDictionary(
            template => new HumanSiteTemplateKey(
                template.Economy,
                template.SubType));
    }

    public IReadOnlyList<HumanSiteTemplate> Templates => templates;

    public int Count => templates.Length;

    public HumanSiteTemplate? Find(HumanSiteEconomy economy, int subType)
    {
        return byKey.GetValueOrDefault(
            new HumanSiteTemplateKey(economy, subType));
    }

    public IReadOnlyList<HumanSiteTemplate> ForEconomy(
        HumanSiteEconomy economy)
    {
        return templates
            .Where(template => template.Economy == economy)
            .OrderBy(template => template.SubType)
            .ToArray();
    }

    public HumanSiteTemplateCatalog WithTemplate(HumanSiteTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var replaced = false;
        var updated = templates.Select(candidate =>
        {
            if (candidate.Economy != template.Economy
                || candidate.SubType != template.SubType)
            {
                return candidate;
            }

            replaced = true;
            return template;
        }).ToList();
        if (!replaced)
        {
            updated.Add(template);
        }

        return new HumanSiteTemplateCatalog(updated);
    }

    public static HumanSiteTemplateCatalog LoadEmbedded()
    {
        var assembly = typeof(HumanSiteTemplateCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found.");
        return Load(stream);
    }

    public static HumanSiteTemplateCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            var rows = JsonSerializer.Deserialize<TemplateRow[]>(
                    stream,
                    CaseInsensitiveJson)
                ?? throw new InvalidDataException(
                    "The human settlement template catalog is empty.");
            return new HumanSiteTemplateCatalog(rows.Select(ToTemplate));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The human settlement template catalog is not valid JSON.",
                exception);
        }
    }

    private static HumanSiteTemplate ToTemplate(TemplateRow row)
    {
        if (!Enum.TryParse<HumanSiteEconomy>(
                row.Economy,
                ignoreCase: true,
                out var economy)
            || economy == HumanSiteEconomy.Unknown)
        {
            throw new InvalidDataException(
                $"Unknown human settlement economy '{row.Economy}'.");
        }

        return new HumanSiteTemplate(
            economy,
            row.SubType,
            row.Name ?? string.Empty,
            (row.LandingPads ?? []).Select(ToLandingPad).ToArray(),
            (row.SecureDoors ?? []).Select(ToPoi).ToArray(),
            (row.NamedPoi ?? []).Select(ToNamedPoi).ToArray(),
            (row.DataTerminals ?? []).Select(ToPoi).ToArray(),
            (row.CzPoints ?? []).Select(ToPoi).ToArray(),
            (row.Buildings ?? []).Select(ToBuilding).ToArray());
    }

    private static HumanSiteLandingPad ToLandingPad(PoiRow row)
    {
        if (!Enum.TryParse<HumanSiteLandingPadSize>(
                row.Size,
                ignoreCase: true,
                out var size))
        {
            throw new InvalidDataException(
                $"Unknown human settlement landing-pad size '{row.Size}'.");
        }

        return new HumanSiteLandingPad(
            ToPoint(row.Offset),
            row.Rotation,
            row.SecurityLevel,
            row.Floor,
            size);
    }

    private static HumanSitePointOfInterest ToPoi(PoiRow row)
    {
        return new HumanSitePointOfInterest(
            ToPoint(row.Offset),
            row.Rotation,
            row.SecurityLevel,
            row.Floor);
    }

    private static HumanSiteNamedPointOfInterest ToNamedPoi(PoiRow row)
    {
        return new HumanSiteNamedPointOfInterest(
            ToPoint(row.Offset),
            row.Rotation,
            row.SecurityLevel,
            row.Floor,
            row.Name ?? string.Empty);
    }

    private static HumanSiteBuilding ToBuilding(BuildingRow row)
    {
        return new HumanSiteBuilding(
            row.Name ?? string.Empty,
            (row.Paths ?? []).Select(ToBuildingPath).ToArray());
    }

    private static HumanSiteBuildingPath ToBuildingPath(PathRow row)
    {
        var points = (row.PathPoints ?? []).Select(ToPoint).ToArray();
        var pointTypes = row.PathTypes ?? [];
        if (pointTypes.Length > points.Length)
        {
            pointTypes = pointTypes[..points.Length];
        }

        return new HumanSiteBuildingPath(points, pointTypes, row.FillMode);
    }

    private static HumanSiteMapPoint ToPoint(PointRow? row)
    {
        return row is null
            ? new HumanSiteMapPoint(double.NaN, double.NaN)
            : new HumanSiteMapPoint(row.X, row.Y);
    }

    private static void Validate(IReadOnlyList<HumanSiteTemplate> templates)
    {
        if (templates.Count == 0)
        {
            throw new InvalidDataException(
                "The human settlement template catalog has no entries.");
        }

        var duplicate = templates
            .GroupBy(template => new HumanSiteTemplateKey(
                template.Economy,
                template.SubType))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                "Duplicate human settlement template "
                + $"'{duplicate.Key.Economy}/{duplicate.Key.SubType}'.");
        }

        foreach (var template in templates)
        {
            if (template.SubType <= 0
                || string.IsNullOrWhiteSpace(template.Name)
                || template.LandingPads.Count == 0
                || template.Buildings.Count == 0)
            {
                throw new InvalidDataException(
                    $"Human settlement template "
                    + $"'{template.Economy}/{template.SubType}' is incomplete.");
            }

            ValidatePoints(template);
        }
    }

    private static void ValidatePoints(HumanSiteTemplate template)
    {
        var points = template.LandingPads
            .Select(point => point.Offset)
            .Concat(template.SecureDoors.Select(point => point.Offset))
            .Concat(template.NamedPoints.Select(point => point.Offset))
            .Concat(template.DataTerminals.Select(point => point.Offset))
            .Concat(template.ConflictZonePoints.Select(point => point.Offset))
            .Concat(template.Buildings.SelectMany(
                building => building.Paths.SelectMany(path => path.Points)));
        if (points.Any(point => !point.IsFinite))
        {
            throw new InvalidDataException(
                $"Human settlement template "
                + $"'{template.Economy}/{template.SubType}' has an invalid point.");
        }

        if (template.Buildings.Any(building =>
                string.IsNullOrWhiteSpace(building.Name)
                || building.Paths.Count == 0
                || building.Paths.Any(path =>
                    path.Points.Count == 0
                    || path.PointTypes.Count != path.Points.Count)))
        {
            throw new InvalidDataException(
                $"Human settlement template "
                + $"'{template.Economy}/{template.SubType}' has an invalid building path.");
        }
    }

    private readonly record struct HumanSiteTemplateKey(
        HumanSiteEconomy Economy,
        int SubType);

    private sealed record TemplateRow(
        [property: JsonPropertyName("economy")] string? Economy,
        [property: JsonPropertyName("subType")] int SubType,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("landingPads")] PoiRow[]? LandingPads,
        [property: JsonPropertyName("secureDoors")] PoiRow[]? SecureDoors,
        [property: JsonPropertyName("namedPoi")] PoiRow[]? NamedPoi,
        [property: JsonPropertyName("dataTerminals")] PoiRow[]? DataTerminals,
        [property: JsonPropertyName("czPoints")] PoiRow[]? CzPoints,
        [property: JsonPropertyName("buildings")] BuildingRow[]? Buildings);

    private sealed record PoiRow(
        [property: JsonPropertyName("offset")] PointRow? Offset,
        [property: JsonPropertyName("rot")] double Rotation,
        [property: JsonPropertyName("level")] int SecurityLevel,
        [property: JsonPropertyName("floor")] int Floor,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] string? Size);

    private sealed record PointRow(
        [property: JsonPropertyName("X")] double X,
        [property: JsonPropertyName("Y")] double Y);

    private sealed record BuildingRow(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("paths")] PathRow[]? Paths);

    private sealed record PathRow(
        [property: JsonPropertyName("PathPoints")] PointRow[]? PathPoints,
        [property: JsonPropertyName("PathTypes")] byte[]? PathTypes,
        [property: JsonPropertyName("FillMode")] int FillMode);
}

public sealed record HumanSiteTemplate(
    HumanSiteEconomy Economy,
    int SubType,
    string Name,
    IReadOnlyList<HumanSiteLandingPad> LandingPads,
    IReadOnlyList<HumanSitePointOfInterest> SecureDoors,
    IReadOnlyList<HumanSiteNamedPointOfInterest> NamedPoints,
    IReadOnlyList<HumanSitePointOfInterest> DataTerminals,
    IReadOnlyList<HumanSitePointOfInterest> ConflictZonePoints,
    IReadOnlyList<HumanSiteBuilding> Buildings);

public record HumanSitePointOfInterest(
    HumanSiteMapPoint Offset,
    double Rotation,
    int SecurityLevel,
    int Floor);

public sealed record HumanSiteNamedPointOfInterest(
    HumanSiteMapPoint Offset,
    double Rotation,
    int SecurityLevel,
    int Floor,
    string Name) : HumanSitePointOfInterest(
        Offset,
        Rotation,
        SecurityLevel,
        Floor);

public sealed record HumanSiteLandingPad(
    HumanSiteMapPoint Offset,
    double Rotation,
    int SecurityLevel,
    int Floor,
    HumanSiteLandingPadSize Size) : HumanSitePointOfInterest(
        Offset,
        Rotation,
        SecurityLevel,
        Floor);

public sealed record HumanSiteBuilding(
    string Name,
    IReadOnlyList<HumanSiteBuildingPath> Paths);

public sealed record HumanSiteBuildingPath(
    IReadOnlyList<HumanSiteMapPoint> Points,
    IReadOnlyList<byte> PointTypes,
    int FillMode);

public readonly record struct HumanSiteMapPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public bool IsPlausibleMapOffset(double maximumDistance = 10_000)
    {
        return IsFinite
            && Math.Abs(X) <= maximumDistance
            && Math.Abs(Y) <= maximumDistance;
    }
}

public enum HumanSiteEconomy
{
    Unknown,
    Agriculture,
    Colony,
    Damaged,
    Extraction,
    HighTech,
    Industrial,
    Military,
    Prison,
    PrivateEnterprise,
    Refinery,
    Repair,
    Rescue,
    Service,
    Terraforming,
    Tourist,
}

public enum HumanSiteLandingPadSize
{
    Unknown,
    Small,
    Medium,
    Large,
}
