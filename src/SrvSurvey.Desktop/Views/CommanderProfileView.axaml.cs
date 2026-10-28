using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class CommanderProfileView : UserControl
{
    private bool? compactHeader;

    public CommanderProfileView()
    {
        InitializeComponent();
    }

    private void Console_SizeChanged(object? sender, SizeChangedEventArgs args)
    {
        bool compact = args.NewSize.Width < 860;
        if (compactHeader == compact)
        {
            return;
        }
        compactHeader = compact;
        ConsoleHeader.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 300);
        ConsoleHeader.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto" : "Auto,Auto");
        Grid.SetColumn(CommanderAccountSelection, compact ? 0 : 1);
        Grid.SetColumnSpan(CommanderAccountSelection, compact ? 3 : 1);
        Grid.SetRow(CommanderAccountSelection, compact ? 1 : 0);
        Grid.SetRow(CommanderFeedback, compact ? 2 : 1);
    }
}
