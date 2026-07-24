using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NavigationSeparatesImplementedAndPendingSurfaces()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(9, viewModel.NavigationItems.Count);
        Assert.Equal(3, viewModel.NavigationItems.Count(item => item.IsImplemented));
        Assert.True(viewModel.IsOverviewSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "exploration");

        Assert.True(viewModel.IsPendingSelected);
        Assert.Equal("Exploration", viewModel.PendingPageTitle);
    }

    [Fact]
    public void ThemeGalleryContainsEveryRavenTheme()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(5, viewModel.ThemeOptions.Count);
        Assert.Equal("Blue (dark)", viewModel.SelectedThemeName);
    }
}
