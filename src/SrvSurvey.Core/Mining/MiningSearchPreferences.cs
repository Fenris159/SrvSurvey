namespace SrvSurvey.Core.Mining;

/// <summary>Editable search criteria; results and in-flight requests are never persisted.</summary>
public sealed record MiningSearchPreferences
{
    public string CommodityCategory { get; init; } = "Mining";
    public string Reference { get; init; } = "";
    public string Mineral { get; init; } = "Platinum";
    public string RingType { get; init; } = "All";
    public string Commodity { get; init; } = "Platinum";
    public double Radius { get; init; } = 100;
    public int MinimumHotspots { get; init; } = 1;
    public string Source { get; init; } = "Both";
    public bool OnlyOverlaps { get; init; } = false;
    public bool OnlyRes { get; init; } = false;
    public bool Buying { get; init; } = false;
    public bool GalaxyWide { get; init; } = false;
    public bool ExcludeCarriers { get; init; } = false;
    public bool LargePads { get; init; } = false;
    public int MaximumAgeDays { get; init; } = 2;
    public string StationType { get; init; } = "";
    public string Security { get; init; } = "";
    public string Allegiance { get; init; } = "";
    public string Government { get; init; } = "";
    public string Economy { get; init; } = "";
    public string State { get; init; } = "";
    public string Power { get; init; } = "";
    public string PowerState { get; init; } = "";
    public long MinimumPopulation { get; init; } = 0;
    public string TraderType { get; init; } = "Raw";
}
