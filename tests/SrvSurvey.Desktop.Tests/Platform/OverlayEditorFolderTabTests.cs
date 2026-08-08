using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayEditorFolderTabTests
{
    [AvaloniaFact]
    public void PreviewHostsAVisibleFolderTabWithDisplayName()
    {
        var definition = OverlayLayoutCatalog.GetRequired("PlotFSSInfo");
        var preview = new OverlayPositionPreviewWindow(definition);
        try
        {
            Assert.True(preview.EditorFolderTabControl.IsVisible);
            Assert.Equal(
                definition.DisplayName,
                preview.EditorFolderTabLabelControl.Text);
            Assert.True(preview.EditorFolderTabControl.MinHeight >= 24);
        }
        finally
        {
            preview.Close();
        }
    }
}
