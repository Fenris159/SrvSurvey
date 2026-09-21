namespace SrvSurvey.Core.Mining;

/// <summary>Editable search criteria; results and in-flight requests are never persisted.</summary>
public sealed record MiningSearchPreferences
{
    public string CommodityCategory { get; init; } = "Mining";
    public string Reference { get; init; } = "";
    public string Mineral { get; init; } = "Platinum";
    public string RingType { get; init; } = "All";
    public string Reserve { get; init; } = "All";
    public string Commodity { get; init; } = "Platinum";
    public double Radius { get; init; } = 100;
    public int MinimumHotspots { get; init; } = 1;
    public string Source { get; init; } = "Both";
    public bool OnlyOverlaps { get; init; }
    public bool OnlyRes { get; init; }
    public bool Buying { get; init; }
    public bool GalaxyWide { get; init; }
    public bool ExcludeCarriers { get; init; }
    public bool LargePads { get; init; }
    public int MaximumAgeDays { get; init; } = 2;
    public long MinimumDemand { get; init; }
    public long MaximumDemand { get; init; }
    public int ResultLimit { get; init; } = 30;
    public string PlatinumMode { get; init; } = "Spots++";
    public string StationType { get; init; } = "";
    public string Security { get; init; } = "";
    public string Allegiance { get; init; } = "";
    public string Government { get; init; } = "";
    public string Economy { get; init; } = "";
    public string State { get; init; } = "";
    public string Power { get; init; } = "";
    public string OpposingPower { get; init; } = "";
    public string PowerState { get; init; } = "";
    public long MinimumPopulation { get; init; }
    public string TraderType { get; init; } = "Raw";
}
