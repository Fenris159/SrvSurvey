using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningMissionCardsView : UserControl
{
    public MiningMissionCardsView()
    {
        InitializeComponent();
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
