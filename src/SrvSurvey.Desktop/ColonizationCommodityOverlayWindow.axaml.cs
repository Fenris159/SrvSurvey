using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class ColonizationCommodityOverlayWindow : Window
{
    public ColonizationCommodityOverlayWindow()
        : this(new ColonizationCommodityOverlayViewModel())
    {
    }

    public ColonizationCommodityOverlayWindow(
        ColonizationCommodityOverlayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
