using System.Globalization;
using System.Windows.Input;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MeritLineViewModel(
    string Icon,
    string Text,
    string RingName = "",
    string RingTypeIcon = "",
    string ReserveIcon = "",
    string Mineral = ""
)
{
    public string CommodityCode { get; init; } = "";

    // Older saved searches predate the typed code; keep their badges usable until the next search.
    public string EffectiveCommodityCode =>
        CommodityCode.Length > 0 ? CommodityCode : MiningCommodityCode.Abbreviate(Mineral.Split(':')[0]);
    public bool ShowPlanet => Icon.Length > 0;
    public bool ShowSpacer => Mineral.Length > 0 && Icon.Length == 0;
    public bool ShowRingType => RingTypeIcon.Length > 0;
    public bool ShowReserve => ReserveIcon.Length > 0;
    public bool ShowMineral => Mineral.Length > 0;
    public bool ShowPlainText => Mineral.Length == 0;
}

public sealed record MeritCommodityLineViewModel(string Code, string Price, string Demand)
{
    public string ColorHex => SurfaceMaterialBadgePalette.ColorHexFor(Code);
    public string ContrastForeground => SurfaceMaterialBadgePalette.ForegroundFor(ColorHex);
}

public sealed class MeritStationBlockViewModel : WorkspaceObservable
{
    private bool showAll;
    private readonly MeritCommodityLineViewModel[] allCommodities;
    private readonly IReadOnlyList<MeritCommodityLineViewModel> previewCommodities;

    public MeritStationBlockViewModel(MeritStationBlockSnapshot snapshot)
    {
        Icon = snapshot.Icon;
        Heading = snapshot.Heading;
        Commodity = snapshot.Commodity;
        Price = snapshot.Price;
        Demand = snapshot.Demand;
        Arrival = snapshot.Arrival;
        UpdatedAt = snapshot.UpdatedAt;
        cachedUpdated = snapshot.Updated;
        allCommodities = snapshot.Commodities;
        PrimaryQuote =
            snapshot.PrimaryQuote
            ?? new MeritCommodityLineViewModel(
                MiningCommodityCode.Abbreviate(snapshot.Commodity),
                snapshot.Price.Replace("Price: ", "", StringComparison.Ordinal),
                snapshot.Demand.Replace("Demand: ", "", StringComparison.Ordinal) + " Demand"
            );
        previewCommodities = snapshot.Commodities.Take(6).ToArray();
        ToggleCommoditiesCommand = new WorkspaceCommand(() =>
        {
            showAll = !showAll;
            Changed(nameof(OtherCommodities));
            Changed(nameof(CommodityToggleLabel));
        });
    }

    public string Icon { get; }
    public string Heading { get; }
    public string Commodity { get; }
    public string Price { get; }
    public string Demand { get; }
    public string Arrival { get; }
    private readonly string cachedUpdated;
    public DateTimeOffset? UpdatedAt { get; }
    public string Updated => UpdatedAt is { } updated ? MeritSystemRowViewModel.UpdatedLabel(updated) : cachedUpdated;
    public IReadOnlyList<MeritCommodityLineViewModel> OtherCommodities => showAll ? allCommodities : previewCommodities;
    public IReadOnlyList<MeritCommodityLineViewModel> AllCommodities => allCommodities;
    public MeritCommodityLineViewModel PrimaryQuote { get; }
    public bool CanToggleCommodities => allCommodities.Length > 6;
    public string CommodityToggleLabel => showAll ? "Show Less" : "Show All";
    public ICommand ToggleCommoditiesCommand { get; }
}

public sealed record AcquireQuoteViewModel(
    string Code,
    string Price,
    string Demand,
    bool IsUnavailable = false,
    bool UseMaterialColor = false
)
{
    public string ColorHex => SurfaceMaterialBadgePalette.ColorHexFor(Code);
    public string ContrastForeground => SurfaceMaterialBadgePalette.ForegroundFor(ColorHex);
}

public sealed record AcquireStationViewModel(
    string Name,
    string Pad,
    string Arrival,
    string Updated,
    IReadOnlyList<AcquireQuoteViewModel> Quotes
)
{
    public bool CanToggle { get; init; }

    public bool HasUpdated => Updated.Length > 0;

    public string PadBadge => Pad == "Small / medium pads" ? "S/M" : Pad;
}

public static class AcquireConnector
{
    public static string ForIndex(int index, int count)
    {
        if (count <= 1)
        {
            return "Single";
        }

        if (index <= 0)
        {
            return "First";
        }

        return index >= count - 1 ? "Last" : "Next";
    }
}

public sealed class AcquireMinerViewModel : WorkspaceObservable
{
    private bool showAllSignals;
    private readonly IReadOnlyList<MeritLineViewModel> previewRingLines;

    public AcquireMinerViewModel(
        string name,
        IReadOnlyList<MeritLineViewModel> ringLines,
        string state,
        IReadOnlyList<PowerplayPowerLineViewModel> powerLines,
        string connector = "Single"
    )
    {
        Name = name;
        AllRingLines = ringLines;
        previewRingLines = ringLines.Take(1).ToArray();
        State = state;
        PowerLines = powerLines;
        Connector = connector;
        ToggleSignalsCommand = new WorkspaceCommand(() =>
        {
            showAllSignals = !showAllSignals;
            Changed(nameof(RingLines));
            Changed(nameof(SignalToggleLabel));
        });
    }

    public string Name { get; }
    public IReadOnlyList<MeritLineViewModel> AllRingLines { get; }
    public IReadOnlyList<MeritLineViewModel> RingLines => showAllSignals ? AllRingLines : previewRingLines;
    public bool CanToggleSignals => AllRingLines.Count > 1;
    public string SignalToggleLabel => showAllSignals ? "Show less" : "Show all signals";
    public ICommand ToggleSignalsCommand { get; }
    public string State { get; }
    public IReadOnlyList<PowerplayPowerLineViewModel> PowerLines { get; }
    public string Connector { get; }
}

public sealed record AcquireResultMetadata(
    string FactionState,
    IReadOnlyList<PowerplayPowerLineViewModel> PowerLines,
    double? DistanceLy,
    IReadOnlyList<long> StationScores
);

public sealed class AcquireResultRowViewModel : WorkspaceObservable
{
    private bool showAllStations;
    private readonly IReadOnlyList<MeritStationBlockViewModel> allStationBlocks;
    private readonly IReadOnlyList<MeritStationBlockViewModel> previewStationBlocks;

    public AcquireResultRowViewModel(
        string target,
        string state,
        string distance,
        IReadOnlyList<MeritStationBlockViewModel> stations,
        IReadOnlyList<AcquireMinerViewModel> miners,
        AcquireResultMetadata? metadata = null
    )
    {
        Target = target;
        State = state;
        Distance = distance;
        FactionState = metadata?.FactionState ?? "";
        PowerLines = metadata?.PowerLines ?? [];
        DistanceLy = metadata?.DistanceLy;
        StationScores = metadata?.StationScores ?? [];
        StationRanking = PowerplayStationRanking.FromScores(StationScores);
        allStationBlocks = stations;
        previewStationBlocks = stations.Take(1).ToArray();
        Miners = miners;
        ToggleStationsCommand = new WorkspaceCommand(() =>
        {
            showAllStations = !showAllStations;
            Changed(nameof(StationBlocks));
            Changed(nameof(StationToggleLabel));
        });
    }

    public string Target { get; }
    public string State { get; }
    public string Distance { get; }
    public string FactionState { get; }
    public IReadOnlyList<PowerplayPowerLineViewModel> PowerLines { get; }
    public double? DistanceLy { get; }
    public IReadOnlyList<long> StationScores { get; }
    internal PowerplayStationRanking StationRanking { get; }
    public IReadOnlyList<MeritStationBlockViewModel> StationBlocks =>
        showAllStations ? allStationBlocks : previewStationBlocks;
    public IReadOnlyList<MeritStationBlockViewModel> AllStationBlocks => allStationBlocks;
    public IReadOnlyList<AcquireMinerViewModel> Miners { get; }
    public bool CanToggleStations => allStationBlocks.Count > 1;
    public string StationToggleLabel => showAllStations ? "Show less" : "Show all stations";
    public ICommand ToggleStationsCommand { get; }
}

internal sealed record MeritRowContent(
    string Name,
    string Distance,
    double? DistanceLy,
    string StateIcon,
    string StateText,
    string Power,
    IReadOnlyList<PowerplayPowerLineViewModel> PowerLines,
    IReadOnlyList<MeritLineViewModel> AllRings,
    IReadOnlyList<MeritLineViewModel> BestRings,
    IReadOnlyList<MeritLineViewModel> AllStations,
    IReadOnlyList<MeritLineViewModel> BestStations,
    IReadOnlyList<MeritStationBlockViewModel> StationBlocks,
    string FactionState
);

public sealed class MeritSystemRowViewModel : WorkspaceObservable
{
    private readonly IReadOnlyList<MeritLineViewModel> allRings;
    private readonly IReadOnlyList<MeritLineViewModel> bestRings;
    private readonly IReadOnlyList<MeritLineViewModel> allStations;
    private readonly IReadOnlyList<MeritLineViewModel> bestStations;
    private readonly IReadOnlyList<MeritStationBlockViewModel> allStationBlocks;
    private readonly IReadOnlyList<MeritStationBlockViewModel> bestStationBlocks;
    private bool showAllSignals;
    private bool showAllStations;

    private MeritSystemRowViewModel(MeritRowContent content)
    {
        Name = content.Name;
        Distance = content.Distance;
        DistanceLy = content.DistanceLy;
        StateIcon = content.StateIcon;
        StateText = content.StateText;
        Power = content.Power.Length == 0 ? "No controlling Power" : content.Power;
        PowerLines = content.PowerLines;
        FactionState = content.FactionState;
        allStationBlocks = content.StationBlocks;
        bestStationBlocks = allStationBlocks.Count <= 1 ? allStationBlocks : [allStationBlocks[0]];
        allRings = content.AllRings;
        bestRings = content.BestRings;
        allStations = content.AllStations;
        bestStations = content.BestStations;
        ToggleSignalsCommand = new WorkspaceCommand(ToggleSignals);
        ToggleStationsCommand = new WorkspaceCommand(ToggleStations);
    }

    public string Name { get; }
    public string Distance { get; }
    public double? DistanceLy { get; }
    public string StateIcon { get; }
    public string StateText { get; }
    public string Power { get; }
    public IReadOnlyList<PowerplayPowerLineViewModel> PowerLines { get; }
    public string FactionState { get; }
    public IReadOnlyList<MeritStationBlockViewModel> StationBlocks =>
        showAllStations ? allStationBlocks : bestStationBlocks;
    public IReadOnlyList<MeritLineViewModel> Rings => showAllSignals ? allRings : bestRings;
    public IReadOnlyList<MeritLineViewModel> Stations => showAllSignals ? allStations : bestStations;
    public bool CanToggleSignals => allRings.Count > bestRings.Count;
    public bool CanToggleStations => allStationBlocks.Count > 1;
    public string SignalToggleLabel => showAllSignals ? "Show less" : "Show all signals";
    public string StationToggleLabel => showAllStations ? "Show less" : "Show all stations";
    public ICommand ToggleSignalsCommand { get; }
    public ICommand ToggleStationsCommand { get; }

    public MeritSystemSnapshot ExportSnapshot() =>
        new(
            Name,
            Distance,
            DistanceLy,
            StateIcon,
            StateText,
            Power,
            PowerLines.ToArray(),
            allRings.ToArray(),
            bestRings.ToArray(),
            allStations.ToArray(),
            bestStations.ToArray(),
            allStationBlocks.Select(MeritStationBlockSnapshot.From).ToArray(),
            FactionState
        );

    public static MeritSystemRowViewModel RestoreSnapshot(MeritSystemSnapshot snapshot) =>
        new(
            new MeritRowContent(
                snapshot.Name,
                snapshot.Distance,
                snapshot.DistanceLy,
                snapshot.StateIcon,
                snapshot.StateText,
                snapshot.Power,
                snapshot.PowerLines,
                snapshot.AllRings,
                snapshot.BestRings,
                snapshot.AllStations,
                snapshot.BestStations,
                snapshot.StationBlocks.Select(block => block.Restore()).ToArray(),
                snapshot.FactionState
            )
        );

    public void ToggleSignals()
    {
        showAllSignals = !showAllSignals;
        Changed(nameof(Rings));
        Changed(nameof(Stations));
        Changed(nameof(SignalToggleLabel));
    }

    public void ToggleStations()
    {
        showAllStations = !showAllStations;
        Changed(nameof(StationBlocks));
        Changed(nameof(StationToggleLabel));
    }

    public static MeritSystemRowViewModel From(
        PowerplayMeritSystem system,
        string preferredCommodity = "",
        IReadOnlyDictionary<string, long>? averageSellPrices = null,
        bool revealEveryRing = false,
        IReadOnlyList<string>? focusMinerals = null
    )
    {
        HashSet<string> hotspotNames = HotspotNames(system);
        string bestCommodity =
            preferredCommodity.Length > 0
                ? preferredCommodity
                : system
                    .Stations.Where(station => hotspotNames.Contains(MiningCommodityName.Key(station.Commodity)))
                    .OrderByDescending(SellScore)
                    .ThenByDescending(station => station.Price)
                    .Select(station => station.Commodity)
                    .FirstOrDefault(commodity => commodity.Length > 0)
                    ?? "";
        MeritLineViewModel[] rings = RingLines(system);
        MeritLineViewModel[] focusedRings = PrimaryRing(
            FocusedRings(rings, revealEveryRing, focusMinerals, bestCommodity)
        );

        MeritLineViewModel[] stations = system
            .Stations.GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
                group
                    .OrderByDescending(SellScore)
                    .Select(station => new MeritLineViewModel(StationIcon(station.Type), StationText(station)))
            )
            .ToArray();
        MeritLineViewModel[] best = system
            .Stations.GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                PowerplayMeritStation top = group.OrderByDescending(SellScore).First();
                return new MeritLineViewModel(StationIcon(top.Type), StationText(top));
            })
            .ToArray();
        return new(
            new MeritRowContent(
                system.Name,
                system.DistanceLy is { } distance ? $"{distance:N1} ly" : "",
                system.DistanceLy,
                system.PowerState,
                system.PowerState.Length == 0 ? "Unknown" : system.PowerState,
                system.NearbyPowers.Count > 0 ? string.Join("\n", system.NearbyPowers) : system.Power,
                PowerplayPowerLineViewModel.From(
                    system.NearbyPowers.Count > 0 ? system.NearbyPowers : [system.Power],
                    system.Conflict,
                    system.Power,
                    system.ControlProgress
                ),
                rings,
                focusedRings,
                stations,
                best,
                BuildStationBlocks(system, bestCommodity, averageSellPrices),
                system.FactionState
            )
        );
    }

    private static MeritLineViewModel[] RingLines(PowerplayMeritSystem system)
    {
        var lines = new List<MeritLineViewModel>();
        foreach (PowerplayMeritRing ring in system.Rings.OrderBy(ring => ring.Body, StringComparer.OrdinalIgnoreCase))
        {
            lines.AddRange(LinesForRing(system.Name, ring));
        }

        return lines.ToArray();
    }

    public static IEnumerable<MeritLineViewModel> LinesForRing(string systemName, PowerplayMeritRing ring)
    {
        string body = ring.Body.StartsWith(systemName, StringComparison.OrdinalIgnoreCase)
            ? ring.Body[systemName.Length..].Trim()
            : ring.Body;
        IReadOnlyList<string> signals = ring.SignalLines is { Count: > 0 } ? ring.SignalLines : [ring.Detail];
        for (int index = 0; index < signals.Count; index++)
        {
            string mineral = signals[index];
            string text = body.Length == 0 ? mineral : $"{body}: {mineral}";
            yield return new MeritLineViewModel(
                ring.Planetary || index == 0 ? "Planet" : "",
                text,
                body,
                RingTypeKind(ring.RingType),
                ReserveKind(ring.Reserve),
                ring.Planetary ? "" : mineral
            )
            {
                CommodityCode = MiningCommodityCode.Abbreviate(mineral.Split(':')[0]),
            };
        }
    }

    private static string RingTypeKind(string ringType)
    {
        string compact = ringType
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return compact switch
        {
            "icy" => "RingIcy",
            "rocky" => "RingRocky",
            "metallic" => "RingMetallic",
            "metalrich" => "RingMetalRich",
            _ => "",
        };
    }

    private static string ReserveKind(string reserve) =>
        reserve.ToLowerInvariant() switch
        {
            "pristine" => "ReservePristine",
            "major" => "ReserveMajor",
            "common" => "ReserveCommon",
            "low" => "ReserveLow",
            "depleted" => "ReserveDepleted",
            "" => "",
            _ => "ReserveUnknown",
        };

    private static MeritLineViewModel[] FocusedRings(
        MeritLineViewModel[] rings,
        bool revealEveryRing,
        IReadOnlyList<string>? focusMinerals,
        string bestCommodity
    )
    {
        if (revealEveryRing)
        {
            return rings;
        }

        string[] focus;
        if (focusMinerals is { Count: > 0 })
        {
            focus = focusMinerals.Where(name => name.Length > 0).ToArray();
        }
        else if (bestCommodity.Length == 0)
        {
            focus = [];
        }
        else
        {
            focus = [bestCommodity];
        }
        if (focus.Length == 0)
        {
            return rings;
        }

        MeritLineViewModel[] focused = rings
            .Where(ring => focus.Any(name => ring.Text.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return focused.Length == 0 ? rings : focused;
    }

    public static MeritStationBlockViewModel[] BuildStationBlocks(
        PowerplayMeritSystem system,
        string preferredCommodity,
        IReadOnlyDictionary<string, long>? averageSellPrices
    )
    {
        var blocks = new List<MeritStationBlockViewModel>();
        HashSet<string> hotspots = HotspotNames(system);
        foreach (
            IGrouping<string, PowerplayMeritStation> group in system
                .Stations.GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => HeadlinePrice(group, preferredCommodity))
                .ThenByDescending(group => group.Max(SellScore))
        )
        {
            bool planetary = system.Rings.Any(ring => ring.Planetary);
            PowerplayMeritStation[] quotes = group
                .Where(station => planetary || PlanetaryMiningPlan.IsEdpmCommodity(station.Commodity))
                .OrderByDescending(SellScore)
                .ToArray();
            if (quotes.Length == 0)
            {
                continue;
            }

            PowerplayMeritStation primary =
                quotes.FirstOrDefault(station => MiningCommodityName.Same(station.Commodity, preferredCommodity))
                ?? quotes[0];
            blocks.Add(
                new MeritStationBlockViewModel(
                    new MeritStationBlockSnapshot(
                        StationIcon(primary.Type),
                        primary.Name + " (" + PadLetter(primary.Pad) + ")",
                        primary.Commodity,
                        "Price: "
                            + primary.Price.ToString("N0", CultureInfo.CurrentCulture)
                            + " CR "
                            + MiningPriceMarks.For(primary.Price, Average(averageSellPrices, primary.Commodity)),
                        "Demand: " + primary.Demand.ToString("N0", CultureInfo.CurrentCulture),
                        primary.ArrivalLs is { } arrival
                            ? "Distance: " + arrival.ToString("0", CultureInfo.CurrentCulture) + " Ls"
                            : "",
                        UpdatedLabel(primary.Updated),
                        quotes
                            .Where(station => !MiningCommodityName.Same(station.Commodity, primary.Commodity))
                            .OrderByDescending(station => hotspots.Contains(MiningCommodityName.Key(station.Commodity)))
                            .ThenByDescending(station => station.Price)
                            .Select(station => new MeritCommodityLineViewModel(
                                MiningCommodityCode.Abbreviate(station.Commodity),
                                station.Price.ToString("N0", CultureInfo.CurrentCulture)
                                    + " CR "
                                    + MiningPriceMarks.For(
                                        station.Price,
                                        Average(averageSellPrices, station.Commodity)
                                    ),
                                station.Demand.ToString("N0", CultureInfo.CurrentCulture) + " Demand"
                            ))
                            .ToArray()
                    )
                    {
                        UpdatedAt = primary.Updated,
                        PrimaryQuote = new MeritCommodityLineViewModel(
                            MiningCommodityCode.Abbreviate(primary.Commodity),
                            primary.Price.ToString("N0", CultureInfo.CurrentCulture)
                                + " CR "
                                + MiningPriceMarks.For(primary.Price, Average(averageSellPrices, primary.Commodity)),
                            primary.Demand.ToString("N0", CultureInfo.CurrentCulture) + " Demand"
                        ),
                    }
                )
            );
        }

        return blocks.ToArray();
    }

    private static long HeadlinePrice(IEnumerable<PowerplayMeritStation> quotes, string commodity) =>
        quotes
            .Where(station => MiningCommodityName.Same(station.Commodity, commodity))
            .Select(station => station.Price)
            .DefaultIfEmpty(0)
            .Max();

    private static HashSet<string> HotspotNames(PowerplayMeritSystem system)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PowerplayMeritRing ring in system.Rings)
        {
            foreach (string line in ring.SignalLines ?? [])
            {
                int split = line.IndexOf(':');
                string name = (split < 0 ? line : line[..split]).Trim();
                if (name.Length > 0 && !PlanetaryMiningPlan.IsSurfaceExclusive(name))
                {
                    names.Add(MiningCommodityName.Key(name));
                }
            }
        }

        return names;
    }

    private static MeritLineViewModel[] PrimaryRing(MeritLineViewModel[] focused) =>
        focused.Length == 0 ? focused : [focused[0]];

    private static long SellScore(PowerplayMeritStation station) => station.Price;

    private static long Average(IReadOnlyDictionary<string, long>? prices, string commodity) =>
        prices is not null && prices.TryGetValue(commodity, out long average) ? average : 0;

    private static string PadLetter(string pad) =>
        pad switch
        {
            "Large" or "L" => "L",
            "Medium" or "M" => "M",
            "Small" or "S" => "S",
            "Small / medium pads" => "S/M",
            _ => pad,
        };

    internal static string UpdatedLabel(DateTimeOffset? updated)
    {
        if (updated is null)
        {
            return "";
        }

        TimeSpan age = DateTimeOffset.UtcNow - updated.Value;
        if (age.TotalMinutes < 1)
        {
            return "Updated: just now";
        }

        if (age.TotalHours < 1)
        {
            return "Updated: " + (int)age.TotalMinutes + "m ago";
        }

        if (age.TotalDays < 1)
        {
            return "Updated: " + (int)age.TotalHours + "h " + age.Minutes + "m ago";
        }

        return "Updated: " + (int)age.TotalDays + "d ago";
    }

    private static string StationText(PowerplayMeritStation station)
    {
        string commodity = station.Commodity.Length == 0 ? "" : station.Commodity + "\n";
        return station.Name
            + " ("
            + station.Pad
            + ")\n"
            + commodity
            + "Price: "
            + station.Price.ToString("N0", CultureInfo.CurrentCulture)
            + " CR\nDemand: "
            + station.Demand.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static string StationIcon(string type)
    {
        if (type.Contains("Coriolis", StringComparison.OrdinalIgnoreCase))
        {
            return "Coriolis";
        }

        if (type.Contains("Orbis", StringComparison.OrdinalIgnoreCase))
        {
            return "Orbis";
        }

        if (type.Contains("Ocellus", StringComparison.OrdinalIgnoreCase))
        {
            return "Ocellus";
        }

        if (type.Contains("Asteroid", StringComparison.OrdinalIgnoreCase))
        {
            return "Asteroid";
        }

        if (type.Contains("Settlement", StringComparison.OrdinalIgnoreCase))
        {
            return "Settlement";
        }

        if (
            type.Contains("Surface", StringComparison.OrdinalIgnoreCase)
            || type.Contains("Planetary", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "SurfacePort";
        }

        return "Outpost";
    }
}
