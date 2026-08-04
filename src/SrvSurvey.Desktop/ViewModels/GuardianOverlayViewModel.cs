using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuardianOverlayViewModel : INotifyPropertyChanged
{
    private string platformStatus;
    private bool isClickThrough;

    public GuardianOverlayViewModel(
        IGuardianOverlayPresentationState guardian,
        OverlayPlatformCapabilities capabilities)
    {
        Guardian = guardian ?? throw new ArgumentNullException(nameof(guardian));
        ArgumentNullException.ThrowIfNull(capabilities);
        platformStatus = capabilities.StatusText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IGuardianOverlayPresentationState Guardian { get; }

    internal static GuardianOverlayViewModel CreateEditorPreview()
    {
        var viewModel = new GuardianOverlayViewModel(
            GuardianOverlayPreviewState.Instance,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));
        viewModel.ApplyPreparation(new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: true,
            "EDITOR PREVIEW - REPRESENTATIVE GUARDIAN STATE"));
        return viewModel;
    }

    public string PlatformStatus
    {
        get => platformStatus;
        private set => SetField(ref platformStatus, value);
    }

    public bool IsClickThrough
    {
        get => isClickThrough;
        private set => SetField(ref isClickThrough, value);
    }

    public string InputMode => IsClickThrough
        ? "CLICK-THROUGH"
        : "PASS-THROUGH UNAVAILABLE";

    public void ApplyPreparation(OverlayPreparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PlatformStatus = result.Status;
        IsClickThrough = result.IsClickThrough;
        OnPropertyChanged(nameof(InputMode));
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
