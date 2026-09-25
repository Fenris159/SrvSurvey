using System.Collections;
using System.Windows.Input;
using Avalonia;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningStationList : Avalonia.Controls.UserControl
{
    public static readonly StyledProperty<IEnumerable?> StationsProperty = AvaloniaProperty.Register<
        MiningStationList,
        IEnumerable?
    >(nameof(Stations));

    public static readonly StyledProperty<ICommand?> ToggleCommandProperty = AvaloniaProperty.Register<
        MiningStationList,
        ICommand?
    >(nameof(ToggleCommand));

    public static readonly StyledProperty<string> ToggleLabelProperty = AvaloniaProperty.Register<
        MiningStationList,
        string
    >(nameof(ToggleLabel), "Show all stations");

    public MiningStationList() => InitializeComponent();

    public IEnumerable? Stations
    {
        get => GetValue(StationsProperty);
        set => SetValue(StationsProperty, value);
    }

    public ICommand? ToggleCommand
    {
        get => GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public string ToggleLabel
    {
        get => GetValue(ToggleLabelProperty);
        set => SetValue(ToggleLabelProperty, value);
    }
}
