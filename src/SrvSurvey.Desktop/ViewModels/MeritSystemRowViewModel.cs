using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MeritLineViewModel(string Icon, string Text);

public sealed class MeritSystemRowViewModel
{
    private MeritSystemRowViewModel(
        string name,
        string distance,
        double? distanceLy,
        string stateIcon,
        string stateText,
        string power,
        IReadOnlyList<MeritLineViewModel> rings,
        IReadOnlyList<MeritLineViewModel> stations
    )
    {
        Name = name;
        Distance = distance;
        DistanceLy = distanceLy;
        StateIcon = stateIcon;
        StateText = stateText;
        Power = power.Length == 0 ? "No controlling Power" : power;
        Rings = rings;
        Stations = stations;
    }

    public string Name { get; }
    public string Distance { get; }
    public double? DistanceLy { get; }
    public string StateIcon { get; }
    public string StateText { get; }
    public string Power { get; }
    public IReadOnlyList<MeritLineViewModel> Rings { get; }
    public IReadOnlyList<MeritLineViewModel> Stations { get; }

    public static MeritSystemRowViewModel From(PowerplayMeritSystem system)
    {
        bool planet = true;
        MeritLineViewModel[] rings = system
            .Rings.Select(ring =>
            {
                string icon = ring.Planetary || planet ? "Planet" : "";
                if (!ring.Planetary)
                {
                    planet = false;
                }
                return new MeritLineViewModel(icon, $"{ring.Body}: {ring.Detail}");
            })
            .ToArray();
        MeritLineViewModel[] stations = system
            .Stations.Select(station => new MeritLineViewModel(
                StationIcon(station.Type),
                $"{station.Name} · {station.Detail}"
            ))
            .ToArray();
        return new(
            system.Name,
            system.DistanceLy is { } distance ? $"{distance:N1} ly" : "",
            system.DistanceLy,
            system.PowerState,
            system.PowerState.Length == 0 ? "Unknown" : system.PowerState,
            system.Power,
            rings,
            stations
        );
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
