using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SphericalSearchOverlayViewModel : INotifyPropertyChanged
{
    private string platformStatus;
    private string inputMode;

    public SphericalSearchOverlayViewModel(
        SphereLimitViewModel sphere,
        BoxelSearchViewModel boxel,
        RouteWorkspaceViewModel route,
        OverlayPlatformCapabilities capabilities)
    {
        Sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
        Boxel = boxel ?? throw new ArgumentNullException(nameof(boxel));
        Route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(capabilities);
        platformStatus = capabilities.StatusText;
        inputMode = capabilities.SupportsClickThrough
            ? "PASSIVE"
            : "UNAVAILABLE";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SphereLimitViewModel Sphere { get; }

    public BoxelSearchViewModel Boxel { get; }

    public RouteWorkspaceViewModel Route { get; }

    public string PlatformStatus
    {
        get => platformStatus;
        private set => SetField(ref platformStatus, value);
    }

    public string InputMode
    {
        get => inputMode;
        private set => SetField(ref inputMode, value);
    }

    public void ApplyPreparation(OverlayPreparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PlatformStatus = result.Status;
        InputMode = result.IsClickThrough ? "PASSIVE" : "BLOCKED";
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
