using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Holds a selected deposit's editable details until the user saves them.</summary>
public sealed class MineMapMarkerEditorViewModel : WorkspaceObservable
{
    private readonly MineMapMarker original;
    private string material;
    private MineMapRating mineralAmount;
    private MineMapRating density;
    private decimal rigCount;
    private decimal latitude;
    private decimal longitude;

    /// <summary>Copies the saved marker into a draft without changing the map.</summary>
    public MineMapMarkerEditorViewModel(MineMapMarker marker)
    {
        original = marker;
        material = marker.Material;
        mineralAmount = marker.MineralAmount;
        density = marker.Density;
        rigCount = marker.RigCount ?? 0;
        latitude = (decimal)marker.Location.Latitude;
        longitude = (decimal)marker.Location.Longitude;
    }

    public Guid Id => original.Id;

    public static IReadOnlyList<string> Materials { get; } =
        SurfaceMiningCommodityCatalog
            .All.Select(commodity => commodity.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<MineMapRating> Ratings { get; } = Enum.GetValues<MineMapRating>();

    public string Material
    {
        get => material;
        set => Set(ref material, value);
    }

    public MineMapRating MineralAmount
    {
        get => mineralAmount;
        set => Set(ref mineralAmount, value);
    }

    public MineMapRating Density
    {
        get => density;
        set => Set(ref density, value);
    }

    public decimal RigCount
    {
        get => rigCount;
        set => Set(ref rigCount, Math.Clamp(decimal.Truncate(value), 0, int.MaxValue));
    }

    public decimal Latitude
    {
        get => latitude;
        set => Set(ref latitude, Math.Clamp(value, -90, 90));
    }

    public decimal Longitude
    {
        get => longitude;
        set => Set(ref longitude, Math.Clamp(value, -180, 180));
    }

    /// <summary>Builds a marker edit; a zero rig count represents an unrecorded capacity.</summary>
    public MineMapMarker CreateMarker() =>
        original with
        {
            Material = Material,
            MineralAmount = MineralAmount,
            Density = Density,
            RigCount = RigCount == 0 ? null : (int)RigCount,
            Location = new SurfaceCoordinate((double)Latitude, (double)Longitude),
        };
}
