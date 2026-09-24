namespace SrvSurvey.Core.Mining;

/// <summary>Editable search criteria; completed results are cached separately from in-flight requests.</summary>
public sealed record MiningSearchPreferences
{
    public string CommodityCategory { get; init; } = "Mining";
    public string Reference { get; init; } = "";
    public bool ForceIncludeReference { get; init; }
    public string Mineral { get; init; } = "Platinum";
    public string RingType { get; init; } = "All";
    public string Reserve { get; init; } = "All";
    public string Commodity { get; init; } = "Platinum";
    public IReadOnlyList<string> MarketCommodities { get; init; } = [];
    public string MarketPadSize { get; init; } = "Any";
    public long MarketMinimumVolume { get; init; }
    public long MarketMaximumVolume { get; init; }
    public int MarketMaximumAgeDays { get; init; } = 2;
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
    public long MaximumDemand { get; init; } = 90_000;
    public int ResultLimit { get; init; } = 30;
    public string PlatinumMode { get; init; } = "Spots++";
    public string StationType { get; init; } = "";
    public IReadOnlyList<string> MarketStationTypes { get; init; } = [];
    public string Security { get; init; } = "";
    public string Allegiance { get; init; } = "";
    public string Government { get; init; } = "";
    public string Economy { get; init; } = "";
    public string State { get; init; } = "";
    public string Power { get; init; } = "";
    public string OpposingPower { get; init; } = "";
    public string PowerState { get; init; } = "";
    public string MiningType { get; init; } = "All";
    public string AlsoMineral { get; init; } = "Any";
    public string PadSize { get; init; } = "Any";
    public bool LimitMarketAge { get; init; } = true;
    public int MarketAge { get; init; } = 48;
    public string MarketAgeUnit { get; init; } = "Hours";
    public long MinimumPopulation { get; init; }
    public string TraderType { get; init; } = "Raw";
    public string PledgedPower { get; init; } = "";
}
