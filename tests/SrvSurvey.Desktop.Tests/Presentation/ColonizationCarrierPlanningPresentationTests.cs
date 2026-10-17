using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaFact]
    public async Task PlanningCarrierSelectorUpdatesLoadedColumnAndManifestDefault()
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                LinkedCarriers =
                [
                    new()
                    {
                        MarketId = 43,
                        Name = "BBB-222",
                        DisplayName = "Team Carrier",
                        Owner = "Teammate",
                        Cargo = new() { ["steel"] = 25 },
                    },
                ],
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        main.Colonization.SetCommanderProfile("F123", true, "test-key");
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 800,
        };
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ComboBox selector = view.FindControl<ComboBox>("PlanningCarrierSelector")!;
            Assert.Equal("ALL", Assert.IsType<ColonizationCarrierChoice>(selector.SelectedItem).Label);
            Assert.Equal(2, selector.ItemCount);
            selector.SelectedItem = main.Colonization.PlanningCarrierOptions[1];
            Assert.Equal(43, main.Colonization.SelectedPlanningCarrier.MarketId);
            Assert.Equal("23%", main.Colonization.Projects[0].CarrierCoverageText);
            TextBlock label = Assert.Single(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "LOADED"
            );
            Assert.Equal(2, Grid.GetColumn(label));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "23%");
            using ColonizationCarrierManifestViewModel editor = main.Colonization.CreateCarrierManifest();
            await editor.InitializeAsync();
            Assert.Equal(43, editor.SelectedCarrier!.MarketId);
            selector.BringIntoView();
            using WriteableBitmap? preview = window.CaptureRenderedFrame();
            Assert.NotNull(preview);
            preview.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-carrier-planning-preview.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }
}
