using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

public sealed record ColonizationProject
{
    public const string FleetCarrierLoadingBuildType = "fc_loading";

    [JsonPropertyName("buildId")]
    public string BuildId { get; init; } = string.Empty;

    [JsonPropertyName("buildType")]
    public string BuildType { get; init; } = string.Empty;

    [JsonPropertyName("buildName")]
    public string BuildName { get; init; } = string.Empty;

    [JsonPropertyName("marketId")]
    public long MarketId { get; init; }

    [JsonPropertyName("systemAddress")]
    public long SystemAddress { get; init; }

    [JsonPropertyName("systemName")]
    public string SystemName { get; init; } = string.Empty;

    [JsonPropertyName("starPos")]
    public double[] StarPosition { get; init; } = [];

    [JsonPropertyName("bodyNum")]
    public int? BodyNumber { get; init; }

    [JsonPropertyName("bodyName")]
    public string? BodyName { get; init; }

    [JsonPropertyName("factionName")]
    public string? FactionName { get; init; }

    [JsonPropertyName("architectName")]
    public string? ArchitectName { get; init; }

    [JsonPropertyName("maxNeed")]
    public int MaximumRequired { get; init; }

    [JsonPropertyName("sumNeed")]
    public int RemainingRequired { get; init; }

    [JsonPropertyName("sumTotal")]
    public int TotalContributed { get; init; }

    [JsonPropertyName("complete")]
    public bool IsComplete { get; init; }

    [JsonPropertyName("discordLink")]
    public string? DiscordLink { get; init; }

    [JsonPropertyName("isPrimaryPort")]
    public bool IsPrimaryPort { get; init; }

    [JsonPropertyName("commanders")]
    public Dictionary<string, HashSet<string>> Commanders { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("commodities")]
    public Dictionary<string, int> Commodities { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("ready")]
    public HashSet<string> Ready { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("linkedFC")]
    public List<ColonizationProjectFleetCarrier> LinkedFleetCarriers
    {
        get;
        init;
    } = [];

    [JsonPropertyName("Timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    [JsonPropertyName("ETag")]
    public string? ETag { get; init; }

    [JsonIgnore]
    public bool IsFleetCarrierLoading => string.Equals(
        BuildType,
        FleetCarrierLoadingBuildType,
        StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public long Delivered => MaximumRequired <= 0
        ? 0
        : Math.Clamp(
            (long)MaximumRequired - Math.Max(0, RemainingRequired),
            0,
            MaximumRequired);

    [JsonIgnore]
    public double? Progress => MaximumRequired > 0
        ? Math.Clamp(Delivered / (double)MaximumRequired, 0, 1)
        : null;
}

public sealed record ColonizationProjectFleetCarrier
{
    [JsonPropertyName("marketId")]
    public long MarketId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("assign")]
    public HashSet<string> AssignedCommodities { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public static class ColonizationProjectCalculator
{
    public static ColonizationProjectTotals CalculateTotals(
        IEnumerable<ColonizationProject> projects,
        IEnumerable<string>? hiddenBuildIds,
        int shipCargoCapacity)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var hidden = hiddenBuildIds?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
        var selected = projects
            .Where(project => !hidden.Contains(project.BuildId))
            .ToArray();
        var remaining = selected.Sum(
            project => Math.Max(0L, project.RemainingRequired));
        var trips = shipCargoCapacity > 0
            ? (long?)Math.Ceiling(remaining / (double)shipCargoCapacity)
            : null;
        return new ColonizationProjectTotals(
            selected.Length,
            remaining,
            trips);
    }
}

public sealed record ColonizationProjectTotals(
    int SelectedProjectCount,
    long RemainingCargo,
    long? TripsInCurrentShip);
