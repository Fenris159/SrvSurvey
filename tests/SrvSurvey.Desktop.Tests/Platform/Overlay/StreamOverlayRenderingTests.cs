using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class StreamOverlayRenderingTests
{
    [AvaloniaFact]
    public void CaptureWindowDrawsPanelPixelsAndLeavesTheRestTransparent()
    {
        var panel = new Border
        {
            Width = 20,
            Height = 15,
            Background = Brushes.Red,
        };
        panel.Measure(new Size(20, 15));
        panel.Arrange(new Rect(0, 0, 20, 15));
        var bitmap = new RenderTargetBitmap(new PixelSize(20, 15));
        bitmap.Render(panel);
        var window = new StreamOverlayWindow { Width = 120, Height = 80 };
        try
        {
            window.ReplaceFrames([new(bitmap, new StreamOverlayFrame(10, 10, 20, 15))]);
            window.Show();
            using WriteableBitmap? rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            VrOverlayFrame frame = VrOverlayFrameRenderer.Render(window);
            int panelPixel = ((15 * frame.Width) + 15) * 4;
            Assert.Equal(255, frame.RgbaBytes[panelPixel]);
            Assert.Equal(255, frame.RgbaBytes[panelPixel + 3]);
            Assert.Equal(0, frame.RgbaBytes[3]);
        }
        finally
        {
            window.Close();
        }
    }
}
