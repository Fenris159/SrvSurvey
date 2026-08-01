using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class RouteBioOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private string platformStatus;
    private string inputMode;

    public RouteBioOverlayViewModel(
        RouteWorkspaceViewModel route,
        OverlayPlatformCapabilities capabilities)
    {
        Route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(capabilities);
        platformStatus = capabilities.StatusText;
        inputMode = capabilities.SupportsClickThrough
            ? "PASSIVE"
            : "UNAVAILABLE";
        Route.PropertyChanged += OnRoutePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RouteWorkspaceViewModel Route { get; }

    public string SystemName => Route.CurrentBioSystemName;

    public IReadOnlyList<RouteBioTargetItemViewModel> Targets =>
        Route.CurrentBioTargets;

    public int CompletedCount => Targets.Count(target => target.IsCompleted);

    public string Progress => $"{CompletedCount:N0} / {Targets.Count:N0} BODIES COMPLETE";

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

    public Task SetCompletedAsync(
        RouteBioTargetItemViewModel target,
        bool isCompleted)
    {
        return Route.SetBioTargetCompletedAsync(target, isCompleted);
    }

    public void ApplyPreparation(OverlayPreparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PlatformStatus = result.Status;
        InputMode = result.IsClickThrough ? "PASSIVE" : "BLOCKED";
    }

    public void Dispose()
    {
        Route.PropertyChanged -= OnRoutePropertyChanged;
    }

    private void OnRoutePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(RouteWorkspaceViewModel.CurrentBioHop)
            or nameof(RouteWorkspaceViewModel.CurrentBioTargets)
            or nameof(RouteWorkspaceViewModel.CurrentBioSystemName)
            or nameof(RouteWorkspaceViewModel.HasCurrentBioTargets))
        {
            Raise(nameof(SystemName));
            Raise(nameof(Targets));
            Raise(nameof(CompletedCount));
            Raise(nameof(Progress));
        }
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
        Raise(propertyName);
        return true;
    }

    private void Raise(string? propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
