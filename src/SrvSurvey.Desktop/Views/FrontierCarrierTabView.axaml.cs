using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class FrontierCarrierTabView : UserControl
{
    public static readonly Avalonia.StyledProperty<Control?> SupplementaryContentProperty =
        Avalonia.AvaloniaProperty.Register<FrontierCarrierTabView, Control?>(nameof(SupplementaryContent));
    public Control? SupplementaryContent
    {
        get => GetValue(SupplementaryContentProperty);
        set => SetValue(SupplementaryContentProperty, value);
    }

    public FrontierCarrierTabView()
    {
        InitializeComponent();
    }
}
