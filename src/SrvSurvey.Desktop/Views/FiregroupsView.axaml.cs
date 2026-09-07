using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class FiregroupsView : UserControl
{
    private bool confirmingDeletion;
    public FiregroupsView() => InitializeComponent();

    private async void DeleteConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (confirmingDeletion || DataContext is not MainWindowViewModel model
            || TopLevel.GetTopLevel(this) is not Window owner) return;
        var row = (sender as Control)?.DataContext as FiregroupSavedRow ?? model.Firegroups.SelectedSavedProfile;
        if (row is null) { model.Firegroups.RemoveCommand.Execute(null); return; }
        confirmingDeletion = true;
        try
        {
            var dialog = new DeleteFiregroupDialog(row.Name, row.ShipName);
            if (await dialog.ShowDialog<bool>(owner)) row.DeleteCommand.Execute(null);
        }
        finally { confirmingDeletion = false; }
    }
}
