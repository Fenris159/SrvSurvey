using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class SurfaceMiningSplitResults : UserControl
{
    public SurfaceMiningSplitResults() => InitializeComponent();

    internal StackPanel ResultsTableControl => ResultsTable;
}
