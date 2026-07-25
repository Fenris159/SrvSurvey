using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SurfaceSurveyOverlayViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private string platformStatus;
    private string inputMode;
    private bool disposed;

    public SurfaceSurveyOverlayViewModel(
        SurfaceSurveyViewModel surfaceSurvey,
        OverlayPlatformCapabilities capabilities)
    {
        SurfaceSurvey = surfaceSurvey
            ?? throw new ArgumentNullException(nameof(surfaceSurvey));
        ArgumentNullException.ThrowIfNull(capabilities);
        platformStatus = capabilities.StatusText;
        inputMode = capabilities.SupportsClickThrough
            ? "PASSIVE"
            : "UNAVAILABLE";
        SurfaceSurvey.PropertyChanged += OnSurfaceSurveyPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SurfaceSurveyViewModel SurfaceSurvey { get; }

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

    public double WindowWidth => GetWindowSize(
        SurfaceSurvey.RadarSize).Width;

    public double WindowHeight => GetWindowSize(
        SurfaceSurvey.RadarSize).Height;

    public void ApplyPreparation(OverlayPreparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PlatformStatus = result.Status;
        InputMode = result.IsClickThrough ? "PASSIVE" : "BLOCKED";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SurfaceSurvey.PropertyChanged -= OnSurfaceSurveyPropertyChanged;
    }

    private static (double Width, double Height) GetWindowSize(int radarSize)
    {
        return Math.Clamp(radarSize, 0, 4) switch
        {
            0 => (250, 400),
            1 => (250, 500),
            2 => (320, 440),
            3 => (380, 500),
            _ => (440, 600),
        };
    }

    private void OnSurfaceSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SurfaceSurveyViewModel.RadarSize))
        {
            OnPropertyChanged(nameof(WindowWidth));
            OnPropertyChanged(nameof(WindowHeight));
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
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
