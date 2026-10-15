using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed partial class SpanshSystemListEntry : UserControl
{
    public SpanshSystemListEntry() => InitializeComponent();

    private void FocusEntry_Click(object? sender, RoutedEventArgs e) =>
        DestinationEntry.FindControl<TextBox>("InputBox")!.Focus();

    private void Entry_KeyDown(object? sender, KeyEventArgs e)
    {
        if (
            e.Key == Key.Enter
            && DestinationEntry.IsKeyboardFocusWithin
            && DataContext is SpanshSystemListViewModel model
        )
        {
            model.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }
}
