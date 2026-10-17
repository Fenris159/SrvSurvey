using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

/// <summary>Shows the commander's saved station and commodity choices independently of the market search window.</summary>
public sealed partial class ColonizationWishListWindow : Window
{
    public ColonizationWishListWindow()
        : this(new ColonizationWishListViewModel()) { }

    public ColonizationWishListWindow(ColonizationWishListViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
