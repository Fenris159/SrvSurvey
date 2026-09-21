using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningChipBox : UserControl
{
    public MiningChipBox() => InitializeComponent();

    private MiningChipBoxViewModel? Model => DataContext as MiningChipBoxViewModel;

    private void Query_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model)
        {
            model.Open = true;
        }
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is Button { Tag: string value })
        {
            model.Add(value);
        }
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is Button { Tag: string value })
        {
            model.Remove(value);
        }
    }
}
