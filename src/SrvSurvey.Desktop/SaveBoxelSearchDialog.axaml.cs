using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop;

public sealed partial class SaveBoxelSearchDialog : Window
{
    public SaveBoxelSearchDialog()
    {
        InitializeComponent();
    }

    public SaveBoxelSearchDialog(string suggestedName) : this()
    {
        SearchNameBox.Text = suggestedName;
        Opened += (_, _) =>
        {
            SearchNameBox.Focus();
            SearchNameBox.SelectAll();
        };
    }

    private void Confirm_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var name = SearchNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationText.IsVisible = true;
            SearchNameBox.Focus();
            return;
        }

        Close(new BoxelSearchSaveDialogResult(name, NotesBox.Text));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close(null);
    }
}

public sealed record BoxelSearchSaveDialogResult(string Name, string? Notes);
