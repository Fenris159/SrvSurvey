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
    public bool ShowPlanet => Icon.Length > 0;
    public bool ShowSpacer => Mineral.Length > 0 && Icon.Length == 0;
    public bool ShowRingType => RingTypeIcon.Length > 0;
    public bool ShowReserve => ReserveIcon.Length > 0;
    public bool ShowMineral => Mineral.Length > 0;
    public bool ShowPlainText => Mineral.Length == 0;
}

public sealed record MeritCommodityLineViewModel(string Code, string Price, string Demand);

public sealed record MeritStationBlockViewModel(
    string Icon,
    string Heading,
    string Commodity,
    string Price,
    string Demand,
    string Arrival,
    string Updated,
    IReadOnlyList<MeritCommodityLineViewModel> OtherCommodities
);

public sealed record AcquireQuoteViewModel(string Code, string Price, string Demand, bool Highlight);

public sealed record AcquireStationViewModel(
    string Name,
    string Pad,
    string Arrival,
    string Updated,
    IReadOnlyList<AcquireQuoteViewModel> Quotes
)
{
    public bool HasUpdated => Updated.Length > 0;
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

public sealed record AcquireMinerViewModel(
    string Name,
    string Rings,
    string State,
    string Power,
    string Connector = "Single"
);

public sealed record AcquireResultRowViewModel(
    string Target,
    string State,
    string Distance,
    IReadOnlyList<AcquireStationViewModel> Stations,
    IReadOnlyList<AcquireMinerViewModel> Miners
);

internal sealed record MeritRowContent(
    string Name,
    string Distance,
    double? DistanceLy,
    string StateIcon,
    string StateText,
    string Power,
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
        int commodityLimit = 6,
        IReadOnlyList<string>? focusMinerals = null
    )
    {
        HashSet<string> hotspotNames = HotspotNames(system);
        string bestCommodity =
            preferredCommodity.Length > 0
                ? preferredCommodity
                : system
                    .Stations.Where(station => hotspotNames.Contains(station.Commodity))
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
                rings,
                focusedRings,
                stations,
                best,
                BuildStationBlocks(system, bestCommodity, averageSellPrices, commodityLimit),
                system.FactionState
            )
        );
    }

    private static MeritLineViewModel[] RingLines(PowerplayMeritSystem system)
    {
        var lines = new List<MeritLineViewModel>();
        foreach (
            PowerplayMeritRing ring in system.Rings.OrderBy(ring => ring.Body, StringComparer.OrdinalIgnoreCase)
        )
        {
            string body = ring.Body.StartsWith(system.Name, StringComparison.OrdinalIgnoreCase)
                ? ring.Body[system.Name.Length..].Trim()
                : ring.Body;
            IReadOnlyList<string> signals = ring.SignalLines is { Count: > 0 } ? ring.SignalLines : [ring.Detail];
            for (int index = 0; index < signals.Count; index++)
            {
                string mineral = signals[index];
                string text = body.Length == 0 ? mineral : $"{body}: {mineral}";
                lines.Add(
                    new MeritLineViewModel(
                        ring.Planetary || index == 0 ? "Planet" : "",
                        text,
                        body,
                        RingTypeKind(ring.RingType),
                        ReserveKind(ring.Reserve),
                        ring.Planetary ? "" : mineral
                    )
                );
            }
        }

        return lines.ToArray();
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

    private static MeritStationBlockViewModel[] BuildStationBlocks(
        PowerplayMeritSystem system,
        string preferredCommodity,
        IReadOnlyDictionary<string, long>? averageSellPrices,
        int commodityLimit
    )
    {
        var blocks = new List<MeritStationBlockViewModel>();
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
                quotes.FirstOrDefault(station =>
                    station.Commodity.Equals(preferredCommodity, StringComparison.OrdinalIgnoreCase)
                ) ?? quotes[0];
            blocks.Add(
                new MeritStationBlockViewModel(
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
                        .Take(commodityLimit)
                        .Select(station => new MeritCommodityLineViewModel(
                            MiningCommodityCode.Abbreviate(station.Commodity),
                            station.Price.ToString("N0", CultureInfo.CurrentCulture)
                                + " CR "
                                + MiningPriceMarks.For(station.Price, Average(averageSellPrices, station.Commodity)),
                            station.Demand.ToString("N0", CultureInfo.CurrentCulture) + " Demand"
                        ))
                        .ToArray()
                )
            );
        }

        return blocks.ToArray();
    }

    private static long HeadlinePrice(IEnumerable<PowerplayMeritStation> quotes, string commodity) =>
        quotes
            .Where(station => station.Commodity.Equals(commodity, StringComparison.OrdinalIgnoreCase))
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
                    names.Add(name);
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
            _ => pad,
        };

    private static string UpdatedLabel(DateTimeOffset? updated)
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
