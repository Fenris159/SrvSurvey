using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class VrCalibrationView : UserControl
{
    public static readonly StyledProperty<bool> IsInHeadsetProperty = AvaloniaProperty.Register<
        VrCalibrationView,
        bool
    >(nameof(IsInHeadset));

    public bool IsInHeadset
    {
        get => GetValue(IsInHeadsetProperty);
        set => SetValue(IsInHeadsetProperty, value);
    }

    public VrCalibrationView() => InitializeComponent();
}
