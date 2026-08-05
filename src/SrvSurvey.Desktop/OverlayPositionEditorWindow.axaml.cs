using Avalonia.Controls;
using Avalonia.Input;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionEditorWindow : Window
{
    public OverlayPositionEditorWindow()
    {
        InitializeComponent();
    }

    public OverlayPositionEditorWindow(OverlayInteractionViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is OverlayInteractionViewModel viewModel)
        {
            viewModel.Cancel();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
