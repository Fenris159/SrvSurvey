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

    /// <summary>The persistent identity of the deposit being edited.</summary>
    public Guid Id => original.Id;

    /// <summary>Recognized surface-mining minerals and metals, ordered for the editor's selector.</summary>
    public static IReadOnlyList<string> Materials { get; } =
        SurfaceMiningCommodityCatalog
            .All.Select(commodity => commodity.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>The supported low, medium, and high deposit ratings.</summary>
    public static IReadOnlyList<MineMapRating> Ratings { get; } = Enum.GetValues<MineMapRating>();

    /// <summary>The mineral or metal selected in this unsaved draft.</summary>
    public string Material
    {
        get => material;
        set => Set(ref material, value);
    }

    /// <summary>The draft's mineral-amount rating.</summary>
    public MineMapRating MineralAmount
    {
        get => mineralAmount;
        set => Set(ref mineralAmount, value);
    }

    /// <summary>The draft's mineral-density rating.</summary>
    public MineMapRating Density
    {
        get => density;
        set => Set(ref density, value);
    }

    /// <summary>A nonnegative whole-number rig capacity; zero clears the recorded capacity on save.</summary>
    public decimal RigCount
    {
        get => rigCount;
        set => Set(ref rigCount, Math.Clamp(decimal.Truncate(value), 0, int.MaxValue));
    }

    /// <summary>The draft's latitude in degrees, constrained to the valid surface-coordinate range.</summary>
    public decimal Latitude
    {
        get => latitude;
        set => Set(ref latitude, Math.Clamp(value, -90, 90));
    }

    /// <summary>The draft's longitude in degrees, constrained to the valid surface-coordinate range.</summary>
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
