using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

/// <summary>A commander-visible Nexus entry whose ID resolves the full Chain endpoint.</summary>
public sealed record ColonizationNexusSummary
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public bool Open { get; init; }
    public string? Destination { get; init; }
}

/// <summary>Raven's Nexus snapshot; carrier membership and per-system assignments remain distinct.</summary>
public sealed record ColonizationNexus
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public bool Open { get; init; }

    [JsonPropertyName("cmdrs")]
    public IReadOnlyList<string> Commanders { get; init; } = [];

    [JsonPropertyName("fcs")]
    public IReadOnlyList<ColonizationFleetCarrier> FleetCarriers { get; init; } = [];
    public IReadOnlyList<ColonizationNexusSystem> Systems { get; init; } = [];
    public IReadOnlyList<long> Hubs { get; init; } = [];
    public string? Notes { get; init; }
}

public sealed record ColonizationNexusSystem
{
    [JsonPropertyName("id64")]
    public long SystemAddress { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Nickname { get; init; }
    public long? Total { get; init; }
    public long? Progress { get; init; }
    public Dictionary<string, long>? Needs { get; init; }

    [JsonPropertyName("fcs")]
    public IReadOnlyList<long> FleetCarrierIds { get; init; } = [];
    public IReadOnlyList<ColonizationProject> Builds { get; init; } = [];
    public Dictionary<string, int> BuildTypes { get; init; } = [];
}
