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
            panel.ItemWidth = e.NewSize.Width >= 760 ? Math.Floor(e.NewSize.Width / 2) : e.NewSize.Width;
        }
    }
}
