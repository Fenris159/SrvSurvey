using System.Collections;
using Avalonia;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningStationList : Avalonia.Controls.UserControl
{
    public static readonly StyledProperty<IEnumerable?> StationsProperty = AvaloniaProperty.Register<
        MiningStationList,
        IEnumerable?
    >(nameof(Stations));

    public MiningStationList() => InitializeComponent();

    public IEnumerable? Stations
    {
        get => GetValue(StationsProperty);
        set => SetValue(StationsProperty, value);
    }
}
