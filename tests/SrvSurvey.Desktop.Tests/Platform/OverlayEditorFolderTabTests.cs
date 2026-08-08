using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayEditorFolderTabTests
{
    [AvaloniaFact]
    public void PreviewRendersAVisibleFolderTabAttachedAboveTheBody()
    {
        var definition = OverlayLayoutCatalog.GetRequired("PlotFSSInfo");
        var preview = new OverlayPositionPreviewWindow(definition);
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.Show();

            Assert.Equal(
                new Thickness(12, 4, 12, 5),
                preview.EditorFolderTabControl.Padding);
            Assert.Equal(
                new Thickness(2, 2, 2, 0),
                preview.EditorFolderTabControl.BorderThickness);
            Assert.Equal(
                new CornerRadius(7, 7, 0, 0),
                preview.EditorFolderTabControl.CornerRadius);

            Assert.True(preview.EditorFolderTabControl.IsVisible);
            Assert.Equal(
                definition.DisplayName,
                preview.EditorFolderTabLabelControl.Text);
            Assert.True(preview.EditorFolderTabControl.MinHeight >= 24);
            Assert.True(preview.EditorFolderTabControl.Bounds.Width >= 72);
            Assert.True(preview.EditorFolderTabControl.Bounds.Height >= 24);
            Assert.Equal(0, preview.EditorFolderTabControl.Bounds.Top);
            Assert.True(
                preview.PreviewBodyControl.Bounds.Top
                    >= preview.EditorFolderTabControl.Bounds.Bottom - 2);

            AssertFolderTabBrush(preview.EditorFolderTabControl.Background);
            AssertFolderTabBrush(preview.EditorFolderTabControl.BorderBrush);

            var frame = preview.CaptureRenderedFrame();
            Assert.NotNull(frame);
        }
        finally
        {
            preview.Close();
        }
    }

    private static void AssertFolderTabBrush(IBrush? candidate)
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(candidate);
        Assert.Equal(Color.Parse("#FFCC33"), brush.Color);
    }
}
