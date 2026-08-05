using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Exobiology;

public sealed class BiologyCriteriaCatalog
{
    public const int EngineVersion = 4;

    private const string EmbeddedResourcePrefix =
        "SrvSurvey.Core.Resources.bio-criteria.";

    private readonly IReadOnlyList<BiologyCriteriaNode> roots;
    private readonly IReadOnlyList<string> sourceNames;

    public BiologyCriteriaCatalog(
        IEnumerable<BiologyCriteriaNode> roots,
        IEnumerable<string>? sourceNames = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        this.roots = roots.ToArray();
        this.sourceNames = sourceNames?.ToArray() ?? [];
    }

    public IReadOnlyList<BiologyCriteriaNode> Roots => roots;

    public IReadOnlyList<string> SourceNames => sourceNames;

    public static BiologyCriteriaCatalog LoadEmbedded()
    {
        var assembly = typeof(BiologyCriteriaCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(
                EmbeddedResourcePrefix,
                StringComparison.Ordinal))
            .Where(name => name.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException(
                $"No biology criteria resources were found under {EmbeddedResourcePrefix}.");
        }

        var roots = new List<BiologyCriteriaNode>(resourceNames.Length);
        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"The embedded biology criteria {resourceName} is missing.");
            roots.Add(ParseRoot(stream, resourceName));
        }

        return new BiologyCriteriaCatalog(roots, resourceNames);
    }

    public static BiologyCriteriaCatalog Load(
        Stream stream,
        string sourceName = "criteria.json")
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new BiologyCriteriaCatalog(
            [ParseRoot(stream, sourceName)],
            [sourceName]);
    }

    public static BiologyCriteriaCatalog LoadDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var directory = Path.GetFullPath(directoryPath);
        var paths = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        if (paths.Length == 0)
        {
            throw new InvalidDataException(
                $"No biology criteria JSON files were found in {directory}.");
        }

        var roots = new List<BiologyCriteriaNode>(paths.Length);
        foreach (var path in paths)
        {
            using var stream = File.OpenRead(path);
            roots.Add(ParseRoot(stream, path));
        }

        return new BiologyCriteriaCatalog(roots, paths);
    }

    private static BiologyCriteriaNode ParseRoot(
        Stream stream,
        string sourceName)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            return ParseNode(document.RootElement, sourceName);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Biology criteria '{sourceName}' is not valid JSON.",
                ex);
        }
    }

    private static BiologyCriteriaNode ParseNode(
        JsonElement element,
        string sourceName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Biology criteria '{sourceName}' contains a non-object node.");
        }

        var query = ParseQuery(element, sourceName);
        var children = ParseChildren(element, "children", sourceName);
        var commonChildren = ParseOptionalChildren(
            element,
            "commonChildren",
            sourceName);
        var useCommonChildren = GetBoolean(
            element,
            "useCommonChildren",
            sourceName);
        if (useCommonChildren && children.Length > 0)
        {
            throw new InvalidDataException(
                $"Biology criteria '{sourceName}' has a node with both "
                + "children and useCommonChildren.");
        }

        return new BiologyCriteriaNode(
            GetString(element, "genus", sourceName),
            GetString(element, "species", sourceName),
            GetString(element, "variant", sourceName),
            query,
            children,
            useCommonChildren,
            commonChildren);
    }

    private static List<BiologyCriteriaClause> ParseQuery(
        JsonElement element,
        string sourceName)
    {
        if (!element.TryGetProperty("query", out var query))
        {
            return [];
        }

        if (query.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Biology criteria '{sourceName}' has a non-array query.");
        }

        var clauses = new List<BiologyCriteriaClause>();
        foreach (var value in query.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidDataException(
                    $"Biology criteria '{sourceName}' has an invalid query clause.");
            }

            clauses.Add(BiologyCriteriaClause.Parse(value.GetString()!));
        }

        return clauses;
    }

    private static BiologyCriteriaNode[] ParseChildren(
        JsonElement element,
        string propertyName,
        string sourceName)
    {
        if (!element.TryGetProperty(propertyName, out var children))
        {
            return [];
        }

        if (children.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Biology criteria '{sourceName}' has non-array {propertyName}.");
        }

        return children.EnumerateArray()
            .Select(child => ParseNode(child, sourceName))
            .ToArray();
    }

    private static IReadOnlyList<BiologyCriteriaNode>? ParseOptionalChildren(
        JsonElement element,
        string propertyName,
        string sourceName)
    {
        return element.TryGetProperty(propertyName, out _)
            ? ParseChildren(element, propertyName, sourceName)
            : null;
    }

    private static string? GetString(
        JsonElement element,
        string propertyName,
        string sourceName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Biology criteria '{sourceName}' has a non-string {propertyName}.");
        }

        return value.GetString();
    }

    private static bool GetBoolean(
        JsonElement element,
        string propertyName,
        string sourceName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind is not JsonValueKind.True
            and not JsonValueKind.False)
        {
            throw new InvalidDataException(
                $"Biology criteria '{sourceName}' has a non-boolean {propertyName}.");
        }

        return value.GetBoolean();
    }
}

public sealed record BiologyCriteriaNode(
    string? Genus,
    string? Species,
    string? Variant,
    IReadOnlyList<BiologyCriteriaClause> Query,
    IReadOnlyList<BiologyCriteriaNode> Children,
    bool UseCommonChildren,
    IReadOnlyList<BiologyCriteriaNode>? CommonChildren);

public sealed class BiologyCriteriaClause
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex ClausePattern = new(
        @"^\s*(?<property>\w+)\s*(?<operator>[&!]?)\[(?<value>.*)\]\s*$",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex CompositionPattern = new(
        @"^(?<name>[\w\s]+)>=\s*(?<amount>[.\d]+)$",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly IReadOnlyDictionary<string, string> ValueAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Icy"] = "Icy body",
            ["Rocky"] = "Rocky body",
            ["RockyIce"] = "Rocky ice ",
            ["HMC"] = "High metal content ",
            ["MRB"] = "Metal rich body",
        };

    private static readonly Dictionary<string, int[]> RegionAliases =
        new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["Orion-CygnusArm"] = [7, 8, 16, 17, 18, 35],
            ["OuterArm"] = [5, 6, 13, 14, 27, 29, 31, 37, 41],
            ["Scutum-CentaurusArm"] = [9, 10, 11, 12, 24, 25, 26, 28, 42],
            ["PerseusArm"] = [15, 30, 32, 33, 34, 36, 38, 39],
            ["Sagittarius-CarinaArm"] = [9, 18, 19, 20, 21, 22, 23, 40],
            ["CentreLeft"] = [1, 4],
            ["CentreTop"] = [1, 3, 7],
            ["CentreRight"] = [1, 2],
            ["AmphoraBatch"] = [10, 19, 20, 21, 22],
            ["AnemoneBatch"] = [7, 8, 9, 13, 14, 15, 16, 17, 18, 27, 31],
            ["BarkMoundBatch"] =
                [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 20, 25, 32, 33, 34],
            ["BrainTreeBatch"] = [2, 9, 10, 17, 18, 35],
            ["TubersBatch"] = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 18, 19],
            ["ShardBatch"] = [14, 21, 22, 23, 24, 25, 26, 27, 28, 29, 31, 34, 36, 37, 38, 39, 40, 41, 42],
        };

    private static readonly HashSet<string> SupportedProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "body",
            "gravity",
            "temp",
            "pressure",
            "atmosphere",
            "atmosType",
            "atmosComp",
            "matsComp",
            "dist",
            "volcanism",
            "mats",
            "regions",
            "star",
            "parentStar",
            "primaryStar",
            "nebulae",
            "guardian",
        };

    private BiologyCriteriaClause(
        string rawText,
        string property,
        BiologyCriteriaOperator @operator,
        IReadOnlyList<string>? values = null,
        double? minimum = null,
        double? maximum = null,
        IReadOnlyDictionary<string, double>? compositions = null)
    {
        RawText = rawText;
        Property = property;
        Operator = @operator;
        Values = values ?? [];
        Minimum = minimum;
        Maximum = maximum;
        Compositions = compositions
            ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public string RawText { get; }

    public string Property { get; }

    public BiologyCriteriaOperator Operator { get; }

    public IReadOnlyList<string> Values { get; }

    public double? Minimum { get; }

    public double? Maximum { get; }

    public IReadOnlyDictionary<string, double> Compositions { get; }

    public static BiologyCriteriaClause Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            return ParseCore(text);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new InvalidDataException(
                $"Invalid biology criterion: {text}",
                exception);
        }
    }

    private static BiologyCriteriaClause ParseCore(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('#'))
        {
            return new BiologyCriteriaClause(
                trimmed,
                string.Empty,
                BiologyCriteriaOperator.Comment);
        }

        var match = ClausePattern.Match(trimmed);
        if (!match.Success)
        {
            throw new InvalidDataException($"Invalid biology criterion: {text}");
        }

        var property = match.Groups["property"].Value;
        if (!SupportedProperties.Contains(property))
        {
            throw new InvalidDataException(
                $"Unsupported biology criteria property: {property}");
        }

        var valueText = match.Groups["value"].Value.Trim();
        if (valueText.Contains('~'))
        {
            return ParseRange(trimmed, property, valueText);
        }

        if (valueText.Contains(">=", StringComparison.Ordinal))
        {
            return ParseComposition(trimmed, property, valueText);
        }

        var @operator = match.Groups["operator"].Value switch
        {
            "&" => BiologyCriteriaOperator.All,
            "!" => BiologyCriteriaOperator.Not,
            _ => BiologyCriteriaOperator.Is,
        };
        var values = valueText.Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(value => ExpandValue(property, value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length == 0)
        {
            throw new InvalidDataException($"Invalid biology criterion: {text}");
        }

        return new BiologyCriteriaClause(
            trimmed,
            property,
            @operator,
            values);
    }

    public override string ToString() => RawText;

    private static BiologyCriteriaClause ParseRange(
        string rawText,
        string property,
        string valueText)
    {
        var parts = valueText.Split(
            '~',
            StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new InvalidDataException(
                $"Invalid biology criteria range: {rawText}");
        }

        var minimum = ParseOptionalDouble(parts[0], rawText);
        var maximum = ParseOptionalDouble(parts[1], rawText);
        if (minimum is null && maximum is null)
        {
            throw new InvalidDataException(
                $"Invalid biology criteria range: {rawText}");
        }

        return new BiologyCriteriaClause(
            rawText,
            property,
            BiologyCriteriaOperator.Range,
            minimum: minimum,
            maximum: maximum);
    }

    private static BiologyCriteriaClause ParseComposition(
        string rawText,
        string property,
        string valueText)
    {
        var compositions = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var part in valueText.Split(
                     '|',
                     StringSplitOptions.TrimEntries
                     | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = CompositionPattern.Match(part);
            if (!match.Success
                || !double.TryParse(
                    match.Groups["amount"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                throw new InvalidDataException(
                    $"Invalid biology criteria composition: {rawText}");
            }

            compositions.Add(match.Groups["name"].Value.Trim(), amount);
        }

        if (compositions.Count == 0)
        {
            throw new InvalidDataException(
                $"Invalid biology criteria composition: {rawText}");
        }

        return new BiologyCriteriaClause(
            rawText,
            property,
            BiologyCriteriaOperator.Composition,
            compositions: compositions);
    }

    private static double? ParseOptionalDouble(
        string text,
        string rawText)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result))
        {
            throw new InvalidDataException(
                $"Invalid biology criteria number: {rawText}");
        }

        return result;
    }

    private static IEnumerable<string> ExpandValue(
        string property,
        string value)
    {
        if (property == "regions")
        {
            if (int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var regionId))
            {
                return [regionId.ToString(CultureInfo.InvariantCulture)];
            }

            if (!RegionAliases.TryGetValue(value, out var regionIds))
            {
                throw new InvalidDataException(
                    $"Unknown biology criteria region alias: {value}");
            }

            return regionIds.Select(id => id.ToString(CultureInfo.InvariantCulture));
        }

        return [ValueAliases.GetValueOrDefault(value) ?? value];
    }
}

public enum BiologyCriteriaOperator
{
    Is,
    All,
    Not,
    Range,
    Composition,
    Comment,
}
