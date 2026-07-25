using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class QuestIndicatorOverlayWindow : Window
{
    public QuestIndicatorOverlayWindow()
        : this(new QuestIndicatorViewModel())
    {
    }

    public QuestIndicatorOverlayWindow(QuestIndicatorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
