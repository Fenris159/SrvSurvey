using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

public sealed class ColonizationBuildCatalog
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private const string ResourceName = "SrvSurvey.Core.Resources.colonization-costs2.json";

    private static readonly string[] ExtraOrbitalSiteBuildTypes = ["coriolis", "installation", "orbis", "outpost"];

    private readonly ColonizationBuildCost[] builds;
    private readonly FrozenDictionary<string, ColonizationBuildCost> byBuildType;
    private readonly FrozenDictionary<string, ColonizationBuildCost[]> byLayout;
    private readonly FrozenSet<string> orbitalSiteBuildTypeKeys;

    public ColonizationBuildCatalog(IEnumerable<ColonizationBuildCost> builds)
    {
        ArgumentNullException.ThrowIfNull(builds);
        this.builds = builds.ToArray();
        Validate(this.builds);
        byBuildType = this.builds.ToFrozenDictionary(build => build.BuildType, StringComparer.OrdinalIgnoreCase);
        byLayout = this
            .builds.SelectMany(build => build.Layouts.Select(layout => (layout, build)))
            .GroupBy(item => item.layout, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.Select(item => item.build).ToArray(),
                StringComparer.OrdinalIgnoreCase
            );
        SiteBuildTypes = this
            .builds.SelectMany(build => build.Layouts.Concat([build.BuildType]))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        orbitalSiteBuildTypeKeys = this
            .builds.Where(build => build.Location == ColonizationBuildLocation.Orbital)
            .SelectMany(build => build.Layouts.Concat([build.BuildType]))
            .Select(NormalizeSiteBuildTypeKey)
            .Concat(ExtraOrbitalSiteBuildTypes)
            .Where(key => key.Length > 0)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ColonizationBuildCost> Builds => builds;

    public IReadOnlyList<string> SiteBuildTypes { get; }

    public int Count => builds.Length;

    public ColonizationBuildCost? FindByBuildType(string? buildType)
    {
        return string.IsNullOrWhiteSpace(buildType) ? null : byBuildType.GetValueOrDefault(buildType);
    }

    public bool IsOrbitalSiteBuildType(string? buildType)
    {
        string key = NormalizeSiteBuildTypeKey(buildType);
        return key.Length > 0 && orbitalSiteBuildTypeKeys.Contains(key);
    }

    public bool TryResolveSiteBuildType(string? buildType, out ColonizationBuildCost? build)
    {
        string key = NormalizeSiteBuildTypeKey(buildType);
        if (key.Length == 0)
        {
            build = null;
            return false;
        }

        IReadOnlyList<ColonizationBuildCost> layouts = FindByLayout(key);
        if (layouts.Count > 0)
        {
            build = layouts[0];
            return true;
        }

        build = FindByBuildType(key);
        return build is not null;
    }

    public static string NormalizeSiteBuildTypeKey(string? buildType)
    {
        if (string.IsNullOrWhiteSpace(buildType))
        {
            return string.Empty;
        }

        string key = buildType.Replace(" (primary)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        key = key.TrimEnd('?').Trim();
        return key.Length == 0 ? string.Empty : key.Replace(' ', '_');
    }

    public IReadOnlyList<ColonizationBuildCost> FindByLayout(string? layout)
    {
        return string.IsNullOrWhiteSpace(layout) ? [] : byLayout.GetValueOrDefault(layout) ?? [];
    }

    public IReadOnlyList<ColonizationBuildCost> ForLocation(ColonizationBuildLocation location)
    {
        return builds
            .Where(build => build.Location == location)
            .OrderBy(build => build.Tier)
            .ThenBy(build => build.DisplayName)
            .ToArray();
    }

    public static ColonizationBuildCatalog LoadEmbedded()
    {
        Assembly assembly = typeof(ColonizationBuildCatalog).Assembly;
        using Stream stream =
            assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        return Load(stream);
    }

    public static ColonizationBuildCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            BuildCostRow[] rows =
                JsonSerializer.Deserialize<BuildCostRow[]>(stream, CaseInsensitiveJson)
                ?? throw new InvalidDataException("The colonisation build catalog is empty.");
            return new ColonizationBuildCatalog(rows.Select(ToBuildCost));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The colonisation build catalog is not valid JSON.", exception);
        }
    }

    private static ColonizationBuildCost ToBuildCost(BuildCostRow row)
    {
        if (
            !Enum.TryParse<ColonizationBuildLocation>(
                row.Location,
                ignoreCase: true,
                out ColonizationBuildLocation location
            )
        )
        {
            throw new InvalidDataException($"Unknown colonisation build location '{row.Location}'.");
        }

        return new ColonizationBuildCost(
            row.BuildType ?? string.Empty,
            row.Category ?? string.Empty,
            row.Tier,
            location,
            row.DisplayName ?? string.Empty,
            row.Layouts ?? [],
            row.Cargo ?? []
        );
    }

    private static void Validate(ColonizationBuildCost[] candidateBuilds)
    {
        if (candidateBuilds.Length == 0)
        {
            throw new InvalidDataException("The colonisation build catalog has no entries.");
        }

        IGrouping<string, ColonizationBuildCost>? duplicateBuildType = candidateBuilds
            .GroupBy(build => build.BuildType, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBuildType is not null)
        {
            throw new InvalidDataException($"Duplicate colonisation build type '{duplicateBuildType.Key}'.");
        }

        foreach (ColonizationBuildCost build in candidateBuilds)
        {
            if (
                string.IsNullOrWhiteSpace(build.BuildType)
                || string.IsNullOrWhiteSpace(build.Category)
                || string.IsNullOrWhiteSpace(build.DisplayName)
                || build.Tier <= 0
                || build.Layouts.Count == 0
                || build.Layouts.Any(string.IsNullOrWhiteSpace)
                || build.CommodityCosts.Count == 0
                || build.CommodityCosts.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)
            )
            {
                throw new InvalidDataException($"Colonisation build type '{build.BuildType}' is incomplete.");
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
        [property: JsonPropertyName("cargo")] Dictionary<string, int>? Cargo
    );
}

public sealed record ColonizationBuildCost(
    string BuildType,
    string Category,
    int Tier,
    ColonizationBuildLocation Location,
    string DisplayName,
    IReadOnlyList<string> Layouts,
    IReadOnlyDictionary<string, int> CommodityCosts
)
{
    public long TotalCargo => CommodityCosts.Values.Sum(value => (long)value);
}

public enum ColonizationBuildLocation
{
    Orbital,
    Surface,
}
