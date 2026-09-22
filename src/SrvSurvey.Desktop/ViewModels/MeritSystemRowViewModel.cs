using System.Globalization;
using System.Windows.Input;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MeritLineViewModel(string Icon, string Text);

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
    private bool showAllSignals;

    private MeritSystemRowViewModel(MeritRowContent content)
    {
        Name = content.Name;
        Distance = content.Distance;
        DistanceLy = content.DistanceLy;
        StateIcon = content.StateIcon;
        StateText = content.StateText;
        Power = content.Power.Length == 0 ? "No controlling Power" : content.Power;
        FactionState = content.FactionState;
        StationBlocks = content.StationBlocks;
        allRings = content.AllRings;
        bestRings = content.BestRings;
        allStations = content.AllStations;
        bestStations = content.BestStations;
        ToggleSignalsCommand = new WorkspaceCommand(ToggleSignals);
    }

    public string Name { get; }
    public string Distance { get; }
    public double? DistanceLy { get; }
    public string StateIcon { get; }
    public string StateText { get; }
    public string Power { get; }
    public string FactionState { get; }
    public IReadOnlyList<MeritStationBlockViewModel> StationBlocks { get; }
    public IReadOnlyList<MeritLineViewModel> Rings => showAllSignals ? allRings : bestRings;
    public IReadOnlyList<MeritLineViewModel> Stations => showAllSignals ? allStations : bestStations;
    public bool CanToggleSignals => allStations.Count > bestStations.Count || allRings.Count > bestRings.Count;
    public string SignalToggleLabel => showAllSignals ? "Show less" : "Show all signals";
    public ICommand ToggleSignalsCommand { get; }

    public void ToggleSignals()
    {
        showAllSignals = !showAllSignals;
        Changed(nameof(Rings));
        Changed(nameof(Stations));
        Changed(nameof(SignalToggleLabel));
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
        string bestCommodity =
            preferredCommodity.Length > 0
                ? preferredCommodity
                : system
                    .Stations.OrderByDescending(station => station.Price)
                    .Select(station => station.Commodity)
                    .FirstOrDefault(commodity => commodity.Length > 0)
                    ?? "";
        MeritLineViewModel[] rings = RingLines(system);
        MeritLineViewModel[] focusedRings = FocusedRings(rings, revealEveryRing, focusMinerals, bestCommodity);

        MeritLineViewModel[] stations = system
            .Stations.GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
                group
                    .OrderByDescending(station => station.Price)
                    .Select(station => new MeritLineViewModel(StationIcon(station.Type), StationText(station)))
            )
            .ToArray();
        MeritLineViewModel[] best = system
            .Stations.GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                PowerplayMeritStation top = group.OrderByDescending(station => station.Price).First();
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
        bool planet = true;
        return system
            .Rings.SelectMany(ring =>
            {
                string icon = ring.Planetary || planet ? "Planet" : "";
                if (!ring.Planetary)
                {
                    planet = false;
                }

                string body = ring.Body.StartsWith(system.Name, StringComparison.OrdinalIgnoreCase)
                    ? ring.Body[system.Name.Length..].Trim()
                    : ring.Body;
                IReadOnlyList<string> signals = ring.SignalLines is { Count: > 0 } ? ring.SignalLines : [ring.Detail];
                return signals.Select(signal => new MeritLineViewModel(
                    icon,
                    body.Length == 0 ? signal : $"{body}: {signal}"
                ));
            })
            .ToArray();
    }

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
    ) =>
        system
            .Stations.GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                PowerplayMeritStation[] quotes = group.OrderByDescending(station => station.Price).ToArray();
                PowerplayMeritStation primary =
                    quotes.FirstOrDefault(station =>
                        station.Commodity.Equals(preferredCommodity, StringComparison.OrdinalIgnoreCase)
                    ) ?? quotes[0];
                return new MeritStationBlockViewModel(
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
                );
            })
            .ToArray();

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
