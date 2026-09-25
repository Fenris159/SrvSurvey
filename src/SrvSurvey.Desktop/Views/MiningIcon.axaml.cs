using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningIcon : UserControl
{
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<MiningIcon, string?>(
        nameof(Kind)
    );

    public static readonly StyledProperty<string?> AccentProperty = AvaloniaProperty.Register<MiningIcon, string?>(
        nameof(Accent)
    );

    private static readonly Dictionary<string, string> PowerStates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Stronghold"] = "M20,0 L25.61,14.39 40,20 25.61,25.61 20,40 14.39,25.61 0,20 14.39,14.39 Z",
        ["Fortified"] = "M0,0 L20,34.26 40,0 Z",
        ["Exploited"] = "M0,0 L40,0 20,34.26 Z",
        ["Unoccupied"] =
            "M17.5,35 C7.85,35 0,27.15 0,17.5 S7.85,0 17.5,0 35,7.85 35,17.5 27.15,35 17.5,35 Z M17.5,3.37 C9.71,3.37 3.37,9.71 3.37,17.5 S9.71,31.63 17.5,31.63 31.63,25.29 31.63,17.5 25.29,3.37 17.5,3.37 Z",
        ["Expansion"] =
            "M17.5,6.62c6,0,10.88,4.88,10.88,10.88s-4.88,10.88-10.88,10.88-10.88-4.88-10.88-10.88,4.88-10.88,10.88-10.88M17.5,4.94c-6.94,0-12.56,5.62-12.56,12.56s5.62,12.56,12.56,12.56,12.56-5.62,12.56-12.56-5.62-12.56-12.56-12.56z M17.5,35C7.85,35,0,27.15,0,17.5S7.85,0,17.5,0s17.5,7.85,17.5,17.5-7.85,17.5-17.5,17.5ZM17.5,3.37c-7.79,0-14.13,6.34-14.13,14.13s6.34,14.13,14.13,14.13,14.13-6.34,14.13-14.13S25.29,3.37,17.5,3.37Z",
        ["Contested"] =
            "M11.5,19.2 C6.38,19.2 2.21,23.37 2.21,28.49 S6.38,37.78 11.5,37.78 20.79,33.61 20.79,28.49 16.62,19.2 11.5,19.2 Z M19.28,14.17 L16.29,11.18 17.92,9.55 16.56,8.19 15.2,9.55 6.23,0.57 C5.8,0.15 3.98,0.02 3.98,0.02 C3.98,0.02 4.11,1.84 4.54,2.27 L13.96,11.69 12.6,13.05 13.96,14.41 15.59,12.78 18.58,15.77 Z",
    };

    private static readonly Dictionary<string, string> Pictures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Planet"] = "ringed-planet-2.png",
        ["Coriolis"] = "Coriolis_sm.png",
        ["Orbis"] = "Orbis_sm.png",
        ["Ocellus"] = "Ocellus_sm.png",
        ["Asteroid"] = "Asteroid_Station.png",
        ["SurfacePort"] = "surface_port_sm.png",
        ["Settlement"] = "settlement_sm.png",
        ["Outpost"] = "Outpost_sm.png",
    };

    public MiningIcon()
    {
        InitializeComponent();
        KindProperty.Changed.AddClassHandler<MiningIcon>((icon, _) => icon.Apply());
        AccentProperty.Changed.AddClassHandler<MiningIcon>((icon, _) => icon.Apply());
    }

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private void Apply()
    {
        string kind = Kind ?? "";
        if (PowerStates.TryGetValue(kind, out string? geometry))
        {
            Shape.Data = StreamGeometry.Parse(geometry);
            Shape.Fill = BrushFor(kind, Accent);
            Shape.IsVisible = true;
            Picture.IsVisible = false;
            return;
        }

        if (Pictures.TryGetValue(kind, out string? file))
        {
            Picture.Source = new Bitmap(AssetLoader.Open(new Uri($"avares://SrvSurvey.Desktop/Assets/Mining/{file}")));
            Picture.IsVisible = true;
            Shape.IsVisible = false;
            return;
        }

        Shape.IsVisible = false;
        Picture.IsVisible = false;
    }

    private static SolidColorBrush BrushFor(string state, string? power)
    {
        bool owned = state is "Stronghold" or "Fortified" or "Exploited";
        string hex =
            owned && power is not null && PowerColors.TryGetValue(power, out string? match) ? match : "#DDDDDD";
        return new SolidColorBrush(Color.Parse(hex));
    }

    private static readonly Dictionary<string, string> PowerColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aisling Duval"] = "#0099FF",
        ["Edmund Mahon"] = "#019C00",
        ["A. Lavigny-Duval"] = "#7F00FF",
        ["Arissa Lavigny-Duval"] = "#7F00FF",
        ["Nakato Kaine"] = "#A3F127",
        ["Felicia Winters"] = "#FFC400",
        ["Denton Patreus"] = "#00FFFF",
        ["Jerome Archer"] = "#DF1DE4",
        ["Zemina Torval"] = "#0040FF",
        ["Pranav Antal"] = "#FFFF00",
        ["Li Yong-Rui"] = "#33D688",
        ["Archon Delaine"] = "#FF0000",
        ["Yuri Grom"] = "#FF8000",
    };
}
