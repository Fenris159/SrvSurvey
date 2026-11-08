using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    /// <summary>Building and Loading expose the same carrier actions, with checked-row guards and usable wrapping.</summary>
    [AvaloniaTheory]
    [InlineData(820, "Building")]
    [InlineData(1180, "Building")]
    [InlineData(820, "Loading")]
    [InlineData(1180, "Loading")]
    public async Task ProjectCarrierActionsFollowCarrierAndShowSelectionAndWrapWithinTheCard(int width, string status)
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                ActiveProjects =
                [
                    .. ColonizationShoppingViewModelTests.Projects(),
                    new()
                    {
                        BuildId = "loading-project",
                        BuildName = "Carrier loading",
                        BuildType = ColonizationProject.FleetCarrierLoadingBuildType,
                        SystemName = "Sol",
                        MaximumRequired = 10_080,
                        RemainingRequired = 10_080,
                        Commodities = new() { ["steel"] = 10_080 },
                    },
                ],
                LinkedCarriers =
                [
                    new()
                    {
                        MarketId = 42,
                        Name = "N4W-T0Z",
                        DisplayName = "DIRE ECLIPSE",
                    },
                ],
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 900,
        };
        try
        {
            window.Show();
            main.Colonization.SelectedPlanningCarrierMarketId = 0;
            Button link = view.FindControl<Button>("LinkCarrierToSelectedProjectsButton")!;
            Button unlink = view.FindControl<Button>("UnlinkCarrierFromSelectedProjectsButton")!;
            Assert.False(link.IsVisible);
            Assert.False(unlink.IsVisible);
            main.Colonization.ProjectBuildFilter = Assert.Single(
                main.Colonization.ProjectBuildOptions,
                option => option.Label == status
            );
            Assert.True(link.IsVisible);
            Assert.True(unlink.IsVisible);
            Assert.True(link.IsEffectivelyEnabled);
            Assert.True(unlink.IsEffectivelyEnabled);
            CarrierMultiSelect carriers = view.FindControl<CarrierMultiSelect>("PlanningCarrierSelector")!;
            SetCarrierChecks(carriers, 42);
            Assert.True(link.IsEffectivelyEnabled);
            Assert.True(unlink.IsEffectivelyEnabled);
            link.BringIntoView();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-project-carrier-actions-{status}-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            WrapPanel panel = Assert.IsType<WrapPanel>(link.Parent);
            Assert.True(link.Bounds.Top >= carriers.Bounds.Top - 1);
            Assert.True(unlink.Bounds.Center.Y >= link.Bounds.Center.Y - 1);
            Assert.True(link.Bounds.Right <= panel.Bounds.Width + 1);
            Assert.True(unlink.Bounds.Right <= panel.Bounds.Width + 1);
            Assert.True(link.Bounds.Width >= link.DesiredSize.Width - link.Margin.Left - link.Margin.Right);
            Assert.True(unlink.Bounds.Width >= unlink.DesiredSize.Width - unlink.Margin.Left - unlink.Margin.Right);

            main.Colonization.UncheckProjectSelection();
            Assert.False(link.IsEffectivelyEnabled);
            Assert.False(unlink.IsEffectivelyEnabled);
            main.Colonization.VisibleProjects[0].IsShown = true;
            Assert.True(link.IsEffectivelyEnabled);
            main.Colonization.ProjectBuildFilter = main.Colonization.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Planning
            );
            Assert.False(link.IsVisible);
            Assert.False(unlink.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
