using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed partial class SpanshSystemListEntry : UserControl
{
    /// <summary>
    /// Initializes the shared destination autocomplete and ordered-list control.
    /// </summary>
    public SpanshSystemListEntry() => InitializeComponent();

    /// <summary>
    /// Returns keyboard focus to the reusable destination entry after a row action.
    /// </summary>
    private void FocusEntry_Click(object? sender, RoutedEventArgs e) =>
        DestinationEntry.FindControl<TextBox>("InputBox")!.Focus();

    /// <summary>
    /// Commits a pending destination when Enter is pressed in the system entry.
    /// </summary>
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
