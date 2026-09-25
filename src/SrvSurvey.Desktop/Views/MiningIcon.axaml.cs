using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SrvSurvey.Desktop.ViewModels;

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
        ["RingIcy"] = "ring-icy.png",
        ["RingRocky"] = "ring-rocky.png",
        ["RingMetallic"] = "ring-metallic.png",
        ["RingMetalRich"] = "ring-metal-rich.png",
        ["Coriolis"] = "Coriolis_sm.png",
        ["Orbis"] = "Orbis_sm.png",
        ["Ocellus"] = "Ocellus_sm.png",
        ["Asteroid"] = "Asteroid_Station.png",
        ["SurfacePort"] = "surface_port_sm.png",
        ["Settlement"] = "settlement_sm.png",
        ["Outpost"] = "Outpost_sm.png",
    };

    static MiningIcon()
    {
        KindProperty.Changed.AddClassHandler<MiningIcon>((icon, _) => icon.Apply());
        AccentProperty.Changed.AddClassHandler<MiningIcon>((icon, _) => icon.Apply());
    }

    public MiningIcon()
    {
        InitializeComponent();
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
        if (kind.Equals("Hotspot", StringComparison.OrdinalIgnoreCase))
        {
            Shape.Data = StreamGeometry.Parse("M7,1 A6,6 0 1 1 6.99,1 Z");
            Shape.Fill = new SolidColorBrush(Color.Parse("#F3DA87"));
            Shape.IsVisible = true;
            Picture.IsVisible = false;
            return;
        }

        if (ReserveMarks.TryGetValue(kind, out string? reserve))
        {
            Shape.Data = StreamGeometry.Parse(reserve);
            Shape.Fill = new SolidColorBrush(Color.Parse("#F5730D"));
            Shape.IsVisible = true;
            Picture.IsVisible = false;
            return;
        }

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
        string hex = owned && power is not null ? PowerplayPowerLineViewModel.ColorFor(power) : "#DDDDDD";
        return new SolidColorBrush(Color.Parse(hex));
    }

    private static readonly Dictionary<string, string> ReserveMarks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReservePristine"] =
            "M4.32.23l.79,2.43h2.56c.32,0,.46.41.2.6l-2.07,1.5.79,2.43c.1.31-.25.56-.51.37l-2.07-1.5-2."
            + "07,1.5c-.26.19-.61-.07-.51-.37l.79-2.43L.14,3.27c-.26-.19-.13-.6.2-.6h2.56s.79-2.43.79-2.4"
            + "3c.1-.31.53-.31.63,0Z M13.32.23l.79,2.43h2.56c.32,0,.46.41.2.6l-2.07,1.5.79,2.43c.1.31-.25"
            + ".56-.51.37l-2.07-1.5-2.07,1.5c-.26.19-.61-.07-.51-.37l.79-2.43-2.07-1.5c-.26-.19-.13-.6.2-"
            + ".6h2.56s.79-2.43.79-2.43c.1-.31.53-.31.63,0Z M4.32,8.57l.79,2.43h2.56c.32,0,.46.41.2.6l-2."
            + "07,1.5.79,2.43c.1.31-.25.56-.51.37l-2.07-1.5-2.07,1.5c-.26.19-.61-.07-.51-.37l.79-2.43L.14"
            + ",11.6c-.26-.19-.13-.6.2-.6h2.56s.79-2.43.79-2.43c.1-.31.53-.31.63,0Z M13,8.36c-.13,0-.27.0"
            + "8-.32.23l-.79,2.43h-2.56c-.32,0-.46.41-.2.6l2.07,1.5-.79,2.43c-.08.23.11.44.32.44.07,0,.13"
            + "-.02.19-.07l2.07-1.5,2.07,1.5c.06.05.13.07.19.07.21,0,.39-.2.32-.44l-.79-2.43,2.07-1.5c.26"
            + "-.19.13-.6-.2-.6h-2.56l-.79-2.43c-.05-.15-.18-.23-.32-.23Z",
        ["ReserveMajor"] =
            "M4,0c-.13,0-.27.08-.32.23l-.79,2.43H.33c-.32,0-.46.41-.2.6l2.07,1.5-.79,2.43c-.08.23.11.44"
            + ".32.44.07,0,.13-.02.19-.07l2.07-1.5,2.07,1.5c.06.05.13.07.19.07.21,0,.39-.2.32-.44l-.79-2."
            + "43,2.07-1.5c.26-.19.13-.6-.2-.6h-2.56L4.32.23c-.05-.15-.18-.23-.32-.23Z M13,0c-.13,0-.27.0"
            + "8-.32.23l-.79,2.43h-2.56c-.32,0-.46.41-.2.6l2.07,1.5-.79,2.43c-.08.23.11.44.32.44.07,0,.13"
            + "-.02.19-.07l2.07-1.5,2.07,1.5c.06.05.13.07.19.07.21,0,.39-.2.32-.44l-.79-2.43,2.07-1.5c.26"
            + "-.19.13-.6-.2-.6h-2.56l-.79-2.43c-.05-.15-.18-.23-.32-.23Z M4,8.36c-.13,0-.27.08-.32.23l-."
            + "79,2.43H.33c-.32,0-.46.41-.2.6l2.07,1.5-.79,2.43c-.08.23.11.44.32.44.07,0,.13-.02.19-.07l2"
            + ".07-1.5,2.07,1.5c.06.05.13.07.19.07.21,0,.39-.2.32-.44l-.79-2.43,2.07-1.5c.26-.19.13-.6-.2"
            + "-.6h-2.56l-.79-2.43c-.05-.15-.18-.23-.32-.23Z M13,9.43l.57,1.76.13.39h2.26l-1.5,1.09-.33.2"
            + "4.13.39.57,1.76-1.5-1.09-.33-.24-.33.24-1.5,1.09.57-1.76.13-.39-.33-.24-1.5-1.09h2.26l.13-"
            + ".39.57-1.76Z",
        ["ReserveCommon"] =
            "M4,0c-.13,0-.27.08-.32.23l-.79,2.43H.33c-.32,0-.46.41-.2.6l2.07,1.5-.79,2.43c-.08.23.11.44"
            + ".32.44.07,0,.13-.02.19-.07l2.07-1.5,2.07,1.5c.06.05.13.07.19.07.21,0,.39-.2.32-.44l-.79-2."
            + "43,2.07-1.5c.26-.19.13-.6-.2-.6h-2.56L4.32.23c-.05-.15-.18-.23-.32-.23Z M13,0c-.13,0-.27.0"
            + "8-.32.23l-.79,2.43h-2.56c-.32,0-.46.41-.2.6l2.07,1.5-.79,2.43c-.08.23.11.44.32.44.07,0,.13"
            + "-.02.19-.07l2.07-1.5,2.07,1.5c.06.05.13.07.19.07.21,0,.39-.2.32-.44l-.79-2.43,2.07-1.5c.26"
            + "-.19.13-.6-.2-.6h-2.56l-.79-2.43c-.05-.15-.18-.23-.32-.23Z M4,9.43l.57,1.76.13.39h2.26l-1."
            + "5,1.09-.33.24.13.39.57,1.76-1.5-1.09-.33-.24-.33.24-1.5,1.09.57-1.76.13-.39-.33-.24-1.5-1."
            + "09h2.26l.13-.39.57-1.76Z M13,9.43l.57,1.76.13.39h2.26l-1.5,1.09-.33.24.13.39.57,1.76-1.5-1"
            + ".09-.33-.24-.33.24-1.5,1.09.57-1.76.13-.39-.33-.24-1.5-1.09h2.26l.13-.39.57-1.76Z",
        ["ReserveLow"] =
            "M4,0c-.13,0-.27.08-.32.23l-.79,2.43H.33c-.32,0-.46.41-.2.6l2.07,1.5-.79,2.43c-.08.23.11.44"
            + ".32.44.07,0,.13-.02.19-.07l2.07-1.5,2.07,1.5c.06.05.13.07.19.07.21,0,.39-.2.32-.44l-.79-2."
            + "43,2.07-1.5c.26-.19.13-.6-.2-.6h-2.56L4.32.23c-.05-.15-.18-.23-.32-.23Z M13,1.08l.57,1.76."
            + "13.39h2.26l-1.5,1.09-.33.24.13.39.57,1.76-1.5-1.09-.33-.24-.33.24-1.5,1.09.57-1.76.13-.39-"
            + ".33-.24-1.5-1.09h2.26l.13-.39.57-1.76Z M4,9.43l.57,1.76.13.39h2.26l-1.5,1.09-.33.24.13.39."
            + "57,1.76-1.5-1.09-.33-.24-.33.24-1.5,1.09.57-1.76.13-.39-.33-.24-1.5-1.09h2.26l.13-.39.57-1"
            + ".76Z M13,9.43l.57,1.76.13.39h2.26l-1.5,1.09-.33.24.13.39.57,1.76-1.5-1.09-.33-.24-.33.24-1"
            + ".5,1.09.57-1.76.13-.39-.33-.24-1.5-1.09h2.26l.13-.39.57-1.76Z",
        ["ReserveDepleted"] =
            "M4,1.08l.57,1.76.13.39h2.26l-1.5,1.09-.33.24.13.39.57,1.76-1.5-1.09-.33-.24-.33.24-1.5,1.0"
            + "9.57-1.76.13-.39-.33-.24-1.5-1.09h2.26l.13-.39.57-1.76Z M13,1.08l.57,1.76.13.39h2.26l-1.5,"
            + "1.09-.33.24.13.39.57,1.76-1.5-1.09-.33-.24-.33.24-1.5,1.09.57-1.76.13-.39-.33-.24-1.5-1.09"
            + "h2.26l.13-.39.57-1.76Z M4,9.43l.57,1.76.13.39h2.26l-1.5,1.09-.33.24.13.39.57,1.76-1.5-1.09"
            + "-.33-.24-.33.24-1.5,1.09.57-1.76.13-.39-.33-.24-1.5-1.09h2.26l.13-.39.57-1.76Z M13,9.43l.5"
            + "7,1.76.13.39h2.26l-1.5,1.09-.33.24.13.39.57,1.76-1.5-1.09-.33-.24-.33.24-1.5,1.09.57-1.76."
            + "13-.39-.33-.24-1.5-1.09h2.26l.13-.39.57-1.76Z",
        ["ReserveUnknown"] =
            "M8,0C3.58,0,0,3.58,0,8s3.58,8,8,8,8-3.58,8-8S12.42,0,8,0ZM8.92,12.68l-.64.64h-1.02l-.64-.6"
            + "4v-.9l.64-.65h1.02l.64.65v.9ZM11.48,6.61l-2.73,2.64v1.21h-1.98v-1.84l2.7-2.61v-.67l-.77-.7"
            + "6h-1.96l-.71.71v.85h-2.01v-1.47l1.83-1.83h3.74l1.89,1.89v1.89Z",
    };
}
