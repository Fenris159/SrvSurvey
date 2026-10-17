using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class ColonizationWishListOverlayWindow : Window
{
    public ColonizationWishListOverlayWindow()
        : this(new ColonizationWishListViewModel()) { }

    public ColonizationWishListOverlayWindow(ColonizationWishListViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
