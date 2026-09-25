using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class AcquireSplitRowPresentationTests
{
    [AvaloniaFact]
    public void SurfaceResultLineRendersInsideSharedTreeRow()
    {
        var row = new AcquireSplitRow
        {
            DataContext = "Sell System",
            Lines = new[]
            {
                new SurfaceMiningSystemRowViewModel(
                    "Mining System",
                    4.2,
                    [new SurfaceBodyLine(["DIA"], "1: Rocky body")]
                ),
            },
            TargetTemplate = new FuncDataTemplate<string>((target, _) => new TextBlock { Text = target }),
            LineTemplate = new FuncDataTemplate<SurfaceMiningSystemRowViewModel>(
                (line, _) => new TextBlock { Text = line?.System }
            ),
        };
        var window = new Window
        {
            Content = row,
            Width = 900,
            Height = 300,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Mining System");
        }
        finally
        {
            window.Close();
        }
    }
}
