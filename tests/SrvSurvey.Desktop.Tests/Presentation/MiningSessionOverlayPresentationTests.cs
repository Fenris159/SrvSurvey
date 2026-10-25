using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningSessionOverlayPresentationTests
{
    [AvaloniaFact]
    public void SessionPreviewUsesTheLivePresentationAndMiningCategory()
    {
        OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired("PlotMiningSession");
        Assert.Equal(OverlayLayoutCategory.Mining, definition.Category);
        using var previewModel = new MiningSessionOverlayViewModel(null);
        Assert.True(previewModel.HasSession);
        Assert.Equal("Active", previewModel.State);
        var preview = new OverlayPositionPreviewWindow(definition);
        try
        {
            preview.Show();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.IsType<SrvSurvey.Desktop.MiningSessionOverlayPresentation>(preview.RuntimePresentation);
            Assert.Contains(preview.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "SESSION");
            Assert.Contains(
                preview.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text != null && text.Text.Contains("64 t", StringComparison.Ordinal)
            );
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-mining-session-preview.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            preview.Close();
        }
        Assert.All(
            OverlayLayoutCatalog.Supported.Where(panel =>
                panel.SettingsCategories.Contains(OverlaySettingsCategory.MineMap)
            ),
            panel => Assert.Equal(OverlayLayoutCategory.SurfaceMining, panel.Category)
        );
        Assert.Equal(
            "Mining (surface)",
            OverlayLayoutCatalog
                .Categories.Single(category => category.Category == OverlayLayoutCategory.SurfaceMining)
                .DisplayName
        );
    }
}
