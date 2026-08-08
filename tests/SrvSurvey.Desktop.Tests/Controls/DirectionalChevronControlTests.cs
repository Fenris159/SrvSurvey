using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DirectionalChevronControlTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearAndFarChevronsRenderToTheHeadlessSurface(bool isFar)
    {
        var control = new DirectionalChevronControl
        {
            Width = 24,
            Height = 24,
            BearingDegrees = 35,
            IsFar = isFar,
            Stroke = Brushes.Orange,
            StrokeThickness = 2,
        };
        var window = new Window
        {
            Width = 32,
            Height = 32,
            Content = control,
        };

        try
        {
            window.Show();
            var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(32, 32), frame.PixelSize);
        }
        finally
        {
            window.Close();
        }
    }
}
