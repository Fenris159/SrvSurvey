using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace SrvSurvey.Desktop.Views;

public sealed partial class FrontierCommanderTabView : UserControl
{
    public FrontierCommanderTabView()
    {
        InitializeComponent();
    }

    private void Readout_SizeChanged(object? sender, SizeChangedEventArgs args)
    {
        bool compact = args.NewSize.Width < 820;
        Grid.SetRow(FlightShip, compact ? 1 : 0);
        Grid.SetColumn(FlightShip, compact ? 1 : 2);
        Grid.SetColumnSpan(FlightShip, compact ? 2 : 1);
        Grid.SetColumnSpan(FlightLocation, compact ? 2 : 1);
        Grid.SetRow(CommanderFleet, compact ? 1 : 0);
        Grid.SetColumn(CommanderFleet, compact ? 0 : 1);
        Grid.SetColumnSpan(CommanderFleet, compact ? 2 : 1);
        Grid.SetColumnSpan(CommanderReputation, compact ? 2 : 1);
    }

    private void Ranks_SizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (CommanderRanks.ItemsPanelRoot is UniformGrid panel)
        {
            panel.Columns = Math.Max(1, (int)(args.NewSize.Width / 240));
        }
    }
}
