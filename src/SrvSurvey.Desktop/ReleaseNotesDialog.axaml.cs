using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class ReleaseNotesDialog : Window
{
    public ReleaseNotesDialog()
    {
        InitializeComponent();
    }

    public ReleaseNotesDialog(
        string fallbackTitle,
        string releaseNotes) : this()
    {
        DataContext = ReleaseNotesDialogViewModel.Create(
            fallbackTitle,
            releaseNotes);
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
