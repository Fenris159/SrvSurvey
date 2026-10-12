using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exploration;

public sealed record ExplorationPoi(
    long Id,
    string Source,
    string Name,
    string SystemName,
    long? SystemAddress,
    GalacticCoordinate? Position,
    string Category,
    string SecondaryCategory,
    string Summary,
    string Description,
    double? Rating,
    double? AverageStars,
    int Votes,
    bool IsRare
)
{
    public string Identity => Source + ":" + Id.ToString(CultureInfo.InvariantCulture);
    public Uri SourceUri => new(WellKnownUris.EdastroPoiPagePrefix + Id.ToString(CultureInfo.InvariantCulture));
    public bool IsLegacy => Category == "Legacy" || SecondaryCategory == "Legacy";
    public string CategoryText => string.Join(" · ", new[] { Category, SecondaryCategory }.Where(s => s.Length > 0));
    public IReadOnlyList<ExplorationPoiDescriptionBlock> DescriptionBlocks { get; init; } = [];
}

public sealed class ExplorationPoiCatalog
{
    public static ExplorationPoiCatalog Empty { get; } = new([]);

    /// <summary>Captures the supplied attraction snapshot for local system and nearby queries.</summary>
    public ExplorationPoiCatalog(IEnumerable<ExplorationPoi> pois)
    {
        ArgumentNullException.ThrowIfNull(pois);
        Items = pois.DistinctBy(p => p.Identity).ToArray();
    }

    public IReadOnlyList<ExplorationPoi> Items { get; }

    /// <summary>Finds all attractions matching a system name or its exact 64-bit address.</summary>
    public IReadOnlyList<ExplorationPoi> ForSystem(string? name, long? address, bool includeLegacy = false) =>
        Items
            .Where(p =>
                (includeLegacy || !p.IsLegacy)
                && (
                    p.SystemAddress is > 0 && address is > 0
                        ? p.SystemAddress == address
                        : string.Equals(p.SystemName, name?.Trim(), StringComparison.OrdinalIgnoreCase)
                )
            )
            .OrderByDescending(p => p.Rating)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Builds an immutable catalogue from provider rows while skipping unusable attractions.</summary>
    public static ExplorationPoiCatalog Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The EDAstro POI catalogue is not an array.");
        }

        return new ExplorationPoiCatalog(root.EnumerateArray().Select(ParseRow).OfType<ExplorationPoi>());
    }

    /// <summary>Maps one GEC row while preserving provider identity, categories, ratings and description fields.</summary>
    private static ExplorationPoi? ParseRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || Number(row, "id") is not > 0)
        {
            return null;
        }

        string name = Text(row, "name");
        string system = Text(row, "galMapSearch");
        if (name.Length == 0 || system.Length == 0)
        {
            return null;
        }

        string source = Text(row, "source");
        string html = Text(row, "descriptionHtml");
        var description = ExplorationPoiDescription.Parse(html.Length > 0 ? html : Text(row, "descriptionMardown"));

        return new ExplorationPoi(
            Number(row, "id")!.Value,
            source.Length == 0 ? "GEC" : source,
            name,
            system,
            Number(row, "id64") is > 0 ? Number(row, "id64") : null,
            Position(row),
            Text(row, "type"),
            Text(row, "type2"),
            Text(row, "summary"),
            description.Text,
            Decimal(row, "rating"),
            Decimal(row, "avgStars"),
            (int)Math.Clamp(Number(row, "votes") ?? 0, 0, int.MaxValue),
            row.TryGetProperty("rare", out JsonElement rare) && rare.ToString() is "1" or "True" or "true"
        )
        {
            DescriptionBlocks = description.Blocks,
        };
    }

    /// <summary>Reads finite galactic coordinates from the provider's coordinate object.</summary>
    private static GalacticCoordinate? Position(JsonElement row)
    {
        if (!row.TryGetProperty("coordinates", out JsonElement coords) || coords.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        double[] values = coords.EnumerateArray().Select(e => ParseDecimal(e) ?? double.NaN).ToArray();
        return values.Length == 3 && values.All(double.IsFinite)
            ? new GalacticCoordinate(values[0], values[1], values[2])
            : null;
    }

    /// <summary>Reads a nonempty textual provider field with a supplied fallback.</summary>
    private static string Text(JsonElement row, string key) =>
        row.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    /// <summary>Reads integer identity fields without converting 64-bit addresses through floating point.</summary>
    private static long? Number(JsonElement row, string key) =>
        row.TryGetProperty(key, out JsonElement value)
        && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            ? number
            : null;

    /// <summary>Reads a finite rating from either numeric or textual provider JSON.</summary>
    private static double? Decimal(JsonElement row, string key) =>
        row.TryGetProperty(key, out JsonElement value) ? ParseDecimal(value) : null;

    /// <summary>Parses an invariant finite decimal value from provider text.</summary>
    private static double? ParseDecimal(JsonElement value) =>
        double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
        && double.IsFinite(number)
            ? number
            : null;
}
