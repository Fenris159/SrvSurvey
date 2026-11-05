using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningMissionCardsView : UserControl
{
    public MiningMissionCardsView()
    {
        InitializeComponent();
    }

    private static void Summary_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid summary || e.NewSize.Width <= 0)
        {
            return;
        }
        bool compact = e.NewSize.Width < 640;
        if (summary.ColumnDefinitions.Count == (compact ? 2 : 3))
        {
            return;
        }
        summary.ColumnDefinitions = new ColumnDefinitions(compact ? "*,Auto" : "2*,4*,Auto");
        summary.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        // The template orders its children as location, mission metadata, then commodity totals.
        Grid.SetColumn(summary.Children[1], compact ? 1 : 2);
        Grid.SetColumn(summary.Children[2], compact ? 0 : 1);
        Grid.SetRow(summary.Children[2], compact ? 1 : 0);
        Grid.SetColumnSpan(summary.Children[2], compact ? 2 : 1);
    }

    private static void Cards_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is WrapPanel panel && e.NewSize.Width > 0)
        {
            int columns = Math.Clamp((int)(e.NewSize.Width / 240), 1, 6);
            panel.ItemWidth = Math.Floor(e.NewSize.Width / columns);
        }
    }
}
