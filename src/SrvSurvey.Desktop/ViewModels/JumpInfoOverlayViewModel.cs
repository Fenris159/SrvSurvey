using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JumpInfoOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SystemNicknameViewModel? systemNicknames;
    private string platformStatus;
    private string inputMode;

    public JumpInfoOverlayViewModel(
        JumpInfoViewModel jumpInfo,
        OverlayPlatformCapabilities capabilities,
        SystemNicknameViewModel? systemNicknames = null)
    {
        JumpInfo = jumpInfo ?? throw new ArgumentNullException(nameof(jumpInfo));
        this.systemNicknames = systemNicknames;
        ArgumentNullException.ThrowIfNull(capabilities);
        platformStatus = capabilities.StatusText;
        inputMode = capabilities.SupportsClickThrough
            ? "PASSIVE"
            : "UNAVAILABLE";
        JumpInfo.PropertyChanged += OnJumpInfoPropertyChanged;
        if (systemNicknames is not null)
        {
            systemNicknames.NamesChanged += OnNamesChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public JumpInfoViewModel JumpInfo { get; }

    public string TargetName => systemNicknames?.Resolve(JumpInfo.TargetName)
        ?? JumpInfo.TargetName;

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
        JumpInfo.PropertyChanged -= OnJumpInfoPropertyChanged;
        if (systemNicknames is not null)
        {
            systemNicknames.NamesChanged -= OnNamesChanged;
        }
    }

    private void OnJumpInfoPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(JumpInfoViewModel.TargetName))
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(TargetName)));
        }
    }

    private void OnNamesChanged(object? sender, EventArgs eventArgs)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(TargetName)));
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
