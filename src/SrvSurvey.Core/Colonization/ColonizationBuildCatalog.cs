using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

public sealed class ColonizationBuildCatalog
{
    private const string ResourceName =
        "SrvSurvey.Core.Resources.colonization-costs2.json";

    private readonly ColonizationBuildCost[] builds;
    private readonly FrozenDictionary<string, ColonizationBuildCost>
        byBuildType;
    private readonly FrozenDictionary<string, ColonizationBuildCost[]>
        byLayout;

    public ColonizationBuildCatalog(
        IEnumerable<ColonizationBuildCost> builds)
    {
        ArgumentNullException.ThrowIfNull(builds);
        this.builds = builds.ToArray();
        Validate(this.builds);
        byBuildType = this.builds.ToFrozenDictionary(
            build => build.BuildType,
            StringComparer.OrdinalIgnoreCase);
        byLayout = this.builds
            .SelectMany(build => build.Layouts.Select(layout => (layout, build)))
            .GroupBy(item => item.layout, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.Select(item => item.build).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ColonizationBuildCost> Builds => builds;

    public int Count => builds.Length;

    public ColonizationBuildCost? FindByBuildType(string? buildType)
    {
        return string.IsNullOrWhiteSpace(buildType)
            ? null
            : byBuildType.GetValueOrDefault(buildType);
    }

    public IReadOnlyList<ColonizationBuildCost> FindByLayout(string? layout)
    {
        return string.IsNullOrWhiteSpace(layout)
            ? []
            : byLayout.GetValueOrDefault(layout) ?? [];
    }

    public IReadOnlyList<ColonizationBuildCost> ForLocation(
        ColonizationBuildLocation location)
    {
        return builds
            .Where(build => build.Location == location)
            .OrderBy(build => build.Tier)
            .ThenBy(build => build.DisplayName)
            .ToArray();
    }

    public static ColonizationBuildCatalog LoadEmbedded()
    {
        var assembly = typeof(ColonizationBuildCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found.");
        return Load(stream);
    }

    public static ColonizationBuildCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            var rows = JsonSerializer.Deserialize<BuildCostRow[]>(
                    stream,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    })
                ?? throw new InvalidDataException(
                    "The colonisation build catalog is empty.");
            return new ColonizationBuildCatalog(rows.Select(ToBuildCost));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The colonisation build catalog is not valid JSON.",
                exception);
        }
    }

    private static ColonizationBuildCost ToBuildCost(BuildCostRow row)
    {
        if (!Enum.TryParse<ColonizationBuildLocation>(
                row.Location,
                ignoreCase: true,
                out var location))
        {
            throw new InvalidDataException(
                $"Unknown colonisation build location '{row.Location}'.");
        }

        return new ColonizationBuildCost(
            row.BuildType ?? string.Empty,
            row.Category ?? string.Empty,
            row.Tier,
            location,
            row.DisplayName ?? string.Empty,
            row.Layouts ?? [],
            row.Cargo ?? new Dictionary<string, int>());
    }

    private static void Validate(
        IReadOnlyList<ColonizationBuildCost> candidateBuilds)
    {
        if (candidateBuilds.Count == 0)
        {
            throw new InvalidDataException(
                "The colonisation build catalog has no entries.");
        }

        var duplicateBuildType = candidateBuilds
            .GroupBy(build => build.BuildType, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBuildType is not null)
        {
            throw new InvalidDataException(
                $"Duplicate colonisation build type '{duplicateBuildType.Key}'.");
        }

        foreach (var build in candidateBuilds)
        {
            if (string.IsNullOrWhiteSpace(build.BuildType)
                || string.IsNullOrWhiteSpace(build.Category)
                || string.IsNullOrWhiteSpace(build.DisplayName)
                || build.Tier <= 0
                || build.Layouts.Count == 0
                || build.Layouts.Any(string.IsNullOrWhiteSpace)
                || build.CommodityCosts.Count == 0
                || build.CommodityCosts.Any(pair =>
                    string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
            {
                throw new InvalidDataException(
                    $"Colonisation build type '{build.BuildType}' is incomplete.");
            }
        }
    }

    private sealed record BuildCostRow(
        [property: JsonPropertyName("buildType")] string? BuildType,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("tier")] int Tier,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("layouts")] string[]? Layouts,
        [property: JsonPropertyName("cargo")]
        Dictionary<string, int>? Cargo);
}

public sealed record ColonizationBuildCost(
    string BuildType,
    string Category,
    int Tier,
    ColonizationBuildLocation Location,
    string DisplayName,
    IReadOnlyList<string> Layouts,
    IReadOnlyDictionary<string, int> CommodityCosts)
{
    public long TotalCargo => CommodityCosts.Values.Sum(value => (long)value);
}

public enum ColonizationBuildLocation
{
    Orbital,
    Surface,
}
