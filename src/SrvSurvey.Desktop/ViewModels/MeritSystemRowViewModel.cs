using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MeritLineViewModel(string Icon, string Text);

public sealed class MeritSystemRowViewModel
{
    private MeritSystemRowViewModel(
        string name,
        string distance,
        string stateIcon,
        string stateText,
        string power,
        IReadOnlyList<MeritLineViewModel> rings,
        IReadOnlyList<MeritLineViewModel> stations
    )
    {
        Name = name;
        Distance = distance;
        StateIcon = stateIcon;
        StateText = stateText;
        Power = power.Length == 0 ? "No controlling Power" : power;
        Rings = rings;
        Stations = stations;
    }

    public string Name { get; }
    public string Distance { get; }
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
                string icon = planet ? "Planet" : "";
                planet = false;
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
