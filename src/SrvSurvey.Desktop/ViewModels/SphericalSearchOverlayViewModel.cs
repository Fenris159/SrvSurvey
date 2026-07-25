using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SphericalSearchOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SystemNicknameViewModel? systemNicknames;
    private string platformStatus;
    private string inputMode;

    public SphericalSearchOverlayViewModel(
        SphereLimitViewModel sphere,
        BoxelSearchViewModel boxel,
        RouteWorkspaceViewModel route,
        OverlayPlatformCapabilities capabilities,
        SystemNicknameViewModel? systemNicknames = null)
    {
        Sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
        Boxel = boxel ?? throw new ArgumentNullException(nameof(boxel));
        Route = route ?? throw new ArgumentNullException(nameof(route));
        this.systemNicknames = systemNicknames;
        ArgumentNullException.ThrowIfNull(capabilities);
        platformStatus = capabilities.StatusText;
        inputMode = capabilities.SupportsClickThrough
            ? "PASSIVE"
            : "UNAVAILABLE";
        Sphere.PropertyChanged += OnSourcePropertyChanged;
        Boxel.PropertyChanged += OnSourcePropertyChanged;
        Route.PropertyChanged += OnSourcePropertyChanged;
        if (systemNicknames is not null)
        {
            systemNicknames.NamesChanged += OnNamesChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SphereLimitViewModel Sphere { get; }

    public BoxelSearchViewModel Boxel { get; }

    public RouteWorkspaceViewModel Route { get; }

    public string SphereCenterSystemName =>
        Resolve(Sphere.CenterSystemName);

    public string SphereDestinationSystemName =>
        Resolve(Sphere.DestinationSystemName);

    public string BoxelNextSystem => Resolve(Boxel.NextSystem);

    public string RouteNextHopName => Resolve(Route.NextHopName);

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

    public void Dispose()
    {
        Sphere.PropertyChanged -= OnSourcePropertyChanged;
        Boxel.PropertyChanged -= OnSourcePropertyChanged;
        Route.PropertyChanged -= OnSourcePropertyChanged;
        if (systemNicknames is not null)
        {
            systemNicknames.NamesChanged -= OnNamesChanged;
        }
    }

    private string Resolve(string? value)
    {
        return systemNicknames?.Resolve(value) ?? value ?? string.Empty;
    }

    private void OnSourcePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, Sphere))
        {
            if (eventArgs.PropertyName == nameof(SphereLimitViewModel.CenterSystemName))
            {
                RaiseNameChanged(nameof(SphereCenterSystemName));
            }

            if (eventArgs.PropertyName
                == nameof(SphereLimitViewModel.DestinationSystemName))
            {
                RaiseNameChanged(nameof(SphereDestinationSystemName));
            }
        }
        else if (ReferenceEquals(sender, Boxel)
            && eventArgs.PropertyName == nameof(BoxelSearchViewModel.NextSystem))
        {
            RaiseNameChanged(nameof(BoxelNextSystem));
        }
        else if (ReferenceEquals(sender, Route)
            && eventArgs.PropertyName == nameof(RouteWorkspaceViewModel.NextHopName))
        {
            RaiseNameChanged(nameof(RouteNextHopName));
        }
    }

    private void OnNamesChanged(object? sender, EventArgs eventArgs)
    {
        RaiseNameChanged(nameof(SphereCenterSystemName));
        RaiseNameChanged(nameof(SphereDestinationSystemName));
        RaiseNameChanged(nameof(BoxelNextSystem));
        RaiseNameChanged(nameof(RouteNextHopName));
    }

    private void RaiseNameChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
