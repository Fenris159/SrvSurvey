using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Colonization;

/// <summary>One public Raven response set; absent optional sections represent unknown data rather than zero cargo.</summary>
public sealed record ColonizationProjectPreviewData(
    ColonizationProject Project,
    Dictionary<string, Dictionary<string, int>>? CarrierCargo,
    ColonizationProjectStatistics? Statistics
);

/// <summary>Raven's delivery totals and hourly commander contributions.</summary>
public sealed record ColonizationProjectStatistics
{
    public long TotalCargo { get; init; }
    public long TotalDeliveries { get; init; }
    public DateTimeOffset? Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public Dictionary<string, long> Cmdrs { get; init; } = [];
    public List<ColonizationDeliveryBucket> Stats { get; init; } = [];
}

/// <summary>Contributions grouped into the hourly buckets returned by Raven.</summary>
public sealed record ColonizationDeliveryBucket
{
    public DateTimeOffset Time { get; init; }
    public Dictionary<string, long> Cmdrs { get; init; } = [];
    public long Total => Cmdrs.Values.Sum(value => Math.Max(0, value));
}

/// <summary>Static build effects from Raven's reference catalog, including tier points and prerequisites.</summary>
public sealed record ColonizationBuildEffects
{
    public string Name { get; init; } = string.Empty;
    public string[] Layouts { get; init; } = [];
    public int? Score { get; init; }
    public string Pads { get; init; } = string.Empty;
    public Dictionary<string, int[]> LandingPads { get; init; } = [];
    public string Economy { get; init; } = string.Empty;
    public Dictionary<string, int> Effects { get; init; } = [];
    public int NeedsTier { get; init; }
    public int NeedsCount { get; init; }
    public int GivesTier { get; init; }
    public int GivesCount { get; init; }
    public string Prerequisite { get; init; } = string.Empty;
    public string[] Unlocks { get; init; } = [];
    public double AverageHaul { get; init; }
}

/// <summary>A linked carrier and its normalized, nonduplicated public cargo.</summary>
public sealed record ColonizationPreviewCarrier(
    long MarketId,
    string Name,
    string DisplayName,
    IReadOnlyDictionary<string, int>? Cargo
)
{
    public string Label => $"{DisplayName} ({Name})";
    public long? TotalCargo => Cargo?.Values.Sum(value => (long)value);
}

/// <summary>A Raven commodity table row with signed carrier surplus or shortage.</summary>
public sealed record ColonizationPreviewCommodity(
    string Key,
    string Name,
    string Category,
    long Need,
    IReadOnlyList<int?> CarrierQuantities
)
{
    public long? CarrierDifference =>
        CarrierQuantities.Any(value => value is null)
            ? null
            : CarrierQuantities.Sum(value => (long)value!.Value) - Need;
}

/// <summary>Calculates independent build report snapshots without applying primary or Show selection filters.</summary>
public sealed partial class ColonizationProjectPreview
{
    public const int LargeShipCapacity = 794;
    public const int MediumShipCapacity = 400;
    private static readonly Dictionary<string, CommodityName> CommodityNames = LoadResource<
        Dictionary<string, CommodityName>
    >("colonization-commodity-names.json");
    private static readonly IReadOnlyDictionary<string, string> CommodityAliases = CreateCommodityAliases();
    private static readonly ColonizationBuildEffects[] BuildEffects = LoadResource<ColonizationBuildEffects[]>(
        "colonization-effects.json"
    );
    private static readonly ColonizationBuildCatalog BuildCatalog = ColonizationBuildCatalog.LoadEmbedded();

    /// <summary>Copies carrier cargo and calculates per-commodity coverage without double counting Raven's spaced aliases.</summary>
    public ColonizationProjectPreview(ColonizationProjectPreviewData data, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(data);
        Project = data.Project;
        Projects = [Project];
        MaximumRequired = Math.Max(0, Project.MaximumRequired);
        Statistics = data.Statistics;
        FetchedAt = fetchedAt;
        Effects = ResolveEffects(Project.BuildType);
        Carriers = Project
            .LinkedFleetCarriers.DistinctBy(carrier => carrier.MarketId)
            .Select(carrier => new ColonizationPreviewCarrier(
                carrier.MarketId,
                carrier.Name,
                carrier.DisplayName,
                data.CarrierCargo?.GetValueOrDefault(carrier.MarketId.ToString(CultureInfo.InvariantCulture))
                    is { } cargo
                    ? Normalize(cargo)
                    : null
            ))
            .ToArray();
        var needs = Normalize(Project.Commodities).ToDictionary(pair => pair.Key, pair => (long)pair.Value);
        Rows = CreateRows(needs, Carriers);
        Remaining = Rows.Sum(row => row.Need);
        CarrierDeficit = CalculateDeficit(Rows, Carriers);
        Delivered = Math.Clamp(MaximumRequired - Remaining, 0, MaximumRequired);
    }

    /// <summary>Aligns normalized requirements and unique carrier stock into one consistent cargo table.</summary>
    private static ColonizationPreviewCommodity[] CreateRows(
        Dictionary<string, long> needs,
        IReadOnlyList<ColonizationPreviewCarrier> carriers
    )
    {
        IEnumerable<string> keys = needs
            .Keys.Concat(carriers.SelectMany(carrier => carrier.Cargo?.Keys ?? []).Where(CommodityNames.ContainsKey))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return keys.Select(key => new ColonizationPreviewCommodity(
                key,
                CommodityNames.GetValueOrDefault(key)?.Name ?? key,
                CommodityNames.GetValueOrDefault(key)?.Category ?? "Other",
                needs.GetValueOrDefault(key),
                carriers.Select(carrier => carrier.Cargo?.GetValueOrDefault(key)).ToArray()
            ))
            .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Leaves deficits unknown if any linked carrier's stock is unavailable.</summary>
    private static long? CalculateDeficit(
        IReadOnlyList<ColonizationPreviewCommodity> rows,
        IReadOnlyList<ColonizationPreviewCarrier> carriers
    ) =>
        carriers.All(carrier => carrier.Cargo is not null)
            ? rows.Sum(row => Math.Max(0, -row.CarrierDifference!.Value))
            : null;

    public ColonizationProject Project { get; }
    public IReadOnlyList<ColonizationProject> Projects { get; }
    public bool IsCombined { get; }
    public long MaximumRequired { get; }
    public IReadOnlyDictionary<string, HashSet<string>> Commanders =>
        Projects
            .SelectMany(project => project.Commanders)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(pair => pair.Value).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase
            );
    public ColonizationProjectStatistics? Statistics { get; }
    public ColonizationBuildEffects? Effects { get; }
    public DateTimeOffset FetchedAt { get; }
    public IReadOnlyList<ColonizationPreviewCarrier> Carriers { get; }
    public IReadOnlyList<ColonizationPreviewCommodity> Rows { get; }
    public long Remaining { get; }
    public long Delivered { get; }
    public long? CarrierDeficit { get; }
    public long? ReadyOnCarriers => Remaining - CarrierDeficit;
    public double? Progress =>
        MaximumRequired > 0 && Projects.All(project => project.MaximumRequired > 0)
            ? 100d * Delivered / MaximumRequired
            : null;

    /// <summary>Calculates rounded-up trips only when both the quantity and ship capacity are known.</summary>
    public static long? Trips(long? quantity, int capacity)
    {
        return quantity is null || capacity <= 0 ? null : (long)Math.Ceiling((double)quantity / capacity);
    }

    /// <summary>Combines Raven effects with layout-specific pad counts and the commodity catalog's current reference haul.</summary>
    internal static ColonizationBuildEffects? ResolveEffects(string buildType)
    {
        string layout = ColonizationBuildCatalog.NormalizeSiteBuildTypeKey(buildType).ToLowerInvariant();
        ColonizationBuildEffects? effects = BuildEffects.FirstOrDefault(build =>
            build.Layouts.Contains(layout, StringComparer.OrdinalIgnoreCase)
        );
        if (effects is null)
        {
            return null;
        }
        IReadOnlyList<ColonizationBuildCost> costs = BuildCatalog.FindByLayout(layout);
        return effects with
        {
            Pads = effects.LandingPads.GetValueOrDefault(layout) is { Length: 3 } pads
                ? $"Small: {pads[0]}, Medium: {pads[1]}, Large: {pads[2]}"
                : "No landing pads",
            AverageHaul = costs.Count > 0 ? costs[0].TotalCargo : effects.AverageHaul,
        };
    }

    /// <summary>Raven may return both journal and display aliases for the same stock; these are alternate names, not separate cargo.</summary>
    private static Dictionary<string, int> Normalize(IReadOnlyDictionary<string, int> cargo)
    {
        return cargo
            .GroupBy(pair => NormalizeCommodityKey(pair.Key), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => Math.Max(0, group.Max(pair => pair.Value)),
                StringComparer.OrdinalIgnoreCase
            );
    }

    /// <summary>Resolves Raven's journal, display, and legacy names to a single stock identity.</summary>
    private static string NormalizeCommodityKey(string name)
    {
        string key = ColonizationConstructionState
            .NormalizeCommodityName(name)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
        return CommodityAliases.GetValueOrDefault(key) ?? key;
    }

    /// <summary>Combines the build catalog's alternate names with renamed non-build commodities seen in Raven carrier cargo.</summary>
    private static Dictionary<string, string> CreateCommodityAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["guardianrelic"] = "ancientrelic",
            ["guardiancasket"] = "ancientcasket",
            ["guardianorb"] = "ancientorb",
            ["guardiantablet"] = "ancienttablet",
            ["guardiantotem"] = "ancienttotem",
            ["guardianurn"] = "ancienturn",
            ["thargoidsensor"] = "unknownartifact",
            ["thargoidprobe"] = "unknownartifact2",
            ["thargoidlink"] = "unknownartifact3",
            ["titandrivecomponent"] = "thargoidtitandrivecomponent",
        };
        foreach (
            IGrouping<string, KeyValuePair<string, CommodityName>> group in CommodityNames.GroupBy(
                pair => pair.Value.Name,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            string canonical = group.First().Key;
            foreach (KeyValuePair<string, CommodityName> pair in group)
            {
                aliases[pair.Key] = canonical;
            }
            string displayKey = group
                .Key.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal);
            aliases[displayKey] = canonical;
        }
        return aliases;
    }

    /// <summary>Loads trusted bundled reference data, keeping public project requests independent of website markup changes.</summary>
    private static T LoadResource<T>(string name)
    {
        using Stream stream =
            typeof(ColonizationProjectPreview).Assembly.GetManifestResourceStream($"SrvSurvey.Core.Resources.{name}")
            ?? throw new InvalidOperationException($"Missing build preview reference data: {name}");
        return JsonSerializer.Deserialize<T>(stream)
            ?? throw new InvalidDataException($"Empty build preview reference data: {name}");
    }

    private sealed record CommodityName(string Name, string Category);
}
